using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CustomWeapons;
using CustomWeapons.Core;

internal static partial class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly DatabaseSnapshot Empty = new([], []);
    private static object? Get(object obj, string name) => obj.GetType().GetField(name, Private)!.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    private static async Task Until(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Server.DrainWorld();
            if (timeout.Elapsed.TotalSeconds > 5) throw new TimeoutException("Background read did not settle");
            await Task.Delay(1);
        }
    }
    private sealed class Repository : IAccessRepository
    {
        public Task<DatabaseSnapshot> ReadAsync(IReadOnlyCollection<string> ids, CancellationToken ct) => Task.FromResult(Empty);
    }
    private sealed class DelayedRepository : IAccessRepository
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource<DatabaseSnapshot>> Reads = new();
        public Task<DatabaseSnapshot> ReadAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        {
            var result = new TaskCompletionSource<DatabaseSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            Reads.Enqueue(result);
            return result.Task.WaitAsync(ct);
        }
    }
    private static async Task<CustomWeaponsPlugin> Start(bool grenades = true, bool website = false)
    {
        Entities.All.Clear(); Entities.Next = 100; Utilities.Players.Clear();
        Server.DrainWorld(); Server.DrainFrames();
        Server.AdvanceTicks(16);
        var plugin = new CustomWeaponsPlugin();
        var weapons = new Dictionary<string, WeaponDefinition>
        {
            ["weapon_ak47"] = new() { Skins = new() { ["gun"] = new() { Private = false, Model = "models/gun.vmdl" } } }
        };
        if (grenades)
            foreach (var name in WeaponNames.Projectiles.Values.SelectMany(x => x).Distinct())
                weapons[name] = new() { Skins = new() { [name] = new() { Private = false, Model = $"models/{name}.vmdl" } } };
        var config = new PluginConfig { SaveSelections = false, Weapons = weapons };
#if WEBSITE_SELECTIONS
        config.WebsiteSelectionsEnabled = website;
#endif
        plugin.OnConfigParsed(config);
        plugin.Load(false);
        Set(plugin, "_repository", new Repository());
        ((Listeners.OnServerPrecacheResources)plugin.ListenersByType[typeof(Listeners.OnServerPrecacheResources)])(new());
        ((Listeners.OnMapStart)plugin.ListenersByType[typeof(Listeners.OnMapStart)])("fixture");
        await Until(() => !(bool)Get(plugin, "_refreshRunning")!);
        return plugin;
    }
    private static CCSPlayerController Player(int slot)
    {
        var pawn = new Pawn();
        var player = new CCSPlayerController { Slot = slot, SteamID = 76561198000000000UL + (uint)slot, PlayerPawn = new(pawn.EntityHandle.Raw) };
        Utilities.Players.Add(player);
        return player;
    }
    private static Weapon Give(CCSPlayerController player, string name)
    {
        var weapon = new Weapon { DesignerName = name, OwnerEntity = new(player.PlayerPawn.Raw) };
        var services = player.PlayerPawn.Value!.WeaponServices!;
        services.MyWeapons.Add(new(weapon.EntityHandle.Raw));
        services.ActiveWeapon = new(weapon.EntityHandle.Raw);
        return weapon;
    }
    private static Action Safety(CustomWeaponsPlugin plugin) => plugin.Timers.Single(t => t.Interval == 0.5f).Callback;
    private static void Tick(CustomWeaponsPlugin plugin)
    {
        if (plugin.ListenersByType.TryGetValue(typeof(Listeners.OnTick), out var callback)) ((Listeners.OnTick)callback)();
    }
    private static void MapEnd(CustomWeaponsPlugin plugin) => ((Listeners.OnMapEnd)plugin.ListenersByType[typeof(Listeners.OnMapEnd)])();
    private static void Disconnect(CustomWeaponsPlugin plugin, int slot) => ((Listeners.OnClientDisconnect)plugin.ListenersByType[typeof(Listeners.OnClientDisconnect)])(slot);

    private static async Task Tests()
    {
        foreach (var enabled in new[] { false, true })
        {
            var plugin = await Start(enabled);
            Check(plugin.ListenersByType.ContainsKey(typeof(Listeners.OnTick)) == enabled, "Tick feature gate");
            Check(plugin.ListenersByType.ContainsKey(typeof(Listeners.OnEntitySpawned)) == enabled, "Spawn feature gate");
#if HELD_REFRESH
            Check(plugin.Events.ContainsKey(typeof(EventItemEquip)), "Model refresh needs equip events in both configurations");
#else
            Check(plugin.Events.ContainsKey(typeof(EventItemEquip)) == enabled, "Historical equip feature gate");
#endif
            Check(plugin.ListenersByType.ContainsKey(typeof(Listeners.OnEntityDeleted)), "Deletion cleanup stays enabled");
            var count = plugin.ListenersByType.Count;
            MapEnd(plugin);
            ((Listeners.OnMapStart)plugin.ListenersByType[typeof(Listeners.OnMapStart)])("next");
            Check(count == plugin.ListenersByType.Count && plugin.Timers.Count == 2, "No duplicate hooks or timers after map");
            await Until(() => !(bool)Get(plugin, "_refreshRunning")!);
            plugin.Unload(false);
        }

        foreach (var (projectileName, aliases) in WeaponNames.Projectiles)
        foreach (var alias in aliases)
        {
            var plugin = await Start();
            var player = Player(1);
            var weapon = Give(player, alias);
            // Other-plugin grant has no pickup/equip event: safety reconciliation discovers it.
            Safety(plugin)();
            var holders = (HashSet<int>)Get(plugin, "_grenadeHolders")!;
            Check(holders.SetEquals([1]), "Safety discovers holder " + alias);
            Check(weapon.Model == $"models/{alias}.vmdl", "Model application " + alias);
            Utilities.PlayerScans = 0;
            Tick(plugin);
            Check(Utilities.PlayerScans == 0, "Tick must use holder sessions");
            var projectile = new CBaseCSGrenadeProjectile { DesignerName = projectileName, Thrower = new(player.PlayerPawn.Raw) };
            ((Listeners.OnEntitySpawned)plugin.ListenersByType[typeof(Listeners.OnEntitySpawned)])(projectile);
            // Switch/delete before NextFrame; projectile must use pre-removal sample.
            player.PlayerPawn.Value!.WeaponServices!.MyWeapons.Clear();
            player.PlayerPawn.Value.WeaponServices.ActiveWeapon = new(0);
            weapon.IsValid = false;
            Server.DrainFrames();
            Check(projectile.Model == $"models/{alias}.vmdl", "Pre-removal capture " + alias);
            Safety(plugin)();
            Check(holders.Count == 0, "Drop removes holder");
            plugin.Unload(false);
        }

        {
            var plugin = await Start();
            var first = Player(1); var second = Player(2);
            var grenade = Give(first, "weapon_flashbang");
            Safety(plugin)();
            ((HashSet<string>)Get(plugin, "_precached")!).Add("models/transferred.vmdl");
            Call(plugin, "ApplyModel", grenade, "models/transferred.vmdl");
            // Transfer actual instance, including model, then throw with OriginalThrower fallback.
            first.PlayerPawn.Value!.WeaponServices!.MyWeapons.Clear();
            first.PlayerPawn.Value.WeaponServices.ActiveWeapon = new(0);
            second.PlayerPawn.Value!.WeaponServices!.MyWeapons.Add(new(grenade.EntityHandle.Raw));
            second.PlayerPawn.Value.WeaponServices.ActiveWeapon = new(grenade.EntityHandle.Raw);
            plugin.Fire(new EventItemPickup { Userid = second }); Server.DrainFrames(); Tick(plugin);
            var projectile = new CBaseCSGrenadeProjectile { DesignerName = "flashbang_projectile", OriginalThrower = new(second.PlayerPawn.Raw) };
            ((Listeners.OnEntitySpawned)plugin.ListenersByType[typeof(Listeners.OnEntitySpawned)])(projectile);
            Server.DrainFrames();
            Check(projectile.Model == grenade.Model, "Transferred instance model uses actual thrower");
            // A standard second flash clears the preceding custom sample.
            var plain = Give(second, "weapon_flashbang"); Tick(plugin);
            var next = new CBaseCSGrenadeProjectile { DesignerName = "flashbang_projectile", Thrower = new(second.PlayerPawn.Raw) };
            ((Listeners.OnEntitySpawned)plugin.ListenersByType[typeof(Listeners.OnEntitySpawned)])(next);
            Server.DrainFrames();
            Check(next.Writes == 0 && plain.Writes == 0, "Plain second flash cannot inherit custom sample");
            // Scheduled inventory callback cannot apply to replacement session in the same slot.
            plugin.Fire(new EventPlayerSpawn { Userid = second });
            Disconnect(plugin, 2); Utilities.Players.Remove(second);
            var replacement = Player(2); var stock = Give(replacement, "weapon_ak47");
            Server.DrainFrames(); Check(stock.Writes == 0, "Slot reuse rejects scheduled callback");
            Safety(plugin)(); Check(stock.Writes == 1, "Safety applies new slot session");
            Weapon.DefinitionReads = 0;
            Call(plugin, "ApplyInventory", replacement, null);
            Check(Weapon.DefinitionReads == 0, "Applied handles skip repeated definition/normalization");
            // Count work for an unpainted weapon: a skipped ApplyInventory cannot hide
            // behind its applied-model fast path. Forced sweeps and inventory changes
            // must still retry applications, including ones awaiting an earlier DB read.
            plugin.Config.Weapons["weapon_ak47"].Skins["gun"].Private = true;
            var third = Player(3); Give(third, "weapon_ak47"); Safety(plugin)();
            Weapon.DefinitionReads = 0; Safety(plugin)();
            Check(Weapon.DefinitionReads == 0, "Unchanged inventory skips application");
            Give(third, "weapon_ak47"); Safety(plugin)();
            Check(Weapon.DefinitionReads > 0, "Changed inventory retries application");
            var allSessions = (IDictionary)Get(plugin, "_sessions")!;
            allSessions[3]!.GetType().GetProperty("NextSweep")!.SetValue(allSessions[3], 0d);
            Weapon.DefinitionReads = 0; Safety(plugin)();
            Check(Weapon.DefinitionReads > 0, "Safety deadline retries unchanged inventory");
            replacement.PawnIsAlive = false; Safety(plugin)();
            Check(!((HashSet<int>)Get(plugin, "_grenadeHolders")!).Contains(2), "Dead holder removed");
            // Pending projectile callback is map-scoped.
            ((Listeners.OnEntitySpawned)plugin.ListenersByType[typeof(Listeners.OnEntitySpawned)])(projectile);
            var writes = projectile.Writes; MapEnd(plugin); Server.DrainFrames();
            Check(projectile.Writes == writes, "Map invalidates projectile callback");
            plugin.Unload(false);
        }

        await MapRefreshTest();
#if HELD_REFRESH
        await HeldRefreshTests();
#endif
#if WEBSITE_SELECTIONS
        await WebsiteTests();
#endif
        Console.WriteLine($"{_checks} runtime checks passed");
    }

    private static async Task MapRefreshTest()
    {
        {
            var plugin = await Start();
            var player = Player(1); Give(player, "weapon_flashbang"); Safety(plugin)();
            var delayed = new DelayedRepository(); Set(plugin, "_repository", delayed);
            Call(plugin, "RequestRefresh");
            await Until(() => delayed.Reads.Count == 1);
            delayed.Reads.TryDequeue(out var oldRead);
            MapEnd(plugin);
            ((Listeners.OnMapStart)plugin.ListenersByType[typeof(Listeners.OnMapStart)])("new");
            var stale = new DatabaseSnapshot([new("stale", "old map", 1)], []);
            oldRead!.SetResult(stale);
            await Until(() => delayed.Reads.Count == 1);
            Check(!ReferenceEquals(Get(plugin, "_catalog"), stale), "Old-map read cannot publish catalog");
            delayed.Reads.TryDequeue(out var freshRead);
            freshRead!.SetResult(Empty);
            await Until(() => !(bool)Get(plugin, "_refreshRunning")!);
            Check(ReferenceEquals(Get(plugin, "_catalog"), Empty), "Fresh map retries read");
            // A failed current read still denies new private applications.
            Call(plugin, "RequestRefresh"); await Until(() => delayed.Reads.Count == 1);
            delayed.Reads.TryDequeue(out var failure); failure!.SetException(new IOException("fixture outage"));
            await Until(() => !(bool)Get(plugin, "_refreshRunning")!);
            var sessions = (IDictionary)Get(plugin, "_sessions")!;
            var access = (AccessState)sessions[1]!.GetType().GetProperty("Access")!.GetValue(sessions[1])!;
            Check(!access.Ready, "DB outage invalidates access readiness");
            plugin.Fire(new EventPlayerSpawn { Userid = player }); plugin.Unload(false); Server.DrainFrames();
            Check(((HashSet<int>)Get(plugin, "_grenadeHolders")!).Count == 0, "Unload clears holders and deferred callback");
        }
    }

    private static async Task Benchmark(string output)
    {
        var rows = new List<object>();
        foreach (var active in new[] { 0, 1, 8, 32 })
        {
            var plugin = await Start();
            for (var i = 0; i < 32; i++)
            {
                var player = Player(i);
                Give(player, i < active ? "weapon_flashbang" : "weapon_ak47");
            }
            Safety(plugin)();
            var tick = (Listeners.OnTick)plugin.ListenersByType[typeof(Listeners.OnTick)];
            var safety = Safety(plugin);
            foreach (var (name, action, iterations) in new (string, Action, int)[] { ("tick", () => tick(), 10000), ("safety", safety, 10000) })
            {
                for (var i = 0; i < 2000; i++) action();
                for (var window = 0; window < 3; window++)
                {
                    Utilities.PlayerScans = 0; Weapon.DefinitionReads = 0;
                    var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                    for (var i = 0; i < iterations; i++) action();
                    var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                    bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    var weapons = Entities.All.Values.OfType<Weapon>().ToArray();
                    Check(weapons.Length == 32 && weapons.All(w => w.Writes == 1 && w.Model != "models/stock.vmdl"), "Benchmark preserves model application");
                    rows.Add(new { active, name, window, iterations, ns_per_call = elapsed / iterations,
                        bytes_per_call = bytes / (double)iterations, player_scans = Utilities.PlayerScans,
                        definition_reads = Weapon.DefinitionReads, model_writes = weapons.Sum(w => w.Writes) });
                }
            }
            plugin.Unload(false);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { synthetic = true, runtime = Environment.Version.ToString(), rows }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
    private static async Task Main(string[] args)
    {
        if (args is ["map-only"]) await MapRefreshTest();
#if WEBSITE_SELECTIONS
        else if (args is ["review-repaint"]) await WebsiteRepaintDiagnostic();
#endif
        else if (args is ["bench", var output]) await Benchmark(output);
        else await Tests();
    }
}
