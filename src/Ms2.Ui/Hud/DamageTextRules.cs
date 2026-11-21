using System;
using System.Collections.Generic;
using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIDamageText's names and texts: what FUN_141911e90 (@0x141911e90), FUN_141912b20 (@0x141912b20) and FUN_141913060
/// (@0x141913060) put in the object they pass to the movie's damageText / itemIcon.
/// </summary>
public static class DamageTextNames
{
    public const string User = "User", Monster = "Monster", Normal = "Normal", Critical = "Critical", Recover = "Recover";
    public const string Exp = "Exp", Meso = "Meso", Item = "Item", Miss = "Miss", Block = "Block";

    /// <summary>IsDamageGrade (@0x1419144f0): grades 0 to 3 (the recover grades are <see cref="DamageNumbers.IsRecover"/>).</summary>
    public static bool IsDamageGrade(int grade) => grade is >= 0 and <= 3;

    /// <summary>bGem's grade and target part (uidamagetext.cpp:354): FUN_141914530 (:809, grades 5 / 6 / 7) is 0 and the
    /// target is not the own PC; the caster being a PC is the caller's (a gem only a PC wears).</summary>
    public static bool GemShows(int grade, bool targetMe) => grade is not (5 or 6 or 7) && !targetMe;

    /// <summary>GetGradeName (@0x141914570): 0, 2, 3 Normal; 1 Critical; 5, 6, 7 Recover; else empty.</summary>
    public static string GradeName(int grade) => grade switch
    {
        0 or 2 or 3 => Normal,
        1 => Critical,
        5 or 6 or 7 => Recover,
        _ => "",
    };

    /// <summary>GetDamageValueText (@0x141914660): 0 and 1 the number, 5 "+" and the number, 2 Miss, 3 Block, else empty
    /// (the recover grades 6 and 7 show no number).</summary>
    public static string ValueText(long value, int grade) => grade switch
    {
        0 or 1 => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        5 => "+" + value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        2 => Miss,
        3 => Block,
        _ => "",
    };

    /// <summary>
    /// <see cref="ValueText"/> written into <paramref name="dst"/> (at least 21 chars), with no string made per hit: the
    /// length, 0 for an empty text.
    /// </summary>
    public static int ValueChars(long value, int grade, Span<char> dst)
    {
        switch (grade)
        {
            case 0 or 1: return Digits(value, dst);
            case 5: dst[0] = '+'; return 1 + Digits(value, dst[1..]);
            case 2: Miss.AsSpan().CopyTo(dst); return Miss.Length;
            case 3: Block.AsSpan().CopyTo(dst); return Block.Length;
            default: return 0;
        }
    }

    /// <summary>What <see cref="DamageLook.For"/> reads of a value (only DamageTextCommon.isNumber): Miss, Block, else "".</summary>
    public static string ValueKind(int grade) => grade switch { 2 => Miss, 3 => Block, _ => "" };

    /// <summary><see cref="GainValueText"/> written into <paramref name="dst"/> (at least 12 chars): the length.</summary>
    public static int GainValueChars(int value, int type, Span<char> dst)
    {
        switch (type)
        {
            case 0 or 1: return Digits(value, dst);
            case 2: dst[0] = '+'; return 1 + Digits(value, dst[1..]);
            default: return 0;
        }
    }

    static int Digits(long v, Span<char> dst) => v.TryFormat(dst, out int n, default, System.Globalization.CultureInfo.InvariantCulture) ? n : 0;

    /// <summary>GetTargetKind (@0x1419142a0): 0 for a PC (class 0x6200004, 0x6200003 or 0x6200011), else 1.</summary>
    public static int TargetKind(bool pc) => pc ? 0 : 1;

    /// <summary>GetTargetKindName (@0x1419143c0): a PC is User when <paramref name="user"/>, else Monster; kind 1 Monster.</summary>
    public static string TargetKindName(int kind, bool user) => kind switch
    {
        0 => user ? User : Monster,
        1 => Monster,
        _ => "",
    };

    /// <summary>FUN_141914890 (@0x141914890): the value of a gain, type 0 exp and 1 meso the number, 2 item "+" and the count.</summary>
    public static string GainValueText(int value, int type) => type switch
    {
        0 or 1 => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        2 => "+" + value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "",
    };

