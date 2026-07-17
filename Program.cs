using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BluetoothAudioRelay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\BluetoothAudioRelay.Singleton", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "蓝牙音频中继已经在运行，请查看右下角系统托盘。",
                "蓝牙音频中继",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        GC.KeepAlive(singleInstance);
    }
}

public sealed class MainForm : Form
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private static readonly TimeSpan QuickReconnectSettleDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan AudioProfileResetSettleDelay = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan DeviceStatusRefreshInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ConnectingStateTimeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, AudioPlaybackConnection> _connections = new();
    private readonly BindingSource _devicesSource = new();
    private readonly BindingList<RemoteAudioDevice> _devices = new();
    private readonly DataGridView _devicesGrid;
    private readonly Label _deviceCountLabel;
    private readonly Label _selectedDeviceLabel;
    private readonly Label _statusLabel;
    private readonly StatusDot _statusDot;
    private readonly TextBox _logTextBox;
    private readonly Icon _appIcon;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayQuickConnectItem;
    private readonly System.Windows.Forms.Timer _deviceStatusTimer;
    private DeviceWatcher? _deviceWatcher;
    private bool _exitRequested;
    private bool _trayHintShown;
    private bool _isDeviceStatusRefreshRunning;

    public MainForm()
    {
        Text = "蓝牙音频中继 · Bluetooth Audio Relay";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 760);
        Size = new Size(1220, 860);
        BackColor = AppTheme.Background;
        Font = new Font("Microsoft YaHei UI", 9.5F);
        DoubleBuffered = true;
        _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? TrayIconFactory.Create();
        Icon = _appIcon;

        _devicesSource.DataSource = _devices;
        _devicesGrid = BuildDevicesGrid();
        _deviceCountLabel = new Label
        {
            AutoSize = true,
            BackColor = AppTheme.AccentSoft,
            ForeColor = AppTheme.Accent,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Padding = new Padding(10, 5, 10, 5),
            Text = "0 台"
        };
        _selectedDeviceLabel = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextPrimary,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            Text = "等待选择设备"
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextSecondary,
            Font = new Font(Font.FontFamily, 9.5F),
            Text = "应用已启动，等待开始扫描设备。"
        };
        _statusDot = new StatusDot();
        _logTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = AppTheme.SurfaceSoft,
            ForeColor = AppTheme.TextSecondary,
            Font = new Font("Cascadia Mono", 9F)
        };

        _deviceStatusTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)DeviceStatusRefreshInterval.TotalMilliseconds
        };
        _deviceStatusTimer.Tick += async (_, _) => await RefreshDeviceStatusFromSystemAsync();

        var trayMenu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9.5F),
            ShowImageMargin = false
        };
        trayMenu.Items.Add(new ToolStripMenuItem("显示主窗口", null, (_, _) => ShowMainWindow()));
        trayMenu.Items.Add(new ToolStripSeparator());
        _trayQuickConnectItem = new ToolStripMenuItem("快速连接手机");
        _trayQuickConnectItem.Click += async (_, _) => await QuickConnectFromTrayAsync();
        trayMenu.Items.Add(_trayQuickConnectItem);
        trayMenu.Items.Add(new ToolStripMenuItem("刷新蓝牙设备", null, (_, _) => StartDeviceWatcher()));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("退出程序", null, (_, _) => ExitApplication()));
        trayMenu.Opening += TrayMenu_Opening;

        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = trayMenu,
            Icon = _appIcon,
            Text = "蓝牙音频中继",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        Controls.Add(BuildLayout());

        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
        FormClosed += MainForm_FormClosed;
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));

        var hero = new GradientCard
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 16, 28, 16),
            Margin = new Padding(0, 0, 0, 18)
        };
        var heroLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1
        };
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74));
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));

        var heroText = new FlowLayoutPanel
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        heroText.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 21F, FontStyle.Bold),
            Text = "蓝牙音频中继"
        });
        heroText.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(224, 239, 255),
            Font = new Font(Font.FontFamily, 10F),
            Margin = new Padding(2, 3, 0, 0),
            Text = "让手机声音经由电脑，从有线耳机自然播放。"
        });
        heroLayout.Controls.Add(heroText, 0, 0);

        var readyChip = new RoundedPanel
        {
            Anchor = AnchorStyles.Right,
            Size = new Size(152, 44),
            CornerRadius = 18,
            FillColor = Color.FromArgb(225, 245, 255),
            BorderColor = Color.FromArgb(220, 255, 255, 255),
            Padding = new Padding(10)
        };
        readyChip.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(20, 74, 125),
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "接收端已就绪"
        });
        heroLayout.Controls.Add(readyChip, 1, 0);
        hero.Controls.Add(heroLayout);
        root.Controls.Add(hero, 0, 0);

        var mainArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 18)
        };
        mainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        mainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        mainArea.Controls.Add(BuildDeviceCard(), 0, 0);
        mainArea.Controls.Add(BuildStatusCard(), 1, 0);
        root.Controls.Add(mainArea, 0, 1);

        root.Controls.Add(BuildLogCard(), 0, 2);
        return root;
    }

    private Control BuildDeviceCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            CornerRadius = 24,
            Padding = new Padding(20),
            Margin = new Padding(0, 0, 9, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));

        var titleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        titleRow.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.TextPrimary,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            Margin = new Padding(0, 7, 10, 0),
            Text = "可用设备"
        });
        titleRow.Controls.Add(_deviceCountLabel);
        header.Controls.Add(titleRow, 0, 0);

        var refreshButton = CreateButton("重新扫描", RefreshButton_Click);
        refreshButton.Dock = DockStyle.Fill;
        refreshButton.Margin = new Padding(0, 2, 0, 6);
        header.Controls.Add(refreshButton, 2, 0);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_devicesGrid, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildStatusCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            CornerRadius = 24,
            Padding = new Padding(22),
            Margin = new Padding(9, 0, 0, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 7
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.TextSecondary,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Text = "当前设备"
        }, 0, 0);

        _selectedDeviceLabel.Margin = new Padding(0, 8, 0, 14);
        layout.Controls.Add(_selectedDeviceLabel, 0, 1);

        var statusBox = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.SurfaceSoft,
            BorderColor = AppTheme.Border,
            CornerRadius = 16,
            Padding = new Padding(16, 13, 16, 13),
            Margin = new Padding(0, 0, 0, 14)
        };
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _statusDot.Anchor = AnchorStyles.Left;
        statusLayout.Controls.Add(_statusDot, 0, 0);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLayout.Controls.Add(_statusLabel, 1, 0);
        statusBox.Controls.Add(statusLayout);
        layout.Controls.Add(statusBox, 0, 2);

        layout.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "连接后，手机声音将从电脑播放。"
        }, 0, 3);

        var connectButton = CreateButton("快速连接", QuickConnectButton_Click, primary: true);
        connectButton.Dock = DockStyle.Fill;
        connectButton.Margin = new Padding(0, 4, 0, 6);
        layout.Controls.Add(connectButton, 0, 4);

        var stopButton = CreateButton("停止中继", StopRelayButton_Click);
        stopButton.Dock = DockStyle.Fill;
        stopButton.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(stopButton, 0, 5);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(126, 142, 163),
            Font = new Font(Font.FontFamily, 8F),
            Margin = new Padding(0, 10, 0, 0),
            Text = "蓝牙已配对 · Windows 默认输出已设置"
        }, 0, 6);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildLogCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            CornerRadius = 24,
            Padding = new Padding(20),
            Margin = new Padding(0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.TextPrimary,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            Text = "运行记录"
        }, 0, 0);

        var logHost = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.SurfaceSoft,
            BorderColor = AppTheme.Border,
            CornerRadius = 14,
            Padding = new Padding(14, 10, 8, 10)
        };
        logHost.Controls.Add(_logTextBox);
        layout.Controls.Add(logHost, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private DataGridView BuildDevicesGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = AppTheme.Surface,
            BorderStyle = BorderStyle.None,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = AppTheme.Border,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 44,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            DataSource = _devicesSource
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AppTheme.SurfaceSoft,
            ForeColor = AppTheme.TextSecondary,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Padding = new Padding(10, 0, 10, 0),
            SelectionBackColor = AppTheme.SurfaceSoft,
            SelectionForeColor = AppTheme.TextSecondary
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextPrimary,
            Font = new Font(Font.FontFamily, 10F),
            Padding = new Padding(10, 0, 10, 0),
            SelectionBackColor = AppTheme.AccentSoft,
            SelectionForeColor = AppTheme.TextPrimary
        };
        grid.RowTemplate.Height = 56;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RemoteAudioDevice.DisplayName),
            HeaderText = "设备",
            FillWeight = 48
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RemoteAudioDevice.EnableStateText),
            HeaderText = "启用状态",
            FillWeight = 22
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RemoteAudioDevice.ConnectionStateText),
            HeaderText = "连接状态",
            FillWeight = 30
        });

        grid.SelectionChanged += DevicesGrid_SelectionChanged;
        grid.CellFormatting += DevicesGrid_CellFormatting;
        return grid;
    }

    private static ModernButton CreateButton(string text, EventHandler onClick, bool primary = false)
    {
        var button = new ModernButton
        {
            Primary = primary,
            Text = text
        };
        button.Click += onClick;
        return button;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var cornerPreference = 2;
        DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));

        var borderColor = ColorTranslator.ToWin32(AppTheme.Border);
        var captionColor = ColorTranslator.ToWin32(AppTheme.Background);
        DwmSetWindowAttribute(Handle, DwmBorderColor, ref borderColor, sizeof(int));
        DwmSetWindowAttribute(Handle, DwmCaptionColor, ref captionColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        StartDeviceWatcher();
        _deviceStatusTimer.Start();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exitRequested || e.CloseReason == CloseReason.WindowsShutDown)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _trayIcon.Visible = true;

        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _trayIcon.BalloonTipTitle = "蓝牙音频中继仍在运行";
        _trayIcon.BalloonTipText = "右键托盘图标即可快速连接手机或退出程序。";
        _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(3000);
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void TrayMenu_Opening(object? sender, CancelEventArgs e)
    {
        var device = GetPreferredDevice();
        _trayQuickConnectItem.Enabled = device is not null;
        _trayQuickConnectItem.Text = device is null
            ? "快速连接手机（未发现设备）"
            : $"快速连接 {device.DisplayName}";
    }

    private async Task QuickConnectFromTrayAsync()
    {
        var device = GetPreferredDevice();
        if (device is null)
        {
            StartDeviceWatcher();
            _trayIcon.ShowBalloonTip(2500, "未发现可用设备", "正在重新扫描已配对的蓝牙手机。", ToolTipIcon.Info);
            return;
        }

        var connected = await OpenDeviceAsync(device, forceReconnect: true);
        _trayIcon.ShowBalloonTip(
            2600,
            connected ? "连接成功" : "连接失败",
            connected ? $"已重建并打开 {device.DisplayName} 的音频接收。" : "请打开主窗口查看运行记录。",
            connected ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private RemoteAudioDevice? GetPreferredDevice()
    {
        return GetSelectedDevice() ?? _devices.FirstOrDefault();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        _deviceStatusTimer.Stop();
        _deviceStatusTimer.Dispose();
        StopDeviceWatcher();

        foreach (var deviceId in _connections.Keys.ToList())
        {
            ReleaseConnection(deviceId, updateStatus: false);
        }

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _appIcon.Dispose();
    }

    private void DevicesGrid_SelectionChanged(object? sender, EventArgs e)
    {
        var device = GetSelectedDevice();
        _selectedDeviceLabel.Text = device is null
            ? "等待选择设备"
            : device.DisplayName;
    }

    private void DevicesGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.Value is not string value)
        {
            return;
        }

        var style = e.CellStyle;
        if (style is null)
        {
            return;
        }

        if (value is "已启用" or "正在播放")
        {
            style.ForeColor = AppTheme.Success;
            style.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        }
        else if (value.Contains("失败", StringComparison.Ordinal) || value.Contains("异常", StringComparison.Ordinal))
        {
            style.ForeColor = AppTheme.Danger;
            style.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        }
        else if (value is "正在连接")
        {
            style.ForeColor = AppTheme.Warning;
            style.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        }
        else if (value is "等待连接" or "未启用" or "已断开")
        {
            style.ForeColor = AppTheme.TextSecondary;
        }
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        StartDeviceWatcher();
    }

    private async void QuickConnectButton_Click(object? sender, EventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            UpdateStatus("请先在列表里选择一个设备。");
            return;
        }

        await OpenDeviceAsync(device, forceReconnect: true);
    }

    private async Task<bool> OpenDeviceAsync(RemoteAudioDevice device, bool forceReconnect = false)
    {
        if (forceReconnect)
        {
            UpdateStatus($"正在重建音频接收：{device.DisplayName}");
            ReleaseConnection(device.Id, updateStatus: false);
            device.ConnectionState = "正在连接";

            var audioProfileReset = await TryResetBluetoothAudioProfileAsync(device);
            await Task.Delay(audioProfileReset ? AudioProfileResetSettleDelay : QuickReconnectSettleDelay);

            var refreshedDevice = await RefreshAudioDeviceAsync(device);
            if (refreshedDevice is not null)
            {
                device = refreshedDevice;
            }
        }
        else if (_connections.TryGetValue(device.Id, out var existingConnection))
        {
            if (TryReadConnectionState(existingConnection, out var state, out var stateError))
            {
                device.ConnectionState = state.ToString();
                if (state == AudioPlaybackConnectionState.Opened)
                {
                    UpdateStatus($"音频接收已开启：{device.DisplayName}");
                    return true;
                }
            }
            else
            {
                AppendLog($"现有连接状态不可读，准备重建：{stateError}");
                ReleaseConnection(device.Id, updateStatus: false);
            }
        }

        var connection = await EnsureConnectionStartedAsync(device);
        if (connection is null)
        {
            return false;
        }

        try
        {
            var openResult = await connection.OpenAsync();
            if (!IsActiveConnection(device.Id, connection))
            {
                AppendLog($"打开操作已取消：{device.DisplayName} 已断开。");
                return false;
            }

            if (openResult.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                device.ConnectionState = "Opened";
                UpdateStatus($"已打开音频接收：{device.DisplayName}");
                AppendLog($"OpenAsync 成功：{device.DisplayName}");
                UpdateTrayText($"已连接 {device.DisplayName}");
                return true;
            }

            device.ConnectionState = $"打开失败：{openResult.Status}";
            UpdateStatus($"打开失败：{openResult.Status}");
            AppendLog($"OpenAsync 失败：{device.DisplayName}，状态 {openResult.Status}");
            if (forceReconnect)
            {
                ReleaseConnection(device.Id, updateStatus: false);
                device.ConnectionState = $"打开失败：{openResult.Status}";
            }

            return false;
        }
        catch (Exception ex)
        {
            if (IsActiveConnection(device.Id, connection))
            {
                ReleaseConnection(device.Id, updateStatus: false);
                device.ConnectionState = "打开异常";
            }

            UpdateStatus("打开音频连接时出现异常。");
            AppendLog($"OpenAsync 异常：{ex.Message}");
            return false;
        }
    }

    private void StopRelayButton_Click(object? sender, EventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            UpdateStatus("请先选择一个设备。");
            return;
        }

        ReleaseConnection(device.Id);
        device.ConnectionState = "Closed";
        UpdateStatus($"已停止中继：{device.DisplayName}");
    }

    private void StartDeviceWatcher()
    {
        StopDeviceWatcher();
        _devices.Clear();
        UpdateDeviceCount();

        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            _deviceWatcher = DeviceInformation.CreateWatcher(selector);
            _deviceWatcher.Added += DeviceWatcher_Added;
            _deviceWatcher.Removed += DeviceWatcher_Removed;
            _deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            _deviceWatcher.Stopped += DeviceWatcher_Stopped;
            _deviceWatcher.Start();

            UpdateStatus("正在扫描支持音频接收的蓝牙设备...");
            AppendLog("设备扫描已启动。");
        }
        catch (Exception ex)
        {
            StopDeviceWatcher();
            UpdateStatus("蓝牙设备扫描启动失败，请检查电脑蓝牙是否已开启。");
            AppendLog($"设备扫描异常：{ex.Message}");
        }
    }

    private void StopDeviceWatcher()
    {
        if (_deviceWatcher is null)
        {
            return;
        }

        var watcher = _deviceWatcher;
        _deviceWatcher = null;

        try
        {
            watcher.Added -= DeviceWatcher_Added;
            watcher.Removed -= DeviceWatcher_Removed;
            watcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;
            watcher.Stopped -= DeviceWatcher_Stopped;

            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"停止设备扫描时系统返回异常（已忽略）：{ex.Message}");
        }
    }

    private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        RunOnUiThread(() =>
        {
            var existing = _devices.FirstOrDefault(item => item.Id == deviceInfo.Id);
            if (existing is not null)
            {
                if (existing.ConnectionState == "未连接")
                {
                    existing.ConnectionState = "未启用";
                }

                return;
            }

            var device = new RemoteAudioDevice(deviceInfo.Id, deviceInfo.Name);
            _devices.Add(device);
            UpdateDeviceCount();
            AppendLog($"发现设备：{device.DisplayName}");
        });
    }

    private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceUpdate)
    {
        RunOnUiThread(() =>
        {
            ReleaseConnection(deviceUpdate.Id, updateStatus: false);

            var device = _devices.FirstOrDefault(item => item.Id == deviceUpdate.Id);
            if (device is not null)
            {
                device.IsEnabled = false;
                device.ConnectionState = "未连接";
                AppendLog($"设备已离线：{device.DisplayName}");
            }
        });
    }

    private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
    {
        RunOnUiThread(() =>
        {
            UpdateDeviceCount();
            UpdateStatus($"扫描完成 · 发现 {_devices.Count} 台设备");
            AppendLog("设备扫描完成。");
        });
    }

    private void DeviceWatcher_Stopped(DeviceWatcher sender, object args)
    {
        RunOnUiThread(() =>
        {
            AppendLog("设备扫描已停止。");
        });
    }

    private void Connection_StateChanged(AudioPlaybackConnection sender, object args)
    {
        string deviceId;
        AudioPlaybackConnectionState state;

        try
        {
            // Capture WinRT values before dispatching. The connection may be disposed
            // by a device-removal callback while this UI work is still queued.
            deviceId = sender.DeviceId;
            state = sender.State;
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => AppendLog($"连接状态回调已忽略：{ex.Message}"));
            return;
        }

        RunOnUiThread(() =>
        {
            if (!IsActiveConnection(deviceId, sender))
            {
                return;
            }

            var device = _devices.FirstOrDefault(item => item.Id == deviceId);
            if (device is null)
            {
                if (state == AudioPlaybackConnectionState.Closed)
                {
                    ReleaseConnection(deviceId, updateStatus: false);
                }

                return;
            }

            device.ConnectionState = state.ToString();
            AppendLog($"连接状态变更：{device.DisplayName} -> {state}");

            if (state == AudioPlaybackConnectionState.Opened)
            {
                UpdateTrayText($"已连接 {device.DisplayName}");
            }
            else if (state == AudioPlaybackConnectionState.Closed)
            {
                UpdateTrayText("等待连接");
            }

            if (GetSelectedDevice()?.Id == deviceId)
            {
                UpdateStatus(state == AudioPlaybackConnectionState.Closed
                    ? $"设备已断开：{device.DisplayName}，可在蓝牙恢复后重新连接。"
                    : $"当前连接状态：{device.DisplayName} -> {state}");
            }

            if (state == AudioPlaybackConnectionState.Closed)
            {
                ReleaseConnection(deviceId, updateStatus: false);
            }
        });
    }

    private async Task RefreshDeviceStatusFromSystemAsync()
    {
        if (_isDeviceStatusRefreshRunning || IsDisposed || Disposing)
        {
            return;
        }

        _isDeviceStatusRefreshRunning = true;
        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            var deviceInfos = await DeviceInformation.FindAllAsync(selector);
            var snapshot = deviceInfos.Select(info => (info.Id, info.Name)).ToList();
            RunOnUiThread(() => SyncDeviceStatusSnapshot(snapshot));
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => AppendLog($"同步设备状态失败（已忽略）：{ex.Message}"));
        }
        finally
        {
            _isDeviceStatusRefreshRunning = false;
        }
    }

    private void SyncDeviceStatusSnapshot(List<(string Id, string Name)> snapshot)
    {
        var now = DateTime.UtcNow;

        foreach (var info in snapshot)
        {
            var existing = _devices.FirstOrDefault(item => item.Id == info.Id) ??
                           _devices.FirstOrDefault(item => DeviceNameMatches(item.DisplayName, info.Name));
            if (existing is not null)
            {
                if (existing.ConnectionState == "未连接")
                {
                    existing.ConnectionState = "未启用";
                }

                continue;
            }

            _devices.Add(new RemoteAudioDevice(info.Id, info.Name));
        }

        foreach (var device in _devices.ToList())
        {
            var isAvailable = snapshot.Any(info => info.Id == device.Id || DeviceNameMatches(info.Name, device.DisplayName));
            if (!isAvailable)
            {
                if (_connections.ContainsKey(device.Id))
                {
                    ReleaseConnection(device.Id, updateStatus: false);
                }

                device.IsEnabled = false;
                if (device.ConnectionState != "未连接")
                {
                    device.ConnectionState = "未连接";
                    AppendLog($"设备状态同步：{device.DisplayName} 已离线。");
                }

                continue;
            }

            if (IsStaleConnectingState(device, now))
            {
                ReleaseConnection(device.Id, updateStatus: false);
                device.ConnectionState = "未连接";
                AppendLog($"设备状态同步：{device.DisplayName} 连接超时，已重置为未连接。");
            }
        }

        UpdateActiveConnectionStates();
        UpdateDeviceCount();
    }

    private void UpdateActiveConnectionStates()
    {
        foreach (var pair in _connections.ToList())
        {
            var device = _devices.FirstOrDefault(item => item.Id == pair.Key);
            if (!TryReadConnectionState(pair.Value, out var state, out var error))
            {
                AppendLog($"设备状态同步：连接状态不可读，已释放：{error}");
                ReleaseConnection(pair.Key, updateStatus: false);
                if (device is not null)
                {
                    device.ConnectionState = "未连接";
                }

                continue;
            }

            if (state == AudioPlaybackConnectionState.Closed)
            {
                ReleaseConnection(pair.Key, updateStatus: false);
                if (device is not null)
                {
                    device.ConnectionState = "未连接";
                }

                continue;
            }

            if (device is not null)
            {
                device.ConnectionState = state.ToString();
            }
        }
    }

    private static bool IsStaleConnectingState(RemoteAudioDevice device, DateTime now)
    {
        return device.ConnectionState is "Opening" or "正在连接" &&
               now - device.StateUpdatedAt > ConnectingStateTimeout;
    }

    private async Task<bool> TryResetBluetoothAudioProfileAsync(RemoteAudioDevice device)
    {
        try
        {
            AppendLog($"正在重置蓝牙音频服务：{device.DisplayName}");
            var result = await Task.Run(() => BluetoothProfileReset.TryResetAudioSourceService(device.DisplayName));
            AppendLog(result.Message);
            return result.Success;
        }
        catch (Exception ex)
        {
            AppendLog($"蓝牙音频服务重置异常（已回退普通重连）：{ex.Message}");
            return false;
        }
    }

    private async Task<RemoteAudioDevice?> RefreshAudioDeviceAsync(RemoteAudioDevice previousDevice)
    {
        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            var deviceInfos = await DeviceInformation.FindAllAsync(selector);
            var matchedInfo = deviceInfos.FirstOrDefault(info => info.Id == previousDevice.Id) ??
                              deviceInfos.FirstOrDefault(info => DeviceNameMatches(info.Name, previousDevice.DisplayName));

            if (matchedInfo is null)
            {
                AppendLog($"刷新后未找到音频接收设备：{previousDevice.DisplayName}");
                return null;
            }

            var existingDevice = _devices.FirstOrDefault(item => item.Id == matchedInfo.Id);
            if (existingDevice is not null)
            {
                return existingDevice;
            }

            var refreshedDevice = new RemoteAudioDevice(matchedInfo.Id, matchedInfo.Name);
            var previousIndex = _devices.IndexOf(previousDevice);
            if (previousIndex >= 0)
            {
                _devices.RemoveAt(previousIndex);
                _devices.Insert(previousIndex, refreshedDevice);
            }
            else
            {
                _devices.Add(refreshedDevice);
            }

            UpdateDeviceCount();
            AppendLog($"刷新后更新音频接收设备：{refreshedDevice.DisplayName}");
            return refreshedDevice;
        }
        catch (Exception ex)
        {
            AppendLog($"刷新音频接收设备失败（已继续重连）：{ex.Message}");
            return null;
        }
    }

    private static bool DeviceNameMatches(string? candidate, string target)
    {
        var normalizedCandidate = NormalizeDeviceName(candidate);
        var normalizedTarget = NormalizeDeviceName(target);
        return normalizedCandidate.Length > 0 &&
               (normalizedCandidate == normalizedTarget ||
                normalizedCandidate.Contains(normalizedTarget, StringComparison.Ordinal) ||
                normalizedTarget.Contains(normalizedCandidate, StringComparison.Ordinal));
    }

    private static string NormalizeDeviceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(static ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
    }

    private async Task<AudioPlaybackConnection?> EnsureConnectionStartedAsync(RemoteAudioDevice device)
    {
        if (_connections.TryGetValue(device.Id, out var existingConnection))
        {
            if (!TryReadConnectionState(existingConnection, out var state, out var stateError))
            {
                AppendLog($"现有连接状态不可读，已丢弃并重建：{stateError}");
                ReleaseConnection(device.Id, updateStatus: false);
            }
            else
            {
                device.ConnectionState = state.ToString();
                device.IsEnabled = true;
                UpdateStatus($"连接已存在：{device.DisplayName}");
                return existingConnection;
            }
        }

        AudioPlaybackConnection? connection;
        try
        {
            connection = AudioPlaybackConnection.TryCreateFromId(device.Id);
        }
        catch (Exception ex)
        {
            UpdateStatus("创建音频接收连接失败。");
            AppendLog($"TryCreateFromId 异常：{ex.Message}");
            return null;
        }

        if (connection is null)
        {
            UpdateStatus("系统未能为该设备创建音频接收连接。");
            AppendLog($"TryCreateFromId 返回空值：{device.DisplayName}");
            return null;
        }

        connection.StateChanged += Connection_StateChanged;
        _connections[device.Id] = connection;

        try
        {
            await connection.StartAsync();
            if (!IsActiveConnection(device.Id, connection))
            {
                AppendLog($"启用操作已取消：{device.DisplayName} 已断开。");
                return null;
            }

            if (!TryReadConnectionState(connection, out var state, out var stateError))
            {
                AppendLog($"StartAsync 后连接状态不可读：{stateError}");
                ReleaseConnection(device.Id, updateStatus: false);
                return null;
            }

            device.IsEnabled = true;
            device.ConnectionState = state.ToString();
            UpdateStatus($"已启用音频接收：{device.DisplayName}");
            AppendLog($"StartAsync 成功：{device.DisplayName}");
            return connection;
        }
        catch (Exception ex)
        {
            AppendLog($"StartAsync 异常：{ex.Message}");
            ReleaseConnection(device.Id, updateStatus: false);
            UpdateStatus("启用音频接收失败。");
            return null;
        }
    }

    private void ReleaseConnection(string deviceId, bool updateStatus = true)
    {
        if (!_connections.TryGetValue(deviceId, out var connection))
        {
            return;
        }

        // Remove first so already queued StateChanged callbacks become harmless.
        _connections.Remove(deviceId);

        try
        {
            connection.StateChanged -= Connection_StateChanged;
        }
        catch (Exception ex)
        {
            AppendLog($"取消连接状态监听时系统返回异常（已忽略）：{ex.Message}");
        }


        try
        {
            connection.Dispose();
        }
        catch (Exception ex)
        {
            AppendLog($"释放蓝牙连接时系统返回异常（已忽略）：{ex.Message}");
        }

        UpdateTrayText("等待连接");

        var device = _devices.FirstOrDefault(item => item.Id == deviceId);
        if (device is not null)
        {
            device.IsEnabled = false;
            if (device.ConnectionState != "Closed")
            {
                device.ConnectionState = "未启用";
            }
            AppendLog($"已释放连接：{device.DisplayName}");
        }

        if (updateStatus)
        {
            UpdateStatus("连接资源已释放。");
        }
    }

    private bool IsActiveConnection(string deviceId, AudioPlaybackConnection connection)
    {
        return _connections.TryGetValue(deviceId, out var activeConnection) &&
               ReferenceEquals(activeConnection, connection);
    }

    private static bool TryReadConnectionState(
        AudioPlaybackConnection connection,
        out AudioPlaybackConnectionState state,
        out string? error)
    {
        try
        {
            state = connection.State;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            state = AudioPlaybackConnectionState.Closed;
            error = ex.Message;
            return false;
        }
    }

    private RemoteAudioDevice? GetSelectedDevice()
    {
        return _devicesGrid.CurrentRow?.DataBoundItem as RemoteAudioDevice;
    }

    private void UpdateStatus(string message)
    {
        _statusLabel.Text = message;

        _statusDot.DotColor = message.Contains("失败", StringComparison.Ordinal) ||
                              message.Contains("异常", StringComparison.Ordinal)
            ? AppTheme.Danger
            : message.Contains("已打开", StringComparison.Ordinal) ||
              message.Contains("成功", StringComparison.Ordinal)
                ? AppTheme.Success
                : message.Contains("关闭", StringComparison.Ordinal) ||
                  message.Contains("释放", StringComparison.Ordinal)
                    ? AppTheme.TextSecondary
                    : AppTheme.Accent;
        _statusDot.Invalidate();
    }

    private void UpdateTrayText(string status)
    {
        var text = $"蓝牙音频中继 - {status}";
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void UpdateDeviceCount()
    {
        _deviceCountLabel.Text = $"{_devices.Count} 台";
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _logTextBox.Text = line + _logTextBox.Text;
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => ExecuteUiCallback(action)));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
                // The window handle was destroyed while the system event arrived.
            }

            return;
        }

        ExecuteUiCallback(action);
    }

    private void ExecuteUiCallback(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                AppendLog($"后台蓝牙事件异常已安全处理：{ex.Message}");
            }
            catch
            {
            }
        }
    }
}
