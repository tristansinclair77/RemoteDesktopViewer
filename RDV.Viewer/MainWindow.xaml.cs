using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RDV.Viewer;

public partial class MainWindow : Window
{
    private ViewerClient? _client;
    private KeyboardHook? _hook;
    private bool _connected;

    public MainWindow()
    {
        InitializeComponent();
        HostBox.Text = LoadSavedHost();
        _hook = new KeyboardHook();
        _hook.KeyEvent += OnRemoteKey;
        Activated += (_, _) => { if (_hook != null) _hook.Active = _connected; };
        Deactivated += (_, _) => { if (_hook != null) _hook.Active = false; };
    }

    // ──────────────────── Connection ────────────────────

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(host)) { ShowError("Enter a host address."); return; }
        if (!int.TryParse(PortBox.Text.Trim(), out var port)) { ShowError("Invalid port."); return; }
        if (string.IsNullOrWhiteSpace(password)) { ShowError("Enter the password."); return; }

        ConnectBtn.IsEnabled = false;
        ConnectBtn.Content = "Connecting…";
        HideError();

        try
        {
            _client = new ViewerClient();
            _client.FrameReceived += OnFrameReceived;
            _client.ScreenInfoReceived += OnScreenInfo;
            _client.ScreenListReceived += OnScreenList;
            _client.Disconnected += OnDisconnected;

            await _client.ConnectAsync(host, port, password);

            SaveHost(host);
            ShowDesktop();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            if (_client != null) { await _client.DisposeAsync(); _client = null; }
            ConnectBtn.IsEnabled = true;
            ConnectBtn.Content = "Connect";
        }
    }

    private async void DisconnectBtn_Click(object sender, RoutedEventArgs e) => await DisconnectAsync();

    private async Task DisconnectAsync()
    {
        _connected = false;
        if (_hook != null) _hook.Active = false;
        if (_client != null) { await _client.DisposeAsync(); _client = null; }
        ShowConnect();
    }

    private void OnDisconnected()
    {
        Dispatcher.Invoke(async () =>
        {
            ShowError("Connection lost.");
            await DisconnectAsync();
        });
    }

    // ──────────────────── Frame rendering ────────────────────

    private void OnFrameReceived(byte[] jpegData)
    {
        // Decode off the UI thread, then update on it
        BitmapImage bmp;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(jpegData);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze(); // Makes it safe to pass across threads
        }
        catch { return; }

        Dispatcher.InvokeAsync(() => DesktopImage.Source = bmp);
    }

    private void OnScreenInfo(int w, int h)
    {
        Dispatcher.InvokeAsync(() => ResolutionLabel.Text = $"{w}×{h}");
    }

    private RemoteScreen[]? _screens;
    private int _selectedScreen;

    private void OnScreenList(RemoteScreen[] screens, int selected)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _screens = screens;
            _selectedScreen = selected;
            RebuildMonitorBar();
        });
    }

    private void RebuildMonitorBar()
    {
        MonitorBar.Children.Clear();
        if (_screens == null || _screens.Length <= 1)
        {
            MonitorBarContainer.Visibility = Visibility.Collapsed;
            return;
        }

        for (int i = 0; i < _screens.Length; i++)
        {
            var screen = _screens[i];
            var isSelected = screen.Index == _selectedScreen;
            var btn = new Button
            {
                Content = screen.Primary ? $"{i + 1}*" : $"{i + 1}",
                Width = 32,
                Height = 24,
                Margin = new Thickness(2, 0, 2, 0),
                FontSize = 12,
                Background = isSelected ? new SolidColorBrush(Color.FromRgb(0x33, 0x88, 0xff))
                                        : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                ToolTip = $"{screen.Name} — {screen.Width}×{screen.Height}{(screen.Primary ? " (primary)" : "")}",
                Tag = screen.Index,
                Cursor = Cursors.Hand
            };
            btn.Click += MonitorBtn_Click;
            MonitorBar.Children.Add(btn);
        }
        MonitorBarContainer.Visibility = Visibility.Visible;
    }

    private void MonitorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null || sender is not Button b || b.Tag is not int idx) return;
        if (idx == _selectedScreen) return;
        _selectedScreen = idx;
        RebuildMonitorBar();
        _ = _client.SendSelectScreenAsync(idx);
    }

    // ──────────────────── Input forwarding ────────────────────

    private void Desktop_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_connected || _client == null) return;
        var (rx, ry) = ToRemote(e.GetPosition(DesktopImage));
        _ = _client.SendMouseMoveAsync(rx, ry);
    }

    private void Desktop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_connected || _client == null) return;
        DesktopImage.CaptureMouse();
        _ = _client.SendMouseButtonAsync(ButtonName(e.ChangedButton), down: true);
    }

    private void Desktop_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_connected || _client == null) return;
        DesktopImage.ReleaseMouseCapture();
        _ = _client.SendMouseButtonAsync(ButtonName(e.ChangedButton), down: false);
    }

    private void Desktop_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_connected || _client == null) return;
        _ = _client.SendMouseWheelAsync(e.Delta);
    }

    private void OnRemoteKey(int vk, bool isDown)
    {
        if (!_connected || _client == null) return;
        _ = _client.SendKeyAsync(vk, isDown);
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) ConnectBtn_Click(sender, e);
    }

    // ──────────────────── Coordinate mapping ────────────────────

    private (int x, int y) ToRemote(System.Windows.Point pos)
    {
        if (_client == null) return (0, 0);

        double cw = DesktopImage.ActualWidth;
        double ch = DesktopImage.ActualHeight;
        double imgAspect = (double)_client.RemoteWidth / _client.RemoteHeight;
        double ctrlAspect = cw / ch;

        double renderW, renderH, offsetX = 0, offsetY = 0;
        if (imgAspect > ctrlAspect)
        {
            renderW = cw;
            renderH = cw / imgAspect;
            offsetY = (ch - renderH) / 2;
        }
        else
        {
            renderH = ch;
            renderW = ch * imgAspect;
            offsetX = (cw - renderW) / 2;
        }

        int rx = (int)((pos.X - offsetX) / renderW * _client.RemoteWidth);
        int ry = (int)((pos.Y - offsetY) / renderH * _client.RemoteHeight);

        rx = Math.Clamp(rx, 0, _client.RemoteWidth - 1);
        ry = Math.Clamp(ry, 0, _client.RemoteHeight - 1);
        return (rx, ry);
    }

    // ──────────────────── UI state helpers ────────────────────

    private void ShowDesktop()
    {
        _connected = true;
        if (_hook != null) _hook.Active = true;
        ConnectPanel.Visibility = Visibility.Collapsed;
        DesktopPanel.Visibility = Visibility.Visible;
        WindowState = WindowState.Maximized;
        ResizeMode = ResizeMode.CanResize;
    }

    private void ShowConnect()
    {
        DesktopPanel.Visibility = Visibility.Collapsed;
        ConnectPanel.Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
        ConnectBtn.IsEnabled = true;
        ConnectBtn.Content = "Connect";
        _screens = null;
        MonitorBar.Children.Clear();
        MonitorBarContainer.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        ErrorLabel.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorLabel.Visibility = Visibility.Collapsed;

    private static string ButtonName(MouseButton b) => b switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => "left"
    };

    // ──────────────────── Settings persistence ────────────────────

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RDV", "viewer.txt");

    private static string LoadSavedHost()
    {
        try { return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : ""; }
        catch { return ""; }
    }

    private static void SaveHost(string host)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, host); }
        catch { }
    }

    // ──────────────────── Cleanup ────────────────────

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hook?.Dispose();
        if (_client != null) await _client.DisposeAsync();
    }
}