    /// <summary>FUN_1419149d0 (@0x1419149d0): the grade of a gain, 0 Exp, 1 Meso, 2 Item.</summary>
    public static string GainGradeName(int type) => type switch
    {
        0 => Exp,
        1 => Meso,
        2 => Item,
        _ => "",
    };

    /// <summary>FUN_141912b20's show test: an item always, a meso when DisplayGetMeso, exp when DisplayGetExp.</summary>
    public static bool GainShows(int type, bool displayMeso, bool displayExp) => type == 2 || (type == 1 ? displayMeso : displayExp);

    /// <summary>OnExpUp (scenestategame.cpp:4160): the exp types whose number waits in the gain queue (FUN_1419129d0)
    /// instead of showing at once.</summary>
    public static bool QuestExp(int type) => type is 0xc1f or 0xc20 or 0xc21 or 0xc22;
}

/// <summary>
/// LoadLayoutConstants (@0x141913a20): DamageGap the line height, DamageRenderCount the newest numbers shown per owner
/// (m_nJumpCount, at least 1), DamageRenderTotalCount the numbers shown in a frame (m_nMaxShown, at least 1), the list
/// keeps 10 more (m_nMaxList), indices wrap at 1000 (m_nMaxIndex). The defaults are the client's compiled ones
/// (constantsblocks.cpp:1472-1476); the tables set them.
/// </summary>
public readonly record struct DamageTextLayout(int LineHeight, int JumpCount, int MaxShown, int MaxList, int MaxIndex, float OtherScale, float OtherAlpha, float ShowDistance)
{
    public static DamageTextLayout From(ClientConstants c)
    {
        int shown = Math.Max(1, c?.Int("DamageRenderTotalCount", 0) ?? 0);
        return new DamageTextLayout(c?.Int("DamageGap", 0) ?? 0, Math.Max(1, c?.Int("DamageRenderCount", 0) ?? 0), shown, shown + 10, 1000,
            c?.Float("DamageOtherScale", 0) ?? 0, c?.Float("DamageOtherAlpha", 0) ?? 0, c?.Float("DamageDistance", 1500) ?? 1500);
    }

    /// <summary>UpdateNormals' line heights (@0x141915990): mine (int)(gap x scale step / 100); another's the same, or
    /// (int)(gap x DamageOtherScale) when the battle option's other-damage form has bit 1 (size); the critical lift is twice each.</summary>
    public (int Mine, int Other) Lines(int scaleStep, int otherForm)
    {
        int mine = (int)((float)(LineHeight * scaleStep) / 100f);
        int other = (otherForm & 1) != 0 ? (int)((float)LineHeight * OtherScale) : mine;
        return (mine, other);
    }
}

/// <summary>
/// What the movie's DamageTextFactory builds for one damageText(obj) / itemIcon call (as_uidamagetext, DamageTextCommon):
/// the glyph classes, the DamageMovie's letter spacing, the ani, its critical text and extra effect, and the container.
/// <see cref="Glyphs"/> null means the factory has no number array for the target and grade: the AS call fails and the
/// client makes no number (FUN_1419175c0 needs an object back).
/// </summary>
public sealed record DamageLook(string Glyphs, int SignSpacing, int NumberSpacing, DamageAni Ani, string CritText, string ExEffect, int Container)
{
    /// <summary>The draw order of the factory's containers: m_listNormal, m_listCritical, m_myDamageNormal, m_myDamageCritical.</summary>
    public const int ListNormal = 0, ListCritical = 1, MyNormal = 2, MyCritical = 3;

    /// <summary>
    /// The gem font whose arrays the glyphs and the critical text come from (its id as text, the uidamagefont_&lt;id&gt; movie);
    /// null for the damage text movie's own.
    /// </summary>
    public string Font { get; init; }

