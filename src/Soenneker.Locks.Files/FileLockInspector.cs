using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Locks.Files.Abstract;

namespace Soenneker.Locks.Files;

public sealed class FileLockInspector : IFileLockInspector
{
    public Task<IReadOnlyList<FileLockProcess>> GetLockingProcesses(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.Directory) != 0)
            throw new ArgumentException("The path must identify a file, not a directory.", nameof(path));

        if (OperatingSystem.IsWindows())
            return Task.Run(() => WindowsFileLockInspector.GetProcesses(fullPath, cancellationToken), cancellationToken);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return UnixFileLockInspector.GetProcesses(fullPath, cancellationToken);
        throw new PlatformNotSupportedException("File lock inspection requires Windows, Linux, or macOS.");
    }
}
