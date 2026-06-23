namespace RDV.Host.Dxgi;

public sealed class PasswordPromptDialog : Form
{
    private readonly TextBox _passwordBox;
    private readonly TextBox _confirmBox;
    private readonly Label _errorLabel;

    public string Password { get; private set; } = "";

    public PasswordPromptDialog()
    {
        Text = "RDV Host (DXGI) — Set Password";
        ClientSize = new Size(340, 200);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;

        Label lbl(string t, int y)
        {
            var l = new Label { Text = t, Location = new Point(20, y), AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180) };
            Controls.Add(l); return l;
        }
        TextBox txt(int y)
        {
            var t = new TextBox
            {
                Location = new Point(20, y), Width = 300,
                BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true
            };
            Controls.Add(t); return t;
        }

        lbl("New password:", 12);
        _passwordBox = txt(32);

        lbl("Confirm password:", 62);
        _confirmBox = txt(82);

        _errorLabel = new Label
        {
            Location = new Point(20, 112), Size = new Size(300, 20),
            ForeColor = Color.FromArgb(255, 100, 100), Text = ""
        };
        Controls.Add(_errorLabel);

        var ok = new Button
        {
            Text = "Save", Location = new Point(140, 144), Width = 88, Height = 32,
            BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += Ok_Click;
        Controls.Add(ok);

        var cancel = new Button
        {
            Text = "Cancel", Location = new Point(232, 144), Width = 88, Height = 32,
            BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        cancel.FlatAppearance.BorderSize = 0;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Ok_Click(object? sender, EventArgs e)
    {
        _errorLabel.Text = "";

        if (string.IsNullOrWhiteSpace(_passwordBox.Text))
        { _errorLabel.Text = "Password cannot be empty."; return; }

        if (_passwordBox.Text != _confirmBox.Text)
        { _errorLabel.Text = "Passwords do not match."; return; }

        Password = _passwordBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
