using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UITokenPocketDialog (UI id 0x4ece, ms2/client/ms2/uitokenpocketdialog.cpp): the token pocket, opened from the upper
/// HUD's token pocket button (UIUpperHudDialog::OnTokenPocketClick, ToggleDialog(0x4ece)). One row per token currency
/// the features allow, each a UITokenPocketDialog_slot with the currency's icon, its amount (and cap) and a tooltip,
/// below the drag bar; the black and white backgrounds grow with the rows. NotifyChangedTokenMoney refreshes the texts.
/// </summary>
public sealed class TokenPocketController : UiController
{
    public const int UiId = 0x4ece;

    public override string Movie => "uitokenpocketdialog";
    public override string TypeName => "TokenPocketDialog";       // GetTypeName (@:50)
    protected override string EntityMethod => "alignment";        // Dialog's

    /// <summary>One row of the list: the currency (<c>nTokenKind</c>), its feature (0 = always) and its tooltip key.</summary>
    static readonly (int Currency, int Feature, string Tooltip)[] Rows =
    {
        (4, Ui.Feature.PvPRankingDuelMode, "$s_token_pocket_honor_tooltip"),
        (5, Ui.Feature.ItemSocketSystem, "$s_token_pocket_karma_tooltip"),
        (6, 0, "$s_token_pocket_lu_tooltip"),
        (7, Ui.Feature.HabiToken, "$s_token_pocket_habi_tooltip"),
        (0xc, Ui.Feature.ReverseRaid01, "$s_token_pocket_reverse_coin_tooltip"),
        (0xd, Ui.Feature.Mentoring, "$s_token_pocket_mentor_token_tooltip"),
        (0xe, Ui.Feature.Mentoring, "$s_token_pocket_mentee_token_tooltip"),
        (0xf, -1, "$s_token_pocket_star_point_tooltip"),             // -1: while the star point event runs
        (0x10, Ui.Feature.MesoMarketToken, "$s_token_pocket_meso_market_token_tooltip"),
    };

    readonly List<(int Currency, object Slot)> slots = new();
    int nextY;                                                    // m_178
    readonly HashSet<object> registered = new(ReferenceEqualityComparer.Instance);

    /// <summary>The rows built, by currency.</summary>
    public IReadOnlyList<(int Currency, object Slot)> Slots => slots;

    /// <summary>vfunc_43 (:107): ASDialog::vfunc_2(5, 0x118, 0x28), the absolute place (dx, dy) = (0x118, 0x28).</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (KeepsPlace(stageW, stageH)) return;
        Host.Align(Host.Entity, 5, 0x118, 0x28);
    }

    /// <summary>
    /// OnLoadComplete (@0x141351af0): UIDialog's, the drag bar and close button bound (the movie's own drag handling, and
    /// BindCloseButton("m_closeButton")), the rows from y 0x32, the backgrounds sized to them, then the texts.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_closeButton");
        nextY = 0x32;
        slots.Clear();
        foreach (var (currency, feature, tooltip) in Rows)
        {
            bool on = feature == 0 || (feature == -1 ? Pc.StarPointEvent : Model.IsFeatureEnabled(feature));
            if (on) AddTokenSlot(currency, tooltip);
        }
        int black = nextY - 0x2c;
        int white = black + 0x37;
        if (Host.Child("m_bgWhite") is { } bw) Host.Set(bw, "height", white);                // SetHeightInt
        if (Host.Child("m_bgBlack") is { } bb) Host.Set(bb, "height", black);
        RefreshTexts();
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the button's click handler is the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("token pocket dialog: " + name); return; }
        Func<object[], object> f = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>AddTokenSlot (@0x141352cf0): the slot component with its icon html, placed at (0xe, y), 0x17 below the last,
    /// added to the dialog, with a text tooltip aligned 5.</summary>
    void AddTokenSlot(int currency, string tooltipKey)
    {
        var cls = Host.RT.GetClass("UITokenPocketDialog_slot");
        if (cls == null) { Host.RT.NoteMissing("token pocket dialog: UITokenPocketDialog_slot"); return; }
        var slot = Host.RT.Construct(cls, Array.Empty<object>());
        var (icon, vspace) = TokenCurrency.Icon(currency);
        if (Host.Child("icon", slot) is { } ic) Host.Set(ic, "htmlText", $"<img src='{icon}' vspace='{vspace}'/>");
        Host.Set(slot, "x", 0xe);
        Host.Set(slot, "y", nextY);
        nextY += 0x17;
        Host.Invoke(Host.Entity, "addChild", slot);
        Host.Set(slot, "_tooltipData", Host.Obj(("tooltip", "export_common_tooltip_tooltipText"), ("text", tooltipKey),
            ("onDelay", 300), ("offDelay", 5000), ("tooltipAlign", 5)));
        if (registered.Add(slot) && Host.RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm)
            Host.Invoke(tm, "registerMouseEvent", slot);
        slots.Add((currency, slot));
    }

    /// <summary>NotifyChangedTokenMoney (OnChangedTokenMoney @0x141352530).</summary>
    public override void OnStatus(StatusEvent e)
    {
        if (e == StatusEvent.TokenMoney && Host?.Entity != null) RefreshTexts();
    }

    /// <summary>FUN_141352560: each row's "textField" gets the currency's amount text (FUN_141900120(currency, 0, 0)).</summary>
    public void RefreshTexts()
    {
        if (Host?.Entity == null) return;
        foreach (var (currency, slot) in slots)
            if (slot is AsDisplayObject s) Host.SetChildText("textField", TokenCurrency.AmountText(Model, currency), s);
    }
}
