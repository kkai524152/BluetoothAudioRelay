using System.Runtime.InteropServices;

namespace BluetoothAudioRelay;

internal sealed record BluetoothProfileResetResult(bool Success, string Message);

internal static class BluetoothProfileReset
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorServiceDoesNotExist = 1060;
    private const uint EInvalidArg = 0x80070057;
    private const uint BluetoothServiceDisable = 0x00000000;
    private const uint BluetoothServiceEnable = 0x00000001;

    private static readonly Guid AudioSourceServiceClass = new("0000110a-0000-1000-8000-00805f9b34fb");

    public static BluetoothProfileResetResult TryResetAudioSourceService(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return new BluetoothProfileResetResult(false, "蓝牙音频服务重置跳过：设备名称为空。");
        }

        var radioParams = new BluetoothFindRadioParams
        {
            DwSize = Marshal.SizeOf<BluetoothFindRadioParams>()
        };

        var radioFindHandle = BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFindHandle == IntPtr.Zero)
        {
            return new BluetoothProfileResetResult(false, $"蓝牙音频服务重置跳过：未找到本机蓝牙适配器，错误 {Marshal.GetLastWin32Error()}。");
        }

        try
        {
            do
            {
                try
                {
                    var result = TryResetOnRadio(radioHandle, deviceName);
                    if (result.Success || result.Message.Contains("已找到设备", StringComparison.Ordinal))
                    {
                        return result;
                    }
                }
                finally
                {
                    CloseHandle(radioHandle);
                }
            }
            while (BluetoothFindNextRadio(radioFindHandle, out radioHandle));
        }
        finally
        {
            BluetoothFindRadioClose(radioFindHandle);
        }

        return new BluetoothProfileResetResult(false, $"蓝牙音频服务重置跳过：未在已配对设备中找到 {deviceName}。");
    }

    private static BluetoothProfileResetResult TryResetOnRadio(IntPtr radioHandle, string deviceName)
    {
        var searchParams = new BluetoothDeviceSearchParams
        {
            DwSize = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = false,
            ReturnConnected = true,
            IssueInquiry = false,
            TimeoutMultiplier = 2,
            Radio = radioHandle
        };

        var deviceInfo = CreateDeviceInfo();
        var deviceFindHandle = BluetoothFindFirstDevice(ref searchParams, ref deviceInfo);
        if (deviceFindHandle == IntPtr.Zero)
        {
            return new BluetoothProfileResetResult(false, $"蓝牙音频服务重置跳过：没有枚举到已配对设备，错误 {Marshal.GetLastWin32Error()}。");
        }

        try
        {
            do
            {
                if (!IsDeviceNameMatch(deviceInfo.Name, deviceName))
                {
                    deviceInfo = CreateDeviceInfo();
                    continue;
                }

                var service = AudioSourceServiceClass;
                var disableCode = BluetoothSetServiceState(radioHandle, ref deviceInfo, ref service, BluetoothServiceDisable);
                Thread.Sleep(900);

                uint enableCode = ErrorServiceDoesNotExist;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    service = AudioSourceServiceClass;
                    enableCode = BluetoothSetServiceState(radioHandle, ref deviceInfo, ref service, BluetoothServiceEnable);
                    if (IsAcceptableServiceStateResult(enableCode))
                    {
                        break;
                    }

                    Thread.Sleep(700);
                }

                if (IsAcceptableServiceStateResult(disableCode) && IsAcceptableServiceStateResult(enableCode))
                {
                    return new BluetoothProfileResetResult(
                        true,
                        $"已重置 {deviceInfo.Name} 的蓝牙音频服务：disable={FormatCode(disableCode)}, enable={FormatCode(enableCode)}。");
                }

                if (disableCode == ErrorServiceDoesNotExist || enableCode == ErrorServiceDoesNotExist)
                {
                    return new BluetoothProfileResetResult(
                        false,
                        $"已找到设备 {deviceInfo.Name}，但系统报告手机不支持 Audio Source 服务：disable={FormatCode(disableCode)}, enable={FormatCode(enableCode)}。");
                }

                return new BluetoothProfileResetResult(
                    false,
                    $"已找到设备 {deviceInfo.Name}，但蓝牙音频服务重置失败：disable={FormatCode(disableCode)}, enable={FormatCode(enableCode)}。");
            }
            while (BluetoothFindNextDevice(deviceFindHandle, ref deviceInfo));
        }
        finally
        {
            BluetoothFindDeviceClose(deviceFindHandle);
        }

        return new BluetoothProfileResetResult(false, $"蓝牙音频服务重置跳过：当前蓝牙适配器下未找到 {deviceName}。");
    }

    private static BluetoothDeviceInfo CreateDeviceInfo()
    {
        return new BluetoothDeviceInfo
        {
            DwSize = Marshal.SizeOf<BluetoothDeviceInfo>()
        };
    }

    private static bool IsAcceptableServiceStateResult(uint code)
    {
        return code is ErrorSuccess or EInvalidArg;
    }

    private static string FormatCode(uint code)
    {
        return code switch
        {
            ErrorSuccess => "OK",
            EInvalidArg => "AlreadySet",
            ErrorServiceDoesNotExist => "ServiceMissing",
            _ => code.ToString()
        };
    }

    private static bool IsDeviceNameMatch(string? candidate, string target)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public int DwSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public int DwSize;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnAuthenticated;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnRemembered;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnUnknown;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ReturnConnected;

        [MarshalAs(UnmanagedType.Bool)]
        public bool IssueInquiry;

        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public int DwSize;
        public ulong Address;
        public uint ClassOfDevice;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Connected;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Remembered;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Authenticated;

        public SystemTime LastSeen;
        public SystemTime LastUsed;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string Name;
    }

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParams searchParams, out IntPtr radioHandle);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr findHandle, out IntPtr radioHandle);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr findHandle);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BluetoothDeviceSearchParams searchParams, ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr findHandle, ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr findHandle);

    [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
    private static extern uint BluetoothSetServiceState(
        IntPtr radioHandle,
        ref BluetoothDeviceInfo deviceInfo,
        ref Guid serviceGuid,
        uint serviceFlags);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}