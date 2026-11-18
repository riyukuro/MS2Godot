using Godot;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// STAND-IN for the name box UICharacterCreateDialog::OnCreate opens (CMsgBoxData FUN_140d73e40(10, 1) with SetInput(2, "",
/// 12): uimessagebox.gfx, which has no movie port yet): a centred panel with the prompt, one input of up to 12 characters,
/// the error line UIMessageBox::UpdateErrorMessage fills, OK and Cancel. Enter is OK.
/// </summary>
public partial class NamePromptStandIn : Panel
{
    readonly LineEdit edit;
    readonly Label error;

    /// <summary>OK with the text (the callback FUN_1418e7790 gets result 0).</summary>
    public event Action<string> Ok;
    public event Action Cancel;

    public string Text { get => edit.Text; set { edit.Text = value; edit.CaretColumn = value.Length; } }

    public NamePromptStandIn(string prompt)
    {
        Name = "NamePromptStandIn";
        Size = new Vector2(420, 170);
        var box = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f, 0.95f), BorderColor = new Color(0.55f, 0.5f, 0.35f) };
        box.SetBorderWidthAll(2); box.SetCornerRadiusAll(6);
        AddThemeStyleboxOverride("panel", box);
        AddChild(new Label { Text = prompt, Position = new Vector2(20, 14), Size = new Vector2(380, 24) });
        edit = new LineEdit { MaxLength = 12, Position = new Vector2(20, 46), Size = new Vector2(380, 34) };
        edit.TextSubmitted += t => Ok?.Invoke(t);
        AddChild(edit);
        error = new Label { Position = new Vector2(20, 84), Size = new Vector2(380, 22), Modulate = new Color(1f, 0.45f, 0.4f) };
        AddChild(error);
        var ok = new Button { Text = "OK", Position = new Vector2(110, 118), Size = new Vector2(90, 34) };
        ok.Pressed += () => Ok?.Invoke(edit.Text);
        var cancel = new Button { Text = "Cancel", Position = new Vector2(220, 118), Size = new Vector2(90, 34) };
        cancel.Pressed += () => Cancel?.Invoke();
        AddChild(ok); AddChild(cancel);
    }

    public override void _Ready() => edit.GrabFocus();

    /// <summary>UIMessageBox::UpdateErrorMessage.</summary>
    public void SetError(string message) => error.Text = message ?? "";

    /// <summary>Placed at the window's centre.</summary>
    public void Center() => Position = GetViewportRect().Size / 2 - Size / 2;
}
