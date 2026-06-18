namespace RDV.Host;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _addressItem;

    public TrayApp(HostServer server, Config config, string? connectionAddress)
    {
        _statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        _addressItem = new ToolStripMenuItem(connectionAddress ?? "Address unknown") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("RDV Host") { Enabled = false, Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(_addressItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp(server));

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RDV Host",
            Visible = true,
            ContextMenuStrip = menu
        };

        server.StatusChanged += msg =>
        {
            if (_tray.ContextMenuStrip!.InvokeRequired)
                _tray.ContextMenuStrip.Invoke(() => UpdateStatus(msg));
            else
                UpdateStatus(msg);
        };

        UpdateStatus($"Listening on port {config.Port}");
    }

    private void UpdateStatus(string msg)
    {
        _statusItem.Text = msg;
        _tray.Text = msg.Length > 63 ? msg[..63] : msg; // NotifyIcon tooltip limit
    }

    public void UpdateAddress(string address)
    {
        if (_tray.ContextMenuStrip!.InvokeRequired)
            _tray.ContextMenuStrip.Invoke(() => _addressItem.Text = address);
        else
            _addressItem.Text = address;
    }

    public void ShowBalloon(string title, string text)
    {
        if (_tray.ContextMenuStrip!.InvokeRequired)
            _tray.ContextMenuStrip.Invoke(() => _tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info));
        else
            _tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
    }

    private void ExitApp(HostServer server)
    {
        server.Stop();
        _tray.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tray.Dispose();
        base.Dispose(disposing);
    }
}
