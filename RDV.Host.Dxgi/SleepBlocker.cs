using System.Runtime.InteropServices;

namespace RDV.Host.Dxgi;

internal static class SleepBlocker
{
    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_CONTINUOUS      = 0x80000000,
    }

    [DllImport("kernel32.dll")]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    // Prevent the system from sleeping. Display may still turn off.
    public static void Prevent() =>
        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED);

    // Restore normal sleep behavior.
    public static void Allow() =>
        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
}
