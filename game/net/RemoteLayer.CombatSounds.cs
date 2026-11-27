using Godot;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

// The sounds of a damage record on one target besides the npc's own hit sound (NpcLayer.Hit): a PC's hit sound and death
// voice (PC::vfunc_33 / vfunc_34 / vfunc_35, pc.cpp:884-1085) and a dot's hit sound on an npc (CNpc::vfunc_34); the choices
// are Ms2.Ui.Skills.CombatVoice's.
public partial class RemoteLayer
{
    /// <summary>
    /// A record's sounds on <paramref name="t"/>, after its hit on the target:
    /// - on the own PC or another PC: the hit sound (<see cref="CombatVoice.PcHitSound"/>: the record's attack material, or a
    ///   dot's buff level's soundMaterial, on defence material 1, the owner's object kind, the PC's place), then, when the PC
    ///   is dead (IsDead), its death voice (PC::FUN_141653740, <see cref="CombatVoice.PcDeathVoice"/>);
    /// - on an npc, a dot's hit sound (<see cref="CombatVoice.NpcDotSound"/>, on the npc's defenceMaterial).
    /// A dot's buff is the target's list entry of the packet's buff id (CDotDamageRecord m_nEffectLevel, FUN_14096fc80).
    /// The own PC's is looked up in packet order (<see cref="QueuedBuffRemovals"/>): a remove read after the record does not hide it.
    /// STILL-OPEN: a server that writes the dot's tick count there, not the buff's key, finds another entry or none.
    /// STILL-OPEN: a cast's or splash's hit sound needs its total below 0 (the client's sign for health taken, PC::vfunc_33
    /// pc.cpp:923 GetTotalDamage() &lt; 0, CDamageRecord::AddDamage skillrecord.cpp:554 storing the raw amounts); the server
    /// writes those amounts positive (a dot's negative), so on it only dots play a PC's hit sound.
    /// STILL-OPEN: the dead flag is the one the dead user / user state packets set, and the server sends the killing record
    /// before its DeadUser, so the killing record finds the PC alive and plays no death voice.
    /// </summary>
    void PlayRecordSounds(Ms2.Net.Packets.SkillDamage d, Ms2.Net.Packets.HitTarget t, HitOwner owner, bool byLocal, bool ownerBoss)
    {
        var audio = FieldAudio.Instance;
        if (audio == null || t.TargetId == 0) return;
        bool self = t.TargetId == Tracker.SelfObjectId;
        Remote target = null;
        if (!self && !users.TryGetValue(t.TargetId, out target) && !npcs.Has(t.TargetId)) return;
        int ownerKind = CombatVoice.ObjectKind(owner, byLocal, ownerBoss);
        var constants = World.FieldContents.Constants(packs);
        var dotBuffs = d.Mode == 3 ? BuffsOf(t.TargetId) : null;
        var dotBuff = d.Mode != 3 ? null : self ? OwnEntry(d.BuffId) : dotBuffs?.Find(d.BuffId);
        int dotMaterial = dotBuff?.LevelData?.DotSoundMaterial ?? 0;
        if (d.Mode == 3 && dotMaterial == 0 && LiveSession.LiveCheck)
            GD.Print($"audio: dot {t.TargetId} buff key {d.BuffId}: list {(dotBuffs == null ? "none" : string.Join(" ", System.Linq.Enumerable.Select(dotBuffs.Buffs, b => $"{b.Key}:{b.Id}")))}, entry {(dotBuff == null ? "none" : $"{dotBuff.Id} level {dotBuff.Level} data {dotBuff.LevelData != null}")}");
        if (!self && target == null)
        {
            if (CombatVoice.NpcDotSound(ownerKind, d.Mode == 3 ? dotMaterial : 0, constants) is { } dot
                && npcs.RootOf(t.TargetId) is { } root && npcs.RowOf(t.TargetId) is { } row)
            {
                bool played = audio.PlayHitSound(dot.Attack, row.DefenceMaterial, ownerKind, root.GlobalPosition, dot.Volume, ViewerPosition, HitSoundRand);
                if (LiveSession.LiveCheck) GD.Print($"audio: npc dot sound {t.TargetId} buff {d.BuffId} material {dot.Attack}/{row.DefenceMaterial} owner kind {ownerKind} volume {dot.Volume:F2} played {played}");
            }
            return;
        }
        var live = LiveSession.Instance;
        Node3D body = self ? live?.OwnPlayer : target.Visual;
        if (body == null || !IsInstanceValid(body)) return;
        int targetKind = self ? 1 : 3;
        int attack = d.Mode == 3 ? 0 : NpcHitSound.AttackMaterial(owner, NpcHitSound.RecordAttack(d, RegionSkillOf, attackOf));
        long total = Ms2.Ui.Skills.NpcVibrate.TotalDamage(t.Hits);
        if (CombatVoice.PcHitSound(d.Mode, total, targetKind, attack, dotMaterial, constants) is not { } hit)
        {
            if (LiveSession.LiveCheck) GD.Print($"audio: pc hit sound {t.TargetId} mode {d.Mode} none: material {attack} total {total} skill {d.SkillId}");
        }
        else
        {
            bool played = audio.PlayHitSound(hit.Attack, CombatVoice.PcDefenceMaterial, ownerKind, body.GlobalPosition, hit.Volume, ViewerPosition, HitSoundRand);
            if (LiveSession.LiveCheck) GD.Print($"audio: pc hit sound {t.TargetId} mode {d.Mode} material {hit.Attack} owner kind {ownerKind} target kind {targetKind} volume {hit.Volume:F2} played {played}");
        }
        bool dead = self ? live?.Me.Dead ?? false : target.Pc?.States.Dead ?? false;
        if (!dead) return;
        var (type, actor) = CombatVoice.PcDeathVoice(ownerKind, targetKind);
        if (LiveSession.LiveCheck) GD.Print($"audio: pc death voice {t.TargetId} owner kind {ownerKind} target kind {targetKind}: type {type} for {actor}");
        if (actor == VoiceActor.Target && self)
            audio.PlayEventVoice(type, live.Me.User.Gender, live.Me.User.JobCode, true, Ms2.Audio.SoundMusicEffectState.CasterMyPc, body.GlobalPosition, ViewerPosition);
        else if (actor == VoiceActor.Owner && byLocal && live?.OwnPlayer is { } me && IsInstanceValid(me))
            audio.PlayEventVoice(type, live.Me.User.Gender, live.Me.User.JobCode, true, Ms2.Audio.SoundMusicEffectState.CasterMyPc, me.GlobalPosition, ViewerPosition);
        else if (actor == VoiceActor.Owner && users.TryGetValue(d.OwnerId, out var o) && o.Visual != null && IsInstanceValid(o.Visual))
            audio.PlayEventVoice(type, o.Gender, o.Pc.JobGroup, false, Ms2.Audio.SoundMusicEffectState.CasterOtherPc, o.Visual.GlobalPosition, ViewerPosition);
    }
}
