using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CustomWeapons.Core;
using CustomWeapons.Hud.Api;
using System.Reflection;

internal static partial class Program
{
    private static async Task HudBridgeTests()
    {
        Check(HudBridgeRegistry.ContractVersion==2 && typeof(HudBridgeRegistry).Assembly.GetName().Version==new Version(2,0,0,0),"HUD API version contract");
        var plugin=await Start(false,true);var bridge=HudBridgeRegistry.Provider();
        Check(bridge.GetSnapshot() is {Loaded:true,MapReady:true,WebsiteEnabled:true,DatabaseReady:true},"HUD readiness snapshot");
        Check(bridge.GetSnapshot().DatabaseServerUuid=="11111111-1111-1111-1111-111111111111" && bridge.GetSnapshot().DatabaseName=="fixture","HUD snapshot database identity");
        Check(bridge.GetSnapshot().Models.Any(m=>m.Weapon=="weapon_ak47" && m.Id=="gun" && m.Precached),"HUD configured model and precache snapshot");
        Check(bridge.GetSnapshot().Models == bridge.GetSnapshot().Models,"model snapshot is cached");
        var player=Player(1);player.TeamNum=3;Call(plugin,"EnsureSession",player);
        var repo=new DelayedRepository();Set(plugin,"_repository",repo);
        Call(plugin,"RequestRefresh");await Until(()=>repo.Reads.Count==1);
        var request=new HudRefreshRequest(player.Slot,player.SteamID,player.EntityHandle.Raw,3,"weapon_ak47","gun");
        HudRefreshResult? observed=null;int completions=0;
        bridge.RequestRefresh(request,r=>{observed=r;completions++;});
        Check(repo.Reads.TryDequeue(out var oldRead),"older refresh enqueued");oldRead!.SetResult(Empty);
        await Until(()=>repo.Reads.Count==1);
        Check(observed==null,"older pre-request read cannot acknowledge new selection");
        Check(repo.Reads.TryDequeue(out var currentRead),"post-request refresh enqueued");
        currentRead!.SetResult(new DatabaseSnapshot([],[],[new WebsiteSelection(player.SteamID.ToString(),"weapon_ak47","gun",1,3)],"11111111-1111-1111-1111-111111111111","fixture"));
        await Until(()=>observed!=null);
        Check(observed==HudRefreshResult.Reconciled && completions==1,"confirmed database/cache reconciliation");
        var session=Call(plugin,"EnsureSession",player)!;
        var sessionFields=BindingFlags.Instance|BindingFlags.Public;
        var access=(AccessState)session.GetType().GetProperty("Access",sessionFields)!.GetValue(session)!;
        var overrides=(Dictionary<(int Team,string Weapon),WebsiteSelection>)session.GetType().GetField("WebsiteOverrides",sessionFields)!.GetValue(session)!;
        var pending=(Dictionary<(int Team,string Weapon),string>)session.GetType().GetField("WebsitePending",sessionFields)!.GetValue(session)!;
        var selectionRequest=new HudSelectionRequest(player.Slot,player.SteamID,player.EntityHandle.Raw,2,"weapon_ak47");
        var teamField=session.GetType().GetField("Team",sessionFields)!;
        var originalTeam=(int)teamField.GetValue(session)!;
        Check(bridge.GetEffectiveSelection(selectionRequest) is {Known:true,Supported:true,Model:"gun"},"automatic T choice resolved");
        Check(bridge.GetEffectiveSelection(selectionRequest with {Team=3}) is {Known:true,Model:"gun"},"CT stored choice resolved");
        overrides[(2,"weapon_ak47")]=new WebsiteSelection(player.SteamID.ToString(),"weapon_ak47","",1,2);
        Check(bridge.GetEffectiveSelection(selectionRequest) is {Known:true,Model:""},"T explicit stock overrides automatic selection");
        Check(overrides.Count==1 && (int)teamField.GetValue(session)! == originalTeam,"getter does not change session team or overrides");
        overrides[(3,"weapon_ak47")]=new WebsiteSelection(player.SteamID.ToString(),"weapon_ak47","",0,3);
        Check(bridge.GetEffectiveSelection(selectionRequest with {Team=3}) is {Known:true,Model:"gun"} && overrides.Count==2,
            "newer stored revision wins without pruning stale override");
        pending[(2,"weapon_ak47")]="gun";
        Check(!bridge.GetEffectiveSelection(selectionRequest).Known,"pending selection is unknown");
        pending.Clear();
        Check(!bridge.GetEffectiveSelection(selectionRequest with {SteamId=player.SteamID+1}).Known,"wrong owner is unknown");
        access.Fail();Check(!bridge.GetEffectiveSelection(selectionRequest).Known,"unready access is unknown");
        access.Succeed(new DatabaseSnapshot([],[],[new WebsiteSelection(player.SteamID.ToString(),"weapon_ak47","gun",1,3)],"11111111-1111-1111-1111-111111111111","fixture"));
        overrides.Clear();plugin.Config.Weapons["weapon_ak47"].Skins["gun"].Private=true;
        Check(bridge.GetEffectiveSelection(selectionRequest) is {Known:true,Model:""},"unowned private automatic model is not selected");
        plugin.Config.Weapons["weapon_ak47"].Skins["gun"].Private=false;
        observed=null;bridge.RequestRefresh(request,r=>observed=r);await Until(()=>repo.Reads.Count==1);
        Check(repo.Reads.TryDequeue(out var anonymousRead),"anonymous-identity read queued");
        anonymousRead!.SetResult(new DatabaseSnapshot([],[],[new WebsiteSelection(player.SteamID.ToString(),"weapon_ak47","gun",2,3)]));await Until(()=>observed!=null);
        Check(observed==HudRefreshResult.Deferred && !bridge.GetSnapshot().DatabaseReady && bridge.GetSnapshot().DatabaseServerUuid==null,"missing server UUID fails closed");
        HudRefreshResult? first=null,second=null; int firstCount=0,secondCount=0;
        bridge.RequestRefresh(request,r=>{first=r;firstCount++;});await Until(()=>repo.Reads.Count==1);
        bridge.RequestRefresh(request,r=>{second=r;secondCount++;});
        Check(first==HudRefreshResult.Superseded && firstCount==1,"same-owner request supersedes once");
        Check(repo.Reads.TryDequeue(out var coalesceOld),"coalesced old read queued");coalesceOld!.SetResult(Empty);await Until(()=>repo.Reads.Count==1);
        Check(second==null,"newest request awaits later read");Check(repo.Reads.TryDequeue(out var coalesceNew),"coalesced new read queued");
        coalesceNew!.SetResult(new DatabaseSnapshot([],[],[new WebsiteSelection(player.SteamID.ToString(),"weapon_ak47","gun",2,3)],"11111111-1111-1111-1111-111111111111","fixture"));await Until(()=>second!=null);
        Check(second==HudRefreshResult.Reconciled && secondCount==1 && firstCount==1,"newest request completes once");
        HudRefreshResult? invalid=null;bridge.RequestRefresh(request with { Slot=-1 },r=>invalid=r);Check(invalid==HudRefreshResult.Deferred,"invalid slot rejected");
        bridge.RequestRefresh(request with { Slot=-1 },_=>throw new InvalidOperationException("fixture"));
        observed=null;bridge.RequestRefresh(request,r=>observed=r);await Until(()=>repo.Reads.Count==1);
        Check(repo.Reads.TryDequeue(out var changed),"second read queued");changed!.SetResult(Empty);await Until(()=>observed!=null);
        Check(observed==HudRefreshResult.Superseded,"missing exact persisted selection rejected");
        observed=null;bridge.RequestRefresh(request,r=>observed=r);await Until(()=>repo.Reads.Count==1);
        Check(repo.Reads.TryDequeue(out var failedRead),"failure read queued");failedRead!.SetException(new IOException("fixture failure"));await Until(()=>observed!=null);
        Check(observed==HudRefreshResult.Error && !bridge.GetSnapshot().DatabaseReady,"failed SQL refresh reports error and clears readiness");
        observed=null;bridge.RequestRefresh(request,r=>observed=r);await Until(()=>repo.Reads.Count==1);
        player.SteamID++;Call(plugin,"EnsureSession",player);Check(repo.Reads.TryDequeue(out var stale),"slot-reuse read queued");
        stale!.SetResult(new DatabaseSnapshot([],[],[new WebsiteSelection(request.SteamId.ToString(),"weapon_ak47","gun",1,3)]));await Until(()=>observed!=null);
        Check(observed==HudRefreshResult.Deferred,"reused player slot cannot receive old completion");
        player.SteamID=request.SteamId;Call(plugin,"EnsureSession",player);
        observed=null;bridge.RequestRefresh(request,r=>observed=r);await Until(()=>repo.Reads.Count==1);
        int deferred=0;
        for(int i=0;i<512;i++) {
            var weapon="weapon_fixture_"+i;plugin.Config.Weapons[weapon]=new WeaponDefinition();
            bridge.RequestRefresh(request with { Weapon=weapon,ExpectedModel="" },r=> { if(r==HudRefreshResult.Deferred) deferred++; });
        }
        Check(deferred==1,"capacity 512 rejects excess waiter");
        ((Listeners.OnMapEnd)plugin.ListenersByType[typeof(Listeners.OnMapEnd)])();
        Check(observed==HudRefreshResult.Deferred && deferred==512,"map end releases every accepted waiter exactly once");
        var beforeUnload=HudBridgeRegistry.Instance.Generation;
        plugin.Unload(false);Check(!bridge.GetSnapshot().Loaded && HudBridgeRegistry.Instance.Generation>beforeUnload,"unloaded provider reports unavailable and bumps generation");
        var next=await Start(false,true);Check(ReferenceEquals(bridge,HudBridgeRegistry.Provider())&&bridge.GetSnapshot().Loaded,"capability provider is stable after reload");
        var beforeOldUnload=HudBridgeRegistry.Instance.Generation;
        plugin.Unload(false);Check(bridge.GetSnapshot().Loaded && HudBridgeRegistry.Instance.Generation==beforeOldUnload,"old unload cannot clear newer service or change generation");next.Unload(false);
        Check(!bridge.GetSnapshot().Loaded,"last unload clears service");
        var oldStub=new HudBridgeStub();var newStub=new HudBridgeStub();HudBridgeRegistry.Instance.SetCurrent(oldStub);
        HudRefreshResult? staleResult=null;int staleCalls=0;bridge.RequestRefresh(request,r=>{staleResult=r;staleCalls++;});
        var beforeSwap=HudBridgeRegistry.Instance.Generation;HudBridgeRegistry.Instance.SetCurrent(newStub);
        Check(HudBridgeRegistry.Instance.Generation>beforeSwap,"provider swap bumps generation");
        oldStub.Pending!(HudRefreshResult.Reconciled);oldStub.Pending!(HudRefreshResult.Reconciled);
        Check(staleResult==HudRefreshResult.Deferred && staleCalls==1,"stale service callback deferred and delivered once");
        HudBridgeRegistry.Instance.ClearCurrent(oldStub);Check(HudBridgeRegistry.Instance.Generation==beforeSwap+1,"stale clear leaves generation intact");
        HudBridgeRegistry.Instance.ClearCurrent(newStub);
    }
    private sealed class HudBridgeStub : IHudBridge
    {
        internal Action<HudRefreshResult>? Pending;
        public HudSnapshot GetSnapshot()=>new(false,false,false,0,false,null,null,[]);
        public HudEffectiveSelection GetEffectiveSelection(HudSelectionRequest request)=>new(false,false,null);
        public void RequestRefresh(HudRefreshRequest request,Action<HudRefreshResult> completed)=>Pending=completed;
    }
}
