using System.Runtime.InteropServices;

namespace RDV.Host;

public static class InputInjector
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    public static void MouseMove(int x, int y, int screenLeft, int screenTop)
    {
        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vWidth <= 0 || vHeight <= 0) return;

        int targetX = screenLeft + x;
        int targetY = screenTop + y;
        int absX = (int)((double)(targetX - vLeft) / vWidth * 65535);
        int absY = (int)((double)(targetY - vTop) / vHeight * 65535);

        Send(new INPUT
        {
            type = INPUT_MOUSE,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
                }
            }
        });
    }

    public static void MouseButton(string button, bool down)
    {
        uint flag = (button, down) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN,
            ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };
        if (flag == 0) return;

        Send(new INPUT
        {
            type = INPUT_MOUSE,
            union = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = flag } }
        });
    }

    public static void MouseWheel(int delta)
    {
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT { mouseData = (uint)delta, dwFlags = MOUSEEVENTF_WHEEL }
            }
        });
    }

    public static void KeyEvent(ushort vk, bool down)
    {
        uint flags = down ? 0u : KEYEVENTF_KEYUP;
        // Extended key flag for navigation/function keys
        if (vk is (>= 0x21 and <= 0x28) or 0x2D or 0x2E or (>= 0x70 and <= 0x87) or 0x5B or 0x5C)
            flags |= KEYEVENTF_EXTENDEDKEY;

        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            union = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
        });
    }

    private static void Send(INPUT input) =>
        SendInput(1, [input], Marshal.SizeOf<INPUT>());

    [DllImport("sas.dll")]
    private static extern void SendSAS(bool asUser);

    public static bool SendSas()
    {
        try { SendSAS(false); return true; }
        catch { return false; }
    }
}