    /// <summary>
    /// DamageTextFactory.damageText: <paramref name="caster"/> is dropped when the number is not <paramref name="myDamage"/>.
    /// getDamageNumberArray: DamageTextCommon[caster + target + grade + gem], else without the gem. getDamageMovie:
    /// grade + "Movie", else NormalMovie. getDamageAni / getCriticalTxt / getExEff by their keys; getContainer(grade, myDamage).
    /// The client always sends an empty caster (FUN_141911e90).
    /// <para>
    /// The gem keys are the ones DamageTextCommon.loadDamageFont(id) adds once the font movie is loaded
    /// (<paramref name="gemLoaded"/>; GFxMovieDamage::LoadDamageFont, gfxmoviedamage.cpp:210, then UIDamageText::LoadFont on its
    /// loadComplete, :170): MonsterNormal&lt;id&gt; (Nor0_&lt;id&gt; .. Nor9_&lt;id&gt;, Miss_&lt;id&gt;, Block_&lt;id&gt;),
    /// MonsterCritical&lt;id&gt; (Cri0_&lt;id&gt; .. Cri9_&lt;id&gt;) and MonsterCriticalTxt&lt;id&gt; (dmg_mc_monstercri_&lt;id&gt;).
    /// So only a Monster target's Normal (Miss and Block too) and Critical take the font; without it loaded the lookup
    /// falls back to the plain array.
    /// </para>
    /// </summary>
    public static DamageLook For(string value, string target, string grade, string caster = "", string gem = "", bool myDamage = false, bool gemLoaded = false)
    {
        if (!myDamage) caster = "";
        string font = null;
        string glyphs = null;
        if (gemLoaded && !string.IsNullOrEmpty(gem) && GemArray(caster + target + grade) is { } g) { glyphs = g; font = gem; }
        glyphs ??= Array(caster + target + grade);
        var (sign, number) = Spacing(grade);
        var ani = DamageAni.For(value, target, grade);
        string crit = null;
        if (ani.HasCritText)
        {
            // MonsterCriticalTxt<id> is set with MonsterCritical<id>: a font's glyphs and its critical text go together
            crit = font != null && caster + target + grade + "Txt" == "MonsterCriticalTxt" ? "dmg_mc_monstercri_" + font : CritTxt(caster + target + grade + "Txt");
            if (crit == null) glyphs = null;      // getCriticalTxt news a null constructor: the call fails
        }
        string ex = ani.HasExEffect ? ExEff(IsNumber(value) ? target + grade + "Eff" : value + target + "Eff") : null;
        int container = (myDamage ? MyNormal : ListNormal) + (grade == DamageTextNames.Critical ? 1 : 0);
        return new DamageLook(glyphs, sign, number, ani, crit, ex, container) { Font = font };
    }

    /// <summary>The itemIcon call: the same picks, always in the own player's containers, no scale or alpha.</summary>
    public static DamageLook ForItem(string value, string target, string grade) => For(value, target, grade, "", "", true);

    /// <summary>DamageTextCommon.isNumber: anything but Miss and Block (an empty text is a number).</summary>
    public static bool IsNumber(string value) => value != DamageTextNames.Miss && value != DamageTextNames.Block;

    /// <summary>The glyph class prefix of a number array (initNumbers; UserExp = MonsterExp, UserItem = MonsterNormal,
    /// PCUserRecover = UserRecover). Every array's keys are the prefix plus the glyph key (initImageText: Miss, Block, Exp).</summary>
    static string Array(string key) => key switch
    {
        "UserNormal" => "User",
        "UserCritical" => "UserCri",
        "MonsterNormal" or "UserItem" => "Monster",
        "MonsterCritical" => "MonsterCri",
        "UserRecover" or "PCUserRecover" => "Recover",
        "UserRecoverCritical" => "RecoverCri",
        "UserMeso" => "Meso",
        "MonsterExp" or "UserExp" => "Exp",
        _ => null,
    };

    /// <summary>loadDamageFont's arrays: the digit class prefix of MonsterNormal&lt;id&gt; and MonsterCritical&lt;id&gt;.</summary>
    static string GemArray(string key) => key switch
    {
        "MonsterNormal" => "Nor",
        "MonsterCritical" => "Cri",
        _ => null,
    };

    /// <summary>initMovieClip: NormalMovie -12 / -15, CriticalMovie and RecoverCriticalMovie -12 / -17, ExpMovie and
    /// MesoMovie -14 / -10, ItemMovie -11 / -11; any other grade takes NormalMovie.</summary>
    static (int, int) Spacing(string grade) => grade switch
    {
        "Critical" or "RecoverCritical" => (-12, -17),
        "Exp" or "Meso" => (-14, -10),
        "Item" => (-11, -11),
        _ => (-12, -15),
    };

