using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Locks.Files.Tests;

public sealed class FileLockTests
{
    [Test]
    public async Task Bound_lock_is_reusable_with_using_await()
    {
        using var scope = new LockTestScope();
        Soenneker.Locks.Files.Abstract.IFileLock fileLock = new FileLock(scope.Path);
        await Assert.That(File.Exists(scope.Path)).IsFalse();
        using (await fileLock.Acquire(CancellationToken.None))
            await Assert.That(fileLock.TryAcquire()).IsNull();
        using (await fileLock.Acquire(TimeSpan.Zero))
            await Assert.That(new FileLock(scope.Path).TryAcquire()).IsNull();
        using FileLockLease? lease = fileLock.TryAcquire();
        await Assert.That(lease).IsNotNull();
    }

    [Test]
    public async Task Using_await_releases_after_protected_work_throws()
    {
        using var scope = new LockTestScope();
        var fileLock = new FileLock(scope.Path);
        await Assert.That(async () =>
        {
            using (await fileLock.Acquire())
                throw new InvalidOperationException("Protected operation failed.");
        }).Throws<InvalidOperationException>();
        using FileLockLease lease = await fileLock.Acquire(TimeSpan.Zero);
    }

    [Test]
    public async Task Constructor_rejects_empty_path()
    {
        await Assert.That(() => new FileLock(" ")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Contention_across_multiple_retries_can_be_canceled()
    {
        using var scope = new LockTestScope();
        using FileLockLease lease = await new FileLock(scope.Path).Acquire();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(225));
        await Assert.That(async () => { using var unexpected = await new FileLock(scope.Path).Acquire(cancellationToken: cancellation.Token); })
            .Throws<OperationCanceledException>();
        await Assert.That(new FileLock(scope.Path).TryAcquire()).IsNull();
    }

    [Test]
    public async Task Unicode_paths_are_marshaled_without_changing_the_lock_identity()
    {
        using var scope = new LockTestScope();
        string path = Path.Combine(scope.Directory, "lock café 日本語 🔒.lock");
        using (FileLockLease lease = await new FileLock(path).Acquire())
            await Assert.That(new FileLock(path).TryAcquire()).IsNull();
        using FileLockLease next = await new FileLock(path).Acquire(TimeSpan.Zero);
        await Assert.That(File.Exists(path)).IsTrue();
    }

    [Test]
    public async Task Existing_file_contents_are_preserved()
    {
        using var scope = new LockTestScope();
        await File.WriteAllTextAsync(scope.Path, "keep this");
        using (FileLockLease lease = await new FileLock(scope.Path).Acquire()) { }
        await Assert.That(await File.ReadAllTextAsync(scope.Path)).IsEqualTo("keep this");
    }

    [Test]
    public async Task Directory_path_error_is_not_retried()
    {
        using var scope = new LockTestScope();
        await Assert.That(async () => { using var unexpected = await new FileLock(scope.Directory).Acquire(); })
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task Independent_instances_contend_and_disposal_preserves_file()
    {
        using var scope = new LockTestScope();
        FileLockLease first = await new FileLock(scope.Path).Acquire();
        try
        {
            await Assert.That(new FileLock(scope.Path).TryAcquire()).IsNull();
        }
        finally { first.Dispose(); }
        await Assert.That(File.Exists(scope.Path)).IsTrue();
        using FileLockLease second = await new FileLock(scope.Path).Acquire(TimeSpan.Zero);
        first.Dispose();
        await first.DisposeAsync();
        await Assert.That(new FileLock(scope.Path).TryAcquire()).IsNull();
    }

    [Test]
    public async Task Waiting_acquisition_succeeds_after_release()
    {
        using var scope = new LockTestScope();
        using FileLockLease first = await new FileLock(scope.Path).Acquire();
        Task<FileLockLease> pending = new FileLock(scope.Path).Acquire(TimeSpan.FromSeconds(5)).AsTask();
        await Assert.That(pending.IsCompleted).IsFalse();
        await first.DisposeAsync();
        using FileLockLease second = await pending.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    [Arguments(0)]
    [Arguments(100)]
    public async Task Contention_times_out(int milliseconds)
    {
        using var scope = new LockTestScope();
        using FileLockLease lease = await new FileLock(scope.Path).Acquire();
        await Assert.That(async () => { using var unexpected = await new FileLock(scope.Path).Acquire(TimeSpan.FromMilliseconds(milliseconds)); })
            .Throws<TimeoutException>();
    }

    [Test]
    public async Task Waiting_can_be_canceled_and_does_not_release_owner()
    {
        using var scope = new LockTestScope();
        using FileLockLease lease = await new FileLock(scope.Path).Acquire();
        using var cancellation = new CancellationTokenSource();
        Task<FileLockLease> waiting = new FileLock(scope.Path).Acquire(cancellationToken: cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.That(async () => { using var unexpected = await waiting; }).Throws<OperationCanceledException>();
        await Assert.That(new FileLock(scope.Path).TryAcquire()).IsNull();
    }

    [Test]
    public async Task Pre_canceled_acquisition_does_not_create_file()
    {
        using var scope = new LockTestScope();
        await Assert.That(async () => { using var unexpected = await new FileLock(scope.Path).Acquire(cancellationToken: new CancellationToken(true)); })
            .Throws<OperationCanceledException>();
        await Assert.That(File.Exists(scope.Path)).IsFalse();
    }

    [Test]
    public async Task Missing_directory_is_not_treated_as_contention()
    {
        using var scope = new LockTestScope();
        string missing = System.IO.Path.Combine(scope.Directory, "missing", "lock");
        await Assert.That(() => new FileLock(missing).TryAcquire()).Throws<DirectoryNotFoundException>();
        await Assert.That(async () => { using var unexpected = await new FileLock(missing).Acquire(); }).Throws<DirectoryNotFoundException>();
    }

    [Test]
    public async Task Invalid_timeout_is_rejected()
    {
        using var scope = new LockTestScope();
        await Assert.That(async () => { using var unexpected = await new FileLock(scope.Path).Acquire(TimeSpan.FromMilliseconds(-2)); })
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Different_paths_can_be_held_together()
    {
        using var scope = new LockTestScope();
        using FileLockLease first = await new FileLock(scope.Path).Acquire();
        using FileLockLease second = await new FileLock(scope.Path + ".other").Acquire(TimeSpan.Zero);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Separate_process_excludes_others_and_releases_on_exit(bool kill)
    {
        using var scope = new LockTestScope();
        using var worker = scope.StartWorker();
        try
        {
            string? ready = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(ready).IsEqualTo("acquired");
            await Assert.That(new FileLock(scope.Path).TryAcquire()).IsNull();
            if (kill)
                worker.Kill();
            else
                await worker.StandardInput.WriteLineAsync("release");
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using FileLockLease lease = await new FileLock(scope.Path).Acquire(TimeSpan.FromSeconds(5));
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
    public async Task Separate_process_waits_for_parent_lease()
    {
        using var scope = new LockTestScope();
        using FileLockLease parent = await new FileLock(scope.Path).Acquire();
        using var worker = scope.StartWorker();
        try
        {
            Task<string?> ready = worker.StandardOutput.ReadLineAsync();
            await Task.Delay(250);
            await Assert.That(ready.IsCompleted).IsFalse();
            parent.Dispose();
            await Assert.That(await ready.WaitAsync(TimeSpan.FromSeconds(20))).IsEqualTo("acquired");
            await worker.StandardInput.WriteLineAsync("release");
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(worker.ExitCode).IsEqualTo(0);
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
}
