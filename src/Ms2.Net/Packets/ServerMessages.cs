// Layouts from the restored client's readers, checked against the server fork's writers where it has one.
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>Inbound UiText (CSceneStateGame::FUN_141731190, scenestategame.cpp:7878): i32 object id, i32 type, i32 value.
/// Types 4, 7 and 0xa show the value as a damage text of grade 5, 6 and 7 over that object (PCBase::vfunc_111,
/// pcbase.cpp:2844); any other type shows nothing. The server fork has no writer.</summary>
public sealed record UiText(int ObjectId, int Type, int Value) {
    public static UiText Read(ByteReader r) { var m = new UiText(r.ReadInt(), r.ReadInt(), r.ReadInt()); r.ExpectEnd("UiText"); return m; }

    /// <summary>The damage grade the type maps to (4 to 5, 7 to 6, 0xa to 7), or -1 when the client ignores the packet.</summary>
    public int Grade => Type switch { 4 => 5, 7 => 6, 0xa => 7, _ => -1 };
}

/// <summary>Inbound NoticeDialog (UINoticeDialog::OnNoticeDialog, uinoticedialog.cpp:224): one u8 flag; flag 0 shows the
/// notice dialog for its configured time. The server fork has no writer.</summary>
public sealed record NoticeDialogMessage(byte Flag) {
    public static NoticeDialogMessage Read(ByteReader r) { var m = new NoticeDialogMessage(r.ReadByte()); r.ExpectEnd("NoticeDialog"); return m; }
    public bool Show => Flag == 0;
}

/// <summary>Inbound PlaySystemSound (CSceneStateGame::OnPlaySystemSound, scenestategame.cpp:4389): one unicode string, the
/// systemsound.xml key; an empty key plays nothing. Server: SoundPacket.System.</summary>
public sealed record SystemSound(string Key) {
    public static SystemSound Read(ByteReader r) { var m = new SystemSound(r.ReadUnicodeString()); r.ExpectEnd("PlaySystemSound"); return m; }
}

/// <summary>Inbound NewsNotification (CSceneStateGame::OnNewsNotification, scenestategame.cpp:3789): u8 mode; mode 0 reads
/// a unicode url or token, a bool in game and an i32 type. In game, type 0 opens the web browser, 1 the visitors book,
/// 2 the notice dialog, 5 the browser popup, each given the url; not in game the token goes to the browser service.
/// Server: NewsNotificationPacket.</summary>
public sealed record NewsNotification(byte Mode, string Url, bool InGame, int Type) {
    public static NewsNotification Read(ByteReader r) {
        byte mode = r.ReadByte();
        if (mode != 0) return new NewsNotification(mode, "", false, 0);
        var m = new NewsNotification(mode, r.ReadUnicodeString(), r.ReadBool(), r.ReadInt());
        r.ExpectEnd("NewsNotification");
        return m;
    }
}

/// <summary>Inbound SmartPush (CSceneStateGame::OnSmartPush, scenestategame.cpp:7335): bool positional; true reads i32 x, i32 y
/// for the push marker; false reads a u8 mode, and mode 1 opens the smart push list while any other mode reads an i32
/// string id shown as a centre notice. The server fork has no writer.</summary>
public sealed record SmartPushMessage(bool Positional, int X, int Y, byte Mode, int StringId) {
    public static SmartPushMessage Read(ByteReader r) {
        if (r.ReadBool()) { var p = new SmartPushMessage(true, r.ReadInt(), r.ReadInt(), 0, 0); r.ExpectEnd("SmartPush"); return p; }
        byte mode = r.ReadByte();
        int id = mode == 1 ? 0 : r.ReadInt();
        r.ExpectEnd("SmartPush");
        return new SmartPushMessage(false, 0, 0, mode, id);
    }
}

/// <summary>Inbound Meret (CSceneStateGame::OnMeret, scenestategame.cpp:7025): the four balances (meret, extra meret, game
/// meret, extra game meret) and the i64 delta. Server: CurrencyPacket.UpdateMeret.</summary>
public sealed record MeretUpdate(long Meret, long ExtraMeret, long GameMeret, long ExtraGameMeret, long Delta) {
    public static MeretUpdate Read(ByteReader r) {
        var m = new MeretUpdate(r.ReadLong(), r.ReadLong(), r.ReadLong(), r.ReadLong(), r.ReadLong());
        r.ExpectEnd("Meret");
        return m;
    }
}

/// <summary>Inbound MeretUse (CSceneStateGame::OnMeretUse, scenestategame.cpp:7053): the i64 red meret balance and the i64 delta.</summary>
public sealed record MeretUseUpdate(long Total, long Delta) {
    public static MeretUseUpdate Read(ByteReader r) { var m = new MeretUseUpdate(r.ReadLong(), r.ReadLong()); r.ExpectEnd("MeretUse"); return m; }
}

/// <summary>Inbound CurrencyToken (CSceneStateGame::OnCurrencyToken, scenestategame.cpp:7094): u8 token type, i64 balance,
/// i64 delta, an i32 the chat line takes, and the i64 amount over the cap (nonzero shows the overflow message).
/// Server: CurrencyPacket.UpdateCurrency.</summary>
public sealed record CurrencyTokenUpdate(byte Type, long Value, long Delta, int Extra, long Overflow) {
    public static CurrencyTokenUpdate Read(ByteReader r) {
        var m = new CurrencyTokenUpdate(r.ReadByte(), r.ReadLong(), r.ReadLong(), r.ReadInt(), r.ReadLong());
        r.ExpectEnd("CurrencyToken");
        return m;
    }
}