    /// <summary>initImageText's critical texts.</summary>
    static string CritTxt(string key) => key switch
    {
        "UserCriticalTxt" => "dmg_mc_pccri",
        "MonsterCriticalTxt" => "dmg_mc_monstercri",
        "PCMonsterCriticalTxt" => "dmg_mc_pcmonstercri",
        "UserRecoverCriticalTxt" => "dmg_mc_recovercri",
        _ => null,
    };

    /// <summary>initMovieClip's effects (MissPCEff is BlockUserEff there).</summary>
    static string ExEff(string key) => key switch
    {
        "BlockMonsterEff" => "dmg_mc_block_monster",
        "BlockUserEff" or "BlockPCEff" or "MissPCEff" => "dmg_mc_block_pc",
        "MissMonsterEff" => "dmg_mc_miss_monster",
        "MissUserEff" => "dmg_mc_miss_pc",
        _ => null,
    };

    /// <summary>The glyph key of a character (initNumbers): digits themselves, "+" Plus, "-" Minus; null for any other
    /// (the DamageMovie traces it and skips it).</summary>
    public static string GlyphKey(char c) => c switch
    {
        >= '0' and <= '9' => Digits[c - '0'],
        '+' => "Plus",
        '-' => "Minus",
        _ => null,
    };

    static readonly string[] Digits = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    /// <summary>The glyph keys past the digits 0 to 9 (<see cref="GlyphIndex"/>, <see cref="Glyph"/>).</summary>
    public const int PlusKey = 10, MinusKey = 11, MissKey = 12, BlockKey = 13, ExpKey = 14, MesoKey = 15;
    static readonly string[] KeyNames = [.. Digits, "Plus", "Minus", DamageTextNames.Miss, DamageTextNames.Block, DamageTextNames.Exp, DamageTextNames.Meso];

    /// <summary><see cref="GlyphKey"/> as an index: 0 to 9 the digits, <see cref="PlusKey"/>, <see cref="MinusKey"/>; -1 for any other.</summary>
    public static int GlyphIndex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        '+' => PlusKey,
        '-' => MinusKey,
        _ => -1,
    };

    string[] names;

    /// <summary>
    /// The class of the array's glyph for a key (the array's value at that key), made once per look: the array prefix and
    /// the key; in a gem font Nor / Cri, the digit and "_" id for a digit, Miss_&lt;id&gt; and Block_&lt;id&gt;, and none for
    /// the keys loadDamageFont does not set (Plus, Minus, Exp, Meso). Null without the array or for a key out of range.
    /// </summary>
    public string Glyph(int key)
    {
        if (Glyphs == null || (uint)key >= (uint)KeyNames.Length) return null;
        names ??= new string[KeyNames.Length];
        return names[key] ??= Font == null ? Glyphs + KeyNames[key]
            : key < 10 ? Glyphs + KeyNames[key] + "_" + Font
            : key is MissKey or BlockKey ? KeyNames[key] + "_" + Font : null;
    }
}

/// <summary>
/// The looks a damage text has used, by what <see cref="DamageLook.For"/> reads (the value only through isNumber), so a
/// hit after the first of its kind makes no look and no key strings.
/// </summary>
public sealed class DamageLookCache
{
    /// <summary>
    /// The key, a class so the dictionary runs the shared reference-type code the ReadyToRun image holds (a value tuple
    /// key's instantiation is not in it and would be compiled at the first hit). <see cref="probe"/> is the lookup's,
    /// so a hit makes no key; a new look stores a copy.
    /// </summary>
    sealed class Key : IEquatable<Key>
    {
        public string Kind, Target, Grade, Gem;
        public bool Mine, Item;
        public bool Equals(Key o) => o != null && Mine == o.Mine && Item == o.Item && string.Equals(Kind, o.Kind) && string.Equals(Target, o.Target)
            && string.Equals(Grade, o.Grade) && string.Equals(Gem, o.Gem);
        public override bool Equals(object o) => Equals(o as Key);
        public override int GetHashCode()
        {
            int h = (Kind?.GetHashCode() ?? 0) * 31 + (Target?.GetHashCode() ?? 0);
            h = h * 31 + (Grade?.GetHashCode() ?? 0);
            h = h * 31 + (Gem?.GetHashCode() ?? 0);
            return h * 4 + (Mine ? 2 : 0) + (Item ? 1 : 0);
        }
    }
    readonly Dictionary<Key, DamageLook> looks = new();
    readonly Key probe = new();

