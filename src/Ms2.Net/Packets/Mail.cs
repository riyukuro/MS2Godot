using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// One attachment as the CMailAttachItem(CInPacket&amp;) ctor reads it (mailattachitem.cpp:87): i32 item id, i64 uid, u8 the
/// attachment slot (the client sets it as the item's slot, and GetItemId returns that slot: SetAttachment keys on it), i32
/// rarity (FUN_140523c40's second argument), i32 amount (SetCountClamped), i64 the receive time (m_nTime, 0 while it waits),
/// i32 m_n20, i64 m_n28 (their meaning is STILL-OPEN), then the item body (ReadFromStream). The client's locals there are named
/// by guess (nRarity, nAmount, nEnchant); the order of use settles them. STILL-OPEN: the client reads the body only when the
/// item id is known (FUN_140523c40 succeeds), so an unknown id would leave the rest of the packet unread; here the body is
/// always read.
/// </summary>
public sealed class MailAttachment {
    public int ItemId; public long Uid; public byte Slot; public int Rarity; public int Amount;
    public long ReceiveTime;            // m_nTime
    public int Value20; public long Value28;
    public EquipItem Item;

    public static MailAttachment Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static MailAttachment Read(ByteReader r, ProtocolProfile profile) {
        var a = new MailAttachment {
            ItemId = r.ReadInt(), Uid = r.ReadLong(), Slot = r.ReadByte(), Rarity = r.ReadInt(), Amount = r.ReadInt(),
            ReceiveTime = r.ReadLong(), Value20 = r.ReadInt(), Value28 = r.ReadLong(),
        };
        a.Item = ItemReader.ReadItem(r, a.ItemId, a.Rarity, profile);
        return a;
    }
}

/// <summary>
/// One product of an ad mail (kind 200), CMailAdItem::Read (mailattachitem.cpp:130): i32 m_n18 (the item id), i32 m_n1c, i32
/// m_n20 (the rarity), i32 m_n24, i64 m_n28, i64 m_n30 (the price; FUN_140692510 sums it), i64 m_n38 (the sale price;
/// FUN_1406925a0). The meaning of m_n1c, m_n24 and m_n28 is STILL-OPEN.
/// </summary>
public sealed record MailAd(int ItemId, int Value1c, int Rarity, int Value24, long Value28, long Price, long SalePrice) {
    public static MailAd Read(ByteReader r) =>
        new(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadLong(), r.ReadLong(), r.ReadLong());
}

/// <summary>Unk_MailTimeRange (mailattachitem.cpp:190): i64 begin, i64 claim time; a money amount that waits while claim is 0.</summary>
public sealed class MailRange {
    public long Begin, Claim;
    public bool IsClaimed => Begin > 0 && Claim != 0;
    public bool IsUnclaimed => Begin > 0 && Claim == 0;

    /// <summary>TryClaim: sets the claim time once.</summary>
    public bool TryClaim(long claim) {
        if (Claim != 0) return false;
        Claim = claim;
        return true;
    }

    public static MailRange Read(ByteReader r) => new() { Begin = r.ReadLong(), Claim = r.ReadLong() };
}

/// <summary>
/// One benefit, Unk_CommonPriceRecord::Read (unit_14026a1b0_commonstructpriceconfigs.cpp): the price (u8 type, u8 sub type,
/// i64 amount), then i64 the time it was used. IsAvailable: amount &gt; 0 and not used.
/// </summary>
public sealed class MailBenefit {
    public byte Type, Sub; public long Amount, UsedTime;
    public bool IsAvailable => Amount > 0 && UsedTime == 0;
    /// <summary>IsUsed (@0x14026a170): an amount above 0 and a use time.</summary>
    public bool IsUsed => Amount > 0 && UsedTime != 0;
    /// <summary>GetAvailableAmount (@0x14026a1f0): the amount while unused, any sign.</summary>
    public long AvailableAmount => UsedTime != 0 ? 0 : Amount;

    public static MailBenefit Read(ByteReader r) => new() { Type = r.ReadByte(), Sub = r.ReadByte(), Amount = r.ReadLong(), UsedTime = r.ReadLong() };
}

