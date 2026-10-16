[![](https://img.shields.io/nuget/v/soenneker.locks.files.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.locks.files/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.locks.files/build-and-test.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.locks.files/actions/workflows/build-and-test.yml)
[![](https://img.shields.io/nuget/dt/soenneker.locks.files.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.locks.files/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.locks.files/codeql.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.locks.files/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Locks.Files
Cross-process filesystem locking with disposable leases for .NET 10. Coordinate installers, cache writers, generators, and other cooperating processes through a shared lock file.

`FileLock` provides exclusive access on Windows, Linux, and macOS. Each instance is bound to one file and can be shared between threads. Separate instances and processes coordinate through the same file.

## Installation

```shell
dotnet add package Soenneker.Locks.Files
```

## Acquire a lock

Create the parent directory, choose a dedicated lock file, and hold the returned lease for the entire protected operation.

```csharp
using Soenneker.Locks.Files;

string directory = Path.Combine(Path.GetTempPath(), "my-app");
Directory.CreateDirectory(directory);
string lockPath = Path.Combine(directory, "cache.lock");

var fileLock = new FileLock(lockPath);

using (await fileLock.Acquire(
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken))
{
    await RefreshCache(cancellationToken);
}
```

Reuse the same `fileLock` for subsequent operations:

```csharp
using (await fileLock.Acquire(cancellationToken))
{
    await RefreshCache(cancellationToken);
}
```

The reusable `FileLock` is not disposable. The `using` statement disposes the acquired lease. Relative paths are resolved once in the constructor, so later working-directory changes do not redirect the lock. Constructing a lock does not open or create the file.

The lock is released when the lease is disposed, including when the protected operation throws. Both `using` and `await using` are supported. Disposal is thread-safe and idempotent; it does not delete the lock file.

Every participant must resolve `lockPath` to the same file. Temporary directories can differ between users, services, and containers; use an explicitly configured shared directory when those processes need to coordinate.

## Try once without waiting

`TryAcquire` returns `null` when another holder prevents acquisition. Filesystem and permission errors still throw.

```csharp
using FileLockLease? lease = fileLock.TryAcquire();
if (lease is null)
    return; // Another process is already doing the work.

await RefreshCache(cancellationToken);
```

## Timeouts and cancellation

| Argument | Behavior |
| --- | --- |
| Omitted timeout | Wait indefinitely until acquired or canceled. |
| `Timeout.InfiniteTimeSpan` | Wait indefinitely until acquired or canceled. |
| `TimeSpan.Zero` | Attempt once; throw `TimeoutException` on contention. |
| Positive timeout | Retry until acquired, canceled, or the wait expires. |
| Other negative timeout | Throw `ArgumentOutOfRangeException`. |

```csharp
using var cancellation = new CancellationTokenSource();
cancellation.CancelAfter(TimeSpan.FromSeconds(10));

try
{
    using FileLockLease lease = await fileLock.Acquire(
        timeout: TimeSpan.FromSeconds(5),
        cancellationToken: cancellation.Token);

    await RefreshCache(cancellation.Token);
}
catch (TimeoutException)
{
    // The lock was not available before the acquisition timeout.
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    // Acquisition or the protected operation was canceled.
}
```

The timeout and cancellation token control acquisition. They do not revoke a returned lease or limit how long it can be held. Pass cancellation separately to the protected operation and dispose the lease when that operation finishes.

Waiting uses a reusable timer with a 50 ms retry interval, shortened near a finite deadline. Acquisition has no FIFO or fairness guarantee. Timeout and cancellation checks occur between native filesystem calls; they cannot interrupt a filesystem call already in progress.

## Coordinating installation or initialization

Recheck the resource **after acquiring the lock**. Another process may have completed the work while this caller waited.

```csharp
async Task EnsureInstalled(FileLock fileLock, CancellationToken cancellationToken)
{
    if (await IsInstalled(cancellationToken))
        return;

    using FileLockLease lease = await fileLock.Acquire(
        timeout: TimeSpan.FromMinutes(20),
        cancellationToken: cancellationToken);

    if (await IsInstalled(cancellationToken))
        return;

    await Install(cancellationToken);
}
```

`RefreshCache`, `IsInstalled`, and `Install` in these examples represent application code. Use a single stable lock path for the shared resource, even when callers operate from different project directories.

## Lock-file lifecycle and guarantees

- The parent directory must already exist. The library creates the lock file if it is missing and preserves existing contents.
- The file remains after release. Its existence does **not** mean the lock is held; ownership is tied to an open operating-system handle.
- Do not delete, replace, or rename the file while participants may be acquiring or holding it. Replacing it can allow processes to lock different files under the same path.
- Disposing a lease or terminating its owning process releases the lock. No stale-file cleanup or expiration protocol is required.
- Locks are non-reentrant. Acquiring the same file again while holding a lease contends, even through the same `FileLock` instance.
- Use a dedicated lock file in a trusted directory, with read/write permissions for every participant. Protect the actual resource separately and have all writers follow the same locking protocol.
- A lock does not make protected writes transactional. If a process terminates partway through an update, application code must detect or recover from that incomplete work.

## Platforms and filesystems

| Platform | Mechanism |
| --- | --- |
| Windows | Native `CreateFileW` with exclusive sharing. |
| Linux and macOS | Nonblocking exclusive `flock` on an open file descriptor. |

Platform detection uses `Soenneker.Utils.Runtime`. Unsupported operating systems throw `PlatformNotSupportedException`.

Unix locks are advisory: processes that ignore the locking protocol can still modify the protected resource. Network filesystems, mounted volumes, and container environments must provide compatible locking semantics and expose the same underlying file to all participants. This library does not provide a distributed lease or fencing token.

## API

`IFileLock` is available in `Soenneker.Locks.Files.Abstract` and is implemented by `FileLock`.

```csharp
ValueTask<FileLockLease> Acquire(CancellationToken cancellationToken = default);

ValueTask<FileLockLease> Acquire(
    TimeSpan timeout,
    CancellationToken cancellationToken = default);

FileLockLease? TryAcquire();
```

`FileLockLease` implements `IDisposable` and `IAsyncDisposable`. Await each acquisition `ValueTask` once and keep the lease alive until the protected work completes.

Missing directories, access failures, invalid paths, and unsupported locking operations propagate as exceptions rather than being retried as contention. Ordinary contention returns `null` from `TryAcquire` or causes `Acquire` to wait.

## Performance

- Path validation and normalization occur once at construction, rather than on every acquisition.
- Uncontended acquisition returns a completed `ValueTask`; the async retry state is created only when waiting is necessary.
- Native return codes handle normal contention without creating exceptions on the existing-file path.
- Waiting reuses one `PeriodicTimer` rather than allocating a new delay task on every retry.
- Successful acquisition allocates a lease and a safe handle, preserving deterministic release and idempotent disposal.

A local Release-mode Windows measurement over 2,000 attempts observed **0 managed bytes per contended `TryAcquire`** for an existing lock file and a warmed absolute path. This is a narrow allocation measurement, not a guarantee for every path, platform, or first-use scenario. Filesystem calls remain part of every attempt; path normalization, long-path marshalling, first creation, and exceptional failures can add costs.

## Validation

The tests cover independent instances, separate processes, process termination, release and reacquisition, cancellation, timeouts, Unicode paths, preservation of file contents, and propagation of filesystem errors. CI is configured for Windows, Linux, and macOS.

Run the focused tests using Microsoft Testing Platform:

```shell
dotnet test --project test/Soenneker.Locks.Files.Tests -- --treenode-filter "/*/*/FileLockTests/*"
```
