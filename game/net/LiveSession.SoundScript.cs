using Godot;

namespace Ms2Godot.Net;

public partial class LiveSession
{
    /// <summary>The sound option slider each step name moves (the sound tab's controls, SoundOptionPage).</summary>
    static readonly Dictionary<string, string> SoundControls = new()
    {
        ["main"] = "m_mainSoundStep", ["effect"] = "m_effectSoundStep", ["env"] = "m_enviromentSoundStep", ["ui"] = "m_uiSoundStep",
        ["npc"] = "m_npcSoundStep", ["boss"] = "m_bossSoundStep", ["bgm"] = "m_bgmSoundStep", ["system"] = "m_systemSoundStep",
        ["movie"] = "m_movieSoundStep", ["char"] = "m_charSoundStep",
    };

    /// <summary>
    /// MS2_LIVE_SOUND="step|step|...": "set:effect=0,bgm=10,mute=0" opens the options dialog's sound tab, moves those sliders
    /// (0..10; mute ticks "mute all"), saves and closes it; "play" plays the ui click and presses the attack key (a skill and
    /// its effect sounds), then logs every sound player's gain; "~N" waits N s. The gains are printed by FieldAudio under LiveCheck.
    /// </summary>
    internal static async Task SoundRun(SceneTree tree, Func<int, Task> frames)
    {
        string run = System.Environment.GetEnvironmentVariable("MS2_LIVE_SOUND");
        if (string.IsNullOrEmpty(run) || tree.CurrentScene is not Ui.HudScene hud) return;
        await frames(240);
        int n = 0;
        foreach (var step in run.Split('|'))
        {
            n++;
            if (step.StartsWith('~') && int.TryParse(step.AsSpan(1), out int wait)) { await frames(60 * wait); continue; }
            if (step.StartsWith("set:"))
            {
                int tab = hud.OpenSoundOptionsScripted();
                for (int i = 0; i < 600 && !hud.SoundOptionsReadyScripted(); i++) await frames(1);
                bool ready = hud.SoundOptionsReadyScripted();
                GD.Print($"live: sound {n}: options sound tab {tab} {(ready ? "open" : "NOT open")}");
                if (!ready) continue;
                foreach (var kv in step[4..].Split(','))
                {
                    var p = kv.Split('=');
                    if (p.Length != 2 || !int.TryParse(p[1], out int v)) continue;
                    if (p[0] == "mute") hud.SetOptionSelectedScripted("m_bMuteAll", v != 0);
                    else if (SoundControls.TryGetValue(p[0], out var control)) hud.SetOptionValueScripted(control, v);
                }
                await frames(10);
                hud.SaveOptionsScripted();
                await frames(30);
                var o = GameOptions.Current.SoundOption;
                GD.Print($"live: sound {n}: saved main {o.MainSoundStep} effect {o.EffectSoundStep} bgm {o.BgmSoundStep} ui {o.UiSoundStep} mute {o.MuteAll}: {SoundBuses.Describe()}");
                continue;
            }
            if (step == "play")
            {
                var audio = FieldAudio.Instance;
                GD.Print($"live: sound {n}: ui click {(audio?.PlayUiClick() == true ? "played" : "not played")}");
                await frames(20);
                GD.Print($"live: sound {n}: attack {(hud.AttackScripted() ? "cast" : "no cast")}");
                await frames(90);
                GD.Print($"live: sound {n}: {audio?.DescribeGains() ?? "no field audio"}");
            }
        }
        GD.Print("live: sound run done");
    }
}
