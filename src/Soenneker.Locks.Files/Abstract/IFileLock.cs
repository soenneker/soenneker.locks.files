using System;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Locks.Files.Abstract;

/// <summary>Coordinates exclusive access between cooperating processes using a persistent lock file.</summary>
/// <remarks>
/// Supported on Windows, Linux, and macOS. All callers must use the same file on a filesystem supporting locks.
/// Use a dedicated file in a trusted directory with permissions allowing every participant to open it for writing.
/// Do not delete, replace, or rename the lock file while participants may be running.
/// Locks are non-reentrant, do not guarantee fairness, and are released when their owning process exits.
/// Construct FileLock with the lock-file path. Relative paths are resolved once at construction.
/// The reusable lock object does not own a lease; dispose each acquired lease to release it.
/// </remarks>
public interface IFileLock
{
    /// <summary>Waits asynchronously for an exclusive lease. The parent directory must already exist.</summary>
    /// <param name="cancellationToken">Cancels acquisition, without affecting a lease already returned.</param>
    /// <returns>A lease that must be disposed to release the lock.</returns>
    /// <remarks>Waits indefinitely until acquired or canceled. Creates the file if absent; never deletes it.</remarks>
    ValueTask<FileLockLease> Acquire(CancellationToken cancellationToken = default);

    /// <summary>Waits up to the specified timeout for an exclusive lease on this lock's file.</summary>
    /// <param name="timeout">Maximum wait; <see cref="Timeout.InfiniteTimeSpan"/> waits indefinitely. Zero attempts once.</param>
    /// <param name="cancellationToken">Cancels acquisition, without affecting a lease already returned.</param>
    /// <returns>A lease that must be disposed to release the lock.</returns>
    /// <exception cref="TimeoutException">The lock remained unavailable for the requested wait.</exception>
    /// <exception cref="OperationCanceledException">Acquisition was canceled.</exception>
    /// <exception cref="System.IO.IOException">Opening or locking failed for a reason other than contention.</exception>
    ValueTask<FileLockLease> Acquire(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Attempts acquisition once, returning null only when another holder prevents acquisition.</summary>
    /// <returns>An exclusive disposable lease, or null on contention. Other filesystem errors propagate.</returns>
    FileLockLease? TryAcquire();
}
