using System;
using System.Diagnostics;
using System.IO;
using Soenneker.Utils.Runtime;

namespace Soenneker.Locks.Files.Tests;

internal sealed class LockTestScope : IDisposable
{
    internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "file-lock-tests-" + Guid.NewGuid().ToString("N"));
    internal string Path => System.IO.Path.Combine(Directory, "test.lock");

    internal LockTestScope() => System.IO.Directory.CreateDirectory(Directory);

    internal Process StartWorker()
    {
        string executable = System.IO.Path.Combine(AppContext.BaseDirectory, "worker",
            "Soenneker.Locks.Files.TestWorker" + (RuntimeUtil.IsWindows() ? ".exe" : ""));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Path);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start lock worker.");
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
