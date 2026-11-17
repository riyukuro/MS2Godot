using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>CLIK CheckBox: a toggle Button (its constructor sets toggle = true); the label is the skin's text field.</summary>
[GlobalClass]
public partial class NCheckBox : NButton
{
    public NCheckBox() { Toggle = true; }
}
