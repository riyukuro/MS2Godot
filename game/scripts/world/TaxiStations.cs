using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2Godot.Net;


namespace Ms2Godot.World;

/// <summary>
/// The field's taxi station, as CSceneStateGame::FUN_141739450(bool bFlag) (@0x141739450, scenestategame.cpp:11876-11933) sets it up.
/// It takes the MS2TaxiStation entities (0x85359406, at most one per field: every field that places one places exactly one) and
/// decides whether the station is open:
/// <list type="bullet">
/// <item>CTaxiManager::FUN_14085f730(field) (taximanager.cpp:119, the field meta table's taxi IsHomePrivilege): open when the
/// player's house-taxi privilege (MyPC home privilege FUN_14050da30) holds;</item>
/// <item>otherwise open when the found-taxi list (DAT_143ab3078) holds the field.</item>
/// </list>
/// For each station it stops the running effect (StopTaxiEffect), and then:
/// <list type="bullet">
/// <item>bFlag: plays CommonResource EFFECT_TAXI_REVEAL (+0x380, ETC/Eff_Taxi_Reveal.xml) once at the station;</item>
/// <item>open: writes the cached properties back (FUN_141749ed0, the NifAsset again) and plays EFFECT_TAXI_DEFAULT (+0x360,
/// ETC/Eff_Taxi_Default.xml, the looping light shafts after its 2 s startDelay), kept as m_nTaxiEffect;</item>
/// <item>closed: when the entity has a DisabledAsset (0x78cb6d8d), caches its properties and sets NifAsset (0x770e5fc6) to it.</item>
/// </list>
/// Both effects play at the entity's position with an identity rotation, not attached.
/// Callers: CSceneStateGame::vfunc_1 (@0x14170d9f0, the field enter, scenestategame.cpp:2006) with false, here
/// <see cref="BeforeBuild"/> and <see cref="Start"/>; OnTaxi (@0x141731470, :7902-7925) with true, here <see cref="Found"/>;
/// OnResponseCube (@0x141723670, :10240-10241) in a house-privilege field, with its result. StopTaxiEffect (@0x141739880) runs
/// on the field leave (vfunc_4 @0x14170daf0, :2017): this node and its effects go with the field.
/// The found-taxi list is <see cref="Ui.StubHudData.FoundTaxis"/>: in live mode the ServerEnter packet's second int list
/// (OnServerEnter, scenestategame.cpp:2115-2122) plus each Taxi packet's ids (OnTaxi); offline it is the stub's reference-run
/// list (Tria's station found).
/// STILL-OPEN: the house-taxi privilege (FUN_14050da30) has no port, so a house-privilege field's station is taken as closed
/// (never as open), and OnResponseCube's re-run is not wired.
/// STILL-OPEN: a station opened by OnTaxi while in its field keeps its DisabledAsset model until the next field load (the
/// batched field model is not rebuilt); the reveal and default effects do play. OnTaxi's notice, system sound, minimap
/// EnableTaxiFrame and guide trigger 0x10 run on the HUD (<see cref="Ms2.Ui.TaxiFoundOpen"/>).
/// STILL-OPEN: OnTaxi ends with CAutoFindTaxiStation::FUN_1409f1970 (@0x1409f1970, autofindtaxistation.cpp:307), which arms a
/// 1500 ms timer for the auto-find camera fly-by (FUN_1409f1af0) when an auto-find is active and not done. The auto-find
/// (StartAutoFind, the camera fly-by and its Esc request) has no port, so the call is left out.
/// </summary>
public partial class TaxiStations : Node3D
{
    public const string DefaultEffect = "ETC/Eff_Taxi_Default.xml";   // commonResource.xml EFFECT_TAXI_DEFAULT
    public const string RevealEffect = "ETC/Eff_Taxi_Reveal.xml";     // commonResource.xml EFFECT_TAXI_REVEAL
    const string EffectArchive = "Resource/Model/Effect";
    const string Model = "MS2TaxiStation";

    readonly PackSet packs;
    readonly ModelBuilder builder;
    readonly EffectTable table;
    readonly List<FieldEntity> stations;
    readonly int fieldId;
    readonly Func<bool> open;
    readonly Dictionary<string, EffectScene> scenes = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Node3D> running = new();   // m_nTaxiEffect
    int seed;
    public int Played;

    TaxiStations(PackSet packs, ModelBuilder builder, List<FieldEntity> stations, int fieldId, Func<bool> open)
    {
        this.packs = packs; this.builder = builder; this.stations = stations; this.fieldId = fieldId; this.open = open;
        table = new EffectTable(packs);
        Name = "TaxiStations";
    }

    public static List<FieldEntity> Of(FieldMap map) => map.Entities.Where(e => e.HasMixin(Model)).ToList();

    /// <summary>The open test of FUN_141739450. <paramref name="homeTaxiField"/>: the field meta table's IsHomePrivilege for the
    /// field (null: the field has no taxi row, the found list decides); <paramref name="homePrivilege"/>: the player's house-taxi
    /// privilege (null: unknown, taken as not held).</summary>
    public static bool IsOpen(int fieldId, bool? homeTaxiField, ICollection<int> found, bool? homePrivilege) =>
        homeTaxiField == true ? homePrivilege == true : found != null && found.Contains(fieldId);

