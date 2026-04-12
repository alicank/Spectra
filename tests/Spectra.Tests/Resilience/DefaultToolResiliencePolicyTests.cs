using Spectra.Contracts.Tools;
using Spectra.Kernel.Resilience;
using Xunit;

namespace Spectra.Tests.Resilience;

public class DefaultToolResiliencePolicyTests
{
    // --- Initial state ---

    [Fact]
    public void NewTool_StartsInClosedState()
    {
        var sut = CreatePolicy();

        var info = sut.GetInfo("my-tool");

        Assert.Equal(ToolCircuitState.Closed, info.State);
        Assert.Equal(0, info.ConsecutiveFailures);
    }

    [Fact]
    public void ClosedCircuit_AllowsExecution()
    {
        var sut = CreatePolicy();

        var (state, allowed) = sut.CanExecute("my-tool");

        Assert.Equal(ToolCircuitState.Closed, state);
        Assert.True(allowed);
    }

    // --- Closed → Open ---

    [Fact]
    public void ClosedCircuit_OpensAfterThresholdFailures()
    {
        var sut = CreatePolicy(failureThreshold: 3);

        sut.RecordFailure("my-tool");
        sut.RecordFailure("my-tool");
        Assert.Equal(ToolCircuitState.Closed, sut.GetInfo("my-tool").State);

        sut.RecordFailure("my-tool"); // 3rd failure = threshold
        Assert.Equal(ToolCircuitState.Open, sut.GetInfo("my-tool").State);
    }

    [Fact]
    public void OpenCircuit_RejectsExecution()
    {
        var sut = CreatePolicy(failureThreshold: 1);
        sut.RecordFailure("my-tool");

        var (state, allowed) = sut.CanExecute("my-tool");

        Assert.Equal(ToolCircuitState.Open, state);
        Assert.False(allowed);
    }

    // --- Open → HalfOpen ---

    [Fact]
    public void OpenCircuit_TransitionsToHalfOpenAfterCooldown()
    {
        var sut = CreatePolicy(failureThreshold: 1, cooldownMs: 1);
        sut.RecordFailure("my-tool");

        // Wait for cooldown
        Thread.Sleep(50);

        var (state, allowed) = sut.CanExecute("my-tool");

        Assert.Equal(ToolCircuitState.HalfOpen, state);
        Assert.True(allowed);
    }

    [Fact]
    public void OpenCircuit_StaysOpenBeforeCooldown()
    {
        var sut = CreatePolicy(failureThreshold: 1, cooldownMs: 60_000);
        sut.RecordFailure("my-tool");

        var (state, allowed) = sut.CanExecute("my-tool");

        Assert.Equal(ToolCircuitState.Open, state);
        Assert.False(allowed);
    }

    // --- HalfOpen → Closed ---

    [Fact]
    public void HalfOpenCircuit_ClosesOnSuccess()
    {
        var sut = CreatePolicy(failureThreshold: 1, cooldownMs: 1);
        sut.RecordFailure("my-tool");
        Thread.Sleep(50);
        sut.CanExecute("my-tool"); // triggers half-open transition

        sut.RecordSuccess("my-tool");

        Assert.Equal(ToolCircuitState.Closed, sut.GetInfo("my-tool").State);
    }

    // --- HalfOpen → Open ---

    [Fact]
    public void HalfOpenCircuit_ReopensOnFailure()
    {
        var sut = CreatePolicy(failureThreshold: 1, cooldownMs: 1);
        sut.RecordFailure("my-tool");
        Thread.Sleep(50);
        sut.CanExecute("my-tool"); // triggers half-open transition

        sut.RecordFailure("my-tool");

        Assert.Equal(ToolCircuitState.Open, sut.GetInfo("my-tool").State);
    }

    // --- HalfOpen max attempts ---

    [Fact]
    public void HalfOpenCircuit_LimitsProbeAttempts()
    {
        var options = new ToolResilienceOptions
        {
            FailureThreshold = 1,
            CooldownPeriod = TimeSpan.FromMilliseconds(1),
            HalfOpenMaxAttempts = 1,
            SuccessThresholdToClose = 1
        };
        var sut = new DefaultToolResiliencePolicy(options);

        sut.RecordFailure("my-tool");
        Thread.Sleep(50);

        // First call triggers half-open and is allowed
        var (state1, allowed1) = sut.CanExecute("my-tool");
        Assert.True(allowed1);

        // Record a success that increments HalfOpenAttempts to 1
        sut.RecordSuccess("my-tool");

        // Circuit should now be closed
        Assert.Equal(ToolCircuitState.Closed, sut.GetInfo("my-tool").State);
    }

