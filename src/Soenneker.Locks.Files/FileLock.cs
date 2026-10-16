using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Soenneker.Extensions.ValueTask;
using Soenneker.Locks.Files.Abstract;
using Soenneker.Utils.Runtime;

namespace Soenneker.Locks.Files;

public sealed class FileLock : IFileLock
{
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(50);
    private readonly string _path;

    public FileLock(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public ValueTask<FileLockLease> Acquire(CancellationToken cancellationToken = default) =>
        Acquire(Timeout.InfiniteTimeSpan, cancellationToken);

    public ValueTask<FileLockLease> Acquire(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        TimeSpan wait = timeout;
        if (wait < TimeSpan.Zero && wait != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        cancellationToken.ThrowIfCancellationRequested();
        long started = wait == Timeout.InfiniteTimeSpan ? 0 : Stopwatch.GetTimestamp();
        FileLockLease? lease = TryAcquireCore(_path);

        if (lease is not null)
            return new ValueTask<FileLockLease>(lease);

        return AcquireContended(_path, wait, started, cancellationToken);
    }

    private static async ValueTask<FileLockLease> AcquireContended(string path, TimeSpan wait, long started,
        CancellationToken cancellationToken)
    {
        if (wait != Timeout.InfiniteTimeSpan && Stopwatch.GetElapsedTime(started) >= wait)
            throw new TimeoutException($"Timed out acquiring file lock '{path}'.");

        // Reuse one timer and its ValueTask source instead of allocating a Task.Delay on each retry.
        using var timer = new PeriodicTimer(_retryDelay);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan delay = _retryDelay;
            if (wait != Timeout.InfiniteTimeSpan)
            {
                TimeSpan remaining = wait - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException($"Timed out acquiring file lock '{path}'.");
                if (remaining < delay)
                    delay = remaining;
            }

            // PeriodicTimer requires at least one millisecond.
            if (delay < TimeSpan.FromMilliseconds(1))
                delay = TimeSpan.FromMilliseconds(1);
            if (timer.Period != delay)
                timer.Period = delay;
            await timer.WaitForNextTickAsync(cancellationToken).NoSync();
            cancellationToken.ThrowIfCancellationRequested();
            if (wait != Timeout.InfiniteTimeSpan && Stopwatch.GetElapsedTime(started) >= wait)
                throw new TimeoutException($"Timed out acquiring file lock '{path}'.");

            FileLockLease? lease = TryAcquireCore(path);
            if (lease is not null)
                return lease;
        }
    }

    public FileLockLease? TryAcquire() => TryAcquireCore(_path);

    private static FileLockLease? TryAcquireCore(string path)
    {
        if (RuntimeUtil.IsWindows())
        {
            // GENERIC_READ | GENERIC_WRITE, no sharing, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL.
            // Return a raw handle so failed attempts allocate neither exceptions nor SafeHandles.
            nint handle = WindowsFileLock.Open(path, 0xC0000000, 0, 0, 4, 0x80, 0);
            if (handle != -1)
                return new FileLockLease(new SafeFileHandle(handle, ownsHandle: true));

            int error = Marshal.GetLastPInvokeError();
            if (error == 32) // ERROR_SHARING_VIOLATION
                return null;
            throw CreateOpenException(path, error, windows: true);
        }

        bool mac = RuntimeUtil.IsMacOs();
        if (!mac && !RuntimeUtil.IsLinux())
            throw new PlatformNotSupportedException("File locks require Windows, Linux, or macOS.");

        // O_RDWR | O_CLOEXEC. Existing files avoid File.OpenHandle's implicit shared flock.
        // Use .NET for first creation, including the platform-specific variadic open ABI on macOS ARM64.
        int flags = mac ? 2 | 0x1000000 : 2 | 0x80000;
        SafeFileHandle? createdHandle = null;
        int descriptor;
        int openError;
        do
        {
            descriptor = UnixFileLock.Open(path, flags);
            openError = descriptor < 0 ? Marshal.GetLastPInvokeError() : 0;
        }
        while (openError == 4); // EINTR

        if (descriptor < 0 && openError == 2) // ENOENT: first creation (or missing parent).
        {
            try
            {
                createdHandle = File.OpenHandle(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                descriptor = (int)createdHandle.DangerousGetHandle();
            }
            catch (IOException ex) when (ex.HResult == (mac ? 35 : 11))
            {
                // A competing creator already holds the exclusive lock.
                return null;
            }
        }
        else if (descriptor < 0)
            throw CreateOpenException(path, openError, windows: false);

        var acquired = false;
        try
        {
            int result;
            int error;
            do
            {
                result = UnixFileLock.Flock(descriptor, 2 | 4); // LOCK_EX | LOCK_NB
                error = result == 0 ? 0 : Marshal.GetLastPInvokeError();
            }
            while (error == 4);

            if (result != 0)
            {
                if (error == (mac ? 35 : 11)) // EWOULDBLOCK
                    return null;
                throw new IOException($"Unable to lock '{path}'.", new Win32Exception(error));
            }

            var lease = new FileLockLease(createdHandle ?? new SafeFileHandle(descriptor, ownsHandle: true));
            acquired = true;
            return lease;
        }
        finally
        {
            if (!acquired)
            {
                if (createdHandle is not null)
                    createdHandle.Dispose();
                else
                    UnixFileLock.Close(descriptor);
            }

            GC.KeepAlive(createdHandle);
        }
    }

    private static Exception CreateOpenException(string path, int error, bool windows)
    {
        var inner = new Win32Exception(error);
        if (windows ? error is 2 or 3 : error is 2 or 20)
            return new DirectoryNotFoundException($"The lock file's directory does not exist: '{path}'.", inner);
        if (windows ? error == 5 : error is 1 or 13 or 21)
            return new UnauthorizedAccessException($"Cannot open lock file '{path}'.", inner);
        if (windows ? error == 206 : error == (RuntimeUtil.IsMacOs() ? 63 : 36))
            return new PathTooLongException($"Lock file path is too long: '{path}'.", inner);
        return new IOException($"Cannot open lock file '{path}'.", inner);
    }
}