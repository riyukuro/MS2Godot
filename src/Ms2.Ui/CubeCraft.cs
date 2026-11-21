using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The local pc's cube mode and cube craft: PC::m_nCubeModeFFC, the craft object m_pCubeCraft (Unk_PCCubeCraft) and the
/// craft held back until the pc's model is in the scene (m_n1000, m_spCube1008, m_n1018).
/// <para>The mode is set by PC::vfunc_135 (@0x141659450, pc.cpp:3068), from SetCraftMode (CSceneStateGame::OnSetCraftMode
/// @0x141723410, scenestategame.cpp:7827) and from the pc's AddUser (scenestategame.cpp:11189-11192). A nonzero mode puts
/// the action key on its cube-mode actions (Unk_ActionKeyManager::GetActiveActionMap @0x140953770,
/// actionkeyobject.cpp:707-717).</para>
/// STILL-OPEN: what the mode change does besides the craft: the stance change (vfunc_59 5 or 1, pc.cpp:3072-3077), the
/// weapon effects (FUN_141659000, pc.cpp:2491), the housing service's state (FUN_1410cc160, pc.cpp:3083-3089), MyPC's
/// build HUD dialog 0x4e85 with its sounds and FUN_140ef4330 (mypc.cpp:1364-1397), and the target refresh after
/// SetCraftMode (scenestategame.cpp:7846-7851). What the craft does when it starts and ends (FUN_1410a8af0,
/// cubecraft_2.cpp:340: the cell in front of the pc, the guides, the scale pulse, the god mode info) is not ported.
/// Other pcs' cube modes are not kept.
/// </summary>
public sealed class CubeCraftState
{
    /// <summary>Unk_PCCubeCraft (ctor @0x1410a8550, cubecraft_2.cpp:282): the craft mode and the cube item.</summary>
    public sealed class Craft
    {
        /// <summary>m_nMode (GetMode @0x1410a8a90).</summary>
        public int Mode { get; }
        /// <summary>m_spItem: the CubeItemInfo the craft was made with.</summary>
        public CubeItemInfo Item { get; }
        /// <summary>FUN_1410a8ab0 (cubecraft_2.cpp:332): mode 2, the craft of a lifted cube.</summary>
        public bool HoldsLift => Mode == 2;
        public Craft(int mode, CubeItemInfo item) { Mode = mode; Item = item; }
    }

    /// <summary>m_nCubeModeFFC (GetCubeMode, pc.cpp:1842).</summary>
    public int CubeMode { get; private set; }
    /// <summary>IsInCubeMode (pc.cpp:1845).</summary>
    public bool InCubeMode => CubeMode != 0;
    /// <summary>m_pCubeCraft (GetCubeCraft, pc.cpp:1867).</summary>
    public Craft Current { get; private set; }
    /// <summary>The craft waiting for the model (FUN_141661880 with 1, pc.cpp:2627): m_n1000 set, m_n1018 and
    /// m_spCube1008.</summary>
    public Craft Pending { get; private set; }
    /// <summary>The pc's model is in the scene (FUN_141b6efc0 on the pc's entity gives a root).</summary>
    public bool ModelLoaded { get; private set; }

    /// <summary>A new pc (PC ctor, pc.cpp:640: mode 0, no craft, nothing pending, no model yet).</summary>
    public void NewPc()
    {
        CubeMode = 0;
        Current = null;
        Pending = null;
        ModelLoaded = false;
    }

    /// <summary>PC::vfunc_135 (@0x141659450, pc.cpp:3068): nothing on a dead pc; else the mode, then FUN_141659250 with the
    /// craft mode and the item.</summary>
    public void Set(int cubeMode, int craftMode, CubeItemInfo item, bool dead)
    {
        if (dead) return;
        CubeMode = cubeMode;
        SetCraft(craftMode, item);
    }

    /// <summary>
    /// PC::FUN_141659250 (@0x141659250, pc.cpp:3038): the old craft always goes. In cube mode a new craft is made at once
    /// when the model is loaded, else it waits for it (FUN_141661880). Leaving the cube mode does not drop a craft that is
    /// waiting (the else branch, pc.cpp:3056-3064), so PC::vfunc_12 still makes it.
    /// </summary>
    void SetCraft(int craftMode, CubeItemInfo item)
    {
        Current = null;
        if (!InCubeMode) return;
        if (!ModelLoaded) Pending = new Craft(craftMode, item);
        else Current = new Craft(craftMode, item);
    }

    /// <summary>MyPC::GetActiveCubeCraft (@0x141530290, mypc.cpp:4516): no craft for a dead pc or outside the cube mode;
    /// else the craft.</summary>
    public Craft Active(bool dead) => dead || !InCubeMode ? null : Current;

    /// <summary>Where Unk_PCCubeCraft::ConfirmPlacement stopped.</summary>
    public enum PlacementStop
    {
        /// <summary>No active craft (PutDownLiftableAction::vfunc_2 @0x14095ad50 does not call it).</summary>
        NoCraft,
        /// <summary>Cube mode 1 (PC::IsCubeMode1, pc.cpp:1848) without the pc's construction guide
        /// (GetConstructionGuide, pc.cpp:2102).</summary>
        NoConstructionGuide,
        /// <summary>Craft mode 1: the housing service's build notice <see cref="ModeOneNotice"/>.</summary>
        BuildNotice,
        /// <summary>The craft has no guide cube (m_spGuide).</summary>
        NoGuide,
    }

    /// <summary>The build notice ConfirmPlacement gives in craft mode 1 (ShowBuildNotice(0x39), cubecraft_2.cpp:945).</summary>
    public const int ModeOneNotice = 0x39;

    /// <summary>
    /// PutDownLiftableAction::vfunc_2 (@0x14095ad50, actionkeyobject.cpp) through GetActiveCubeCraft, then
    /// Unk_PCCubeCraft::ConfirmPlacement (@0x1410aad50, cubecraft_2.cpp:936-948), in its order: in cube mode 1 it returns
    /// without the pc's construction guide; in craft mode 1 it shows build notice 0x39 and returns; it returns without
    /// its guide cube.
    /// <para>Neither guide exists here: the construction guide is the pc's guide slot 0 and the guide cube is the
    /// CNifCubeGuide FUN_1410a8af0 loads (cubecraft_2.cpp:446-448), both not ported, so every press stops at one of
    /// these returns.</para>
    /// STILL-OPEN: ShowBuildNotice (@0x1410d6590, cubemapinformationservice_head.cpp:306: the notice sound and text) is
    /// not ported, so the caller shows nothing. Everything after the guide check (cubecraft_2.cpp:950-1063: the item data,
    /// the texture-mesh check, the attachable path FUN_1410aded0, the rotation from either guide snapped to 90 degrees,
    /// and the RequestCube sends 10 and 0xf) needs a guide and is not ported.
    /// </summary>
    public PlacementStop ConfirmPlacement(bool dead)
    {
        var craft = Active(dead);
        if (craft == null) return PlacementStop.NoCraft;
        const bool hasConstructionGuide = false;
        if (CubeMode == 1 && !hasConstructionGuide) return PlacementStop.NoConstructionGuide;
        if (craft.Mode == 1) return PlacementStop.BuildNotice;
        return PlacementStop.NoGuide;
    }

    /// <summary>PC::vfunc_12 (@0x1416515a0, pc.cpp:844-852): the model is in the scene; a waiting craft is made and the
    /// wait cleared.</summary>
    public void ModelAttached()
    {
        ModelLoaded = true;
        if (Pending == null) return;
        Current = Pending;
        Pending = null;
    }
}
