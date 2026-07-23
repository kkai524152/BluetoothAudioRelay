using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BluetoothAudioRelay;

internal static class Program
{
    internal static readonly int ShowMainWindowMessage = RegisterWindowMessage("BluetoothAudioRelay.ShowMainWindow.v1");
    internal static readonly int ExitApplicationMessage = RegisterWindowMessage("BluetoothAudioRelay.ExitApplication.v1");

    [STAThread]
    private static void Main(string[] args)
    {
        DiagnosticLog.Initialize();
        var shutdownRequested = args.Any(arg => arg.Equals("--shutdown", StringComparison.OrdinalIgnoreCase));
        var startInBackground = args.Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase));
        using var singleInstance = new Mutex(true, @"Local\BluetoothAudioRelay.Singleton", out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (shutdownRequested || !startInBackground)
            {
                PostMessage(
                    new IntPtr(0xffff),
                    shutdownRequested ? ExitApplicationMessage : ShowMainWindowMessage,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }

            return;
        }

        if (shutdownRequested)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
            DiagnosticLog.WriteException("UI 未处理异常", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                DiagnosticLog.WriteException("进程未处理异常", exception);
            }
        };

        Application.Run(new MainForm(startInBackground));
        GC.KeepAlive(singleInstance);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

public sealed class MainForm : Form
{
    private enum StatusKind
    {
        Neutral,
        Progress,
        Success,
        Error
    }

    private sealed record ConnectionAttemptResult(
        bool Success,
        RemoteAudioDevice Device,
        string? FailureReason);

    private const int DwmWindowCornerPreference = 33;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private static readonly TimeSpan DirectReconnectDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan AudioProfileResetSettleDelay = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan AudioProfileEndpointPollInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan AudioProfileEndpointRefreshTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DeviceStatusRefreshInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ConnectingStateTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ConnectionStartTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ConnectionOpenTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan InitialAutoConnectDelay = TimeSpan.FromMilliseconds(700);
    private const int DirectConnectAttempts = 2;
    private const int RecoveryConnectAttempts = 2;

    private readonly Dictionary<string, AudioPlaybackConnection> _connections = new();
    private readonly BindingSource _devicesSource = new();
    private readonly BindingList<RemoteAudioDevice> _devices = new();
    private readonly DataGridView _devicesGrid;
    private readonly Label _deviceCountLabel;
    private readonly Label _selectedDeviceLabel;
    private readonly Label _statusLabel;
    private readonly StatusDot _statusDot;
    private readonly ThemedLogBox _logTextBox;
    private readonly ThemedSelectButton _themeModeButton;
    private readonly ThemedSelectButton _accentButton;
    private readonly ThemedSelectButton _behaviorButton;
    private readonly Label _outputDeviceLabel;
    private readonly Icon _appIcon;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayQuickConnectItem;
    private readonly ToolStripMenuItem _trayDevicesItem;
    private readonly System.Windows.Forms.Timer _deviceStatusTimer;
    private readonly System.Windows.Forms.Timer _themeSyncTimer;
    private readonly UserPreferences _preferences;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly HashSet<string> _profileRecoveryRequiredDeviceKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly AutoReconnectBudget _autoReconnectBudget = new();
    private readonly bool _startInBackground;
    private AudioOutputMonitor? _audioOutputMonitor;
    private CancellationTokenSource? _connectionOperationCancellation;
    private CancellationTokenSource? _autoReconnectCancellation;
    private DeviceWatcher? _deviceWatcher;
    private bool _exitRequested;
    private bool _trayHintShown;
    private bool _isDeviceStatusRefreshRunning;
    private bool _isConnectionOperationRunning;
    private bool _autoConnectSuppressed;
    private string _defaultOutputName = "正在检测默认输出...";

