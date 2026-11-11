using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using CustomWeapons.Core;
using Microsoft.Extensions.Logging;

namespace CustomWeapons;

public sealed partial class CustomWeaponsPlugin
{
    private WebsiteSelection? ExactWebsitePreference(Session session, int team, string weapon)
    {
        WebsiteSelection? current = null;
        session.Access.Snapshot?.WebsiteSelections.TryGetValue((session.SteamId, team, weapon), out current);
        if (session.WebsiteOverrides.TryGetValue((team, weapon), out var pending))
        {
            if (current == null || pending.Revision > current.Revision) return pending;
            session.WebsiteOverrides.Remove((team, weapon));
        }
        return current;
    }
    private WebsiteSelection? WebsitePreference(Session session, int team, string weapon) =>
        ExactWebsitePreference(session, team, weapon) ?? (team != 0 ? ExactWebsitePreference(session, 0, weapon) : null);

    private string? WebsiteModel(Session session, int team, string weapon) =>
        session.WebsitePending.TryGetValue((team, weapon), out var pending) ? pending :
        WebsitePreference(session, team, weapon)?.Model ?? _selections.Get(session.SteamId, weapon);

    private void RegisterWebsiteRefreshListeners()
    {
        void Refresh(CCSPlayerController? player)
        {
            var session = EnsureSession(player);
            if (session == null || Now < session.NextWebsiteRefresh) return;
            session.NextWebsiteRefresh = Now + 2;
            RequestRefresh();
        }
        AddCommandListener("css_wp", (player, _) => { Refresh(player); return HookResult.Continue; });
        foreach (var command in new[] { "say", "say_team" }) AddCommandListener(command, (player, info) =>
        {
            if (info.ArgCount > 1 && info.GetArg(1).Trim().ToLowerInvariant() is "!wp" or "/wp") Refresh(player);
            return HookResult.Continue;
        });
    }

    private void ApplyWebsiteChanges(CCSPlayerController player, Session session)
    {
        if (EnsureSession(player) is not { } current || !ReferenceEquals(session, current)) return;
        var active = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        var heldCategory = active is { IsValid: true }
            ? WeaponNames.Resolve(active.DesignerName, active.AttributeManager.Item.ItemDefinitionIndex, Config.Weapons.Keys) : null;
        var heldChanged = false;
        foreach (var weapon in Config.Weapons.Keys)
        {
            var choice = Selected(session, weapon); var key = (session.Team, weapon);
            if (session.WebsiteEffective.TryGetValue(key, out var previous) && previous == choice) continue;
            session.WebsiteEffective[key] = choice;
            ApplyInventory(player, weapon);
            heldChanged |= weapon == heldCategory;
        }
        if (heldChanged) QueueHeldCheck(player, true, changedCategory: heldCategory);
    }

    private void SelectWebsite(CCSPlayerController player, Session session, string weapon, string? id)
    {
        if (session.Team is not (2 or 3)) { Tell(player, "Сначала выберите команду КТ или Т."); return; }
        if (_repository is not IWebsiteSelectionRepository repository || !session.WebsiteWrites.Add(weapon))
        {
            Tell(player, "Выбор ещё сохраняется. Повторите позже."); return;
        }
        var team = session.Team; var expected = ExactWebsitePreference(session, team, weapon)?.Revision ?? 0;
        var generation = _mapGeneration; var model = id ?? ""; var slot = player.Slot;
        var menu = MenuManager.GetActiveMenu(player);
        session.NextChange = Now + Config.CooldownSeconds;
        // Selection/access were validated by Select. Show the same-side choice
        // immediately while the existing revision-checked transaction commits.
        // It is not a saved override until SQL acknowledges it.
        session.WebsitePending[(team, weapon)] = model;
        ApplyWebsiteChanges(player, session);
        Tell(player, "Сохранение выбора…");
        _ = Task.Run(async () =>
        {
            long? revision = null; Exception? failure = null;
            try { revision = await repository.SaveSelectionAsync(session.SteamId, team, weapon, model, expected, _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception error) { failure = error; }
            if (!_active) return;
            Server.NextWorldUpdate(() =>
            {
                session.WebsiteWrites.Remove(weapon);
                session.WebsitePending.Remove((team, weapon));
                if (!_active || generation != _mapGeneration || _lifecycle?.MapReady != true ||
                    !_sessions.TryGetValue(slot, out var live) || !ReferenceEquals(live, session) ||
                    new CHandle<CCSPlayerController>(session.Controller).Value is not { IsValid: true } owner ||
                    owner.SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture) != session.SteamId) return;
                if (failure != null || revision == null)
                {
                    if (failure != null) Logger.LogWarning("Website selection write failed ({Type}).", failure.GetType().Name);
                    ApplyWebsiteChanges(owner, session);
                    Tell(owner, failure != null ? "Не удалось сохранить выбор. Повторите позже." : "Выбор изменился на сайте. Откройте меню заново.");
                    RequestRefresh(); return;
                }
                session.WebsiteOverrides[(team, weapon)] = new(session.SteamId, weapon, model, revision.Value, team);
                _selections.Set(session.SteamId, weapon, model);
                ApplyWebsiteChanges(owner, session);
                Tell(owner, "Выбор сохранён.");
                if (menu != null && ReferenceEquals(MenuManager.GetActiveMenu(owner), menu)) OpenSkins(owner, weapon);
                RequestRefresh();
            });
        });
    }
}
