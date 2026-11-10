using System.Collections;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;

public sealed class CWAcceptanceProbe : BasePlugin
{
    public override string ModuleName => "Temporary CustomWeapons acceptance inspection";
    public override string ModuleVersion => "1.0.0";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Field(object obj, string name) => obj.GetType().GetField(name, Private)?.GetValue(obj);
    private static object? Property(object? obj, string name) => obj?.GetType().GetProperty(name)?.GetValue(obj);
    private static IPlugin? Plugin()
    {
        var manager = typeof(Application).GetField("_pluginManager", Private)?.GetValue(Application.Instance) as IPluginManager;
        return manager?.GetLoadedPlugins().Select(p => p.Plugin).SingleOrDefault(p => p?.ModuleName == "CustomWeapons");
    }
    private static string? Model(CBaseModelEntity entity) => entity.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
    public override void Load(bool hotReload)
    {
        AddCommand("css_cwqa_state", "Console-only read-only CustomWeapons inspection", (caller, command) =>
        {
            if (caller != null) return;
            try
            {
                var plugin = Plugin() ?? throw new InvalidOperationException("CustomWeapons absent");
                var sessions = Field(plugin, "_sessions") as IDictionary;
                var applied = Field(plugin, "_applied") as IDictionary;
                var precached = (Field(plugin, "_precached") as IEnumerable)?.Cast<string>().ToHashSet() ?? [];
                var weapons = Property(Property(plugin, "Config"), "Weapons") as IDictionary;
                var rows = new List<object>();
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
                {
                    var session = sessions?[player.Slot];
                    var selected = session == null ? null : plugin.GetType().GetMethod("Selected", Private)?.Invoke(plugin, [session, "weapon_ak47"]);
                    var selectionStore = Field(plugin, "_selections");
                    var saved = session == null ? null : selectionStore?.GetType().GetMethod("Get")?.Invoke(selectionStore, [Property(session, "SteamId"), "weapon_ak47"]);
                    var qaSkin = (Property(weapons?["weapon_ak47"], "Skins") as IDictionary)?["cwqa_ak47"];
                    var qaModel = Property(qaSkin, "Model") as string;
                    var inventory = new List<object>();
                    var pawn = player.PlayerPawn.Value;
                    if (pawn is { IsValid: true } && pawn.WeaponServices?.MyWeapons is {} handles)
                        foreach (var handle in handles)
                            if (handle.Value is { IsValid: true } weapon)
                                inventory.Add(new { raw = weapon.EntityHandle.Raw, name = weapon.DesignerName,
                                    definition = weapon.AttributeManager.Item.ItemDefinitionIndex,
                                    model = Model(weapon), applied = Property(applied?[weapon.EntityHandle.Raw], "Model") });
                    rows.Add(new { slot = player.Slot, alive = player.PawnIsAlive, selected, saved,
                        qa_model = qaModel, qa_precached = qaModel != null && precached.Contains(qaModel),
                        database_ready = Property(Property(session, "Access"), "Ready"), inventory });
                }
                var ground = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_ak47")
                    .Where(w => w.IsValid).Take(12).Select(w => new { raw = w.EntityHandle.Raw, model = Model(w),
                        applied = Property(applied?[w.EntityHandle.Raw], "Model") }).ToArray();
                var globalSkin = (Property(weapons?["weapon_ak47"], "Skins") as IDictionary)?["cwqa_ak47"];
                var globalModel = Property(globalSkin, "Model") as string;
                command.ReplyToCommand(JsonSerializer.Serialize(new { version = plugin.ModuleVersion,
                    map_ready = Property(Field(plugin, "_lifecycle"), "MapReady"), precached = precached.Count,
                    qa_model = globalModel, qa_precached = globalModel != null && precached.Contains(globalModel),
                    players = rows, ak_entities = ground }));
            }
            catch (Exception ex) { command.ReplyToCommand("Inspection failed: " + ex.GetType().Name); }
        });
        AddCommand("css_cwqa_give_ak", "Console-only AK grant to the single test client", (caller, command) =>
        {
            if (caller != null) return;
            var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToArray();
            if (players.Length != 1 || !players[0].PawnIsAlive) { command.ReplyToCommand("Rejected: require one alive human"); return; }
            players[0].GiveNamedItem("weapon_ak47");
            command.ReplyToCommand("AK grant invoked for single test client");
        });
    }
}
