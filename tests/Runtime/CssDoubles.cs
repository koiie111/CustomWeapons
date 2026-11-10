// Managed boundary doubles. Production plugin source is linked unchanged by Runtime.csproj.
using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;

namespace CounterStrikeSharp.API
{
    public static class NativeAPI
    {
        public static readonly Dictionary<nint,CEconItemAttribute> Attributes=[];
        public static nint GetNetworkVectorElementAt(nint vector,int index)
        {
            if(index!=0) throw new Exception("Only element zero is stride-independent");
            var list=CAttributeList.Registry[vector].Attributes;var pointer=vector*1048576;
            for(var n=0;n<list.Count;n++)Attributes[pointer+n*64]=list[n];
            return pointer;
        }
    }
    public static class Server
    {
        public static readonly ConcurrentQueue<Action> World = new();
        public static readonly Queue<Action> Frames = new();
        public static int TickCount;
        public static readonly SortedDictionary<int, List<Action>> Ticks = [];
        public static void RunOnTick(int tick, Action action)
        {
            if (!Ticks.TryGetValue(tick, out var actions)) Ticks[tick] = actions = [];
            actions.Add(action);
        }
        public static void AdvanceTicks(int count)
        {
            for (var i = 0; i < count; i++)
            {
                TickCount++;
                if (Ticks.Remove(TickCount, out var actions)) foreach (var action in actions) action();
            }
        }
        public static void NextWorldUpdate(Action action) => World.Enqueue(action);
        public static void NextFrame(Action action) => Frames.Enqueue(action);
        public static void DrainWorld() { while (World.TryDequeue(out var action)) action(); }
        public static void DrainFrames()
        {
            var limit=256;
            while(Frames.Count!=0) { if(--limit==0)throw new Exception("Unbounded frame loop");StepFrame(); }
        }
        public static readonly Dictionary<int,int> NativeWork=[];
        public static void Work() => NativeWork[TickCount]=NativeWork.GetValueOrDefault(TickCount)+1;
        public static void StepFrame() { TickCount++; var count = Frames.Count; for (var i=0;i<count;i++) Frames.Dequeue()(); }
    }
    public static class Utilities
    {
        public static readonly List<CCSPlayerController> Players = [];
        public static int PlayerScans;
        public static void SetStateChanged(CEntityInstance entity, string type, string field) { }
        public static List<CCSPlayerController> GetPlayers() { PlayerScans++; return Players; }
        public static CCSPlayerController? GetPlayerFromSlot(int slot) => Players.Find(p => p.Slot == slot);
    }
}
namespace CounterStrikeSharp.API.Core
{
    public static class Entities
    {
        public static readonly Dictionary<uint, CEntityInstance> All = [];
        public static uint Next = 100;
    }
    public class CHandle<T>(uint raw) where T : CEntityInstance
    {
        public uint Raw { get; set; } = raw;
        public T? Value => Entities.All.GetValueOrDefault(Raw) as T;
        public bool IsValid => Value is { IsValid: true };
    }
    public class CEntityInstance
    {
        public CEntityInstance() { EntityHandle = new(Entities.Next++); Entities.All[EntityHandle.Raw] = this; }
        public bool IsValid { get; set; } = true;
        public int RemovalDelay;
        public void Remove()
        {
            void Finish() { if (RemovalDelay-- > 0) CounterStrikeSharp.API.Server.NextFrame(Finish); else IsValid = false; }
            Finish();
        }
        public string DesignerName { get; set; } = "";
        public CHandle<CEntityInstance> EntityHandle { get; set; }
    }
    public class CBaseModelEntity : CEntityInstance
    {
        public Body? CBodyComponent { get; set; } = new();
        public int Writes;
        public string Model = "models/stock.vmdl";
        public CHandle<CBaseModelEntity> OwnerEntity { get; set; } = new(0);
        public T As<T>() where T : CEntityInstance => (T)(object)this;
        public void SetModel(string model)
        {
            Model = model; Writes++;
            var state = CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState;
            if (state != null) state.ModelName = model;
        }
    }
    public sealed class Body { public Node? SceneNode { get; set; } = new(); }
    public sealed class Node { private readonly Skeleton _skeleton = new(); public Skeleton? GetSkeletonInstance() => _skeleton; }
    public sealed class Skeleton { public ModelState ModelState { get; } = new(); }
    public sealed class ModelState { public string ModelName { get; set; } = "models/stock.vmdl"; public ulong MeshGroupMask { get; set; } = 1; }
    public sealed class CEconItemAttribute
    {
        private sealed class Data { public ushort Index; public float Value,Initial; public int Currency; public bool Bonus; }
        private readonly Data _data = new();
        public CEconItemAttribute() { }
        public CEconItemAttribute(nint pointer) { _data=CounterStrikeSharp.API.NativeAPI.Attributes[pointer]._data; }
        public ushort AttributeDefinitionIndex { get=>_data.Index; set=>_data.Index=value; }
        public float Value { get=>_data.Value; set=>_data.Value=value; }
        public float InitialValue { get=>_data.Initial; set=>_data.Initial=value; }
        public int RefundableCurrency { get=>_data.Currency; set=>_data.Currency=value; }
        public bool SetBonus { get=>_data.Bonus; set=>_data.Bonus=value; }
    }
    public sealed class AttributeVector : List<CEconItemAttribute>, IEnumerable<CEconItemAttribute>
    {
        public nint Handle { get; set; }
        public void RemoveAll() => Clear();
        IEnumerator<CEconItemAttribute> IEnumerable<CEconItemAttribute>.GetEnumerator() => throw new NotSupportedException("Native CSS only enumerates CHandle<T>");
    }
    public sealed class CAttributeList
    {
        private static int _next;
        public static readonly Dictionary<nint,CAttributeList> Registry = [];
        public CAttributeList() { Handle = ++_next; Registry[Handle] = this; Attributes.Handle=Handle; }
        public nint Handle { get; }
        public AttributeVector Attributes { get; } = [];
    }
    public sealed class Item
    {
        public ushort ItemDefinitionIndex { get; set; } public ulong ItemID { get; set; }
        public string CustomName { get; set; } = "";
        public string CustomNameOverride { get; set; } = ""; public string CustomNameOverride2 { get; set; } = ""; public string CustomNameOverride3 { get; set; } = "";
        public int EntityQuality { get; set; } public uint EntityLevel { get; set; } public uint InventoryPosition { get; set; } public uint AccountID { get; set; }
        public bool Initialized { get; set; }
        public CAttributeList AttributeList { get; } = new(); public CAttributeList NetworkedDynamicAttributes { get; } = new();
    }
    public sealed class GameData { public static string GetSignature(string name) => name; }
    public class CEconEntity : CBaseModelEntity
    {
        public int FallbackPaintKit { get; set; } public int FallbackSeed { get; set; } public int FallbackStatTrak { get; set; } public float FallbackWear { get; set; }
        public uint OriginalOwnerXuidLow { get; set; } public uint OriginalOwnerXuidHigh { get; set; }
        public void AcceptInput(string name, string value) { }
    }
    public sealed class WeaponAttributes { public Item Item { get; } = new(); }
    public class CBasePlayerWeapon : CEconEntity
    {
        public static int DefinitionReads;
        private readonly WeaponAttributes _attributes = new();
        public WeaponAttributes AttributeManager { get { DefinitionReads++; return _attributes; } }
        public int NextPrimaryAttackTick { get; set; } public float NextPrimaryAttackTickRatio { get; set; }
        public int NextSecondaryAttackTick { get; set; } public float NextSecondaryAttackTickRatio { get; set; }
        public int Clip1 { get; set; } = 30;
        public int Clip2 { get; set; }
        public int[] ReserveAmmo { get; } = [90, 0];
    }
    public enum gear_slot_t { GEAR_SLOT_RIFLE, GEAR_SLOT_PISTOL, GEAR_SLOT_KNIFE, GEAR_SLOT_GRENADES, GEAR_SLOT_C4 }
    public sealed class CCSWeaponBaseVData { public gear_slot_t GearSlot { get; set; } public byte PrimaryAmmoType { get; set; } public byte SecondaryAmmoType { get; set; } = 1; }
    public class CCSWeaponBase : CBasePlayerWeapon
    {
        public bool BurstMode { get; set; } public bool SilencerOn { get; set; }
        public int IRecoilIndex { get; set; } public float FlRecoilIndex { get; set; } public float AccuracyPenalty { get; set; }
        private readonly CCSWeaponBaseVData _data = new();
        public CCSWeaponBaseVData? VData
        {
            get
            {
                _data.GearSlot = DesignerName=="weapon_c4" ? gear_slot_t.GEAR_SLOT_C4 : DesignerName.Contains("knife") ? gear_slot_t.GEAR_SLOT_KNIFE :
                    DesignerName.Contains("glock") || DesignerName.Contains("deagle") ? gear_slot_t.GEAR_SLOT_PISTOL :
                    DesignerName.Contains("grenade") || DesignerName.Contains("flashbang") || DesignerName.Contains("molotov") || DesignerName.Contains("decoy") || DesignerName.Contains("snowball") ? gear_slot_t.GEAR_SLOT_GRENADES : gear_slot_t.GEAR_SLOT_RIFLE;
                return _data;
            }
        }
    }
    public sealed class Weapon : CCSWeaponBase { }
    public class CPlayer_WeaponServices
    {
        public T As<T>() => (T)(object)this;
        public ushort[] Ammo { get; } = new ushort[32];
        public List<CHandle<CBasePlayerWeapon>> MyWeapons { get; } = [];
        public CHandle<CBasePlayerWeapon> ActiveWeapon { get; set; } = new(0);
        public CHandle<CBasePlayerWeapon> LastWeapon { get; set; } = new(0);
    }
    public class CCSPlayer_WeaponServices : CPlayer_WeaponServices { public float NextAttack { get; set; } }
    public sealed class WeaponServices : CCSPlayer_WeaponServices { }
    public sealed class CCSPlayer_ItemServices(Pawn pawn)
    {
        public bool BlockDrop;
        public T As<T>() => (T)(object)this;
        public void DropActivePlayerWeapon(CBasePlayerWeapon weapon)
        {
            if (BlockDrop) return;
            CounterStrikeSharp.API.Server.Work();
            var services=pawn.WeaponServices!;
            services.MyWeapons.RemoveAll(h=>h.Raw==weapon.EntityHandle.Raw);
            weapon.OwnerEntity=new(0);
            if(services.ActiveWeapon.Raw==weapon.EntityHandle.Raw) services.ActiveWeapon=new(services.MyWeapons.FirstOrDefault()?.Raw??0);
        }
    }
    public sealed class Pawn : CBaseModelEntity
    {
        public Pawn() { ItemServices=new(this); }
        public WeaponServices? WeaponServices { get; set; } = new();
        public CCSPlayer_ItemServices? ItemServices { get; set; }
    }
    public sealed class CCSPlayerController : CEntityInstance
    {
        public int Slot { get; set; }
        public ulong SteamID { get; set; }
        public bool IsBot { get; set; }
        public bool IsHLTV { get; set; }
        public bool PawnIsAlive { get; set; } = true;
        public CHandle<Pawn> PlayerPawn { get; set; } = new(0);
        public readonly List<string> ClientCommands = [];
        public int GrantFailures, Grants;
        public Action<Weapon>? OnGrant;
        public T? GiveNamedItem<T>(string name) where T:CEntityInstance
        {
            if(GrantFailures-->0)return null;
            Grants++; CounterStrikeSharp.API.Server.Work();
            var weapon=new Weapon { DesignerName=name, OwnerEntity=new(PlayerPawn.Raw) };
            weapon.AttributeManager.Item.ItemDefinitionIndex=(ushort)(name=="weapon_ak47"?7:name.Contains("glock")?4:42);
            weapon.AttributeManager.Item.ItemID=100000+weapon.EntityHandle.Raw;
            var services=PlayerPawn.Value!.WeaponServices!;
            services.MyWeapons.Add(new(weapon.EntityHandle.Raw)); services.ActiveWeapon=new(weapon.EntityHandle.Raw);
            OnGrant?.Invoke(weapon);
            return (T)(CEntityInstance)weapon;
        }
        public bool ClientSwitchEnabled = true;
        public void ExecuteClientCommand(string command)
        {
            ClientCommands.Add(command);
            if (!ClientSwitchEnabled || PlayerPawn.Value?.WeaponServices is not {} services) return;
            var slot = command == "slot1" ? gear_slot_t.GEAR_SLOT_RIFLE : command == "slot2" ? gear_slot_t.GEAR_SLOT_PISTOL : gear_slot_t.GEAR_SLOT_KNIFE;
            var weapon = services.MyWeapons.Select(h => h.Value).OfType<CCSWeaponBase>().FirstOrDefault(w => w.IsValid && w.VData?.GearSlot == slot);
            if (weapon == null) return;
            services.LastWeapon = services.ActiveWeapon;
            services.ActiveWeapon = new(weapon.EntityHandle.Raw);
        }
        public void PrintToChat(string _) { }
    }
    public sealed class CBaseCSGrenadeProjectile : CBaseModelEntity
    {
        public CHandle<Pawn> Thrower { get; set; } = new(0);
        public CHandle<Pawn> OriginalThrower { get; set; } = new(0);
    }
    public class BasePluginConfig { }
    public interface IPluginConfig<T> { T Config { get; set; } void OnConfigParsed(T config); }
    public enum HookResult { Continue }
    public sealed class EventInfo { }
    public class PlayerEvent { public CCSPlayerController? Userid { get; set; } }
    public sealed class EventPlayerConnectFull : PlayerEvent { }
    public sealed class EventPlayerSpawn : PlayerEvent { }
    public sealed class EventPlayerDeath : PlayerEvent { }
    public sealed class EventRoundStart { }
    public sealed class EventRoundEnd { }
    public sealed class EventItemPickup : PlayerEvent { public long Defindex { get; set; } }
    public sealed class EventItemEquip : PlayerEvent { }
    public sealed class Manifest { public void AddResource(string _) { } }
    public static class Listeners
    {
        public delegate void OnMapStart(string map);
        public delegate void OnMapEnd();
        public delegate void OnServerPrecacheResources(Manifest manifest);
        public delegate void OnClientDisconnect(int slot);
        public delegate void OnClientAuthorized(int slot, string steam);
        public delegate void OnEntityDeleted(CEntityInstance entity);
        public delegate void OnEntitySpawned(CEntityInstance entity);
        public delegate void OnTick();
    }
    public class BasePlugin
    {
        public virtual string ModuleName => "";
        public virtual string ModuleVersion => "";
        public virtual string ModuleAuthor => "";
        public virtual string ModuleDescription => "";
        public string ModuleDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "cw-runtime-" + Guid.NewGuid());
        public Microsoft.Extensions.Logging.TestLogger Logger { get; } = new();
        public readonly Dictionary<Type, Delegate> ListenersByType = [];
        public readonly Dictionary<Type, Delegate> Events = [];
        public readonly List<(float Interval, Action Callback)> Timers = [];
        public void RegisterListener<T>(T callback) where T : Delegate => ListenersByType.Add(typeof(T), callback);
        public void RegisterEventHandler<T>(Func<T, EventInfo, HookResult> callback) => Events.Add(typeof(T), callback);
        public void AddCommand(string name, string description, Action<CCSPlayerController?, object> callback) { }
        public void AddTimer(float interval, Action callback, Modules.Timers.TimerFlags flags) => Timers.Add((interval, callback));
        public virtual void Load(bool hotReload) { }
        public virtual void Unload(bool hotReload) { }
        public void Fire<T>(T ev) => ((Func<T, EventInfo, HookResult>)Events[typeof(T)])(ev, new());
    }
}
namespace CounterStrikeSharp.API.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class MinimumApiVersionAttribute(int version) : Attribute
    { public int Version => version; }
}
namespace CounterStrikeSharp.API.Modules.Timers { [Flags] public enum TimerFlags { REPEAT = 1 } }
namespace CounterStrikeSharp.API.Modules.Utils { }
namespace CounterStrikeSharp.API.Modules.Menu
{
    public interface IMenuInstance { }
    public enum PostSelectAction { Nothing }
    public sealed class CenterHtmlMenu(string title, BasePlugin plugin) : IMenuInstance
    {
        public string Title => title;
        public BasePlugin Plugin => plugin;
        public PostSelectAction PostSelectAction { get; set; }
        public void AddMenuOption(string text, Action<CCSPlayerController, object> callback, bool disabled = false) { }
        public void Open(CCSPlayerController player) => MenuManager.Active[player.EntityHandle.Raw] = this;
    }
    public static class MenuManager
    {
        public static readonly Dictionary<uint, IMenuInstance> Active = [];
        public static IMenuInstance? GetActiveMenu(CCSPlayerController player) => Active.GetValueOrDefault(player.EntityHandle.Raw);
        public static void CloseActiveMenu(CCSPlayerController player) => Active.Remove(player.EntityHandle.Raw);
    }
}
namespace Microsoft.Extensions.Logging
{
    public sealed class TestLogger
    {
        public void LogError(string text, params object?[] args) { }
        public void LogWarning(string text, params object?[] args) { }
        public void LogInformation(string text, params object?[] args) { }
    }
}

namespace CounterStrikeSharp.API.Modules.Memory.DynamicFunctions
{
    public sealed class MemoryFunctionVoid<T1,T2,T3>
    {
        public MemoryFunctionVoid(string signature) { _ = signature; }
        private static readonly Dictionary<string,ushort> Names = System.Text.Json.JsonSerializer.Deserialize<Dictionary<ushort,string>>(
            typeof(CBasePlayerWeapon).Assembly.GetManifestResourceStream("CustomWeapons.econ-attribute-names.json")!)!.ToDictionary(p=>p.Value,p=>p.Key);
        public void Invoke(T1 handle,T2 name,T3 value)
        {
            var list=CAttributeList.Registry[(nint)(object)handle!];
            list.Attributes.Add(new() { AttributeDefinitionIndex=Names[(string)(object)name!], Value=(float)(object)value! });
        }
    }
}


namespace CounterStrikeSharp.API.Modules.Memory
{
    public static class Schema { public static int GetClassSize(string name) => name=="CEconItemAttribute"?64:throw new Exception("Unexpected schema"); }
}
