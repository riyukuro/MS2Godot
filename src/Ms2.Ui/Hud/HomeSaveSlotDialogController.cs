using Ms2.Avm2;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>What the home save slot dialog reaches outside itself (UIHomeSaveSlotDialog, unit_140fa9eb0_uihomesaveslotdialog.cpp).</summary>
public interface IHomeSaveSlotHost
{
    /// <summary>The CubeMapInformationService exists (the client tests the smart pointer).</summary>
    bool HasService { get; }
    /// <summary>CubeMapInformationService::FUN_1410c3600(mode): the plot mode (f330) equals <paramref name="mode"/> (1 design, 3 blueprint).</summary>
    bool PlotModeIs(int mode);
    /// <summary>FUN_1410d4660: the design slots (+0x388).</summary>
    IReadOnlyList<UgcMapLayout> Designs { get; }
    /// <summary>FUN_1410d4920: the blueprint slots (+0x3a8).</summary>
    IReadOnlyList<UgcMapLayout> Blueprints { get; }
    /// <summary>DAT_143ab2914: the GM level of the account.</summary>
    int GmLevel { get; }
    /// <summary>FUN_1410d2f10: the current field is a home field.</summary>
    bool InHomeField { get; }
    /// <summary>FUN_1408a06a0(FUN_1410c3140(), FUN_1410c3100()): the plot's group is admin only (ugcmapdata_1.cpp:150).</summary>
    bool PlotAdminOnly { get; }
    /// <summary>GetLanguage (the date text's field order).</summary>
    int Language { get; }
    /// <summary>RequestCube 0x2e: save the design into the slot.</summary>
    void SendSaveDesign(int slot, string name);
    /// <summary>RequestCube 0x2f: load the design of the slot (the blocked form).</summary>
    void SendLoadDesignBlocked(int slot);
    /// <summary>SendRequestCubeMode35 (@0x140ee8360): the other design load.</summary>
    void SendLoadDesign(int slot);
    /// <summary>RequestCube 0x40: save the blueprint into the slot.</summary>
    void SendSaveBlueprint(int slot, string name);
    /// <summary>RequestCube 0x41: load the blueprint of the slot.</summary>
    void SendLoadBlueprint(int slot);
}

/// <summary>
/// UIHomeSaveSlotDialog (dialog 0x4f27, GetTypeName "HomeSaveSlotDialog"; unit_140fa9eb0_uihomesaveslotdialog.cpp): the save and load slots
/// of the home designer (m_saveMode +0x178, 1 save, 0 load) and of the blueprint designer (the second slot list).
/// STILL-OPEN: the own account's GM level and the plot's admin-only group (<see cref="IHomeSaveSlotHost.GmLevel"/>, PlotAdminOnly) are not known
/// here; FUN_14016a730(name, 0) (the code page conversion of the slot name) is taken as the name itself; SendRequestCubeMode35's
/// bill state (UIUGCCubeBillDialog::s_pending, the close of dialog 0x4f29) is not ported.
/// </summary>
public class HomeSaveSlotDialogController : UiController
{
    public const int UiId = 0x4f27;
    /// <summary>The slots the movie shows (FUN_140faa2d0 loops 0..4).</summary>
    public const int SlotCount = 5;

    public override string Movie => "uihomesaveslotdialog";
    protected override string EntityMethod => "setSlot";
    public override string TypeName => "HomeSaveSlotDialog";

    readonly IHomeSaveSlotHost host;
    /// <summary>m_saveMode (+0x178): the constructor sets 1.</summary>
    public int SaveMode { get; private set; } = 1;

    public HomeSaveSlotDialogController(IHomeSaveSlotHost host) { this.host = host; }

    /// <summary>GetStringCommonByKey.</summary>
    public Func<string, string> Str = key => MessageBoxData.CommonByKey(key) ?? "";
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>m_30->Invoke(name, args).</summary>
    public Action<string, object[]> CallMovie;
    /// <summary>m_30->SetIntMember(name, value).</summary>
    public Action<string, int> SetIntMember;
    /// <summary>FUN_141924630: the movie is loaded.</summary>
    public Func<bool> MovieLoaded = () => true;
    /// <summary>FUN_141924680: the dialog opens.</summary>
    public Action Open = () => { };