/// <summary>
/// One mail, CMailInfo (mailattachitem.cpp), as CMailInfo::Read (:590) reads it: u8 kind, i64 id, i64 the sender's id (m_n28),
/// the sender (m_str30), title (m_str48), content (m_str50), title args (m_str58) and content args (m_str60) as unicode
/// strings; kind 200 (an ad) then u8 n × <see cref="MailAd"/>, the product name (m_str120), i64 the ad's end (m_n128) and u8
/// sold (m_n130); any other kind u8 n × <see cref="MailAttachment"/> by slot 0..4. Then the three money ranges (m_rangeB0 meso,
/// m_rangeC0 merets, m_rangeD0 red merets: STILL-OPEN which meret is which), the benefits (u8 n × <see cref="MailBenefit"/>),
/// i64 read time (m_n100), i64 expiry (m_n108), i64 sent time (m_n118) and m_str68. The text resolution Read does after
/// that (the system mail templates, the reserved words and MailText) needs the client's tables and is the UI side's
/// (MailBook). The time stamps are server seconds.
/// </summary>
public sealed class MailInfo {
    public byte Kind; public long Id; public long SenderId;
    public string Sender = "", Title = "", Content = "", TitleArgs = "", ContentArgs = "", Text68 = "";
    public readonly MailAttachment[] Attachments = new MailAttachment[5];      // m_attach, by slot
    public readonly MailAd[] Ads = new MailAd[5];                              // m_ads
    public string AdName = "";                  // m_str120
    public long AdEnd;                          // m_n128
    public byte AdSold = 1;                     // m_n130: the ctor sets 1
    public MailRange Meso = new(), Meret = new(), RedMeret = new();           // m_rangeB0, m_rangeC0, m_rangeD0
    public readonly List<MailBenefit> Benefits = new();
    public long ReadTime, ExpireTime, SentTime; // m_n100, m_n108, m_n118
    public int State;                           // m_nState: SetState(1) on any change

