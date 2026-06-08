using System.Diagnostics;

namespace RDV.Host;

public static class FirstRunSetup
{
    public static void AddFirewallRule(int port)
    {
        // Remove any stale rule first, then add fresh
        Run("netsh", $"advfirewall firewall delete rule name=\"RDV Host\"");
        Run("netsh", $"advfirewall firewall add rule name=\"RDV Host\" dir=in action=allow protocol=TCP localport={port} description=\"Remote Desktop Viewer host\"");
    }

    public static void RegisterAutoStart()
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName;
        // ONLOGON + HIGHEST so it runs with the same elevation level as the user session
        Run("schtasks", $"/Create /TN \"RDV Host\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
    }

    public static void RemoveAutoStart()
    {
        Run("schtasks", "/Delete /TN \"RDV Host\" /F");
    }

    private static void Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
        }
        catch { }
    }
}
