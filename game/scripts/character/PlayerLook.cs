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

    /// <summary>
    /// The model in the live (or chosen, or starter) look. <paramref name="dress"/> changes the items the model is dressed in: the gacha
    /// shop's avatar puts its preview items over the player's own (null: the look as it is).
    /// </summary>
    public static (Node3D visual, AnimationPlayer anim, ModelData body, string bodyDir) Build(ModelBuilder builder, PackSet packs,
        Func<IReadOnlyList<EquipItem>, IReadOnlyList<EquipItem>> dress = null)
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
            EffectAttach.SetDummies(visual, Ms2.Formats.World.EffectDummyTable.Load(packs).Pc(gender), md.RootName);
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
                if (dress != null) items = dress(items);
                var equip = new EquipApplier(packs, builder) { Palettes = palettes };
                equip.Apply(visual, md, gender, items, skin?.Primary, skin?.Secondary);
                visual.SetMeta(FaceTexture.FaceItemMeta, equip.FaceItem(visual));
                dressers.AddOrUpdate(visual, new Dresser(equip, builder, packs));
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
        LiveLookOf = 0;
        if (!LiveSession.Active)    // offline: the chosen character's own items (stub list or a lobby-created one), else the starter look
            return Flow.Session.Chosen is { User: { } o } own && own.Equips.Count > 0 ? (own.Equips, o.Skin) : null;
        if (LiveSession.Instance.Me is { User: { } u, Equips: { } } && LiveSession.Instance.OwnLook() is { } worn)
        { LiveLookOf = u.CharacterId; return (worn, u.Skin); }
        if (Flow.Session.Chosen is { User: { } c } chosen) { LiveLookOf = c.CharacterId; return (chosen.Equips, c.Skin); }
        return null;
    }

    sealed record Dresser(EquipApplier Equip, ModelBuilder Builder, PackSet Packs);
    // the applier each body was dressed by, which keeps the worn parts of that body for a later put-on or put-off
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node3D, Dresser> dressers = new();

    /// <summary>
    /// The own player's look after a put-on, put-off, worn item update or outfit layer change: PC::vfunc_127 / vfunc_128
    /// (@0x1416563c0 / @0x1416564a0, pc.cpp:1400-1426; MyPC's overrides call them first, mypc.cpp:4892-4915) equip or take
    /// off the item in the PC's CEquipment and then run FUN_141656710 (pc.cpp:1454): UpdateWeaponAction on the new hands,
    /// the alpha blending and the model refresh. Here the body <see cref="Build"/> dressed is rebuilt in the changed slots
    /// (EquipApplier.Reapply, as RemoteLayer does for the other players; <paramref name="force"/> also rebuilds that slot),
    /// the player takes the rebuilt weapon rig, and a changed face item gives the face its new emotion group (the face item
    /// test of PC::vfunc_69, pc.cpp:3096-3101). False when this body was not built here.
    /// </summary>
    public static bool Redress(Player player, IEnumerable<EquipItem> items, EquipSlot? force = null)
    {
        var visual = player?.Visual;
        if (visual == null || !GodotObject.IsInstanceValid(visual) || items == null || !dressers.TryGetValue(visual, out var d)) return false;
        int faceBefore = d.Equip.FaceItem(visual);
        if (!d.Equip.Reapply(visual, items, force)) return false;
        player.SetWeapons(EquipApplier.RigOf(visual));
        int face = d.Equip.FaceItem(visual);
        if (face != faceBefore)
        {
            visual.SetMeta(FaceTexture.FaceItemMeta, face);
            player.SetFace(FaceTexture.For(visual, d.Builder, d.Packs, player.Gender));
        }
        return true;
    }

    /// <summary>The character whose items and skin the last live look was built with (0 offline or with none).</summary>
    public static long LiveLookOf { get; private set; }

    /// <summary>
    /// The offline character's user block for the equipment window: the fixture's, with what the chosen list entry carries
    /// (CCharacterInfo::Read's user block: ids, name, gender, level, job, skin, gear score, HP) taken from the entry, as the
    /// worn items are. The fixture stays for what the entry does not carry (stats, bag, titles, ...).
    /// </summary>
    public static void TakeChosen(UserBlock into, UserBlock entry)
    {
        if (into == null || entry == null) return;
        into.AccountId = entry.AccountId; into.CharacterId = entry.CharacterId; into.Name = entry.Name;
        into.Gender = entry.Gender; into.Level = entry.Level; into.JobCode = entry.JobCode; into.Job = entry.Job;
        into.Skin = entry.Skin; into.GearScore = entry.GearScore;
        into.HpMax = entry.HpMax; into.HpCurrent = entry.HpCurrent;
    }
}
