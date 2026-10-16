using System.Runtime.InteropServices;

namespace Soenneker.Locks.Files;

internal static partial class WindowsFileLock
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint Open(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
