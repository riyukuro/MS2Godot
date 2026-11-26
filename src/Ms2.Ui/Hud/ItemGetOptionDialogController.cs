using Ms2.Avm2;
using Ms2.Formats.Options;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIItemGetOptionDialog (client/ms2/uiitemgetoptiondialog.cpp; UI id 0x4e6c, a UIModalDialog), for its movie
/// uiitemgetoptiondialog: the auto-loot options. <see cref="Mode"/> 1, which the inventory's onClickItemOption sets
/// (uiinventorydialog.cpp:860), works on the character options' autoLooting block; mode 0 (the pet's) on the summoned pet's
/// flags, sent back as RequestPet mode 6 (STILL-OPEN: the port has no pet info, so mode 0 loads the defaults and its OK is
/// raised as <see cref="Button"/>).
/// </summary>
public sealed class ItemGetOptionDialogController : ModalDialogController
{
    public override string Movie => "uiitemgetoptiondialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemGetOptionDialog";

    public const int UiId = 0x4e6c;
    public override int TypeId => UiId;

    /// <summary>The dropdown's rows are the grades 1 to 6.</summary>
    public const int GradeRows = 6;

    /// <summary>m_nMode: 1 the character's options (the ctor's), 0 the pet's.</summary>
    public int Mode = 1;
    /// <summary>m_opt: the options the dialog edits.</summary>
    public GameOption.AutoLoot Options { get; private set; } = new();
    /// <summary>CGameOption m_autoLoot (FUN_141533e20): the character's saved options.</summary>
    public Func<GameOption.AutoLoot> Saved = () => null;
    /// <summary>CGameOption::SaveCharacterSettings.</summary>
    public Action SaveCharacterSettings = () => { };
    /// <summary>CFeatureManager::IsFeatureEnabled.</summary>
    public Func<int, bool> Feature = _ => true;
    /// <summary>CFeatureManager::IsRegionCN.</summary>
    public Func<bool> RegionCn = () => false;
    /// <summary>What the port does not do (mode 0's RequestPet send), raised by name.</summary>
    public event Action<string> Button;

    static readonly string[] Boxes = { "m_equip", "m_consume", "m_etc", "m_meso", "m_merat", "m_token", "m_other", "m_gemstone", "m_pet" };

    /// <summary>OnLoadInit: LoadOptions before the movie's load completes.</summary>
    public override void OnLoadCompleteBeforeBase() => LoadOptions();

    /// <summary>
    /// OnLoadComplete (@0x1419d9720): UIModalDialog's; each check box selected by its option and the m_equipRank dropdown on the
    /// grade's row (grade 1 to 6 is row 0 to 5; any other grade row 0), set only when it differs. Then vfunc_46.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        var o = Options;
        bool[] values = { o.Equip, o.Consume, o.Etc, o.Meso, o.Merat, o.Token, o.Other, o.GemStone, o.Pet };
        for (int i = 0; i < Boxes.Length; i++)
            if (Host.Child(Boxes[i]) is { } box) Host.Set(box, "selected", values[i]);
        if (Host.Child("m_equipRank") is { } rank)
        {
            int row = o.EquipGrade is >= 1 and <= GradeRows ? o.EquipGrade - 1 : 0;
            if (Rt.ToInt32(Host.Get(rank, "selectedIndex")) != row) Host.Set(rank, "selectedIndex", row);
        }
        Shown();
    }

    /// <summary>vfunc_46 (@0x1419da640): without feature 0x8d m_pet hidden and disabled; without 0x2e m_gemstone hidden; in the CN
    /// region m_merat hidden; then the movie's updateAlign.</summary>
    void Shown()
    {
        if (!Feature(0x8d) && Host.Child("m_pet") is { } pet) { Host.SetVisible(pet, false); Host.Set(pet, "enabled", false); }
        if (!Feature(0x2e) && Host.Child("m_gemstone") is { } gem) Host.SetVisible(gem, false);
        if (RegionCn() && Host.Child("m_merat") is { } merat) Host.SetVisible(merat, false);
        if (Host?.Entity != null) Host.Invoke("updateAlign");
    }

    /// <summary>
    /// vfunc_28 (@0x1419d9f30): the check boxes' OnCheckEquip, OnCheckConsume, OnCheckEtc, onCheckGemStone, OnCheckMeso,
    /// OnCheckMerat, OnCheckToken, OnCheckOther and onCheckPet set their option from the argument (nothing without one),
    /// onChangeEquipRank the grade of the row (row 0 to 5 is grade 1 to 6, any other row grade 1), OnClickOk applies and closes.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        var o = Options;
        switch (name.ToLowerInvariant())
        {
            case "oncheckequip": if (Arg(args) is bool a) o.Equip = a; return true;
            case "oncheckconsume": if (Arg(args) is bool b) o.Consume = b; return true;
            case "onchecketc": if (Arg(args) is bool c) o.Etc = c; return true;
            case "oncheckgemstone": if (Arg(args) is bool d) o.GemStone = d; return true;
            case "oncheckmeso": if (Arg(args) is bool e) o.Meso = e; return true;
            case "oncheckmerat": if (Arg(args) is bool f) o.Merat = f; return true;
            case "onchecktoken": if (Arg(args) is bool g) o.Token = g; return true;
            case "oncheckother": if (Arg(args) is bool h) o.Other = h; return true;
            case "oncheckpet": if (Arg(args) is bool i) o.Pet = i; return true;
            case "onchangeequiprank":
                if (args is { Length: > 0 }) { int row = Rt.ToInt32(args[0]); o.EquipGrade = row is >= 0 and < GradeRows ? row + 1 : 1; }
                return true;
            case "onclickok": ApplyOptions(); Close(); return true;
        }
        return false;
    }

    static bool? Arg(object[] args) => args is { Length: > 0 } ? Rt.ToBoolean(args[0]) : null;

    /// <summary>LoadOptions (@0x1419da8f0): mode 1 the reset options (FUN_141260ed0) read from the character file
    /// (LoadGameOptionAutoLooting), here a copy of the saved ones; mode 0 the pet info's flags (STILL-OPEN: the defaults).</summary>
    public void LoadOptions()
    {
        Options = new GameOption.AutoLoot();
        if (Mode != 0 && Saved() is { } s) Copy(s, Options);
    }

    /// <summary>ApplyOptions (@0x1419dabc0): mode 1 copies the options into the character's (FUN_141261310) and saves the
    /// character settings; mode 0 sends RequestPet 6 with the flags (raised: not ported).</summary>
    public void ApplyOptions()
    {
        if (Mode != 0)
        {
            if (Saved() is { } s) Copy(Options, s);
            SaveCharacterSettings();
        }
        else Button?.Invoke("RequestPet6");
    }

    /// <summary>FUN_141261310: every option copied.</summary>
    static void Copy(GameOption.AutoLoot from, GameOption.AutoLoot to)
    {
        to.Equip = from.Equip; to.Consume = from.Consume; to.Etc = from.Etc; to.Meso = from.Meso; to.Merat = from.Merat;
        to.Token = from.Token; to.Other = from.Other; to.GemStone = from.GemStone; to.Pet = from.Pet; to.EquipGrade = from.EquipGrade;
    }
}
