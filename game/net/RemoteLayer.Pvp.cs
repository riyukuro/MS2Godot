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

    /// <summary>WorldService::FUN_141bd1530 (worldservice_1.cpp:6211) over the other players, then vfunc_77: the name of the
    /// field's player with this character id; null when none is here.</summary>
    public string NameOfCharacter(long characterId)
    {
        foreach (var r in users.Values)
            if (r.CharacterId == characterId) return r.Name ?? "";
        return null;
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

    /// <summary>Another player's head bar now (the live run's view).</summary>
    public string PcBarLine(int objectId) =>
        !users.TryGetValue(objectId, out var r) ? "no pc" : r.Bar is not { } b ? $"no bar (can {CanPcBar(r)})" :
        $"active {b.Active}, shown {b.Shown}, drawn {b.Drawn}, rank {b.Rank}, friendly {b.Friendly}, monster option {bars.MonsterGauge}";

    /// <summary>NameTagColorPCHostile as the tags read it, for checks.</summary>
    public Color? HostileTagColor => tags?.HostileColor;

    /// <summary>
    /// The local player's PC::vfunc_122 on another player (pc.cpp:3112, <see cref="Ms2.Ui.Skills.SkillHit.PcHostile"/> with the
    /// field's pkMode), the name tag's NameTagColorPCHostile test (PC::vfunc_53, pc.cpp:4459); false for an id that is no
    /// other player here or without the own relation (<see cref="LiveSession.OwnPcRelation"/>).
    /// </summary>
    bool PcHostileToOwn(int objectId) => users.TryGetValue(objectId, out var r) && PcHostileToOwn(r);

    /// <summary>The same for a player not yet in the table (its spawn, PC::vfunc_10).</summary>
    bool PcHostileToOwn(Remote r)
    {
        var live = LiveSession.Instance;
        var ownOf = live?.OwnPcRelation;
        if (ownOf == null || r.Pc == null) return false;
        int objectId = r.ObjectId;
        bool pk = FieldPkMode() != 0;
        // off a pk field a PC without a point is never hostile (PvPPoint differs, or both teams are 1): the name tags ask this
        // per player per frame, so skip building the two candidates on the fields where it cannot be true
        if (!pk && !live.OwnPvp.HasPoint) return false;
        var own = ownOf();
        var other = PcCandidate(objectId, r, live.Party);
        return Ms2.Ui.Skills.SkillHit.PcHostile(own, other, pk);
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
