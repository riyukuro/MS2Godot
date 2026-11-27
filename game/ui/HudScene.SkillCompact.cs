using System;
using Godot;
using Ms2Godot.Net;

namespace Ms2Godot.Ui;

// ---- compact skill control: uiskillcompactcontrol run by its own ActionScript, UISkillCompactControlDialog's C++ side as
// SkillCompactControlController (game menu entry 27 "compactkey", key action 400031: CUIService::ToggleDialog(0x4ef6))

public partial class HudScene
{
    MovieDialog skillCompactMovie;              // from open until OnEndClose
    Ms2.Ui.Hud.SkillCompactControlController skillCompact;
    int[] skillCompactShot;                     // --skillcompact subJob[,level]

    bool SkillCompactOpen => skillCompact?.IsClosed == false;

    /// <summary>CUIService::ToggleDialog(0x4ef6): open when closed, else vfunc_67.</summary>
    void ToggleSkillCompact()
    {
        if (!SkillCompactOpen) OpenSkillCompact();
        else skillCompact.Close();
    }

    /// <summary>
    /// Opens the compact skill control window over the live session's skill book: the window asks for the saved tabs
    /// (SkillMacro mode 0) and takes the 0xD1 mode 0 answer; its close sends them back (mode 1) and keeps them as the skill
    /// system's compact configs. With no live session, over a new character's book of the HUD's job and an answer with no tabs
    /// (the window then makes its one default tab). A live session without its book yet opens nothing.
    /// </summary>
    void OpenSkillCompact()
    {
        if (GuideRefusesOpen("SkillCompactControl")) return;
        if (skillCompactMovie != null) { skillCompactMovie.Host.Unload(); skillCompactMovie.QueueFree(); skillCompactMovie = null; }
        var live = LiveSession.Instance;
        var tables = hudModel.Tables.Skills;
        var book = live != null ? live.SkillBook : Ms2.Ui.UiFixture.NewCharacterSkillBook(tables.Jobs, hudModel.Player.SubJob / 10);
        if (book == null) { GD.Print("skill compact: the live session has no skill book yet"); return; }
        var jobs = new Ms2.Ui.Skills.JobSystem(tables) { IsFeatureEnabled = hudModel.IsFeatureEnabled };
        jobs.Read(book);
        var c = new Ms2.Ui.Hud.SkillCompactControlController { Jobs = jobs };
        // ASTooltip(pPlayer, 0x10, 2) + FUN_1409e1b80(id, 0) -> FUN_1409e1c50(id, level, 0)
        var tipPlayer = SkillTooltipPlayer(jobs);
        c.SkillTooltip = (id, level) =>
        {
            tipPlayer.Level = hudModel.Player.Level;
            return Ms2.Ui.Skills.SkillTooltip.Build(hudModel.Tables, tipPlayer, id, level, 0x10, false);
        };
        Action<Ms2.Net.Packets.SkillMacroPacket> onMacro = c.OnSkillMacro;
        if (live != null)
        {
            c.RequestTabs += live.RequestSkillMacro;
            c.Save += live.SaveSkillMacro;
            c.ConfigsChanged += tabs => { live.CompactConfigs = tabs; FeedCompactConfigs(tabs, true); };   // SetCompactConfigs (FUN_140fa1400)
            live.SkillMacro += onMacro;
        }
        else
        {
            c.RequestTabs += () => Callable.From(() => c.OnSkillMacro(Ms2.Net.Packets.SkillMacroPacket.Read(
                new Maple2.PacketLib.Tools.ByteReader(Ms2.Net.Packets.SkillMacroPacket.Write(0, Array.Empty<Ms2.Net.Packets.SkillMacroTab>()))))).CallDeferred();
            c.Save += tabs => GD.Print($"skill compact: {tabs.Count} tabs are not sent (no live session)");
            c.ConfigsChanged += tabs => FeedCompactConfigs(tabs, true);   // SetCompactConfigs (FUN_140fa1400)
        }
        c.HotKeyCapture += on => compact.HotKeyCapture(on, (uint)Time.GetTicksMsec());   // FUN_141767df0
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            if (live != null) live.SkillMacro -= onMacro;
            m.Host.Unload();
            m.QueueFree();
            if (skillCompactMovie == m) skillCompactMovie = null;
        };
        skillCompact = c; skillCompactMovie = m;
        uiRoot.AddChild(m);
    }

    /// <summary>The DirectInput code of a physical key (the first one <see cref="GodotKeyOfDik"/> maps to it), 0 for none.</summary>
    static int DikOfGodotKey(Key key)
    {
        for (int dik = 1; dik < 0x100; dik++)
            if (GodotKeyOfDik(dik) == key) return dik;
        return 0;
    }

    /// <summary>The window's keys: while its hot key field reads a key (vfunc_13 @0x140f9e700), every press goes to it.</summary>
    bool SkillCompactKeys(InputEventKey k)
    {
        if (!SkillCompactOpen) return false;
        if (skillCompact.CapturingKey) return skillCompact.OnKey(DikOfGodotKey(k.PhysicalKeycode));
        return false;
    }
}
