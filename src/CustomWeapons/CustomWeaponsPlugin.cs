using System.Diagnostics;
using System.Globalization;
using System.Net;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CustomWeapons.Core;
using Microsoft.Extensions.Logging;

namespace CustomWeapons;

[MinimumApiVersion(375)]
public sealed class CustomWeaponsPlugin : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "CustomWeapons";
    public override string ModuleVersion => "1.0.1";
    public override string ModuleAuthor => "koiie111";
    public override string ModuleDescription => "Custom weapon models with MySQL access and local selections";
    public PluginConfig Config { get; set; } = new();

    private sealed class Session(string steamId, uint controller)
    {
        public string SteamId { get; } = steamId;
        public uint Controller { get; } = controller;
        public AccessState Access { get; } = new();
        public double NextChange { get; set; }
    }
    private sealed record Applied(string OriginalModel, string Model);
    private readonly Dictionary<int, Session> _sessions = new();
    private readonly Dictionary<uint, Applied> _applied = new();
    private readonly Dictionary<uint, IMenuInstance> _menus = new();
    private readonly HashSet<string> _precached = new(StringComparer.Ordinal);
    private readonly HashSet<string> _modelWarnings = new(StringComparer.Ordinal);
    private readonly GrenadeTracker _grenades = new();
    private readonly CancellationTokenSource _stop = new();
    private SelectionStore _selections = null!;
    private IAccessRepository _repository = null!;
    private DatabaseSnapshot? _catalog;
    private volatile bool _active;
    private bool _refreshRunning;
    private bool _refreshAgain;
    private int _mapGeneration;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public void OnConfigParsed(PluginConfig config)
    {
        if (!double.IsFinite(config.CooldownSeconds) || config.CooldownSeconds < 0 ||
            !float.IsFinite(config.RefreshIntervalSeconds) || config.RefreshIntervalSeconds < 5)
            throw new ArgumentException("CooldownSeconds must be >= 0; RefreshIntervalSeconds must be >= 5");
        ArgumentNullException.ThrowIfNull(config.Database);
        ArgumentNullException.ThrowIfNull(config.Weapons);
        SkinRules.ValidateWeapons(config.Weapons);
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        _repository = new MySqlRepository(Config.Database);
        _selections = new SelectionStore(Path.Combine(ModuleDirectory, "data", "selections.json"), Config.SaveSelections,
            ex => Logger.LogError("Selection storage error ({Type}). Check data directory permissions and backups.", ex.GetType().Name));
        _active = true;
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _precached.Clear();
            foreach (var model in Config.Weapons.Values.SelectMany(w => w.Skins.Values).Select(s => s.Model).Distinct())
            {
                manifest.AddResource(model);
                _precached.Add(model);
            }
        });
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _mapGeneration++;
            _applied.Clear();
            _precached.Clear();
            _modelWarnings.Clear();
            _grenades.Clear();
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            if (_sessions.Remove(slot, out var session))
            {
                _selections.Disconnect(session.SteamId);
                _menus.Remove(session.Controller);
            }
        });
        RegisterListener<Listeners.OnClientAuthorized>((slot, _) => Server.NextWorldUpdate(() =>
        {
            if (_active) { EnsureSession(Utilities.GetPlayerFromSlot(slot)); RequestRefresh(); }
        }));
        RegisterEventHandler<EventPlayerConnectFull>((ev, _) =>
        {
            EnsureSession(ev.Userid); RequestRefresh(); return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerSpawn>((ev, _) => { Schedule(ev.Userid); return HookResult.Continue; });
        RegisterEventHandler<EventItemPickup>((ev, _) => { Schedule(ev.Userid); return HookResult.Continue; });
        RegisterListener<Listeners.OnEntityDeleted>(entity => _applied.Remove(entity.EntityHandle.Raw));
        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        RegisterListener<Listeners.OnTick>(ObserveHeldGrenades);
        AddCommand("css_cw", "Открыть меню CustomWeapons", (player, _) => OpenWeapons(player));
        AddCommand("css_customweapons", "Открыть меню CustomWeapons", (player, _) => OpenWeapons(player));
        AddTimer(Config.RefreshIntervalSeconds, RequestRefresh, TimerFlags.REPEAT);
        // Covers weapons granted by other plugins and joins while an earlier DB read was pending.
        AddTimer(0.5f, () =>
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var existed = _sessions.ContainsKey(player.Slot);
                if (EnsureSession(player) is not null)
                {
                    ApplyInventory(player);
                    if (!existed) RequestRefresh();
                }
            }
            _grenades.Prune(Now);
        }, TimerFlags.REPEAT);
        foreach (var player in Utilities.GetPlayers()) EnsureSession(player);
        RequestRefresh();
        if (hotReload) Logger.LogWarning("CustomWeapons loaded mid-map: change map to precache custom models before applying skins.");
    }

    private Session? EnsureSession(CCSPlayerController? player)
    {
        if (player is not { IsValid: true, IsBot: false, IsHLTV: false } || player.SteamID == 0) return null;
        var steamId = player.SteamID.ToString(CultureInfo.InvariantCulture);
        if (_sessions.TryGetValue(player.Slot, out var existing) && existing.SteamId == steamId &&
            existing.Controller == player.EntityHandle.Raw) return existing;
        if (existing != null)
        {
            _selections.Disconnect(existing.SteamId);
            _menus.Remove(existing.Controller);
        }
        var session = new Session(steamId, player.EntityHandle.Raw);
        _sessions[player.Slot] = session;
        return session;
    }

    private void RequestRefresh()
    {
        if (!_active) return;
        if (_refreshRunning) { _refreshAgain = true; return; }
        _refreshRunning = true;
        var sessions = _sessions.ToArray();
        var ids = sessions.Select(x => x.Value.SteamId).Distinct().ToArray();
        _ = Task.Run(async () =>
        {
            DatabaseSnapshot? result = null;
            Exception? failure = null;
            try { result = await _repository.ReadAsync(ids, _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception ex) { failure = ex; }
            if (!_active) return;
            Server.NextWorldUpdate(() =>
            {
                if (!_active) return;
                _refreshRunning = false;
                if (result != null)
                {
                    _catalog = result;
                    foreach (var id in result.Conflicts) Logger.LogWarning("Conflicting cw_skins rows for {Skin}; disabled.", id);
                }
                else Logger.LogWarning("MySQL refresh failed ({Type}); new private applications denied. Retrying in {Seconds}s.",
                    failure?.GetType().Name, Config.RefreshIntervalSeconds);
                foreach (var (slot, session) in sessions)
                {
                    if (!_sessions.TryGetValue(slot, out var current) || !ReferenceEquals(current, session)) continue;
                    if (result != null) session.Access.Succeed(result); else session.Access.Fail();
                    var player = new CHandle<CCSPlayerController>(session.Controller).Value;
                    if (player is { IsValid: true }) ApplyInventory(player);
                }
                if (_refreshAgain) { _refreshAgain = false; RequestRefresh(); }
            });
        });
    }

    private bool Allowed(Session session, string id, SkinDefinition skin) =>
        SkinRules.CanApply(skin, id, session.SteamId, Config.ServerId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            session.Access.Ready, session.Access.Snapshot ?? _catalog);

    private void OpenWeapons(CCSPlayerController? player)
    {
        var session = EnsureSession(player);
        if (session == null || player == null) return;
        if (!session.Access.Ready) RequestRefresh();
        var menu = new CenterHtmlMenu("CustomWeapons — оружие", this);
        foreach (var (weapon, definition) in Config.Weapons)
            menu.AddMenuOption(Html(string.IsNullOrWhiteSpace(definition.Name) ? weapon : definition.Name),
                (p, _) => OpenSkins(p, weapon));
        ShowMenu(player, menu);
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);
    private void ShowMenu(CCSPlayerController player, CenterHtmlMenu menu)
    {
        // Callbacks navigate to new menus themselves; do not let the old instance reset them.
        menu.PostSelectAction = PostSelectAction.Nothing;
        menu.Open(player);
        if (MenuManager.GetActiveMenu(player) is { } instance) _menus[player.EntityHandle.Raw] = instance;
    }
    private void OpenSkins(CCSPlayerController player, string weapon)
    {
        var session = EnsureSession(player);
        if (session == null || !Config.Weapons.TryGetValue(weapon, out var definition)) return;
        var menu = new CenterHtmlMenu(Html(definition.Name), this);
        var selected = _selections.Get(session.SteamId, weapon);
        menu.AddMenuOption(string.IsNullOrEmpty(selected) ? "✓ Стандартная модель" : "Стандартная модель",
            (p, _) => Select(p, weapon, null));
        foreach (var (id, skin) in definition.Skins)
        {
            if (_catalog?.Catalog.TryGetValue(id, out var row) == true && !row.Active) continue;
            var allowed = Allowed(session, id, skin);
            if (!allowed && skin.Hide) continue;
            var label = Html(SkinRules.DisplayName(skin, id, _catalog));
            if (selected == id) label = "✓ " + label;
            if (!allowed) label += " — нет доступа";
            else if (!_precached.Contains(skin.Model)) label += " — после смены карты";
            menu.AddMenuOption(label, (p, _) => Select(p, weapon, id), !allowed || !_precached.Contains(skin.Model));
        }
        menu.AddMenuOption("← Назад", (p, _) => OpenWeapons(p));
        ShowMenu(player, menu);
    }

    private void Select(CCSPlayerController player, string weapon, string? id)
    {
        var session = EnsureSession(player);
        if (session == null || !Config.Weapons.TryGetValue(weapon, out var definition)) return;
        if (session.NextChange > Now)
        {
            Tell(player, $"Подождите {Math.Ceiling(session.NextChange - Now)} сек."); return;
        }
        if (id != null && (!definition.Skins.TryGetValue(id, out var skin) || !Allowed(session, id, skin) || !_precached.Contains(skin.Model)))
        {
            Tell(player, "Скин недоступен или ещё не загружен в ресурсы карты."); return;
        }
        _selections.Set(session.SteamId, weapon, id);
        session.NextChange = Now + Config.CooldownSeconds;
        ApplyInventory(player, weapon);
        Tell(player, "Выбор сохранён. Он применяется к вашему оружию этого типа.");
        OpenSkins(player, weapon);
    }

    private static void Tell(CCSPlayerController player, string text) => player.PrintToChat($"[CustomWeapons] {text}");
    private void Schedule(CCSPlayerController? player)
    {
        var session = EnsureSession(player);
        if (session == null) return;
        var generation = _mapGeneration;
        Server.NextFrame(() =>
        {
            if (!_active || generation != _mapGeneration) return;
            var current = new CHandle<CCSPlayerController>(session.Controller).Value;
            if (current is { IsValid: true } && _sessions.TryGetValue(current.Slot, out var live) && ReferenceEquals(live, session))
                ApplyInventory(current);
        });
    }

    private void ApplyInventory(CCSPlayerController player, string? forceWeapon = null)
    {
        var session = EnsureSession(player);
        var pawn = player.PlayerPawn.Value;
        if (session == null || pawn is not { IsValid: true } || !player.PawnIsAlive) return;
        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons == null) return;
        foreach (var handle in weapons)
        {
            var weapon = handle.Value;
            if (weapon is not { IsValid: true }) continue;
            var category = WeaponNames.Resolve(weapon.DesignerName, weapon.AttributeManager.Item.ItemDefinitionIndex, Config.Weapons.Keys);
            if (category == null || forceWeapon != null && category != forceWeapon) continue;
            var raw = weapon.EntityHandle.Raw;
            if (forceWeapon == null && _applied.ContainsKey(raw)) continue;
            var selected = _selections.Get(session.SteamId, category);
            if (string.IsNullOrEmpty(selected))
            {
                if (forceWeapon != null) Revert(raw);
                continue;
            }
            if (Config.Weapons[category].Skins.TryGetValue(selected, out var skin) && Allowed(session, selected, skin))
                ApplyModel(weapon, skin.Model);
        }
    }

    private void ApplyModel(CBaseModelEntity entity, string model)
    {
        if (!_precached.Contains(model) || !entity.IsValid) return;
        var raw = entity.EntityHandle.Raw;
        if (_applied.TryGetValue(raw, out var old) && old.Model == model) return;
        var original = old?.OriginalModel ?? entity.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
        if (string.IsNullOrWhiteSpace(original))
        {
            if (_modelWarnings.Add(model)) Logger.LogWarning("Cannot capture original model for {Model}; application skipped.", model);
            return;
        }
        entity.SetModel(model);
        _applied[raw] = new(original, model);
    }

    private void Revert(uint raw)
    {
        if (!_applied.TryGetValue(raw, out var applied)) return;
        var entity = new CHandle<CBaseModelEntity>(raw).Value;
        if (entity is { IsValid: true }) entity.SetModel(applied.OriginalModel);
        _applied.Remove(raw);
    }

    private void ObserveHeldGrenades()
    {
        if (!_active) return;
        foreach (var player in Utilities.GetPlayers())
        {
            var pawn = player.PlayerPawn.Value;
            var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
            if (pawn is not { IsValid: true } || weapon is not { IsValid: true }) continue;
            var name = WeaponNames.Normalize(weapon.DesignerName, weapon.AttributeManager.Item.ItemDefinitionIndex);
            if (!WeaponNames.Projectiles.Values.Any(candidates => candidates.Contains(name))) continue;
            _grenades.Observe(pawn.EntityHandle.Raw, name,
                _applied.TryGetValue(weapon.EntityHandle.Raw, out var applied) ? applied.Model : null, Now);
        }
    }

    private void OnEntitySpawned(CEntityInstance entity)
    {
        if (!entity.IsValid || !WeaponNames.Projectiles.TryGetValue(entity.DesignerName, out var candidates)) return;
        var raw = entity.EntityHandle.Raw;
        var generation = _mapGeneration;
        // Capture before the throwing weapon is removed; resolve owner/projectile after spawn completes.
        ObserveHeldGrenades();
        Server.NextFrame(() =>
        {
            if (!_active || generation != _mapGeneration) return;
            var projectile = new CHandle<CBaseCSGrenadeProjectile>(raw).Value;
            if (projectile is not { IsValid: true }) return;
            var thrower = projectile.Thrower.IsValid ? projectile.Thrower.Raw : projectile.OriginalThrower.Raw;
            var model = _grenades.Find(thrower, candidates, Now);
            if (model != null) ApplyModel(projectile, model);
        });
    }

    public override void Unload(bool hotReload)
    {
        _active = false;
        _stop.Cancel();
        foreach (var raw in _applied.Keys.ToArray())
        {
            try { Revert(raw); }
            catch (Exception ex) { Logger.LogWarning("Failed to restore entity {Handle}: {Type}", raw, ex.GetType().Name); }
        }
        foreach (var player in Utilities.GetPlayers())
            if (_menus.TryGetValue(player.EntityHandle.Raw, out var menu) && ReferenceEquals(MenuManager.GetActiveMenu(player), menu))
                MenuManager.CloseActiveMenu(player);
        _menus.Clear();
        _selections.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _sessions.Clear();
        _grenades.Clear();
    }
}
