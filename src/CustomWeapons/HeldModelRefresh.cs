using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CustomWeapons.Core;
using Microsoft.Extensions.Logging;

namespace CustomWeapons;

public sealed partial class CustomWeaponsPlugin
{
    private sealed class HeldCheck(uint pawn, int map)
    {
        public readonly uint Pawn = pawn;
        public readonly int Map = map;
        public bool Force;
        public string? ChangedCategory;
    }
    private sealed class HeldRefresh(Session session, uint pawn, uint weapon, int map,
        string category, gear_slot_t slot, WeaponState state, string original, string model, bool custom)
    {
        public readonly Session Session = session;
        public readonly uint Pawn = pawn, Weapon = weapon;
        public readonly int Map = map;
        public readonly string Category = category, Original = original;
        public readonly gear_slot_t Slot = slot;
        public readonly WeaponState State = state;
        public string Model = model;
        public bool Custom = custom, Retired;
        public uint FallbackActive, Last;
        public uint Replacement;
        public string? BornModel;
        public int Attempts;
    }
    private readonly Dictionary<uint, HeldRefresh> _heldRefreshes = [];
    private int _heldWorkTick = -1, _heldWorkCount;
    private bool TakeHeldWork()
    {
        var tick = Server.TickCount;
        if (tick != _heldWorkTick) { _heldWorkTick = tick; _heldWorkCount = 0; }
        if (_heldWorkCount >= 4) return false;
        _heldWorkCount++;
        return true;
    }
    private static string? SlotCommand(gear_slot_t? slot) => slot switch
    {
        gear_slot_t.GEAR_SLOT_RIFLE => "slot1", gear_slot_t.GEAR_SLOT_PISTOL => "slot2",
        gear_slot_t.GEAR_SLOT_KNIFE => "slot3", _ => null
    };
    private static string? ReadModel(CBaseModelEntity weapon) =>
        weapon.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;

    private void QueueHeldCheck(CCSPlayerController? player, bool force = false, long pickedDefinition = 0, string? changedCategory = null)
    {
        var session = EnsureSession(player);
        if (session == null || player == null || !player.PawnIsAlive) return;
        if (session.HeldRefresh is {} running)
        {
            // Our own grant emits pickup/equip synchronously. Never reissue that grant.
            // A new menu choice while the old item is retiring supersedes its model.
            if (force && changedCategory == running.Category)
            {
                var choice = Selected(session, running.Category);
                running.Custom = !string.IsNullOrEmpty(choice);
                running.Model = running.Custom ? Config.Weapons[running.Category].Skins[choice!].Model : running.Original;
            }
            return;
        }
        var pawn = player.PlayerPawn.Raw;
        if (session.HeldCheck is {} pending && pending.Pawn == pawn && pending.Map == _mapGeneration)
        {
            pending.Force |= force;
            if (changedCategory != null) pending.ChangedCategory = changedCategory;
            return;
        }
        var check = new HeldCheck(pawn, _mapGeneration) { Force = force, ChangedCategory = changedCategory };
        session.HeldCheck = check;
        Server.NextFrame(() =>
        {
            if (!ReferenceEquals(session.HeldCheck, check)) return;
            session.HeldCheck = null;
            if (!ResolveHeldOwner(session, check.Pawn, check.Map, false, out var owner, out var services)) return;
            var active = services.ActiveWeapon.Value;
            if (active is not { IsValid: true } || active.OwnerEntity.Raw != check.Pawn || session.HeldRefresh != null) return;
            var raw = active.EntityHandle.Raw;
            if (!_applied.ContainsKey(raw) && !check.Force && !session.HeldModels.ContainsKey(raw)) return;
            var weapon = active.As<CCSWeaponBase>();
            var slot = weapon.VData?.GearSlot;
            if (SlotCommand(slot) == null) return; // Grenades and C4 keep their existing paths.
            var model = ReadModel(active);
            if (string.IsNullOrWhiteSpace(model)) return;
            if (session.HeldModels.TryGetValue(raw, out var seen) && seen.Pawn == check.Pawn && seen.Model == model) return;
            var definition = active.AttributeManager.Item.ItemDefinitionIndex;
            var category = WeaponNames.Resolve(active.DesignerName, definition, Config.Weapons.Keys);
            if (category == null || check.Force && check.ChangedCategory != category) return;
            if (!_applied.TryGetValue(raw, out var applied) && !session.HeldModels.ContainsKey(raw))
                return; // An unchanged, never-custom stock weapon needs no replacement.
            if (!TakeHeldWork())
            {
                QueueHeldCheck(owner, check.Force, changedCategory: check.ChangedCategory);
                return;
            }
            if (services.MyWeapons.Count(h => h.Value is { IsValid: true } w && w.OwnerEntity.Raw == check.Pawn &&
                w.As<CCSWeaponBase>().VData?.GearSlot == slot) != 1) return;
            try
            {
                var state = new WeaponState(weapon, services); // Validates economy attributes before removing anything.
                var request = new HeldRefresh(session, check.Pawn, raw, check.Map, category, slot!.Value, state,
                    applied?.OriginalModel ?? model, applied?.Model ?? model, applied != null) { Last = services.LastWeapon.Raw };
                session.HeldRefresh = request; _heldRefreshes[raw] = request;
                var items = owner.PlayerPawn.Value?.ItemServices?.As<CCSPlayer_ItemServices>();
                if (items == null) { RemoveHeldRefresh(request); return; }
                items.DropActivePlayerWeapon(active);
                if (services.MyWeapons.Any(h => h.Raw == raw))
                { RemoveHeldRefresh(request); throw new InvalidOperationException("Drop did not detach inventory handle"); }
                request.FallbackActive = services.ActiveWeapon.Raw;
                request.Retired = true;
                if (active.IsValid) active.Remove();
                Server.NextFrame(() => ReplaceHeldWeapon(request));
            }
            catch (Exception ex)
            {
                if (session.HeldRefresh is {} request && !request.Retired) RemoveHeldRefresh(request);
                else if (session.HeldRefresh is {} retired) Server.NextFrame(() => ReplaceHeldWeapon(retired));
                Logger.LogWarning("Held model replacement setup failed ({Type}).", ex.GetType().Name);
                Tell(owner, "Не удалось безопасно обновить оружие. Выбор модели сохранён.");
            }
        });
    }

