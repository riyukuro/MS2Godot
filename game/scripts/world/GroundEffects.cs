using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Net;

namespace Ms2Godot.World;

/// <summary>
/// The visual half of CGroundEffectDataManager::PlayEffects (@0x141377420): each effect file a ground action picks (the steps' and
/// StateLand's "land", e.g. CH/Common/Tile/default/default_e_land.xml) goes to CEffectService::InsertEffect at the character. Its nif
/// parts play here through the effect scene runtime, at the character's feet and facing, not attached (attachToNode false), for the
/// part's duration after its startDelay, and its vibrate parts shake the camera. The sound parts play in FootstepSounds.
/// </summary>
public partial class GroundEffects : Node3D
{
    const string EffectArchive = "Resource/Model/Effect";
    readonly PackSet packs;
    readonly ModelBuilder builder;
    readonly EffectTable table;
    readonly Dictionary<string, EffectScene> scenes = new(StringComparer.OrdinalIgnoreCase);
    int seed;
    public int Played;

    public GroundEffects(PackSet packs, ModelBuilder builder)
    {
        this.packs = packs; this.builder = builder; table = new EffectTable(packs);
        Name = "GroundEffects";
    }

    readonly List<Ms2.Formats.World.EffectPart> playParts = new();

    /// <summary>The kind of the character the effects play at (1 the own PC, 3 another PC's): a vibrate part shakes the camera
    /// only for one not of kind 3 (<see cref="CameraShakes.Makes"/>).</summary>
    public int CasterKind = EffectOwner.KindMyPc;

    /// <summary>Plays the nif parts of an effect file at a Godot point, turned by <paramref name="yawRadians"/> about the up axis.</summary>
    public void Play(string effectFile, Vector3 at, float yawRadians)
    {
        var parts = table.Get(effectFile);
        if (parts == null) return;
        // a random part makes one child (CRandomEffect::vfunc_3, randomeffectparam.cpp:143-178)
        foreach (var part in Ms2.Formats.World.EffectRandom.PlayList(parts, 0, Ms2.Formats.World.EffectRandom.Tick, playParts))
        {
            if (part.TypeId == EffectSkipTable.TypeVibrate) { CameraShakes.Play(parts, part, EffectOwner.OfKind(CasterKind)); continue; }
            if (part.Type != "nif" || string.IsNullOrEmpty(part.FilePath)) continue;
            var sc = Scene(part.FilePath);
            if (sc == null) continue;
            float s = part.Scale <= 0 ? 1 : part.Scale;
            var q = new Quaternion(Vector3.Back, Mathf.DegToRad(part.Rotation[2])) * new Quaternion(Vector3.Up, Mathf.DegToRad(part.Rotation[1])) * new Quaternion(Vector3.Right, Mathf.DegToRad(part.Rotation[0]));
            var fx = new EffectInstance(sc, builder, part.Duration, seed++) { Position = at, Rotation = new Vector3(0, yawRadians, 0), Delay = part.StartDelay };
            fx.FileSpace = new Transform3D(new Basis(q).Scaled(new Vector3(s, s, s)), new Vector3(part.Translate[0], part.Translate[1], part.Translate[2]));
            AddChild(fx);
            Played++;
        }
    }

    EffectScene Scene(string nifPath)
    {
        string entry = nifPath.Replace('\\', '/').ToLowerInvariant();
        if (scenes.TryGetValue(entry, out var sc)) return sc;
        sc = null;
        try { if (packs.Exists(EffectArchive, entry)) sc = EffectScene.Load(NifFile.Load(builder.Spec, packs.Read(EffectArchive, entry))); }
        catch (Exception ex) { GD.PushWarning($"ground effect {entry}: {ex.Message}"); }
        return scenes[entry] = sc;
    }
}
