namespace Ms2.Net.Packets;

/// <summary>
/// The shop mode numbers, both ways, on both clients. The KMS client's shop packet is a u8 mode and a body (receive: UIShopDialog's
/// handler of RecvOp 0x51, OnShopPacket @0x141af8710, uishopdialog.cpp:1524, GMS2 0x52; send: SendOp 0x28). The GMS2 server (upstream
/// ShopPacket.Command / ShopHandler.Command) numbers every mode the same, so unlike the guild modes there is nothing to translate:
/// <see cref="Send"/> and <see cref="Receive"/> hold for both profiles, and the two differ only in bodies (the product entry, and the
/// buy request without the shop id; see <see cref="ShopProduct"/> and <see cref="ShopBuyRequest"/>).
/// <para>The client's receive switch has cases 0 1 2 4 6 7 8 9 11 12 13 14 15; modes 3, 5 and 10 are not read (the switch's
/// default). The GMS2 server writes 0 1 2 4 6 7 8 9 14 15 only (no merat 11/12, no new-start 13), and reads 3 4 5 9 10 (13 "possibly
/// unable to function in game"); every number it names agrees with the KMS row.</para>
/// </summary>
public static class ShopModes {
    // ---- receive (UIShopDialog::OnShopPacket) ----
    /// <summary>ReadShopInfo @0x141af88d0: the header (GMS2: Open, which prefixes the npc id; same body).</summary>
    public const byte RecvInfo = 0;
    /// <summary>ReadProducts @0x141af90f0: u8 count and the product entries (GMS2: LoadItems).</summary>
    public const byte RecvProducts = 1;
    /// <summary>ReadProductUpdate @0x141af98d0: i32 serial, i32 buy count (GMS2: Update).</summary>
    public const byte RecvProductUpdate = 2;
    /// <summary>ReadPurchaseResult @0x141afa2f0 (GMS2: Buy).</summary>
    public const byte RecvPurchaseResult = 4;
    /// <summary>ReadRepurchaseStart @0x141af9ce0: u16 count (GMS2: BuyBackItemCount).</summary>
    public const byte RecvRepurchaseStart = 6;
    /// <summary>ReadRepurchaseAdd: u16 count and the repurchase entries (GMS2: LoadBuyBack).</summary>
    public const byte RecvRepurchaseAdd = 7;
    /// <summary>ReadRepurchaseRemoval: i32 serial (GMS2: RemoveBuyBack).</summary>
    public const byte RecvRepurchaseRemoval = 8;
    /// <summary>ReadResetResult @0x141af9ad0 (GMS2: InstantRestock).</summary>
    public const byte RecvResetResult = 9;
    /// <summary>The merat shop's count: u16 (KMS only; the GMS2 server never sends it).</summary>
    public const byte RecvMeratStart = 11;
    /// <summary>ReadMeratItems @uishopdialog.cpp:1722 (KMS only).</summary>
    public const byte RecvMeratItems = 12;
    /// <summary>ReadNewStart @0x141af9390 (KMS only).</summary>
    public const byte RecvNewStart = 13;
    /// <summary>ReadNewProducts @0x141af94e0: u8 count and entries (GMS2: LoadNew).</summary>
    public const byte RecvNewProducts = 14;
    /// <summary>ReadShopError @0x141afa640: i32 error, u8 command, i32 value (GMS2: Error).</summary>
    public const byte RecvError = 15;

    // ---- send (SendOp::Shop 0x28) ----
    /// <summary>RequestRepurchase @0x141affeb0, uishopdialog.cpp:2826 (GMS2 PurchaseBuyBack).</summary>
    public const byte SendRepurchase = 3;
    /// <summary>SendPurchase @0x141b00060, uishopdialog.cpp:2846 (GMS2 Buy: no shop id).</summary>
    public const byte SendBuy = 4;
    /// <summary>RequestSellItem @0x141425fc0, itemugcmesh.cpp:1097 (GMS2 Sell).</summary>
    public const byte SendSell = 5;
    /// <summary>ResetList @0x141afe8b0, uishopdialog.cpp:2550 (GMS2 InstantRestock).</summary>
    public const byte SendReset = 9;
    /// <summary>vfunc_72 @0x141af3460, uishopdialog.cpp:750, 762 (GMS2 Refresh).</summary>
    public const byte SendRequestList = 10;
    /// <summary>FillNewProducts @0x141afc9d0, uishopdialog.cpp:2270 (GMS2 LoadNew).</summary>
    public const byte SendRequestNewItems = 13;

    /// <summary>Every receive mode the client's switch reads.</summary>
    public static readonly byte[] Receive = { 0, 1, 2, 4, 6, 7, 8, 9, 11, 12, 13, 14, 15 };
    /// <summary>Every send mode the client writes.</summary>
    public static readonly byte[] Send = { 3, 4, 5, 9, 10, 13 };
}

/// <summary>
/// The system shop's type numbers (SendOp::SystemShop, CNetworkService::SendSystemShop @0x141538c40, networkservice.cpp:530: u8 type, u8
/// state; RecvOp::SystemShop, CSceneStateGame::OnSystemShop @0x141733660: u8 type, bool ok). The client asks through ToggleShopTab
/// (unit_141347b10.cpp:468) with the tab its caller passes: rank pvp 3 (uirankpvpdialog.cpp:432), the fishing pane 4 (the pane's mode,
/// uisystemshopdialog.cpp:257), the mentoring reward page 6 mentee and 7 mentor (uimentoringrewardpage.cpp:85), the merat market's
/// star shop 8 (uimeratmarketdialog.cpp:4141), the game menu and the lullu box 9 (uigamemenudialog.cpp:2209,
/// uiitemlulluboxdialog.cpp:331), and 10 for a coin shop opened by an item (<see cref="SystemShopCoinRequest"/>). The GMS2 server
/// (upstream SystemShopHandler.Command) numbers Arena 3, Fishing 4, Mentee 6, Mentor 7, Item 10 the same and has no 8 or 9, so a
/// GMS2 server never answers those two.
/// </summary>
public static class SystemShopTypes {
    public const byte Arena = 3;
    public const byte Fishing = 4;
    public const byte Mentee = 6;
    public const byte Mentor = 7;
    public const byte StarPoint = 8;
    public const byte Style = 9;
    public const byte Item = 10;

    /// <summary>Every type the client sends.</summary>
    public static readonly byte[] All = { Arena, Fishing, Mentee, Mentor, StarPoint, Style, Item };
    /// <summary>The types the GMS2 server answers (its SystemShopHandler cases).</summary>
    public static readonly byte[] Gms2Server = { Arena, Fishing, Mentee, Mentor, Item };
}
