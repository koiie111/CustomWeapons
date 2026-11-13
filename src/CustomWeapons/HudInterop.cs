using System.Collections.Immutable;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CustomWeapons.Core;
using CustomWeapons.Hud.Api;
using Microsoft.Extensions.Logging;

namespace CustomWeapons;

public sealed partial class CustomWeaponsPlugin
{
    private sealed record HudWaiter(HudRefreshRequest Request, Action<HudRefreshResult> Callback, long Sequence, int Generation, Session Session);
    private readonly List<HudWaiter> _hudWaiters = [];
    private long _hudSequence;
    private bool _hudDatabaseReady;
    private ImmutableArray<HudModel> _hudModels = [];
    private HudBridge? _hudBridge;

    private sealed class HudBridge(CustomWeaponsPlugin owner) : IHudBridge
    {
        public HudSnapshot GetSnapshot() => owner.HudSnapshot();
        public void RequestRefresh(HudRefreshRequest request, Action<HudRefreshResult> completed) => owner.HudRequest(request, completed);
    }
    private void RebuildHudModels()
    {
        _hudModels = Config.Weapons.SelectMany(w => w.Value.Skins.Select(s => new HudModel(w.Key, s.Key, s.Value.Name, s.Value.Model, s.Value.Private, _precached.Contains(s.Value.Model))))
            .OrderBy(s=>s.Weapon,StringComparer.Ordinal).ThenBy(s=>s.Id,StringComparer.Ordinal).ToImmutableArray();
    }
    private HudSnapshot HudSnapshot() => new(_active, _lifecycle?.MapReady == true, Config.WebsiteSelectionsEnabled, Config.ServerId, _hudDatabaseReady, _hudDatabaseReady ? _catalog?.DatabaseServerUuid : null, _hudDatabaseReady ? _catalog?.DatabaseName : null, _hudModels);
    private void HudRequest(HudRefreshRequest request, Action<HudRefreshResult> completed)
    {
        if (completed == null) return;
        if (request == null) { Deliver(completed,HudRefreshResult.Deferred); return; }
        if (!_active || _lifecycle?.MapReady != true || !Config.WebsiteSelectionsEnabled || request.Slot is < 0 or > 63 || request.SteamId == 0 || request.Controller == 0 || request.Team is not (2 or 3)
            || string.IsNullOrEmpty(request.Weapon) || request.ExpectedModel == null || !Config.Weapons.TryGetValue(request.Weapon, out var weapon)
            || (request.ExpectedModel.Length != 0 && !weapon.Skins.ContainsKey(request.ExpectedModel)))
        { Deliver(completed,HudRefreshResult.Deferred); return; }
        var player = Utilities.GetPlayerFromSlot(request.Slot);
        if (player is not { IsValid:true } || player.SteamID != request.SteamId || player.EntityHandle.Raw != request.Controller || player.TeamNum != request.Team
            || EnsureSession(player) is not { } session)
        { Deliver(completed,HudRefreshResult.Deferred); return; }
        var replaced = _hudWaiters.Where(w => w.Request.Slot == request.Slot && w.Request.Controller == request.Controller && w.Request.Weapon == request.Weapon).ToArray();
        foreach (var old in replaced) _hudWaiters.Remove(old);
        if (_hudWaiters.Count >= 512) { foreach (var old in replaced) Deliver(old.Callback,HudRefreshResult.Superseded); Deliver(completed,HudRefreshResult.Deferred); return; }
        _hudWaiters.Add(new(request, completed, _hudSequence + 1, _mapGeneration, session));
        foreach (var old in replaced) Deliver(old.Callback,HudRefreshResult.Superseded);
        RequestRefresh();
    }
    private void CompleteHudRefresh(long sequence, int generation, DatabaseSnapshot? snapshot)
    {
        var ready = _hudWaiters.Where(w => w.Sequence <= sequence).ToArray();
        foreach (var waiter in ready)
        {
            _hudWaiters.Remove(waiter);
            var r = waiter.Request;
            if (!_active || generation != _mapGeneration || waiter.Generation != generation || _lifecycle?.MapReady != true ||
                !_sessions.TryGetValue(r.Slot, out var session) || !ReferenceEquals(session, waiter.Session) ||
                new CHandle<CCSPlayerController>(r.Controller).Value is not { IsValid:true } player || player.SteamID != r.SteamId || player.TeamNum != r.Team)
            { Deliver(waiter.Callback,HudRefreshResult.Deferred); continue; }
            if (snapshot == null) { Deliver(waiter.Callback,HudRefreshResult.Error); continue; }
            if (!_hudDatabaseReady) { Deliver(waiter.Callback,HudRefreshResult.Deferred); continue; }
            if (!snapshot.WebsiteSelections.TryGetValue((session.SteamId,r.Team,r.Weapon),out var row) || row.Model != r.ExpectedModel)
            { Deliver(waiter.Callback,HudRefreshResult.Superseded); continue; }
            if (Selected(session,r.Weapon) != r.ExpectedModel)
            { Deliver(waiter.Callback,HudRefreshResult.Deferred); continue; }
            Deliver(waiter.Callback,HudRefreshResult.Reconciled);
        }
    }
    private void Deliver(Action<HudRefreshResult> callback, HudRefreshResult result)
    {
        try { callback(result); } catch (Exception error) { Logger.LogWarning("HUD refresh callback failed ({Type}).",error.GetType().Name); }
    }
    private void CancelHudWaiters()
    {
        var waiters=_hudWaiters.ToArray(); _hudWaiters.Clear();
        foreach(var waiter in waiters) Deliver(waiter.Callback,HudRefreshResult.Deferred);
    }
}

