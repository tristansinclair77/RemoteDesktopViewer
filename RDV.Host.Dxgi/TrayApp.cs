namespace RDV.Host.Dxgi;

public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _addressItem;
    private readonly Config _config;
    private string? _copyableHost;

    public TrayApp(HostServer server, Config config, string? connectionAddress, string? copyableHost = null)
    {
        _config = config;
        _copyableHost = copyableHost;

        _statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        _addressItem = new ToolStripMenuItem(FormatAddressLabel(connectionAddress, _copyableHost))
        {
            Enabled = _copyableHost != null,
            ToolTipText = _copyableHost != null ? "Click to copy host address" : null
        };
        _addressItem.Click += (_, _) => CopyHostToClipboard();

        var setPasswordItem = new ToolStripMenuItem("Set Password...");
        setPasswordItem.Click += (_, _) => PromptForNewPassword();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("RDV Host (DXGI)") { Enabled = false, Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(_addressItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(setPasswordItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp(server));

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "RDV Host (DXGI)",
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
        var prefix = "DXGI: ";
        var full = prefix + msg;
        _tray.Text = full.Length > 63 ? full[..63] : full;
    }

    public void UpdateAddress(string address, string? copyableHost = null)
    {
        void Apply()
        {
            _copyableHost = copyableHost;
            _addressItem.Text = FormatAddressLabel(address, _copyableHost);
            _addressItem.Enabled = _copyableHost != null;
            _addressItem.ToolTipText = _copyableHost != null ? "Click to copy host address" : null;
        }
        if (_tray.ContextMenuStrip!.InvokeRequired)
            _tray.ContextMenuStrip.Invoke(Apply);
        else
            Apply();
    }

    public void ShowBalloon(string title, string text)
    {
        if (_tray.ContextMenuStrip!.InvokeRequired)
            _tray.ContextMenuStrip.Invoke(() => _tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info));
        else
            _tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
    }

    private static string FormatAddressLabel(string? address, string? copyableHost)
    {
        var baseText = address ?? "Address unknown";
        return copyableHost != null ? $"{baseText}  (click to copy)" : baseText;
    }

    private void CopyHostToClipboard()
    {
        if (_copyableHost == null) return;
        var text = _copyableHost;
        Exception? copyError = null;
        try
        {
            var t = new Thread(() =>
            {
                try { Clipboard.SetText(text); }
                catch (Exception ex) { copyError = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
        }
        catch (Exception ex)
        {
            copyError = ex;
        }

        if (copyError != null)
            ShowBalloon("RDV Host (DXGI)", $"Copy failed: {copyError.Message}");
        else
            ShowBalloon("RDV Host (DXGI)", $"Copied: {text}");
    }

    private void PromptForNewPassword()
    {
        using var dlg = new PasswordPromptDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _config.SetPassword(dlg.Password);
            _config.Save();
            ShowBalloon("RDV Host (DXGI)", "Password updated. New connections will require the new password.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save password:\n{ex.Message}",
                "RDV Host (DXGI)", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
