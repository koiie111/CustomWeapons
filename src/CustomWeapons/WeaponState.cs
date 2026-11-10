using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Memory;

namespace CustomWeapons;

// Cold, per-replacement snapshots. No schema reads or native attribute calls in tick/safety.
internal sealed class WeaponState
{
    private sealed record Attribute(ushort Index, float Value, float Initial, int Currency, bool Bonus);
    private static readonly Dictionary<ushort, string> AttributeNames = JsonSerializer.Deserialize<Dictionary<ushort, string>>(
        typeof(WeaponState).Assembly.GetManifestResourceStream("CustomWeapons.econ-attribute-names.json")!)!;
    private static MemoryFunctionVoid<nint, string, float>? _writeAttribute;
    private readonly ushort _definition;
    private readonly int _clip1, _clip2, _primaryTick, _secondaryTick, _quality, _paint, _seed, _stattrak, _recoil;
    private readonly float _primaryRatio, _secondaryRatio, _wear, _accuracy, _recoilFloat;
    private readonly int[] _reserve;
    private readonly (byte Index, ushort Count)[] _ammo;
    private readonly bool _burst, _silencer, _initialized;
    private readonly uint _account, _level, _inventory, _ownerLow, _ownerHigh;
    private readonly string _name, _override1, _override2, _override3;
    private readonly Attribute[] _attributes, _dynamic;
    private readonly ulong? _mesh;
    private readonly float? _nextAttack;

    public WeaponState(CCSWeaponBase weapon, CPlayer_WeaponServices services)
    {
        var item = weapon.AttributeManager.Item;
        _definition = item.ItemDefinitionIndex;
        _clip1 = weapon.Clip1; _clip2 = weapon.Clip2; _reserve = weapon.ReserveAmmo.ToArray();
        _primaryTick = weapon.NextPrimaryAttackTick; _primaryRatio = weapon.NextPrimaryAttackTickRatio;
        _secondaryTick = weapon.NextSecondaryAttackTick; _secondaryRatio = weapon.NextSecondaryAttackTickRatio;
        _burst = weapon.BurstMode; _silencer = weapon.SilencerOn;
        _accuracy = weapon.AccuracyPenalty; _recoil = weapon.IRecoilIndex; _recoilFloat = weapon.FlRecoilIndex;
        _quality = item.EntityQuality; _level = item.EntityLevel; _inventory = item.InventoryPosition;
        _initialized = item.Initialized; _account = item.AccountID;
        _name = item.CustomName; _override1 = item.CustomNameOverride;
        _override2 = item.CustomNameOverride2; _override3 = item.CustomNameOverride3;
        _paint = weapon.FallbackPaintKit; _seed = weapon.FallbackSeed; _wear = weapon.FallbackWear; _stattrak = weapon.FallbackStatTrak;
        _ownerLow = weapon.OriginalOwnerXuidLow; _ownerHigh = weapon.OriginalOwnerXuidHigh;
        _mesh = weapon.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.MeshGroupMask;
        _nextAttack = services.As<CCSPlayer_WeaponServices>().NextAttack;
        _attributes = Capture(item.AttributeList); _dynamic = Capture(item.NetworkedDynamicAttributes);
        var ammo = new List<(byte, ushort)>();
        foreach (var index in new[] { weapon.VData!.PrimaryAmmoType, weapon.VData.SecondaryAmmoType }.Distinct())
            if (index < services.Ammo.Length) ammo.Add((index, services.Ammo[index]));
        _ammo = ammo.ToArray();
        // Resolve/validate everything BEFORE retiring the original weapon.
        if (_attributes.Length + _dynamic.Length != 0)
            _writeAttribute ??= new(GameData.GetSignature("CW_SetOrAddAttributeValueByName"));
    }

    private static Attribute[] Capture(CAttributeList list)
    {
        if (list.Attributes.Count > 256) throw new InvalidOperationException("Attribute count exceeds snapshot bound");
        var result = EconAttributeVector.Read(list).Select(a => new Attribute(a.AttributeDefinitionIndex, a.Value, a.InitialValue,
            a.RefundableCurrency, a.SetBonus)).ToArray();
        if (result.Select(a => a.Index).Distinct().Count() != result.Length || result.Any(a => !AttributeNames.ContainsKey(a.Index)))
            throw new InvalidOperationException("Unknown or duplicate economy attribute");
        return result;
    }

