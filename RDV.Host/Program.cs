using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using RDV.Host;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

using var mutex = new Mutex(true, "RDVHostSingleInstance", out bool isNew);
if (!isNew)
{
    MessageBox.Show("RDV Host is already running.\n\nCheck the system tray.",
        "RDV Host", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var config = Config.Load();

if (config.IsFirstRun)
{
    if (!IsAdmin())
    {
        MessageBox.Show(
            "RDV Host needs to run as Administrator for first-run setup.\n\n" +
            "Please right-click the exe and choose 'Run as administrator'.",
            "RDV Host", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
    }

    var wizard = new SetupWizard();
    if (wizard.ShowDialog() != DialogResult.OK) return;

    config.SetPassword(wizard.Password);
    config.DuckDnsToken = wizard.DuckDnsToken;
    config.DuckDnsSubdomain = wizard.DuckDnsSubdomain;
    config.Save();

    FirstRunSetup.AddFirewallRule(config.Port);
    FirstRunSetup.RegisterAutoStart();
    FirstRunSetup.EnableSoftwareSAS();

    var address = config.DuckDnsSubdomain != null
        ? $"{config.DuckDnsSubdomain}.duckdns.org:{config.Port}"
        : $"<your-public-ip>:{config.Port}";

    MessageBox.Show(
        $"Setup complete! RDV Host will now start automatically when you log in.\n\n" +
        $"From your work laptop, connect to:\n{address}\n\n" +
        (config.DuckDnsSubdomain == null
            ? "Tip: Re-run setup and enable DuckDNS to get a stable hostname that won't change if your home IP rotates."
            : "Your IP will be kept up to date automatically via DuckDNS."),
        "RDV Host — Setup Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
}

using var server = new HostServer(config);
server.Start();

var localIP = GetLocalIP();
string? dnsAddress = config.DuckDnsSubdomain != null
    ? $"{config.DuckDnsSubdomain}.duckdns.org:{config.Port}"
    : null;

// Show local IP immediately so it's always visible in the tray.
var tray = new TrayApp(server, config, dnsAddress ?? $"Local: {localIP}:{config.Port}");

// Background: fetch public IP, update tray, show balloon.
_ = Task.Run(async () =>
{
    var publicIP = await GetPublicIPAsync();
    bool upnpOk = await UPnPHelper.TryMapPortAsync(config.Port);

    string connectLine = dnsAddress != null
        ? $"Connect to: {dnsAddress}"
        : publicIP != null
            ? $"Public: {publicIP}:{config.Port}"
            : upnpOk
                ? $"UPnP active — public IP unknown, port {config.Port} forwarded"
                : $"Local: {localIP}:{config.Port} (port forwarding may be needed)";

    tray.UpdateAddress(connectLine);
    tray.ShowBalloon("RDV Host Ready",
        dnsAddress != null
            ? $"Connect to: {dnsAddress}\nLocal: {localIP}:{config.Port}"
            : publicIP != null
                ? $"Public: {publicIP}:{config.Port}\nLocal: {localIP}:{config.Port}"
                : $"Local: {localIP}:{config.Port}");
});

// DuckDNS updater
DuckDnsUpdater? dns = null;
if (config.DuckDnsToken != null && config.DuckDnsSubdomain != null)
{
    dns = new DuckDnsUpdater(config.DuckDnsToken, config.DuckDnsSubdomain);
    dns.Start();
}

Application.Run(tray);

dns?.Dispose();

static bool IsAdmin()
{
    using var id = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
}

static string GetLocalIP()
{
    try
    {
        var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
        var ip = addresses.FirstOrDefault(a =>
            a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        return ip?.ToString() ?? "unknown";
    }
    catch { return "unknown"; }
}

static async Task<string?> GetPublicIPAsync()
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var ip = await http.GetStringAsync("https://api.ipify.org");
        return ip.Trim();
    }
    catch { return null; }
}