    public static MailInfo Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static MailInfo Read(ByteReader r, ProtocolProfile profile) {
        var m = new MailInfo { Kind = r.ReadByte(), Id = r.ReadLong(), SenderId = r.ReadLong() };
        m.Sender = r.ReadUnicodeString(); m.Title = r.ReadUnicodeString(); m.Content = r.ReadUnicodeString();
        m.TitleArgs = r.ReadUnicodeString(); m.ContentArgs = r.ReadUnicodeString();
        if (m.Kind == 200) {
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) {
                var ad = MailAd.Read(r);
                if (i < 5) m.Ads[i] = ad;            // SetAd ignores a slot past 4
            }
            m.AdName = r.ReadUnicodeString(); m.AdEnd = r.ReadLong(); m.AdSold = r.ReadByte();
        } else {
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) {
                var a = MailAttachment.Read(r, profile);
                if (a.Slot < 5) m.Attachments[a.Slot] = a;     // SetAttachment(GetItemId(), ...)
            }
        }
        m.Meso = MailRange.Read(r); m.Meret = MailRange.Read(r); m.RedMeret = MailRange.Read(r);
        int nb = r.ReadByte();
        for (int i = 0; i < nb; i++) m.Benefits.Add(MailBenefit.Read(r));
        m.ReadTime = r.ReadLong(); m.ExpireTime = r.ReadLong(); m.SentTime = r.ReadLong();
        m.Text68 = r.ReadUnicodeString();
        return m;
    }

    /// <summary>IsRead (@0x140691130).</summary>
    public bool IsRead => ReadTime != 0;

    /// <summary>IsExpired: the server time has reached the expiry.</summary>
    public bool IsExpired(long now) => now >= ExpireTime;

    /// <summary>FUN_140692630: an ad whose end has passed.</summary>
    public bool AdExpired(long now) => AdEnd != 0 && now > AdEnd;

    /// <summary>
    /// HasAttachment: an attachment not yet received, a money range not claimed or a benefit not used.
    /// </summary>
    public bool HasAttachment() =>
        Attachments.Any(a => a != null && a.ReceiveTime == 0) || Meso.IsUnclaimed || Meret.IsUnclaimed || RedMeret.IsUnclaimed
        || Benefits.Any(b => b.IsAvailable);

    /// <summary>FUN_140690b80: something to take: what <see cref="HasAttachment"/> tests, or an ad on sale not yet bought.</summary>
    public bool HasClaimable(long now) =>
        Meso.IsUnclaimed || Meret.IsUnclaimed || RedMeret.IsUnclaimed || Benefits.Any(b => b.IsAvailable)
        || Attachments.Any(a => a != null && a.ReceiveTime == 0) || (Kind == 200 && AdEnd > now && AdSold == 0);

    /// <summary>FUN_1406909b0: something was already taken (a claimed range, a used benefit, a received attachment, a sold ad).</summary>
    public bool HasClaimed() =>
        Meso.IsClaimed || Meret.IsClaimed || RedMeret.IsClaimed || Benefits.Any(b => b.IsUsed)
        || Attachments.Any(a => a != null && a.ReceiveTime > 0) || (Kind == 200 && AdSold == 1);

    /// <summary>GetUnreceivedAttachmentCount.</summary>
    public int UnreceivedAttachmentCount => Attachments.Count(a => a != null && a.ReceiveTime <= 0);

    /// <summary>FUN_140692510 / FUN_1406925a0: the ads' prices and sale prices, up to the first empty slot.</summary>
    public long AdPrice => Ads.TakeWhile(a => a != null).Sum(a => a.Price);
    public long AdSalePrice => Ads.TakeWhile(a => a != null).Sum(a => a.SalePrice);

    /// <summary>SetAttachmentReceiveTime: the slot's attachment, once.</summary>
    public void SetAttachmentReceiveTime(int slot, long time) {
        if (slot < 0 || slot >= 5 || Attachments[slot] is not { } a || a.ReceiveTime != 0) return;
        a.ReceiveTime = time;
        State = 1;
    }

    public void ClaimMeso(long time) { if (Meso.TryClaim(time)) State = 1; }               // ClaimRangeB0
    public void ClaimMeret(long time) { if (Meret.TryClaim(time)) State = 1; }             // ClaimRangeC0
    public void ClaimRedMeret(long time) { if (RedMeret.TryClaim(time)) State = 1; }       // FUN_140690e80

    /// <summary>
    /// MarkBenefitsUsed: Unk_CommonPriceRecord::MarkUsed on each, which stamps the server time (GetServerTime), not the
    /// packet's time (the argument is unused).
    /// </summary>
    public void MarkBenefitsUsed(long now) {
        foreach (var b in Benefits) {
            if (b.UsedTime != 0) continue;
            b.UsedTime = now;
            State = 1;
        }
    }

    /// <summary>FUN_140692420: the ad's sold flag from the time (0: not sold).</summary>
    public void SetAdSold(long time) { AdSold = (byte)(time != 0 ? 1 : 0); State = 1; }

    /// <summary>FUN_140692690: the kinds whose content may be a system mail template id.</summary>
    public static bool TemplateKind(int kind) => kind is 3 or 0x65 or 0x6b or 0x6d or 0x6f;

    /// <summary>FUN_1406926e0: the player mails (kinds 1, 2), whose texts are not localized nor resolved as MailText.</summary>
    public static bool PlayerKind(int kind) => kind is 1 or 2;

    /// <summary>FUN_140692720: the kinds whose content gets ReplaceObjectAndValueWords at the end.</summary>
    public static bool WordsKind(int kind) => kind is 3 or 0x64 or 0x67;
}

