using System.Collections;
using System.Linq;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Ui.Skills;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/buff_keep.tscn, game data) of the buffs' keep effects on a far owner, through the field's real
/// effect service (RemoteLayer's buff service on RegionSkills): AdditionalCli::PlayKeepEffect sets the target's m_b30
/// (additionalcli_1.cpp:347), and EffectSkipMgr::IsOutOfView never culls a nif part of such a target (effectskipmgr.cpp:138);
/// its sound parts are still tested (CreateEffect, effectservice.cpp:322-330).
/// - the data's nif row distance for another player is set, so a far camera actor culls a plain kept spawn of the stun keep;
/// - another player's stun (10500051) on the far owner keeps its keep effect at once; its frame update allocates 0 B;
/// - Iron Shield's keep (10100151), a sound part only, does not spawn that far, as in the client, and is tried each frame;
///   those tries, a refused plain spawn and a keep whose xml is missing allocate 0 B.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class BuffKeepCheck : Node3D
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const uint Stun = 10500051, IronShield = 10100151;
    const int Other = Ms2.Audio.SoundMusicEffectState.CasterOtherPc;
    readonly System.Collections.Generic.List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static int KeepOf(AdditionalBuffList list, int key)
    {
        if (list != null) foreach (var b in list.Buffs) if (b.Key == key) return b.KeepEffect;
        return 0;
    }

    static BuffPacket Add(int key, uint id) => new(BuffPacket.Add, 5, key, 6, 0, new BuffState(0, uint.MaxValue, id, 1, 1, 1), 0);

    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var field = new FieldStub(packs);
        AddChild(field);
        var fx = new RegionSkills(packs, builder);
        field.AddChild(fx);
        var layer = new RemoteLayer(field, builder, 1);
        var kept = (IDictionary)typeof(RegionSkills).GetField("kept", Private).GetValue(fx);
        var tables = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage);
        var effects = new Ms2.Formats.World.EffectTable(packs);
        string stunXml = tables.Additional.GetLevel(Stun, 1)?.KeepEffect ?? "";
        string shieldXml = tables.Additional.GetLevel(IronShield, 1)?.KeepEffect ?? "";
        bool HasNif(string xml) { var ps = effects.Get(xml); if (ps != null) foreach (var p in ps) if (p.TopLevel && p.Type == "nif") return true; return false; }
        bool SoundOnly(string xml) { var ps = effects.Get(xml); if (ps == null || ps.Count == 0) return false; foreach (var p in ps) if (p.TopLevel && p.Type != "soundEvent") return false; return true; }
        Check(HasNif(stunXml), $"the stun's keep effect '{stunXml}' has no nif part");
        Check(SoundOnly(shieldXml), $"Iron Shield's keep effect '{shieldXml}' is not a sound part only");

        FieldAddUser User(int id) => new() { ObjectId = id, User = new UserBlock { Name = "r" + id, CharacterId = 100 + id, Gender = 1 } };
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(5), false));
        layer.Tracker.Events.Enqueue(new UserAddedEvent(0, User(6), false));
        layer._Process(0);
        var owner = FindVisual(layer, 5);
        Check(owner != null, "no body for the owner");
        Check(layer.ObjectKind(6) == Other, $"the caster's kind {layer.ObjectKind(6)}, want another player's");

        // the camera's actor far from the owner, past the nif and sound rows' distances for another player
        var skip = FieldAudio.EffectSkip(packs);
        float view = skip?.ViewDistance(Ms2.Formats.World.EffectSkipTable.RowNif, Other) ?? 0;
        float hear = skip?.ViewDistance(Ms2.Formats.World.EffectSkipTable.RowSound, Other) ?? 0;
        Check(view > 0 && hear > 0, $"the nif / sound rows' distances for another player are {view} / {hear}: nothing is culled, the check proves nothing");
        var at = owner != null ? EffectView.Client(owner.GlobalPosition) : default;
        float away = System.Math.Max(view, hear) * 4 + 10000;
        var far = at + new System.Numerics.Vector3(away, 0, 0);
        fx.ViewerOf = () => far;

        // control: a plain kept spawn of the stun keep (no m_b30) is culled there
        int refusedBefore = fx.PartsOutOfView;
        int plain = owner != null ? fx.SpawnOn(stunXml, owner, Other) : 0;
        Check(plain == 0 && fx.PartsOutOfView > refusedBefore, $"the plain spawn far away was not culled (handle {plain})");
        long refusedBytes = owner != null ? CheckAlloc.Least(() => { for (int i = 0; i < 100; i++) fx.SpawnOn(stunXml, owner, Other); }) : -1;
        Check(refusedBytes == 0, $"100 refused spawns allocated {refusedBytes} B");

        // the stun on the far owner by the other player keeps its effect at once
        var list = layer.BuffsOf(5);
        Check(list != null && list.Effects != null, "the owner has no buff list with the effect service");
        list?.Apply(Add(1, Stun));
        layer._Process(0);
        int h = KeepOf(list, 1);
        Check(h != 0 && kept.Contains(h), $"the far owner's stun keep did not spawn (handle {h})");
        long keptBytes = list != null ? CheckAlloc.Least(() => { for (int i = 0; i < 1000; i++) list.UpdateEffects(); }) : -1;
        Check(keptBytes == 0, $"1000 updates of the kept list allocated {keptBytes} B");
        Check(KeepOf(list, 1) == h, $"the updates replaced the keep effect {h} with {KeepOf(list, 1)}");

        // Iron Shield's sound only keep: out of hearing, nothing is made (InsertEffect's nCount 0), tried again each frame
        list?.Apply(Add(2, IronShield));
        layer._Process(0);
        Check(KeepOf(list, 2) == 0, $"Iron Shield's sound only keep spawned out of hearing ({KeepOf(list, 2)})");
        long retryBytes = list != null ? CheckAlloc.Least(() => { for (int i = 0; i < 1000; i++) list.UpdateEffects(); }) : -1;
        Check(retryBytes == 0, $"1000 frames retrying Iron Shield's keep allocated {retryBytes} B");
        fx.ViewerOf = () => at;
        layer._Process(0);
        int near = KeepOf(list, 2);
        Check(near != 0 && kept.Contains(near), $"Iron Shield's keep did not spawn once the camera's actor is at the owner ({near})");
        fx.ViewerOf = () => far;

        list?.Remove(1, 6);
        list?.Remove(2, 6);
        Check(!kept.Contains(h) && !kept.Contains(near), "the removed buffs left their keep effects");

        // a keep whose xml is missing: tried each frame through the real service, 0 B
        var missing = new AdditionalLevelData { Level = 1, KeepEffect = "Additional/Missing/Eff_No_Such_Keep.xml" };
        var retry = new AdditionalBuffList((id, lv) => id == 1 ? missing : null) { Effects = layer.BuffEffects };
        retry.Apply(new BuffPacket(BuffPacket.Add, 5, 3, 6, 0, new BuffState(0, uint.MaxValue, 1, 1, 1, 1), 0));
        retry.UpdateEffects();
        long missingBytes = CheckAlloc.Least(() => { for (int i = 0; i < 1000; i++) retry.UpdateEffects(); });
        Check(KeepOf(retry, 3) == 0 && missingBytes == 0, $"1000 retries of a missing keep xml allocated {missingBytes} B (keep {KeepOf(retry, 3)})");

        GD.Print($"buff keep: nif row distances by caster kind 1..6: {string.Join(" ", System.Linq.Enumerable.Range(1, 6).Select(k => skip?.ViewDistance(Ms2.Formats.World.EffectSkipTable.RowNif, k) ?? 0))}");
        GD.Print($"buff keep: nif row {view:F0}, sound row {hear:F0}, viewer {away:F0} away; plain spawn {plain}, stun keep {h}, "
            + $"iron shield keep far 0 near {near}; bytes refused {refusedBytes} kept {keptBytes} retry {retryBytes} missing {missingBytes}");
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "buff keep: ok" : $"buff keep: {fails.Count} FAILED");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>A remote's body: the private Remote record's Visual.</summary>
    static Node3D FindVisual(RemoteLayer layer, int objectId)
    {
        var users = (IDictionary)typeof(RemoteLayer).GetField("users", Private).GetValue(layer);
        return users.Contains(objectId) && users[objectId] is { } r ? (Node3D)r.GetType().GetField("Visual").GetValue(r) : null;
    }
}
