using System.Globalization;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIRecallScrollNotifyDialog (unit_14129ba60_recallscrollnotifydialog.cpp, "RecallScrollNotifyDialog", UI id 0x4edd, movie
/// uirecallscrollnotifydialog): the bottom-centre notices of a guild (slot 0), party (1) or wedding (2) recall, each with its
/// countdown. The game scene opens it with the HUD (UISceneGame's open list, uiservice.cpp:735). The RecallScroll packet
/// (<see cref="OnPacket"/>, CSceneStateGame::OnRecallScroll, scenestategame.cpp:8038) opens or closes a slot; a click on a
/// notice (the movie's "OnNotify") asks in a box whether to go, and its yes sends RecallScroll (<see cref="RecallScrollAccept"/>,
/// UIMessageBox::HandleRecallscrollconfirmButton, uimessagebox.cpp:1641, here <see cref="MessageBoxController"/>).
/// STILL-OPEN: UIBottomCenterAlignManager::FUN_140bc1730 (uibottomcenteralignmanager.cpp:51) raises the dialog 60 px over an
/// active UIHighlightGameMenu (0x4f75); that menu has no port, so the offset is always 0.
/// STILL-OPEN: OnRecallScroll does not check the type's range before OpenNotify writes its slot (a type over 2 writes past the
/// client's arrays); here such a type changes no slot, and the notice (empty text) and the sound still play.
/// </summary>
public sealed class RecallScrollNotifyController : UiController
{
    public const int UiId = 0x4edd;
    /// <summary>DAT_143ae7558, the recall notice's system sound (scenestategame.cpp:1216).</summary>
    public const string Sound = "Summon_Cash";
    /// <summary>s_strRecallConfirm, the confirm box's tag.</summary>
    public const string ConfirmTag = "recallscrollconfirm";
    const int Slots = 3;

    public override string Movie => "uirecallscrollnotifydialog";
    protected override string EntityMethod => "alignment";            // Dialog's
    public override string TypeName => "RecallScrollNotifyDialog";    // GetTypeName (@0x14129bc10)
    public override bool AlwaysOn => true;                              // a UIBuiltInDialog (its ctor sets flag 0)

    /// <summary>GetServerTime: the server's clock, seconds.</summary>
    public Func<long> ServerNow = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>A millisecond clock for m_tick (Check(1000)).</summary>
    public Func<ulong> NowMs = () => (ulong)Environment.TickCount64;
    /// <summary>WorldService::GetFieldData != 0: the player is in a field.</summary>
    public Func<bool> InField = () => true;
    /// <summary>CFieldDataCli::Find(id) != 0.</summary>
    public Func<int, bool> FieldExists = _ => true;
    /// <summary>CStringTable::FUN_141811980(3, id): the map's name, empty when the table has none.</summary>
    public Func<int, string> MapName;
    /// <summary>GetStringCommon(id).</summary>
    public Func<int, string> CommonById;
    /// <summary>GetStringCommonByKey(key).</summary>
    public Func<string, string> CommonByKey;
    /// <summary>ShowSystemNotice(text, type, log).</summary>
    public Action<string, int, bool> Notice;
    /// <summary>PlaySystemSound(name, "").</summary>
    public Action<string> PlaySound;
    /// <summary>FUN_140d73c60: the box shown.</summary>
    public Action<MessageBoxData> ShowBox;

    readonly long[] times = new long[Slots];          // m_times (server seconds; 0 none)
    readonly string[] names = { "", "", "" };         // m_names
    readonly int[] fields = new int[Slots];           // m_fields
    readonly object[] clips = new object[Slots];      // m_clips
    int offset;                                       // m_nOffset
    bool active;                                      // m_bActive
    int count;                                        // m_nCount
    ulong? lastTick;                                  // m_tick

    public RecallScrollNotifyController()
    {
        Unloaded += ClearClips;
    }

    /// <summary>IsActive (@0x14129c900).</summary>
    public bool IsActive => active;
    public long Time(int slot) => times[slot];
    public string Name(int slot) => names[slot];
    public int Field(int slot) => fields[slot];

    /// <summary>
    /// OnRecallScroll (@0x141734530, scenestategame.cpp:8038): a mode other than 0 opens the type's slot (OpenNotify with the
    /// expiry, the caller and the field), then shows the type's notice (type 0 common string 0xb64, 1 0xb63, 2
    /// s_cash_recall_wedding_notice; ShowSystemNotice(text, 3, 1)) and plays <see cref="Sound"/>; mode 0 closes the slot.
    /// </summary>
    public void OnPacket(RecallScrollPacket p)
    {
        switch (p)
        {
            case RecallScrollPacket.Open o:
            {
                var (id, key) = RecallScrollPacket.NoticeText(o.Type);
                string text = key != null ? CommonByKey?.Invoke(key) ?? "" : id != 0 ? CommonById?.Invoke(id) ?? "" : "";
                if (o.Type < Slots) OpenNotify(o.Type, o.Expiry, o.Name ?? "", o.FieldId);
                Notice?.Invoke(text, 3, true);
                PlaySound?.Invoke(Sound);
                break;
            }
            case RecallScrollPacket.Close c:
                if (c.Type < Slots) CloseNotify(c.Type);
                break;
        }
    }

    /// <summary>OpenNotify (@0x14129c920): the slot's expiry, name and field kept; with the movie loaded, the movie's
    /// OpenNotify(slot, the time left) and SetActive(true).</summary>
    public void OpenNotify(int slot, long expiry, string name, int field)
    {
        times[slot] = expiry;
        names[slot] = name;
        fields[slot] = field;
        if (Host?.Entity == null) return;           // FUN_1422dc2d0
        Host.Invoke("OpenNotify", slot, Remaining(expiry - ServerNow()));
        SetActive(true);
    }

    /// <summary>CloseNotify (@0x14129caa0): the slot's expiry cleared; with the movie loaded, the movie's CloseNotify(slot).</summary>
    public void CloseNotify(int slot)
    {
        times[slot] = 0;
        if (Host?.Entity == null) return;
        Host.Invoke("CloseNotify", slot);
    }

    /// <summary>ClearTimes (@0x14129cb20): every slot's expiry cleared (UISceneGame::vfunc_49, uiscenegame.cpp:223).</summary>
    public void ClearTimes() => Array.Clear(times);

    /// <summary>OnLoadComplete (@0x14129bdd0): UIDialog's, the three notice clips, then each slot whose expiry is still ahead is
    /// opened in the movie with its time left.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        clips[1] = Host.Child("m_partyNotify");
        clips[0] = Host.Child("m_guildNotify");
        clips[2] = Host.Child("m_weddingNotify");
        long now = ServerNow();
        for (int i = 0; i < Slots; i++)
            if (times[i] > now) Host.Invoke("OpenNotify", i, Remaining(times[i] - now));
    }

    /// <summary>vfunc_43 (@0x14129c0b0): UIDialog's, then the entity aligned bottom centre (2), the bottom inset over it plus the
    /// align manager's offset.</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        if (Host?.Entity == null) return;
        Host.Align(Host.Entity, 2, 0, -BottomInset + offset);
    }

    /// <summary>The bottom bar heights and margin FUN_1418dd0d0 sums (uibuiltindialog.cpp:53), as PopupNpcTalk keeps them.</summary>
    public int BottomBarHeight = 21, AdventureBarHeight = 21, ScreenMargin = 193;

    /// <summary>FUN_1418dd0d0: (DAT_1439f8ccc - 2) + the adventure bar's height - 2 with feature 0xbd + DAT_1439f8cc8.</summary>
    public int BottomInset
        => BottomBarHeight - 2 + (Model.IsFeatureEnabled(MainPlayerInfoController.AdventureLevelFeature) ? AdventureBarHeight - 2 : 0) + ScreenMargin;

    /// <summary>vfunc_67 (@0x14129bc90): UIBuiltInDialog's close, then every slot forgotten.</summary>
    public override void Close()
    {
        base.Close();
        for (int i = 0; i < Slots; i++)
        {
            times[i] = 0;
            names[i] = "";
            fields[i] = 0;
            clips[i] = null;
        }
    }

    /// <summary>vfunc_26 (@0x14129bd70): the clips dropped with the movie.</summary>
    void ClearClips()
    {
        clips[0] = clips[1] = clips[2] = null;
    }

    /// <summary>
    /// vfunc_72 (@0x14129c610): with the movie loaded, once a second each slot with an expiry whose clip is shown counts as
    /// active and is closed in the movie when the time is past (its expiry stays), else shows its time left; a change of the
    /// count sets the active state (on while any is shown).
    /// </summary>
    public void Update()
    {
        if (Host?.Entity == null) return;
        ulong nowMs = NowMs();
        lastTick ??= nowMs;
        if (nowMs - lastTick.Value < 1000) return;
        lastTick = nowMs;
        long now = ServerNow();
        int shown = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (times[i] == 0) continue;
            if (clips[i] == null || !Equals(Host.Get(clips[i], "visible"), true)) continue;
            shown++;
            if (now > times[i]) Host.Invoke("CloseNotify", i);
            else Host.Invoke("UpdateNotify", i, Remaining(times[i] - now));
        }
        if (count != shown)
        {
            count = shown;
            SetActive(count != 0);
        }
    }

    /// <summary>The active state changed (UIBottomCenterAlignManager::FUN_140bc1730 re-stacks the bottom-centre dialogs).</summary>
    public event Action ActiveChanged;

    /// <summary>SetActive (@0x14129cc60): kept, then the align manager re-stacks: an active dialog takes the offset (0 here; see
    /// the STILL-OPEN above) and is laid out again.</summary>
    void SetActive(bool on)
    {
        active = on;
        if (active && Host?.Entity != null) Host.Align(Host.Entity, 2, 0, -BottomInset + offset);
        ActiveChanged?.Invoke();
    }

    /// <summary>
    /// vfunc_28 (@0x14129c170): UIDialog's calls first; nothing without the field data; "OnNotify"(slot): a slot out of 0-2 is
    /// taken and does nothing; a field the data lacks is not taken; else the yes / no box (type 1) of common string 0xb62 with
    /// the caller's name and the map's name, tag <see cref="ConfirmTag"/>, the arguments slot, name and field, not blocking
    /// (FUN_141a02460(1)), modaless, not answered by Enter (FUN_141a024a0(0)), buttons 0x477 / 0x478, shown now.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!InField()) return false;
        if (!string.Equals(name, "OnNotify", StringComparison.OrdinalIgnoreCase)) return false;
        if (args == null || args.Length == 0) return false;    // ASData::SetArgsIfAny
        int slot = args[0] switch                                // ASData::PopInt
        {
            double d => (int)d,
            int n => n,
            var o => MessageBoxData.WToI(Convert.ToString(o, CultureInfo.InvariantCulture) ?? ""),
        };
        if (slot < 0 || slot >= Slots) return true;
        if (!FieldExists(fields[slot])) return false;
        string text = ClientFormat.Format(CommonById?.Invoke(0xb62) ?? "", names[slot], MapName?.Invoke(fields[slot]) ?? "");
        var box = MessageBoxQueue.Create(1, text, null, ConfirmTag);
        box.Append(slot);
        box.Args.Add(names[slot]);                   // AddStringArg
        box.Append((uint)fields[slot]);
        box.NoInputBlock = true;
        box.Modaless = true;
        box.EnterAllowed = false;
        box.SetButtonLabelIds(0x477, 0x478, 0);
        (ShowBox ?? MessageBoxQueue.Show)(box);
        return true;
    }

    /// <summary>The click of a notice (the movie's OnNotify with its slot), for checks.</summary>
    public bool ClickNotice(int slot) => OnCallback("OnNotify", new object[] { (double)slot });

    /// <summary>
    /// FUN_14129cb50 (@0x14129cb50): "%02d:%02d" of the span's minutes (0-59) and seconds, in a &lt;FONT color&gt; of white, red
    /// (0xff0000) at 10 s or less (FUN_14183e930).
    /// </summary>
    public static string Remaining(long seconds)
    {
        long minutes = seconds / 60 % 60, secs = seconds % 60;     // CTimeSpan::GetMinutes / GetSeconds
        int color = seconds <= 10 ? 0xff0000 : 0xffffff;
        return $"<FONT color='#{color.ToString("x", CultureInfo.InvariantCulture)}'>{Pad2(minutes)}:{Pad2(secs)}</FONT>";
    }

    /// <summary>printf "%02d": at least two characters, zero padded after a sign.</summary>
    static string Pad2(long n) => n < 0 ? (n > -10 ? "-" + (-n).ToString(CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture))
                                        : n.ToString("00", CultureInfo.InvariantCulture);
}

/// <summary>The confirm box's yes (HandleRecallscrollconfirmButton): RecallScroll with the box's slot, name and field.</summary>
public static class RecallScrollConfirm
{
    /// <summary>The packet the yes sends: argument 0 the slot (FUN_141a02e50, one byte), 1 the name (FUN_141a02fd0), 2 the field
    /// (FUN_141a02e90).</summary>
    public static RecallScrollAccept Accept(MessageBoxData box) =>
        new(box.ArgInt(0, 0), box.Args.Count > 1 ? box.Args[1] : "", (int)box.ArgUInt(2, 0));
}
