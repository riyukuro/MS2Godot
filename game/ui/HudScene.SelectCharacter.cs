using Godot;

namespace Ms2Godot.Ui;

public partial class HudScene
{
    /// <summary>A window point on the open system menu's child <paramref name="name"/> that the menu's hit test reaches, for a
    /// check that clicks it through the window's input as a hand would; null when the menu is closed or nothing is hit.</summary>
    public Vector2? SystemMenuPoint(string name) =>
        systemMenuMovie?.Host.Child(name) is Ms2.Avm2.Flash.AsDisplayObject d ? WindowPoint(systemMenuMovie, d) : null;
}
