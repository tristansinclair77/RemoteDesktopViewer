namespace RDV.Host.Dxgi;

public sealed class SetupWizard : Form
{
    private readonly TextBox _passwordBox;
    private readonly TextBox _confirmBox;
    private readonly CheckBox _duckDnsCheck;
    private readonly TextBox _duckDnsTokenBox;
    private readonly TextBox _duckDnsSubdomainBox;
    private readonly Label _errorLabel;

    public string Password { get; private set; } = "";
    public string? DuckDnsToken { get; private set; }
    public string? DuckDnsSubdomain { get; private set; }

    public SetupWizard()
    {
        Text = "RDV Host (DXGI) — First Run Setup";
        ClientSize = new Size(420, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;

        Label lbl(string t, int y)
        {
            var l = new Label { Text = t, Location = new Point(20, y), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            Controls.Add(l); return l;
        }
        TextBox txt(int y, bool password = false)
        {
            var t = new TextBox
            {
                Location = new Point(20, y), Width = 380,
                BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            if (password) t.UseSystemPasswordChar = true;
            Controls.Add(t); return t;
        }

        lbl("Set a password for remote access:", 20);
        _passwordBox = txt(42, password: true);

        lbl("Confirm password:", 74);
        _confirmBox = txt(96, password: true);

        lbl("DuckDNS Token (from duckdns.org):", 168);
        _duckDnsTokenBox = txt(190); _duckDnsTokenBox.Enabled = false;

        lbl("Your subdomain (e.g.  myhome  →  myhome.duckdns.org):", 222);
        _duckDnsSubdomainBox = txt(244); _duckDnsSubdomainBox.Enabled = false;

        _duckDnsCheck = new CheckBox
        {
            Text = "Use DuckDNS for a stable hostname (recommended)",
            Location = new Point(20, 136), AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 200)
        };
        _duckDnsCheck.CheckedChanged += (_, _) =>
        {
            _duckDnsTokenBox.Enabled = _duckDnsCheck.Checked;
            _duckDnsSubdomainBox.Enabled = _duckDnsCheck.Checked;
        };
        Controls.Add(_duckDnsCheck);

        _errorLabel = new Label
        {
            Location = new Point(20, 284), Size = new Size(380, 36),
            ForeColor = Color.FromArgb(255, 100, 100), Text = ""
        };
        Controls.Add(_errorLabel);

        var btn = new Button
        {
            Text = "Finish Setup",
            Location = new Point(20, 328), Width = 180, Height = 36,
            BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += Finish_Click;
        Controls.Add(btn);
    }

    private void Finish_Click(object? sender, EventArgs e)
    {
        _errorLabel.Text = "";

        if (string.IsNullOrWhiteSpace(_passwordBox.Text))
        { _errorLabel.Text = "Password cannot be empty."; return; }

        if (_passwordBox.Text != _confirmBox.Text)
        { _errorLabel.Text = "Passwords do not match."; return; }

        if (_duckDnsCheck.Checked)
        {
            if (string.IsNullOrWhiteSpace(_duckDnsTokenBox.Text))
            { _errorLabel.Text = "DuckDNS token is required when DuckDNS is enabled."; return; }
            if (string.IsNullOrWhiteSpace(_duckDnsSubdomainBox.Text))
            { _errorLabel.Text = "Subdomain is required when DuckDNS is enabled."; return; }
            DuckDnsToken = _duckDnsTokenBox.Text.Trim();
            DuckDnsSubdomain = _duckDnsSubdomainBox.Text.Trim().ToLowerInvariant();
        }

        Password = _passwordBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
