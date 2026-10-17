using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Locks.Files.Abstract;

/// <summary>Inspects processes using a file on Windows, Linux, and macOS.</summary>
/// <remarks>
/// Windows uses Restart Manager; Linux and macOS require lsof on PATH (or /usr/sbin/lsof on macOS).
/// Results are a best-effort snapshot limited by OS permissions and process visibility. An empty result does
/// not prove a file is unlocked. Open-file users are included because not every platform exposes lock ownership.
/// This inspector never terminates processes or releases their locks. Process IDs can be reused after inspection.
/// </remarks>
public interface IFileLockInspector
{
    /// <summary>Returns processes that may be preventing access to the specified file.</summary>
    /// <param name="path">An existing file path; relative paths are resolved at the start of the call.</param>
    /// <param name="cancellationToken">Cancels inspection. A running Windows native call cannot be interrupted.</param>
    /// <returns>Distinct processes ordered by ID, including open-file users without a confirmed advisory lock.</returns>
    /// <exception cref="System.IO.FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="System.IO.IOException">Inspection failed or lsof reported a diagnostic.</exception>
    /// <exception cref="System.InvalidOperationException">The required lsof executable could not be started.</exception>
    Task<IReadOnlyList<FileLockProcess>> GetLockingProcesses(string path, CancellationToken cancellationToken = default);
}
