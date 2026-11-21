using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>
/// The item the local pc holds (PC::m_spItem1040, GetHeldItem), set and let go by PC::FUN_14165f1f0 (@0x14165f1f0,
/// pc.cpp:3434-3460). While one is held the action key does nothing (Unk_ActionKeyManager::ActivateCurrentAction,
/// actionkeyobject.cpp:619-620).
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
