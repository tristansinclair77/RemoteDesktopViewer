using System.Diagnostics;

namespace RDV.Host.Dxgi;

public static class FirstRunSetup
{
    private const string FirewallRuleName = "RDV Host (DXGI)";
    private const string ScheduledTaskName = "RDV Host (DXGI)";

    public static void AddFirewallRule(int port)
    {
        Run("netsh", $"advfirewall firewall delete rule name=\"{FirewallRuleName}\"");
        Run("netsh", $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port} description=\"Remote Desktop Viewer DXGI host\"");
    }

    public static void RegisterAutoStart()
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName;
        Run("schtasks", $"/Create /TN \"{ScheduledTaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
    }

    public static void EnableSoftwareSAS()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            key?.SetValue("SoftwareSASGeneration", 3, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }
    }

    public static void RemoveAutoStart()
    {
        Run("schtasks", $"/Delete /TN \"{ScheduledTaskName}\" /F");
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
