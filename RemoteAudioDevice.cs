using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BluetoothAudioRelay;

internal enum RelayDeviceState
{
    Unavailable,
    Ready,
    Enabling,
    Connecting,
    Playing,
    Failed
}

public sealed class RemoteAudioDevice : INotifyPropertyChanged
{
    private string _id;
    private string _name;
    private string _stableKey;
    private string? _deviceAddress;
    private ulong? _bluetoothAddress;
    private Guid? _containerId;
    private bool _isAvailable = true;
    private bool _isEnabled;
    private RelayDeviceState _state = RelayDeviceState.Ready;
    private string? _statusDetail;
    private DateTime _stateUpdatedAt = DateTime.UtcNow;

    internal RemoteAudioDevice(
        string id,
        string? name,
        string stableKey,
        string? deviceAddress = null,
        ulong? bluetoothAddress = null,
        Guid? containerId = null)
    {
        _id = id;
        _name = NormalizeDisplayName(name);
        _stableKey = stableKey;
        _deviceAddress = deviceAddress;
        _bluetoothAddress = bluetoothAddress;
        _containerId = containerId;
    }

    public string Id => _id;

    public string Name => _name;

    public string DisplayName => Name;

    public string StableKey => _stableKey;

    public string? DeviceAddress => _deviceAddress;

    public ulong? BluetoothAddress => _bluetoothAddress;

    public Guid? ContainerId => _containerId;

    public bool IsAvailable
    {
        get => _isAvailable;
        internal set
        {
            if (_isAvailable == value)
            {
                return;
            }

            _isAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailabilityText));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        internal set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EnableStateText));
        }
    }

    internal RelayDeviceState State
    {
        get => _state;
        set => SetState(value);
    }

    public string? StatusDetail
    {
        get => _statusDetail;
        internal set
        {
            if (_statusDetail == value)
            {
                return;
            }

            _statusDetail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionStateText));
        }
    }

    public string EnableStateText => IsEnabled ? "已启用" : "未启用";

    public string AvailabilityText => IsAvailable ? "在线" : "离线";

    public DateTime StateUpdatedAt => _stateUpdatedAt;

    public string ConnectionStateText => State switch
    {
        RelayDeviceState.Unavailable => "未连接",
        RelayDeviceState.Ready => "等待连接",
        RelayDeviceState.Enabling => "正在启用",
        RelayDeviceState.Connecting => "正在连接",
        RelayDeviceState.Playing => "正在播放",
        RelayDeviceState.Failed => string.IsNullOrWhiteSpace(StatusDetail) ? "连接失败" : StatusDetail,
        _ => "未知状态"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateIdentity(
        string id,
        string? name,
        string stableKey,
        string? deviceAddress,
        ulong? bluetoothAddress,
        Guid? containerId)
    {
        SetField(ref _id, id, nameof(Id));
        SetField(ref _name, NormalizeDisplayName(name), nameof(Name));
        SetField(ref _stableKey, stableKey, nameof(StableKey));
        SetField(ref _deviceAddress, deviceAddress, nameof(DeviceAddress));
        SetField(ref _bluetoothAddress, bluetoothAddress, nameof(BluetoothAddress));
        SetField(ref _containerId, containerId, nameof(ContainerId));
        OnPropertyChanged(nameof(DisplayName));
    }

    internal void SetState(RelayDeviceState state, string? detail = null)
    {
        var changed = _state != state || _statusDetail != detail;
        if (!changed)
        {
            return;
        }

        _state = state;
        _statusDetail = detail;
        _stateUpdatedAt = DateTime.UtcNow;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(StateUpdatedAt));
        OnPropertyChanged(nameof(ConnectionStateText));
    }

    private static string NormalizeDisplayName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? "未命名设备" : name.Trim();
    }

    private void SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
