using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>A learned emotion (CSkillSystem::stEmotion, skillsystem.h:55) with the new mark the dialogs read.</summary>
public sealed class Emotion
{
    public EmoteEntry Entry;
    /// <summary>+0x18: set when learned by Emote mode 1, cleared by CSkillSystem::FUN_141769280 (skillslot.cpp:2858).</summary>
    public bool IsNew;

    public int Id => Entry.Id;
    public ushort Level => Entry.Level;

    /// <summary>stEmotion::FUN_1417643d0 (@0x1417643d0, skillslot.cpp:1778): expiry 0 never ends, else kept while
    /// expiry >= now.</summary>
    public bool IsValidAt(long now) => Entry.Expire == 0 || Entry.Expire >= now;
}

/// <summary>
/// The local player's emotions (CSkillSystem m_vEmotions) as the Emote packet (0x1F, CSceneStateGame::OnEmote,
/// scenestategame.cpp:2533) fills them, and the Emote sends (0x13) the client makes.
/// </summary>
public sealed class EmotionList
{
    /// <summary>The common string shown for Emote mode 3 result 1 (FUN_140d73bc0(0x858), scenestategame.cpp:2566).</summary>
    public const int ResultOneString = 0x858;
    /// <summary>The common string shown for Emote mode 3 result 0 (FUN_140d73bc0(0x857), scenestategame.cpp:2568).</summary>
    public const int ResultZeroString = 0x857;
    /// <summary>Unk_Inventory::SendEmotion's notice for a missing player or a non-emotion item (inventory.cpp:1617-1620).</summary>
    public const int NotEmotionItemString = 2135;
    /// <summary>Unk_Inventory::SendEmotion's notice for an emotion already learned (inventory.cpp:1621).</summary>
    public const int AlreadyLearnedString = 2136;
    /// <summary>The item type of an emotion item on the KMS data (CItemData::GetType() == 24, inventory.cpp:1620).</summary>
    public const int EmotionItemType = Items.ItemData.EmotionItemType;

    readonly List<Emotion> emotions = new();

    public IReadOnlyList<Emotion> All => emotions;

    /// <summary>Raised with the id of an emotion learned by Emote mode 1 (NotifyLearnEmotion, scenestategame.cpp:2561).</summary>
    public event Action<int> Learned;
    /// <summary>Raised with the common string id of an Emote mode 3 result.</summary>
    public event Action<int> Notice;

    /// <summary>CSceneStateGame::OnEmote (@0x141712a90): mode 0 clears and refills the list, mode 1 adds one (as new),
    /// mode 3 shows 0x858 for 1, 0x857 for 0, nothing for other codes. Returns false for a null message.</summary>
    public bool Apply(EmoteMessage m, long now)
    {
        switch (m)
        {
            case EmoteMessage.List l:
                emotions.Clear();
                foreach (var e in l.Entries) Add(e, false, now);
                return true;
            case EmoteMessage.Learn learn:
                Add(learn.Entry, true, now);
                Learned?.Invoke(learn.Entry.Id);
                return true;
            case EmoteMessage.Result res:
                if (res.Code == 1) Notice?.Invoke(ResultOneString);
                else if (res.Code == 0) Notice?.Invoke(ResultZeroString);
                return true;
        }
        return false;
    }

    /// <summary>CSkillSystem::AddEmotion (skillslot.cpp:2790): skipped when the id and level are already learned
    /// (and valid now).</summary>
    void Add(EmoteEntry e, bool isNew, long now)
    {
        if (IsLearned(e.Id, e.Level, now)) return;
        emotions.Add(new Emotion { Entry = e, IsNew = isNew });
    }

    /// <summary>CSkillSystem::IsEmotionLearned (@0x141768fb0, skillslot.cpp:2808): an emotion of this id and level that is
    /// valid now. STILL-OPEN: the two hide-and-seek emotions (0x5607ed1, 0x5607ed2) count as learned in the hide-and-seek
    /// map or with the hide-and-seek info (skillslot.cpp:2810-2817).</summary>
    public bool IsLearned(int id, ushort level, long now)
    {
        for (int i = 0; i < emotions.Count; i++)
        {
            var e = emotions[i];
            if (!e.IsValidAt(now)) continue;
            if (e.Id == id && e.Level == level) return true;
        }
        return false;
    }

    /// <summary>CSkillSystem::GetValidEmotions (@0x1417691a0, skillslot.cpp:2832): the emotions valid now, in list order.</summary>
    public void GetValid(long now, List<Emotion> output)
    {
        for (int i = 0; i < emotions.Count; i++)
            if (emotions[i].IsValidAt(now)) output.Add(emotions[i]);
    }

    /// <summary>CSkillSystem::FUN_141769280 (skillslot.cpp:2858): clear every new mark.</summary>
    public void ClearNew()
    {
        for (int i = 0; i < emotions.Count; i++) emotions[i].IsNew = false;
    }

    /// <summary>Unk_SsEmotionLess (@0x141764410, skillslot.cpp:1787): permanent first, then permanent ones by id, the
    /// others by sort time, ties by id (ids unsigned).</summary>
    public static int CompareAscending(Emotion a, Emotion b)
    {
        if (a.Entry.Permanent != b.Entry.Permanent) return a.Entry.Permanent ? -1 : 1;
        if (!a.Entry.Permanent && a.Entry.SortTime != b.Entry.SortTime) return a.Entry.SortTime.CompareTo(b.Entry.SortTime);
        return ((uint)a.Id).CompareTo((uint)b.Id);
    }

    /// <summary>
    /// Unk_Inventory::SendEmotion (@0x140a65bb0, inventory.cpp:1615): an emotion item (type 24; GMS2 data subtype 14, the
    /// GMS2 copy @0x1403333d0, <see cref="Items.ItemData.IsEmotionItem"/>) whose skill id and level are not learned sends
    /// Emote mode 1 with the item uid; otherwise the notice string comes back and nothing is sent. Returns the request, or
    /// null with <paramref name="notice"/> set.
    /// </summary>
    public EmoteLearnRequest LearnFromItem(long itemUid, bool emotionItem, int skillId, ushort skillLevel, long now, out int notice)
    {
        notice = 0;
        if (!emotionItem) { notice = NotEmotionItemString; return null; }
        if (IsLearned(skillId, skillLevel, now)) { notice = AlreadyLearnedString; return null; }
        return new EmoteLearnRequest(itemUid);
    }
}