    public MainForm(bool startInBackground = false)
    {
        _startInBackground = startInBackground;
        _preferences = UserPreferencesStore.Load();
        if (_preferences.PreferredDeviceNeedsProfileRecovery &&
            !string.IsNullOrWhiteSpace(_preferences.PreferredDeviceKey))
        {
            _profileRecoveryRequiredDeviceKeys.Add(_preferences.PreferredDeviceKey);
        }

        _preferences.StartWithWindows = StartupRegistration.IsEnabled();
        AppTheme.Apply(
            ThemeResolver.ResolveDarkMode(_preferences.ThemePreference),
            AccentPalettes.Find(_preferences.AccentKey));

        Text = "蓝牙音频中继 · Bluetooth Audio Relay";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 760);
        Size = new Size(1240, 860);
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
            Text = "0 台",
            Tag = "accent-chip"
        };
        _selectedDeviceLabel = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextPrimary,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            Text = "等待选择设备",
            Tag = "primary"
        };
        _statusLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = AppTheme.TextSecondary,
            Font = new Font(Font.FontFamily, 9.5F),
            Text = "应用已启动，等待开始扫描设备。",
            Tag = "secondary"
        };
        _statusDot = new StatusDot();
        _outputDeviceLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"默认输出 · {_defaultOutputName}",
            Tag = "secondary"
        };
        _logTextBox = new ThemedLogBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Cascadia Mono", 9F)
        };
        _themeModeButton = BuildThemeModeButton();
        _accentButton = BuildAccentButton();
        _behaviorButton = BuildBehaviorButton();

        _deviceStatusTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)DeviceStatusRefreshInterval.TotalMilliseconds
        };
        _deviceStatusTimer.Tick += async (_, _) => await RefreshDeviceStatusFromSystemAsync();
        _themeSyncTimer = new System.Windows.Forms.Timer
        {
            Interval = 60_000
        };
        _themeSyncTimer.Tick += (_, _) => ApplyThemeFromPreferences(save: false);

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
        _trayDevicesItem = new ToolStripMenuItem("选择手机");
        trayMenu.Items.Add(_trayDevicesItem);
        trayMenu.Items.Add(new ToolStripMenuItem("刷新蓝牙设备", null, (_, _) => StartDeviceWatcher()));
        trayMenu.Items.Add(new ToolStripMenuItem("打开声音设置", null, (_, _) => OpenSoundSettings()));
        trayMenu.Items.Add(new ToolStripMenuItem("检查更新", null, async (_, _) => await CheckForUpdatesAsync(manual: true)));
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

        try
        {
            _audioOutputMonitor = new AudioOutputMonitor();
            _audioOutputMonitor.DefaultOutputChanged += AudioOutputMonitor_DefaultOutputChanged;
            RefreshDefaultOutput();
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("默认输出监听初始化失败", ex);
            _defaultOutputName = "检测不可用";
            _outputDeviceLabel.Text = $"默认输出 · {_defaultOutputName}";
        }

        Controls.Add(BuildLayout());
        ApplyThemeFromPreferences(save: false);

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
            RowCount = 3,
            Tag = "background"
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));

        var hero = new GradientCard
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(30, 14, 30, 14),
            Margin = new Padding(0, 0, 0, 18)
        };
        var heroLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2
        };
        heroLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        heroLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heroText = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        heroText.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        heroText.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var title = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.Accent,
            Font = new Font(Font.FontFamily, 23F, FontStyle.Bold),
            Text = "Bluetooth Audio Relay",
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = "accent"
        };
        heroText.Controls.Add(title, 0, 0);
        var subtitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.TextSecondary,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Italic),
            Text = "手机音频经由电脑中继，从默认输出设备自然播放。",
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = "secondary"
        };
        heroText.Controls.Add(subtitle, 0, 1);
        var settingsRail = new RoundedPanel
        {
            Size = new Size(570, 50),
            Anchor = AnchorStyles.Top,
            CornerRadius = 20,
            ThemeRole = "surface-soft",
            BorderWidth = 0,
            Padding = new Padding(8, 7, 8, 7),
            Margin = new Padding(0)
        };
        var selectRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _themeModeButton.Size = new Size(184, 36);
        _accentButton.Size = new Size(158, 36);
        _behaviorButton.Size = new Size(184, 36);
        _themeModeButton.Margin = new Padding(0, 0, 12, 0);
        _behaviorButton.Margin = new Padding(0, 0, 12, 0);
        _accentButton.Margin = new Padding(0);
        selectRow.Controls.Add(_behaviorButton);
        selectRow.Controls.Add(_themeModeButton);
        selectRow.Controls.Add(_accentButton);
        settingsRail.Controls.Add(selectRow);

        heroLayout.Controls.Add(heroText, 0, 0);
        heroLayout.Controls.Add(settingsRail, 0, 1);
        hero.Controls.Add(heroLayout);
        root.Controls.Add(hero, 0, 0);

        var mainArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 18),
            Tag = "background"
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
            Margin = new Padding(0, 0, 9, 0),
            ThemeRole = "surface"
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
            Text = "可用设备",
            Tag = "primary"
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
            Margin = new Padding(9, 0, 0, 0),
            ThemeRole = "surface"
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.TextSecondary,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Text = "当前设备",
            Tag = "secondary"
        }, 0, 0);

        _selectedDeviceLabel.Margin = new Padding(0, 8, 0, 14);
        layout.Controls.Add(_selectedDeviceLabel, 0, 1);

        var statusBox = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.SurfaceSoft,
            BorderColor = AppTheme.Border,
            CornerRadius = 16,
            Padding = new Padding(16, 8, 16, 8),
            Margin = new Padding(0, 0, 0, 10),
            ThemeRole = "surface-soft"
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

        layout.Controls.Add(_outputDeviceLabel, 0, 3);

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
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 24,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(126, 142, 163),
            Font = new Font(Font.FontFamily, 8F),
            Margin = new Padding(0, 10, 0, 0),
            Text = "蓝牙已配对 · Windows 默认输出已设置",
            Tag = "muted"
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
            Margin = new Padding(0),
            ThemeRole = "surface"
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
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        header.Controls.Add(new Label
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = AppTheme.TextPrimary,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            Text = "运行记录",
            Tag = "primary"
        }, 0, 0);

        var soundSettingsButton = CreateButton("声音设置", (_, _) => OpenSoundSettings());
        soundSettingsButton.Dock = DockStyle.Fill;
        soundSettingsButton.Margin = new Padding(0, 0, 8, 5);
        header.Controls.Add(soundSettingsButton, 1, 0);

        var diagnosticsButton = CreateButton("导出诊断", ExportDiagnosticsButton_Click);
        diagnosticsButton.Dock = DockStyle.Fill;
        diagnosticsButton.Margin = new Padding(0, 0, 0, 5);
        header.Controls.Add(diagnosticsButton, 2, 0);
        layout.Controls.Add(header, 0, 0);

        var logHost = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.SurfaceSoft,
            BorderColor = AppTheme.Border,
            CornerRadius = 14,
            Padding = new Padding(14, 10, 8, 10),
            ThemeRole = "surface-soft"
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
            SelectionBackColor = AppTheme.SelectedRow,
            SelectionForeColor = AppTheme.TextPrimary
        };
        grid.RowsDefaultCellStyle.SelectionBackColor = AppTheme.SelectedRow;
        grid.RowsDefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = AppTheme.SelectedRow;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;
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

    private ThemedSelectButton BuildThemeModeButton()
    {
        var button = BuildSelectButton(GetThemePreferenceText());
        button.Click += (_, _) =>
        {
            var menu = BuildThemedMenu();
            AddThemeMenuItem(menu, "跟随 Windows", ThemePreference.System);
            AddThemeMenuItem(menu, "日出日落", ThemePreference.SunCycle);
            AddThemeMenuItem(menu, "浅色", ThemePreference.Light);
            AddThemeMenuItem(menu, "深色", ThemePreference.Dark);
            menu.Show(button, new Point(0, button.Height + 4));
        };
        return button;
    }

    private ThemedSelectButton BuildAccentButton()
    {
        var button = BuildSelectButton(AccentPalettes.Find(_preferences.AccentKey).DisplayName);
        button.Click += (_, _) =>
        {
            var menu = BuildThemedMenu();
            foreach (var palette in AccentPalettes.All)
            {
                var item = new ToolStripMenuItem(palette.DisplayName)
                {
                    Checked = palette.Key.Equals(_preferences.AccentKey, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += (_, _) =>
                {
                    _preferences.AccentKey = palette.Key;
                    ApplyThemeFromPreferences();
                };
                menu.Items.Add(item);
            }

            menu.Show(button, new Point(0, button.Height + 4));
        };
        return button;
    }

    private ThemedSelectButton BuildBehaviorButton()
    {
        var button = BuildSelectButton(GetBehaviorButtonText());
        button.Click += (_, _) =>
        {
            var menu = BuildThemedMenu();
            var autoConnectItem = new ToolStripMenuItem("自动连接首选手机")
            {
                Checked = _preferences.AutoConnectEnabled
            };
            autoConnectItem.Click += (_, _) =>
            {
                _preferences.AutoConnectEnabled = !_preferences.AutoConnectEnabled;
                _autoConnectSuppressed = false;
                if (!_preferences.AutoConnectEnabled)
                {
                    CancelAutoReconnect();
                }
                else
                {
                    ResetAutoReconnectBudget();
                }

                SavePreferences();
                if (_preferences.AutoConnectEnabled)
                {
                    ScheduleAutoConnect(InitialAutoConnectDelay);
                }
            };
            menu.Items.Add(autoConnectItem);

            var startupItem = new ToolStripMenuItem("开机后在后台启动")
            {
                Checked = _preferences.StartWithWindows
            };
            startupItem.Click += (_, _) => ToggleStartWithWindows();
            menu.Items.Add(startupItem);

            var quietItem = new ToolStripMenuItem("静默通知")
            {
                Checked = _preferences.QuietNotifications
            };
            quietItem.Click += (_, _) =>
            {
                _preferences.QuietNotifications = !_preferences.QuietNotifications;
                SavePreferences();
            };
            menu.Items.Add(quietItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("检查更新", null, async (_, _) => await CheckForUpdatesAsync(manual: true)));
            menu.Items.Add(new ToolStripMenuItem("打开日志目录", null, (_, _) => OpenLogDirectory()));
            menu.Show(button, new Point(0, button.Height + 4));
        };
        return button;
    }

    private static ThemedSelectButton BuildSelectButton(string text)
    {
        return new ThemedSelectButton
        {
            Text = text,
            Size = new Size(150, 30)
        };
    }

    private void AddThemeMenuItem(ContextMenuStrip menu, string text, ThemePreference preference)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = _preferences.ThemePreference == preference
        };
        item.Click += (_, _) =>
        {
            _preferences.ThemePreference = preference;
            ApplyThemeFromPreferences();
        };
        menu.Items.Add(item);
    }

    private static ContextMenuStrip BuildThemedMenu()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextPrimary,
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new ThemedMenuColorTable())
        };
        return menu;
    }

    private string GetThemePreferenceText()
    {
        return _preferences.ThemePreference switch
        {
            ThemePreference.SunCycle => "日出日落",
            ThemePreference.Light => "浅色",
            ThemePreference.Dark => "深色",
            _ => "跟随 Windows"
        };
    }

    private string GetBehaviorButtonText()
    {
        return _preferences.AutoConnectEnabled ? "自动连接 · 开" : "自动连接 · 关";
    }

    private void SavePreferences()
    {
        UserPreferencesStore.Save(_preferences);
        _behaviorButton.Text = GetBehaviorButtonText();
    }

    private void ToggleStartWithWindows()
    {
        var enabled = !_preferences.StartWithWindows;
        if (!StartupRegistration.TrySetEnabled(enabled, out var error))
        {
            UpdateStatus($"开机启动设置失败：{error}", StatusKind.Error);
            AppendLog($"开机启动设置失败：{error}");
            return;
        }

        _preferences.StartWithWindows = enabled;
        SavePreferences();
        UpdateStatus(enabled ? "已开启开机后台启动。" : "已关闭开机后台启动。", StatusKind.Neutral);
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

    private void ApplyThemeFromPreferences(bool save = true)
    {
        AppTheme.Apply(
            ThemeResolver.ResolveDarkMode(_preferences.ThemePreference),
            AccentPalettes.Find(_preferences.AccentKey));

        if (save)
        {
            UserPreferencesStore.Save(_preferences);
        }

        _themeModeButton.Text = GetThemePreferenceText();
        _accentButton.Text = AccentPalettes.Find(_preferences.AccentKey).DisplayName;
        _behaviorButton.Text = GetBehaviorButtonText();
        BackColor = AppTheme.Background;
        ApplyWindowChrome();
        ApplyThemeToControl(this);
        ConfigureDevicesGridTheme();
        Invalidate(true);
    }

    private void ApplyThemeToControl(Control control)
    {
        if (Equals(control.Tag, "background"))
        {
            control.BackColor = AppTheme.Background;
        }

        switch (control)
        {
            case RoundedPanel roundedPanel:
                roundedPanel.FillColor = roundedPanel.ThemeRole switch
                {
                    "shell" => AppTheme.Shell,
                    "surface-soft" => AppTheme.SurfaceSoft,
                    "accent-soft" => AppTheme.AccentSoft,
                    _ => AppTheme.Surface
                };
                roundedPanel.BorderColor = AppTheme.Border;
                roundedPanel.Invalidate();
                break;
            case GradientCard gradientCard:
                gradientCard.Invalidate();
                break;
            case ModernButton modernButton:
                modernButton.Invalidate();
                break;
            case ThemedSelectButton selectButton:
                selectButton.Invalidate();
                break;
            case ThemedLogBox logBox:
                logBox.Invalidate();
                break;
            case Label label:
                label.ForeColor = label.Tag switch
                {
                    "accent" => AppTheme.Accent,
                    "accent-text" => AppTheme.AccentText,
                    "secondary" => AppTheme.TextSecondary,
                    "muted" => AppTheme.TextMuted,
                    "on-accent" => Color.White,
                    "accent-chip" => AppTheme.Accent,
                    _ => AppTheme.TextPrimary
                };

                if (Equals(label.Tag, "accent-chip"))
                {
                    label.BackColor = AppTheme.AccentSoft;
                }

                break;
            case DataGridView:
                ConfigureDevicesGridTheme();
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyThemeToControl(child);
        }
    }

    private void ConfigureDevicesGridTheme()
    {
        _devicesGrid.BackgroundColor = AppTheme.Surface;
        _devicesGrid.GridColor = AppTheme.Border;
        _devicesGrid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.SurfaceSoft;
        _devicesGrid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.TextSecondary;
        _devicesGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = AppTheme.SurfaceSoft;
        _devicesGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = AppTheme.TextSecondary;
        _devicesGrid.DefaultCellStyle.BackColor = AppTheme.Surface;
        _devicesGrid.DefaultCellStyle.ForeColor = AppTheme.TextPrimary;
        _devicesGrid.DefaultCellStyle.SelectionBackColor = AppTheme.SelectedRow;
        _devicesGrid.DefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;
        _devicesGrid.RowsDefaultCellStyle.BackColor = AppTheme.Surface;
        _devicesGrid.RowsDefaultCellStyle.SelectionBackColor = AppTheme.SelectedRow;
        _devicesGrid.RowsDefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;
        _devicesGrid.AlternatingRowsDefaultCellStyle.BackColor = AppTheme.Surface;
        _devicesGrid.AlternatingRowsDefaultCellStyle.SelectionBackColor = AppTheme.SelectedRow;
        _devicesGrid.AlternatingRowsDefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;
        foreach (DataGridViewRow row in _devicesGrid.Rows)
        {
            row.DefaultCellStyle.BackColor = AppTheme.Surface;
            row.DefaultCellStyle.ForeColor = AppTheme.TextPrimary;
            row.DefaultCellStyle.SelectionBackColor = AppTheme.SelectedRow;
            row.DefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;
        }

        _devicesGrid.Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowChrome();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Program.ShowMainWindowMessage)
        {
            ShowMainWindow();
            return;
        }

        if (message.Msg == Program.ExitApplicationMessage)
        {
            ExitApplication();
            return;
        }

        base.WndProc(ref message);
    }

    private void ApplyWindowChrome()
    {
        if (!IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var cornerPreference = 2;
        DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));
        var darkMode = AppTheme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));

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
        _themeSyncTimer.Start();

        if (_startInBackground)
        {
            BeginInvoke(new Action(HideToTray));
        }

        _ = CheckForUpdatesAsync(manual: false);
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

        if (_trayHintShown || _preferences.QuietNotifications || _startInBackground)
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

        _trayDevicesItem.DropDownItems.Clear();
        foreach (var candidate in _devices.Where(item => item.IsAvailable).OrderBy(item => item.DisplayName))
        {
            var item = new ToolStripMenuItem(candidate.DisplayName)
            {
                Checked = BluetoothDeviceIdentity.MatchesPreference(
                    candidate,
                    _preferences.PreferredDeviceKey,
                    _preferences.PreferredDeviceName)
            };
            item.Click += async (_, _) =>
            {
                SelectDevice(candidate);
                await OpenDeviceAsync(candidate, allowProfileRecovery: true, automatic: false);
            };
            _trayDevicesItem.DropDownItems.Add(item);
        }

        _trayDevicesItem.Enabled = _trayDevicesItem.DropDownItems.Count > 0;
    }

    private async Task QuickConnectFromTrayAsync()
    {
        var device = GetPreferredDevice();
        if (device is null)
        {
            StartDeviceWatcher();
            ShowTrayNotification("未发现可用设备", "正在重新扫描已配对的蓝牙手机。", ToolTipIcon.Info);
            return;
        }

        var connected = await OpenDeviceAsync(device, allowProfileRecovery: true, automatic: false);
        ShowTrayNotification(
            connected ? "连接成功" : "连接失败",
            connected ? $"已打开 {device.DisplayName} 的音频接收。" : "请打开主窗口查看运行记录。",
            connected ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private RemoteAudioDevice? GetPreferredDevice()
    {
        return _devices.FirstOrDefault(device =>
                   device.IsAvailable &&
                   BluetoothDeviceIdentity.MatchesPreference(
                       device,
                       _preferences.PreferredDeviceKey,
                       _preferences.PreferredDeviceName)) ??
               (GetSelectedDevice() is { IsAvailable: true } selected ? selected : null) ??
               _devices.FirstOrDefault(device => device.IsAvailable);
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        _lifetimeCancellation.Cancel();
        CancelConnectionOperation();
        CancelAutoReconnect();
        _deviceStatusTimer.Stop();
        _deviceStatusTimer.Dispose();
        _themeSyncTimer.Stop();
        _themeSyncTimer.Dispose();
        StopDeviceWatcher();

        foreach (var deviceId in _connections.Keys.ToList())
        {
            ReleaseConnection(deviceId, updateStatus: false);
        }

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _appIcon.Dispose();
        if (_audioOutputMonitor is not null)
        {
            _audioOutputMonitor.DefaultOutputChanged -= AudioOutputMonitor_DefaultOutputChanged;
            _audioOutputMonitor.Dispose();
        }

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

        style.SelectionBackColor = AppTheme.SelectedRow;
        style.SelectionForeColor = AppTheme.TextPrimary;

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
        else if (value is "正在连接" or "正在启用")
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
        if (device is null || !device.IsAvailable)
        {
            UpdateStatus("请先选择一台在线设备。", StatusKind.Neutral);
            return;
        }

        await OpenDeviceAsync(device, allowProfileRecovery: true, automatic: false);
    }

    private async Task<bool> OpenDeviceAsync(
        RemoteAudioDevice device,
        bool allowProfileRecovery,
        bool automatic)
    {
        if (_exitRequested)
        {
            return false;
        }

        if (!device.IsAvailable)
        {
            if (automatic &&
                !_autoConnectSuppressed &&
                !_lifetimeCancellation.IsCancellationRequested)
            {
                RecordAutoReconnectFailure(device);
            }

            return false;
        }

        if (automatic &&
            (!_preferences.AutoConnectEnabled ||
             _autoConnectSuppressed ||
             _autoReconnectBudget.IsExhausted ||
             _isConnectionOperationRunning))
        {
            return false;
        }

        if (!automatic)
        {
            ResetAutoReconnectBudget(device, "用户发起快速连接");
            _autoConnectSuppressed = false;
            CancelAutoReconnect();
            CancelConnectionOperation();
        }

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _connectionOperationCancellation = operationCancellation;
        var lockTaken = false;
        var succeeded = false;
        try
        {
            await _connectionGate.WaitAsync(operationCancellation.Token);
            lockTaken = true;
            _isConnectionOperationRunning = true;
            var existingState = AudioPlaybackConnectionState.Closed;
            var hasOpenedConnection = _connections.TryGetValue(device.Id, out var existingConnection) &&
                                      TryReadConnectionState(existingConnection, out existingState, out _) &&
                                      existingState == AudioPlaybackConnectionState.Opened;
            var markedForRecovery = IsProfileRecoveryRequired(device);
            var requiresProfileRecovery = ProfileRecoveryPolicy.ShouldResetBeforeConnect(
                markedForRecovery,
                automatic,
                hasOpenedConnection);
            var profileRecoveryAttempted = false;
            var profileRecoverySucceeded = false;
            if (requiresProfileRecovery)
            {
                allowProfileRecovery = true;
            }

            if (hasOpenedConnection && !requiresProfileRecovery)
            {
                ApplyNativeConnectionState(device, existingState);
                RememberPreferredDevice(device);
                UpdateStatus($"音频接收已开启：{device.DisplayName}", StatusKind.Success);
                succeeded = true;
                return true;
            }

            foreach (var activeDeviceId in _connections.Keys.ToList())
            {
                ReleaseConnection(activeDeviceId, updateStatus: false);
            }

            if (requiresProfileRecovery)
            {
                profileRecoveryAttempted = true;
                UpdateStatus(
                    markedForRecovery
                        ? $"检测到上次异常断开，正在恢复蓝牙音频服务：{device.DisplayName}"
                        : $"正在主动重建蓝牙音频服务：{device.DisplayName}",
                    StatusKind.Progress);
                AppendLog(
                    markedForRecovery
                        ? $"设备曾在播放中异常断开，本次连接先重置 Profile：{device.DisplayName}"
                        : $"用户对已打开的连接再次执行快速连接，本次主动重置 Profile：{device.DisplayName}");
                profileRecoverySucceeded = await TryResetBluetoothAudioProfileAsync(
                    device,
                    operationCancellation.Token);
                if (profileRecoverySucceeded)
                {
                    await Task.Delay(AudioProfileResetSettleDelay, operationCancellation.Token);
                    var refreshedDevice = await WaitForAudioDeviceAfterProfileResetAsync(
                        device,
                        operationCancellation.Token);
                    if (refreshedDevice is null)
                    {
                        device.SetState(RelayDeviceState.Failed, "音频端点尚未恢复");
                        UpdateStatus($"音频端点尚未恢复：{device.DisplayName}，请稍后重试。", StatusKind.Error);
                        return false;
                    }

                    device = refreshedDevice;
                }
                else
                {
                    device.SetState(RelayDeviceState.Failed, "蓝牙音频服务恢复失败");
                    UpdateStatus($"蓝牙音频服务恢复失败：{device.DisplayName}，请稍后重试。", StatusKind.Error);
                    AppendLog($"断线恢复所需的 Profile 重置未成功，已停止普通直连，避免无音频假连接：{device.DisplayName}");
                    return false;
                }
            }

            UpdateStatus(
                automatic
                    ? $"正在自动连接：{device.DisplayName}"
                    : profileRecoverySucceeded
                        ? $"蓝牙音频服务已恢复，正在连接：{device.DisplayName}"
                        : $"正在快速连接：{device.DisplayName}",
                StatusKind.Progress);
            AppendLog($"开始{(automatic ? "自动" : "手动")}连接：{device.DisplayName}");

            var directResult = await OpenDeviceWithAttemptsAsync(
                device,
                profileRecoverySucceeded ? RecoveryConnectAttempts : DirectConnectAttempts,
                profileRecoverySucceeded ? RecoveryRetryDelay : DirectReconnectDelay,
                operationCancellation.Token);
            device = directResult.Device;
            if (directResult.Success)
            {
                if (!requiresProfileRecovery || profileRecoverySucceeded)
                {
                    ClearProfileRecoveryRequired(device);
                }

                RememberPreferredDevice(device);
                succeeded = true;
                return true;
            }

            if (!allowProfileRecovery || profileRecoveryAttempted || operationCancellation.IsCancellationRequested)
            {
                device.SetState(RelayDeviceState.Failed, directResult.FailureReason ?? "连接失败");
                if (profileRecoveryAttempted)
                {
                    UpdateStatus($"断线恢复失败：{device.DisplayName}，请再次尝试或导出诊断。", StatusKind.Error);
                }

                return false;
            }

            UpdateStatus($"直连未成功，正在修复蓝牙音频服务：{device.DisplayName}", StatusKind.Progress);
            profileRecoveryAttempted = true;
            var profileReset = await TryResetBluetoothAudioProfileAsync(device, operationCancellation.Token);
            if (!profileReset)
            {
                device.SetState(RelayDeviceState.Failed, directResult.FailureReason ?? "连接失败");
                UpdateStatus($"连接失败：{device.DisplayName}，请查看诊断记录。", StatusKind.Error);
                return false;
            }

            await Task.Delay(AudioProfileResetSettleDelay, operationCancellation.Token);
            var refreshedRecoveryDevice = await WaitForAudioDeviceAfterProfileResetAsync(
                device,
                operationCancellation.Token);
            if (refreshedRecoveryDevice is null)
            {
                device.SetState(RelayDeviceState.Failed, "音频端点尚未恢复");
                UpdateStatus($"恢复连接失败：{device.DisplayName} 的音频端点尚未出现。", StatusKind.Error);
                return false;
            }

            device = refreshedRecoveryDevice;
            var recoveryResult = await OpenDeviceWithAttemptsAsync(
                device,
                RecoveryConnectAttempts,
                RecoveryRetryDelay,
                operationCancellation.Token);
            device = recoveryResult.Device;
            if (recoveryResult.Success)
            {
                ClearProfileRecoveryRequired(device);
                RememberPreferredDevice(device);
                succeeded = true;
                return true;
            }

            device.SetState(RelayDeviceState.Failed, recoveryResult.FailureReason ?? "恢复连接失败");
            UpdateStatus($"恢复连接失败：{device.DisplayName}，请导出诊断记录。", StatusKind.Error);
            return false;
        }
        catch (OperationCanceledException)
        {
            if (device.IsAvailable && device.State is RelayDeviceState.Enabling or RelayDeviceState.Connecting)
            {
                device.SetState(RelayDeviceState.Ready);
            }

            AppendLog($"连接操作已取消：{device.DisplayName}");
            return false;
        }
        finally
        {
            if (lockTaken)
            {
                _connectionGate.Release();
            }

            _isConnectionOperationRunning = false;
            if (ReferenceEquals(_connectionOperationCancellation, operationCancellation))
            {
                _connectionOperationCancellation = null;
            }

            if (automatic &&
                !succeeded &&
                !operationCancellation.IsCancellationRequested &&
                !_lifetimeCancellation.IsCancellationRequested)
            {
                RecordAutoReconnectFailure(device);
            }
        }
    }

    private async Task<ConnectionAttemptResult> OpenDeviceWithAttemptsAsync(
        RemoteAudioDevice device,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        string? failureReason = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = await EnsureConnectionStartedAsync(device, cancellationToken);
            if (connection is not null)
            {
                try
                {
                    device.SetState(RelayDeviceState.Connecting);
                    var openOperation = connection.OpenAsync();
                    var openResult = await AwaitWithTimeoutAsync(
                        openOperation,
                        ConnectionOpenTimeout,
                        cancellationToken);
                    if (!IsActiveConnection(device.Id, connection))
                    {
                        failureReason = "连接已被系统取消";
                    }
                    else if (openResult.Status == AudioPlaybackConnectionOpenResultStatus.Success)
                    {
                        device.IsEnabled = true;
                        device.SetState(RelayDeviceState.Playing);
                        UpdateStatus($"已打开音频接收：{device.DisplayName}", StatusKind.Success);
                        AppendLog($"直连成功：{device.DisplayName}，尝试 {attempt}/{maxAttempts}");
                        UpdateTrayText($"已连接 {device.DisplayName}");
                        ResetAutoReconnectBudget();
                        return new ConnectionAttemptResult(true, device, null);
                    }
                    else
                    {
                        failureReason = $"打开失败：{openResult.Status}";
                    }
                }
                catch (TimeoutException)
                {
                    failureReason = $"打开连接超过 {ConnectionOpenTimeout.TotalSeconds:0} 秒";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failureReason = $"打开连接异常：{ex.Message}";
                }
            }
            else
            {
                failureReason = "启用音频接收失败";
            }

            AppendLog($"{failureReason}：{device.DisplayName}，尝试 {attempt}/{maxAttempts}");
            ReleaseConnection(device.Id, updateStatus: false);
            if (attempt < maxAttempts)
            {
                device.SetState(RelayDeviceState.Connecting);
                await Task.Delay(retryDelay, cancellationToken);
                device = await RefreshAudioDeviceAsync(device, cancellationToken) ?? device;
            }
        }

        return new ConnectionAttemptResult(false, device, failureReason);
    }

    private void StopRelayButton_Click(object? sender, EventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            UpdateStatus("请先选择一个设备。", StatusKind.Neutral);
            return;
        }

        _autoConnectSuppressed = true;
        CancelAutoReconnect();
        CancelConnectionOperation();
        foreach (var activeDeviceId in _connections.Keys.ToList())
        {
            ReleaseConnection(activeDeviceId, updateStatus: false);
        }

        device.IsEnabled = false;
        device.SetState(device.IsAvailable ? RelayDeviceState.Ready : RelayDeviceState.Unavailable);
        UpdateStatus($"已停止中继：{device.DisplayName}，本次运行不再自动重连。", StatusKind.Neutral);
    }

    private void StartDeviceWatcher()
    {
        StopDeviceWatcher();

        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            _deviceWatcher = DeviceInformation.CreateWatcher(
                selector,
                BluetoothDeviceIdentity.RequestedProperties);
            _deviceWatcher.Added += DeviceWatcher_Added;
            _deviceWatcher.Removed += DeviceWatcher_Removed;
            _deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            _deviceWatcher.Stopped += DeviceWatcher_Stopped;
            _deviceWatcher.Start();

            UpdateStatus("正在扫描支持音频接收的蓝牙设备...", StatusKind.Progress);
            AppendLog("设备扫描已启动。");
        }
        catch (Exception ex)
        {
            StopDeviceWatcher();
            UpdateStatus("蓝牙设备扫描启动失败，请检查电脑蓝牙是否已开启。", StatusKind.Error);
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
            var existing = _devices.FirstOrDefault(item => BluetoothDeviceIdentity.Matches(item, deviceInfo));
            if (existing is not null)
            {
                var wasAvailable = existing.IsAvailable;
                var previousId = existing.Id;
                if (!previousId.Equals(deviceInfo.Id, StringComparison.OrdinalIgnoreCase))
                {
                    ReleaseConnection(previousId, updateStatus: false);
                }

                BluetoothDeviceIdentity.Update(existing, deviceInfo);
                ResetAutoReconnectBudgetForReappearedDevice(existing, wasAvailable);
                UpdateDeviceCount();
                ScheduleAutoConnect(InitialAutoConnectDelay);
                return;
            }

            var device = BluetoothDeviceIdentity.Create(deviceInfo);
            _devices.Add(device);
            ResetAutoReconnectBudgetForReappearedDevice(device, wasAvailable: false);
            UpdateDeviceCount();
            AppendLog($"发现设备：{device.DisplayName}");
            ScheduleAutoConnect(InitialAutoConnectDelay);
        });
    }

    private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceUpdate)
    {
        RunOnUiThread(() =>
        {
            var device = _devices.FirstOrDefault(item => item.Id == deviceUpdate.Id);
            if (device is not null &&
                ProfileRecoveryPolicy.IsUnexpectedDisconnect(
                    device.State,
                    device.IsEnabled,
                    _connections.ContainsKey(deviceUpdate.Id),
                    _autoConnectSuppressed))
            {
                MarkProfileRecoveryRequired(device, "设备在中继过程中离线");
            }

            ReleaseConnection(deviceUpdate.Id, updateStatus: false);
            if (device is not null)
            {
                device.IsAvailable = false;
                device.IsEnabled = false;
                device.SetState(RelayDeviceState.Unavailable);
                AppendLog($"设备已离线：{device.DisplayName}");
                UpdateDeviceCount();
            }
        });
    }

    private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
    {
        RunOnUiThread(() =>
        {
            UpdateDeviceCount();
            UpdateStatus($"扫描完成 · 发现 {_devices.Count(device => device.IsAvailable)} 台在线设备", StatusKind.Neutral);
            AppendLog("设备扫描完成。");
            ScheduleAutoConnect(InitialAutoConnectDelay);
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

            var wasPlaying = device.State == RelayDeviceState.Playing;
            if (state == AudioPlaybackConnectionState.Closed && wasPlaying)
            {
                MarkProfileRecoveryRequired(device, "播放中的连接被系统关闭");
            }

            ApplyNativeConnectionState(device, state);
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
                UpdateStatus(
                    state == AudioPlaybackConnectionState.Closed
                        ? _autoReconnectBudget.IsExhausted
                            ? $"设备已断开：{device.DisplayName}，请手动执行快速连接。"
                            : $"设备已断开：{device.DisplayName}，等待自动重连。"
                        : $"当前连接状态：{device.DisplayName} -> {state}",
                    state == AudioPlaybackConnectionState.Opened ? StatusKind.Success : StatusKind.Progress);
            }

            if (state == AudioPlaybackConnectionState.Closed)
            {
                ReleaseConnection(deviceId, updateStatus: false);
                ScheduleAutoConnect(GetAutoReconnectDelay());
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
            var deviceInfos = await DeviceInformation.FindAllAsync(
                selector,
                BluetoothDeviceIdentity.RequestedProperties);
            var snapshot = deviceInfos.ToList();
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

    private void SyncDeviceStatusSnapshot(IReadOnlyList<DeviceInformation> snapshot)
    {
        var now = DateTime.UtcNow;

        foreach (var info in snapshot)
        {
            var existing = _devices.FirstOrDefault(item => BluetoothDeviceIdentity.Matches(item, info));
            if (existing is not null)
            {
                var wasAvailable = existing.IsAvailable;
                var previousId = existing.Id;
                if (!previousId.Equals(info.Id, StringComparison.OrdinalIgnoreCase))
                {
                    ReleaseConnection(previousId, updateStatus: false);
                }

                BluetoothDeviceIdentity.Update(existing, info);
                ResetAutoReconnectBudgetForReappearedDevice(existing, wasAvailable);
                continue;
            }

            var addedDevice = BluetoothDeviceIdentity.Create(info);
            _devices.Add(addedDevice);
            ResetAutoReconnectBudgetForReappearedDevice(addedDevice, wasAvailable: false);
        }

        foreach (var device in _devices.ToList())
        {
            var isAvailable = snapshot.Any(info => BluetoothDeviceIdentity.Matches(device, info));
            if (!isAvailable)
            {
                if (ProfileRecoveryPolicy.IsUnexpectedDisconnect(
                        device.State,
                        device.IsEnabled,
                        _connections.ContainsKey(device.Id),
                        _autoConnectSuppressed))
                {
                    MarkProfileRecoveryRequired(device, "系统状态同步发现中继设备离线");
                }

                if (_connections.ContainsKey(device.Id))
                {
                    ReleaseConnection(device.Id, updateStatus: false);
                }

                device.IsAvailable = false;
                device.IsEnabled = false;
                if (device.State != RelayDeviceState.Unavailable)
                {
                    device.SetState(RelayDeviceState.Unavailable);
                    AppendLog($"设备状态同步：{device.DisplayName} 已离线。");
                }

                continue;
            }

            if (IsStaleConnectingState(device, now))
            {
                ReleaseConnection(device.Id, updateStatus: false);
                device.SetState(RelayDeviceState.Ready);
                AppendLog($"设备状态同步：{device.DisplayName} 连接超时，已重置为等待连接。");
            }
        }

        UpdateActiveConnectionStates();
        UpdateDeviceCount();
        ScheduleAutoConnect(InitialAutoConnectDelay);
    }

    private void UpdateActiveConnectionStates()
    {
        foreach (var pair in _connections.ToList())
        {
            var device = _devices.FirstOrDefault(item => item.Id == pair.Key);
            if (!TryReadConnectionState(pair.Value, out var state, out var error))
            {
                AppendLog($"设备状态同步：连接状态不可读，已释放：{error}");
                if (device?.State == RelayDeviceState.Playing)
                {
                    MarkProfileRecoveryRequired(device, "活动连接状态不可读");
                }

                ReleaseConnection(pair.Key, updateStatus: false);
                if (device is not null)
                {
                    device.IsEnabled = false;
                    device.SetState(device.IsAvailable ? RelayDeviceState.Ready : RelayDeviceState.Unavailable);
                }

                continue;
            }

            if (state == AudioPlaybackConnectionState.Closed)
            {
                if (device?.State == RelayDeviceState.Playing)
                {
                    MarkProfileRecoveryRequired(device, "活动连接异常关闭");
                }

                ReleaseConnection(pair.Key, updateStatus: false);
                if (device is not null)
                {
                    device.IsEnabled = false;
                    device.SetState(device.IsAvailable ? RelayDeviceState.Ready : RelayDeviceState.Unavailable);
                }

                continue;
            }

            if (device is not null)
            {
                ApplyNativeConnectionState(device, state);
            }
        }
    }

    private static bool IsStaleConnectingState(RemoteAudioDevice device, DateTime now)
    {
        return device.State is RelayDeviceState.Enabling or RelayDeviceState.Connecting &&
               now - device.StateUpdatedAt > ConnectingStateTimeout;
    }

    private async Task<bool> TryResetBluetoothAudioProfileAsync(
        RemoteAudioDevice device,
        CancellationToken cancellationToken)
    {
        try
        {
            AppendLog($"正在重置蓝牙音频服务：{device.DisplayName}");
            var deviceName = device.DisplayName;
            var bluetoothAddress = device.BluetoothAddress;
            var result = await Task.Run(
                    () => BluetoothProfileReset.TryResetAudioSourceService(
                        deviceName,
                        bluetoothAddress),
                    cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            AppendLog(result.Message);
            return result.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            AppendLog("蓝牙音频服务重置超时，已停止恢复流程。");
            return false;
        }
        catch (Exception ex)
        {
            AppendLog($"蓝牙音频服务重置异常（已停止恢复流程）：{ex.Message}");
            return false;
        }
    }

    private async Task<RemoteAudioDevice?> RefreshAudioDeviceAsync(
        RemoteAudioDevice previousDevice,
        CancellationToken cancellationToken,
        bool logMissing = true)
    {
        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            var findTask = AwaitWithTimeoutAsync(
                DeviceInformation.FindAllAsync(selector, BluetoothDeviceIdentity.RequestedProperties),
                ConnectionStartTimeout,
                cancellationToken);
            var deviceInfos = await findTask;
            var matchedInfo = deviceInfos.FirstOrDefault(info =>
                BluetoothDeviceIdentity.Matches(previousDevice, info));

            if (matchedInfo is null)
            {
                if (logMissing)
                {
                    AppendLog($"刷新后未找到音频接收设备：{previousDevice.DisplayName}");
                }

                return null;
            }

            var existingDevice = _devices.FirstOrDefault(item => BluetoothDeviceIdentity.Matches(item, matchedInfo));
            if (existingDevice is not null && !ReferenceEquals(existingDevice, previousDevice))
            {
                BluetoothDeviceIdentity.Update(existingDevice, matchedInfo);
                return existingDevice;
            }

            var previousId = previousDevice.Id;
            if (!previousId.Equals(matchedInfo.Id, StringComparison.OrdinalIgnoreCase))
            {
                ReleaseConnection(previousId, updateStatus: false);
            }

            BluetoothDeviceIdentity.Update(previousDevice, matchedInfo);
            UpdateDeviceCount();
            AppendLog($"刷新后更新音频接收设备：{previousDevice.DisplayName}");
            return previousDevice;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppendLog($"刷新音频接收设备失败（已继续重连）：{ex.Message}");
            return null;
        }
    }

    private async Task<AudioPlaybackConnection?> EnsureConnectionStartedAsync(
        RemoteAudioDevice device,
        CancellationToken cancellationToken)
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
                device.IsEnabled = true;
                ApplyNativeConnectionState(device, state);
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
            AppendLog($"TryCreateFromId 异常：{ex.Message}");
            return null;
        }

        if (connection is null)
        {
            AppendLog($"TryCreateFromId 返回空值：{device.DisplayName}");
            return null;
        }

        connection.StateChanged += Connection_StateChanged;
        _connections[device.Id] = connection;

        try
        {
            device.SetState(RelayDeviceState.Enabling);
            await AwaitWithTimeoutAsync(
                connection.StartAsync(),
                ConnectionStartTimeout,
                cancellationToken);
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
            ApplyNativeConnectionState(device, state);
            AppendLog($"StartAsync 成功：{device.DisplayName}");
            return connection;
        }
        catch (OperationCanceledException)
        {
            ReleaseConnection(device.Id, updateStatus: false);
            throw;
        }
        catch (TimeoutException)
        {
            AppendLog($"StartAsync 超时：{device.DisplayName}");
            ReleaseConnection(device.Id, updateStatus: false);
            return null;
        }
        catch (Exception ex)
        {
            AppendLog($"StartAsync 异常：{ex.Message}");
            ReleaseConnection(device.Id, updateStatus: false);
            return null;
        }
    }

    private async Task<RemoteAudioDevice?> WaitForAudioDeviceAfterProfileResetAsync(
        RemoteAudioDevice previousDevice,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + AudioProfileEndpointRefreshTimeout;
        var consecutiveMatches = 0;
        var sawEndpointGap = false;
        RemoteAudioDevice? latestDevice = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refreshed = await RefreshAudioDeviceAsync(
                previousDevice,
                cancellationToken,
                logMissing: false);
            if (refreshed is null)
            {
                sawEndpointGap = true;
                consecutiveMatches = 0;
            }
            else
            {
                latestDevice = refreshed;
                consecutiveMatches++;
                if (consecutiveMatches >= 2)
                {
                    AppendLog(
                        sawEndpointGap
                            ? $"蓝牙音频服务重置后端点已重新出现：{refreshed.DisplayName}"
                            : $"蓝牙音频服务重置后端点已稳定：{refreshed.DisplayName}");
                    return refreshed;
                }
            }

            await Task.Delay(AudioProfileEndpointPollInterval, cancellationToken);
        }

        AppendLog(
            latestDevice is null
                ? $"蓝牙音频服务重置后仍未重新枚举音频端点：{previousDevice.DisplayName}"
                : $"蓝牙音频端点在等待窗口内未达到稳定状态：{latestDevice.DisplayName}");
        return null;
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
            device.SetState(device.IsAvailable ? RelayDeviceState.Ready : RelayDeviceState.Unavailable);
            AppendLog($"已释放连接：{device.DisplayName}");
        }

        if (updateStatus)
        {
            UpdateStatus("连接资源已释放。", StatusKind.Neutral);
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

    private void UpdateStatus(string message, StatusKind kind = StatusKind.Progress)
    {
        _statusLabel.Text = message;
        _statusDot.DotColor = kind switch
        {
            StatusKind.Success => AppTheme.Success,
            StatusKind.Error => AppTheme.Danger,
            StatusKind.Neutral => AppTheme.TextSecondary,
            _ => AppTheme.Accent
        };
        _statusDot.Invalidate();
    }

    private void UpdateTrayText(string status)
    {
        var text = $"蓝牙音频中继 - {status}";
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void UpdateDeviceCount()
    {
        _deviceCountLabel.Text = $"{_devices.Count(device => device.IsAvailable)} 台在线";
    }

    private void AppendLog(string message)
    {
        _logTextBox.AddLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        DiagnosticLog.Write(message);
    }

    private void ApplyNativeConnectionState(RemoteAudioDevice device, AudioPlaybackConnectionState state)
    {
        switch (state)
        {
            case AudioPlaybackConnectionState.Opened:
                device.IsEnabled = true;
                device.SetState(RelayDeviceState.Playing);
                break;
            default:
                device.SetState(device.IsAvailable ? RelayDeviceState.Ready : RelayDeviceState.Unavailable);
                break;
        }
    }

    private void RememberPreferredDevice(RemoteAudioDevice device)
    {
        var changed = !_preferences.PreferredDeviceKey?.Equals(
                          device.StableKey,
                          StringComparison.OrdinalIgnoreCase) ?? true;
        _preferences.PreferredDeviceKey = device.StableKey;
        _preferences.PreferredDeviceName = device.DisplayName;
        _preferences.PreferredDeviceNeedsProfileRecovery = IsProfileRecoveryRequired(device);
        _autoConnectSuppressed = false;
        ResetAutoReconnectBudget();
        SavePreferences();
        SelectDevice(device);
        if (changed)
        {
            AppendLog($"已记住首选设备：{device.DisplayName}");
        }
    }

    private bool IsProfileRecoveryRequired(RemoteAudioDevice device)
    {
        return !string.IsNullOrWhiteSpace(device.StableKey) &&
               _profileRecoveryRequiredDeviceKeys.Contains(device.StableKey);
    }

    private void MarkProfileRecoveryRequired(RemoteAudioDevice device, string reason)
    {
        if (_autoConnectSuppressed || string.IsNullOrWhiteSpace(device.StableKey))
        {
            return;
        }

        if (_profileRecoveryRequiredDeviceKeys.Add(device.StableKey))
        {
            AppendLog($"{reason}，下次连接将先恢复蓝牙音频 Profile：{device.DisplayName}");
        }

        if (BluetoothDeviceIdentity.MatchesPreference(
                device,
                _preferences.PreferredDeviceKey,
                _preferences.PreferredDeviceName))
        {
            _preferences.PreferredDeviceNeedsProfileRecovery = true;
            UserPreferencesStore.Save(_preferences);
        }
    }

    private void ClearProfileRecoveryRequired(RemoteAudioDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.StableKey))
        {
            return;
        }

        _profileRecoveryRequiredDeviceKeys.Remove(device.StableKey);
        if (BluetoothDeviceIdentity.MatchesPreference(
                device,
                _preferences.PreferredDeviceKey,
                _preferences.PreferredDeviceName))
        {
            _preferences.PreferredDeviceNeedsProfileRecovery = false;
            UserPreferencesStore.Save(_preferences);
        }
    }

    private void SelectDevice(RemoteAudioDevice device)
    {
        foreach (DataGridViewRow row in _devicesGrid.Rows)
        {
            if (ReferenceEquals(row.DataBoundItem, device))
            {
                row.Selected = true;
                if (row.Cells.Count > 0)
                {
                    _devicesGrid.CurrentCell = row.Cells[0];
                }

                break;
            }
        }
    }

    private void RecordAutoReconnectFailure(RemoteAudioDevice device)
    {
        var wasExhausted = _autoReconnectBudget.IsExhausted;
        _autoReconnectBudget.RecordFailure();
        if (wasExhausted || !_autoReconnectBudget.IsExhausted)
        {
            return;
        }

        AppendLog(
            $"自动重连已达到一轮上限（每轮最多 {DirectConnectAttempts} 次）：{device.DisplayName}，" +
            "等待设备重新出现或用户手动快速连接。");
        UpdateStatus(
            $"自动重连已暂停：{device.DisplayName}，请打开手机蓝牙后执行快速连接。",
            StatusKind.Error);
        UpdateTrayText("等待手动连接");
    }

    private void ResetAutoReconnectBudget(RemoteAudioDevice? device = null, string? reason = null)
    {
        var wasExhausted = _autoReconnectBudget.IsExhausted;
        if (!_autoReconnectBudget.Reset() || !wasExhausted || device is null || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        AppendLog($"{reason}，已解除自动重连暂停：{device.DisplayName}");
    }

    private void ResetAutoReconnectBudgetForReappearedDevice(
        RemoteAudioDevice device,
        bool wasAvailable)
    {
        if (wasAvailable ||
            !_preferences.AutoConnectEnabled ||
            _autoConnectSuppressed ||
            !_autoReconnectBudget.IsExhausted ||
            !BluetoothDeviceIdentity.MatchesPreference(
                device,
                _preferences.PreferredDeviceKey,
                _preferences.PreferredDeviceName))
        {
            return;
        }

        ResetAutoReconnectBudget(device, "检测到首选设备重新出现");
    }

    private void ScheduleAutoConnect(TimeSpan delay)
    {
        if (!_preferences.AutoConnectEnabled ||
            _autoConnectSuppressed ||
            _autoReconnectBudget.IsExhausted ||
            _exitRequested ||
            _lifetimeCancellation.IsCancellationRequested ||
            _isConnectionOperationRunning ||
            _connectionOperationCancellation is not null ||
            _autoReconnectCancellation is { IsCancellationRequested: false })
        {
            return;
        }

        var preferredDevice = _devices.FirstOrDefault(device =>
            device.IsAvailable &&
            BluetoothDeviceIdentity.MatchesPreference(
                device,
                _preferences.PreferredDeviceKey,
                _preferences.PreferredDeviceName));
        if (preferredDevice is null ||
            preferredDevice.State == RelayDeviceState.Playing ||
            string.IsNullOrWhiteSpace(_preferences.PreferredDeviceKey))
        {
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _autoReconnectCancellation = cancellation;
        _ = AutoConnectAfterDelayAsync(preferredDevice.StableKey, delay, cancellation);
    }

    private async Task AutoConnectAfterDelayAsync(
        string stableKey,
        TimeSpan delay,
        CancellationTokenSource cancellation)
    {
        var connected = false;
        var canceled = false;
        try
        {
            await Task.Delay(delay, cancellation.Token);
            var device = _devices.FirstOrDefault(item =>
                item.IsAvailable && item.StableKey.Equals(stableKey, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                connected = await OpenDeviceAsync(
                    device,
                    allowProfileRecovery: false,
                    automatic: true);
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        finally
        {
            if (ReferenceEquals(_autoReconnectCancellation, cancellation))
            {
                _autoReconnectCancellation = null;
            }

            cancellation.Dispose();
        }

        if (!connected &&
            !canceled &&
            !_lifetimeCancellation.IsCancellationRequested &&
            !_autoConnectSuppressed &&
            !_autoReconnectBudget.IsExhausted)
        {
            ScheduleAutoConnect(GetAutoReconnectDelay());
        }
    }

    private TimeSpan GetAutoReconnectDelay()
    {
        var seconds = Math.Min(30, Math.Pow(2, Math.Clamp(_autoReconnectBudget.FailedRounds, 1, 5)));
        return TimeSpan.FromSeconds(seconds);
    }

    private void CancelConnectionOperation()
    {
        try
        {
            _connectionOperationCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CancelAutoReconnect()
    {
        try
        {
            _autoReconnectCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ShowTrayNotification(string title, string message, ToolTipIcon icon)
    {
        if (_preferences.QuietNotifications)
        {
            return;
        }

        _trayIcon.ShowBalloonTip(2600, title, message, icon);
    }

    private void AudioOutputMonitor_DefaultOutputChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RefreshDefaultOutput);
    }

    private void RefreshDefaultOutput()
    {
        var previousOutput = _defaultOutputName;
        _defaultOutputName = _audioOutputMonitor?.GetDefaultOutputName() ?? "检测不可用";
        _outputDeviceLabel.Text = $"默认输出 · {_defaultOutputName}";
        if (!previousOutput.Equals(_defaultOutputName, StringComparison.Ordinal))
        {
            AppendLog($"默认输出设备：{_defaultOutputName}");
        }
    }

    private void OpenSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatus($"无法打开声音设置：{ex.Message}", StatusKind.Error);
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual &&
            _preferences.LastUpdateCheckUtc is { } lastCheck &&
            DateTime.UtcNow - lastCheck < TimeSpan.FromHours(24))
        {
            return;
        }

        if (manual)
        {
            UpdateStatus("正在检查更新...", StatusKind.Progress);
        }

        try
        {
            var currentVersion = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0);
            var result = await UpdateChecker.CheckAsync(currentVersion, _lifetimeCancellation.Token);
            _preferences.LastUpdateCheckUtc = DateTime.UtcNow;
            SavePreferences();
            AppendLog(result.Message);

            if (!result.Succeeded)
            {
                if (manual)
                {
                    UpdateStatus(result.Message, StatusKind.Error);
                }

                return;
            }

            if (result.UpdateAvailable)
            {
                UpdateStatus(result.Message, StatusKind.Success);
                ShowTrayNotification("蓝牙音频中继有新版本", result.Message, ToolTipIcon.Info);
                if (manual && result.ReleaseUri is not null &&
                    MessageBox.Show(
                        this,
                        $"{result.Message}\n\n是否打开正式发布页面？",
                        "检查更新",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(result.ReleaseUri.AbsoluteUri) { UseShellExecute = true });
                }

                return;
            }

            if (manual)
            {
                UpdateStatus(result.Message, StatusKind.Success);
                MessageBox.Show(this, result.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendLog($"检查更新异常：{ex.Message}");
            if (manual)
            {
                UpdateStatus($"检查更新失败：{ex.Message}", StatusKind.Error);
            }
        }
    }

    private void OpenLogDirectory()
    {
        try
        {
            var directory = Path.GetDirectoryName(DiagnosticLog.CurrentPath)!;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatus($"无法打开日志目录：{ex.Message}", StatusKind.Error);
        }
    }

    private void ExportDiagnosticsButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "txt",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"BluetoothAudioRelay-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Title = "导出蓝牙音频中继诊断"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var report = DiagnosticLog.BuildReport(_defaultOutputName, _devices, _preferences);
            File.WriteAllText(dialog.FileName, report);
            UpdateStatus("诊断报告已导出。", StatusKind.Success);
            AppendLog($"诊断报告已导出：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"导出诊断失败：{ex.Message}", StatusKind.Error);
            AppendLog($"导出诊断失败：{ex.Message}");
        }
    }

    private static async Task AwaitWithTimeoutAsync(
        Windows.Foundation.IAsyncAction operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await AwaitOperationAsync(operation).WaitAsync(timeout, cancellationToken);
        }
        catch
        {
            operation.Cancel();
            throw;
        }
    }

    private static async Task<T> AwaitWithTimeoutAsync<T>(
        Windows.Foundation.IAsyncOperation<T> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AwaitOperationAsync(operation).WaitAsync(timeout, cancellationToken);
        }
        catch
        {
            operation.Cancel();
            throw;
        }
    }

    private static async Task AwaitOperationAsync(Windows.Foundation.IAsyncAction operation)
    {
        await operation;
    }

    private static async Task<T> AwaitOperationAsync<T>(Windows.Foundation.IAsyncOperation<T> operation)
    {
        return await operation;
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
