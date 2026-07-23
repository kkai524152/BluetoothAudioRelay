# BluetoothAudioRelay

把 Windows 电脑作为蓝牙音频接收端，让手机音频从电脑当前默认播放设备输出。

## 使用方法

1. 在 Windows 蓝牙设置中先与手机完成配对。
2. 把有线耳机、USB 耳机或目标蓝牙耳机设为 Windows 默认输出设备。
3. 启动应用，选择手机并点击“快速连接”。首次连接成功后，应用会记住这台手机。
4. 在手机上播放音频。

“快速连接”会优先尝试直接打开音频接收，只有系统蓝牙状态异常时才修复 Audio Source Profile，因此正常连接不再等待 Profile 重置。

## 自动连接与诊断

- 首次手动连接成功后，应用会在手机重新进入范围时自动连接；每次断线自动连接最多执行一轮（两次尝试），失败后等待设备重新出现或手动快速连接，避免反复触发蓝牙驱动。
- 如果手机在中继过程中关闭蓝牙或离开范围，下次连接会先恢复 Audio Source Profile，避免系统显示已连接但没有声音。
- 顶部“自动连接”菜单可以关闭自动连接、开启开机后台启动或启用静默通知。
- 当前 Windows 默认输出设备会显示在连接状态卡中；更换默认输出后界面会自动更新。
- “导出诊断”会生成包含系统、输出设备、脱敏设备标识和最近日志的文本报告。
- 持久日志位于 `%LOCALAPPDATA%\BluetoothAudioRelay\logs`，最多保留当前日志和三个轮转日志。
- 应用每天最多自动检查一次 GitHub 最新版本，不会静默下载或执行未签名程序。

## 托盘操作

- 点击窗口关闭按钮后，程序不会退出，而是继续在系统托盘运行。
- 双击托盘图标可恢复主窗口。
- 右键托盘图标可以快速连接手机、刷新设备或彻底退出程序。
- 托盘“选择手机”菜单可以切换首选设备。
- 为避免重复托盘图标，应用只允许同时运行一个实例。
- 再次启动应用会唤起现有窗口，而不是显示重复运行提示。

## 发布包

- `dist/BluetoothAudioRelay-Setup-x64.exe`：推荐的轻量安装包，约 5.8 MB。
- `dist/BluetoothAudioRelay-win-x64.zip`：x64 轻量版。
- `dist/BluetoothAudioRelay-win-arm64.zip`：Windows ARM64 轻量版。
- 轻量版本需要电脑安装 `.NET 8 Desktop Runtime`。
- 安装包采用当前用户安装，不需要管理员权限。

## 蓝牙耳机作为输出设备

应用本身始终把手机音频交给 Windows 默认输出设备。因此，如果默认输出是蓝牙耳机，软件逻辑上支持：

`手机 -> 蓝牙 -> Windows -> 蓝牙耳机`

实际能否稳定工作取决于电脑蓝牙适配器、驱动和无线环境是否支持同时承担 A2DP 接收端与发送端角色。可能出现延迟增加、卡顿或无法同时建立两条音频链路。如果单个适配器不稳定，可以使用第二个 USB 蓝牙适配器，或改用有线/USB 输出。使用蓝牙耳机时应选择“立体声/A2DP”输出，避免启用耳机麦克风后切换到低音质的免提模式。

## 开发

```powershell
dotnet build
dotnet run
dotnet test .\tests\BluetoothAudioRelay.Tests\BluetoothAudioRelay.Tests.csproj
```

发布 x64、ARM64 压缩包和 x64 安装包：

```powershell
.\tools\Publish.ps1
```

安装包构建需要 Inno Setup 6；未安装时可使用 `-SkipInstaller` 只生成 x64/ARM64 压缩包。

正式发布时传入证书指纹进行 Authenticode 签名：

```powershell
.\tools\Publish.ps1 -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT"
```

没有代码签名证书时脚本仍可生成测试包，但会明确输出未签名警告。CI 会在 Windows 上执行测试并分别构建 `win-x64`、`win-arm64` 产物。

核心 API：`Windows.Media.Audio.AudioPlaybackConnection`。
