using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// Checks every 3D view for the gamma -> linear conversion (autoload). The field and character shaders write gamma values
/// (field_lit.gdshader), so each camera needs one conversion before Godot's linear -> sRGB output: <see cref="Ms2Bloom"/>
/// (the field) or <see cref="Ms2Bloom.GammaOut"/> (views without bloom). Without it the view comes out washed out
/// (the gamma values encoded once more). A camera whose compositor has neither, or whose Ms2Bloom could not set up its
/// RenderingDevice passes, is logged as an error and gets the conversion from its environment instead
/// (<see cref="Fallback"/>): a visible frame, without the bloom. Cameras in the <see cref="Exempt"/> group draw no
/// gamma-valued output for the screen (the glow target: <see cref="GlowPass"/>).
/// </summary>
public partial class GammaGuard : Node
{
    public const string Exempt = "gamma_exempt";

    readonly List<(Camera3D cam, ulong frame)> pending = new();

    public override void _EnterTree()
    {
#if MS2_DEVTOOLS
        DevEndpoint.Bootstrap(this);   // starts only with env MS2_DEBUG=1
#endif
        if (DisplayServer.GetName() == "headless") { SetProcess(false); return; }   // nothing drawn, no RenderingDevice
        GetTree().NodeAdded += OnNodeAdded;
        connected = true;
    }

    // headless never connects: disconnecting there makes the engine print an error at every exit
    bool connected;

    public override void _ExitTree()
    {
        if (connected) GetTree().NodeAdded -= OnNodeAdded;
        connected = false;
    }

    void OnNodeAdded(Node node)
    {
        if (node is Camera3D cam && !cam.IsInGroup(Exempt)) pending.Add((cam, Engine.GetProcessFrames()));
    }

    public override void _Process(double delta)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var (cam, frame) = pending[i];
            if (!IsInstanceValid(cam) || !cam.IsInsideTree()) { pending.RemoveAt(i); continue; }
            if (Engine.GetProcessFrames() < frame + 2) continue;   // the compositor is set after AddChild; Ms2Bloom sets up on the render thread
            Ms2Bloom bloom = null;
            if (cam.Compositor?.CompositorEffects is { } effects)
                foreach (var effect in effects)
                    if (effect is Ms2Bloom b && b.Enabled) bloom = b;
            if (bloom is { Ready: false }) continue;
            pending.RemoveAt(i);
            if (bloom == null)
                GD.PushError($"{cam.GetPath()}: 3D view without Ms2Bloom or Ms2Bloom.GammaOut(); converting through its environment instead");
            else if (bloom.Failed)
                GD.PushError($"{cam.GetPath()}: Ms2Bloom could not set up its passes; converting through the environment instead (no bloom)");
            else continue;
            Fallback(cam);
        }
    }

    static ImageTexture decode;

    /// <summary>
    /// Godot applies the environment's colour correction after its linear -> sRGB encode, so a 1D lookup that decodes
    /// sRGB leaves the buffer's gamma values on screen, as Ms2Bloom's conversion does. The camera gets its own copy of
    /// the environment it draws with (the world's may serve other cameras that do convert).
    /// </summary>
    static void Fallback(Camera3D cam)
    {
        var env = cam.Environment ?? cam.GetWorld3D()?.Environment;
        if (decode == null)
        {
            var lut = Image.CreateEmpty(256, 1, false, Image.Format.Rgb8);
            for (int i = 0; i < 256; i++)
            {
                float c = i / 255f, v = c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
                lut.SetPixel(i, 0, new Color(v, v, v));
            }
            decode = ImageTexture.CreateFromImage(lut);
        }
        if (env != null && env.AdjustmentColorCorrection == decode) return;
        var own = (Godot.Environment)env?.Duplicate() ?? new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Disabled };
        own.AdjustmentEnabled = true;
        own.AdjustmentColorCorrection = decode;
        cam.Environment = own;
    }
}
