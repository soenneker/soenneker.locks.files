using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Locks.Files.Tests;

public sealed class FileLockInspectorTests
{
    [Test]
    public async Task Reports_separate_owner_and_no_owner_after_release()
    {
        using var scope = new LockTestScope();
        using var worker = scope.StartWorker();
        try
        {
            await Assert.That(await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20))).IsEqualTo("acquired");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var inspector = new FileLockInspector();
            IReadOnlyList<FileLockProcess> owners = await inspector.GetLockingProcesses(scope.Path, cancellation.Token);
            await Assert.That(owners.Any(p => p.ProcessId == worker.Id)).IsTrue();
            if (OperatingSystem.IsLinux())
                await Assert.That(owners.Single(p => p.ProcessId == worker.Id).HasConfirmedLock).IsTrue();
            if (OperatingSystem.IsWindows())
                await Assert.That(owners.Single(p => p.ProcessId == worker.Id).HasConfirmedLock).IsFalse();
            await Assert.That(new FileLock(scope.Path).TryAcquire()).IsNull();
            await worker.StandardInput.WriteLineAsync("release");
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That((await inspector.GetLockingProcesses(scope.Path, cancellation.Token)).Count).IsEqualTo(0);
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill();
                await worker.WaitForExitAsync();
            }
        }
    }

    [Test]
    public async Task Handles_unicode_and_spaces()
    {
        using var scope = new LockTestScope();
        string path = Path.Combine(scope.Directory, "café 日本語 lock");
        using FileLockLease lease = await new FileLock(path).Acquire();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        IReadOnlyList<FileLockProcess> owners = await new FileLockInspector().GetLockingProcesses(path, cancellation.Token);
        await Assert.That(owners.Any(p => p.ProcessId == Environment.ProcessId)).IsTrue();
    }

    [Test]
    public async Task Missing_file_is_not_created()
    {
        using var scope = new LockTestScope();
        await Assert.That(async () => { await new FileLockInspector().GetLockingProcesses(scope.Path); })
            .Throws<FileNotFoundException>();
        await Assert.That(File.Exists(scope.Path)).IsFalse();
    }

    [Test]
    public async Task Pre_canceled_inspection_does_not_start()
    {
        using var scope = new LockTestScope();
        await Assert.That(async () => { await new FileLockInspector().GetLockingProcesses(scope.Path, new CancellationToken(true)); })
            .Throws<OperationCanceledException>();
    }
}

