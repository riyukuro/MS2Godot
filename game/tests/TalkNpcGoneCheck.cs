using System;
using Godot;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/talk_npc_gone.tscn) of the npc that ResetTalk plays the end sound on (UITalkManager::ResetTalk,
/// uitalkcinematic.cpp:1507-1528: FindNpcById(m_nNpcId), PlayEndSound only if it is found). A talk ends because its npc left
/// the field (EndTalkIfNpcGone, :1612): the npc's node is freed, so reading its position threw ObjectDisposedException.
/// - the npc is gone from the field (the lookup returns null): no npc, nothing to read;
/// - the npc was added again under the same object id: the new one;
/// - offline (no lookup): the talked-to npc;
/// - no talk npc: none.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class TalkNpcGoneCheck : Node
{
    public override void _Ready()
    {
        var root = new Node3D();
        AddChild(root);
        var old = new FieldNpcs.Placed { Root = root, ObjectId = 50000020 };
        root.Free();                               // the npc's node leaves with the npc
        bool readThrows = false;
        try { _ = old.Position; } catch (ObjectDisposedException) { readThrows = true; }

        var again = new FieldNpcs.Placed { Root = new Node3D(), ObjectId = 50000020 };
        int asked = 0;
        var gone = NpcTalk.ResetTalkNpc(old, id => { asked = id; return null; });
        var added = NpcTalk.ResetTalkNpc(old, id => again);
        var offline = NpcTalk.ResetTalkNpc(old, null);
        var none = NpcTalk.ResetTalkNpc(null, id => again);
        again.Root.Free();

        bool ok = readThrows && gone == null && asked == 50000020 && added == again && offline == old && none == null;
        GD.Print($"talk npc gone: {(ok ? "OK" : "FAIL")} (read throws {readThrows}, gone {gone == null}, asked {asked}, re-added {added == again}, offline {offline == old}, none {none == null})");
        GetTree().Quit(ok ? 0 : 1);
    }
}