    private bool ResolveHeldOwner(Session session, uint pawn, int map, bool cleanup,
        out CCSPlayerController player, out CPlayer_WeaponServices services)
    {
        player = null!; services = null!;
        if ((!cleanup && (!_active || _lifecycle?.MapReady != true)) || map != _mapGeneration) return false;
        var current = new CHandle<CCSPlayerController>(session.Controller).Value;
        if (current is not { IsValid: true } || !current.PawnIsAlive || current.PlayerPawn.Raw != pawn ||
            !_sessions.TryGetValue(current.Slot, out var live) || !ReferenceEquals(live, session)) return false;
        var currentPawn = current.PlayerPawn.Value;
        if (currentPawn is not { IsValid: true } || currentPawn.WeaponServices is not {} weaponServices) return false;
        player = current; services = weaponServices;
        return true;
    }

    private void ReplaceHeldWeapon(HeldRefresh request)
    {
        if (!ReferenceEquals(request.Session.HeldRefresh, request)) return;
        if (!ResolveHeldOwner(request.Session, request.Pawn, request.Map, false, out var player, out var services))
        { RemoveHeldRefresh(request); return; }
        if (new CHandle<CBasePlayerWeapon>(request.Weapon).Value is { IsValid: true })
        {
            request.Attempts++;
            RetryHeldReplacement(request); return; // Removal is deferred: wait for the complete serial to disappear.
        }
        if (services.MyWeapons.Any(h => h.Value is { IsValid: true } w && w.OwnerEntity.Raw == request.Pawn && w.As<CCSWeaponBase>().VData?.GearSlot == request.Slot))
        { RemoveHeldRefresh(request); return; } // A later acquisition owns this slot now; do not evict it.
        if (!TakeHeldWork()) { Server.NextFrame(() => ReplaceHeldWeapon(request)); return; }
        request.Attempts++;
        try
        {
            GiveHeldReplacement(request, player, services, false);
            Server.NextFrame(() => FinishHeldReplacement(request));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Held model replacement attempt {Attempt} failed ({Type}).", request.Attempts, ex.GetType().Name);
            RetryHeldReplacement(request);
        }
    }