    public void Restore(CCSWeaponBase weapon, CPlayer_WeaponServices services)
    {
        RestoreAppearance(weapon);
        weapon.Clip1 = _clip1; weapon.Clip2 = _clip2;
        for (var i = 0; i < Math.Min(_reserve.Length, weapon.ReserveAmmo.Length); i++) weapon.ReserveAmmo[i] = _reserve[i];
        foreach (var (index, count) in _ammo) if (index < services.Ammo.Length) services.Ammo[index] = count;
        weapon.BurstMode = _burst; weapon.SilencerOn = _silencer;
        weapon.AccuracyPenalty = _accuracy; weapon.IRecoilIndex = _recoil; weapon.FlRecoilIndex = _recoilFloat;
        if (_nextAttack.HasValue) { var cssServices = services.As<CCSPlayer_WeaponServices>(); cssServices.NextAttack = Math.Max(cssServices.NextAttack, _nextAttack.Value); }
        // Never shorten an old cooldown or the new weapon's normal deploy delay.
        if (_primaryTick > weapon.NextPrimaryAttackTick || _primaryTick == weapon.NextPrimaryAttackTick && _primaryRatio > weapon.NextPrimaryAttackTickRatio)
        { weapon.NextPrimaryAttackTick = _primaryTick; weapon.NextPrimaryAttackTickRatio = _primaryRatio; }
        if (_secondaryTick > weapon.NextSecondaryAttackTick || _secondaryTick == weapon.NextSecondaryAttackTick && _secondaryRatio > weapon.NextSecondaryAttackTickRatio)
        { weapon.NextSecondaryAttackTick = _secondaryTick; weapon.NextSecondaryAttackTickRatio = _secondaryRatio; }
    }

    public void RestoreMesh(CCSWeaponBase weapon)
    {
        if (_mesh.HasValue && weapon.CBodyComponent?.SceneNode?.GetSkeletonInstance() is {} skeleton)
            skeleton.ModelState.MeshGroupMask = _mesh.Value;
    }

    // A give hook can paint again on its next world update. Reapply appearance
    // once after those hooks, without rolling back ammo fired during that frame.
    public void RestoreAppearance(CCSWeaponBase weapon)
    {
        var item = weapon.AttributeManager.Item;
        if (item.ItemDefinitionIndex != _definition) weapon.AcceptInput("ChangeSubclass", value: _definition.ToString(System.Globalization.CultureInfo.InvariantCulture));
        item.ItemDefinitionIndex = _definition;
        // Keep the NEW item's unique ID. Reusing a retired ID can reuse client caches.
        item.EntityQuality = _quality; item.EntityLevel = _level; item.InventoryPosition = _inventory;
        item.AccountID = _account; item.Initialized = _initialized;
        item.CustomName = _name; item.CustomNameOverride = _override1;
        item.CustomNameOverride2 = _override2; item.CustomNameOverride3 = _override3;
        weapon.FallbackPaintKit = _paint; weapon.FallbackSeed = _seed; weapon.FallbackWear = _wear; weapon.FallbackStatTrak = _stattrak;
        weapon.OriginalOwnerXuidLow = _ownerLow; weapon.OriginalOwnerXuidHigh = _ownerHigh;
        Restore(item.AttributeList, _attributes); Restore(item.NetworkedDynamicAttributes, _dynamic);
        Utilities.SetStateChanged(weapon, nameof(CEconEntity), "m_AttributeManager");
    }

    private static void Restore(CAttributeList list, Attribute[] values)
    {
        list.Attributes.RemoveAll();
        foreach (var value in values)
            _writeAttribute!.Invoke(list.Handle, AttributeNames[value.Index], value.Value);
        var restored = EconAttributeVector.Read(list).ToDictionary(a => a.AttributeDefinitionIndex);
        if (restored.Count != values.Length) throw new InvalidOperationException("Attribute restore count mismatch");
        foreach (var value in values)
        {
            var added = restored[value.Index];
            added.InitialValue = value.Initial; added.RefundableCurrency = value.Currency; added.SetBonus = value.Bonus;
        }
    }
}

internal static class EconAttributeVector
{
    public static CEconItemAttribute[] Read(CAttributeList list)
    {
        var vector = list.Attributes;
        var count = vector.Count;
        if (count is < 0 or > 256) throw new InvalidOperationException("Attribute vector count outside bounds");
        if (count == 0) return [];
        var size = Schema.GetClassSize("CEconItemAttribute");
        if (size is <= 0 or > 4096) throw new InvalidOperationException("Attribute schema size outside bounds");
        // CSS 375's indexer supports only CHandle<T>. Its native element-zero
        // accessor returns the buffer address independently of element stride.
        // Obtain the actual stride from the current engine schema, not an offset.
        var buffer = NativeAPI.GetNetworkVectorElementAt(vector.Handle, 0);
        if (buffer == 0) throw new InvalidOperationException("Attribute buffer missing");
        var result = new CEconItemAttribute[count];
        for (var i = 0; i < count; i++) result[i] = new CEconItemAttribute(buffer + checked(i * size));
        return result;
    }
}
