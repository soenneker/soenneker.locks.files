using System.Runtime.InteropServices;

namespace Soenneker.Locks.Files;

internal static partial class UnixFileLock
{
    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    internal static partial int Flock(int descriptor, int operation);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    internal static partial int Close(int descriptor);
}