    public static bool IsOpen(PackSet packs, int fieldId)
    {
        bool? home = null;
        if (FieldMeta.EntryOf(packs) != null && QuestMapGraph.SharedMeta(packs).TaxiStations.TryGetValue(fieldId, out bool h)) home = h;
        return IsOpen(fieldId, home, Ui.StubHudData.Instance.FoundTaxis, null);   // STILL-OPEN: the house-taxi privilege
    }

    /// <summary>The closed branch, before the field build reads NifAsset: a closed station with a DisabledAsset draws that model.
    /// Returns the stations. The two models share the PhysX shape (co_fi_struc_station_a01(_off).nif), so the collision is the same.</summary>
    public static List<FieldEntity> BeforeBuild(FieldMap map, bool isOpen)
    {
        var list = Of(map);
        if (!isOpen)
            foreach (var e in list)
                if (e.Get("DisabledAsset") is { Length: > 0 } disabled) e.Raw.Props["NifAsset"] = disabled;
        return list;
    }

    /// <summary>The field enter's FUN_141739450(false), after the build: the open station's default effect.</summary>
    public static TaxiStations Start(PackSet packs, ModelBuilder builder, List<FieldEntity> stations, int fieldId)
    {
        var t = new TaxiStations(packs, builder, stations, fieldId, () => IsOpen(packs, fieldId));
        t.Run(false);
        return t;
    }

    public override void _EnterTree() { if (Net.LiveSession.Instance is { } ls) ls.TaxiFound += OnTaxi; }
    public override void _ExitTree() { if (Net.LiveSession.Instance is { } ls) ls.TaxiFound -= OnTaxi; }

    /// <summary>OnTaxi: the ids are already in the found list (the session adds them); bOpen runs FUN_141739450(true).</summary>
    void OnTaxi(Ms2.Net.Packets.TaxiFound p) { if (p.Open) Found(); }

    /// <summary>FUN_141739450(true): the reveal, then the default effect when the station is now open.</summary>
    public void Found() => Run(true);

    void Run(bool reveal)
    {
        bool isOpen = open();
        foreach (var e in stations)
        {
            StopTaxiEffect();
            var at = FieldBuilder.ToGodotPoint(e.Position);
            if (reveal) Play(RevealEffect, at, false);
            if (isOpen) Play(DefaultEffect, at, true);
        }
    }

    /// <summary>StopTaxiEffect (@0x141739880): frees m_nTaxiEffect.</summary>
    public void StopTaxiEffect()
    {
        foreach (var n in running) if (IsInstanceValid(n)) n.QueueFree();
        running.Clear();
    }

    /// <summary>The effect xml's parts at a Godot point, identity rotation: nif parts through the effect scene runtime (startDelay,
    /// duration; a looping part stays), sound events at the point.</summary>
    void Play(string xml, Vector3 at, bool keep)
    {
        var parts = table.Get(xml);
        if (parts == null) { GD.PushWarning($"taxi: no effect xml {xml}"); return; }
        foreach (var part in parts)
        {
            if (part.Type == "soundEvent" && part.SoundEvent != null)
            {
                FieldAudio.Instance?.PlayEventAt(part.SoundGroup == null ? part.SoundEvent : part.SoundGroup + "/" + part.SoundEvent, at, part.StartDelay);
                continue;
            }
            if (string.Equals(part.Type, "soundMusic", StringComparison.OrdinalIgnoreCase))
            {
                // CSceneStateGame::FUN_141739450 (@0x141739450, scenestategame.cpp:11909,11918): req.m_nObject = 0 (system).
                FieldAudio.Instance?.PlayEffectSoundMusic(part, Ms2.Audio.SoundMusicEffectState.CasterSystem);
                continue;
            }
            if (part.Type != "nif" || string.IsNullOrEmpty(part.FilePath) || Scene(part.FilePath) is not { } sc) continue;
            float s = part.Scale <= 0 ? 1 : part.Scale;
            var q = new Quaternion(Vector3.Back, Mathf.DegToRad(part.Rotation[2])) * new Quaternion(Vector3.Up, Mathf.DegToRad(part.Rotation[1])) * new Quaternion(Vector3.Right, Mathf.DegToRad(part.Rotation[0]));
            float duration = part.Loop ? 1e7f : part.Duration;
            var fx = new EffectInstance(sc, builder, duration, seed++) { Name = (keep ? "TaxiDefault_" : "TaxiReveal_") + seed, Position = at, Delay = part.StartDelay };
            fx.FileSpace = new Transform3D(new Basis(q).Scaled(new Vector3(s, s, s)), new Vector3(part.Translate[0], part.Translate[1], part.Translate[2]));
            AddChild(fx);
            if (keep) running.Add(fx);
            Played++;
        }
        GD.Print($"taxi: {xml} at field {fieldId}");
    }

    EffectScene Scene(string nifPath)
    {
        string entry = nifPath.Replace('\\', '/').ToLowerInvariant();
        if (scenes.TryGetValue(entry, out var sc)) return sc;
        sc = null;
        try { if (packs.Exists(EffectArchive, entry)) sc = EffectScene.Load(NifFile.Load(builder.Spec, packs.Read(EffectArchive, entry))); }
        catch (Exception ex) { GD.PushWarning($"taxi effect {entry}: {ex.Message}"); }
        return scenes[entry] = sc;
    }
}
