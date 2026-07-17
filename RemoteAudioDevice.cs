using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BluetoothAudioRelay;

public sealed class RemoteAudioDevice : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _connectionState = "未启用";
    private DateTime _stateUpdatedAt = DateTime.UtcNow;

    public RemoteAudioDevice(string id, string? name)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "未命名设备" : name;
    }

    public string Id { get; }

    public string Name { get; }

    public string DisplayName => Name;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
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

    public string ConnectionState
    {
        get => _connectionState;
        set
        {
            if (_connectionState == value)
            {
                return;
            }

            _connectionState = value;
            _stateUpdatedAt = DateTime.UtcNow;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionStateText));
        }
    }

    public string EnableStateText => IsEnabled ? "已启用" : "未启用";

    public DateTime StateUpdatedAt => _stateUpdatedAt;

    public string ConnectionStateText => ConnectionState switch
    {
        "Opened" => "正在播放",
        "Closed" => "已断开",
        "Opening" => "正在连接",
        "未启用" => "等待连接",
        "未连接" => "未连接",
        _ => ConnectionState
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
