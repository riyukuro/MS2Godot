using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// WorldService m_848 (worldservice.h:682): the trigger's effect nifs of the field, by game object id. The Trigger types 0x10,
/// 0x11 and 0x12 fill and empty it, and each entry is hung on its object's "Eff_UI" node (PCBase::FUN_141678ad0, pcbase.cpp:4278;
/// <see cref="Attach"/>) when that object is in the field and loaded (WorldService::FUN_141bd1200 and GameObject::FUN_14123dbd0).
/// An object added later gets its entry then (WorldService::OnActorAdded @0x141bbfb50, worldservice_1.cpp:3598). The field's
/// load (WorldService::FUN_141bcda90, :6950) empties it.
/// </summary>
public sealed class FieldEffectNifs
{
    readonly Dictionary<int, TriggerEffectNif> byKey = new();

    /// <summary>WorldService::FUN_141bd1200 and GameObject::FUN_14123dbd0: the object of that id is in the field and loaded.</summary>
    public Func<int, bool> Loaded = _ => false;
    /// <summary>PCBase::FUN_141678ad0(name, outline, scale, angle) on the object of the effect's id. The actor keeps one effect
    /// (m_spC40) and an attach while it has one does nothing; the actor side checks that.</summary>
    public Action<int, TriggerEffectNif> Attach = (_, _) => { };
    /// <summary>PCBase::DetachEffectUiNode (@0x141678f50) on the object of that id.</summary>
    public Action<int> Detach = _ => { };

    public int Count => byKey.Count;

    /// <summary>The CAtlMap Lookup (FUN_141c06b10) of a key; null when the table has none.</summary>
    public TriggerEffectNif Find(int key) => byKey.GetValueOrDefault(key);

    /// <summary>
    /// Type 0x10 (FUN_141bd42f0 @0x141bd42f0, worldservice_1.cpp:7302): each entry SetAt by its key, the old entries kept;
    /// then every entry of the table is attached to the loaded object its own id names, with no detach first.
    /// </summary>
    public void Load(IEnumerable<(int Key, TriggerEffectNif Effect)> entries)
    {
        foreach (var (key, effect) in entries) byKey[key] = effect;
        foreach (var effect in byKey.Values)
            if (Loaded(effect.ObjectId)) Attach(effect.ObjectId, effect);
    }

    /// <summary>
    /// Type 0x11 (AddEffect @0x141bd4490, worldservice_1.cpp:7319): SetAt by the effect's id, then on the loaded object of that
    /// id the old effect detached and this one attached.
    /// </summary>
    public void Add(TriggerEffectNif effect)
    {
        byKey[effect.ObjectId] = effect;
        if (!Loaded(effect.ObjectId)) return;
        Detach(effect.ObjectId);
        Attach(effect.ObjectId, effect);
    }

    /// <summary>Type 0x12 (RemoveEffect @0x141bd4670, worldservice_1.cpp:7337): RemoveKey, then the loaded object's effect detached.</summary>
    public void Remove(int objectId)
    {
        byKey.Remove(objectId);
        if (Loaded(objectId)) Detach(objectId);
    }

    /// <summary>OnActorAdded (@0x141bbfb50, :3598-3613): an added object with an entry of its id gets it attached.</summary>
    public void ActorAdded(int objectId)
    {
        if (byKey.TryGetValue(objectId, out var effect)) Attach(objectId, effect);
    }

    /// <summary>
    /// The Trigger packet's types 0x10, 0x11 and 0x12 (CTriggerService::FUN_141861840, triggerservice.cpp:398-406, after
    /// <see cref="TriggerObjects.ClientType"/>), applied; false for a body of another type, which is left to the other readers.
    /// </summary>
    public bool ApplyTrigger(byte[] body, ProtocolProfile profile)
    {
        var r = new ByteReader(body);
        if (r.Available < 1) return false;
        switch (TriggerObjects.ClientType(r.ReadByte(), profile))
        {
            case TriggerObjects.LoadEffects: Load(TriggerObjects.ReadLoadEffects(r)); return true;
            case TriggerObjects.AddEffect: Add(TriggerEffectNif.Read(r)); return true;
            case TriggerObjects.RemoveEffect: Remove(TriggerObjects.ReadRemoveEffect(r)); return true;
            default: return false;
        }
    }

    /// <summary>The field's load (FUN_141bcda90, :6950): RemoveAll.</summary>
    public void Clear() => byKey.Clear();
}
