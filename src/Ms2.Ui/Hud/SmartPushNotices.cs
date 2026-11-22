using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// What CSceneStateGame::OnSmartPush (scenestategame.cpp:7335) does with a SmartPush packet that is not a plain notice:
/// a positional one (id, value) goes to CSmartPushManager::FUN_140dd86a0 (smartpushmanager.cpp:133), which hands the value
/// to the push with that id (CSmartPush::vfunc_4, smartpush.cpp:309); mode 1 is CUIService::ShowLackMeratAsk
/// (uiservice.cpp:1110). Also the outbound answer of CSmartPush::vfunc_5 (smartpush.cpp:322).
/// </summary>
public sealed class SmartPushNotices
{
    readonly SmartPushTable table;
    readonly Func<string, string> common;
    readonly Func<int, ushort, string> effectName;

    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> SystemNotice;
    /// <summary>The message box queue's show (FUN_140d73c60).</summary>
    public Action<MessageBoxData> ShowBox;
    /// <summary>CFeatureManager::IsFeatureEnabled(0xc2).</summary>
    public Func<bool> ChargeFeature = () => false;
    /// <summary>The yes answer of the lack-merat box (FUN_141add940): with feature 0xc2 off FUN_140d73cd0 runs, with it on the
    /// platform charge page opens (vfunc_45 of FUN_140d86260). Argument: the feature's state. STILL-OPEN: neither is drawn
    /// here (the charge page is a web page).</summary>
    public Action<bool> ChargeRequested;
    /// <summary>SendOp SmartPush: id, the push's m_n1c, 0, accept.</summary>
    public Action<int, int, bool> SendAnswer;

    public SmartPushNotices(SmartPushTable table, Func<string, string> commonByKey, Func<int, ushort, string> additionalEffectName)
    {
        this.table = table ?? SmartPushTable.Empty;
        common = commonByKey;
        effectName = additionalEffectName;
    }

    /// <summary>The push classes CSmartPushManager::CreatePushes finds by "CSmartPush{content}" (smartpushmanager.cpp:79), the
    /// types registered in smartpush.cpp. Any other content has no class: no push.</summary>
    enum PushClass { None, Base, AutoFishing, Toggle, AutoInteraction }

    // CreatePushes builds the push through CreateClassObject("CSmartPush" + content); smartpush.cpp:251-263 registers only 13
    // names (Breeding(Buff), Farming(Buff), Fishing, Gathering(Buff), Mining(Buff), PlayInstrument, SafeRiding, SafeWaterRiding,
    // SafeWaterRidingToggle, each under the "Auto" prefix where the class has one). The abstract bases AutoInteractObject,
    // AutoFunctionCubeObject and their BuffItem forms have no registrar, so a row with those contents makes no push.
    static PushClass ClassOf(string content) => content switch
    {
        "AutoPlayInstrument" or "SafeRiding" or "SafeWaterRiding" => PushClass.Base,      // vfunc_4 is CSmartPush's (:309)
        "AutoFishing" => PushClass.AutoFishing,                                          // :492
        "SafeWaterRidingToggle" => PushClass.Toggle,                                     // :1089, empty
        "AutoGathering" or "AutoMining" or "AutoBreeding" or "AutoFarming"
            or "AutoGatheringBuff" or "AutoMiningBuff" or "AutoBreedingBuff" or "AutoFarmingBuff" => PushClass.AutoInteraction,   // :1096
        _ => PushClass.None,
    };

    /// <summary>The mastery recipe id of the nearby object (CSmartPushAutoInteraction::vfunc_14, from the content). STILL-OPEN: the
    /// field's nearby gathering / cube object and the best buff item are not asked here; null leaves AutoInteraction pushes silent.</summary>
    public Func<string, int> RecipeOf;
    /// <summary>CStringTable::FUN_141811980(0x2f, recipe): the recipe's name.</summary>
    public Func<int, string> RecipeName;
    /// <summary>ShowSystemNotice(text, 3, 1): the mastery result line. STILL-OPEN: the kind 3 styling is not ported.</summary>
    public Action<string> MasteryNotice;
    /// <summary>CSmartPushAutoFishing::vfunc_4's tail: SendFishingMode9 when the fishing manager has a rod out, then the option
    /// saved. STILL-OPEN: the fishing manager and the character options are not wired.</summary>
    public Action AutoFishingResult;

    /// <summary>g_143ab3150: the gather count per recipe id, which AutoInteraction results add to.</summary>
    public Dictionary<int, int> GatherCounts { get; } = new();

    /// <summary>FUN_140dd86a0 (smartpushmanager.cpp:133) then the push's vfunc_4. The push exists only when the content has a class
    /// (CreatePushes), and the class decides: the base (:309) says an additionalEffect row's effect, at level 1, is active
    /// ("s_smartpush_active" with its name); the toggle shows nothing; AutoInteraction (:1096) says
    /// s_mastery_autointeraction_result with the value and the recipe's name and adds the value to the recipe's gather count;
    /// AutoFishing runs the base notice, then <see cref="AutoFishingResult"/>.</summary>
    public void OnPositional(int id, int value)
    {
        var d = table.Get(id);
        if (d == null) return;
        switch (ClassOf(d.Content))
        {
            case PushClass.Base: BaseResult(d); break;
            case PushClass.AutoFishing: BaseResult(d); AutoFishingResult?.Invoke(); break;
            case PushClass.AutoInteraction:
                if (RecipeOf == null) break;
                int recipe = RecipeOf(d.Content);
                MasteryNotice?.Invoke(ClientFormat.Format(common("s_mastery_autointeraction_result"), value, RecipeName?.Invoke(recipe) ?? ""));
                GatherCounts.TryGetValue(recipe, out int used);
                GatherCounts[recipe] = used + value;
                break;
        }
    }

    void BaseResult(SmartPushTable.Data d)
    {
        if (d.ActionType != SmartPushTable.ActionAdditionalEffect) return;
        string name = effectName?.Invoke(d.ActionValue, 1) ?? "";
        SystemNotice?.Invoke(ClientFormat.Format(common("s_smartpush_active"), name));
    }

    /// <summary>ShowLackMeratAsk: a type 1 box with s_err_lack_merat_ask; the yes answer (0) asks for the charge page and
    /// the box closes only when the feature is on (FUN_141add940 returns false with it off, keeping the box).</summary>
    public MessageBoxData ShowLackMeratAsk()
    {
        var box = MessageBoxQueue.CreateWithCallback(common("s_err_lack_merat_ask"), 1, (result, _) =>
        {
            if (result != 0) return true;
            bool on = ChargeFeature?.Invoke() ?? false;
            ChargeRequested?.Invoke(on);
            return on;
        });
        ShowBox?.Invoke(box);
        return box;
    }

    /// <summary>CSmartPush::vfunc_5: the packet answering a push (id, n1c, 0, accept).</summary>
    public void Answer(int id, int n1c, bool accept) => SendAnswer?.Invoke(id, n1c, accept);
}
