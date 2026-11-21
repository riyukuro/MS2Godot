using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;

namespace Ms2Godot.Movement;

/// <summary>
/// A dead player's tombstone model, the own player's or another's: PC::FUN_14165cac0 (pc.cpp:4037), run by StateDead::vfunc_4's normal branch
/// (statedead.cpp:145). The flat model is MS2TombStoneDark while the revival is blocked (PC::IsRevivalBlocked), else
/// MS2TombStone; its KfmAsset names the kfm, placed at the player's position with no rotation and scale 1
/// (EntityWorldManager::FUN_1411ce7c0). The states and the packet's hits are <see cref="Ms2.Ui.Hud.Tombstone"/>; this node
/// plays what they ask for: the entity's InitialSequence first (the random regen entry), then each SetState's sequence,
/// forced (restarted) or not. The kf's own cycle type sets the loop (0 loop, 1 back and forth, 2 clamp).
/// Each frame is PC::vfunc_142 (pc.cpp:4612): the tomb follows the player's position and runs the state timer; it is freed
/// once finished, or while not breaking and the player left StateDead.
/// STILL-OPEN: the position snap (CubeMapInformationService::FUN_1410b9150: a ray down 750 onto a cube whose field70 is set
/// takes that cube's x,y) is not done, field70 is not known; the tomb gem's kfm and rotation (feature 0x67, gem slot 5) are
/// not read.
/// </summary>
public sealed partial class PlayerTombstone : Node3D
{
    public const string Flat = "MS2TombStone", FlatDark = "MS2TombStoneDark";

    public Tombstone State { get; private set; }
    readonly Dictionary<string, Godot.Animation> anims = new(StringComparer.OrdinalIgnoreCase);
    Node3D pc;
    Func<bool> inDeath;
    AnimationPlayer player;
    PackSet packs;
    ModelBuilder builder;
    KfmFile kfm;
    ModelData md;
    string archive, folder;

    /// <summary>The tomb for the PC body <paramref name="pc"/> (its feet position) under <paramref name="parent"/>, or null when the
    /// flat or its model is missing. <paramref name="inDeath"/> tells whether the PC is in StateDead (GetCurStateId() == 0xc).</summary>
    public static PlayerTombstone Build(Node3D parent, PackSet packs, ModelBuilder builder, Node3D pc, Func<bool> inDeath, bool revivalBlocked)
    {
        try
        {
            var flat = new FlatLibrary(packs).Get(revivalBlocked ? FlatDark : Flat);
            if (flat == null || !flat.Props.TryGetValue("KfmAsset", out var llid)) return null;
            var (archive, entry) = AssetIndex.For(packs).Resolve(llid);
            if (archive == null || entry == null || !packs.Exists(archive, entry)) return null;
            var kfm = KfmFile.Load(packs.Read(archive, entry));
            string folder = entry[..entry.LastIndexOf('/')];
            string nif = $"{folder}/{Path.GetFileName(kfm.Nif.Replace('\\', '/')).ToLowerInvariant()}";
            if (!packs.Exists(archive, nif)) return null;
            if (!builder.TextureArchives.Contains(archive)) builder.TextureArchives = builder.TextureArchives.Append(archive).ToArray();
            var md = builder.LoadModel(archive, nif);
            string P(string k) => flat.Props.TryGetValue(k, out var v) ? v : null;
            var t = new PlayerTombstone
            {
                Name = "Tombstone", pc = pc, inDeath = inDeath, packs = packs, builder = builder, kfm = kfm, md = md, archive = archive, folder = folder,
                State = new Tombstone(P("SpawnAnimation"), P("HitAnimation"), P("InitialSequence"), P("reactionSequenceName")),
            };
            // FUN_140adf9d0 (breakableobject.cpp:584): the box and the flags a tag area reads (TombTagPass)
            t.State.Breakable(P("Enabled") == "True", Tombstone.ParseVector(P("ShapeDimensions")), P("reactionArrow") == "True",
                int.TryParse(P("SoundMaterial"), out int sound) ? sound : 0);
            var body = builder.Build(md, "Body");
            t.AddChild(body);
            t.player = body.GetNodeOrNull<AnimationPlayer>("Model/AnimationPlayer");
            parent.AddChild(t);
            t.GlobalPosition = pc.GlobalPosition;
            t.Play(t.State.Initial, true);
            return t;
        }
        catch (Exception ex) { GD.PushWarning($"tombstone: {ex.Message}"); return null; }
    }

    /// <summary>The kfm sequence as an animation, its loop from the kf's cycle type; null when the kfm has no such name.</summary>
    Godot.Animation Anim(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (anims.TryGetValue(name, out var a)) return a;
        a = null;
        var seq = kfm.ByName(name);
        if (seq != null)
        {
            string entry = $"{folder}/{Path.GetFileName(seq.KfPath.Replace('\\', '/')).ToLowerInvariant()}";
            try
            {
                if (packs.Exists(archive, entry))
                {
                    var kf = KfAnimation.Load(NifFile.Load(builder.Spec, packs.Read(archive, entry)));
                    a = ModelBuilder.BuildAnimation(kf, md);
                    a.LoopMode = kf.Cycle switch { 1 => Godot.Animation.LoopModeEnum.Pingpong, 2 => Godot.Animation.LoopModeEnum.None, _ => Godot.Animation.LoopModeEnum.Linear };
                    player?.GetAnimationLibrary("").AddAnimation(seq.Name, a);
                }
            }
            catch (Exception ex) { GD.PushWarning($"tombstone kf {entry}: {ex.Message}"); }
        }
        return anims[name] = a;
    }

    /// <summary>PlayAnimation(name, force): a name the kfm lacks plays nothing; without force the playing sequence is kept.</summary>
    void Play(string name, bool force)
    {
        if (player == null || Anim(name) == null) return;
        string key = kfm.ByName(name).Name;
        if (!force && player.CurrentAnimation == key && player.IsPlaying()) return;
        if (force) player.Stop();
        player.Play(key);
    }

    /// <summary>The Tombstone packet's tomb part (<see cref="Tombstone.Apply"/>); returns the bar's stage for a hit.</summary>
    public int? Hit(int kind, int value) => PendingStage = State.Apply(kind, value, (long)Time.GetTicksMsec());

    /// <summary>The bar's stage of the last hit, until the bar (TombGaugeBar) took it.</summary>
    public int? PendingStage;

    /// <summary>The playing sequence, for the checks.</summary>
    public string Current => player?.CurrentAnimation;

    public override void _Process(double delta)
    {
        bool pcDead = pc != null && IsInstanceValid(pc) && inDeath();
        State.Update((long)Time.GetTicksMsec());
        if (!State.KeepFor(pcDead) || pc == null || !IsInstanceValid(pc)) { QueueFree(); return; }
        GlobalPosition = pc.GlobalPosition;
        if (State.Play != null) { Play(State.Play, State.PlayForce); State.Played(); }
        if (Visible != State.Visible) Visible = State.Visible;
    }
}
