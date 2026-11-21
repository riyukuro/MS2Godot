using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Ui;

namespace Ms2Godot.Net;

/// <summary>
/// The field's reactors as the world draws them (CReactor, reactor.cpp), a child of the field:
/// <list type="bullet">
/// <item>the model is the kfm asset "urn:gamebryo-animation:" + the add's state kfmName (FUN_140b79490 @0x140b79490,
/// SetModelPath), the kfm of that name in the effect pack with its nif beside it, at the reactor's position (vfunc_19)
/// and rotation (vfunc_21, Euler XYZ degrees);</item>
/// <item>each tick the head of the reactor's animation queue is played (FUN_140b791a0, PlayAnimation(head, 1.0, false));
/// a kf plays with its sequence's own cycle type, and the end of a sequence pops the head when it is the playing one
/// (EndOfSequence @0x140b7a1b0); a head with no kf in the data stays the head;</item>
/// <item>the data's connected effect is on while the own character is in the reactor's player set
/// (UpdateConnectedEffect @0x140b79330: AddEffect @0x140b78ef0 at pos + (0, 0, 1) with the reactor's rotation, scale 1;
/// RemoveEffect @0x140b79120), and goes with the reactor.</item>
/// </list>
/// A frame with no state change allocates 0 B.
/// </summary>
public partial class ReactorLayer : Node3D
{
    public const string EffectArchive = "Resource/Model/Effect";

    sealed class Model
    {
        public ModelData Data; public KfmFile Kfm; public string Folder;
        // per animation name: the built animation and its library name, null when the kfm or the data has no such kf
        public readonly Dictionary<string, (Godot.Animation Anim, StringName Name)?> Anims = new(StringComparer.Ordinal);
    }

    sealed class Inst
    {
        public FieldReactors.Reactor R; public Node3D Root; public AnimationPlayer Player; public Model Model;
        public string Playing;
        public int Effect; public bool EffectOn;
    }

    readonly PackSet packs; readonly ModelBuilder mb; readonly Func<long> characterId;
    readonly Dictionary<string, Model> models = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, Inst> inst = new();

    /// <summary>Reactors whose kfm was not found or did not load.</summary>
    public int NoModel;

    public ReactorLayer(PackSet packs, ModelBuilder mb, Func<long> characterId)
    {
        Name = "Reactors";
        this.packs = packs; this.mb = mb; this.characterId = characterId;
        if (Array.IndexOf(mb.TextureArchives, EffectArchive) < 0)
            mb.TextureArchives = [.. mb.TextureArchives, EffectArchive];
    }

    public ReactorLayer() { }

    static readonly NodePath RegionSkillsName = "RegionSkills";
    World.RegionSkills Fx => GetParent()?.GetNodeOrNull<World.RegionSkills>(RegionSkillsName);
    static readonly NodePath PlayerPath = "Model/AnimationPlayer";
    // Gamebryo's Z-up file space to Godot's Y-up (the built model's own turn, ModelBuilder.Build)
    static readonly Transform3D ZUp = new(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero);

    Model ModelFor(string kfmName)
    {
        if (string.IsNullOrEmpty(kfmName)) return null;
        if (models.TryGetValue(kfmName, out var m)) return m;
        m = null;
        try
        {
            string kfmEntry = packs.FindByFileName(EffectArchive, kfmName.ToLowerInvariant() + ".kfm");
            if (kfmEntry != null)
            {
                string folder = kfmEntry[..kfmEntry.LastIndexOf('/')];
                var kfm = KfmFile.Load(packs.Read(EffectArchive, kfmEntry));
                string nif = $"{folder}/{Path.GetFileName(kfm.Nif.Replace('\\', '/')).ToLowerInvariant()}";
                if (packs.Exists(EffectArchive, nif))
                    m = new Model { Data = mb.LoadModel(EffectArchive, nif), Kfm = kfm, Folder = folder };
            }
        }
        catch (Exception ex) { GD.PushWarning($"reactor kfm {kfmName}: {ex.Message}"); }
        return models[kfmName] = m;
    }

