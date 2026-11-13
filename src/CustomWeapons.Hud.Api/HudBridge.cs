using System.Collections.Immutable;

namespace CustomWeapons.Hud.Api;

// A single copy of this assembly must be loaded by CounterStrikeSharp's shared loader.
public sealed record HudModel(string Weapon, string Id, string Name, string Resource, bool Private, bool Precached);
public sealed record HudSnapshot(bool Loaded, bool MapReady, bool WebsiteEnabled, uint ServerId, bool DatabaseReady, string? DatabaseServerUuid, string? DatabaseName, ImmutableArray<HudModel> Models);
public enum HudRefreshResult { Reconciled, Deferred, Error, Superseded }
public sealed record HudRefreshRequest(int Slot, ulong SteamId, uint Controller, int Team, string Weapon, string ExpectedModel);

/// <summary>All methods and callbacks are game-thread only. Reconciled means DB selection and CW caches agree, not that a live weapon was visually confirmed.</summary>
public interface IHudBridge
{
    HudSnapshot GetSnapshot();
    void RequestRefresh(HudRefreshRequest request, Action<HudRefreshResult> completed);
}

/// <summary>Stable shared provider. Retained capability registrations hold only this singleton, never plugin instances.</summary>
public sealed class HudBridgeRegistry : IHudBridge
{
    public const string CapabilityName = "customweapons:hud:v1";
    public const int ContractVersion = 1;
    public static HudBridgeRegistry Instance { get; } = new();
    private IHudBridge? _current;
    private long _generation;
    private HudBridgeRegistry() { }
    public static IHudBridge Provider() => Instance;
    public long Generation => Volatile.Read(ref _generation);
    public void SetCurrent(IHudBridge service)
    {
        ArgumentNullException.ThrowIfNull(service);
        // Invalidate worker-side guards before publishing the replacement.
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _current, service);
    }
    public void ClearCurrent(IHudBridge service)
    {
        // Provider lifecycle is game-thread-only. Invalidate worker-side guards
        // before clearing; stale unloads must not change the current generation.
        if (!ReferenceEquals(Volatile.Read(ref _current), service)) return;
        Interlocked.Increment(ref _generation);
        Interlocked.CompareExchange(ref _current, null, service);
    }
    public HudSnapshot GetSnapshot() => Volatile.Read(ref _current)?.GetSnapshot() ?? new(false,false,false,0,false,null,null,[]);
    public void RequestRefresh(HudRefreshRequest request, Action<HudRefreshResult> completed)
    {
        var service = Volatile.Read(ref _current);
        var generation = Generation;
        var delivered = 0;
        void Deliver(HudRefreshResult result)
        {
            if (Interlocked.Exchange(ref delivered, 1) != 0) return;
            if (!ReferenceEquals(service, Volatile.Read(ref _current)) || generation != Generation) result = HudRefreshResult.Deferred;
            try { completed?.Invoke(result); } catch { /* Isolate consumer callbacks. */ }
        }
        if (service == null) { Deliver(HudRefreshResult.Deferred); return; }
        try { service.RequestRefresh(request, Deliver); } catch { Deliver(HudRefreshResult.Error); }
    }
}
