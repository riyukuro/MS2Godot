using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// The face texture a pc's face emotion shows: CTextureAnimation's target node (FindTargetNode, textureanimationdata.cpp:440-463,
/// the first node of that name under the actor's NIF root that is a render object) and the texture layer on its texturing
/// property (TextureLayer::vfunc_19, :599-606: the base texture and shader map 0, the dye control map).
/// </summary>
public interface IFaceTexture
{
    /// <summary>Whether the pc's model has a render object of that name.</summary>
    bool Has(string target);
    /// <summary>Shows a frame on the target: the base texture's entry and the control map's (empty: none). Returns false
    /// when the target has no texturing property, as ShowFrame (:371-395) then makes no layer.</summary>
    bool Show(string target, string baseEntry, string controlEntry);
    /// <summary>Removes the layer (Stop, :326-341): the target's own textures again.</summary>
    void Stop();
}

/// <summary>
/// A pc's face emotion, the PCBase members behind it (pcbase.cpp): m_b9d0 (<see cref="Enabled"/>, off on the avatar copies,
/// avatarpc.cpp:418), m_b9d1 (<see cref="TextKeys"/>), m_str9d8 (<see cref="Rest"/>), m_str9e0 (<see cref="Current"/>), the
/// timer m_timer9e8, the emotion group m_spD10 and the texture animation m_sp638 (CTextureAnimation,
/// textureanimationdata.cpp:280-484). The pc's update (PCBase::vfunc_53, :3726-3733) ticks it first (<see cref="Tick"/>):
/// when the texture animation is over, the rest face plays again, so a pc blinks through "default" for good once its face
/// model is on (<see cref="SetGroup"/>).
/// STILL-OPEN: the emotion's effects (the effect list played when nA is set, :3712-3720, and ClearEffects); the equip hide
/// of PC::vfunc_71 / vfunc_72 (pc.cpp:1951-1962, FUN_141659770); FUN_141679fa0 (the FA_Skin floats the optimizer reads);
/// other pcs; the emo_ text keys of an npc's body.
/// </summary>
public sealed class FaceEmotion
{
    readonly IFaceTexture face;
    readonly bool own;
    EmotionGroup group;
    long start = -1;            // m_timer9e8: the start in ms, -1 when not running

    // CTextureAnimation
    EmotionTextureAnimation anim;
    string dir = "";
    int state;                  // m_nState: 0 off, 1 waiting for its textures, 2 showing
    int waited;                 // m_nTime
    int frame = -1;             // m_nFrame
    bool layer;                 // m_spLayer
    HashSet<(EmotionTextureAnimation, string)> loaded;   // the frames this actor has waited for (AreAllFramesLoaded), null until the first wait

    /// <param name="own">The own pc (a MyPC): its frames show at once; another pc waits for its textures first.</param>
    public FaceEmotion(IFaceTexture face, bool own = true) { this.face = face; this.own = own; }

    /// <summary>m_b9d0: the face emotion plays at all.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>m_b9d1: the emo_ text keys of the body's sequences change the face. A trigger's face emotion turns it off
    /// until the face rests again.</summary>
    public bool TextKeys { get; private set; } = true;
    /// <summary>m_str9d8: the face the pc rests on, "default", or "defaultBattle" in battle (PC::vfunc_66).</summary>
    public string Rest { get; private set; } = "default";
    /// <summary>m_str9e0: the emotion last played.</summary>
    public string Current { get; private set; } = "";
    /// <summary>The texture animation's frame on the face, -1 when none shows.</summary>
    public int Frame => layer ? frame : -1;
    /// <summary>The group the face plays from (the face item's).</summary>
    public EmotionGroup Group => group;