    /// <summary>The kfm's sequence of that name as an animation (looping as its cycle type says), or null.</summary>
    (Godot.Animation Anim, StringName Name)? Anim(Model m, string name)
    {
        if (m.Anims.TryGetValue(name, out var a)) return a;
        a = null;
        var seq = m.Kfm.ByName(name);
        if (seq != null)
        {
            string entry = $"{m.Folder}/{Path.GetFileName(seq.KfPath.Replace('\\', '/')).ToLowerInvariant()}";
            try
            {
                if (packs.Exists(EffectArchive, entry))
                {
                    var kf = KfAnimation.Load(NifFile.Load(mb.Spec, packs.Read(EffectArchive, entry)));
                    var anim = ModelBuilder.BuildAnimation(kf, m.Data);
                    anim.LoopMode = kf.Cycle switch { 1 => Godot.Animation.LoopModeEnum.Pingpong, 2 => Godot.Animation.LoopModeEnum.None, _ => Godot.Animation.LoopModeEnum.Linear };
                    a = (anim, new StringName("r" + seq.Id));
                }
            }
            catch (Exception ex) { GD.PushWarning($"reactor kf {entry}: {ex.Message}"); }
        }
        return m.Anims[name] = a;
    }

    /// <summary>The client-space transform of a reactor (position, rotation in degrees) as a Godot transform.</summary>
    public static Transform3D Placement(System.Numerics.Vector3 pos, System.Numerics.Vector3 rot) =>
        ZUp * ModelBuilder.ToGodot(new Xform(new Vec3(pos.X, pos.Y, pos.Z), DummyWorld.EulerXyz(rot.X, rot.Y, rot.Z), 1)) * ZUp.AffineInverse();

    /// <summary>A reactor was added: its model (a replaced reactor went through <see cref="Remove"/> first).</summary>
    public void Add(FieldReactors.Reactor r)
    {
        Remove(r);
        var root = new Node3D { Name = $"Reactor{r.ObjectId}" };
        var i = new Inst { R = r, Root = root, Model = ModelFor(r.State?.KfmName) };
        if (i.Model != null)
        {
            var visual = mb.Build(i.Model.Data, r.State.KfmName);
            root.AddChild(visual);
            i.Player = visual.GetNodeOrNull<AnimationPlayer>(PlayerPath);
            if (i.Player != null) i.Player.AnimationFinished += _ => Finished(i);
        }
        else NoModel++;
        AddChild(root);
        root.Transform = Placement(r.Position, r.Rotation);
        inst[r.ObjectId] = i;
        Tick(i);
    }

    /// <summary>A reactor went: its model and its connected effect (~CReactor).</summary>
    public void Remove(FieldReactors.Reactor r)
    {
        if (!inst.Remove(r.ObjectId, out var i)) return;
        EffectOff(i);
        i.Root.QueueFree();
    }

    /// <summary>The field's reactors that are there already.</summary>
    public void AddAll(FieldReactors all)
    {
        if (all == null) return;
        foreach (var r in all.ByName) Add(r);
    }

    public override void _Process(double delta)
    {
        foreach (var i in inst.Values) Tick(i);
    }

    void Tick(Inst i)
    {
        // FUN_140b791a0: the head plays; PlayAnimation of the playing one changes nothing
        string head = i.R.Head;
        if (head != null && !string.Equals(head, i.Playing, StringComparison.Ordinal))
        {
            i.Playing = head;
            if (i.Player != null && Anim(i.Model, head) is { } a)
            {
                var lib = i.Player.GetAnimationLibrary("");
                if (!lib.HasAnimation(a.Name)) lib.AddAnimation(a.Name, a.Anim);
                i.Player.Play(a.Name);
            }
        }
        string effect = i.R.Data?.ConnectedEffect;
        bool on = !string.IsNullOrEmpty(effect) && i.R.ConnectedEffectOn(characterId());
        if (on == i.EffectOn) return;
        i.EffectOn = on;
        if (!on) { EffectOff(i); return; }
        var p = i.R.Position; var rot = i.R.Rotation;
        i.Effect = Fx?.Spawn(effect, new Xform(new Vec3(p.X, p.Y, p.Z + 1), DummyWorld.EulerXyz(rot.X, rot.Y, rot.Z), 1), true, 0) ?? 0;
    }

    void EffectOff(Inst i)
    {
        i.EffectOn = false;
        if (i.Effect != 0) Fx?.FadeOut(i.Effect, 0);
        i.Effect = 0;
    }

    /// <summary>EndOfSequence: the playing sequence ended.</summary>
    void Finished(Inst i)
    {
        if (i.Playing != null) i.R.EndOfSequence(i.Playing);
    }

    /// <summary>The reactor's model root and playing animation (tests).</summary>
    public Node3D RootOf(int objectId) => inst.TryGetValue(objectId, out var i) ? i.Root : null;
    public string PlayingOf(int objectId) => inst.TryGetValue(objectId, out var i) ? i.Playing : null;
    public int EffectOf(int objectId) => inst.TryGetValue(objectId, out var i) ? i.Effect : 0;
    public AnimationPlayer PlayerOf(int objectId) => inst.TryGetValue(objectId, out var i) ? i.Player : null;
}
