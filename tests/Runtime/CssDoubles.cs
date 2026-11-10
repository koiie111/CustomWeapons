// Managed boundary doubles. Production plugin source is linked unchanged by Runtime.csproj.
using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;

namespace CounterStrikeSharp.API
{
    public static class Server
    {
        public static readonly ConcurrentQueue<Action> World = new();
        public static readonly Queue<Action> Frames = new();
        public static void NextWorldUpdate(Action action) => World.Enqueue(action);
        public static void NextFrame(Action action) => Frames.Enqueue(action);
        public static void DrainWorld() { while (World.TryDequeue(out var action)) action(); }
        public static void DrainFrames() { while (Frames.TryDequeue(out var action)) action(); }
    }
    public static class Utilities
    {
        public static readonly List<CCSPlayerController> Players = [];
        public static int PlayerScans;
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
        public uint Raw => raw;
        public T? Value => Entities.All.GetValueOrDefault(raw) as T;
        public bool IsValid => Value is { IsValid: true };
    }
    public class CEntityInstance
    {
        public CEntityInstance() { EntityHandle = new(Entities.Next++); Entities.All[EntityHandle.Raw] = this; }
        public bool IsValid { get; set; } = true;
        public string DesignerName { get; set; } = "";
        public CHandle<CEntityInstance> EntityHandle { get; set; }
    }
    public class CBaseModelEntity : CEntityInstance
    {
        public Body? CBodyComponent { get; set; } = new();
        public int Writes;
        public string Model = "models/stock.vmdl";
        public void SetModel(string model) { Model = model; Writes++; }
    }
    public sealed class Body { public Node? SceneNode { get; set; } = new(); }
    public sealed class Node { public Skeleton? GetSkeletonInstance() => new(); }
    public sealed class Skeleton { public ModelState ModelState { get; } = new(); }
    public sealed class ModelState { public string ModelName { get; set; } = "models/stock.vmdl"; }
    public sealed class Item { public ushort ItemDefinitionIndex { get; set; } }
    public sealed class WeaponAttributes { public Item Item { get; } = new(); }
    public sealed class Weapon : CBaseModelEntity
    {
        public static int DefinitionReads;
        private readonly WeaponAttributes _attributes = new();
        public WeaponAttributes AttributeManager { get { DefinitionReads++; return _attributes; } }
    }
    public sealed class WeaponServices
    {
        public List<CHandle<Weapon>> MyWeapons { get; } = [];
        public CHandle<Weapon> ActiveWeapon { get; set; } = new(0);
    }
    public sealed class Pawn : CBaseModelEntity { public WeaponServices? WeaponServices { get; set; } = new(); }
    public sealed class CCSPlayerController : CEntityInstance
    {
        public int Slot { get; set; }
        public ulong SteamID { get; set; }
        public bool IsBot { get; set; }
        public bool IsHLTV { get; set; }
        public bool PawnIsAlive { get; set; } = true;
        public CHandle<Pawn> PlayerPawn { get; set; } = new(0);
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
    public sealed class EventItemPickup : PlayerEvent { }
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
