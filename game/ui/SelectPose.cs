using System.Globalization;
using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui;

/// <summary>
/// The select screen's job pose: the job group's CHARACTER_SELECT_BORE and CHARACTER_SELECT_BORE_IDLE actions
/// (ActionType 73 and 74, actioncontainer.cpp; the names from table/pcactiondata.xml), and each sequence's keys in
/// anikeyinfo/male.xml or female.xml: "effect_&lt;effect xml&gt;" keys start an effect at their time, "end" is the
/// sequence's end.
/// </summary>
public sealed class SelectPose
{
    public sealed class Sequence
    {
        public string Name;                                  // e.g. Ranger_CharacterSelect_Bore_A (the kf file name, lower case)
        public float End;                                    // the "end" key, seconds
        public readonly List<(float Time, string EffectXml)> Effects = new();
    }

    public Sequence Bore, BoreIdle;

    static XDocument actions;
    static readonly Dictionary<int, XDocument> keys = new();

    /// <summary>The pose of a job group for a gender (0 male, 1 female); null when the group has none.</summary>
    public static SelectPose For(PackSet packs, int jobGroup, int gender)
    {
        actions ??= Xml(packs, "table/pcactiondata.xml");
        var group = actions?.Root.Elements("group").FirstOrDefault(g => (string)g.Attribute("groupID") == jobGroup.ToString(CultureInfo.InvariantCulture));
        string Name(string type) => ((string)group?.Elements("v").FirstOrDefault(v => (string)v.Attribute("type") == type)?.Attribute("names"))?.Split(',')[0].Trim();
        string bore = Name("CHARACTER_SELECT_BORE"), idle = Name("CHARACTER_SELECT_BORE_IDLE");
        if (bore == null || idle == null) return null;
        if (!keys.TryGetValue(gender, out var doc)) keys[gender] = doc = Xml(packs, gender == 0 ? "anikeyinfo/male.xml" : "anikeyinfo/female.xml");
        return new SelectPose { Bore = Seq(doc, bore), BoreIdle = Seq(doc, idle) };
    }

