using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

/// <summary>
/// The character window's paper doll: the movie's loadRenderTarget loads "img://&lt;movie&gt;:avatar/&lt;w&gt;/&lt;h&gt;"
/// (the m_mouseArea size) into m_renderTarget, which UIMyInfoDialog::vfunc_25 (@0x141a5d0c0) fills with a UIModelView
/// over the window's UIAvatarPC.
/// <list type="bullet">
/// <item>Model: the same build as the face portrait (<see cref="Look.PlayerLook"/>), lit by its own dimmed pair
/// (<see cref="PortraitView.OwnLights"/>), in its idle (Idle_A).</item>
/// <item>Camera (vfunc_25's ApplyCharCamera): the target 65 above the avatar, the eye 190 in front of it on the x axis,
/// the zoom between 300 (min distance) and 150 (max distance) in 5 steps (<see cref="AvatarZoom"/>). The frustum is
/// 0.5 x (w, h) / max(w, h) at near 10, far 100000, so the vertical field of view is 2 atan(0.5 h / max(w, h)).</item>
/// <item>Turning (<see cref="AvatarControl"/>): FromEulerAnglesXYZ of the avatar's angles about the pivot, the avatar
/// position raised to its bound's centre but at most 25 above Eff_Body (vfunc_13).</item>
/// <item>Re-dress: a live session's put-on / put-off (MyCharacter's equip lists replaced, CSceneStateGame::OnItemPutOn /
/// OnItemPutOff) builds the model again from the new lists, keeping the turn and the zoom.</item>
/// <item>Emotion (the skill preview's, <see cref="PlayEmotion"/>): the body's emotion sequences and the face item's emotion
/// group, made at the first emotion; <see cref="PreviewEmotion"/> runs the states.</item>
/// </list>
/// </summary>
public sealed partial class AvatarView : Node
{
    const float Near = 10f, Far = 100000f, TargetHeight = 65f, EyeDistance = 190f;
    const float MinDistance = 300f, MaxDistance = 150f, ZoomSpeed = 700f, PivotRaise = 25f;
    const int ZoomSteps = 5;

    SubViewport view;
    Camera3D camera;
    Node3D copy;
    float pivot;
    AvatarControl control;
    AvatarZoom zoom;
    float minDistance = MinDistance;
    Func<IReadOnlyList<Ms2.Net.Packets.EquipItem>, IReadOnlyList<Ms2.Net.Packets.EquipItem>> dress;
    bool redrawQueued;
    PackSet packs;
    string nifXml;
    object dressedWith;
    Ms2.Net.Packets.MyCharacter me;
    AnimationPlayer anim;
    ModelBuilder builder;
    ModelData md;
    string bodyDir;
    // the emotion part, null until the first PlayEmotion (and again after a re-dress)
    PreviewEmotion emotion;
    /// <summary>The emotion's state and paramStr ("-" before the first), for the scripted live run.</summary>
    public string EmotionState => emotion == null ? "-" : $"state {emotion.State:x} anim {emotion.Anim}";
    Movement.ActionAnims anims;
    FaceEmotion face;
    string playing;                    // the sequence the emotion part played last ("idle" after StateIdle)
    double clock;                      // the view's own ms clock for the face
    double keyPos = KeysFromStart;
    const double KeysFromStart = -2;

    /// <summary>The render target the movie shows.</summary>
    public Texture2D Image => view?.GetTexture();

    /// <summary>The "avatar/&lt;w&gt;/&lt;h&gt;" size of a render target url, or null.</summary>
    public static Vector2I? SizeOf(string url)
    {
        int i = url.IndexOf(":avatar/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var p = url[(i + 8)..].Split('/');
        return p.Length >= 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) && w > 0 && h > 0
            ? new Vector2I(w, h) : null;
    }

    /// <summary>The view for <paramref name="control"/>'s avatar at <paramref name="size"/>, or null when the model cannot
    /// be built. The node has to stay in the tree to render.</summary>
    /// <param name="minDistance">The farthest the camera zooms out (the character window's 300; the shop preview's 1000).</param>
    /// <param name="dress">Changes the items the model is dressed in (the shop preview's items over the player's own); null: as they are.</param>
    /// <param name="stepsDone">The zoom steps the camera starts out from the eye (m_nStepsDone; the skill preview's 2).</param>
    public static AvatarView Create(PackSet packs, string nifXml, Vector2I size, AvatarControl control,
        float minDistance = MinDistance, Func<IReadOnlyList<Ms2.Net.Packets.EquipItem>, IReadOnlyList<Ms2.Net.Packets.EquipItem>> dress = null,
        int stepsDone = 0)
    {
        if (string.IsNullOrEmpty(nifXml) || control == null) return null;
        var av = new AvatarView { Name = "AvatarView", control = control, packs = packs, nifXml = nifXml, dress = dress, minDistance = minDistance,
            zoom = new AvatarZoom(EyeDistance, minDistance, MaxDistance, ZoomSpeed, ZoomSteps, stepsDone) };
        av.dressedWith = av.Worn();
        if (!av.BuildModel(packs, nifXml, size)) { av.QueueFree(); return null; }
        control.Turned += av.Turn;
        control.Zoom += av.StartZoom;
        control.CameraReset += av.ResetZoom;
        if (Ms2Godot.Net.LiveSession.Active && Ms2Godot.Net.LiveSession.Instance.Me is { } me)
        {
            av.me = me;
            me.Changed += av.Redress;
        }
        av.Turn();
        av.Place();
        return av;
    }