    /// <summary>
    /// <see cref="DamageLook.For"/> (<see cref="DamageLook.ForItem"/> when <paramref name="item"/>) with
    /// <paramref name="kind"/> from <see cref="DamageTextNames.ValueKind"/>; <paramref name="gem"/> the loaded gem font's
    /// id text, null or "" for none.
    /// </summary>
    public DamageLook Get(string kind, string target, string grade, string gem, bool mine, bool item = false)
    {
        gem ??= "";
        probe.Kind = kind; probe.Target = target; probe.Grade = grade; probe.Gem = gem; probe.Mine = mine; probe.Item = item;
        if (!looks.TryGetValue(probe, out var look))
            looks[new Key { Kind = kind, Target = target, Grade = grade, Gem = gem, Mine = mine, Item = item }] = look = item ? DamageLook.ForItem(kind, target, grade) : DamageLook.For(kind, target, grade, "", gem, mine, gem.Length > 0);
        return look;
    }

    public int Count => looks.Count;
}

/// <summary>
/// One ani class of the movie with the frames (0-based) whose frame script dispatches RemoveAni, and the child slots
/// damageText fills (dummy1 a copy of the number, mesoeff, cridmy the critical text, exeff the extra effect).
/// </summary>
public sealed record DamageAni(string Class, int[] Remove, bool HasCritText = false, bool HasExEffect = false, bool HasMesoEffect = false, bool HasCopy = false)
{
    public static readonly DamageAni NpcCritical = new("dmg_ani_npccri", [43], HasCritText: true);
    public static readonly DamageAni NpcNormal = new("dmg_ani_npcnormal", [28, 42]);
    public static readonly DamageAni PcCritical = new("dmg_ani_pccri", [39], HasCritText: true);
    public static readonly DamageAni PcNormal = new("dmg_ani_pcnormal", [34]);
    public static readonly DamageAni Recover = new("dmg_ani_recover", [28, 44]);
    public static readonly DamageAni RecoverCritical = new("dmg_ani_recovercri", [49], HasCopy: true);
    public static readonly DamageAni Miss = new("dmg_ani_miss", [28], HasExEffect: true);
    public static readonly DamageAni BlockMonster = new("dmg_ani_block_monster", [28], HasExEffect: true);
    public static readonly DamageAni BlockPc = new("dmg_ani_block_pc", [28], HasExEffect: true);
    public static readonly DamageAni Exp = new("dmg_ani_exp", [33]);
    public static readonly DamageAni Meso = new("dmg_ani_meso", [34], HasMesoEffect: true);
    public static readonly DamageAni ItemDown = new("dmg_ani_basic_down", [28]);

    /// <summary>getDamageAni(value, target, grade): value + target + "Ani" for Miss / Block, else target + grade + "Ani";
    /// then grade + "Ani"; then MissMonsterAni.</summary>
    public static DamageAni For(string value, string target, string grade) =>
        Named(DamageLook.IsNumber(value) ? target + grade + "Ani" : value + target + "Ani") ?? Named(grade + "Ani") ?? Miss;

    /// <summary>initAni's statics.</summary>
    static DamageAni Named(string key) => key switch
    {
        "MonsterCriticalAni" => NpcCritical,
        "MonsterNormalAni" => NpcNormal,
        "UserCriticalAni" => PcCritical,
        "UserNormalAni" => PcNormal,
        "UserRecoverAni" or "RecoverAni" => Recover,
        "UserRecoverCriticalAni" or "RecoverCriticalAni" => RecoverCritical,
        "MissMonsterAni" or "MissUserAni" or "MissPCAni" => Miss,
        "BlockMonsterAni" => BlockMonster,
        "BlockUserAni" or "BlockPCAni" => BlockPc,
        "MonsterExpAni" or "UserExpAni" => Exp,
        "UserMesoAni" => Meso,
        "UserItemAni" => ItemDown,
        _ => null,
    };
}

/// <summary>
/// UIDamageText::DamageInfo (@0x141910bf0): one number in a list. Type 0 a hit or heal, 1 a gain (exp, meso, item).
/// </summary>
public class DamageInfo
{
    public int Type, Grade, Index, State;     // State: 0 shown, 1 jumped, 3 hidden
    public object Owner;
    public bool Mine, CanJump = true;
    public float Scale = 1;
    public uint Start, Elapsed;

