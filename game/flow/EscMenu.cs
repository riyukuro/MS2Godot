using Godot;

namespace Ms2Godot.Flow;

/// <summary>The in-field escape menu: resume, back to character select, quit. It pauses the field while open.</summary>
public partial class EscMenu : CanvasLayer
{
    public event Action BackToSelect, Quit;
    public Button ResumeButton, BackButton, QuitButton;

    public EscMenu()
    {
        Layer = 200;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
    }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both; panel.GrowVertical = Control.GrowDirection.Both;
        dim.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        box.AddChild(new Label { Text = "Menu", HorizontalAlignment = HorizontalAlignment.Center });
        ResumeButton = Add(box, "Resume", Close);
        BackButton = Add(box, "Back to character select", () => { Close(); BackToSelect?.Invoke(); });
        QuitButton = Add(box, "Quit", () => Quit?.Invoke());
    }

    static Button Add(Container parent, string text, Action pressed)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        b.Pressed += pressed;
        parent.AddChild(b);
        return b;
    }

    public bool IsOpen => Visible;

    public void Toggle() { if (Visible) Close(); else Open(); }

    public void Open() { Visible = true; GetTree().Paused = true; ResumeButton.GrabFocus(); }

    public void Close() { Visible = false; GetTree().Paused = false; }
}
