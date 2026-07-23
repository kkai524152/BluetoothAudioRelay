using System.Runtime.InteropServices;

namespace BluetoothAudioRelay;

internal sealed record BluetoothProfileResetResult(
    bool Success,
    string Message,
    bool DeviceFound = false);

internal static class BluetoothProfileReset
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorServiceDoesNotExist = 1060;
    private const uint EInvalidArg = 0x80070057;
    private const uint BluetoothServiceDisable = 0x00000000;
    private const uint BluetoothServiceEnable = 0x00000001;

    private static readonly Guid AudioSourceServiceClass = new("0000110a-0000-1000-8000-00805f9b34fb");

    private sealed record NameResolutionResult(bool Success, ulong Address, string Message);

    public static BluetoothProfileResetResult TryResetAudioSourceService(string deviceName, ulong? bluetoothAddress)
    {
        if (string.IsNullOrWhiteSpace(deviceName) && bluetoothAddress is null)
        {
            return new BluetoothProfileResetResult(
                false,
                "蓝牙音频服务重置跳过：设备名称和蓝牙地址均为空。");
        }

        BluetoothProfileResetResult? addressResult = null;
        if (bluetoothAddress is not null)
        {
            addressResult = TryResetByAddress(deviceName, bluetoothAddress.Value);
            if (addressResult.Success || addressResult.DeviceFound || string.IsNullOrWhiteSpace(deviceName))
            {
                return addressResult;
            }
        }

        var nameResolution = TryResolveUniqueExactNameAddress(deviceName);
        if (!nameResolution.Success)
        {
            return addressResult is null
                ? new BluetoothProfileResetResult(false, nameResolution.Message)
                : addressResult with { Message = $"{addressResult.Message} {nameResolution.Message}" };
        }

        var nameResult = TryResetByAddress(deviceName, nameResolution.Address);
        return nameResult with
        {
            Message = $"已通过唯一精确设备名定位 {deviceName}（{FormatAddress(nameResolution.Address)}）。{nameResult.Message}"
        };
    }

    private static NameResolutionResult TryResolveUniqueExactNameAddress(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return new NameResolutionResult(false, 0, "蓝牙音频服务重置跳过：设备名称为空。");
        }

        var radioParams = new BluetoothFindRadioParams
        {
            DwSize = Marshal.SizeOf<BluetoothFindRadioParams>()
        };
        var radioFindHandle = BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFindHandle == IntPtr.Zero)
        {
            return new NameResolutionResult(
                false,
                0,
                $"蓝牙音频服务重置跳过：未找到本机蓝牙适配器，错误 {Marshal.GetLastWin32Error()}。");
        }

        var candidates = new List<(ulong Address, string? Name)>();
        try
        {
            do
            {
                try
                {
                    CollectPairedDevices(radioHandle, candidates);
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

        var matches = FindExactNameMatchAddresses(candidates, deviceName);
        if (matches.Count == 1)
        {
            return new NameResolutionResult(true, matches[0], "");
        }

        return matches.Count == 0
            ? new NameResolutionResult(
                false,
                0,
                $"蓝牙音频服务重置跳过：未找到名称完全匹配的已配对设备 {deviceName}。")
            : new NameResolutionResult(
                false,
                0,
                $"蓝牙音频服务重置跳过：发现 {matches.Count} 台同名蓝牙设备，为避免误操作未执行重置。");
    }

    private static void CollectPairedDevices(
        IntPtr radioHandle,
        ICollection<(ulong Address, string? Name)> candidates)
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
            return;
        }

        try
        {
            do
            {
                candidates.Add((deviceInfo.Address, deviceInfo.Name));
                deviceInfo = CreateDeviceInfo();
            }
            while (BluetoothFindNextDevice(deviceFindHandle, ref deviceInfo));
        }
        finally
        {
            BluetoothFindDeviceClose(deviceFindHandle);
        }
    }

    internal static IReadOnlyList<ulong> FindExactNameMatchAddresses(
        IEnumerable<(ulong Address, string? Name)> candidates,
        string? deviceName)
    {
        var target = BluetoothDeviceIdentity.NormalizeName(deviceName);
        if (target.Length == 0)
        {
            return [];
        }

        return candidates
            .Where(candidate => BluetoothDeviceIdentity.NormalizeName(candidate.Name) == target)
            .GroupBy(candidate => CanonicalAddress(candidate.Address))
            .Select(group => group.First().Address)
            .ToArray();
    }

    private static BluetoothProfileResetResult TryResetByAddress(string deviceName, ulong bluetoothAddress)
    {

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
                    var result = TryResetOnRadio(radioHandle, deviceName, bluetoothAddress);
                    if (result.Success || result.DeviceFound)
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

        return new BluetoothProfileResetResult(
            false,
            $"蓝牙音频服务重置跳过：未在已配对设备中找到 {deviceName}（{FormatAddress(bluetoothAddress)}）。");
    }

    private static BluetoothProfileResetResult TryResetOnRadio(
        IntPtr radioHandle,
        string deviceName,
        ulong bluetoothAddress)
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
                if (!IsAddressMatch(deviceInfo.Address, bluetoothAddress))
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
                        $"已重置 {deviceInfo.Name} 的蓝牙音频服务：disable={FormatCode(disableCode)}, enable={FormatCode(enableCode)}。",
                        true);
                }

                if (disableCode == ErrorServiceDoesNotExist || enableCode == ErrorServiceDoesNotExist)
                {
                    return new BluetoothProfileResetResult(
                        false,
                        $"已找到设备 {deviceInfo.Name}，但系统报告手机不支持 Audio Source 服务：disable={FormatCode(disableCode)}, enable={FormatCode(enableCode)}。",
                        true);
                }

                return new BluetoothProfileResetResult(
                    false,
                    $"已找到设备 {deviceInfo.Name}，但蓝牙音频服务重置失败：disable={FormatCode(disableCode)}, enable={FormatCode(enableCode)}。",
                    true);
            }
            while (BluetoothFindNextDevice(deviceFindHandle, ref deviceInfo));
        }
        finally
        {
            BluetoothFindDeviceClose(deviceFindHandle);
        }

        return new BluetoothProfileResetResult(
            false,
            $"蓝牙音频服务重置跳过：当前蓝牙适配器下未找到 {deviceName}（{FormatAddress(bluetoothAddress)}）。");
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

    private static string FormatAddress(ulong address)
    {
        var value = address.ToString("X12");
        return $"**:**:**:{value[6..8]}:{value[8..10]}:{value[10..12]}";
    }

    internal static bool IsAddressMatch(ulong candidate, ulong target)
    {
        return candidate == target || ReverseBluetoothAddress(candidate) == target;
    }

    private static ulong CanonicalAddress(ulong address)
    {
        return Math.Min(address, ReverseBluetoothAddress(address));
    }

    private static ulong ReverseBluetoothAddress(ulong address)
    {
        ulong reversed = 0;
        for (var index = 0; index < 6; index++)
        {
            reversed = (reversed << 8) | ((address >> (index * 8)) & 0xff);
        }

        return reversed;
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
