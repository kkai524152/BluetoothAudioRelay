using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace BluetoothAudioRelay;

internal static class DiagnosticLog
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAudioRelay",
        "logs");
    private static readonly string CurrentLogPath = Path.Combine(LogDirectory, "BluetoothAudioRelay.log");

    public static string CurrentPath => CurrentLogPath;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                WriteLineUnsafe(
                    $"应用启动 version={Assembly.GetExecutingAssembly().GetName().Version} " +
                    $"os={Environment.OSVersion.VersionString} arch={RuntimeInformation.ProcessArchitecture}");
            }
            catch
            {
            }
        }
    }

    public static void Write(string message)
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                WriteLineUnsafe(message);
            }
            catch
            {
            }
        }
    }

    public static void WriteException(string context, Exception exception)
    {
        Write($"{context}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    public static string BuildReport(
        string defaultOutput,
        IEnumerable<RemoteAudioDevice> devices,
        UserPreferences preferences)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Bluetooth Audio Relay 诊断报告");
        builder.AppendLine($"生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"应用版本: {Application.ProductVersion}");
        builder.AppendLine($"操作系统: {Environment.OSVersion.VersionString}");
        builder.AppendLine($".NET: {Environment.Version}");
        builder.AppendLine($"进程架构: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"默认输出: {defaultOutput}");
        builder.AppendLine($"自动连接: {(preferences.AutoConnectEnabled ? "开启" : "关闭")}");
        builder.AppendLine($"开机启动: {(preferences.StartWithWindows ? "开启" : "关闭")}");
        builder.AppendLine($"下次连接先恢复 Profile: {(preferences.PreferredDeviceNeedsProfileRecovery ? "是" : "否")}");
        builder.AppendLine();
        builder.AppendLine("设备:");

        foreach (var device in devices)
        {
            builder.AppendLine(
                $"- {device.DisplayName}: {device.AvailabilityText}, {device.ConnectionStateText}, " +
                $"身份={RedactKey(device.StableKey)}");
        }

        builder.AppendLine();
        builder.AppendLine("最近日志:");
        builder.Append(ReadRecentLines(200));
        return builder.ToString();
    }

    private static string ReadRecentLines(int maxLines)
    {
        lock (SyncRoot)
        {
            try
            {
                if (!File.Exists(CurrentLogPath))
                {
                    return "（暂无持久日志）";
                }

                return string.Join(Environment.NewLine, File.ReadLines(CurrentLogPath).TakeLast(maxLines));
            }
            catch (Exception ex)
            {
                return $"（读取日志失败：{ex.Message}）";
            }
        }
    }

    private static string RedactKey(string key)
    {
        if (key.StartsWith("bt:", StringComparison.OrdinalIgnoreCase) && key.Length >= 9)
        {
            return $"bt:******{key[^6..]}";
        }

        if (key.StartsWith("container:", StringComparison.OrdinalIgnoreCase) && key.Length > 18)
        {
            return $"container:******{key[^8..]}";
        }

        return "本地设备标识（已隐藏）";
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(CurrentLogPath);
        if (!info.Exists || info.Length < MaxLogBytes)
        {
            return;
        }

        var archivePath = Path.Combine(LogDirectory, $"BluetoothAudioRelay-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.Move(CurrentLogPath, archivePath, overwrite: true);

        foreach (var oldLog in Directory.EnumerateFiles(LogDirectory, "BluetoothAudioRelay-*.log")
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Skip(3))
        {
            oldLog.Delete();
        }
    }

    private static void WriteLineUnsafe(string message)
    {
        File.AppendAllText(
            CurrentLogPath,
            $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}",
            Encoding.UTF8);
    }
}