    /// <summary>
    /// PCBase::FUN_14166f380 (@0x14166f380, pcbase.cpp:4247-4264), when a face model is on (CEquipItem::FUN_1411dd150,
    /// equipitem.cpp:472-480): the texture animation stops, the timer starts over, the group is the face item's (vfunc_69)
    /// and the emotion last played starts again on the new face.
    /// </summary>
    public void SetGroup(EmotionGroup g, long nowMs)
    {
        StopTexture();
        start = nowMs;
        group = g;
        if (group?.Find(Current) is { Texture: { } t }) StartTexture(t, group.ItemCode);
    }

    /// <summary>PCBase::vfunc_10 (@0x14166b550, pcbase.cpp:919), the actor's init: the group is vfunc_70's library's group of
    /// vfunc_69 (an npc's, <see cref="NpcRow.EmotionGroupId"/>); unlike <see cref="SetGroup"/> the timer does not
    /// start, so the face shows nothing until an emotion plays.</summary>
    public void UseGroup(EmotionGroup g) => group = g;

    /// <summary>
    /// PCBase::FUN_14166ef10 (@0x14166ef10, pcbase.cpp:3688-3722): plays the group's emotion of that name. Nothing without
    /// the flag, a group or the name; nothing when <paramref name="force"/> is off and it is the emotion last played.
    /// Otherwise the texture animation stops, the timer starts and the emotion's texture animation starts with the group's
    /// item code as its folder. Returns whether it played.
    /// </summary>
    public bool Play(string name, bool force, long nowMs)
    {
        if (!Enabled || group == null) return false;
        if (group.Find(name) is not { } data) return false;
        if (!force && Current == name) return false;
        StopTexture();                  // FUN_141679f30 (:3603-3609); the timer starts again just below
        Current = name;
        start = nowMs;                  // StartInfinite
        if (data.Texture != null) StartTexture(data.Texture, group.ItemCode);
        return true;
    }

    /// <summary>PCBase::FUN_14166f8f0 (@0x14166f8f0, pcbase.cpp:1910-1915): the text keys on, the rest face played.</summary>
    public void ToRest(bool force, long nowMs)
    {
        TextKeys = true;
        Play(Rest, force, nowMs);
    }

    /// <summary>
    /// The emo_ branch of PCBase::TextKeyEvent (@0x14166bf40, pcbase.cpp:1032-1039): a text key of the body's target
    /// sequence longer than 4 that starts with "emo_" plays the rest of the key while the text keys are on
    /// (FUN_14166ef10(name, 1, 0, in StateTriggerEmotion), so the emotion last played does not start over). Returns
    /// whether it played. The caller passes only the target sequence's keys.
    /// </summary>
    public bool TextKey(string key, long nowMs)
    {
        if (!TextKeys || key == null || key.Length <= 4 || !key.StartsWith("emo_", StringComparison.Ordinal)) return false;
        return Play(key[4..], false, nowMs);
    }

    /// <summary>
    /// The trigger's Ui sub 9 (CTriggerService::FUN_1418641e0 @0x1418641e0, triggerservice.cpp:1052-1068) on this pc: a
    /// name turns the text keys off (FUN_14166ed50(false)) and plays it (FUN_14166ef10(name, 1, 0, true)); an empty one
    /// rests the face (FUN_14166f8f0(0, 1)).
    /// </summary>
    public void Trigger(string name, long nowMs)
    {
        if (string.IsNullOrEmpty(name)) { ToRest(true, nowMs); return; }
        TextKeys = false;
        Play(name, false, nowMs);
    }

    /// <summary>
    /// PC::vfunc_66 (@0x1416544f0, pc.cpp:1235-1251) from PCBase::SetEmotion: the rest face is "defaultBattle" in battle and
    /// "default" out of it; a face resting on the other one switches.
    /// </summary>
    public void SetBattle(bool inBattle, long nowMs)
    {
        Rest = inBattle ? "defaultBattle" : "default";
        if (Current == (inBattle ? "default" : "defaultBattle")) Play(Rest, false, nowMs);
    }

