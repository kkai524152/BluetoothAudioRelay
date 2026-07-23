namespace BluetoothAudioRelay.Tests;

public sealed class SafetyAndVersionTests
{
    [Fact]
    public void ProfileReset_RefusesDestructiveOperationWithoutIdentity()
    {
        var result = BluetoothProfileReset.TryResetAudioSourceService("", null);

        Assert.False(result.Success);
        Assert.Contains("名称和蓝牙地址均为空", result.Message);
    }

    [Theory]
    [InlineData("v0.4.8", 0, 4, 8)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    [InlineData("V10.0", 10, 0, -1)]
    public void UpdateVersionParser_AcceptsTags(string tag, int major, int minor, int build)
    {
        var parsed = UpdateChecker.TryParseVersion(tag, out var version);

        Assert.True(parsed);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
    }

    [Fact]
    public void RemoteDevice_ExposesExplicitStateText()
    {
        var device = new RemoteAudioDevice("id", "手机", "bt:AABBCCDDEEFF");

        device.SetState(RelayDeviceState.Connecting);
        Assert.Equal("正在连接", device.ConnectionStateText);

        device.SetState(RelayDeviceState.Failed, "打开失败：DeniedBySystem");
        Assert.Equal("打开失败：DeniedBySystem", device.ConnectionStateText);
    }

    [Fact]
    public void AudioOutputMonitor_CanReadWindowsDefaultEndpointState()
    {
        using var monitor = new AudioOutputMonitor();

        Assert.False(string.IsNullOrWhiteSpace(monitor.GetDefaultOutputName()));
    }

    [Fact]
    public void DiagnosticReport_RedactsBluetoothAddress()
    {
        var device = new RemoteAudioDevice("id", "手机", "bt:AABBCCDDEEFF");

        var report = DiagnosticLog.BuildReport("扬声器", [device], new UserPreferences());

        Assert.DoesNotContain("AABBCCDDEEFF", report);
        Assert.Contains("******DDEEFF", report);
    }

    [Fact]
    public void ProfileRecoveryPolicy_MarksInterruptedRelay()
    {
        var cases = new[]
        {
            (RelayDeviceState.Playing, IsEnabled: false, HasActiveConnection: false),
            (RelayDeviceState.Ready, IsEnabled: true, HasActiveConnection: false),
            (RelayDeviceState.Ready, IsEnabled: false, HasActiveConnection: true)
        };

        foreach (var (state, isEnabled, hasActiveConnection) in cases)
        {
            Assert.True(ProfileRecoveryPolicy.IsUnexpectedDisconnect(
                state,
                isEnabled,
                hasActiveConnection,
                autoConnectSuppressed: false));
        }
    }

    [Fact]
    public void ProfileRecoveryPolicy_DoesNotMarkIdleOrManualStop()
    {
        Assert.False(ProfileRecoveryPolicy.IsUnexpectedDisconnect(
            RelayDeviceState.Ready,
            isEnabled: false,
            hasActiveConnection: false,
            autoConnectSuppressed: false));
        Assert.False(ProfileRecoveryPolicy.IsUnexpectedDisconnect(
            RelayDeviceState.Playing,
            isEnabled: true,
            hasActiveConnection: true,
            autoConnectSuppressed: true));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void ProfileRecoveryPolicy_SelectsPreResetPath(
        bool markedForRecovery,
        bool automatic,
        bool hasOpenedConnection,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProfileRecoveryPolicy.ShouldResetBeforeConnect(
                markedForRecovery,
                automatic,
                hasOpenedConnection));
    }
}
