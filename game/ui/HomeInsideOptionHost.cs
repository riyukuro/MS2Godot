using Ms2.Formats.World;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>The home inside option dialog's host (UIHomeInsideOptionDialog, uihomeinsideoptiondialog.cpp): UgcHomeOptionData's lists,
/// the home setting bytes of the cube map service, and the RequestCube send.</summary>
sealed class HomeInsideOptionHost(Net.LiveSession live, UgcHomeOptionData data) : IHomeInsideOptionHost
{
    public UgcHomeOptionData Data => data;
    public (byte Background, byte Light, byte Camera) Setting => (live.CubeMap.Background, live.CubeMap.Lighting, live.CubeMap.Camera);
    public void Send(byte mode, byte index) => live.SendHomeInsideOption(mode, index);
}
