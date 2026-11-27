using Godot;
using Ms2.Formats.Pack;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// The main player info's mount portrait (<see cref="RideeAvatar"/>): the movie's RideeInfo clip loads
/// "img://&lt;movie&gt;:faceView/100/100" into m_renderTarget, which UIMainPlayerInfo::vfunc_25 (@0x1419efa60,
/// uimainplayerinfo.cpp:663-678) fills with a UIModelView over the UIAvatarRidee of the ridden row.
/// <list type="bullet">
/// <item>Model (AvatarRidee, avatarpc.cpp:1282-1378): the row's model for a rider of gender 1 at the row's scale
/// (<see cref="Mount.Load"/>, the field mount's build), at the origin, playing Idle_A, lit by its own pair dimmed by 0.9
/// (<see cref="PortraitView.OwnLights"/>). UIAvatarRidee::vfunc_4 turns it to the preview angle of avatar kind 0
/// (<see cref="RideeAvatar.PreviewAngle"/>: 270 degrees clockwise about z, UIAvatarBase::vfunc_19 / vfunc_20's
/// FromEulerAnglesXYZ on the model root), which faces it to +x, the side every row's faceCamera eye is on.</item>
/// <item>Camera (UIModelView::vfunc_15 and vfunc_13): the frustum <see cref="RideeAvatar.Frustum"/> at near 10, far 100000;
/// the eye at the row's faceCamera cameraPosition looking at its lookAtPosition, up z, both in the model's space.</item>
/// </list>
/// STILL-OPEN: AvatarRidee keeps the row's loopEffect on this model too (avatarpc.cpp:1365-1370); the view has no effect layer.
/// </summary>
public sealed partial class RideeAvatarView : Node
{
    const float Near = 10f, Far = 100000f;

    SubViewport view;
    Mount mount;

    /// <summary>The riding row the view shows.</summary>
    public RideeRow Row { get; private set; }

    /// <summary>The render target the movie shows.</summary>
    public Texture2D Image => view?.GetTexture();

    /// <summary>The camera's eye and target in Godot axes, for the live check.</summary>
    public (Vector3 Eye, Vector3 Target) CameraAt { get; private set; }

    /// <summary>AvatarRidee::vfunc_12: Idle_A looping, once the model is in the tree.</summary>
    public override void _Ready() => mount?.PlaySequence("Idle_A", true);

    /// <summary>The faceView/&lt;w&gt;/&lt;h&gt; size of a render target url, or null.</summary>
    public static Vector2I? SizeOf(string url)
    {
        int i = url.IndexOf(":" + RideeAvatar.ImageName + "/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var p = url[(i + RideeAvatar.ImageName.Length + 2)..].Split('/');
        return p.Length >= 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) && w > 0 && h > 0
            ? new Vector2I(w, h) : null;
    }

    /// <summary>The view of <paramref name="row"/>'s model at <paramref name="size"/>, or null when the model cannot be built
    /// (CreateAvatarRideeFromRow gives no ridee). The node has to stay in the tree to render.</summary>
    public static RideeAvatarView Create(PackSet packs, string nifXml, RideeRow row, Vector2I size)
    {
        if (string.IsNullOrEmpty(nifXml) || row == null) return null;
        var v = new RideeAvatarView { Name = "RideeAvatarView", Row = row };
        try
        {
            v.view = new SubViewport
            {
                Size = size, TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            v.AddChild(v.view);
            var mount = v.mount = Mount.Load(packs, ModelBuilder.Ui(packs, nifXml), row.Id, 1);
            var model = mount.Visual;
            // UIAvatarBase::vfunc_20: the root's rotation FromEulerAnglesXYZ(0, 0, angle); the client's z is Godot's Y, and
            // Gamebryo's MakeZRotation turns clockwise (Foundation/efd/Win32/Matrix3_Win32.cpp:76-107), so 270 turns the
            // model's front (client -y) to +x, where every row's faceCamera eye is
            model.Basis = new Basis(Vector3.Up, -Mathf.DegToRad(RideeAvatar.PreviewAngle(RideeAvatar.PreviewKind))).Scaled(model.Scale);
            v.view.AddChild(model);
            PortraitView.OwnLights(model, PortraitView.ModelViewLightDir);
            var (_, halfH) = RideeAvatar.Frustum(size.X, size.Y);
            var camera = new Camera3D
            {
                Fov = Mathf.RadToDeg(2f * MathF.Atan(halfH)), Near = Near, Far = Far, KeepAspect = Camera3D.KeepAspectEnum.Height,
                Current = true, Compositor = Ms2Bloom.GammaOut(),   // character_lit writes gamma values
            };
            v.view.AddChild(camera);
            // the client's (x, y, z) is Godot's (x, z, -y)
            var eye = new Vector3(row.FaceCamera.X, row.FaceCamera.Z, -row.FaceCamera.Y);
            var target = new Vector3(row.FaceLookAt.X, row.FaceLookAt.Z, -row.FaceLookAt.Y);
            if (!eye.IsEqualApprox(target)) camera.LookAtFromPosition(eye, target, Vector3.Up);
            v.CameraAt = (eye, target);
            return v;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"ridee avatar {row.Id}: {ex.Message}");
            v.QueueFree();
            return null;
        }
    }
}
