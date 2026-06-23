using System.Diagnostics;

namespace RDV.Host.Dxgi;

internal static class PortReclaimer
{
    public static bool TryFreePort(int port)
    {
        var pid = FindListenerPid(port);
        if (pid is null) return false;
        if (pid.Value == Environment.ProcessId) return false;

        try
        {
            using var proc = Process.GetProcessById(pid.Value);
            proc.Kill(entireProcessTree: true);
            return proc.WaitForExit(3000);
        }
        catch
        {
            return false;
        }
    }

    private static int? FindListenerPid(int port)
    {
        var psi = new ProcessStartInfo("netstat", "-ano")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        string output;
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return null;
            output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
        }
        catch
        {
            return null;
        }

        var suffix = ":" + port;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("TCP", StringComparison.Ordinal)) continue;
            if (line.IndexOf("LISTENING", StringComparison.Ordinal) < 0) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            if (!parts[1].EndsWith(suffix, StringComparison.Ordinal)) continue;

            if (int.TryParse(parts[^1], out var pid)) return pid;
        }

        return null;
    }
}
