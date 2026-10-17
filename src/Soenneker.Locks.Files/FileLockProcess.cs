namespace Soenneker.Locks.Files;

/// <summary>A snapshot of a process using the inspected file.</summary>
/// <param name="ProcessId">The operating system process ID.</param>
/// <param name="Name">The reported application or command name, which may be truncated.</param>
/// <param name="HasConfirmedLock">True when lsof reported an advisory lock; false means unconfirmed,
/// not unlocked. Windows Restart Manager does not expose this information.</param>
public sealed record FileLockProcess(int ProcessId, string Name, bool HasConfirmedLock);