    /// <summary>PCBase::FUN_14166f940 (@0x14166f940, pcbase.cpp:1574-1584): while the timer runs, the texture animation
    /// shows the frame of the time since the start; once it is over (or none plays) the face rests.</summary>
    public void Tick(long nowMs)
    {
        if (start < 0) return;
        if (!UpdateTexture((int)Math.Min(nowMs - start, int.MaxValue))) ToRest(true, nowMs);
    }

    /// <summary>CTextureAnimation::FUN_1418484a0 (@0x1418484a0, textureanimationdata.cpp:293-323).</summary>
    void StartTexture(EmotionTextureAnimation t, string folder)
    {
        anim = t;
        dir = folder ?? "";
        frame = -1;
        if (!face.Has(t.Target)) { state = 0; return; }
        // the own pc shows its first frame at once (its textures load as it asks for them); another actor shows it at once
        // when the frames are loaded (AreAllFramesLoaded, :303-309), here those it waited for before, else it waits
        if (own || loaded?.Contains((t, dir)) == true) { ShowFrame(0); state = 2; }
        else { waited = 0; state = 1; }
    }

    /// <summary>CTextureAnimation::Update (@0x141848740, textureanimationdata.cpp:344-367).</summary>
    bool UpdateTexture(int timeMs)
    {
        if (state == 0) return false;
        // waiting: m_nTime adds the time since the start at each update (so about ten updates at 60 fps, not a second) and
        // then checks that every frame is loaded; the port reads them as it shows them, so they are
        if (state == 1 && (waited += timeMs) >= 1000) { waited = 0; (loaded ??= new()).Add((anim, dir)); ShowFrame(0); state = 2; }
        int n = anim.FrameAt(timeMs);
        if (n >= anim.Frames.Count) { StopTexture(); return false; }
        if (!layer || n == frame) return true;
        ShowFrame(n);
        return true;
    }

    /// <summary>CTextureAnimation::ShowFrame (@0x141848890, textureanimationdata.cpp:371-396).</summary>
    void ShowFrame(int n)
    {
        frame = n;
        var f = anim.Frames[n];
        if (face.Show(anim.Target, f.BaseEntry(dir), f.ControlEntry(dir))) layer = true;
    }

    /// <summary>CTextureAnimation::Stop (@0x141848650, textureanimationdata.cpp:326-341).</summary>
    void StopTexture()
    {
        if (layer) { face.Stop(); layer = false; }
        waited = 0;
        frame = -1;
        state = 0;
        dir = "";
    }

    /// <summary>
    /// PC::vfunc_69 (@0x14165f480, pc.cpp:3094-3108): the face item whose group the pc plays from, the worn face item when its
    /// model has parts, else the gender's base face item (CItemDefaultData::FUN_140583f00, the slot FA base="1" item of
    /// table/defaultitems.xml for that gender) when its item data exists, else 0.
    /// </summary>
    public static int FaceItem(int wornFace, bool wornHasParts, PackSet packs, int gender)
    {
        if (wornFace != 0 && wornHasParts) return wornFace;
        return DefaultFace(packs, gender);
    }

    /// <summary>The gender's base face item of table/defaultitems.xml (0 male, 1 female; an item without a gender limit is
    /// either's), 0 when the data has none.</summary>
    public static int DefaultFace(PackSet packs, int gender)
    {
        if (!packs.Exists("Xml", "table/defaultitems.xml")) return 0;
        var doc = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", "table/defaultitems.xml"));
        var files = new Items.ItemFiles(packs);
        int found = 0;
        foreach (var slot in doc.Root.Elements("key").Elements("slot").Where(s => (string)s.Attribute("name") == "FA"))
            foreach (var it in slot.Elements("item").Where(i => (string)i.Attribute("base") == "1"))
            {
                if (!int.TryParse((string)it.Attribute("id"), out int id) || files.Item(id) is not { } data) continue;
                var limit = data.Descendants("limit").FirstOrDefault();
                int g = int.TryParse((string)limit?.Attribute("genderLimit"), out int v) ? v : 2;
                if (g == gender || g == 2) found = id;      // a later row sets the slot again (ms_aMaps)
            }
        return found;
    }
}