    bool BuildModel(PackSet packs, string nifXml, Vector2I size)
    {
        try
        {
            view = new SubViewport
            {
                Size = size, TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            AddChild(view);
            Dress(packs, nifXml);
            float fov = 2f * MathF.Atan(0.5f * size.Y / Math.Max(size.X, size.Y));
            camera = new Camera3D
            {
                Fov = Mathf.RadToDeg(fov), Near = Near, Far = Far, KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true,
                Compositor = Ms2Bloom.GammaOut(),   // character_lit writes gamma values
            };
            view.AddChild(camera);
            return true;
        }
        catch (Exception ex) { GD.PushWarning("avatar view: " + ex.Message); return false; }
    }

    /// <summary>The model in the player's current look (<see cref="Look.PlayerLook"/>), in its idle, lit, with its pivot.</summary>
    void Dress(PackSet packs, string nifXml)
    {
        builder = ModelBuilder.Ui(packs, nifXml);
        (var visual, anim, md, bodyDir) = Look.PlayerLook.Build(builder, packs, dress);
        copy = visual;
        emotion = null; anims = null; face = null; playing = null;
        view.AddChild(copy);
        if (anim.HasAnimation("idle")) anim.Play("idle");     // Idle_A
        PortraitView.OwnLights(copy, PortraitView.ModelViewLightDir);
        pivot = PivotHeight(copy);
    }

    /// <summary>The live equip lists the model was dressed from (replaced, not edited, on a put-on / put-off).</summary>
    object Worn() => Ms2Godot.Net.LiveSession.Active && Ms2Godot.Net.LiveSession.Instance.Me is { } m ? (m.Equips, m.Outfit2) : null;

    /// <summary>The worn items changed (the shop preview's list): the model is built again once, on the main thread, keeping the turn and
    /// the zoom. Nothing is polled; an idle view costs no allocation. A burst of changes (a try-on and a MyCharacter change in one frame)
    /// is one rebuild.</summary>
    public void Refresh()
    {
        if (redrawQueued || !IsInstanceValid(this) || view == null) return;
        redrawQueued = true;
        Callable.From(() =>
        {
            redrawQueued = false;
            if (!IsInstanceValid(this) || view == null) return;
            try
            {
                var old = copy;
                Dress(packs, nifXml);
                old?.QueueFree();
                Turn();
            }
            catch (Exception ex) { GD.PushWarning("avatar view: refresh: " + ex.Message); }
        }).CallDeferred();
    }

    /// <summary>MyCharacter.Changed: a new model when the equip lists changed (on the main thread).</summary>
    void Redress()
    {
        var worn = Worn();
        if (Equals(worn, dressedWith)) return;
        dressedWith = worn;
        Refresh();
    }

    /// <summary>A wheel turn: the zoom starts (the handler is a method so the view can take it off again).</summary>
    void StartZoom(bool zoomIn) => zoom.Start(zoomIn);

    /// <summary>ItemPreview::ResetCamera (itempreview.cpp:489-509): the zoom again from the eye, no steps done.</summary>
    void ResetZoom()
    {
        zoom = new AvatarZoom(EyeDistance, minDistance, MaxDistance, ZoomSpeed, ZoomSteps);
        if (camera != null) Place();
    }

    /// <summary>The bound's centre height, at most <see cref="PivotRaise"/> above Eff_Body.</summary>
    static float PivotHeight(Node3D root)
    {
        Aabb? box = null;
        Walk(root);
        float h = box is { } b ? b.GetCenter().Y : 0f;
        var sk = root.GetNodeOrNull<Skeleton3D>($"Model/{ModelBuilder.SkeletonName}");
        int body = sk?.FindBone("Eff_Body") ?? -1;
        if (body >= 0)
        {
            float cap = (Rel(sk, root) * sk.GetBoneGlobalRest(body)).Origin.Length() + PivotRaise;
            if (h > cap) h = cap;
        }
        return h;

        void Walk(Node n)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                var a = Rel(mi, root) * mi.GetAabb();
                box = box is { } b ? b.Merge(a) : a;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    /// <summary>A node's transform in <paramref name="root"/>'s space (the view is built before it enters the tree).</summary>
    static Transform3D Rel(Node3D n, Node3D root)
    {
        var t = Transform3D.Identity;
        for (Node c = n; c != null && c != root; c = c.GetParent())
            if (c is Node3D c3) t = c3.Transform * t;
        return t;
    }

    /// <summary>vfunc_13: R = FromEulerAnglesXYZ(angles), translate = pivot - R pivot.</summary>
    void Turn()
    {
        var (x, y, z) = control.Angle;
        // MS2 (x, y, z) is Godot (x, z, -y): rotations about the client's x, y, z are about Godot X, -Z, Y
        var r = new Basis(Vector3.Right, Mathf.DegToRad(x)) * new Basis(Vector3.Forward, Mathf.DegToRad(y))
                * new Basis(Vector3.Up, Mathf.DegToRad(z));
        var p = new Vector3(0, pivot, 0);
        copy.Transform = new Transform3D(r, p - r * p);
    }

    /// <summary>The camera at the zoom's distance on the +x axis, looking at the target.</summary>
    void Place()
    {
        var target = new Vector3(0, TargetHeight, 0);
        camera.LookAtFromPosition(target + new Vector3(zoom.Distance, 0, 0), target, Vector3.Up);
    }

    long Ms => (long)clock;

    /// <summary>
    /// The emotion of <paramref name="row"/> on the avatar (UIAvatarPC::FUN_1418bce80, uiavatarbase.cpp:1509, from the skill
    /// preview): the first one loads the body's sequence list (<see cref="Movement.ActionAnims.Body"/>, no movement table) and
    /// the face item's emotion group (<see cref="Look.FaceTexture.For"/>), then <see cref="PreviewEmotion.Enter"/>.
    /// </summary>
    public void PlayEmotion(SkillData row)
    {
        if (copy == null || anim == null || row == null) return;
        if (emotion == null)
        {
            anims = Movement.ActionAnims.Body(packs, builder, md, anim, bodyDir);
            var (f, group) = Look.FaceTexture.For(copy, builder, packs, bodyDir == "male" ? 0 : 1);
            face = f;
            face?.SetBattle(false, Ms);
            face?.SetGroup(group, Ms);
            emotion = new PreviewEmotion
            {
                Animation = (n, k) => EmotionCast.Animation(group, n, k),
                Play = PlaySequence,
                Face = n => face?.Play(n, false, Ms),
                FaceRest = () => face?.ToRest(true, Ms),
                Idle = () => { if (anim.HasAnimation("idle")) { anim.Play("idle"); playing = "idle"; } },
            };
        }
        emotion.Enter(row);
    }

    /// <summary>PCBase::PlayAnimation(name, 1, bForce, false) on the avatar: the kf's own cycle, from its start; without bForce
    /// the sequence playing now is left as it is.</summary>
    (double, int)? PlaySequence(string name, bool force)
    {
        string key = anims.SequenceOwnCycle(name);
        if (key == null || (!force && key == playing && anim.IsPlaying())) return null;
        anim.Play(key);
        anim.Seek(0, true);
        playing = key;
        keyPos = KeysFromStart;
        anims.Kf.TryGetValue(key, out var kf);
        return (anim.GetAnimation(key).Length, kf?.Cycle ?? 0);
    }

    /// <summary>The keys the played sequence passed since the last frame, to the face (PCBase::TextKeyEvent's emo_ branch,
    /// pcbase.cpp:1032-1039), as the own pc's.</summary>
    void FaceKeys()
    {
        if (face == null || playing == null || !anim.IsPlaying() || !anims.Kf.TryGetValue(playing, out var kf) || kf.TextKeys.Count == 0) return;
        double pos = anim.CurrentAnimationPosition, from = keyPos;
        keyPos = pos;
        foreach (var (t, key) in kf.TextKeys)
        {
            bool crossed = from == KeysFromStart ? t <= pos : pos >= from ? from < t && t <= pos : t > from || t <= pos;
            if (crossed) face.TextKey(key, Ms);
        }
    }

    public override void _Process(double delta)
    {
        if (camera != null && zoom.Update((float)delta)) Place();
        if (emotion == null) return;
        clock += delta * 1000;
        face?.Tick(Ms);                // the face before the states, as PCBase::vfunc_53
        emotion.Tick(delta);
        FaceKeys();
    }

    public override void _ExitTree()
    {
        if (me != null) { me.Changed -= Redress; me = null; }
        if (control == null) return;
        control.Turned -= Turn;
        control.Zoom -= StartZoom;
        control.CameraReset -= ResetZoom;
    }
}
