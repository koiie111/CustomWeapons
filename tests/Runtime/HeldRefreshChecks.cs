#if HELD_REFRESH
using System.Collections;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CustomWeapons;

internal static partial class Program
{
    private static (CCSPlayerController Player, Weapon Target, Weapon Alternate) Armed(int slot)
    {
        var player = Player(slot);
        Give(player, "weapon_knife");
        var pistol = Give(player, "weapon_glock");
        var ak = Give(player, "weapon_ak47"); ak.AttributeManager.Item.ItemDefinitionIndex = 7;
        ak.AttributeManager.Item.ItemID = 98765;
        player.PlayerPawn.Value!.WeaponServices!.LastWeapon = new(pistol.EntityHandle.Raw);
        return (player, ak, pistol);
    }
    private static int Pending(CustomWeaponsPlugin plugin) => ((IDictionary)Get(plugin, "_heldRefreshes")!).Count;
    private static void Equip(CustomWeaponsPlugin plugin, CCSPlayerController player) => plugin.Fire(new EventItemEquip { Userid = player });
    private static void Select(CustomWeaponsPlugin plugin, CCSPlayerController player, string? id) => Call(plugin, "Select", player, "weapon_ak47", id);
    private static Weapon CurrentAk(CCSPlayerController player) => (Weapon)player.PlayerPawn.Value!.WeaponServices!.MyWeapons
        .Select(h=>h.Value).Single(w=>w is { IsValid:true } && w.DesignerName=="weapon_ak47")!;
    private static async Task HeldRefreshTests()
    {
        {
            var plugin=await Start(false);plugin.Config.CooldownSeconds=0;
            var (player,old,pistol)=Armed(1);var services=player.PlayerPawn.Value!.WeaponServices!;
            Select(plugin,player,null);Server.DrainFrames();
            Check(old.IsValid && player.Grants==0,"Never-custom stock item is not replaced");
            old.Clip1=13;old.Clip2=2;old.ReserveAmmo[0]=47;services.Ammo[0]=93;services.Ammo[7]=61;
            old.BurstMode=true;old.SilencerOn=true;old.NextPrimaryAttackTick=700;old.NextPrimaryAttackTickRatio=.5f;
            old.NextSecondaryAttackTick=800;old.AccuracyPenalty=.3f;old.IRecoilIndex=5;old.FlRecoilIndex=5.5f;
            old.FallbackPaintKit=302;old.FallbackSeed=571;old.FallbackWear=.16f;old.FallbackStatTrak=1234;
            old.OriginalOwnerXuidLow=555;old.OriginalOwnerXuidHigh=17;
            var item=old.AttributeManager.Item;item.CustomName="retained";item.AccountID=222;item.EntityQuality=9;item.EntityLevel=4;
            item.Initialized=true;item.CustomNameOverride="override";
            var special=BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234));
            item.AttributeList.Attributes.Add(new(){AttributeDefinitionIndex=6,Value=special,InitialValue=.7f,RefundableCurrency=42,SetBonus=true});
            item.AttributeList.Attributes.Add(new(){AttributeDefinitionIndex=7,Value=571});
            item.NetworkedDynamicAttributes.Attributes.Add(new(){AttributeDefinitionIndex=8,Value=.16f});
            item.NetworkedDynamicAttributes.Attributes.Add(new(){AttributeDefinitionIndex=7,Value=571});
            // Emulate another plugin painting the grant and emitting synchronous pickup/equip.
            player.OnGrant=w=>{
                w.FallbackPaintKit=999;w.AttributeManager.Item.CustomName="new preference";
                w.AttributeManager.Item.AttributeList.Attributes.Add(new(){AttributeDefinitionIndex=7,Value=99});
                services.Ammo[0]=200;services.Ammo[7]=60;w.NextSecondaryAttackTick=900;
                plugin.Fire(new EventItemPickup { Userid=player,Defindex=7 });Equip(plugin,player);
            };
            Select(plugin,player,"gun");Server.StepFrame();
            Check(Pending(plugin)==1 && !old.IsValid && services.MyWeapons.All(h=>h.Raw!=old.EntityHandle.Raw) && player.Grants==0,
                "Detach inventory before removal and wait a frame before grant");
            Server.DrainFrames();var fresh=CurrentAk(player);var restored=fresh.AttributeManager.Item;
            Check(fresh.EntityHandle.Raw!=old.EntityHandle.Raw && player.Grants==1 && Pending(plugin)==0 && services.MyWeapons.Count==3,
                "Exactly one fresh instance, no retired inventory handle or recursion");
            Check(fresh.Model=="models/gun.vmdl" && services.ActiveWeapon.Raw==fresh.EntityHandle.Raw,"Model applied before selecting new item");
            Check(fresh.Clip1==13 && fresh.Clip2==2 && fresh.ReserveAmmo[0]==47 && services.Ammo[0]==93 && services.Ammo[7]==60,
                "Restore target ammo without rolling back another ammo pool");
            Check(fresh.BurstMode && fresh.SilencerOn && fresh.NextPrimaryAttackTick==700 && fresh.NextPrimaryAttackTickRatio==.5f &&
                fresh.NextSecondaryAttackTick==900 && fresh.AccuracyPenalty==.3f && fresh.IRecoilIndex==5 && fresh.FlRecoilIndex==5.5f,
                "Preserve modes/recoil and stricter old or new cooldown");
            Check(restored.ItemID!=item.ItemID && restored.CustomName=="retained" && restored.AccountID==222 && restored.EntityQuality==9 &&
                restored.EntityLevel==4 && restored.Initialized && restored.CustomNameOverride=="override","Fresh unique ID, preserved economy metadata");
            Check(fresh.FallbackPaintKit==302 && fresh.FallbackSeed==571 && fresh.FallbackWear==.16f && fresh.FallbackStatTrak==1234 &&
                fresh.OriginalOwnerXuidLow==555 && fresh.OriginalOwnerXuidHigh==17,"Preserve skin, seed, wear, StatTrak and original owner");
            var attr=restored.AttributeList.Attributes[0];
            Check(attr.AttributeDefinitionIndex==6 && BitConverter.SingleToInt32Bits(attr.Value)==unchecked((int)0x7fc01234) &&
                attr.InitialValue==.7f && attr.RefundableCurrency==42 && attr.SetBonus && restored.AttributeList.Attributes.Count==2 && restored.AttributeList.Attributes[1].Value==571 && restored.NetworkedDynamicAttributes.Attributes[0].Value==.16f && restored.NetworkedDynamicAttributes.Attributes[1].Value==571,
                "Both attribute lists restore exact values including NaN payload bits and metadata");
            Utilities.PlayerScans=0;Weapon.DefinitionReads=0;
            for(var n=0;n<1000;n++){Equip(plugin,player);Server.DrainFrames();}
            Check(player.Grants==1 && Weapon.DefinitionReads==0 && Utilities.PlayerScans==0,"Repeated equip has no clone, inventory scan or definition reads");
            for(var n=0;n<100;n++)plugin.Fire(new EventItemPickup{Userid=player,Defindex=7});Server.DrainFrames();
            Check(player.Grants==1,"Pickup of already refreshed same-owner item cannot cause replacement loop");
            Select(plugin,player,"gun");Server.DrainFrames();Check(player.Grants==1,"Repeated model choice is a no-op");
            player.OnGrant=null;services.ActiveWeapon=new(pistol.EntityHandle.Raw);Select(plugin,player,null);Server.DrainFrames();
            Check(player.Grants==1 && fresh.Model=="models/stock.vmdl","Inactive stock reversion waits for equip");
            services.ActiveWeapon=new(fresh.EntityHandle.Raw);Equip(plugin,player);Server.DrainFrames();
            Check(player.Grants==2 && CurrentAk(player).Model=="models/stock.vmdl","Inactive stock reversion gets fresh stock instance on equip");
            plugin.Unload(false);
        }
        {
            var plugin=await Start(false);var(player,old,_)=Armed(1);old.RemovalDelay=2;Safety(plugin)();Server.StepFrame();
            Check(old.IsValid && Pending(plugin)==1 && player.Grants==0,"Retired serial can remain valid after removal request");
            Server.StepFrame();Check(old.IsValid && player.Grants==0,"No grant while retired serial is valid");
            Server.DrainFrames();Check(!old.IsValid && player.Grants==1 && CurrentAk(player).EntityHandle.Raw!=old.EntityHandle.Raw,"Grant follows complete serial invalidation");
            plugin.Unload(false);
        }
        foreach(var cancellation in new[]{"death","spawn","disconnect","session","pawn","map","round-start","round-end","manual","new-slot-item"})
        {
            var plugin=await Start(false);var(player,_,pistol)=Armed(1);Safety(plugin)();Server.StepFrame();
            Check(Pending(plugin)==1,"Pending replacement prepared: "+cancellation);
            var services=player.PlayerPawn.Value!.WeaponServices!;
            switch(cancellation)
            {
                case "death":player.PawnIsAlive=false;plugin.Fire(new EventPlayerDeath{Userid=player});break;
                case "spawn":plugin.Fire(new EventPlayerSpawn{Userid=player});break;
                case "disconnect":Disconnect(plugin,1);break;
                case "session":Utilities.Players.Remove(player);Equip(plugin,Armed(1).Player);break;
                case "pawn":player.PlayerPawn=new(new Pawn().EntityHandle.Raw);break;
                case "map":MapEnd(plugin);break;
                case "round-start":plugin.Fire(new EventRoundStart());break;
                case "round-end":plugin.Fire(new EventRoundEnd());break;
                case "manual":services.ActiveWeapon=new(pistol.EntityHandle.Raw);break;
                case "new-slot-item":Give(player,"weapon_awp");break;
            }
            Server.DrainFrames();
            Check(Pending(plugin)==0 && player.Grants==(cancellation=="manual"?1:0),"Reject stale/occupied replacement: "+cancellation);
            if(cancellation=="manual")Check(services.ActiveWeapon.Raw==pistol.EntityHandle.Raw,"Retain later manual selection after normal acquisition");
            plugin.Unload(false);
        }
        foreach(var hazard in new[]{"unknown-attribute","duplicate-attribute","blocked-drop","duplicate-slot","grenade"})
        {
            var plugin=await Start();var(player,old,_)=Armed(1);
            if(hazard=="unknown-attribute")old.AttributeManager.Item.AttributeList.Attributes.Add(new(){AttributeDefinitionIndex=65535});
            if(hazard=="duplicate-attribute")for(var n=0;n<2;n++)old.AttributeManager.Item.AttributeList.Attributes.Add(new(){AttributeDefinitionIndex=6});
            if(hazard=="blocked-drop")player.PlayerPawn.Value!.ItemServices!.BlockDrop=true;
            if(hazard=="duplicate-slot"){Give(player,"weapon_awp");player.PlayerPawn.Value!.WeaponServices!.ActiveWeapon=new(old.EntityHandle.Raw);}
            if(hazard=="grenade")Give(player,"weapon_flashbang");
            Safety(plugin)();Server.DrainFrames();
            Check(old.IsValid && player.Grants==0 && Pending(plugin)==0,"Reject before destroying original: "+hazard);
            plugin.Unload(false);
        }
        {
            var plugin=await Start(false);var(player,old,_)=Armed(1);player.PlayerPawn.Value!.WeaponServices!.MyWeapons.RemoveAll(h=>h.Raw!=old.EntityHandle.Raw);
            player.GrantFailures=2;Safety(plugin)();Server.DrainFrames();
            Check(player.Grants==1 && CurrentAk(player).Model=="models/gun.vmdl" && Pending(plugin)==0,"Single-item inventory and bounded transient grant retry");
            plugin.Unload(false);
        }
        {
            var plugin=await Start(false);var(player,_,_)=Armed(1);Safety(plugin)();Server.StepFrame();plugin.Unload(false);Server.DrainFrames();
            Check(player.Grants==1 && CurrentAk(player).Model=="models/stock.vmdl" && Pending(plugin)==0,"Unload restores retired item with original model and cancels callback");
        }
        {
            var plugin=await Start(false);plugin.Config.CooldownSeconds=0;var(player,_,_)=Armed(1);Safety(plugin)();Server.StepFrame();
            Select(plugin,player,null);Server.DrainFrames();
            Check(player.Grants==1 && CurrentAk(player).Model=="models/stock.vmdl","Menu revision during retirement replaces pending desired model");
            plugin.Unload(false);
        }
        {
            var plugin=await Start(false);var(first,_,_)=Armed(1);Safety(plugin)();Server.DrainFrames();var carried=CurrentAk(first);
            carried.FallbackPaintKit=302;carried.AttributeManager.Item.AccountID=111;carried.AttributeManager.Item.CustomName="transferred";
            carried.AttributeManager.Item.AttributeList.Attributes.Add(new(){AttributeDefinitionIndex=6,Value=302});
            var second=Player(2);Give(second,"weapon_knife");Give(second,"weapon_glock");
            first.PlayerPawn.Value!.ItemServices!.DropActivePlayerWeapon(carried);
            var services=second.PlayerPawn.Value!.WeaponServices!;services.MyWeapons.Add(new(carried.EntityHandle.Raw));carried.OwnerEntity=new(second.PlayerPawn.Raw);
            second.OnGrant=w=>{w.FallbackPaintKit=999;w.AttributeManager.Item.CustomName="recipient preference";};
            plugin.Fire(new EventItemPickup{Userid=second,Defindex=7});Server.DrainFrames();
            Check(second.Grants==0,"Pickup may precede active weapon changing");
            services.ActiveWeapon=new(carried.EntityHandle.Raw);Equip(plugin,second);Server.DrainFrames();var fresh=CurrentAk(second);
            Check(second.Grants==1 && !carried.IsValid && fresh.FallbackPaintKit==302 && fresh.AttributeManager.Item.AccountID==111 &&
                fresh.AttributeManager.Item.CustomName=="transferred" && fresh.AttributeManager.Item.AttributeList.Attributes[0].Value==302,
                "New owner retains actual transferred item appearance rather than recipient preference");
            plugin.Unload(false);
        }
        {
            var plugin=await Start(false);var(player,old,_)=Armed(1);var services=player.PlayerPawn.Value!.WeaponServices!;
            old.Clip1=19;old.FallbackPaintKit=302;services.NextAttack=10;old.CBodyComponent!.SceneNode!.GetSkeletonInstance()!.ModelState.MeshGroupMask=9;
            player.OnGrant=w=>{services.NextAttack=2;Server.NextFrame(()=>{w.Clip1--;w.FallbackPaintKit=999;w.SetModel("models/other-plugin.vmdl");w.CBodyComponent!.SceneNode!.GetSkeletonInstance()!.ModelState.MeshGroupMask=2;});};
            Safety(plugin)();Server.DrainFrames();var fresh=CurrentAk(player);
            Check(player.Grants==1 && fresh.Clip1==18 && fresh.FallbackPaintKit==302 && fresh.Model=="models/gun.vmdl" && services.NextAttack>=10 && fresh.CBodyComponent!.SceneNode!.GetSkeletonInstance()!.ModelState.MeshGroupMask==9,
                "Settle deferred paint/model hooks without rolling back shots, mesh state or pawn cooldown");
            plugin.Unload(false);
        }
        {
            var plugin=await Start(false);var(player,old,_)=Armed(1);var bomb=Give(player,"weapon_c4");
            player.PlayerPawn.Value!.WeaponServices!.ActiveWeapon=new(old.EntityHandle.Raw);Safety(plugin)();Server.DrainFrames();
            Check(player.Grants==1 && bomb.IsValid && bomb.OwnerEntity.Raw==player.PlayerPawn.Raw && player.PlayerPawn.Value.WeaponServices.MyWeapons.Any(h=>h.Raw==bomb.EntityHandle.Raw),
                "Rifle replacement never removes C4 or other occupied slots");plugin.Unload(false);
        }
        foreach(var count in new[]{0,1,8,32})
        {
            var plugin=await Start(false);var players=Enumerable.Range(0,count).Select(i=>Armed(i).Player).ToArray();Safety(plugin)();Utilities.PlayerScans=0;
            foreach(var player in players)for(var i=0;i<100;i++)Equip(plugin,player);
            var startTick=Server.TickCount;Server.NativeWork.Clear();
            Check(Server.Frames.Count==count,"Burst coalesces one check/session: "+count);Server.StepFrame();
            Check(Pending(plugin)==Math.Min(count,4) && Server.Frames.Count==count,"Budgeted replacement queue: "+count);Server.DrainFrames();
            Check(Server.NativeWork.Values.All(n=>n<=4) && Server.TickCount-startTick<=25,"At most4retire/grant operations per frame without retry starvation: "+count);
            Check(Pending(plugin)==0 && Utilities.PlayerScans==0 && players.All(p=>p.Grants==1),"Exactly one replacement/session without global scans: "+count);
            Check(plugin.Timers.Count==2 && !plugin.ListenersByType.ContainsKey(typeof(Listeners.OnTick)),"No extra repeating timer or gun-only tick: "+count);
            plugin.Unload(false);
        }
    }
}
#endif
