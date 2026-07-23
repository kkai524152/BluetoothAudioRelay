using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;

namespace BluetoothAudioRelay;

internal static partial class BluetoothDeviceIdentity
{
    public const string DeviceAddressProperty = "System.Devices.Aep.DeviceAddress";
    public const string ContainerIdProperty = "System.Devices.ContainerId";

    public static IReadOnlyList<string> RequestedProperties { get; } =
    [
        DeviceAddressProperty,
        ContainerIdProperty
    ];

    public static RemoteAudioDevice Create(DeviceInformation info)
    {
        var identity = Read(info);
        return new RemoteAudioDevice(
            info.Id,
            info.Name,
            identity.StableKey,
            identity.DeviceAddress,
            identity.BluetoothAddress,
            identity.ContainerId);
    }

    public static void Update(RemoteAudioDevice device, DeviceInformation info)
    {
        var identity = Read(info);
        device.UpdateIdentity(
            info.Id,
            info.Name,
            identity.StableKey,
            identity.DeviceAddress,
            identity.BluetoothAddress,
            identity.ContainerId);
        device.IsAvailable = true;
        if (device.State == RelayDeviceState.Unavailable)
        {
            device.SetState(RelayDeviceState.Ready);
        }
    }

    public static bool Matches(RemoteAudioDevice device, DeviceInformation info)
    {
        if (device.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var identity = Read(info);
        return !identity.StableKey.StartsWith("id:", StringComparison.Ordinal) &&
               device.StableKey.Equals(identity.StableKey, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesPreference(RemoteAudioDevice device, string? preferredKey, string? preferredName)
    {
        if (!string.IsNullOrWhiteSpace(preferredKey) &&
            device.StableKey.Equals(preferredKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Name matching only migrates settings written by older versions. Destructive
        // Bluetooth profile operations always require a real hardware address.
        return string.IsNullOrWhiteSpace(preferredKey) &&
               NormalizeName(device.DisplayName) == NormalizeName(preferredName);
    }

    internal static bool TryParseBluetoothAddress(string? value, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = value.Trim();
        if (compact.Length == 12 &&
            ulong.TryParse(compact, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
        {
            return true;
        }

        var matches = MacAddressRegex().Matches(value);
        var tokenMatches = DeviceTokenRegex().Matches(value);
        var candidate = matches.Count > 0
            ? matches[^1].Value
            : tokenMatches.Count > 0
                ? tokenMatches[^1].Groups[1].Value
                : string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var hexadecimal = new string(candidate.Where(Uri.IsHexDigit).ToArray());
        return hexadecimal.Length == 12 &&
               ulong.TryParse(hexadecimal, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    internal static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(static ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
    }

    private static DeviceIdentity Read(DeviceInformation info)
    {
        var rawAddress = info.Properties.TryGetValue(DeviceAddressProperty, out var addressValue)
            ? addressValue
            : null;
        var deviceAddress = rawAddress?.ToString();
        ulong? bluetoothAddress = rawAddress is ulong numericAddress
            ? numericAddress
            : TryParseBluetoothAddress(deviceAddress, out var parsedAddress) ||
                               TryParseBluetoothAddress(info.Id, out parsedAddress)
                ? parsedAddress
                : null;
        var containerId = ReadGuid(info, ContainerIdProperty);
        var stableKey = BuildStableKey(containerId, bluetoothAddress, info.Id);

        return new DeviceIdentity(stableKey, deviceAddress, bluetoothAddress, containerId);
    }

    internal static string BuildStableKey(Guid? containerId, ulong? bluetoothAddress, string deviceId)
    {
        // A container survives audio endpoint re-enumeration and keeps settings
        // written by earlier versions associated with the same phone.
        return containerId is not null
            ? $"container:{containerId.Value:N}"
            : bluetoothAddress is not null
                ? $"bt:{bluetoothAddress.Value:X12}"
                : $"id:{deviceId}";
    }

    private static string? ReadString(DeviceInformation info, string key)
    {
        return info.Properties.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static Guid? ReadGuid(DeviceInformation info, string key)
    {
        if (!info.Properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value is Guid guid
            ? guid
            : Guid.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    [GeneratedRegex(@"(?i)(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}")]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex(@"(?i)(?:dev_|bluetoothdevice_|&0&)([0-9a-f]{12})(?=[_#\\]|$)")]
    private static partial Regex DeviceTokenRegex();

    private sealed record DeviceIdentity(
        string StableKey,
        string? DeviceAddress,
        ulong? BluetoothAddress,
        Guid? ContainerId);
}
