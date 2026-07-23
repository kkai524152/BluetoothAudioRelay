namespace BluetoothAudioRelay.Tests;

public sealed class AutoReconnectBudgetTests
{
    [Fact]
    public void FailedRound_ExhaustsAutomaticReconnectBudget()
    {
        var budget = new AutoReconnectBudget();

        budget.RecordFailure();
        budget.RecordFailure();

        Assert.True(budget.IsExhausted);
        Assert.Equal(AutoReconnectBudget.MaximumAutomaticRounds, budget.FailedRounds);
    }

    [Fact]
    public void Reset_AllowsReconnectAfterManualRequestOrDeviceReappearance()
    {
        var budget = new AutoReconnectBudget();
        budget.RecordFailure();

        var changed = budget.Reset();

        Assert.True(changed);
        Assert.False(budget.IsExhausted);
        Assert.Equal(0, budget.FailedRounds);
        Assert.False(budget.Reset());
    }
}
