using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// One quick slot of the skill bar (uimainplayerinfo's playerinfo_slot_skill, a SkillSlot) when it holds nothing: the
/// slot background m_bg and the shortcut key text m_shortcutKey. BaseSlot's constructor hides m_recharge (the hexagon
/// with a "0") and the empty layers draw nothing, so neither is drawn here. Roll-over plays the slot's "over" frames
/// (item_slot_over_69 on frames 20-39), shown here as its first frame while the mouse is over the slot.
/// </summary>
public partial class NSkillSlot : Control
{
    const string Movie = "uimainplayerinfo";
    [Export] public string Key = "";
    NSkin over;

    public override void _Ready()
    {
        Size = new Vector2(64, 64);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new NSkin { Movie = Movie, Symbol = "playerinfo_slot_skill/m_bg" });
        var key = new NSkin { Movie = Movie, Symbol = "playerinfo_slot_skill/m_shortcutKey", Position = new Vector2(4, 2), ShowTexts = true, Text = Key };
        AddChild(key);
        over = new NSkin { Movie = Movie, Symbol = "UIMainPlayerInfo_fla.item_slot_over_69", Position = new Vector2(2, 2), Visible = false };
        AddChild(over);
        MouseEntered += () => over.Visible = true;
        MouseExited += () => over.Visible = false;
    }
}
