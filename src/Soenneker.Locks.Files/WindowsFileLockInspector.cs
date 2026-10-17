using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Soenneker.Locks.Files;

internal static unsafe partial class WindowsFileLockInspector
{
    internal static IReadOnlyList<FileLockProcess> GetProcesses(string path, CancellationToken cancellationToken)
    {
        char* key = stackalloc char[33];
        Check(StartSession(out uint session, 0, key));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            fixed (char* file = path)
            {
                char* fileName = file;
                Check(RegisterResources(session, 1, &fileName, 0, null, 0, null));
            }

            uint count = 0;
            uint result = GetList(session, out uint needed, ref count, null, out _);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result == 0)
                    return Array.Empty<FileLockProcess>();
                if (result != 234) // ERROR_MORE_DATA
                    Check(result);

                var buffer = new RestartManagerProcessInfo[checked((int)needed)];
                count = (uint)buffer.Length;
                fixed (RestartManagerProcessInfo* entries = buffer)
                {
                    result = GetList(session, out needed, ref count, entries, out _);
                    if (result != 0)
                        continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    var processes = new SortedDictionary<int, FileLockProcess>();
                    for (var i = 0; i < count; i++)
                    {
                        int id = checked((int)entries[i].ProcessId);
                        processes[id] = new FileLockProcess(id, new string(entries[i].ApplicationName), false);
                    }
                    return new List<FileLockProcess>(processes.Values);
                }
            }
            Check(result);
            throw new IOException("The process list changed repeatedly during file lock inspection.");
        }
        finally
        {
            EndSession(session);
        }
    }

    private static void Check(uint error)
    {
        if (error != 0)
            throw new IOException("Windows Restart Manager could not inspect file users.", new Win32Exception((int)error));
    }

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmStartSession")]
    private static partial uint StartSession(out uint session, uint flags, char* key);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmRegisterResources")]
    private static partial uint RegisterResources(uint session, uint fileCount, char** files, uint processCount,
        void* processes, uint serviceCount, char** services);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmGetList")]
    private static partial uint GetList(uint session, out uint needed, ref uint count, RestartManagerProcessInfo* processes, out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmEndSession")]
    private static partial uint EndSession(uint session);
}
