using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Locks.Files;

internal static class UnixFileLockInspector
{
    internal static async Task<IReadOnlyList<FileLockProcess>> GetProcesses(string path, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/sbin/lsof" : "lsof")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-nP");
        start.ArgumentList.Add("-F0pcl");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(path);
        using var process = new Process { StartInfo = start };
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("File lock inspection requires an executable lsof installation.", ex);
        }

        // Drain both pipes concurrently to avoid blocking lsof on a full pipe.
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); }
            catch (InvalidOperationException) { } // Already exited.
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            throw;
        }

        string text = await output.ConfigureAwait(false);
        string diagnostics = await errors.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // lsof exits 1 for an empty selection. Diagnostics must not silently become "unlocked".
        if (!string.IsNullOrWhiteSpace(diagnostics) || (process.ExitCode != 0 && !(process.ExitCode == 1 && text.Length == 0)))
            throw new IOException($"lsof could not inspect '{path}' (exit {process.ExitCode}): {diagnostics.Trim()}");
        return Parse(text);
    }

    internal static IReadOnlyList<FileLockProcess> Parse(string output)
    {
        var processes = new SortedDictionary<int, FileLockProcess>();
        int id = 0;
        string name = "";
        bool confirmed = false;
        foreach (string field in output.Split('\0'))
        {
            // lsof places a newline between process/file sets even in NUL-delimited output.
            ReadOnlySpan<char> value = field.AsSpan().TrimStart('\n');
            if (value.IsEmpty)
                continue;
            switch (value[0])
            {
                case 'p':
                    Save();
                    if (!int.TryParse(value[1..], NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0)
                        throw new IOException("lsof returned an invalid process ID.");
                    name = "";
                    confirmed = false;
                    break;
                case 'c':
                    name = value[1..].ToString();
                    break;
                case 'l':
                    // Documented read/write (whole/partial), read-write, or unknown-type locks.
                    if (value.Length > 1 && value[1] is 'r' or 'R' or 'w' or 'W' or 'u' or 'U' or 'N')
                        confirmed = true;
                    break;
            }
        }
        Save();
        return new List<FileLockProcess>(processes.Values);

        void Save()
        {
            if (id > 0)
            {
                bool previous = processes.TryGetValue(id, out FileLockProcess? existing) && existing.HasConfirmedLock;
                processes[id] = new FileLockProcess(id, name, confirmed || previous);
            }
        }
    }
}