    /// <summary>Back to a new DamageInfo's fields, for an info reused from a pool.</summary>
    public void Reset()
    {
        Type = Grade = Index = State = 0;
        Owner = null;
        Mine = false; CanJump = true;
        Scale = 1;
        Start = Elapsed = 0;
    }

    /// <summary>FUN_141910e60 (@0x141910e60): the time since the first update (0 before it; a clock that went back restarts it).</summary>
    public void Tick(uint now)
    {
        if (Start == 0) Elapsed = 0;
        else if (Start > now) { Start = now; Elapsed = 0; }
        else Elapsed = now - Start;
    }

    /// <summary>FUN_141910ee0 (@0x141910ee0): the first update starts the clock.</summary>
    public void Begin(uint now) { if (Start == 0) Start = now; }

    /// <summary>FUN_141910f10 (@0x141910f10): over 1000 ms, the number leaves its list.</summary>
    public bool Expired => Elapsed > 1000;

    /// <summary>FUN_141910f60 (@0x141910f60): still shown (state 0) and over 100 ms, it may jump.</summary>
    public bool MayJump => State == 0 && Elapsed > 100;

    /// <summary>The dtor (@0x141910d10): a number not jumped (state 0, or hidden, state 3) stops its ani and dispatches
    /// RemoveAni, which removes the clip; a jumped one plays on to its ani's own RemoveAni frame.</summary>
    public bool RemovesClipOnDrop => State is 0 or 3;
}

/// <summary>
/// One of UIDamageText's two lists (m_normals with m_counter, m_ownerFollowers with m_ownerFollowerCounter) with its
/// last-appended position (m_posNormals / m_posOwnerFollowers) and per-owner counters (CounterInfo).
/// </summary>
public sealed class DamageList<T> where T : DamageInfo
{
    /// <summary>CounterInfo: the owner's numbers in the list and the index the newest took.</summary>
    public sealed class Counter { public int Count = 1, Index; }

    readonly LinkedList<T> list = new();
    readonly Dictionary<object, Counter> counters = new();
    // the list's nodes and the counters reused, so a number in and out of the list allocates nothing once warm
    readonly Stack<LinkedListNode<T>> freeNodes = new();
    readonly Stack<Counter> freeCounters = new();
    LinkedListNode<T> last;
    readonly int maxIndex;

    public DamageList(int maxIndex = 1000) { this.maxIndex = maxIndex; }

    public int Count => list.Count;
    public LinkedListNode<T> First => list.First;
    public bool HasCounters => counters.Count > 0;
    public bool TryCounter(object owner, out Counter c) => counters.TryGetValue(owner, out c);

    /// <summary>
    /// InsertDamageInfo (@0x141914da0). Over is checked before the insert: more than <paramref name="max"/> in the list
    /// (never while clearing).
    /// <list type="bullet">
    /// <item>Append with a last position: at the head; when over, the entry at the last position leaves (the position
    /// moves to the one before it) and its owner's count drops.</item>
    /// <item>Append without one: at the head, which becomes the last position.</item>
    /// <item>Not append with a last position: after it; when over, the tail leaves but the count of the new entry's owner
    /// drops (the client's own slip, kept).</item>
    /// <item>Not append without one: at the head.</item>
    /// </list>
    /// Then GetOrCreateCounter (@0x141915240): the owner's count and index go up (the index back to 0 at 1000), or a new
    /// counter at count 1, index 0; the entry takes that index. Returns the entry that left, or null.
    /// </summary>
    public T Insert(bool append, T info, int max, bool clearing = false)
    {
        bool over = !clearing && list.Count > max;
        T removed = null;
        if (append)
        {
            if (last != null)
            {
                list.AddFirst(Node(info));
                if (over)
                {
                    var node = last;
                    last = node.Previous;
                    removed = node.Value;
                    list.Remove(node);
                    Free(node);
                    Decrement(removed.Owner, 1);
                }
            }
            else list.AddFirst(last = Node(info));
        }
        else
        {
            if (last != null)
            {
                list.AddAfter(last, Node(info));
                if (over)
                {
                    var tail = list.Last;
                    if (tail == last) last = tail.Previous;
                    removed = tail.Value;
                    list.RemoveLast();
                    Free(tail);
                    Decrement(info.Owner, 1);
                }
            }
            else list.AddFirst(Node(info));
        }
        if (counters.TryGetValue(info.Owner, out var c))
        {
            c.Count++;
            if (++c.Index >= maxIndex) c.Index = 0;
        }
        else
        {
            if (freeCounters.TryPop(out c)) { c.Count = 1; c.Index = 0; }
            else c = new Counter();
            counters[info.Owner] = c;
        }
        info.Index = c.Index;
        return removed;
    }

