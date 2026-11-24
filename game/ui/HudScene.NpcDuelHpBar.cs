using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The npc duel bar (UINpcDuelHPBarDialog, 0x4f62, uinpcduelhpbardialog.cpp): the Trigger type 0x15 packet opens and closes
/// it (CTriggerService::OnNpcDuelHpBar, triggerservice.cpp:726-747), its movie loads on the open and goes with the close.
/// STILL-OPEN: the client unloads the upper hp bar (0x4e3d, its vfunc_3) while the duel shows and loads it again after
/// (vfunc_2), so its shown target is gone after a duel; here its movie is hidden and shown again and keeps its state. The
/// npc's level is its table row's (CNpcData m_nLevel; the client reads CNpc vfunc_78).
/// </summary>
public partial class HudScene
{
    NpcDuelHpBarController duelBar;      // kept across relayouts, as the client's dialog
    MovieDialog duelBarMovie;
    Action unwatchDuelBar;

    void NpcDuelHpBarPane()
    {
        if (duelBar == null)
        {
            duelBar = new NpcDuelHpBarController
            {
                NowMs = () => (uint)Time.GetTicksMsec(),
                LocalPlayer = DuelLocalPlayer,
                FindNpc = DuelNpc,
                OpenRequested = OpenDuelBar,
            };
            duelBar.Unloaded += DuelBarUnloaded;
        }
        unwatchDuelBar?.Invoke();
        unwatchDuelBar = null;
        if (Net.LiveSession.Instance is { } live)
        {
            Action<Ms2.Net.Packets.TriggerDuelHpBar> on = bar => duelBar.Apply(bar, UpperHpBarLoaded);
            live.DuelHpBarReceived += on;
            unwatchDuelBar = () => live.DuelHpBarReceived -= on;
        }
    }

    /// <summary>PC: the name, sub job, profile image (FUN_141028be0(id, picture, 1)) and stat 4 of the local player.</summary>
    NpcDuelHpBarController.Player DuelLocalPlayer()
    {
        if (hudModel is not { HasLocalPlayer: true } m) return null;
        var live = Net.LiveSession.Instance;
        long self = live?.CharacterId ?? 0;
        string profile = Net.ProfileImageService.Images.Path(self, live?.Me?.User?.Picture ?? "", true);
        return new NpcDuelHpBarController.Player(m.Player.Name, m.Player.SubJob, profile, m.Player.HpCur, m.Player.HpMax);
    }

    /// <summary>WorldService::FindNpcById: the live npc of the object id, with its table row's race, portrait and level.</summary>
    NpcDuelHpBarController.Npc DuelNpc(int objectId)
    {
        if (Net.LiveSession.Instance?.NpcHp(objectId) is not { } n) return null;
        var row = World.FieldContents.Npcs(assets.Packs).Find(n.NpcId);
        return new NpcDuelHpBarController.Npc(n.NpcId, row?.Race ?? 0, row?.Portrait ?? "", row?.Level ?? 1, n.Hp, n.MaxHp);
    }

    /// <summary>FUN_141924680 on 0x4f62: its movie loaded when it is not.</summary>
    void OpenDuelBar()
    {
        if (duelBarMovie != null) return;
        duelBarMovie = new MovieDialog(assets, duelBar, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(duelBarMovie);
    }

    void DuelBarUnloaded()
    {
        duelBarMovie?.Host.Unload();
        duelBarMovie?.QueueFree();
        duelBarMovie = null;
    }

    /// <summary>CUIService::FUN_141add040 / FUN_141add0b0 on the upper hp bar (0x4e3d): see the STILL-OPEN above.</summary>
    void UpperHpBarLoaded(bool loaded)
    {
        if (targetBar != null && IsInstanceValid(targetBar)) targetBar.Visible = loaded;
    }

    /// <summary>UINpcDuelHPBarDialog::vfunc_72 while its movie is loaded.</summary>
    void NpcDuelHpBarTick()
    {
        if (duelBarMovie != null) duelBar?.Update();
    }

    void NpcDuelHpBarUnwatch()
    {
        unwatchDuelBar?.Invoke();
        unwatchDuelBar = null;
    }
}
