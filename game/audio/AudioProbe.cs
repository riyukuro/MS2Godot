using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot;

/// <summary>
/// Headless proof of the sound path: resolve the field's bgm cue from the player's data, start it, let the clock run and
/// log the playback position. godot-mono --headless --path game res://audio_probe.tscn -- [--field XBLOCK] [--seconds N]
/// </summary>
public partial class AudioProbe : Node
{
    FieldAudio audio;
    PackSet packs;
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
        packs = new PackSet(data);
        audio = new FieldAudio();
        AddChild(audio);
        if (!audio.Open(data, packs)) { GetTree().Quit(3); return; }
        var bgm = FieldAudio.FieldBgm(packs, field);
        GD.Print($"audio probe: field {field} bgm cue {bgm.BgmId} param {bgm.BgmParam}");
        bool ok = audio.PlayFieldBgm(bgm);
        bool click = audio.PlayUi("button_click");
        audio.PlayUi("dialog_open");
        Field();
        GD.Print($"audio probe: bgm resolved={ok} ({audio.BgmName}), ui click resolved={click}, audio driver {AudioServer.GetOutputDeviceList().Length} output(s)");
    }

    // ambient entities of the field and a footstep walk over its ground cubes
    void Field()
    {
        var map = FieldMap.Load(packs, new FlatLibrary(packs), field);
        audio.Ambient = new AmbientSounds(audio.Library);
        audio.AddChild(audio.Ambient);
        audio.Ambient.Build(map);
        foreach (var l in audio.Ambient.Log) GD.Print("audio probe: ambient " + l);
        GD.Print($"audio probe: {audio.Ambient.Count} ambient players");
        var body = new CharacterBody3D { Name = "Probe" };
        AddChild(body);
        var anim = new AnimationPlayer();
        body.AddChild(anim);
        var lib = new AnimationLibrary();
        lib.AddAnimation("run", new Animation { Length = 0.6f, LoopMode = Animation.LoopModeEnum.Linear });
        anim.AddAnimationLibrary("", lib);
        var table = new Ms2.Audio.GroundEffectTable(p => { try { return System.Text.Encoding.UTF8.GetString(packs.Read("Xml", p)); } catch { return null; } });
        foot = new FootstepSounds(audio.Library, table, body, anim) { OnFloor = () => true };
        AddChild(foot);
        // stand on the first grass cube and on the first rock cube
        foreach (var kind in new[] { "grass", "rock" })
        {
            var e = map.Entities.FirstOrDefault(x => x.Get("MapAttribute") == kind);
            if (e == null) continue;
            var p = FieldBuilder.ToGodotPoint(e.Position) + new Vector3(75, 150, -75);
            body.GlobalPosition = p;
            // the probe has no controller: stand in for its hit report with the cube's material
            foot.Ground = () => e.Get("MapAttribute");
            GD.Print($"audio probe: at {p}: ground '{foot.Ground()}'");
            foot.Step("walk");
            foot.Step("run0");
            foot.Step("run1");
            GD.Print($"audio probe: step on {foot.LastGround}: action {foot.LastAction}");
        }
        foot.Land();
        GD.Print($"audio probe: land on {foot.LastGround}");
        // the cadence: run animation, velocity set, 1.3 s of frames
        body.GlobalPosition = Vector3.Zero;
        body.Velocity = new Vector3(350, 0, 0);
        anim.Play("run");
        cadenceFrom = foot.Steps;
    }

    FootstepSounds foot;
    int cadenceFrom;

    public override void _Process(double delta)
    {
        if (audio == null) return;
        t += delta;
        if (t < wait) return;
        GD.Print($"audio probe: cadence steps in the run animation over {t:F1} s: {foot.Steps - cadenceFrom} (last action {foot.LastAction})");
        GD.Print($"audio probe: after {t:F1} s music playing={audio.MusicPlaying} position={audio.MusicPosition:F2} s");
        GetTree().Quit(audio.MusicPlaying ? 0 : 1);
    }
}
