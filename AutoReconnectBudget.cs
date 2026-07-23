namespace BluetoothAudioRelay;

/// <summary>
/// Limits one automatic reconnect episode to a single connection round.
/// The round itself still contains the normal per-connection attempts.
/// </summary>
internal sealed class AutoReconnectBudget
{
    // OpenDeviceWithAttemptsAsync already performs up to two attempts. A second
    // automatic round would turn a single disconnect into an unbounded loop.
    public const int MaximumAutomaticRounds = 1;

    public int FailedRounds { get; private set; }

    public bool IsExhausted => FailedRounds >= MaximumAutomaticRounds;

    public void RecordFailure()
    {
        FailedRounds = Math.Min(FailedRounds + 1, MaximumAutomaticRounds);
    }

    public bool Reset()
    {
        if (FailedRounds == 0)
        {
            return false;
        }

        FailedRounds = 0;
        return true;
    }
}
