namespace BluetoothAudioRelay.Tests;

public sealed class BluetoothDeviceIdentityTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", 0xAABBCCDDEEFFUL)]
    [InlineData("aa-bb-cc-dd-ee-ff", 0xAABBCCDDEEFFUL)]
    [InlineData("AABBCCDDEEFF", 0xAABBCCDDEEFFUL)]
    [InlineData("Bluetooth#Dev_AABBCCDDEEFF", 0xAABBCCDDEEFFUL)]
    public void TryParseBluetoothAddress_ParsesCommonFormats(string value, ulong expected)
    {
        var parsed = BluetoothDeviceIdentity.TryParseBluetoothAddress(value, out var address);

        Assert.True(parsed);
        Assert.Equal(expected, address);
    }

    [Fact]
    public void TryParseBluetoothAddress_UsesRemoteAddressFromCompositeId()
    {
        const string id = "Bluetooth#Bluetooth00:11:22:33:44:55-AA:BB:CC:DD:EE:FF";

        var parsed = BluetoothDeviceIdentity.TryParseBluetoothAddress(id, out var address);

        Assert.True(parsed);
        Assert.Equal(0xAABBCCDDEEFFUL, address);
    }

    [Fact]
    public void TryParseBluetoothAddress_ParsesAudioPlaybackEndpointId()
    {
        const string id = @"\\?\BTHENUM#{0000110a-0000-1000-8000-00805f9b34fb}_VID&0001001d_PID&0000#7&2e27dd6b&0&4C92D2BDDC10_C00000000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\SNK";

        var parsed = BluetoothDeviceIdentity.TryParseBluetoothAddress(id, out var address);

        Assert.True(parsed);
        Assert.Equal(0x4C92D2BDDC10UL, address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-device")]
    public void TryParseBluetoothAddress_RejectsInvalidValues(string? value)
    {
        Assert.False(BluetoothDeviceIdentity.TryParseBluetoothAddress(value, out _));
    }

    [Fact]
    public void NormalizeName_IgnoresWhitespaceAndCase()
    {
        Assert.Equal("MYPHONE", BluetoothDeviceIdentity.NormalizeName(" My Phone "));
    }

    [Fact]
    public void StableKey_PrefersContainerAcrossEndpointReenumeration()
    {
        var containerId = Guid.Parse("3C6545E4-3E3D-5217-A0F7-57950ADF1D00");

        var key = BluetoothDeviceIdentity.BuildStableKey(
            containerId,
            0x4C92D2BDDC10UL,
            "endpoint-id");

        Assert.Equal("container:3c6545e43e3d5217a0f757950adf1d00", key);
    }

    [Fact]
    public void ProfileResetAddressMatch_AcceptsNativeAndReversedByteOrder()
    {
        const ulong address = 0xAABBCCDDEEFFUL;

        Assert.True(BluetoothProfileReset.IsAddressMatch(address, address));
        Assert.True(BluetoothProfileReset.IsAddressMatch(0xFFEEDDCCBBAAUL, address));
        Assert.False(BluetoothProfileReset.IsAddressMatch(0x001122334455UL, address));
    }

    [Fact]
    public void ProfileResetNameFallback_AcceptsOneExactNormalizedDevice()
    {
        (ulong Address, string? Name)[] candidates =
        [
            (0xAABBCCDDEEFFUL, "My Phone"),
            (0xAABBCCDDEEFFUL, "My Phone"),
            (0x001122334455UL, "My Phone Pro")
        ];

        var matches = BluetoothProfileReset.FindExactNameMatchAddresses(candidates, " myphone ");

        Assert.Equal([0xAABBCCDDEEFFUL], matches);
    }

    [Fact]
    public void ProfileResetNameFallback_RejectsAmbiguousOrPartialNames()
    {
        (ulong Address, string? Name)[] candidates =
        [
            (0xAABBCCDDEEFFUL, "My Phone"),
            (0x001122334455UL, "MY PHONE")
        ];

        Assert.Equal(2, BluetoothProfileReset.FindExactNameMatchAddresses(candidates, "My Phone").Count);
        Assert.Empty(BluetoothProfileReset.FindExactNameMatchAddresses(candidates, "Phone"));
    }
}
