using System;
using Godot;
using Ms2.Formats.World;

namespace Ms2Godot.Net;

/// <summary>The other players' PvP info (CPvPInfo) and their relation to the local player (PC::vfunc_122).</summary>
public sealed partial class RemoteLayer
{
    /// <summary>Pvp 'd' on another player (CPvPInfo::ReadPointTeamMode, scenestategame.cpp:4997); false when no PC of the
    /// field has the id (FindPCById's null: the packet does nothing).</summary>
    public bool SetPvpInfo(int objectId, Ms2.Ui.Skills.PvpInfo pvp)
    {
        CatchUp(objectId, npc: false);
        if (!users.TryGetValue(objectId, out var r)) return false;
        r.Pvp = pvp;
        return true;
    }

    /// <summary>The PvP info of another player (the live run's view); null when there is no such player.</summary>
    public Ms2.Ui.Skills.PvpInfo? PvpOf(int objectId) => users.TryGetValue(objectId, out var r) ? r.Pvp : null;

    /// <summary>Another player of the field by name (the live run's view): its object id, root, the own relation and its
    /// name tag colour now; null when there is none.</summary>
    public (int ObjectId, Node3D Root, bool Hostile, Color? TagColor, long Hp)? PcByName(string name)
    {
        foreach (var r in users.Values)
            if (r.Name == name && r.Root != null && IsInstanceValid(r.Root))
                return (r.ObjectId, r.Root, PcHostileToOwn(r.ObjectId), tags?.ColorOf(r.Root), r.HpCur);
        return null;
    }

    /// <summary>NameTagColorPCHostile as the tags read it, for checks.</summary>
    public Color? HostileTagColor => tags?.HostileColor;

    /// <summary>
    /// The local player's PC::vfunc_122 on another player (pc.cpp:3112, <see cref="Ms2.Ui.Skills.SkillHit.PcHostile"/> with the
    /// field's pkMode), the name tag's NameTagColorPCHostile test (PC::vfunc_53, pc.cpp:4459); false for an id that is no
    /// other player here or without the own relation (<see cref="LiveSession.OwnPcRelation"/>).
    /// </summary>
    bool PcHostileToOwn(int objectId)
    {
        var ownOf = LiveSession.Instance?.OwnPcRelation;
        if (ownOf == null || !users.TryGetValue(objectId, out var r) || r.Pc == null) return false;
        var own = ownOf();
        var other = PcCandidate(objectId, r, LiveSession.Instance?.Party);
        return Ms2.Ui.Skills.SkillHit.PcHostile(own, other, FieldPkMode() != 0);
    }

    /// <summary>The current field's CFieldData::GetPkMode, read once per field.</summary>
    int FieldPkMode()
    {
        int? fieldId = LiveSession.Instance?.FieldId;
        if (fieldId != tombPkField)
        {
            tombPkField = fieldId;
            tombPkMode = 0;
            try { if (fieldId is int fid) tombPkMode = FieldData.FindById(packs, fid)?.PkMode ?? 0; }
            catch (Exception ex) { GD.PushWarning($"field pk mode: {ex.Message}"); }
        }
        return tombPkMode;
    }
}
