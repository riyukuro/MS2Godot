namespace Ms2.Ui.Items;

/// <summary>
/// The GMS2 client's skill book item (<see cref="ItemData.IsGms2SkillBook"/>, file subtype 8): its double click
/// (InvenSlot::vfunc_13 @0x140ab83e5-0x140ab8820) runs the job system's check @0x14057d8b0 (a CJobSystemCli method, vtable
/// 0x141905fd8) and shows a box per result; the OK of the "learn_skill" box sends Job mode 7 (KMS
/// UIMessageBox::HandleLearnSkillButton @0x1419fb7b0, uimessagebox.cpp:1294-1309, the same handler in GMS2 @0x140dc24ba).
/// Read from the GMS2 x64 dump's disassembly (not decompiled).
/// </summary>
public static class SkillBookItem
{
    /// <summary>The learn box's tag (KMS s_143af3c88 "learn_skill", uimessagebox.cpp:71; GMS2 the global CStringW set up
    /// @0x1400d8bf0 from L"learn_skill").</summary>
    public const string LearnTag = "learn_skill";

    /// <summary>The check's results (@0x14057d8b0, in its order).</summary>
    public const int Ok = 0, LowLevel = 1, WrongJob = 3, Learned = 4, Expired = 5, PcBang = 6, Vip = 7, Wedding = 8,
        NotUsable = 9, NoItem = 10;

    /// <summary>The player the check reads: the job system's owner PC ([this+0xb8]): its level (vfunc 0x278, compared as a u16),
    /// the job system's job (@0x140b9a9d0 -> @0x1403b8020, the job data's +0x100), PC bang (@0x140b9eec0), PC::IsVip
    /// (@0x140ba7dc0, KMS 0x141662de0) and the wedding state (vfunc 0x4e0; INFERRED: a wedding in progress).</summary>
    public readonly record struct Owner(int Level, int Job, bool InPcBang, bool IsVip, bool WeddingInProgress);

    /// <summary>
    /// The job system's check @0x14057d8b0: 10 without the owner PC, the item's data or a skill book (@0x140343fa0); 3 a job
    /// <see cref="ItemData.IsGms2JobAllowed"/> refuses (@0x140350640); 1 a level below levelLimit; an item CItem's use test
    /// refuses (@0x14033af40, <see cref="ItemUse.Usable"/>): 5 when expired (@0x14033af00), else 9; 6 a PC bang item outside
    /// one (@0x140343ae0); 7 a VIP item without VIP (limit +0x84); 8 a wedding item outside a wedding (limit +0x88); 4 the
    /// item's skill learned already (the job system's vfunc_2 on [itemData+0x260], slot 2 @0x14057cc50, the shape of KMS
    /// JobSystemCli::vfunc_2 @0x140a6da90 but with the GMS2 kinds short-cut, <see cref="Skills.JobSystem.IsLearnedGms2"/>); else 0.
    /// <paramref name="isLearned"/> null counts the skill not learned.
    /// STILL-OPEN: the use test's later part (@0x14033af8c on, the +0x7f4 flag), which <see cref="ItemUse.Usable"/> keeps
    /// open as the music score checks; so 9 is never returned here.
    /// </summary>
    public static int Check(Item item, Owner? owner, long serverTime, Func<uint, bool> isLearned)
    {
        if (owner is not { } pc || item?.Data is not { } d || !d.IsGms2SkillBook) return NoItem;
        if (!d.IsGms2JobAllowed(pc.Job)) return WrongJob;
        if ((ushort)pc.Level < (uint)d.LevelLimit) return LowLevel;
        bool expired = item.ExpiryTime > 0 && item.ExpiryTime <= serverTime;
        bool usable = !(expired && !d.IsPetItem && !d.HasMaidId);      // ItemUse.Usable's rule
        if (!usable) return expired ? Expired : NotUsable;
        if (d.PCBang && !pc.InPcBang) return PcBang;
        if (d.Vip && !pc.IsVip) return Vip;
        if (d.Wedding && !pc.WeddingInProgress) return Wedding;
        if (isLearned?.Invoke((uint)d.SkillId) == true) return Learned;
        return Ok;
    }

    /// <summary>
    /// The common string key of each result's box (GMS2 InvenSlot::vfunc_13 @0x140ab8439-0x140ab8820, the GMS2 common
    /// string ids in brackets): 3 s_msg_skillbook_error_job (0x2ec), 1 s_msg_skillbook_error_level (0x2ed), 4 s_msg_skillbook_error_master (0x2ee),
    /// 5 s_msg_skillbook_error_expired (0x2ef), 6 s_item_err_puton_pcbang (0xe1), 7 s_item_err_puton_vip (by name); 0 is the learn box
    /// (s_msg_skillbook_learn_messagebox, 0x2ea); 2, 8, 9 and 10 show nothing. The KMS common string ids differ, so the keys
    /// are named.
    /// </summary>
    public static string BoxKey(int result) => result switch
    {
        Ok => "s_msg_skillbook_learn_messagebox",
        WrongJob => "s_msg_skillbook_error_job",
        LowLevel => "s_msg_skillbook_error_level",
        Learned => "s_msg_skillbook_error_master",
        Expired => "s_msg_skillbook_error_expired",
        PcBang => "s_item_err_puton_pcbang",
        Vip => "s_item_err_puton_vip",
        _ => null,
    };
}
