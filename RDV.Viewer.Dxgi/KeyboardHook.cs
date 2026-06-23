using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RDV.Viewer.Dxgi;

public sealed class KeyboardHook : IDisposable
{
    public event Action<int, bool>? KeyEvent;
    public bool Active { get; set; }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
    private readonly LowLevelKeyboardProc _proc;
    private readonly nint _hookId;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hhk);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern nint GetModuleHandle(string lpModuleName);

    public KeyboardHook()
    {
        _proc = HookCallback;
        using var mod = Process.GetCurrentProcess().MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName!), 0);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && Active)
        {
            var vk = Marshal.ReadInt32(lParam);
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            if (isDown || isUp)
            {
                KeyEvent?.Invoke(vk, isDown);
                return 1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => UnhookWindowsHookEx(_hookId);
}
