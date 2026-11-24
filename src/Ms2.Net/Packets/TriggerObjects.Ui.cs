using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// A Ui command, Trigger type 9 (CTriggerService::FUN_1418627c0 @0x1418627c0, triggerservice.cpp:632-671): u8 sub, then the
/// sub's fields. The fields a sub does not read stay default.
/// </summary>
public sealed record TriggerUiCommand(byte Sub)
{
    /// <summary>Sub 1: the guide trigger id (AddGuideTrigger, :1007); 2 and 3: the guide entity (:987, :998); 4 and 5: the cut
    /// scene id (:963, :978); 9: the pc or npc object (:1053); 0xc: the start tick (OnTypingGame, :769).</summary>
    public int Id { get; init; }
    /// <summary>Sub 2: the summary text id; 8: the emotion's value (m_nValue); 0xc: the time limit in ms.</summary>
    public int Value { get; init; }
    /// <summary>Sub 2: the summary's duration in ms.</summary>
    public int Duration { get; init; }
    /// <summary>Sub 4: the cut scene movie's path (a narrow string).</summary>
    public string Path { get; init; } = "";
    /// <summary>Sub 9: the face emotion's name (a narrow string; empty rests the face).</summary>
    public string Text { get; init; } = "";
    /// <summary>Sub 7: the emotions played in turn; 8: the one emotion (ReadUnicodeStringA: UTF-16 on the wire).</summary>
    public IReadOnlyList<string> Emotions { get; init; } = Array.Empty<string>();
    /// <summary>Sub 8: the emotion's flag (m_nFlag).</summary>
    public byte Flag { get; init; }
    /// <summary>Subs 0xa and 0xb: the point (ReadVector3Short: three i16).</summary>
    public Vector3 Point { get; init; }
    /// <summary>Sub 0xa: move to the point as well as turn; 0xb: LookAtPoint's flag.</summary>
    public bool Move { get; init; }
}

/// <summary>Trigger type 0xf (CTriggerService::FUN_141862110 @0x141862110, triggerservice.cpp:527-629): u8 kind, then the
/// timer every kind reads (WorldService::FUN_141bc6f60: the start tick, the duration in ms, the auto-remove byte, as the
/// typing game sets it, uitypinggamedialog.cpp:333), then kind 1's offset and align, or kind 2's / 5's match block.</summary>
public sealed record TriggerDialogCommand(byte Kind)
{
    public int Start { get; init; }
    /// <summary>The duration in ms; 0 closes the kind's dialogs (kinds 1 and 3).</summary>
    public int Duration { get; init; }
    public byte AutoRemove { get; init; }
    /// <summary>Kind 1: the timer dialog's (0x4e95) y offset.</summary>
    public int OffsetY { get; init; }
    /// <summary>Kind 1: "TR" aligns the timer dialog top right (SetAlign 6), anything else centres it (SetAlign 1).</summary>
    public string Align { get; init; } = "";
    /// <summary>Kinds 2 and 5: the PvP match block (FUN_140743ca0 / FUN_140743f50, pvpinfo.cpp:193 / :222).</summary>
    public TriggerPvpMatch Match { get; init; }

    /// <summary>Kind 1 the timer dialog (0x4e95); 2 and 5 the PvP observer dialog (0x4e96) with a normal or a duel match; 3
    /// opens or closes 0x4e99 and 0x4e9a; 4 opens 0x4e95 and 0x4ed3.</summary>
    public const byte Timer = 1, PvpObserver = 2, PvpDuel = 5;
}

/// <summary>One side of a PvP match block: the character id and the fields after it. Kind 5's block (FUN_140743f50) has one
/// i32 more per side (<see cref="Points"/>) and one more after the second side (<see cref="TriggerPvpMatch.Last"/>).
/// STILL-OPEN: what the two i32 and the u8 after the id mean; the upstream server writes hp, max hp and wins there.</summary>
public readonly record struct TriggerPvpSide(long CharacterId, int Value1, int Value2, byte Wins, int Points);

/// <summary>The match block of a Dialog kind 2 or 5; the observer dialog (0x4e96) closes when either id is 0.</summary>
public sealed record TriggerPvpMatch(TriggerPvpSide First, TriggerPvpSide Second, int Last);

/// <summary>Trigger type 0x13 (OnSidePopupTalk @0x1418629b0, triggerservice.cpp:683-705): the UISidePopupTalk (0x4f57)
/// parameters. Name, Icon, Sound and Script are narrow strings, Text a wide one (a server string key).</summary>
public sealed record TriggerSidePopup(byte Kind, int NpcOrDuration, string Name, string Icon, string Sound, string Script, string Text);

/// <summary>Trigger type 0x14 (FUN_141862d30 @0x141862d30, triggerservice.cpp:709-723): the state set on each named UI
/// object (a comma list) whose state differs.</summary>
public sealed record TriggerSetVisibleUi(byte State, string Names);

/// <summary>Trigger type 0x15 (OnNpcDuelHpBar @0x141862ec0, triggerservice.cpp:726-747): show the npc duel bar (0x4f62)
/// with StartDuelBar(object, duration ms, bar count) (uinpcduelhpbardialog.cpp:114), or close it.</summary>
public sealed record TriggerDuelHpBar(bool Show, int ObjectId, int DurationMs, int BarCount);

public static partial class TriggerObjects
{
    /// <summary>The client types of the UI commands (CTriggerService::FUN_141861840's switch, triggerservice.cpp:366).</summary>
    public const byte Ui = Guide, Dialog = 0xf, SidePopup = 0x13, SetVisibleUi = 0x14, DuelHpBar = 0x15;