    /// <summary>RemoveNormal / RemoveOwnerFollower via FUN_141917ab0: the node leaves; the last position moves back when it was it.</summary>
    public void Remove(LinkedListNode<T> node)
    {
        if (list.Count == 0) return;
        if (node == last) last = node.Previous;
        list.Remove(node);
        Free(node);
    }

    LinkedListNode<T> Node(T info)
    {
        if (!freeNodes.TryPop(out var n)) return new LinkedListNode<T>(info);
        n.Value = info;
        return n;
    }

    void Free(LinkedListNode<T> node)
    {
        node.Value = null;
        freeNodes.Push(node);
    }

    /// <summary>DecrementCounter (@0x141915150): -1 drops the owner's counter; else its count goes down, and the counter at 0.</summary>
    public void Decrement(object owner, int n)
    {
        if (counters.Count == 0 || owner == null) return;
        if (n == -1) { if (counters.Remove(owner, out var gone)) freeCounters.Push(gone); return; }
        if (counters.TryGetValue(owner, out var c) && (c.Count -= n) <= 0) { counters.Remove(owner); freeCounters.Push(c); }
    }

    public void ClearCounters()
    {
        foreach (var c in counters.Values) freeCounters.Push(c);
        counters.Clear();
    }

    public void Clear()
    {
        while (list.First is { } n) { list.RemoveFirst(); Free(n); }
        ClearCounters();
        last = null;
    }
}

/// <summary>
/// One UpdateNormals pass (@0x141915990) over the normals list in order, as state carried from entry to entry.
/// nFirst, nNext and nIndex are kept from the last non-critical entry tested, so a critical number uses its neighbour's.
/// </summary>
public struct DamageNormalsPass
{
    int shown, first, next, index, stacked;
    readonly int jumpCount, maxShown, maxIndex;
    readonly int mine, other;

    public DamageNormalsPass(DamageTextLayout l, int scaleStep, int otherForm)
    {
        jumpCount = l.JumpCount; maxShown = l.MaxShown; maxIndex = l.MaxIndex;
        (mine, other) = l.Lines(scaleStep, otherForm);
        shown = first = next = index = stacked = 0;
    }

    /// <summary>
    /// The hide test of an entry that may still jump: a critical number is never hidden; another is hidden when its owner
    /// has no counter, when more than MaxShown were shown this pass, or when its index is outside the owner's newest
    /// JumpCount (nFirst = newest - JumpCount + 1 to nNext = newest + 1, taken around the wrap at 1000 when nFirst is negative).
    /// </summary>
    public bool Hidden<T>(T e, DamageList<T> list) where T : DamageInfo
    {
        if (e.Grade == 1) return false;
        if (!list.TryCounter(e.Owner, out var c) || shown > maxShown) return true;
        first = c.Index - jumpCount + 1;
        next = c.Index + 1;
        index = e.Index;
        return first >= 0 ? index < first || next <= index : next < index && index <= maxIndex + first;
    }

    /// <summary>
    /// A placed entry (its point projects): a non-critical one counts as shown. nBehind = nNext - 1 - nIndex - nStacked.
    /// A type 0 critical is lifted two lines (mine or another's) and stacks; another type 0 is lifted max(0, nBehind - 1)
    /// lines and ends the stack. Returns the lift in stage units and whether the entry jumps now ("_next": a non-critical
    /// one with nBehind over 0, state 0 and over 100 ms).
    /// </summary>
    public (float Lift, bool Jump) Place(DamageInfo e)
    {
        bool crit = e.Grade == 1;
        if (!crit) shown++;
        int behind = next - 1 - index - stacked;
        float lift = 0;
        if (crit && e.Type == 0)
        {
            lift = (e.Mine ? mine : other) * 2;
            stacked++;
        }
        else if (e.Type == 0)
        {
            int lines = Math.Max(0, behind - 1);
            if (lines > 0) lift = lines * (e.Mine ? mine : other);
            stacked = 0;
        }
        return (lift, behind > 0 && !crit && e.MayJump);
    }
}
