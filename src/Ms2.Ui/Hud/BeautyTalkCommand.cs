using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The NPC talk's beauty open-dialog actions (UITalkManager::FUN_141b1eb90, uitalkcinematic.cpp:1190-1196): "BeautyShopDialog"
/// (OpenBeautyShopDialog @0x141b1f920) and "BeautyStyleDialog" (OpenBeautyStyleDialog @0x141b1fa20, always dialog 0x4f03 type 4),
/// both ending in OpenTalkDialog @0x141b1faa0. The open itself is the flows' own Open; the talk's tail (SendNpcTalkMode0) is the host's
/// <see cref="EndTalk"/>. STILL-OPEN: SetPalette(npc) on the shop dialog 0x4eee; FindNpcData(npc) (only a talking npc is checked);
/// dialogs 0x4e23 (item colour) and 0x4e24 (mirror), whose windows are not ported, do nothing; the sub scene's "openDialog" message
/// and SetKeyGroup (the flows' Show hooks stand in for the sub scene).
/// </summary>
public sealed class BeautyTalkCommand
{
    public const string ShopDialog = "BeautyShopDialog", StyleDialog = "BeautyStyleDialog";
    public const int ShopId = 0x4eee, StyleId = 0x4f03, ItemColorId = 0x4e23, MirrorId = 0x4e24;

    public BeautyShopFlow Shop;
    public BeautyStyleFlow Style;
    /// <summary>The talking npc's object id (GetTalkNpcId); 0 when none.</summary>
    public System.Func<int> TalkNpcId = () => 0;
    /// <summary>SendBeauty's queue (FUN_140710590): the enter request.</summary>
    public System.Action<BeautyRequest> Send;
    /// <summary>SendNpcTalkMode0 (@0x141b1feb0): the talk ends.</summary>
    public System.Action EndTalk = () => { };
    public System.Action<string> Log = _ => { };

    /// <summary>True when <paramref name="dialog"/> is one of the two beauty actions (case-insensitive, CompareNoCase).</summary>
    public static bool Handles(string dialog) =>
        string.Equals(dialog, ShopDialog, System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(dialog, StyleDialog, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>GetDialogIdByKind @0x141b21e90.</summary>
    public static int DialogIdByKind(string kind, string mode)
    {
        if (Is(kind, "hair") || Is(kind, "makeUp") || Is(kind, "face") || Is(kind, "skin"))
            return Is(mode, "styleSave") ? StyleId : ShopId;
        if (Is(kind, "itemColor")) return ItemColorId;
        if (Is(kind, "mirror")) return MirrorId;
        return 0;
    }

    /// <summary>GetDialogType @0x141b21f70.</summary>
    public static int DialogType(int dialog, string mode, string kind)
    {
        if (dialog == ItemColorId || dialog == MirrorId) return 3;
        if (dialog == ShopId || dialog == StyleId)
        {
            if (string.IsNullOrEmpty(mode))
            {
                if (Is(kind, "skin")) return 3;
            }
            else
            {
                if (Is(mode, "random")) return 2;
                if (Is(mode, "styleSave")) return 4;
            }
            return 1;
        }
        return 0;
    }

    /// <summary>The talk action: false when <paramref name="dialog"/> is not a beauty action (the caller's other branches go on).</summary>
    public bool Run(string dialog, string args)
    {
        if (string.Equals(dialog, StyleDialog, System.StringComparison.OrdinalIgnoreCase)) { OpenTalkDialog(4, StyleId); return true; }
        if (!string.Equals(dialog, ShopDialog, System.StringComparison.OrdinalIgnoreCase)) return false;
        // Unk_SplitA(strArgs, ","): the kind, then the mode when there is a second part
        var parts = (args ?? "").Split(',');
        string kind = parts[0], mode = parts.Length > 1 ? parts[1] : "";
        int id = DialogIdByKind(kind, mode);
        OpenTalkDialog(DialogType(id, mode, kind), id);
        return true;
    }

    /// <summary>OpenTalkDialog @0x141b1faa0 (uitalkcinematic.cpp:1366-1400).</summary>
    void OpenTalkDialog(int mode, int dialogId)
    {
        int npc = TalkNpcId();
        if (npc == 0) return;   // FindNpcData(npc) null: nothing opens and nothing is sent (the tail's SendNpcTalkMode0 is skipped too: STILL-OPEN)
        bool opened = dialogId switch
        {
            ShopId => Shop?.Open() ?? false,
            StyleId => Style?.Open() ?? false,
            _ => false,   // 0x4e23 / 0x4e24 / 0: not ported
        };
        if (!opened) { Log($"beauty talk: dialog 0x{dialogId:x} did not open"); return; }
        if (mode != 0) Send?.Invoke(BeautyEnterRequest.Create(npc, (byte)mode));
        EndTalk();
    }

    static bool Is(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
}
