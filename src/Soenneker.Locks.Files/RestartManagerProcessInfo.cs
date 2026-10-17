using System.Runtime.InteropServices;

namespace Soenneker.Locks.Files;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RestartManagerProcessInfo
{
    internal uint ProcessId;
    internal uint StartTimeLow;
    internal uint StartTimeHigh;
    internal fixed char ApplicationName[256];
    internal fixed char ServiceName[64];
    internal uint ApplicationType;
    internal uint Status;
    internal uint SessionId;
    internal int Restartable;
}
