using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

/// <summary>
/// The skill window's auto distribute dialog (UISkillBookAutoDistributeDialog, 0x4f20): opened by the window's auto distribute
/// button with the presets its sub job can use, closed with the skill window. See <see cref="SkillBookAutoDistributeController"/>.
/// </summary>
public partial class HudScene
{
    SkillBookAutoDistributeController skillAutoDistribute;
    MovieDialog skillAutoDistributeMovie;

    void OpenSkillAutoDistribute(SkillBookTreeController tree, IReadOnlyList<AutoDistributePreset> presets)
    {
        if (skillAutoDistributeMovie != null)
        {
            skillAutoDistribute?.Close();
            skillAutoDistributeMovie.Host.Unload(); skillAutoDistributeMovie.QueueFree(); skillAutoDistributeMovie = null;
        }
        var c = new SkillBookAutoDistributeController { Presets = presets, ApplyDistribute = tree.ApplyDistribute };
        var m = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (skillAutoDistributeMovie == m) skillAutoDistributeMovie = null;
        };
        tree.Unloaded += () => { if (skillAutoDistribute == c && !c.IsClosed) c.Close(); };
        skillAutoDistribute = c; skillAutoDistributeMovie = m;
        uiRoot.AddChild(m);
    }
}