    static Sequence Seq(XDocument doc, string name)
    {
        var s = new Sequence { Name = name };
        var seq = doc?.Root.Elements("seq").FirstOrDefault(e => string.Equals((string)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
        foreach (var k in seq?.Elements("key") ?? Enumerable.Empty<XElement>())
        {
            string key = (string)k.Attribute("name") ?? "";
            float.TryParse((string)k.Attribute("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out float t);
            if (key == "end") s.End = t;
            else if (key.StartsWith("effect_", StringComparison.Ordinal)) s.Effects.Add((t, key["effect_".Length..]));
        }
        return s;
    }

    static XDocument Xml(PackSet packs, string entry) => packs.Exists("Xml", entry)
        ? XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'))
        : null;
}

/// <summary>
/// Plays a <see cref="SelectPose"/> on a preview character, as the client's idle state does it:
/// <list type="bullet">
/// <item>when the preview has loaded, sub state 0xe plays action 0x49 (UISceneLobbySelect::vfunc_12 ->
/// UICharacterListDialog::FUN_1418f1f10 -> PCBase::FUN_14166cc40(0xe); StateIdle::vfunc_9 -> FUN_1417ce9b0);</item>
/// <item>its end moves to sub state 0x15 and plays action 0x4a (StateIdle::EndOfSequence), which has no end branch:
/// FUN_1417cee90 plays it again whenever it is not playing, so it loops for good;</item>
/// <item>every start of a sequence runs its keys again: an effect key builds the effect's nif under the character's
/// scene root ("Scene Root" parent node) after its startDelay and removes it after its duration.</item>
/// </list>
/// </summary>
public sealed partial class SelectPosePlayer : Node
{
    public const string Bore = "bore", BoreIdle = "bore_idle";
    const string EffectArchive = "Resource/Model/Effect";

    readonly SelectPose pose;
    readonly AnimationPlayer anim;
    readonly Node3D sceneRoot;
    readonly ModelBuilder builder;
    readonly PackSet packs;
    readonly Ms2.Formats.World.EffectTable effects;

    public SelectPosePlayer(SelectPose pose, AnimationPlayer anim, Node3D sceneRoot, ModelBuilder builder, PackSet packs)
    {
        Name = "SelectPose";
        this.pose = pose; this.anim = anim; this.sceneRoot = sceneRoot; this.builder = builder; this.packs = packs;
        effects = new Ms2.Formats.World.EffectTable(packs);
    }

    /// <summary>Adds the two sequences to the player's library; false when a kf is missing (the caller keeps its idle).</summary>
    public static bool AddSequences(SelectPose pose, AnimationPlayer anim, ModelBuilder builder, ModelData md, string dir)
    {
        try
        {
            var lib = anim.GetAnimationLibrary("");
            foreach (var (key, seq) in new[] { (Bore, pose.Bore), (BoreIdle, pose.BoreIdle) })
            {
                var a = ModelBuilder.BuildAnimation(builder.LoadKf($"{dir}/{seq.Name.ToLowerInvariant()}.kf"), md);
                a.LoopMode = Animation.LoopModeEnum.None;
                lib.AddAnimation(key, a);
            }
            return true;
        }
        catch (Exception ex) { GD.PushWarning("select pose: " + ex.Message); return false; }
    }

    public override void _Ready()
    {
        anim.AnimationStarted += OnStarted;
        anim.AnimationFinished += OnFinished;
        anim.Play(Bore);
    }

    void OnFinished(StringName name)
    {
        if (name == Bore || name == BoreIdle) anim.Play(BoreIdle);
    }

    void OnStarted(StringName name)
    {
        var seq = name == Bore ? pose.Bore : name == BoreIdle ? pose.BoreIdle : null;
        if (seq == null) return;
        foreach (var (time, xml) in seq.Effects)
            foreach (var part in effects.Get(xml) ?? new List<Ms2.Formats.World.EffectPart>())
                if (part.Type == "nif" && !string.IsNullOrEmpty(part.FilePath)) Spawn(part, time + part.StartDelay);
    }

    async void Spawn(Ms2.Formats.World.EffectPart part, float delay)
    {
        if (delay > 0) await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree() || !IsInstanceValid(sceneRoot)) return;
        Node3D fx;
        try { fx = BuildEffect(part); }
        catch (Exception ex) { GD.PushWarning($"select pose effect {part.FilePath}: {ex.Message}"); return; }
        sceneRoot.AddChild(fx);
        if (part.Duration > 0)
        {
            await ToSignal(GetTree().CreateTimer(part.Duration), SceneTreeTimer.SignalName.Timeout);
            if (IsInstanceValid(fx)) fx.QueueFree();
        }
    }

    /// <summary>
    /// The effect nif as a model (a skinned mesh driven by the nif's own transform controllers), placed in the
    /// character's scene root frame by the effect xml's transform (translate, Euler XYZ degrees, scale).
    /// </summary>
    Node3D BuildEffect(Ms2.Formats.World.EffectPart part)
    {
        var nif = NifFile.Load(builder.Spec, packs.Read(EffectArchive, part.FilePath.Replace('\\', '/').ToLowerInvariant()));
        var md = ModelData.Load(nif);
        var root = builder.Build(md, System.IO.Path.GetFileNameWithoutExtension(part.FilePath));
        var kf = KfAnimation.FromControllers(nif);
        if (kf != null)
        {
            var player = root.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            var a = ModelBuilder.BuildAnimation(kf, md);
            a.LoopMode = kf.Cycle == 0 ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            player.GetAnimationLibrary("").AddAnimation("fx", a);
            player.Play("fx");
        }
        // Euler XYZ degrees as the item dummies take them (EquipApplier.DummyOf)
        const double d2r = Math.PI / 180;
        var r = part.Rotation; var t = part.Translate;
        var q = Quat.FromAxis(0, r[0] * d2r) * Quat.FromAxis(1, r[1] * d2r) * Quat.FromAxis(2, r[2] * d2r);
        var xml = ModelBuilder.ToGodot(new Xform(new Vec3(t[0], t[1], t[2]), q.ToMatrix(), part.Scale));
        // under the character's Model node (the scene root, file space): the built root turns Z-up to Y-up again,
        // so it carries that turn undone
        var zup = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero);
        root.Transform = xml * zup.AffineInverse();
        return root;
    }
}
