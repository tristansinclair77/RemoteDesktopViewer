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

string? connectionAddress = config.DuckDnsSubdomain != null
    ? $"Connect to: {config.DuckDnsSubdomain}.duckdns.org:{config.Port}"
    : null;

var tray = new TrayApp(server, config, connectionAddress);

// UPnP in background
_ = Task.Run(async () =>
{
    bool ok = await UPnPHelper.TryMapPortAsync(config.Port);
    if (!ok && connectionAddress == null)
        tray.UpdateAddress($"UPnP failed — set up port forwarding for port {config.Port}");
    else if (ok && connectionAddress == null)
        tray.UpdateAddress($"UPnP active on port {config.Port}");
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
