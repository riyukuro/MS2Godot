using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Look;

/// <summary>
/// The local player's body: the gender's body NIF and its idle / walk / run, dressed in the starter look (StarterLook)
/// through EquipApplier. Command line: --gender male|female (default female), --job &lt;job code&gt; (default 10, knight),
/// --skin-color, --hair-color, --eye-color &lt;palette row&gt; (defaults: row 0, the item model's default row).
/// </summary>
public static class PlayerLook
{
    /// <summary>The starter look the offline player wears (null before the body is built or with a live session), with its
    /// gender and job: the equipment window reads the same items, so the icons and the model agree.</summary>
    public static IReadOnlyList<EquipItem> OfflineWorn { get; private set; }
    public static int OfflineGender { get; private set; } = 1;
    public static int OfflineJob { get; private set; } = 10;

    public static (Node3D visual, AnimationPlayer anim, ModelData body, string bodyDir) Build(ModelBuilder builder, PackSet packs)
    {
        int gender = 1, job = 10, skinRow = 0;
        var rows = new Dictionary<string, int>();
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--gender") gender = args[i + 1].StartsWith("m", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            if (args[i] == "--job" && int.TryParse(args[i + 1], out int j)) job = j;
            if (args[i] == "--skin-color" && int.TryParse(args[i + 1], out int sk)) skinRow = sk;
            if (args[i] == "--hair-color" && int.TryParse(args[i + 1], out int h)) rows["HR"] = h;
            if (args[i] == "--eye-color" && int.TryParse(args[i + 1], out int e)) rows["FA"] = e;
        }
        Flow.Session.ApplyLook(ref gender, ref job);        // the game flow's chosen character
        string dir = gender == 0 ? "male" : "female";
        ModelData md; Node3D visual; AnimationPlayer anim;
        try
        {
            md = builder.LoadModel($"{dir}/{(gender == 0 ? "m_body.nif" : "f_body.nif")}");
            visual = builder.Build(md, gender == 0 ? "Male" : "Female");
            anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            var lib = anim.GetAnimationLibrary("");
            foreach (var (name, kf) in new[] { ("idle", "idle_a"), ("walk", "walk_a"), ("run", "run_a") })
                lib.AddAnimation(name, builder.KfAnimationFor($"{dir}/{kf}.kf", md));

            try
            {
                var palettes = new ColorPalettes(packs);
                var live = Live();
                var (items, skin) = live ?? StarterLook.Build(packs, palettes, gender, job, rows, skinRow);
                if (!LiveSession.Active)
                {
                    // the offline character's worn items, which the equipment window shows too (HudScene.OfflineMe)
                    for (int i = 0; i < items.Count; i++) if (items[i].Uid == 0) items[i].Uid = 2001 + i;
                    OfflineWorn = items; OfflineGender = gender; OfflineJob = job;
                }
                var equip = new EquipApplier(packs, builder) { Palettes = palettes };
                equip.Apply(visual, md, gender, items, skin?.Primary, skin?.Secondary);
                if (Array.IndexOf(args, "--battle") >= 0) EquipApplier.RigOf(visual)?.SetState(true, true);
                GD.Print($"player look: {dir}, job {job}{(LiveSession.Active ? " (live)" : "")}, items {string.Join(" ", items.Select(i => $"{i.Slot}:{i.Id}"))}");
            }
            catch (Exception ex) { GD.PushWarning("player look: " + ex.Message); }
        }
        finally { builder.Trim(); }   // a failed load still leaves the builder holding one look
        return (visual, anim, md, dir);
    }

    /// <summary>
    /// The live character's look: the own FieldAddUser's equips and skin colour (CSceneStateGame::OnAddUser,
    /// scenestategame.cpp:11135, the user block and the equip list it reads into the PC) once it is in, before it the chosen
    /// character list entry's (CCharacterInfo::Read, the same user block and equip list). Null offline: the starter look.
    /// </summary>
    static (IReadOnlyList<EquipItem> items, SkinColor? skin)? Live()
    {
        if (!LiveSession.Active)    // offline: the chosen character's own items (stub list or a lobby-created one), else the starter look
            return Flow.Session.Chosen is { User: { } o } own && own.Equips.Count > 0 ? (own.Equips, o.Skin) : null;
        if (LiveSession.Instance.Me is { User: { } u, Equips: { } worn }) return (worn, u.Skin);
        if (Flow.Session.Chosen is { User: { } c } chosen) return (chosen.Equips, c.Skin);
        return null;
    }
}
