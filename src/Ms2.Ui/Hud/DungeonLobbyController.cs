using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the dungeon lobby notice, UIDungeonLobby (client/ms2/uidungeonlobby.cpp, 0x140e176a0-0x140e186e0; UI id 0x4f21,
/// type name "DungeonLobby") for its movie uidungeonlobby: a title (the room's group and name), a sub title (the lobby's second value) and a
/// web guide button. The packet DungeonWaiting (opcode 0xF0, <see cref="DungeonWaiting"/>) reaches OnRoomPacket (@0x140e17930, :68), which
/// stores the room id and the second value and opens the dialog; OnLoadComplete (@0x140e17990, :77) fills the texts from them.
/// STILL-OPEN: UIDialog::BindWebGuideButton (@0x141926180) needs the WebGuideTable (FUN_141927640, whether the dialog's type name has a
/// guide page) and the browser it opens, neither is here, so the button is hidden as the client hides it for a name without a page; the
/// lobby's size is not refreshed by a second packet while the dialog stays open (the client sets the two members and the texts stay).
/// </summary>
public sealed class DungeonLobbyController : UiController
{
    public const int UiId = 0x4f21;
    /// <summary>OnLoadComplete: the web guide button sits half the title's width and this many pixels right of its place (fOffset).</summary>
    public const double WebGuideOffset = 10.0;

    public override string Movie => "uidungeonlobby";
    protected override string EntityMethod => "alignment";
    /// <summary>GetTypeName (@0x140e178e0, :61).</summary>
    public override string TypeName => "DungeonLobby";

    /// <summary>m_nRoomId (+0x178) and the second value (+0x17c): <see cref="OnRoomPacket"/> sets them before the dialog opens.</summary>
    public int RoomId { get; private set; }
    public int Size { get; private set; }

    /// <summary>OnRoomPacket (@0x140e17930, :68): the two members; the owner opens the dialog (FUN_141924680) after it.</summary>
    public void OnRoomPacket(DungeonWaiting packet)
    {
        RoomId = packet.RoomId;
        Size = packet.Size;
    }

    /// <summary>
    /// OnLoadComplete (@0x140e17990, :77): UIDialog's; the m_webGuideButton and m_title must exist (the client throws a SwfLoad error);
    /// no title without the room in the table; else the title from s_dungeonLobby_title (the group name, the room's name from string table
    /// 0x14), the sub title from s_dungeonLobby_subTitle, and for a raid or chaos raid (groups 2 and 3) the web guide button right of the
    /// title, for another group the button hidden.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        var web = Host.Child("m_webGuideButton");
        var title = Host.Child("m_title");
        if (web == null || title == null) { Host.RT.NoteMissing("dungeon lobby: m_webGuideButton or m_title"); return; }
        Host.SetVisible(web, false);                                    // BindWebGuideButton: STILL-OPEN, no WebGuideTable
        var room = Model.Tables.DungeonRooms.GetRoom((uint)RoomId);
        if (room == null) return;
        string group = room.GroupType > 0 && room.GroupType < AchieveController.DungeonGroupKeys.Length
            ? Model.Tables.Common("s_enum_dungeon_group_" + AchieveController.DungeonGroupKeys[room.GroupType]) ?? "" : "";
        string name = Model.Tables.FieldEnteranceName((int)room.Id) ?? "";
        SetText(title, ClientFormat.Format(Model.Tables.Common("s_dungeonLobby_title") ?? "", group, name));
        if (Host.Child("m_subTitle") is { } sub)
            SetText(sub, ClientFormat.Format(Model.Tables.Common("s_dungeonLobby_subTitle") ?? "", (uint)Size));
        if (room.GroupType == 2 || room.GroupType == 3)
        {
            Host.SetVisible(web, true);
            double x = Rt.ToNumber(Host.Get(web, "x")), y = Rt.ToNumber(Host.Get(web, "y"));
            x += TextWidth(title) / 2.0 + WebGuideOffset;
            Host.Set(web, "x", x); Host.Set(web, "y", y);
        }
    }

    void SetText(object clip, string text) { if (Field(clip) is { } f) Host.Set(f, "text", text); }
    object Field(object o) => Host.Has(o, "htmlText") ? o : Host.Child("textField", o);
    double TextWidth(object clip) => Field(clip) is { } f ? Rt.ToNumber(Host.Get(f, "textWidth")) : 0;
}