    void Call(string name, params object[] args)
    {
        if (CallMovie != null) CallMovie(name, args);
        else Host.Invoke(Host.Entity, name, args);
    }

    /// <summary>OnLoadComplete @0x140fa9fb0: UIDialog's, then saveMode, then UpdateSlots.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        PutSaveMode();
        UpdateSlots();
    }

    void PutSaveMode()
    {
        if (SetIntMember != null) SetIntMember("saveMode", SaveMode);
        else Host.Set(Host.Entity, "saveMode", SaveMode);
    }

    /// <summary>SetSaveMode @0x140faa6d0.</summary>
    public void SetSaveMode() => SetMode(1);

    /// <summary>SetLoadMode @0x140faa750.</summary>
    public void SetLoadMode() => SetMode(0);

    void SetMode(int mode)
    {
        SaveMode = mode;
        if (MovieLoaded()) PutSaveMode();
        else Open();
    }

    /// <summary>FUN_140fac0a0 (@0x140fac0a0): "" for time 0, else the date and "%02d:%02d:%02d".</summary>
    public string TimeText(long time)
    {
        if (time == 0) return "";
        var t = MsTime.LocalTm(time);
        return $"{DateText(t, host.Language, "/")} {t.Hour:D2}:{t.Minute:D2}:{t.Second:D2}";
    }

    /// <summary>FUN_14083bab0 (@0x14083bab0): the date text; the separator "/" becomes the language's (5 ".").</summary>
    public static string DateText(DateTime t, int language, string separator)
    {
        if (separator == "/") separator = language == 5 ? "." : "/";
        string s = separator;
        return language switch
        {
            2 => $"{t.Month:D2}{s}{t.Day:D2}{s}{t.Year:D4}",
            4 or 5 or 6 or 7 => $"{t.Day:D2}{s}{t.Month:D2}{s}{t.Year:D4}",
            _ => $"{t.Year:D4}{s}{t.Month:D2}{s}{t.Day:D2}",
        };
    }

    /// <summary>UpdateSlots @0x140faa2d0: setSlot(id, name, time) for each of the first five slots the list has (none for a missing one).</summary>
    public void UpdateSlots()
    {
        var slots = host.PlotModeIs(3) ? host.Blueprints : host.Designs;
        for (int i = 0; i < SlotCount; i++)
            if (i < slots.Count) Call("setSlot", slots[i].Id, slots[i].Name ?? "", TimeText(slots[i].Time));
    }

    /// <summary>vfunc_28 @0x140faa070: OnSaveSlot and OnLoadSlot (compared without case), each with an argument and the service.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        bool save = string.Equals(name, "OnSaveSlot", StringComparison.OrdinalIgnoreCase);
        if (!save && !string.Equals(name, "OnLoadSlot", StringComparison.OrdinalIgnoreCase)) return false;
        if (args is { Length: > 0 } && host.HasService)
        {
            int slot = Rt.ToInt32(args[0]);
            bool blueprint = host.PlotModeIs(3);
            if (save) { if (blueprint) AskBlueprintSave(slot); else AskSave(slot); }
            else { if (blueprint) AskBlueprintLoad(slot); else AskLoad(slot); }
        }
        return false;
    }

    string Slotted(string key, int slot) => ClientFormat.Format(Str(key), slot);

    MessageBoxData NameBox(int slot, Func<int, MessageBoxData, bool> callback)
    {
        var box = MessageBoxQueue.Create(5, Slotted("s_home_save_messagebox", slot), null, "");
        box.Append(slot);
        box.SetButtonLabels(Str("s_home_save_btn_text"), Str("s_word_cancel"), "");
        box.SetInput(0, Str("s_home_save_title_empty"), 0x14, 1, 1);
        box.Callback = callback;
        return box;
    }

    MessageBoxData YesNoBox(string text, int slot, Func<int, MessageBoxData, bool> callback)
    {
        var box = MessageBoxQueue.Create(1, text, null, "");
        box.Append(slot);
        box.Callback = callback;
        return box;
    }

    /// <summary>FUN_140fab520 (@0x140fab520): the name box of a design save, slots 0..5.</summary>
    public void AskSave(int slot) { if (slot is >= 0 and <= 5) ShowBox(NameBox(slot, OnSaveNameEntered)); }

    /// <summary>FUN_140fabae0 (@0x140fabae0): the same for a blueprint.</summary>
    public void AskBlueprintSave(int slot) { if (slot is >= 0 and <= 5) ShowBox(NameBox(slot, OnBlueprintSaveNameEntered)); }

    /// <summary>AskLoadSlot (@0x140fab8c0).</summary>
    public void AskLoad(int slot) { if (slot is >= 0 and <= 5) ShowBox(YesNoBox(Slotted("s_home_load_messagebox", slot), slot, OnLoadConfirmed)); }

    /// <summary>FUN_140fabe80 (@0x140fabe80).</summary>
    public void AskBlueprintLoad(int slot) { if (slot is >= 0 and <= 5) ShowBox(YesNoBox(Slotted("s_home_blueprint_load_messagebox", slot), slot, SendRequestCubeMode65)); }

    static string EnteredName(MessageBoxData data) => (data.ClickedTag ?? "").Trim(',');

    bool Overwrites(IReadOnlyList<UgcMapLayout> slots, int slot)
    {
        foreach (var s in slots) if (s.Id == slot && s.Time != 0) return true;
        return false;
    }

    bool NameEntered(int result, MessageBoxData data, IReadOnlyList<UgcMapLayout> slots, Func<int, MessageBoxData, bool> overwrite, Action<int, string> send)
    {
        if (result != 0 || !host.HasService) return true;
        int slot = data.ArgInt(0, 0);
        string name = EnteredName(data);
        if (Overwrites(slots, slot))
        {
            var box = MessageBoxQueue.Create(1, Str("s_home_save_btn_overwrite"), null, "");
            box.Append(slot);
            box.Args.Add(name);
            box.Callback = overwrite;
            ShowBox(box);
            return true;
        }
        send(slot, name);
        return true;
    }

    /// <summary>OnSaveNameEntered @0x140faa7d0: a used slot asks first, else RequestCube 0x2e.</summary>
    bool OnSaveNameEntered(int result, MessageBoxData data) => NameEntered(result, data, host.Designs, SendRequestCubeMode46, host.SendSaveDesign);

    /// <summary>OnBlueprintSaveNameEntered @0x140faaeb0: the same on the blueprint slots, RequestCube 0x40.</summary>
    bool OnBlueprintSaveNameEntered(int result, MessageBoxData data) => NameEntered(result, data, host.Blueprints, SendRequestCubeMode64, host.SendSaveBlueprint);

    /// <summary>SendRequestCubeMode46 @0x140faadb0.</summary>
    bool SendRequestCubeMode46(int result, MessageBoxData data)
    {
        if (result == 0) host.SendSaveDesign(data.ArgInt(0, 0), data.Args.Count > 1 ? data.Args[1] : "");
        return true;
    }

    /// <summary>SendRequestCubeMode64 @0x140fab420.</summary>
    bool SendRequestCubeMode64(int result, MessageBoxData data)
    {
        if (result == 0) host.SendSaveBlueprint(data.ArgInt(0, 0), data.Args.Count > 1 ? data.Args[1] : "");
        return true;
    }

    /// <summary>SendRequestCubeMode65 @0x140fab310: needs the service and blueprint mode.</summary>
    bool SendRequestCubeMode65(int result, MessageBoxData data)
    {
        if (result != 0 || !host.HasService) return true;
        if (host.PlotModeIs(3)) host.SendLoadBlueprint(data.ArgInt(0, 0));
        return true;
    }

    /// <summary>OnLoadConfirmed @0x140faac30: blocked (design mode, or a GM level 50 up in a home field with an admin only group) sends 0x2f, else mode 35.</summary>
    bool OnLoadConfirmed(int result, MessageBoxData data)
    {
        if (result != 0 || !host.HasService) return true;
        int slot = data.ArgInt(0, 0);
        bool blocked = host.PlotModeIs(1);
        if (!blocked && host.GmLevel >= 50 && host.InHomeField) blocked = host.PlotAdminOnly;
        if (blocked) host.SendLoadDesignBlocked(slot);
        else host.SendLoadDesign(slot);
        return true;
    }
}