    private void GiveHeldReplacement(HeldRefresh request, CCSPlayerController player, CPlayer_WeaponServices services, bool restoreOriginal)
    {
        var prior = services.ActiveWeapon.Value;
        var manual = prior is { IsValid: true } && prior.OwnerEntity.Raw == request.Pawn &&
            prior.EntityHandle.Raw != request.FallbackActive && prior.EntityHandle.Raw != request.Weapon;
        var replacement = player.GiveNamedItem<CCSWeaponBase>(request.Category);
        if (replacement is not { IsValid: true } || replacement.OwnerEntity.Raw != request.Pawn)
        {
            if (replacement is { IsValid: true } && !replacement.OwnerEntity.IsValid) replacement.Remove();
            throw new InvalidOperationException("Replacement was not acquired");
        }
        try
        {
            request.State.Restore(replacement, services);
            var raw = replacement.EntityHandle.Raw;
            var model = restoreOriginal ? request.Original : request.Model;
            replacement.SetModel(model);
            request.State.RestoreMesh(replacement);
            request.Replacement = raw; request.BornModel = model;
            if (!restoreOriginal && request.Custom) _applied[raw] = new(request.Original, model);
            RememberHeldModel(request.Session, request.Pawn, replacement, model);
            if (request.Last == request.Weapon) services.LastWeapon.Raw = raw;
            else if (new CHandle<CBasePlayerWeapon>(request.Last).Value is { IsValid: true } last && last.OwnerEntity.Raw == request.Pawn)
                services.LastWeapon.Raw = request.Last;
            // Normal GiveNamedItem performs acquisition. Use client commands only
            // for selection; never synthesize an invalid/retired ActiveWeapon handle.
            var command = manual ? SlotCommand(prior!.As<CCSWeaponBase>().VData?.GearSlot) : SlotCommand(request.Slot);
            if (command != null) player.ExecuteClientCommand(command);
        }
        catch
        {
            player.PlayerPawn.Value?.ItemServices?.As<CCSPlayer_ItemServices>().DropActivePlayerWeapon(replacement);
            replacement.Remove();
            throw;
        }
    }

    private void FinishHeldReplacement(HeldRefresh request)
    {
        if (!ReferenceEquals(request.Session.HeldRefresh, request)) return;
        if (!ResolveHeldOwner(request.Session, request.Pawn, request.Map, false, out var player, out _) ||
            new CHandle<CCSWeaponBase>(request.Replacement).Value is not { IsValid: true } weapon || weapon.OwnerEntity.Raw != request.Pawn)
        { RemoveHeldRefresh(request); return; }
        if (!TakeHeldWork()) { Server.NextFrame(() => FinishHeldReplacement(request)); return; }
        try
        {
            request.State.RestoreAppearance(weapon);
            weapon.SetModel(request.BornModel!);
            request.State.RestoreMesh(weapon);
            RememberHeldModel(request.Session, request.Pawn, weapon, request.BornModel);
        }
        catch (Exception ex) { Logger.LogWarning("Held appearance settlement failed ({Type}).", ex.GetType().Name); }
        finally { RemoveHeldRefresh(request); }
        if (request.Model != request.BornModel)
        {
            weapon.SetModel(request.Model);
            if (request.Custom) _applied[request.Replacement] = new(request.Original, request.Model);
            else _applied.Remove(request.Replacement);
            QueueHeldCheck(player, true, changedCategory: request.Category);
        }
    }

    private void RetryHeldReplacement(HeldRefresh request)
    {
        if (request.Attempts < 8) { Server.NextFrame(() => ReplaceHeldWeapon(request)); return; }
        RemoveHeldRefresh(request);
        Logger.LogError("Held replacement exhausted retries for controller {Controller}, old handle {Weapon}, class {Class}.",
            request.Session.Controller, request.Weapon, request.Category);
    }
    private static void RememberHeldModel(Session session, uint pawn, CBasePlayerWeapon weapon, string? model)
    {
        var raw = weapon.EntityHandle.Raw;
        if (session.HeldModels.Count >= 64 && !session.HeldModels.ContainsKey(raw)) session.HeldModels.Clear();
        session.HeldModels[raw] = (pawn, model, weapon.AttributeManager.Item.ItemDefinitionIndex);
    }
    private void RemoveHeldRefresh(HeldRefresh request)
    {
        if (ReferenceEquals(request.Session.HeldRefresh, request)) request.Session.HeldRefresh = null;
        if (_heldRefreshes.TryGetValue(request.Weapon, out var current) && ReferenceEquals(current, request)) _heldRefreshes.Remove(request.Weapon);
    }
    private void CancelHeldRefresh(Session session, bool restoreSelection, bool clearSeen = true)
    {
        session.HeldCheck = null;
        if (session.HeldRefresh is {} request)
        {
            try
            {
                if (restoreSelection && request.Retired &&
                    new CHandle<CBasePlayerWeapon>(request.Weapon).Value is not { IsValid: true } &&
                    ResolveHeldOwner(session, request.Pawn, request.Map, true, out var player, out var services) &&
                    !services.MyWeapons.Any(h => h.Value is { IsValid: true } w && w.As<CCSWeaponBase>().VData?.GearSlot == request.Slot))
                    GiveHeldReplacement(request, player, services, true);
            }
            catch (Exception ex) { Logger.LogWarning("Held replacement cleanup failed ({Type}).", ex.GetType().Name); }
            RemoveHeldRefresh(request);
        }
        if (clearSeen) session.HeldModels.Clear();
    }
    private void ClearHeldRefreshes(bool restoreSelection)
    {
        foreach (var session in _sessions.Values) CancelHeldRefresh(session, restoreSelection);
        _heldRefreshes.Clear();
    }
}