/// <summary>
/// Mail (0x54), CSceneStateGame::OnMail (@0x141721cd0, scenestategame.cpp:11496): u8 mode, then per mode. Modes the client
/// ignores read as <see cref="Other"/>.
/// </summary>
public abstract record MailMessage {
    /// <summary>Mode 0x10: FUN_1408e3030 empties the mails.</summary>
    public sealed record Clear : MailMessage;
    /// <summary>Mode 0: i32 n × CMailInfo, each added when its id is new (AddMailIfNew).</summary>
    public sealed record Listed(IReadOnlyList<MailInfo> Mails) : MailMessage;
    /// <summary>Mode 0x11: the list's refresh flag (DAT_143af37b0) and FUN_1408e4890.</summary>
    public sealed record EndList : MailMessage;
    /// <summary>Mode 1: the mail was sent (notice box 0x228, the draft reset, the send dialog 0x4e4a closed).</summary>
    public sealed record Sent : MailMessage;
    /// <summary>Mode 2: i64 id, i64 the read time (FUN_1408e3540), then UIMailListDialog::OnReadMail(id) on an open list.</summary>
    public sealed record MarkedRead(long Id, long ReadTime) : MailMessage;
    /// <summary>Mode 3: i64 id; notice 0x229 and RemoveMail(id, force).</summary>
    public sealed record Returned(long Id) : MailMessage;
    /// <summary>Mode 0xa: i64 id, u8 an attachment (else the meso range), u8 slot, i64 time.</summary>
    public sealed record Collected(long Id, bool Attachment, int Slot, long Time) : MailMessage;
    /// <summary>Mode 0xb: i64 id, i64 time: every range, benefit and attachment taken.</summary>
    public sealed record CollectedAll(long Id, long Time) : MailMessage;
    /// <summary>Mode 0xc: i64 id, i64 time: the ad's sold flag (FUN_140692420).</summary>
    public sealed record AdBought(long Id, long Time) : MailMessage;
    /// <summary>Mode 0xd: i64 id; RemoveMail(id, force, 0x41) and the list's removed flag (DAT_143af37b4).</summary>
    public sealed record Deleted(long Id) : MailMessage;
    /// <summary>Mode 0xe, ReadMailNew (@0x140201db0): i32 unread, bool alarm, i32 unread alarm.</summary>
    public sealed record New(int Unread, bool Alarm, int UnreadAlarm) : MailMessage;
    /// <summary>Mode 0xf: notice 0x257 (no tutorial) and the chat line 0x258.</summary>
    public sealed record Full : MailMessage;
    /// <summary>Mode 0x14: u8 type, u8 error.</summary>
    public sealed record Error(byte Type, byte Code) : MailMessage;
    /// <summary>
    /// Mode 0x16, ReadGiftNew (@0x140201b00): the sender, u8 type, i32 item id, u8 rarity, i32 count, the UGC path (a narrow
    /// string) and the UGC title.
    /// </summary>
    public sealed record Gift(string Sender, byte Type, int ItemId, byte Rarity, int Count, string UgcPath, string UgcTitle) : MailMessage;
    public sealed record Other(byte Mode) : MailMessage;

    public byte Mode { get; init; }

    public static MailMessage Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static MailMessage Read(ByteReader r, ProtocolProfile profile) {
        byte mode = r.ReadByte();
        MailMessage m = mode switch {
            0x10 => new Clear(),
            0 => ReadList(r, profile),
            0x11 => new EndList(),
            1 => ReadSent(r, profile),
            2 => new MarkedRead(r.ReadLong(), r.ReadLong()),
            3 => new Returned(r.ReadLong()),
            0xa => new Collected(r.ReadLong(), r.ReadByte() != 0, r.ReadByte(), r.ReadLong()),
            0xb => new CollectedAll(r.ReadLong(), r.ReadLong()),
            0xc => new AdBought(r.ReadLong(), r.ReadLong()),
            0xd => new Deleted(r.ReadLong()),
            0xe => new New(r.ReadInt(), r.ReadBool(), r.ReadInt()),
            0xf => new Full(),
            0x14 => new Error(r.ReadByte(), r.ReadByte()),
            0x16 => new Gift(r.ReadUnicodeString(), r.ReadByte(), r.ReadInt(), r.ReadByte(), r.ReadInt(), r.ReadString(), r.ReadUnicodeString()),
            _ => new Other(mode),
        };
        return m with { Mode = mode };
    }

    /// <summary>Mode 1: the KMS2 client sends nothing after the mode; the GMS2 server adds the id of the mail sent (a recording shows it).</summary>
    static Sent ReadSent(ByteReader r, ProtocolProfile profile) {
        if (profile.IsGms2) r.ReadLong();
        return new Sent();
    }

    static Listed ReadList(ByteReader r, ProtocolProfile profile) {
        int n = r.ReadInt();
        var list = new List<MailInfo>();
        for (int i = 0; i < n; i++) list.Add(MailInfo.Read(r, profile));
        return new Listed(list);
    }
}
