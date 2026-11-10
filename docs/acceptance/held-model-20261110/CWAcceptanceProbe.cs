using System.Collections;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

public sealed class CWAcceptanceProbe : BasePlugin
{
    public override string ModuleName => "Temporary CustomWeapons acceptance inspection";
    public override string ModuleVersion => "1.6.0";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Field(object obj, string name) => obj.GetType().GetField(name, Private)?.GetValue(obj);
    private static object? Property(object? obj, string name) => obj?.GetType().GetProperty(name)?.GetValue(obj);
    private static IPlugin? Plugin()
    {
        var manager = typeof(Application).GetField("_pluginManager", Private)?.GetValue(Application.Instance) as IPluginManager;
        return manager?.GetLoadedPlugins().Select(p => p.Plugin).SingleOrDefault(p => p?.ModuleName == "CustomWeapons");
    }
    private static string? Model(CBaseModelEntity entity) => entity.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
    private static IEnumerable<CEconItemAttribute> ReadAttributes(CAttributeList list)
    {
        var count=list.Attributes.Count;if(count==0)yield break;
        if(count>256)throw new InvalidOperationException("Attribute bound");
        var size=CounterStrikeSharp.API.Modules.Memory.Schema.GetClassSize("CEconItemAttribute");
        if(size<=0||size>4096)throw new InvalidOperationException("Attribute schema size");
        var buffer=NativeAPI.GetNetworkVectorElementAt(list.Attributes.Handle,0);
        for(var n=0;n<count;n++)yield return new CEconItemAttribute(buffer+checked(size*n));
    }
    private static object[] Attributes(CAttributeList list) => ReadAttributes(list).Select(a => (object)new { index = a.AttributeDefinitionIndex, bits = BitConverter.SingleToInt32Bits(a.Value), initial_bits = BitConverter.SingleToInt32Bits(a.InitialValue), currency = a.RefundableCurrency, bonus = a.SetBonus }).ToArray();
    public override void Load(bool hotReload)
    {
        AddCommand("css_cwqa_native_cleanup", "Console-only cleanup of native fixture items", (caller,command)=>
        {
            if(caller!=null || Utilities.GetPlayers().Any(p=>p.IsValid&&!p.IsBot&&!p.IsHLTV))return;
            var count=0;
            foreach(var w in Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_ak47").Where(w=>w.IsValid && w.AttributeManager.Item.CustomName=="CW native preserve").ToArray())
            { if(w.OwnerEntity.IsValid)continue;w.Remove();count++; }
            command.ReplyToCommand("Cleaned "+count+" unowned fixture items");
        });
        AddCommand("css_cwqa_native", "Console-only bounded native replacement fixture, no human allowed", (caller, command) =>
        {
            if(caller != null) return;
            var players=Utilities.GetPlayers().Where(p=>p.IsValid && !p.IsHLTV).ToArray();
            if(players.Any(p=>!p.IsBot) || players.Length!=1 || !players[0].PawnIsAlive) { command.ReplyToCommand("Rejected: require one alive bot and no humans"); return; }
            var bot=players[0];var pawn=bot.PlayerPawn.Value!;var pawnRaw=pawn.EntityHandle.Raw;
            var type=Plugin()!.GetType().Assembly.GetType("CustomWeapons.WeaponState")!;
            var writer=new MemoryFunctionVoid<nint,string,float>(GameData.GetSignature("CW_SetOrAddAttributeValueByName"));
            var logs=new List<object>();var remaining=25;
            // A GiveNamedItem result can be a ground weapon if a rifle slot is
            // already occupied. Empty the controlled bot's inventory first.
            foreach(var handle in pawn.WeaponServices!.MyWeapons.ToArray())
                if(handle.Value is {IsValid:true} held && held.OwnerEntity.Raw==pawnRaw && held.As<CCSWeaponBase>().VData?.GearSlot==gear_slot_t.GEAR_SLOT_RIFLE)
                {
                    pawn.ItemServices!.As<CCSPlayer_ItemServices>().DropActivePlayerWeapon(held);
                    if(pawn.WeaponServices.MyWeapons.Any(h=>h.Raw==held.EntityHandle.Raw)) {command.ReplyToCommand("Rejected: fixture rifle was not detached");return;}
                    held.Remove();
                }
            var weapon=bot.GiveNamedItem<CCSWeaponBase>("weapon_ak47");
            if(weapon is not {IsValid:true} || weapon.OwnerEntity.Raw!=pawnRaw || !pawn.WeaponServices.MyWeapons.Any(h=>h.Raw==weapon.EntityHandle.Raw))
            { if(weapon is {IsValid:true} && !weapon.OwnerEntity.IsValid)weapon.Remove();command.ReplyToCommand("Rejected: initial AK was not acquired");return; }
            weapon.Clip1=17;weapon.ReserveAmmo[0]=29;weapon.FallbackPaintKit=302;weapon.FallbackSeed=571;weapon.FallbackWear=.16f;
            weapon.AttributeManager.Item.CustomName="CW native preserve";
            writer.Invoke(weapon.AttributeManager.Item.AttributeList.Handle,"set item texture prefab",302);
            writer.Invoke(weapon.AttributeManager.Item.AttributeList.Handle,"set item texture seed",571);
            writer.Invoke(weapon.AttributeManager.Item.AttributeList.Handle,"kill eater",BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234)));
            writer.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,"set item texture wear",.16f);
            writer.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,"set item texture seed",571);
            var primary=weapon.VData!.PrimaryAmmoType;var services=pawn.WeaponServices!;
            if(primary<services.Ammo.Length)services.Ammo[primary]=73;
            var inheritedInvalid=services.MyWeapons.Where(h=>h.Value is not {IsValid:true}).Select(h=>h.Raw).ToHashSet();
            var file=Path.Combine(ModuleDirectory,"native-result.json");
            weapon.CBodyComponent!.SceneNode!.GetSkeletonInstance()!.ModelState.MeshGroupMask=9;
            var cooldown=Server.CurrentTime+5;services.As<CCSPlayer_WeaponServices>().NextAttack=cooldown;
            void Finish(string status) { File.WriteAllText(file,JsonSerializer.Serialize(new { status,completed=25-remaining,logs })); }
            void Step()
            {
                if(!bot.IsValid || !bot.PawnIsAlive || bot.PlayerPawn.Raw!=pawnRaw || Utilities.GetPlayers().Any(p=>p.IsValid&&!p.IsBot&&!p.IsHLTV)) { Finish("cancelled");return; }
                if(remaining==0) { Finish("passed");return; }
                try
                {
                    var old=weapon;var raw=old.EntityHandle.Raw;
                    var snapshot=Activator.CreateInstance(type,[old,services])!;
                    pawn.ItemServices!.As<CCSPlayer_ItemServices>().DropActivePlayerWeapon(old);
                    if(services.MyWeapons.Any(h=>h.Raw==raw))throw new InvalidOperationException("Stale handle after drop");
                    old.Remove();
                    var attempts=0;
                    void Give()
                    {
                        if(!bot.IsValid || !bot.PawnIsAlive || bot.PlayerPawn.Raw!=pawnRaw || Utilities.GetPlayers().Any(p=>p.IsValid&&!p.IsBot&&!p.IsHLTV)) { Finish("cancelled");return; }
                        if(new CounterStrikeSharp.API.Modules.Utils.CHandle<CBasePlayerWeapon>(raw).Value is {IsValid:true})
                        { if(++attempts<8){Server.NextFrame(Give);return;}Finish("old handle stayed valid");return; }
                        try
                        {
                            weapon=bot.GiveNamedItem<CCSWeaponBase>("weapon_ak47")!;
                            type.GetMethod("Restore")!.Invoke(snapshot,[weapon,services]);
                            type.GetMethod("RestoreMesh")!.Invoke(snapshot,[weapon]);
                            var ak=services.MyWeapons.Select(h=>h.Value).Where(w=>w is {IsValid:true}&&w.DesignerName=="weapon_ak47").ToArray();
                            logs.Add(new { stage="verification", old=raw, fresh=weapon.EntityHandle.Raw, ak_count=ak.Length,owner=weapon.OwnerEntity.Raw,expected_owner=pawnRaw,
                                inventory=services.MyWeapons.Select(h=>new{raw=h.Raw,valid=h.Value?.IsValid,name=h.Value?.DesignerName}).ToArray(),
                                clip=weapon.Clip1,reserve=weapon.ReserveAmmo.ToArray(),paint=weapon.FallbackPaintKit,seed=weapon.FallbackSeed,nametag=weapon.AttributeManager.Item.CustomName,
                                primary,ammo=services.Ammo.ToArray(),attributes=Attributes(weapon.AttributeManager.Item.AttributeList),dynamic_attributes=Attributes(weapon.AttributeManager.Item.NetworkedDynamicAttributes) });
                            if(ak.Length!=1 || services.MyWeapons.Any(h=>h.Raw==raw || h.Value is not {IsValid:true} && !inheritedInvalid.Contains(h.Raw)) || weapon.EntityHandle.Raw==raw ||
                                weapon.Clip1!=17 || weapon.ReserveAmmo[0]!=29 || weapon.FallbackPaintKit!=302 || weapon.FallbackSeed!=571 ||
                                weapon.AttributeManager.Item.CustomName!="CW native preserve" || weapon.CBodyComponent!.SceneNode!.GetSkeletonInstance()!.ModelState.MeshGroupMask!=9 || services.As<CCSPlayer_WeaponServices>().NextAttack<cooldown || weapon.AttributeManager.Item.AttributeList.Attributes.Count!=3 ||
                                weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.Count!=2 || primary<services.Ammo.Length&&services.Ammo[primary]!=73)
                                throw new InvalidOperationException("Snapshot or inventory mismatch");
                            var attrs=ReadAttributes(weapon.AttributeManager.Item.AttributeList).ToDictionary(a=>a.AttributeDefinitionIndex);
                            if(attrs[6].Value!=302 || attrs[7].Value!=571 || !attrs.Values.Any(a=>BitConverter.SingleToInt32Bits(a.Value)==unchecked((int)0x7fc01234)))throw new InvalidOperationException("Multi-element attribute stride/bit mismatch");
                            logs.Add(new { old=raw,fresh=weapon.EntityHandle.Raw,clip=weapon.Clip1,reserve=weapon.ReserveAmmo.ToArray(),ammo=services.Ammo.ToArray(),attributes=Attributes(weapon.AttributeManager.Item.AttributeList),dynamic_attributes=Attributes(weapon.AttributeManager.Item.NetworkedDynamicAttributes) });
                            remaining--;Server.NextFrame(Step);
                        }
                        catch(Exception ex){Finish("failed "+ex.GetType().Name+" "+ex.InnerException?.GetType().Name+" "+ex.InnerException?.Message+" "+ex.InnerException?.StackTrace+" "+ex.Message);}
                    }
                    Server.NextFrame(Give);
                }
                catch(Exception ex){Finish("failed "+ex.GetType().Name+" "+ex.InnerException?.GetType().Name+" "+ex.InnerException?.Message+" "+ex.InnerException?.StackTrace+" "+ex.Message);}
            }
            Server.NextFrame(Step);command.ReplyToCommand("Native fixture started (25 replacements)");
        });
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
                                    definition = weapon.AttributeManager.Item.ItemDefinitionIndex, clip = weapon.Clip1, reserve = weapon.ReserveAmmo.ToArray(), item_id = weapon.AttributeManager.Item.ItemID,
                                    ammo = pawn.WeaponServices.Ammo.ToArray(), primary_ammo = weapon.As<CCSWeaponBase>().VData?.PrimaryAmmoType,
                                    paint = weapon.FallbackPaintKit, seed = weapon.FallbackSeed, wear = weapon.FallbackWear, nametag = weapon.AttributeManager.Item.CustomName,
                                    attributes = Attributes(weapon.AttributeManager.Item.AttributeList), dynamic_attributes = Attributes(weapon.AttributeManager.Item.NetworkedDynamicAttributes),
                                    model = Model(weapon), applied = Property(applied?[weapon.EntityHandle.Raw], "Model") });
                    rows.Add(new { slot = player.Slot, alive = player.PawnIsAlive, selected, saved,
                        qa_model = qaModel, qa_precached = qaModel != null && precached.Contains(qaModel),
                        active = pawn?.WeaponServices?.ActiveWeapon.Raw, last = pawn?.WeaponServices?.LastWeapon.Raw, held_pending = Property(session, "HeldRefresh") != null, held_seen = (Property(session, "HeldModels") as IDictionary)?.Count, database_ready = Property(Property(session, "Access"), "Ready"), inventory });
                }
                var ground = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_ak47")
                    .Where(w => w.IsValid).Take(12).Select(w => new { raw = w.EntityHandle.Raw, owner = w.OwnerEntity.Raw, model = Model(w),
                        applied = Property(applied?[w.EntityHandle.Raw], "Model") }).ToArray();
                var globalSkin = (Property(weapons?["weapon_ak47"], "Skins") as IDictionary)?["cwqa_ak47"];
                var globalModel = Property(globalSkin, "Model") as string;
                var json = JsonSerializer.Serialize(new { version = plugin.ModuleVersion,
                    pending_count = (Field(plugin, "_heldRefreshes") as IDictionary)?.Count, map_ready = Property(Field(plugin, "_lifecycle"), "MapReady"), precached = precached.Count,
                    qa_model = globalModel, qa_precached = globalModel != null && precached.Contains(globalModel),
                    players = rows, ak_entities = ground });
                File.WriteAllText(Path.Combine(ModuleDirectory,"state-current.json"),json);
                command.ReplyToCommand("State saved: version="+plugin.ModuleVersion+", players="+rows.Count+", AKs="+ground.Length);
            }
            catch (Exception ex) { command.ReplyToCommand("Inspection failed: " + ex.GetType().Name); }
        });
        AddCommand("css_cwqa_give_ak", "Console-only AK grant to the single test client", (caller, command) =>
        {
            if (caller != null) return;
            var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToArray();
            if (players.Length != 1 || !players[0].PawnIsAlive) { command.ReplyToCommand("Rejected: require one alive human"); return; }
            if (players[0].PlayerPawn.Value?.WeaponServices?.MyWeapons.Any(h => h.Value is { IsValid: true } w && w.DesignerName == "weapon_ak47") == true) { command.ReplyToCommand("Rejected: AK already in inventory"); return; }
            players[0].GiveNamedItem("weapon_ak47");
            command.ReplyToCommand("AK grant invoked for single test client");
        });
    }
}
