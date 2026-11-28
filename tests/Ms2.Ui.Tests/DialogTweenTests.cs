using System.Reflection;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>m_c4 (SetTweenEnabled, uidialog.cpp:363) is 0 in UIDialog's ctor and only a ctor's SetTweenEnabled(1) turns the
/// open / close tween on. A dialog whose ctor never calls it is unloaded by CloseDialog at once (FUN_141924330, uidialog.cpp:393),
/// so an OpenDialog right after (OnPartyInfo's pair) finds it closed and loads it again; running a close tween here would leave
/// it "closing" and the open would be dropped (vfunc_65, uidialog.cpp:564, opens only a closed dialog).</summary>
public class DialogTweenTests
{
    static readonly (Type Controller, string Cite)[] NoTween =
    {
        (typeof(PartyController), "UIPartyDialog @0x141a74b40 uipartydialog.cpp:397"),
        (typeof(PartyDpsController), "UIPartyDPSDialog @0x141a7b050 uipartydialog.cpp:1198"),
        (typeof(AutoReviveCountDownController), "UIAutoReviveCountDownDialog @0x141284ac0 uiautorevivecountdowndialog.cpp:22"),
        (typeof(BeautyShopDialogController), "UIBeautyShopDialog @0x1418c3630 uibeautyshopdialog.cpp:504"),
        (typeof(BeautyStyleDialogController), "UIBeautyShopStyleDialog @0x140f03090 uibeautyshopstyledialog.cpp:778"),
        (typeof(GameDictionaryController), "UIGameDictionaryDialog @0x140f447d0 uigamedictionarydialog.cpp:153"),
        (typeof(HomeMenuDialogController), "UIHomeMenuDialog @0x140c29160 uihomemenudialog.cpp:220"),
        (typeof(HomeOptionDialogController), "UIHomeOptionDialog @0x140fa8cc0 uihomeoptiondialog.cpp:86"),
        (typeof(ItemColoringDialogController), "UIItemColoringDialog @0x1419abdd0 uiitemcoloringdialog.cpp:250"),
        (typeof(ItemEnchantDialogController), "UIItemEnchantDialog @0x1419befc0 uiitemenchantdialog.cpp:431, base @0x1419be860 :329"),
        (typeof(ItemMergeOptionDialogController), "UIItemMergeOptionDialog @0x1419ccd00 uiitemmergeoptiondialog.cpp:314"),
        (typeof(ItemPreviewController), "UIItemPreviewDialog @0x1412d5890 uiitempreviewdialog.cpp:64"),
        (typeof(MailSendDialogController), "UIMailSendDialog @0x1419ec180 uimaillistdialog.cpp:1356"),
        (typeof(RevivalDialogController), "UIRevivalDialog @0x141aba6a0 uirevivaldialog.cpp:152"),
        (typeof(TriggerTimerDialogController), "UITriggerTimerDialog @0x141b330c0 uitriggertimerdialog.cpp:43"),
    };

    // ctors with SetTweenEnabled(1) (the tween stays on)
    static readonly (Type Controller, string Cite)[] Tween =
    {
        (typeof(QuestDialogController), "UIQuestDialog @0x141a993a0 uiquestdialog.cpp:297"),
        (typeof(TradeDialogController), "UITradeDialogEx @ uitradedialogex.cpp:124"),
        (typeof(CubeBillDialogController), "UIUGCCubeBillDialog @ uiugccubebilldialog.cpp:63"),
        (typeof(HomeSaveSlotDialogController), "UIHomeSaveSlotDialog @0x140fa9eb0 :143"),
        (typeof(LapenshardSystemController), "UILapenshardSystemDialog @ uilapenshardsystemdialog.cpp:694"),
        (typeof(ShadowExpeditionBossBarController), "UIShadowExpeditionBossBar @0x140f9cc60 uishadowexpeditionbossbar.cpp:243"),
        (typeof(DarkStreamScoreDialogController), "UIDarkStreamScoreDialog @0x140f35340 uidarkstreamscoredialog.cpp:36"),
    };

    static bool TweenOf(Type t)
    {
        var c = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);   // the property reads no state; some ctors take arguments
        var p = typeof(UiController).GetProperty("TweenEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        return (bool)p.GetValue(c);
    }

    [TestCaseSource(nameof(NoTweenCases))]
    public void ADialogWhoseCtorHasNoSetTweenEnabledHasNoTween(Type controller, string cite) =>
        Assert.That(TweenOf(controller), Is.False, cite);

    [TestCaseSource(nameof(TweenCases))]
    public void ADialogWhoseCtorTurnsTheTweenOnKeepsIt(Type controller, string cite) =>
        Assert.That(TweenOf(controller), Is.True, cite);

    public static IEnumerable<TestCaseData> NoTweenCases() => NoTween.Select(x => new TestCaseData(x.Controller, x.Cite).SetArgDisplayNames(x.Controller.Name));
    public static IEnumerable<TestCaseData> TweenCases() => Tween.Select(x => new TestCaseData(x.Controller, x.Cite).SetArgDisplayNames(x.Controller.Name));
}