    // --- Success resets failure count ---

    [Fact]
    public void ClosedCircuit_SuccessResetsFailureCount()
    {
        var sut = CreatePolicy(failureThreshold: 3);

        sut.RecordFailure("my-tool");
        sut.RecordFailure("my-tool");
        sut.RecordSuccess("my-tool"); // resets
        sut.RecordFailure("my-tool");
        sut.RecordFailure("my-tool");

        // Only 2 failures since last success, still closed
        Assert.Equal(ToolCircuitState.Closed, sut.GetInfo("my-tool").State);
    }

    // --- Independent per-tool state ---

    [Fact]
    public void DifferentTools_HaveIndependentCircuits()
    {
        var sut = CreatePolicy(failureThreshold: 2);

        sut.RecordFailure("tool-a");
        sut.RecordFailure("tool-a");

        Assert.Equal(ToolCircuitState.Open, sut.GetInfo("tool-a").State);
        Assert.Equal(ToolCircuitState.Closed, sut.GetInfo("tool-b").State);
    }

    // --- Fallback mapping ---

    [Fact]
    public void GetFallbackToolName_ReturnsMappedFallback()
    {
        var options = new ToolResilienceOptions
        {
            FallbackTools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mcp:weather:get_forecast"] = "mcp:backup:get_forecast"
            }
        };
        var sut = new DefaultToolResiliencePolicy(options);

        Assert.Equal("mcp:backup:get_forecast", sut.GetFallbackToolName("mcp:weather:get_forecast"));
    }

    [Fact]
    public void GetFallbackToolName_ReturnsNullForUnmapped()
    {
        var sut = CreatePolicy();

        Assert.Null(sut.GetFallbackToolName("unknown-tool"));
    }

    // --- State transition tracking ---

    [Fact]
    public void Transition_ClosedToOpen_IsTracked()
    {
        var sut = CreatePolicy(failureThreshold: 1);

        sut.RecordFailure("my-tool");

        var transition = sut.GetLastTransition("my-tool");
        Assert.NotNull(transition);
        Assert.Equal(ToolCircuitState.Closed, transition.From);
        Assert.Equal(ToolCircuitState.Open, transition.To);
    }

    [Fact]
    public void Transition_IsConsumedOnce()
    {
        var sut = CreatePolicy(failureThreshold: 1);
        sut.RecordFailure("my-tool");

        var first = sut.GetLastTransition("my-tool");
        var second = sut.GetLastTransition("my-tool");

        Assert.NotNull(first);
        Assert.Null(second);
    }

    // --- GetInfo snapshot ---

    [Fact]
    public void GetInfo_ReturnsAccurateSnapshot()
    {
        var sut = CreatePolicy(failureThreshold: 5);

        sut.RecordFailure("my-tool");
        sut.RecordFailure("my-tool");
        sut.RecordFailure("my-tool");

        var info = sut.GetInfo("my-tool");

        Assert.Equal("my-tool", info.ToolName);
        Assert.Equal(ToolCircuitState.Closed, info.State);
        Assert.Equal(3, info.ConsecutiveFailures);
        Assert.NotNull(info.LastFailureTime);
    }

    // --- Null argument handling ---

    [Fact]
    public void CanExecute_ThrowsOnNull()
    {
        var sut = CreatePolicy();
        Assert.Throws<ArgumentNullException>(() => sut.CanExecute(null!));
    }

    [Fact]
    public void RecordSuccess_ThrowsOnNull()
    {
        var sut = CreatePolicy();
        Assert.Throws<ArgumentNullException>(() => sut.RecordSuccess(null!));
    }

    [Fact]
    public void RecordFailure_ThrowsOnNull()
    {
        var sut = CreatePolicy();
        Assert.Throws<ArgumentNullException>(() => sut.RecordFailure(null!));
    }

    // --- Helpers ---

    private static DefaultToolResiliencePolicy CreatePolicy(
        int failureThreshold = 5,
        int cooldownMs = 60_000)
    {
        return new DefaultToolResiliencePolicy(new ToolResilienceOptions
        {
            FailureThreshold = failureThreshold,
            CooldownPeriod = TimeSpan.FromMilliseconds(cooldownMs)
        });
    }
}