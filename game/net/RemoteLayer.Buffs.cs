using System.Collections.Generic;
using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

// The buffs' motion effects (AdditionalCli's start, keep, stop, invoke and overlap effects, additionalcli_1.cpp) on the own PC,
// the other players and the npcs, through the field's region effects.
public partial class RemoteLayer
{
    /// <summary>The npcs' AdditionalCliLists by object id: FieldAddNpc's list (DecodeListPacket, scenestategame.cpp:3559), then
    /// the npc's OnBuff packets.</summary>
    readonly Dictionary<int, AdditionalBuffList> npcBuffs = new();
    BuffEffectService buffEffects;

    /// <summary>The effect service the buff lists play through.</summary>
    internal IBuffMotionEffects BuffEffects => buffEffects ??= new BuffEffectService(this);

    /// <summary>The kept and inserted buff effects (xml, owner object id, kept id or -1 for an inserted one), for the checks.</summary>
    internal static event System.Action<string, int, int> BuffEffectPlayed;

    /// <summary>The owner a buff effect hangs on (SetActor of the owner's object id): the own player's body, another player's
    /// visual or an npc's root; null when none is here.</summary>
    Node3D BuffActor(int objectId)
    {
        if (objectId == 0) return null;
        if (objectId == Tracker.SelfObjectId)
            return LiveSession.Instance?.OwnPlayer?.Visual is { } v && IsInstanceValid(v) ? v : null;
        if (users.TryGetValue(objectId, out var r)) return r.Visual != null && IsInstanceValid(r.Visual) ? r.Visual : null;
        return npcs.RootOf(objectId);
    }

    /// <summary>The AdditionalCliList of an object id: the own player's (the HUD's), another player's or an npc's; null for none.</summary>
    public AdditionalBuffList BuffsOf(int objectId)
    {
        if (objectId != 0 && objectId == Tracker.SelfObjectId) return LiveSession.Instance?.OwnBuffList;
        if (users.TryGetValue(objectId, out var r)) return r.Buffs;
        return npcBuffs.GetValueOrDefault(objectId);
    }

    /// <summary>OnFieldAddNpc's DecodeListPacket: a new list for the npc, its entries marked as the entry list's (m_b40), so they
    /// play no start effect. A list the id had is cleared first (the old npc's ~AdditionalCliList ClearAll).</summary>
    void AddNpcBuffs(Ms2.Net.Packets.FieldAddNpc n)
    {
        DropNpcBuffs(n.ObjectId);
        if (!npcs.Has(n.ObjectId)) return;
        levelOf ??= Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).Additional.GetLevel;
        var list = new AdditionalBuffList(levelOf) { Effects = BuffEffects, ServerTick = BuffTick };
        foreach (var b in n.Buffs) list.Apply(Ms2.Net.Packets.BuffPacket.FromAdd(b) with { FromList = true });
        npcBuffs[n.ObjectId] = list;
    }

    /// <summary>An npc's list gone with the npc: FieldRemoveNpc's object release (PCBase::vfunc_15, pcbase.cpp:873, the list's
    /// ClearAll: the keep effects removed). FieldDeadNpc does not drop it: CNpc::vfunc_125 (the dead state, the corpse box, the
    /// dead effect) leaves the AdditionalCliList, so a corpse keeps its keep effects until the server removes the buffs or the
    /// body (scenestategame.cpp:3604-3612).</summary>
    void DropNpcBuffs(int objectId)
    {
        if (npcBuffs.Remove(objectId, out var old)) old.ClearAll();
    }

    static uint BuffTick() => unchecked((uint)(LiveSession.Instance?.ServerTick ?? 0));

    /// <summary>AdditionalCliList::UpdateEffects for the other players' and the npcs' lists, once per frame (the own one runs in
    /// the HUD with its list).</summary>
    void TickBuffEffects()
    {
        foreach (var a in users.Values) a.Buffs?.UpdateEffects();
        if (npcBuffs.Count == 0) return;
        foreach (var l in npcBuffs.Values) l.UpdateEffects();
    }

    /// <summary>
    /// The buffs' invoke effects of a damage record's target: a cast's hit (mode 1, PC::vfunc_33 pc.cpp:891 and CNpc::vfunc_33)
    /// and a PC's splash hit (modes 5 and 6, PC::vfunc_35 pc.cpp:1016; CNpc::vfunc_35 plays none) play event 1 when the record's
    /// attack has invokeEffectDisable 0; a dot (mode 3, PC::vfunc_34 pc.cpp:990 and CNpc::vfunc_34) plays event 0.
    /// </summary>
    void PlayHitInvokeEffects(int targetId, int mode, SkillAttackDamage attack)
    {
        var list = BuffsOf(targetId);
        if (list == null) return;
        if (mode == 3) { list.PlayInvokeEffectsByEvent(0); return; }
        bool npc = !users.ContainsKey(targetId) && targetId != Tracker.SelfObjectId;
        if (mode != 1 && (npc || (mode != 5 && mode != 6))) return;
        if (attack != null && attack.InvokeEffectDisable == 0) list.PlayInvokeEffectsByEvent(1);
    }

    /// <summary>The effects of the buff lists (<see cref="IBuffMotionEffects"/>): FUN_1411a49c0 / InsertEffect on the owner's node
    /// with the caster's object kind (WorldService::FUN_141bd19d0: 0 for no object), FUN_1411a6060 to stop. A keep sets the
    /// target's m_b30 (additionalcli_1.cpp:347, :403), so its nif parts are never out of view (effectskipmgr.cpp:138) and a
    /// far owner's keep spawns at once; the start, stop, invoke and overlap start / end inserts do not set it.</summary>
    sealed class BuffEffectService : IBuffMotionEffects
    {
        readonly RemoteLayer layer;
        public BuffEffectService(RemoteLayer layer) => this.layer = layer;

        // the live session's GetObjectKind (the own PC 1); without one (a check) the field's: another player 3, an npc 5 or 6
        int KindOf(int casterId) => LiveSession.Instance is { } s ? s.ObjectKind(casterId) : layer.ObjectKind(casterId);

        public int Keep(string effect, int ownerId, int casterId)
        {
            var actor = layer.BuffActor(ownerId);
            if (actor == null) return 0;
            int h = layer.SpawnKeptEffect(effect, actor, KindOf(casterId), noCull: true);
            if (h != 0) Trace(effect, ownerId, h);
            return h;
        }

        public void Insert(string effect, int ownerId, int casterId)
        {
            var actor = layer.BuffActor(ownerId);
            if (actor == null) return;
            int inserted = layer.SpawnKeptEffect(effect, actor, KindOf(casterId));
            if (inserted != 0) Trace(effect, ownerId, -1);
            layer.ReleaseKeptEffect(inserted);
        }

        public void Stop(int id)
        {
            if (FxTrace) GD.Print($"fx: buff stop {id} at {Time.GetTicksMsec()}");
            layer.StopKeptEffect(id);
        }

        public bool OwnerShown(int ownerId) => layer.BuffActor(ownerId) != null;

        void Trace(string effect, int ownerId, int h)
        {
            BuffEffectPlayed?.Invoke(effect, ownerId, h);
            if (!FxTrace) return;
            var actor = layer.BuffActor(ownerId);
            float away = actor != null && EffectView.Viewer is { } eye ? System.Numerics.Vector3.Distance(eye, EffectView.Client(actor.GlobalPosition)) : -1;
            GD.Print($"fx: buff {(h < 0 ? "insert" : "keep")} {effect} on {ownerId}{(h < 0 ? "" : $" -> {h}")}, viewer {away:F0} away, at {Time.GetTicksMsec()}");
        }
    }
}
