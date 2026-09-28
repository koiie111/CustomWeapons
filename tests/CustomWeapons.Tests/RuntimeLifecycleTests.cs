using CustomWeapons.Core;
using Xunit;

namespace CustomWeapons.Tests;

public sealed class RuntimeLifecycleTests
{
    [Fact]
    public void ColdLoadNeverTouchesUninitializedEngineOrQueuesEventHooks()
    {
        var globalsReady = false;
        var hooks = 0;
        var scans = 0;
        var runtime = new RuntimeLifecycle(
            () => { Assert.True(globalsReady); hooks++; },
            () => { Assert.True(globalsReady); scans++; });
        runtime.Load(false, _ => throw new Exception("Cold load must wait for OnMapStart"));
        Assert.False(runtime.MapReady);
        Assert.Equal(0, hooks);
        Assert.Equal(0, scans);
        globalsReady = true;
        runtime.MapStarted();
        Assert.True(runtime.MapReady);
        Assert.Equal(1, hooks);
        Assert.Equal(1, scans);
        runtime.MapEnded();
        Assert.False(runtime.MapReady);
        runtime.MapStarted();
        Assert.Equal(1, hooks);
        Assert.Equal(2, scans);
    }

    [Fact]
    public void FailedColdLoadCleanupLeavesNoPendingGameplayCallbacks()
    {
        var runtime = new RuntimeLifecycle(
            () => throw new Exception("Must not register native callbacks"),
            () => throw new Exception("Must not access players"));
        runtime.Load(false, _ => throw new Exception("Must not enqueue"));
        runtime.Stop();
        runtime.MapStarted();
        Assert.False(runtime.MapReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HotLoadIsDeferredAndCancelledWhenUnloaded(bool unload)
    {
        var hooks = 0;
        var scans = 0;
        Action? queued = null;
        var runtime = new RuntimeLifecycle(() => hooks++, () => scans++);
        runtime.Load(true, action => queued = action);
        Assert.Equal(0, hooks);
        Assert.NotNull(queued);
        if (unload) runtime.Stop();
        queued();
        Assert.Equal(unload ? 0 : 1, hooks);
        Assert.Equal(unload ? 0 : 1, scans);
        Assert.Equal(!unload, runtime.MapReady);
    }

    [Fact]
    public void PartialInitializationFailureDoesNotRegisterDuplicateHooksOnNextMap()
    {
        var attempts = 0;
        var runtime = new RuntimeLifecycle(() => { attempts++; throw new InvalidOperationException(); },
            () => throw new Exception("Initialization failed"));
        Assert.Throws<InvalidOperationException>(runtime.MapStarted);
        Assert.False(runtime.MapReady);
        runtime.MapStarted();
        Assert.Equal(1, attempts);
    }
}
