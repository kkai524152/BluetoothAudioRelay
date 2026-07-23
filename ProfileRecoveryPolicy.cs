namespace BluetoothAudioRelay;

internal static class ProfileRecoveryPolicy
{
    public static bool IsUnexpectedDisconnect(
        RelayDeviceState state,
        bool isEnabled,
        bool hasActiveConnection,
        bool autoConnectSuppressed)
    {
        return !autoConnectSuppressed &&
               (state == RelayDeviceState.Playing || isEnabled || hasActiveConnection);
    }

    public static bool ShouldResetBeforeConnect(
        bool markedForRecovery,
        bool automatic,
        bool hasOpenedConnection)
    {
        return markedForRecovery || (!automatic && hasOpenedConnection);
    }
}