    /// <summary>The other client types of FUN_141861840's switch: 6 OnTargetLookCenterCamera (:438, the camera mode 0x13), 7
    /// OnCameraFadeOff (:485, the camera reset of fade 0, FUN_141863460(0)), 0xc OnTextPopup (:498, the trigger editor
    /// 0x4e6b), 0x10 / 0x11 / 0x12 WorldService's FUN_141bd42f0 / AddEffect / RemoveEffect, 0x16 FUN_141862930 (a result
    /// of 0 shows box 0xb03) and 0x17-0x19 WorldService::FUN_141bdad00 (CRandomTargetEffectController, worldservice_1.cpp:8894).</summary>
    public const byte TargetLookCenterCamera = 6, CameraFadeOff = 7, TextPopup = 0xc, LoadEffects = 0x10, AddEffect = 0x11,
        RemoveEffect = 0x12, BoxResult = 0x16, RandomTargetSequence = 0x17, RandomTargetFlicker = 0x18, RandomTarget3 = 0x19;

    /// <summary>A client type the switch has no case for (0, 1, 4, 8, 0xa, 0xb, 0xd, 0xe and above 0x19): the client reads
    /// nothing more and does nothing.</summary>
    public static bool ClientIgnores(byte clientType) => clientType switch
    {
        Load or Update or CameraObjects or TargetLookCenterCamera or CameraFadeOff or Guide or TextPopup or Dialog => false,
        >= LoadEffects and <= RandomTarget3 => false,
        _ => true,
    };

    /// <summary>The client types the client applies and this port does not: they are logged once per type.</summary>
    public static bool NotPorted(byte clientType) => clientType is TargetLookCenterCamera or TextPopup or LoadEffects or AddEffect
        or RemoveEffect or BoxResult or RandomTargetSequence or RandomTargetFlicker or RandomTarget3;

    public const byte UiGuideTrigger = 1, UiShowSummary = 2, UiHideSummary = 3, UiStartMovie = 4, UiStopMovie = 5,
        UiEmotions = 7, UiEmotion = 8, UiFaceEmotion = 9, UiTurn = 0xa, UiLookAt = 0xb, UiTypingGame = 0xc;

    /// <summary>The body of a type 9 packet after its type byte; a sub the client has no case for reads nothing more.</summary>
    public static TriggerUiCommand ReadUi(ByteReader r)
    {
        byte sub = r.ReadByte();
        var c = new TriggerUiCommand(sub);
        switch (sub)
        {
            case UiGuideTrigger: return c with { Id = r.ReadInt() };
            case UiShowSummary: return c with { Id = r.ReadInt(), Value = r.ReadInt(), Duration = r.ReadInt() };
            case UiHideSummary: return c with { Id = r.ReadInt() };
            case UiStartMovie: return c with { Path = r.ReadString(), Id = r.ReadInt() };
            case UiStopMovie: return c with { Id = r.ReadInt() };
            case UiEmotions:
            {
                int n = r.ReadInt();
                var list = new List<string>(Math.Max(0, Math.Min(n, 64)));
                for (int i = 0; i < n; i++) list.Add(r.ReadUnicodeString());
                return c with { Emotions = list };
            }
            case UiEmotion:
            {
                byte flag = r.ReadByte(); int value = r.ReadInt();
                return c with { Flag = flag, Value = value, Emotions = new[] { r.ReadUnicodeString() } };
            }
            case UiFaceEmotion: return c with { Id = r.ReadInt(), Text = r.ReadString() };
            case UiTurn:
            case UiLookAt: return c with { Point = new Vector3(r.ReadShort(), r.ReadShort(), r.ReadShort()), Move = r.ReadBool() };
            case UiTypingGame: return c with { Id = r.ReadInt(), Value = r.ReadInt() };
            default: return c;
        }
    }

    /// <summary>The body of a type 0xf packet after its type byte; an unknown kind reads nothing more.</summary>
    public static TriggerDialogCommand ReadDialog(ByteReader r)
    {
        byte kind = r.ReadByte();
        var c = new TriggerDialogCommand(kind);
        if (kind is < TriggerDialogCommand.Timer or > TriggerDialogCommand.PvpDuel) return c;
        c = c with { Start = r.ReadInt(), Duration = r.ReadInt(), AutoRemove = r.ReadByte() };
        switch (kind)
        {
            case TriggerDialogCommand.Timer: return c with { OffsetY = r.ReadInt(), Align = r.ReadUnicodeString() };
            case TriggerDialogCommand.PvpObserver: return c with { Match = ReadMatch(r, false) };
            case TriggerDialogCommand.PvpDuel: return c with { Match = ReadMatch(r, true) };
            default: return c;
        }
    }

    static TriggerPvpMatch ReadMatch(ByteReader r, bool duel)
    {
        TriggerPvpSide Side() => new(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadByte(), duel ? r.ReadInt() : 0);
        var first = Side();
        var second = Side();
        return new TriggerPvpMatch(first, second, duel ? r.ReadInt() : 0);
    }

    /// <summary>The body of a type 0x13 packet after its type byte.</summary>
    public static TriggerSidePopup ReadSidePopup(ByteReader r) =>
        new(r.ReadByte(), r.ReadInt(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadUnicodeString());

    /// <summary>The body of a type 0x14 packet after its type byte.</summary>
    public static TriggerSetVisibleUi ReadSetVisibleUi(ByteReader r) => new(r.ReadByte(), r.ReadString());

    /// <summary>The body of a type 0x15 packet after its type byte.</summary>
    public static TriggerDuelHpBar ReadDuelHpBar(ByteReader r)
    {
        bool show = r.ReadBool();
        return show ? new TriggerDuelHpBar(true, r.ReadInt(), r.ReadInt(), r.ReadInt()) : new TriggerDuelHpBar(false, 0, 0, 0);
    }
}
