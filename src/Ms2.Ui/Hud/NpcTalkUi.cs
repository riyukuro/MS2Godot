using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The dialog an npc opens on a talk without a talk script. UITalkManager's talk start (FUN_141b1ff80,
/// uitalkcinematic.cpp:1456-1477) opens the npc's own dialog (CNpc::OpenTalkUi) when FindTalkScript finds no script and
/// <see cref="UiIdOf"/> is not 0. ResetTalk (:1507) closes it again (CNpc::CloseTalkUi).
/// </summary>
public static class NpcTalkUi
{
    /// <summary>The dialogs of <see cref="UiIdOf"/> besides the shop and the bank.</summary>
    public const int RepairId = 0x4e4a, BlackMarketId = 0x4e97, BirthdayId = 0x4f55, FameId = 0x4f50;

    /// <summary>
    /// CNpc::GetTalkUiId (@0x141094170, unit_14108e1e0_npcactorandvibrate.cpp:1327) of the npc's type (its kind): 1 the
    /// shop 0x4e3b, 2 the bank 0x4e47, 3 0x4e4a, 0x56 0x4e97, 0x58 0x4f55; another kind that is some fame def's officer npc
    /// kind (CFameTable::IsOfficerNpc, FUN_14035b0a0) 0x4f50; else 0 (none).
    /// </summary>
    public static int UiIdOf(int npcKind, Func<int, bool> isOfficerNpc) => npcKind switch
    {
        3 => RepairId,
        2 => StoreDialogController.UiId,
        1 => Shop.ShopController.UiId,
        0x56 => BlackMarketId,
        0x58 => BirthdayId,
        _ => isOfficerNpc != null && isOfficerNpc(npcKind) ? FameId : 0,
    };

    /// <summary>
    /// CNpc::OpenTalkUi's bank branch (@0x141094200, unit_14108e1e0_npcactorandvibrate.cpp:1350-1366): a bank that is not
    /// ready (CStore::Ready) only gets its Load request (CStore::RequestLoad) and nothing is opened: the server's answer opens
    /// it (CStore::OnReset / OnBegin). A ready bank is Reset (CStore::Reset) and its dialog opened (OpenDialog(0x4e47), whose
    /// OnLoadInit asks for the now not ready bank again).
    /// </summary>
    public static void OpenStore(StorageBook book, Action requestLoad, Action openDialog)
    {
        if (!book.Ready)
        {
            requestLoad();
            return;
        }
        book.Reset();
        openDialog();
    }
}
