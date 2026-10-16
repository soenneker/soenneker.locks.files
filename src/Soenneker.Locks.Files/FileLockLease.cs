using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Soenneker.Locks.Files;

/// <summary>Owns an exclusive file lock until disposed. Disposal is thread-safe and idempotent.</summary>
public sealed class FileLockLease : IDisposable, IAsyncDisposable
{
    private SafeFileHandle? _handle;

    internal FileLockLease(SafeFileHandle handle) => _handle = handle;

    /// <summary>Releases the lock without deleting its file.</summary>
    public void Dispose() => Interlocked.Exchange(ref _handle, null)?.Dispose();

    /// <summary>Releases the lock without deleting its file.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
