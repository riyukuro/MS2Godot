using Godot;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// Headless proof of the sound path: resolve the field's bgm cue from the player's data, start it, let the clock run and
/// log the playback position. godot-mono --headless --path game res://audio_probe.tscn -- [--field XBLOCK] [--seconds N]
/// </summary>
public partial class AudioProbe : Node
{
    FieldAudio audio;
    double t, wait = 3;
    string field = "02000001_tw_tria";

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--field") field = args[i + 1];
            if (args[i] == "--seconds") wait = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
        }
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data)) { GD.PushError("Set MS2_DATA."); GetTree().Quit(2); return; }
        var packs = new PackSet(data);
        audio = new FieldAudio();
        AddChild(audio);
        if (!audio.Open(data)) { GetTree().Quit(3); return; }
        int cue = FieldAudio.FieldBgmCue(packs, field);
        GD.Print($"audio probe: field {field} bgm cue {cue}");
        bool ok = audio.PlayBgm(cue);
        bool click = audio.PlayUiClick();
        GD.Print($"audio probe: bgm resolved={ok} ({audio.BgmName}), ui click resolved={click}, audio driver {AudioServer.GetOutputDeviceList().Length} output(s)");
    }

    public override void _Process(double delta)
    {
        if (audio == null) return;
        t += delta;
        if (t < wait) return;
        GD.Print($"audio probe: after {t:F1} s music playing={audio.MusicPlaying} position={audio.MusicPosition:F2} s");
        GetTree().Quit(audio.MusicPlaying ? 0 : 1);
    }
}
