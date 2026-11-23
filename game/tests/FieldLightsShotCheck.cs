using System;
using Godot;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Maple2.PacketLib.Tools;

namespace Ms2Godot.Tests;

/// <summary>
/// Field check (res://tests/field_lights_shot.tscn): the field's ambient and sun light follow the field property records
/// (kinds 8 and 9, FieldProperties.Lights). It opens field.tscn, waits for the load, then shoots the field as built, applies
/// recorded GMS2 0xCC add packets (args after `--`: --field XBLOCK --dir SHOTDIR), shoots again, applies a second pair, shoots
/// and ends with a KMS-profile record with the fade flag, shot at the half and the end of its seconds. The shots are saved as
/// light_0_built.png, light_1_set.png, light_2_set.png, light_3_fade_half.png and light_4_fade_end.png. Quits with 0 when the
/// light colours read back equal the records', else 1; 2 when the field does not load in time.
/// </summary>
public partial class FieldLightsShotCheck : Node
{
    const int LoadFrames = 60 * 300, Settle = 8;
    int frames, loadedAt = -1, stage;
    int wait;
    string dir = "";
    readonly FieldProperties props = new();
    Field field;
    readonly System.Collections.Generic.List<string> fails = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--dir") dir = args[i + 1];
        DirAccess.MakeDirRecursiveAbsolute(dir);
        CallDeferred(MethodName.Move);
    }

    void Move()
    {
        var root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        GetTree().ChangeSceneToFile("res://field.tscn");
    }

    static FieldPropertyMessage Gms2(string hex) => FieldPropertyMessage.Read(new ByteReader(Convert.FromHexString(hex)), ProtocolProfile.Gms2);

    void Shot(string name)
    {
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name));
        GD.Print($"light shot {name}");
    }

    void Expect(string what, System.Numerics.Vector3 got, System.Numerics.Vector3 want)
    {
        if (System.Numerics.Vector3.Distance(got, want) > 1e-3f) fails.Add($"{what}: {got} not {want}");
    }

    public override void _Process(double delta)
    {
        frames++;
        if (loadedAt < 0)
        {
            if (Field.LoadComplete && GetTree().CurrentScene is Field f) { loadedAt = frames; field = f; props.Lights = f; wait = Settle; }
            else if (frames > LoadFrames) { GD.Print("field lights shot: FAIL the field did not load"); GetTree().Quit(2); }
            return;
        }
        props.StepLights((float)delta);
        if (--wait > 0) return;
        var l = (IFieldLights)field;
        switch (stage++)
        {
            case 0:
                GD.Print($"light built: ambient {l.Ambient} sun {l.Sun}");
                Shot("light_0_built.png");
                props.Apply(Gms2("0108E2C5D3")); props.Apply(Gms2("0109E0F6F9AAAAAA"));
                break;
            case 1:
                Expect("ambient set", l.Ambient, new(0xE2 / 255f, 0xC5 / 255f, 0xD3 / 255f));
                Expect("sun set", l.Sun, new(0xE0 / 255f, 0xF6 / 255f, 0xF9 / 255f));
                Shot("light_1_set.png");
                props.Apply(Gms2("010878" + "77B7")); props.Apply(Gms2("0109C16477AAAAAA"));
                break;
            case 2:
                Expect("ambient set 2", l.Ambient, new(0x78 / 255f, 0x77 / 255f, 0xB7 / 255f));
                Expect("sun set 2", l.Sun, new(0xC1 / 255f, 0x64 / 255f, 0x77 / 255f));
                Shot("light_2_set.png");
                // a KMS record with the fade flag: white over 2 s, from the colour the light has
                props.Apply(FieldPropertyMessage.Read(new ByteReader(Convert.FromHexString("0108FFFFFF0100000040" )), ProtocolProfile.Kms2525));
                props.Apply(FieldPropertyMessage.Read(new ByteReader(Convert.FromHexString("0109FFFFFFFFFFFF0100000040")), ProtocolProfile.Kms2525));
                fadeStart = Time.GetTicksMsec();
                break;
            case 3:
                // wait for ~1 s of the 2 s fade
                if (Time.GetTicksMsec() - fadeStart < 1000) { stage--; wait = 1; return; }
                Shot("light_3_fade_half.png");
                GD.Print($"light fade at {(Time.GetTicksMsec() - fadeStart) / 1000f:0.00} s: ambient {l.Ambient} sun {l.Sun}");
                break;
            case 4:
                if (Time.GetTicksMsec() - fadeStart < 2300) { stage--; wait = 1; return; }
                Expect("fade end ambient", l.Ambient, System.Numerics.Vector3.One);
                Expect("fade end sun", l.Sun, System.Numerics.Vector3.One);
                Shot("light_4_fade_end.png");
                GD.Print(fails.Count == 0 ? "field lights shot: ok" : "field lights shot: FAIL " + string.Join("; ", fails));
                GetTree().Quit(fails.Count == 0 ? 0 : 1);
                break;
        }
        if (stage <= 2) wait = Settle;
        else if (stage == 3) wait = 1;
    }

    ulong fadeStart;
}
