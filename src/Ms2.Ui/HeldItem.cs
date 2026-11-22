using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>
/// The item the local pc holds (PC::m_spItem1040, GetHeldItem), set and let go by PC::FUN_14165f1f0 (@0x14165f1f0,
/// pc.cpp:3434-3460). While one is held the action key does nothing (Unk_ActionKeyManager::ActivateCurrentAction,
/// actionkeyobject.cpp:619-620).
/// STILL-OPEN: StateSkill (stateskill.cpp:483) lets the item go without a send when the skill has m_bReleaseObjectWeapon;
/// the port has no skill state yet.
/// STILL-OPEN: StateGlide, StateEmotion and StateTriggerEmotion have no port, so nothing calls <see cref="EnterState"/>
/// with them yet; they are listed for when they come.
/// Each other pc has its own (the game's RemoteLayer: AddUser, ResponseCube for its id).
/// STILL-OPEN: the held item's model (AvatarPC::vfunc_127, AddItemAndPreloadModels), for any pc.
/// </summary>
public sealed class HeldItem
{
    /// <summary>The held item's id, 0 when none is held.</summary>
    public int ItemId { get; private set; }
    public bool Held { get; private set; }

    /// <summary>FUN_14165f1f0(itemId, 1) (:3436-3447): the item is made from the item data (CItem::FUN_140523c40); when that
    /// fails nothing changes, else it becomes the held item.</summary>
    public bool Hold(int itemId, bool itemExists)
    {
        if (!itemExists) return false;
        ItemId = itemId;
        Held = true;
        return true;
    }

    /// <summary>FUN_14165f1f0(0, 0) (:3448-3458): the held item, if any, is let go.</summary>
    public void Release()
    {
        ItemId = 0;
        Held = false;
    }

    /// <summary>PC::vfunc_12 (pc.cpp:853-855): the AddUser i32 (PC::FUN_14165f090 = m_n1038, scenestategame.cpp:11218) is
    /// held when nothing is held and it isn't 0.</summary>
    public bool AddUser(int heldObject, bool itemExists)
    {
        if (Held || heldObject == 0) return false;
        return Hold(heldObject, itemExists);
    }

    /// <summary>A state's vfunc_4 for the local pc (FUN_1417b84b0): the states that start with PC::SendRequestCube.
    /// Rope, Climb, Ladder and the emotions test GetHeldItem first, Ride, Dead and Glide don't; SendRequestCube tests it
    /// itself, so the result is the same. Every other state sends nothing.</summary>
    public bool EnterState(HeldItemState state, Action<IOutPacket> send) => state switch
    {
        HeldItemState.Ride => SendRequestCube(send),            // StateRide::vfunc_4 (statemanager.cpp:1095)
        HeldItemState.Rope => SendRequestCube(send),            // StateRope::vfunc_4 (statemanager.cpp:1238-1239)
        HeldItemState.Climb => SendRequestCube(send),           // StateClimb::vfunc_4 (state.cpp:1040-1041)
        HeldItemState.Ladder => SendRequestCube(send),          // StateLadder::vfunc_4 (stateladder.cpp:163-164)
        HeldItemState.Dead => SendRequestCube(send),            // StateDead::vfunc_4 (statedead.cpp:152)
        HeldItemState.Glide => SendRequestCube(send),           // StateGlide::vfunc_4 (stateglide.cpp:140), past its overlap return
        HeldItemState.Emotion => SendRequestCube(send),         // StateEmotion::vfunc_4 (stateemotionidle.cpp:214-215)
        HeldItemState.TriggerEmotion => SendRequestCube(send),  // StateTriggerEmotion::vfunc_4 (stateemotionidle.cpp:390-391)
        _ => false,
    };

    /// <summary>The PC state ids (PC ctor, pc.cpp:675-697) whose state is in <see cref="HeldItemState"/>: 8 ladder, 9 rope,
    /// 0xc dead, 0x1d climb, 0x21 ride. STILL-OPEN: 0xb emotion and 0x1e glide map to Other until those states are
    /// ported (their vfunc_4 also calls SendRequestCube); TriggerEmotion's id is not registered in that ctor.</summary>
    public static HeldItemState ForStateId(int state) => state switch
    {
        8 => HeldItemState.Ladder,
        9 => HeldItemState.Rope,
        0xc => HeldItemState.Dead,
        0x1d => HeldItemState.Climb,
        0x21 => HeldItemState.Ride,
        _ => HeldItemState.Other,
    };

    /// <summary>OnUserState (@0x141721940, scenestategame.cpp:5759-5771) for the local pc: vfunc_59(state) enters the
    /// state, so its vfunc_4 runs (<see cref="EnterState"/>). State 12 also needs the pc dead (MyPC::vfunc_137,
    /// mypc.cpp:1346-1348). Returns whether 0x12 was sent.</summary>
    public bool UserState(int state, bool dead, Action<IOutPacket> send)
    {
        if (state == 0xc && !dead) return false;
        return EnterState(ForStateId(state), send);
    }

    /// <summary>
    /// OnResponseCube's held-item modes for the pc whose object id is <paramref name="pcId"/> (FindPCById: a packet for
    /// another id is not this pc's):
    /// <list type="bullet">
    /// <item>0x11, error 0 (scenestategame.cpp:10149-10165): GetCubeByKey(cube key); a cube with a weapon takes the value
    /// and the pc holds the item (FUN_14165f1f0(item, 1)). <paramref name="cubeHasWeapon"/> is that lookup; null: the cube
    /// map's weapons are not known, so nothing is held.</item>
    /// <item>0x12, error 0 (:10179-10186): the pc's held item is let go.</item>
    /// </list>
    /// STILL-OPEN: the cube weapon's value (m_n1c, CubeMapInformationService::FUN_1410caf20) and the cube map's weapon
    /// components have no port (the Liftable side).
    /// </summary>
    public bool ResponseCube(ResponseCubeHeldItem m, int pcId, Func<int, bool> cubeHasWeapon, bool itemExists)
    {
        if (m == null || m.Error != 0 || m.Id != pcId) return false;
        if (m.Mode == ResponseCubeHeldItem.ModeRelease) { Release(); return true; }
        if (m.Mode == ResponseCubeHeldItem.ModeHold && cubeHasWeapon != null && cubeHasWeapon(m.CubeKey))
            return Hold(m.ItemId, itemExists);
        return false;
    }

    /// <summary>PC::SendRequestCube (@0x14165f170, pc.cpp:1929-1937): nothing without a held item; else it is let go and
    /// RequestCube 0x12 is sent.</summary>
    public bool SendRequestCube(Action<IOutPacket> send)
    {
        if (!Held) return false;
        Release();
        send(new HeldItemReleaseRequest());
        return true;
    }
}

/// <summary>The local pc's states whose vfunc_4 calls PC::SendRequestCube (see <see cref="HeldItem.EnterState"/>).</summary>
public enum HeldItemState { Other, Ride, Rope, Climb, Ladder, Dead, Glide, Emotion, TriggerEmotion }
