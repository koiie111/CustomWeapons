using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CustomWeapons.Core;
using Microsoft.Extensions.Logging;

namespace CustomWeapons;

public sealed partial class CustomWeaponsPlugin
{
    private bool IsFinishPilot(Session session) =>
        Config.CustomFinishPilotSteamId.Length == 17 && Config.CustomFinishPilotSteamId == session.SteamId;

    private IEnumerable<CustomFinish> FinishOptions(Session session) =>
        Config.WebsiteSelectionsEnabled && (Config.CustomFinishesEnabled || IsFinishPilot(session))
        && session.Access.Ready && session.Access.Snapshot is { } snapshot
        && snapshot.Access.TryGetValue(session.SteamId, out var grants)
            ? CustomFinish.Catalog.Where(finish => snapshot.Catalog.TryGetValue(finish.Key, out var row)
                && (row.Active || IsFinishPilot(session)) && !snapshot.Conflicts.Contains(finish.Key)
                && (SkinRules.HasAccess(grants, session.SteamId, finish.Key, Config.ServerId, DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    || SkinRules.HasAccess(grants, session.SteamId, finish.Key, 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds())))
            : [];

    private static CustomFinish? SelectedFinish(Session session, IEnumerable<CustomFinish> finishes)
    {
        if (session.Access.Snapshot is not { } snapshot) return null;
        foreach (var finish in finishes)
        {
            var row = snapshot.WeaponFinishes.GetValueOrDefault((session.SteamId, session.Team, finish.Definition))
                ?? snapshot.WeaponFinishes.GetValueOrDefault((session.SteamId, 0, finish.Definition));
            if (row?.Paint == finish.Paint && row.Name == finish.Name) return finish;
        }
        return null;
    }

    private void SelectFinish(CCSPlayerController player, CustomFinish finish)
    {
        var session = EnsureSession(player);
        if (session == null || session.Team is not (2 or 3) || _repository is not MySqlRepository repository
            || !FinishOptions(session).Contains(finish) || session.NextChange > Now
            || !session.WebsiteWrites.Add(finish.Weapon)) return;
        var slot = player.Slot;
        var team = session.Team;
        var pilot = IsFinishPilot(session);
        var serverId = Config.ServerId;
        session.NextChange = Now + Config.CooldownSeconds;
        _ = Task.Run(async () =>
        {
            bool saved = false;
            try { saved = await repository.EquipFinishAsync(session.SteamId, team, finish, serverId, pilot, _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception error) { Logger.LogWarning("Custom finish save failed ({Type}).", error.GetType().Name); }
            if (!_active) return;
            Server.NextWorldUpdate(() =>
            {
                session.WebsiteWrites.Remove(finish.Weapon);
                var current = Utilities.GetPlayerFromSlot(slot);
                if (!_active || current is not { IsValid: true } || current.EntityHandle.Raw != session.Controller
                    || !_sessions.TryGetValue(slot, out var live) || !ReferenceEquals(session, live)) return;
                Tell(current, saved ? "Покрытие выбрано." : "Покрытие недоступно или выбор не сохранился.");
                if (saved) RequestRefresh();
            });
        });
    }
}
