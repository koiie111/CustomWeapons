#if WEBSITE_SELECTIONS
using System.Collections;
using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CustomWeapons.Core;

internal static partial class Program
{
    private sealed class WebsiteRepository : IAccessRepository, IWebsiteSelectionRepository
    {
        public DatabaseSnapshot Snapshot = Empty;
        public int Reads, Imports;
        public bool ImportFailure;
        public readonly ConcurrentQueue<TaskCompletionSource<long?>> Writes = new();
        public readonly ConcurrentQueue<(string Steam, int Team, string Weapon, string Model, long Revision)> Requests = new();
        public Task<DatabaseSnapshot> ReadAsync(IReadOnlyCollection<string> ids, CancellationToken ct) { Reads++; return Task.FromResult(Snapshot); }
        public Task ImportSelectionsAsync(IReadOnlyCollection<WebsiteSelection> selections, CancellationToken ct)
        { Imports++; if(ImportFailure) throw new IOException("fixture import failure"); return Task.CompletedTask; }
        public Task<long?> SaveSelectionAsync(string steam, int team, string weapon, string model, long revision, CancellationToken ct)
        {
            Requests.Enqueue((steam,team,weapon,model,revision));
            var result=new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);Writes.Enqueue(result);return result.Task.WaitAsync(ct);
        }
    }
    private static async Task WebsiteTests()
    {
        await WebsiteDecorationTests();
        var disabled=await Start(false);Check(disabled.Commands.Count==0,"Website flag off adds no refresh hooks");disabled.Unload(false);
        var plugin=await Start(false,true);plugin.Config.CooldownSeconds=0;
        Check(plugin.Commands.Keys.Order().SequenceEqual(new[]{"css_wp","say","say_team"}),"Website hooks registered once");
        var player=Player(1);Give(player,"weapon_ak47");Safety(plugin)();
        await Until(() => !(bool)Get(plugin,"_refreshRunning")!);
        var session=((IDictionary)Get(plugin,"_sessions")!)[1]!;
        var access=(AccessState)session.GetType().GetProperty("Access")!.GetValue(session)!;
        var id=player.SteamID.ToString();
        var snapshot=new DatabaseSnapshot([],[],[new(id,"weapon_ak47","gun",1,0),new(id,"weapon_ak47","",2,3)]);
        access.Succeed(snapshot);
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="","Explicit CT stock overrides legacy global choice");
        player.TeamNum=2;Call(plugin,"EnsureSession",player);
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="gun","T falls back to legacy global preference");
        player.TeamNum=3;Call(plugin,"EnsureSession",player);
        var repo=new WebsiteRepository{Snapshot=snapshot};Set(plugin,"_repository",repo);
        Call(plugin,"ApplyWebsiteChanges",player,session);Server.DrainFrames();
        var beforePreview=CurrentAk(player);beforePreview.Clip1=4;
        beforePreview.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.Add(new(){AttributeDefinitionIndex=80,Value=BitConverter.Int32BitsToSingle(321)});
        var beforePreviewRaw=beforePreview.EntityHandle.Raw;
        Call(plugin,"Select",player,"weapon_ak47","gun");await Until(()=>repo.Writes.Count==1);
        repo.Requests.TryPeek(out var request);Check(request.Team==3&&request.Revision==2&&request.Model=="gun","Menu write uses current side and exact SQL revision");
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="gun","Pending SQL write immediately previews the validated model");
        Server.DrainFrames();Check(CurrentAk(player).Model=="models/gun.vmdl","Native model settles before the SQL write is acknowledged");
        Check(CurrentAk(player).EntityHandle.Raw!=beforePreviewRaw,"Website preview replaces the held instance before SQL acknowledgement");
        Check(CurrentAk(player).Clip1==4&&BitConverter.SingleToInt32Bits(CustomWeapons.EconAttributeVector.Read(CurrentAk(player).AttributeManager.Item.NetworkedDynamicAttributes).Single(a=>a.AttributeDefinitionIndex==80).Value)==321,"Immediate preview retains ammo and native StatTrak bits");
        Check(((IDictionary)session.GetType().GetField("WebsiteOverrides")!.GetValue(session)!).Count==0,"Preview is not a committed SQL override");
        Call(plugin,"Select",player,"weapon_ak47",null);Check(repo.Writes.Count==1,"Repeated selection cannot queue a second write while saving");
        access.Succeed(new([],[],[new(id,"weapon_ak47","",2,3),new(id,"weapon_ak47","",6,2)]));
        player.TeamNum=2;Call(plugin,"EnsureSession",player);
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="","Pending CT model cannot replace the explicit T stock choice");
        player.TeamNum=3;Call(plugin,"EnsureSession",player);access.Succeed(snapshot);
        repo.Writes.TryDequeue(out var saved);saved!.SetResult(3);
        await Until(()=>((IDictionary)session.GetType().GetField("WebsiteOverrides")!.GetValue(session)!).Count==1);
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="gun","Newly committed menu choice survives older in-flight snapshot");
        access.Succeed(new([],[],[new(id,"weapon_ak47","",4,3)]));
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="","Newer website choice supersedes pending menu override");
        Call(plugin,"Select",player,"weapon_ak47","gun");await Until(()=>repo.Writes.Count==1);repo.Writes.TryDequeue(out var conflict);conflict!.SetResult(null);
        await Until(()=>((HashSet<string>)session.GetType().GetField("WebsiteWrites")!.GetValue(session)!).Count==0);
        Check(((IDictionary)session.GetType().GetField("WebsiteOverrides")!.GetValue(session)!).Count==0,"Conflicted menu write does not publish an override");
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="","Conflicted write reverts the temporary model to confirmed stock");
        Check(((IDictionary)session.GetType().GetField("WebsitePending")!.GetValue(session)!).Count==0,"Conflict clears the temporary preview");
        Server.DrainFrames();Check(CurrentAk(player).Model!="models/gun.vmdl"&&CurrentAk(player).Clip1==4&&BitConverter.SingleToInt32Bits(CustomWeapons.EconAttributeVector.Read(CurrentAk(player).AttributeManager.Item.NetworkedDynamicAttributes).Single(a=>a.AttributeDefinitionIndex==80).Value)==321,"Conflict restores held stock model without losing ammo or StatTrak");
        Call(plugin,"Select",player,"weapon_ak47","gun");await Until(()=>repo.Writes.Count==1);repo.Writes.TryDequeue(out var failed);failed!.SetException(new IOException("fixture write failure"));
        await Until(()=>((HashSet<string>)session.GetType().GetField("WebsiteWrites")!.GetValue(session)!).Count==0);
        Check((string?)Call(plugin,"Selected",session,"weapon_ak47")=="","Failed SQL write rolls back its temporary model");
        Check(((IDictionary)session.GetType().GetField("WebsitePending")!.GetValue(session)!).Count==0,"Failed write releases temporary preference");
        player.TeamNum=0;Call(plugin,"Select",player,"weapon_ak47","gun");Check(repo.Writes.Count==0,"Spectator cannot change global legacy preference");
        player.TeamNum=3;Call(plugin,"Select",player,"weapon_ak47","gun");await Until(()=>repo.Writes.Count==1);repo.Writes.TryDequeue(out var stale);
        Disconnect(plugin,1);player.IsValid=false;Utilities.Players.Remove(player);var replacement=Player(1);replacement.SteamID++;
        var replacementSession=Call(plugin,"EnsureSession",replacement)!;stale!.SetResult(5);await Task.Delay(30);Server.DrainWorld();
        Check(((IDictionary)replacementSession.GetType().GetField("WebsiteOverrides")!.GetValue(replacementSession)!).Count==0,"Disconnected player's delayed save cannot publish into reused slot");
        await Until(() => !(bool)Get(plugin,"_refreshRunning")!);
        var reads=repo.Reads;plugin.Commands["say"](replacement,new CommandInfo("say","hello"));await Task.Delay(10);
        Check(repo.Reads==reads,"Ordinary chat does not refresh SQL");
        plugin.Commands["css_wp"](replacement,new CommandInfo("css_wp"));await Until(()=>repo.Reads>reads);await Until(() => !(bool)Get(plugin,"_refreshRunning")!);
        reads=repo.Reads;plugin.Commands["say_team"](replacement,new CommandInfo("say_team","!wp"));await Task.Delay(10);
        Check(repo.Reads==reads,"Duplicate !wp across command paths is rate limited");
        var selections=(SelectionStore)Get(plugin,"_selections")!;selections.Set(replacement.SteamID.ToString(),"weapon_ak47","gun");
        repo.ImportFailure=true;Call(plugin,"RequestRefresh");await Until(() => !(bool)Get(plugin,"_refreshRunning")!);
        var replacementAccess=(AccessState)replacementSession.GetType().GetProperty("Access")!.GetValue(replacementSession)!;
        Check(repo.Imports>0&&!replacementAccess.Ready,"Failed legacy import cannot publish a successful access snapshot");
        MapEnd(plugin);((Listeners.OnMapStart)plugin.ListenersByType[typeof(Listeners.OnMapStart)])("next");
        Check(plugin.Commands.Count==3,"Map change does not duplicate refresh command hooks");plugin.Unload(false);
    }

    private static async Task WebsiteDecorationTests()
    {
        var plugin = await Start(false, true); plugin.Config.CooldownSeconds = 0;
        var (player, original, _) = Armed(1);
        original.Clip1 = 17;
        foreach (var list in new[] { original.AttributeManager.Item.AttributeList, original.AttributeManager.Item.NetworkedDynamicAttributes })
        {
            list.Attributes.Add(new() { AttributeDefinitionIndex = 80, Value = BitConverter.Int32BitsToSingle(321) });
            list.Attributes.Add(new() { AttributeDefinitionIndex = 113, Value = BitConverter.Int32BitsToSingle(1) });
            list.Attributes.Add(new() { AttributeDefinitionIndex = 299, Value = BitConverter.Int32BitsToSingle(37), InitialValue = .3f, SetBonus = true });
            list.Attributes.Add(new() { AttributeDefinitionIndex = 321, Value = BitConverter.Int32BitsToSingle(1) });
        }
        Safety(plugin)(); Server.DrainFrames();
        var custom = CurrentAk(player);
        Check(custom.Model == "models/gun.vmdl" && custom.Clip1 == 17, "Selection-only custom model still replaces held gun and retains ammo");
        foreach (var list in new[] { custom.AttributeManager.Item.AttributeList, custom.AttributeManager.Item.NetworkedDynamicAttributes })
        {
            var values = WeaponStateValues(list);
            Check(values.Length == 1 && values[0].AttributeDefinitionIndex == 80 && BitConverter.SingleToInt32Bits(values[0].Value) == 321,
                "Custom model hides both unsupported decoration lists while preserving live StatTrak bits");
        }
        // A normal give/paint path can add attributes before model application.
        // Reapplying the same model must suppress those without another clone.
        var grants = player.Grants;
        custom.AttributeManager.Item.AttributeList.Attributes.Add(new() { AttributeDefinitionIndex = 299, Value = BitConverter.Int32BitsToSingle(12) });
        Call(plugin, "ApplyInventory", player, "weapon_ak47"); Server.DrainFrames();
        Check(player.Grants == grants && !WeaponStateValues(custom.AttributeManager.Item.AttributeList).Any(a => a.AttributeDefinitionIndex == 299),
            "Repeated custom application hides newly painted accessories without replacing the same held model");
        var session = Call(plugin, "EnsureSession", player)!;
        var access = (AccessState)session.GetType().GetProperty("Access")!.GetValue(session)!;
        access.Succeed(new([], [], [new(player.SteamID.ToString(), "weapon_ak47", "", 2, 3)]));
        Call(plugin, "ApplyWebsiteChanges", player, session); Server.DrainFrames();
        var stock = CurrentAk(player);
        Check(stock.Model == "models/stock.vmdl" && stock.Clip1 == 17, "Stock selection restores original model and ammo");
        Check(WeaponStateValues(stock.AttributeManager.Item.AttributeList).Any(a => a.AttributeDefinitionIndex == 299 && BitConverter.SingleToInt32Bits(a.Value) == 12),
            "Stock reversion restores the latest suppressed native charm");
        Check(WeaponStateValues(stock.AttributeManager.Item.NetworkedDynamicAttributes).Any(a => a.AttributeDefinitionIndex == 80 && BitConverter.SingleToInt32Bits(a.Value) == 321),
            "Custom-to-stock transition keeps native StatTrak");
        Check(WeaponStateValues(stock.AttributeManager.Item.AttributeList).Any(a => a.AttributeDefinitionIndex == 113)
            && WeaponStateValues(stock.AttributeManager.Item.NetworkedDynamicAttributes).Any(a => a.AttributeDefinitionIndex == 321),
            "Partial later accessory writes do not discard previously suppressed stickers or the other attribute list");
        plugin.Unload(false);
    }
    private static CEconItemAttribute[] WeaponStateValues(CAttributeList list) => CustomWeapons.EconAttributeVector.Read(list);
}
#endif
