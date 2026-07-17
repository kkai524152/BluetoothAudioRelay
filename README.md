# BluetoothAudioRelay

把 Windows 电脑作为蓝牙音频接收端，让手机音频从电脑当前默认播放设备输出。

## 使用方法

1. 在 Windows 蓝牙设置中先与手机完成配对。
2. 把有线耳机、USB 耳机或目标蓝牙耳机设为 Windows 默认输出设备。
3. 启动应用，选择手机并点击“快速连接”。
4. 在手机上播放音频。

## 托盘操作

- 点击窗口关闭按钮后，程序不会退出，而是继续在系统托盘运行。
- 双击托盘图标可恢复主窗口。
- 右键托盘图标可以快速连接手机、刷新设备或彻底退出程序。
- 为避免重复托盘图标，应用只允许同时运行一个实例。

## 发布包

- `dist/BluetoothAudioRelay-Setup-x64.exe`：推荐的轻量安装包，约 5.8 MB。
- `dist/BluetoothAudioRelay.exe`：轻量单文件，约 25.6 MB。
- `dist/BluetoothAudioRelay-portable-self-contained.exe`：无需预装运行时的便携版，约 179 MB。
- 轻量版本需要电脑安装 `.NET 8 Desktop Runtime`；自包含版不需要。
- 安装包采用当前用户安装，不需要管理员权限。

## 蓝牙耳机作为输出设备

应用本身始终把手机音频交给 Windows 默认输出设备。因此，如果默认输出是蓝牙耳机，软件逻辑上支持：

`手机 -> 蓝牙 -> Windows -> 蓝牙耳机`

实际能否稳定工作取决于电脑蓝牙适配器、驱动和无线环境是否支持同时承担 A2DP 接收端与发送端角色。可能出现延迟增加、卡顿或无法同时建立两条音频链路。如果单个适配器不稳定，可以使用第二个 USB 蓝牙适配器，或改用有线/USB 输出。使用蓝牙耳机时应选择“立体声/A2DP”输出，避免启用耳机麦克风后切换到低音质的免提模式。

## 开发

```powershell
dotnet build
dotnet run
```

核心 API：`Windows.Media.Audio.AudioPlaybackConnection`。
