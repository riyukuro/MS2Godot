using System.Globalization;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UINpcDuelHPBarDialog (client/ms2/uinpcduelhpbardialog.cpp, "NpcDuelHPBarDialog", UI id 0x4f62, movie
/// uinpcduelhpbardialog): the npc duel bar a trigger opens (Trigger type 0x15). The left side is the local player (name,
/// job icon, profile, hp), the right side the duel npc (name and race, level, portrait, its hp cut into bars), with the
/// countdown between them. The ctor (@0x140e6b7b0) sets SetTweenEnabled(1) and SetFlag(0, 1): an always-on dialog.
/// STILL-OPEN: the npc's buff icons (m_effects, an ASEffectManager over a MovieClip at (0x2c4, 0x3c) / (0x2c4, 0x69); the
/// buff list's AddBuffUI / UpdateBuffUI / RemoveBuffUI, additionalcli_2.cpp:1238-1271): this port keeps no npc buffs, so
/// the clip is made and stays empty.
/// </summary>
public sealed class NpcDuelHpBarController : UiController
{
    public const int UiId = 0x4f62;

    public override string Movie => "uinpcduelhpbardialog";
    protected override string EntityMethod => "setHpBarRight";
    public override string TypeName => "NpcDuelHPBarDialog";      // GetTypeName (@0x140e6b8a0)
    public override bool AlwaysOn => true;                         // ctor SetFlag(0, 1)

    /// <summary>The local player as the dialog reads it (PC): vfunc_77 the name, GetSubJob, the profile image path
    /// (FUN_141028be0(id, picture, 1, "")) and stat 4 (FUN_141670a00 the current, FUN_141670a30 the max).</summary>
    public sealed record Player(string Name, int SubJob, string Profile, long Hp, long MaxHp);

    /// <summary>The duel npc as the dialog reads it (CNpc, WorldService::FindNpcById): GetNpcId, its CNpcData's race and
    /// m_strPortrait, vfunc_78 the level and stat 4.</summary>
    public sealed record Npc(int NpcId, int Race, string Portrait, int Level, long Hp, long MaxHp);

    /// <summary>WorldService::GetLocalPlayer; null without one.</summary>
    public Func<Player> LocalPlayer = () => null;
    /// <summary>WorldService::FindNpcById by object id; null when the field has none.</summary>
    public Func<int, Npc> FindNpc = _ => null;
    /// <summary>GetTickCountMs2.</summary>
    public Func<uint> NowMs = () => (uint)Environment.TickCount;
    /// <summary>FUN_141924680: the host loads the movie when it is not (the open of UIDialog, uidialog.cpp:445).</summary>
    public Action OpenRequested;

    int objectId;               // m_nObjectId
    uint endTick;               // m_nEndTick
    int barCount = 1;           // m_nBarCount
    BuffEffectManager effects;  // m_effects

    public NpcDuelHpBarController()
    {
        Unloaded += Reset;
    }

    public int ObjectId => objectId;
    public int BarCount => barCount;

    /// <summary>vfunc_67 (@0x140e6b8f0): UIDialog's, then the duel forgotten (object 0, end tick 0, one bar, no effects).</summary>
    void Reset()
    {
        objectId = 0;
        endTick = 0;
        barCount = 1;
        effects = null;
    }

    /// <summary>
    /// OnLoadComplete (@0x140e6b9e0): UIDialog's, then the buff clip (a new MovieClip with its containers at (0x2c4, 0x3c) and
    /// (0x2c4, 0x69)) added to the root, then both sides filled (FUN_140e6bc10).
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host.RT.GetClass("flash.display.MovieClip") is { } cls)
        {
            object clip = null;
            Host.RT.Guard("create MovieClip", () => clip = Host.RT.Construct(cls, Array.Empty<object>()));
            if (clip != null)
            {
                effects = new BuffEffectManager(Host, Model, clip) { TickMs = () => NowMs() };
                effects.SetPosA(0x2c4, 0x3c);
                effects.SetPosB(0x2c4, 0x69);
                Host.Invoke(Host.Entity, "addChild", clip);
            }
        }
        Refresh();
    }

    /// <summary>vfunc_43 (@0x140e6c6c0): UIDialog's, then the entity aligned top centre 50 down (vfunc_2(1, 0, 0x32)) and shown.</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        if (Host.Entity == null) return;
        Host.Align(Host.Entity, 1, 0, 0x32);
        Host.SetVisible(Host.Entity, true);
    }

    /// <summary>StartDuelBar (@0x140e6bb70): the npc, the end tick (now + duration) and the bar count (0 counts as 1); with the
    /// movie loaded both sides are filled again.</summary>
    public void StartDuelBar(int npcObjectId, int durationMs, int bars)
    {
        objectId = npcObjectId;
        endTick = NowMs() + (uint)durationMs;
        barCount = bars == 0 ? 1 : bars;
        if (Host?.Entity == null) return;
        Refresh();
    }

    /// <summary>
    /// CTriggerService::OnNpcDuelHpBar (@0x141862ec0, triggerservice.cpp:726-747): a show starts the duel and opens the dialog
    /// (FUN_141924680), then the upper hp bar (0x4e3d) is unloaded (CUIService::FUN_141add0b0, its vfunc_3); a hide closes the
    /// dialog (CloseDialog(0x4f62)) and loads the upper hp bar again (FUN_141add040, its vfunc_2).
    /// </summary>
    public void Apply(TriggerDuelHpBar bar, Action<bool> upperHpBarLoaded)
    {
        if (bar.Show)
        {
            StartDuelBar(bar.ObjectId, bar.DurationMs, bar.BarCount);
            Open();
            upperHpBarLoaded?.Invoke(false);
        }
        else
        {
            if (Host?.Entity != null && !IsClosed) Close();
            upperHpBarLoaded?.Invoke(true);
        }
    }

    /// <summary>UIDialog::FUN_141924680 (uidialog.cpp:445): the dialog shown. Its local player check (vfunc_58, m_88) is off
    /// for this dialog (the UIDialog ctor's 0, which this ctor keeps).</summary>
    void Open() => OpenRequested?.Invoke();

    /// <summary>
    /// FUN_140e6bc10 (@0x140e6bc10): with a duel npc set, the local player's name, job icon ("_" + sub job), profile and hp,
    /// then the npc's name line (s_npc_duelhpbar_dialog_right_name with the title name and the race, as html), level,
    /// portrait and hp, and the buff clip emptied for the npc's shown buffs.
    /// </summary>
    void Refresh()
    {
        if (objectId == 0) return;
        if (LocalPlayer() is { } pc)
        {
            Host.SetChildText("m_leftName", pc.Name);
            if (Host.FindNamed("m_leftJobIcon") is { } icon)       // FUN_140cffca0: gotoAndStop the child by the label
                Host.Invoke(icon, "gotoAndStop", "_" + pc.SubJob.ToString(CultureInfo.InvariantCulture));
            Host.Invoke(Host.Entity, "setLeftProfile", pc.Profile ?? "");
            UpdatePlayerHpBar();
        }
        if (FindNpc(objectId) is { } npc)
        {
            string name = ClientFormat.Format(Model.Tables.Common("s_npc_duelhpbar_dialog_right_name") ?? "",
                Model.Tables.NpcNameWithTitle(npc.NpcId), Model.Tables.RaceName(npc.Race) ?? "");
            if (Host.FindNamed("m_rightName") is { } rn) Host.Set(rn, "htmlText", name);
            Host.SetChildText("m_rightLevel", npc.Level.ToString(CultureInfo.InvariantCulture));
            Host.Invoke(Host.Entity, "setRightProfile", npc.Portrait ?? "");
            UpdateNpcHpBar();
            effects?.Clear();
        }
    }

    /// <summary>vfunc_72 (@0x140e6b950), each frame while the movie is loaded: the time, both hp bars and the buff clip.</summary>
    public void Update()
    {
        if (Host?.Entity == null) return;
        UpdateRemainingTime();
        UpdatePlayerHpBar();
        UpdateNpcHpBar();
        effects?.Update();
    }

    /// <summary>The "%02d%02d" text setTime got last, for checks.</summary>
    public string ShownTime { get; private set; } = "";
    /// <summary>The last setHpBarRight arguments (left in the bar, one bar's hp, the bar shown), for checks.</summary>
    public (long Left, long PerBar, int Bar) RightBar { get; private set; }

    /// <summary>UpdateRemainingTime (@0x140e6c7a0): the whole seconds to the end tick as minutes and seconds (0 past it), to
    /// setTime as "%02d%02d".</summary>
    void UpdateRemainingTime()
    {
        uint min = 0, sec = 0, now = NowMs();
        if (now < endTick)
        {
            uint left = (endTick - now) / 1000;
            min = left / 60;
            sec = left % 60;
        }
        ShownTime = $"{min:00}{sec:00}";
        Host.Invoke(Host.Entity, "setTime", ShownTime);
    }

    /// <summary>UpdatePlayerHpBar (@0x140e6c900): setHpBarLeft(hp, max hp) of the local player.</summary>
    void UpdatePlayerHpBar()
    {
        if (LocalPlayer() is not { } pc) return;
        Host.Invoke(Host.Entity, "setHpBarLeft", (double)pc.Hp, (double)pc.MaxHp);
    }

    /// <summary>
    /// UpdateNpcHpBar (@0x140e6c9c0): the npc's hp cut into the bar count's equal bars (one bar's hp at least 1); the bar the
    /// hp is in (1 to the count) and what is left in it go to setHpBarRight; past one bar the count line shows
    /// s_npc_duelhpbar_dialog_barCount of the bar, else it is hidden. Nothing for a max hp of 0.
    /// </summary>
    void UpdateNpcHpBar()
    {
        if (barCount == 0) return;
        if (FindNpc(objectId) is not { } npc) return;
        long hp = npc.Hp, max = npc.MaxHp;
        if (max == 0) return;
        long perBar = max / barCount;
        if (perBar == 0) perBar = 1;
        int bar = (int)(hp / perBar + 1);
        if (bar < 1) bar = 1;
        if (bar > barCount) bar = barCount;
        long rest = hp - (bar - 1) * perBar;
        if (rest < 0) rest = 0;
        if (rest > perBar) rest = perBar;
        RightBar = (rest, perBar, bar);
        Host.Invoke(Host.Entity, "setHpBarRight", (double)rest, (double)perBar, (uint)bar);
        if (bar <= 1) Host.SetChildVisible("m_rightBarCount", false);
        else
        {
            Host.SetChildVisible("m_rightBarCount", true);
            Host.SetChildText("m_rightBarCount", ClientFormat.Format(Model.Tables.Common("s_npc_duelhpbar_dialog_barCount") ?? "", bar));
        }
    }
}
