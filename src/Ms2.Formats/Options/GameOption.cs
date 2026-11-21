using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ms2.Formats.Options;

/// <summary>
/// The client's game options (CGameOption, gameoption.cpp), the blocks it keeps in GameOption.xml: camera, interface, status,
/// battle (with its damage section), community, graphic, sound, connect, optimizer, chroma and language. Each block starts at
/// the values its reset function stores (the block constructors call them), reads its attributes from the document and
/// clamps them as the client's LoadGameOption* functions do, and writes them back as SaveGameOption* does. Every option is an
/// attribute of a section element that is a direct child of the document's root element (gameoptionstruct.cpp:3719-4032).
/// Sections, attribute names and text forms are the client's, so a file written here has the shape of the client's file.
/// </summary>
public sealed class GameOption
{
    /// <summary>The option file version the client writes and requires (SetOptionVersion gameoption.cpp:2101, 0x15;
    /// IsOptionFileMissingOrOutdated gameoption.cpp:2039-2065). A user file without it, or with another value, is replaced by
    /// the default file.</summary>
    public const int Version = 0x15;

    public Camera CameraOption = new();
    public Interface InterfaceOption;
    public Status StatusOption;
    public Battle BattleOption = new();
    public Community CommunityOption = new();
    public Graphic GraphicOption = new();
    public Sound SoundOption = new();
    public Connect ConnectOption = new();
    public Optimizer OptimizerOption = new();
    public Chroma ChromaOption = new();
    public Language LanguageOption;

    /// <param name="regionIndex">the client's region index (DAT_143ab0248, localepath.cpp:599; 0 KR, 1 JP, 2 NA, 3 CN,
    /// GetRegionCode localepath.cpp:535). The language and status blocks reset by it.</param>
    public GameOption(int regionIndex)
    {
        InterfaceOption = new Interface();
        StatusOption = new Status(regionIndex == 0);
        LanguageOption = new Language(regionIndex);
    }

    /// <summary>True when the document is a current option file: it has a root, an &lt;option&gt; child of it and
    /// version 0x15 there (IsOptionFileMissingOrOutdated gameoption.cpp:2039-2065; a missing version reads as 0).</summary>
    public static bool IsCurrent(XDocument doc)
    {
        var option = Section(doc, "option");
        if (option == null) return false;
        int version = 0;
        Xml.ReadInt(option, "version", ref version);
        return version == Version;
    }

    /// <summary>CGameOption::LoadGameOption (gameoption.cpp:1720) and LoadLanguageOption (:1822) over one document: every
    /// block, graphic in its file form (nDefault 0). <paramref name="featureEnabled"/> is CFeatureManager::IsFeatureEnabled by
    /// feature name (the client's ids: 0x66 GemDamageFont featuremanager.cpp:333, 0xbe PartyDamageMeter :509).</summary>
    public void Load(XDocument doc, Func<string, bool> featureEnabled)
    {
        CameraOption.Load(doc);
        InterfaceOption.Load(doc, featureEnabled("PartyDamageMeter"));
        StatusOption.Load(doc);
        BattleOption.Load(doc, featureEnabled("GemDamageFont"));
        CommunityOption.Load(doc);
        GraphicOption.Load(doc);
        SoundOption.Load(doc);
        ConnectOption.Load(doc);
        OptimizerOption.Load(doc);
        ChromaOption.Load(doc);
        LanguageOption.Load(doc);
    }

    /// <summary>CGameOption::SaveGameSettings (gameoption.cpp:1852-1867) for the game document, then SetOptionVersion
    /// (gameoption.cpp:2101). The order is the client's, so new sections are appended in the same order.</summary>
    public void Save(XDocument doc)
    {
        CameraOption.Save(doc);
        InterfaceOption.Save(doc);
        StatusOption.Save(doc);
        BattleOption.Save(doc);
        CommunityOption.Save(doc);
        GraphicOption.Save(doc);
        SoundOption.Save(doc);
        ConnectOption.Save(doc);
        OptimizerOption.Save(doc);
        LanguageOption.Save(doc);
        ChromaOption.Save(doc);
        SetOptionVersion(doc);
    }

    /// <summary>SetOptionVersion (gameoption.cpp:2101): &lt;option version="21"/&gt; on any option file.</summary>
    public static void SetOptionVersion(XDocument doc) => Xml.WriteInt(doc, "option", "version", Version);

    /// <summary>The temporary file's mark after the option file's name, before the writing process's id.</summary>
    public const string TmpMark = ".tmp";

    /// <summary>
    /// An option file written as the client's SaveFile writes it (m_docGame/m_docAccount/m_docCharacter.SaveFile,
    /// gameoption.cpp:1874-1875, :1914): the document, UTF-8 without a byte order mark, indented. Deviation: the client
    /// writes the file in place, so a crash or a full disk mid-write leaves it cut short and the next start resets every
    /// option (a file that does not parse reads as missing). Here it goes to a temporary file that is renamed over the old
    /// one, as the cache writers do, so the file is always the old one or the new one, whole. A failed write leaves no
    /// temporary file and throws.
    /// STILL-OPEN: a symlinked or read-only option file. The rename replaces a symlink with a plain file instead of writing
    /// through it, and on a read-only file the rename's outcome depends on the platform (it replaces the file where the
    /// directory is writable, it fails on Windows) where the client's in-place write fails; neither case is handled.
    /// </summary>
    public static void WriteFile(string path, XDocument doc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var tmp = path + TmpMark + Environment.ProcessId;
        try
        {
            var settings = new System.Xml.XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new System.Text.UTF8Encoding(false) };
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var w = System.Xml.XmlWriter.Create(fs, settings)) doc.Save(w);
                fs.Flush(true);
            }
            File.Move(tmp, path, true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    static XElement Section(XDocument doc, string name) => doc.Root?.Element(name);

    /// <summary>The memcmp the option pages use for "changed" (vfunc_14): every public field of the two blocks is equal.</summary>
    public static bool SameFields<T>(T a, T b) where T : class =>
        typeof(T).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .All(f => Equals(f.GetValue(a), f.GetValue(b)));

    /// <summary>Camera (Unk_CameraState): reset FUN_14125b430 gameoptionstruct.cpp:1119-1126, load :1130-1151, save :1155-1162.</summary>
    public sealed class Camera
    {
        public bool BgTransparency = true, VibrateEffect = true;
        public int CameraLooseStep = 100, CameraFovStep = 100, CameraFovStepMouseMode = 0x82;

        public void Load(XDocument d)
        {
            Xml.ReadBool(d, "camera", "bgTransparency", ref BgTransparency);
            Xml.ReadBool(d, "camera", "vibrateEffect", ref VibrateEffect);
            Xml.ReadInt(d, "camera", "cameraLooseStep", ref CameraLooseStep);
            Xml.ReadInt(d, "camera", "cameraFovStep", ref CameraFovStep);
            Xml.ReadInt(d, "camera", "cameraFovStepMouseMode", ref CameraFovStepMouseMode);
            CameraLooseStep = Math.Clamp(CameraLooseStep, 20, 120);
            CameraFovStep = Math.Clamp(CameraFovStep, 70, 130);
            CameraFovStepMouseMode = Math.Clamp(CameraFovStepMouseMode, 70, 130);
        }

        /// <summary>The mouse-mode fov step is read back from the document, not written (gameoptionstruct.cpp:1161).</summary>
        public Camera Clone() => (Camera)MemberwiseClone();

        public void Save(XDocument d)
        {
            Xml.WriteBool(d, "camera", "bgTransparency", BgTransparency);
            Xml.WriteBool(d, "camera", "vibrateEffect", VibrateEffect);
            Xml.WriteInt(d, "camera", "cameraLooseStep", CameraLooseStep);
            Xml.WriteInt(d, "camera", "cameraFovStep", CameraFovStep);
            Xml.ReadInt(d, "camera", "cameraFovStepMouseMode", ref CameraFovStepMouseMode);
        }
    }

    /// <summary>Interface (Unk_InterfaceState): reset FUN_14125b7a0 gameoptionstruct.cpp:1201-1213, load :1217-1242, save :1246-1256.</summary>
    public sealed class Interface
    {
        public bool EnableCloseAllDlg, EnableAutoDesc = true, EnableShowTipText = true, EnableIndividualDamageMeter, EnableChaosDamageMeter;
        public float UIScale = 1f;
        public int DamageEffectAlpha = 100, DamageTextScaleStep = 100;
        /// <summary>m_b14 / m_b15: the damage meter switches may be used (reset on, not in the file; the page enables its
        /// check boxes by them and IsIndividualDamageMeterOn / IsChaosDamageMeterOn test them, :1268-1280).</summary>
        public bool IndividualDamageMeterAllowed = true, ChaosDamageMeterAllowed = true;

        /// <param name="partyDamageMeter">feature 0xbe: without it the chaos damage meter is forced off (:1238-1239)</param>
        public void Load(XDocument d, bool partyDamageMeter)
        {
            Xml.ReadBool(d, "interface", "EnableCloseAllDlg", ref EnableCloseAllDlg);
            Xml.ReadBool(d, "interface", "EnableAutoDesc", ref EnableAutoDesc);
            Xml.ReadBool(d, "interface", "EnableShowTipText", ref EnableShowTipText);
            Xml.ReadBool(d, "interface", "EnableIndividualDamageMeter", ref EnableIndividualDamageMeter);
            Xml.ReadBool(d, "interface", "EnableChaosDamageMeter", ref EnableChaosDamageMeter);
            Xml.ReadFloat(d, "interface", "UIScale", ref UIScale);
            Xml.ReadInt(d, "interface", "DamageTextScaleStep", ref DamageTextScaleStep);
            Xml.ReadInt(d, "interface", "damageEffectAlpha", ref DamageEffectAlpha);
            if (UIScale < 0.8f) UIScale = 0.8f;
            if (UIScale > 1.2f) UIScale = 1.2f;
            DamageTextScaleStep = Math.Clamp(DamageTextScaleStep, 50, 150);
            DamageEffectAlpha = Math.Clamp(DamageEffectAlpha, 0, 100);
            if (!partyDamageMeter) EnableChaosDamageMeter = false;
        }

        public Interface Clone() => (Interface)MemberwiseClone();

        public void Save(XDocument d)
        {
            Xml.WriteBool(d, "interface", "EnableCloseAllDlg", EnableCloseAllDlg);
            Xml.WriteBool(d, "interface", "EnableAutoDesc", EnableAutoDesc);
            Xml.WriteBool(d, "interface", "EnableShowTipText", EnableShowTipText);
            Xml.WriteBool(d, "interface", "EnableIndividualDamageMeter", EnableIndividualDamageMeter);
            Xml.WriteBool(d, "interface", "EnableChaosDamageMeter", EnableChaosDamageMeter);
            Xml.WriteFloat(d, "interface", "UIScale", UIScale);
            Xml.WriteInt(d, "interface", "DamageTextScaleStep", DamageTextScaleStep);
            Xml.WriteInt(d, "interface", "damageEffectAlpha", DamageEffectAlpha);
        }
    }

    /// <summary>Status (Unk_StatusState): reset FUN_14125bdd0 gameoptionstruct.cpp:1340-1367, load :1371-1401, save :1405-1430.</summary>
    public sealed class Status
    {
        public bool DisplayMyName = true, DisplayPlayerSymbol = true, DisplayFriendlyName = true, DisplayEnemyName = true, DisplayNpcName = true;
        public bool DisplayMonsterName, DisplayPcIllust, DisplayMonsterGaugeValue = true, DisplayExpValue = true, DisplayOnlyMyEffectToMonster;
        public bool DisplayMyHpGauge, DisplayMyShieldGauge = true, DisplayPartyHpGauge, DisplayPartyShieldGauge = true, DisplayMonsterHpGauge;
        public int DisplayBossMonsterNotify;
        public bool DisplayNormalChatBalloon = true, DisplayPartyChatBalloon = true, DisplayGuildChatBalloon = true;
        public bool DisplayShowOnlyMyPet, DisplayHideMyPet, DisplayHideSmartPush, DisplayHideHighlightMenu;

        /// <param name="regionKr">IsRegionKR: the PC illustration starts on (:1364-1366)</param>
        public Status(bool regionKr) { RegionKr = regionKr; DisplayPcIllust = regionKr; }

        /// <summary>The region the reset used (a property, so the vfunc_14 field compare does not see it).</summary>
        public bool RegionKr { get; }

        public void Load(XDocument d)
        {
            Xml.ReadBool(d, "status", "DisplayMyName", ref DisplayMyName);
            Xml.ReadBool(d, "status", "DisplayPlayerSymbol", ref DisplayPlayerSymbol);
            Xml.ReadBool(d, "status", "DisplayFriendlyName", ref DisplayFriendlyName);
            Xml.ReadBool(d, "status", "DisplayEnemyName", ref DisplayEnemyName);
            Xml.ReadBool(d, "status", "DisplayNpcName", ref DisplayNpcName);
            Xml.ReadBool(d, "status", "DisplayMonsterName", ref DisplayMonsterName);
            Xml.ReadBool(d, "status", "DisplayPcIllust", ref DisplayPcIllust);
            Xml.ReadBool(d, "status", "DisplayMonsterGaugeValue", ref DisplayMonsterGaugeValue);
            Xml.ReadBool(d, "status", "DisplayExpValue", ref DisplayExpValue);
            Xml.ReadBool(d, "status", "DisplayOnlyMyEffectToMonster", ref DisplayOnlyMyEffectToMonster);
            Xml.ReadBool(d, "status", "DisplayMyHpGauge", ref DisplayMyHpGauge);
            Xml.ReadBool(d, "status", "DisplayMyShieldGauge", ref DisplayMyShieldGauge);
            Xml.ReadBool(d, "status", "DisplayPartyHpGauge", ref DisplayPartyHpGauge);
            Xml.ReadBool(d, "status", "DisplayPartyShieldGauge", ref DisplayPartyShieldGauge);
            Xml.ReadBool(d, "status", "DisplayMonsterHpGauge", ref DisplayMonsterHpGauge);
            Xml.ReadInt(d, "status", "DisplayBossMonsterNotify", ref DisplayBossMonsterNotify);
            Xml.ReadBool(d, "status", "DisplayNormalChatBalloon", ref DisplayNormalChatBalloon);
            Xml.ReadBool(d, "status", "DisplayPartyChatBalloon", ref DisplayPartyChatBalloon);
            Xml.ReadBool(d, "status", "DisplayGuildChatBalloon", ref DisplayGuildChatBalloon);
            Xml.ReadBool(d, "status", "DisplayShowOnlyMyPet", ref DisplayShowOnlyMyPet);
            Xml.ReadBool(d, "status", "DisplayHideMyPet", ref DisplayHideMyPet);
            Xml.ReadBool(d, "status", "DisplayHideSmartPush", ref DisplayHideSmartPush);
            Xml.ReadBool(d, "status", "DisplayHideHighlightMenu", ref DisplayHideHighlightMenu);
            DisplayBossMonsterNotify = Math.Clamp(DisplayBossMonsterNotify, 0, 2);
        }

        public Status Clone() => (Status)MemberwiseClone();

        public void Save(XDocument d)
        {
            Xml.WriteBool(d, "status", "DisplayMyName", DisplayMyName);
            Xml.WriteBool(d, "status", "DisplayPlayerSymbol", DisplayPlayerSymbol);
            Xml.WriteBool(d, "status", "DisplayFriendlyName", DisplayFriendlyName);
            Xml.WriteBool(d, "status", "DisplayEnemyName", DisplayEnemyName);
            Xml.WriteBool(d, "status", "DisplayNpcName", DisplayNpcName);
            Xml.WriteBool(d, "status", "DisplayMonsterName", DisplayMonsterName);
            Xml.WriteBool(d, "status", "DisplayPcIllust", DisplayPcIllust);
            Xml.WriteBool(d, "status", "DisplayMonsterGaugeValue", DisplayMonsterGaugeValue);
            Xml.WriteBool(d, "status", "DisplayExpValue", DisplayExpValue);
            Xml.WriteBool(d, "status", "DisplayOnlyMyEffectToMonster", DisplayOnlyMyEffectToMonster);
            Xml.WriteBool(d, "status", "DisplayMyHpGauge", DisplayMyHpGauge);
            Xml.WriteBool(d, "status", "DisplayMyShieldGauge", DisplayMyShieldGauge);
            Xml.WriteBool(d, "status", "DisplayPartyHpGauge", DisplayPartyHpGauge);
            Xml.WriteBool(d, "status", "DisplayPartyShieldGauge", DisplayPartyShieldGauge);
            Xml.WriteBool(d, "status", "DisplayMonsterHpGauge", DisplayMonsterHpGauge);
            Xml.WriteInt(d, "status", "DisplayBossMonsterNotify", DisplayBossMonsterNotify);
            Xml.WriteBool(d, "status", "DisplayNormalChatBalloon", DisplayNormalChatBalloon);
            Xml.WriteBool(d, "status", "DisplayPartyChatBalloon", DisplayPartyChatBalloon);
            Xml.WriteBool(d, "status", "DisplayGuildChatBalloon", DisplayGuildChatBalloon);
            Xml.WriteBool(d, "status", "DisplayShowOnlyMyPet", DisplayShowOnlyMyPet);
            Xml.WriteBool(d, "status", "DisplayHideMyPet", DisplayHideMyPet);
            Xml.WriteBool(d, "status", "DisplayHideSmartPush", DisplayHideSmartPush);
            Xml.WriteBool(d, "status", "DisplayHideHighlightMenu", DisplayHideHighlightMenu);
        }
    }

    /// <summary>Battle (Unk_BattleState): reset FUN_14125cab0 gameoptionstruct.cpp:1572-1583, load :1588-1637, save :1641-1660.
    /// The damage forms are 1 mine, 3 party, 7 all (give/take) and 0 default, 1 size, 2 alpha, 3 all (other)
    /// (Parse_140219900 / Parse_140219960 / Name_140219790 / Name_140219830, commonenum.cpp:559-600).</summary>
    public sealed class Battle
    {
        public bool DisplayMyGiveRecovery = true, DisplayMyTakeRecovery = true, DisplayPartyRecovery = true;
        public bool DisplayGetMeso = true, DisplayGetExp = true, DisplayEpGauge = true;
        public int GiveDamage = 7, TakeDamage = 3, OtherDamageForm = 3;

        static int ParseGiveTake(string s) => Eq(s, "mine") ? 1 : Eq(s, "party") ? 3 : 7;
        static int ParseOther(string s) => Eq(s, "alpha") ? 2 : Eq(s, "size") ? 1 : Eq(s, "all") ? 3 : 0;
        static string NameGiveTake(int v) => v == 1 ? "mine" : v == 3 ? "party" : "all";
        static string NameOther(int v) => v == 1 ? "size" : v == 2 ? "alpha" : v == 3 ? "all" : "default";
        static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <param name="gemDamageFont">feature 0x66: a file without the damage section shows all given damage (:1609-1612)</param>
        public void Load(XDocument d, bool gemDamageFont)
        {
            Xml.ReadBool(d, "battle", "DisplayGetMeso", ref DisplayGetMeso);
            Xml.ReadBool(d, "battle", "DisplayGetExp", ref DisplayGetExp);
            Xml.ReadBool(d, "battle", "DisplayEpGauge", ref DisplayEpGauge);
            Xml.ReadBool(d, "battle", "DisplayMyGiveRecovery", ref DisplayMyGiveRecovery);
            Xml.ReadBool(d, "battle", "DisplayMyTakeRecovery", ref DisplayMyTakeRecovery);
            Xml.ReadBool(d, "battle", "DisplayPartyRecovery", ref DisplayPartyRecovery);
            string give = null;
            if (Xml.ReadString(d, "damage", "giveDamage", ref give))
            {
                GiveDamage = ParseGiveTake(give);
                string take = "", other = "";
                Xml.ReadString(d, "damage", "takeDamage", ref take);
                TakeDamage = ParseGiveTake(take);
                Xml.ReadString(d, "damage", "otherDamageform", ref other);
                OtherDamageForm = ParseOther(other);
            }
            else
            {
                if (gemDamageFont) GiveDamage = 7;
                else
                {
                    bool mine = true, party = false;
                    Xml.ReadBool(d, "battle", "DisplayMyGiveDamage", ref mine);
                    Xml.ReadBool(d, "battle", "DisplayPartyGiveDamage", ref party);
                    GiveDamage = mine && !party ? 1 : 3;
                }
                bool mineTake = true, partyTake = false;
                Xml.ReadBool(d, "battle", "DisplayMyTakeDamage", ref mineTake);
                Xml.ReadBool(d, "battle", "DisplayPartyTakeDamage", ref partyTake);
                TakeDamage = mineTake && !partyTake ? 1 : 3;
                OtherDamageForm = 3;
            }
        }

        public Battle Clone() => (Battle)MemberwiseClone();

        public void Save(XDocument d)
        {
            Xml.WriteBool(d, "battle", "DisplayMyGiveRecovery", DisplayMyGiveRecovery);
            Xml.WriteBool(d, "battle", "DisplayMyTakeRecovery", DisplayMyTakeRecovery);
            Xml.WriteBool(d, "battle", "DisplayPartyRecovery", DisplayPartyRecovery);
            Xml.WriteBool(d, "battle", "DisplayGetMeso", DisplayGetMeso);
            Xml.WriteBool(d, "battle", "DisplayGetExp", DisplayGetExp);
            Xml.WriteBool(d, "battle", "DisplayEpGauge", DisplayEpGauge);
            foreach (var old in new[] { "DisplayDropIcon", "DisplayMyGiveDamage", "DisplayPartyGiveDamage", "DisplayMyTakeDamage", "DisplayPartyTakeDamage", "DiffColor", "DiffSize", "DiffAlpha" })
                Xml.Remove(d, "battle", old);
            Xml.WriteString(d, "damage", "giveDamage", NameGiveTake(GiveDamage));
            Xml.WriteString(d, "damage", "takeDamage", NameGiveTake(TakeDamage));
            Xml.WriteString(d, "damage", "otherDamageform", NameOther(OtherDamageForm));
        }
    }

    /// <summary>Community, the game-file part (Unk_CommunityStateA): reset FUN_14125d160 gameoptionstruct.cpp:1679-1684,
    /// load :1688-1694, save :1698-1703.</summary>
    public sealed class Community
    {
        public bool DisplayMyChatBalloon = true, DisplayOtherChatBalloon = true, DisableSingleRiding;

        public void Load(XDocument d)
        {
            Xml.ReadBool(d, "community", "displayMyChatBalloon", ref DisplayMyChatBalloon);
            Xml.ReadBool(d, "community", "displayOtherChatBalloon", ref DisplayOtherChatBalloon);
            Xml.ReadBool(d, "community", "disableSingleRiding", ref DisableSingleRiding);
        }

        public Community Clone() => (Community)MemberwiseClone();

        public void Save(XDocument d)
        {
            Xml.WriteBool(d, "community", "displayMyChatBalloon", DisplayMyChatBalloon);
            Xml.WriteBool(d, "community", "displayOtherChatBalloon", DisplayOtherChatBalloon);
            Xml.WriteBool(d, "community", "disableSingleRiding", DisableSingleRiding);
        }
    }

    /// <summary>The account file's community block (Unk_CommunityStateB, AccountOption.xml): reset FUN_14125d360
    /// gameoptionstruct.cpp:1735-1749, load :1752-1767, save :1770-1784. "Comfirm" and "Couplemotion" are the client's
    /// spellings.</summary>
    public sealed class AccountCommunity
    {
        public bool DisplayChatComfirmSuper = true, DisplayChatComfirmWorld = true, DisplayChatComfirmChannel = true;
        public bool DisablePartyRequest, DisableBuddyRequest, DisableGuildRequest, DisableGroupRequest, DisableWhisperNotify;
        public bool DisableTradeRequest, DisableHomeRequest, DisableCouplemotionRequest;

        static readonly string[] Keys =
        {
            "displayChatComfirmSuper", "displayChatComfirmWorld", "displayChatComfirmChannel", "disablePartyRequest",
            "disableBuddyRequest", "disableGuildRequest", "disableGroupRequest", "disableWhisperNotify", "disableTradeRequest",
            "disableHomeRequest", "disableCouplemotionRequest",
        };

        bool[] Values() => new[]
        {
            DisplayChatComfirmSuper, DisplayChatComfirmWorld, DisplayChatComfirmChannel, DisablePartyRequest,
            DisableBuddyRequest, DisableGuildRequest, DisableGroupRequest, DisableWhisperNotify, DisableTradeRequest,
            DisableHomeRequest, DisableCouplemotionRequest,
        };

        void Set(bool[] v)
        {
            (DisplayChatComfirmSuper, DisplayChatComfirmWorld, DisplayChatComfirmChannel, DisablePartyRequest) = (v[0], v[1], v[2], v[3]);
            (DisableBuddyRequest, DisableGuildRequest, DisableGroupRequest, DisableWhisperNotify) = (v[4], v[5], v[6], v[7]);
            (DisableTradeRequest, DisableHomeRequest, DisableCouplemotionRequest) = (v[8], v[9], v[10]);
        }

        public void Load(XDocument d)
        {
            var v = Values();
            for (int i = 0; i < Keys.Length; i++) Xml.ReadBool(d, "community", Keys[i], ref v[i]);
            Set(v);
        }

        public void Save(XDocument d)
        {
            var v = Values();
            for (int i = 0; i < Keys.Length; i++) Xml.WriteBool(d, "community", Keys[i], v[i]);
        }

        public AccountCommunity Clone() => (AccountCommunity)MemberwiseClone();
    }

    /// <summary>One switch of the character file (Unk_InterfaceFlag "interface"/"EnableMouseControl" :1291-1310,
    /// Unk_StatusFlag "status"/"displayLearningQuestV2" :1512-1531): reset off, read and written by its key.</summary>
    public sealed class CharacterFlag
    {
        readonly string section, key;
        public bool Value;
        public CharacterFlag(string section, string key) { this.section = section; this.key = key; }
        public static CharacterFlag MouseControl() => new("interface", "EnableMouseControl");
        public static CharacterFlag LearningQuest() => new("status", "displayLearningQuestV2");
        /// <summary>True when the attribute was there (the client's readers return 1 then).</summary>
        public bool Load(XDocument d) => Xml.ReadBool(d, section, key, ref Value);
        public void Save(XDocument d) => Xml.WriteBool(d, section, key, Value);
        public CharacterFlag Clone() => (CharacterFlag)MemberwiseClone();
    }

    /// <summary>Graphic (Unk_GraphicState): reset FUN_14125d8c0 gameoptionstruct.cpp:1908-1931, load LoadGameOptionGraphic
    /// :1936-1990 in its file form (nDefault 0, as LoadGameOption calls it, gameoption.cpp:1743), save :1994-2010.
    /// ScreenMode ("fullScreen"): 0 the device's full screen, 1 window, 2 full window (the dialog offers 1 and 2,
    /// gameoption.cpp:1066-1077; FUN_14125e910 gameoptionstruct.cpp:2223-2262). ResolutionIndex is the row of the adapter's
    /// mode list the size was picked from.</summary>
    public sealed class Graphic
    {
        public int Quality = 3, ResolutionIndex, ScreenMode = 1, Brightness = 1, EffectStep = 3;
        public bool ShowFrameRate, AdvancedEffect;
        public int Width = 0x400, Height = 0x300, ShadowLevel = 2, MultisamplingIndex;
        public bool DisplayHdr, DisplayShimmer, Vsync = true, ShowEnchantEffect = true;
        /// <summary>The outer-glow option (m_b1e): true on reset (gameoptionstruct.cpp:1928), never read from or written to the
        /// file; only the dev visual-effect dialog changes it. GraphicGates.IsOuterGlowEnabled reads it.</summary>
        public bool OuterGlow = true;

        public void Load(XDocument d)
        {
            Xml.ReadBool(d, "graphic", "displayHdr", ref DisplayHdr);
            Xml.ReadBool(d, "graphic", "displayShimmer", ref DisplayShimmer);
            Xml.ReadInt(d, "graphic", "multisamplingIdx", ref MultisamplingIndex);
            Xml.ReadInt(d, "graphic", "resolutionWidth", ref Width);
            Xml.ReadInt(d, "graphic", "resolutionHeight", ref Height);
            Xml.ReadInt(d, "graphic", "resolution", ref ResolutionIndex);
            Xml.ReadInt(d, "graphic", "quality", ref Quality);
            Xml.ReadInt(d, "graphic", "effectStep", ref EffectStep);
            Xml.ReadInt(d, "graphic", "shadowLevel", ref ShadowLevel);
            Quality = Math.Clamp(Quality, 0, 3);
            EffectStep = Math.Clamp(EffectStep, 1, 3);
            ShadowLevel = Math.Clamp(ShadowLevel, 0, 4);
            MultisamplingIndex = Math.Clamp(MultisamplingIndex, 0, 2);
            Xml.ReadInt(d, "graphic", "fullScreen", ref ScreenMode);
            Xml.ReadInt(d, "graphic", "brightness", ref Brightness);
            Xml.ReadBool(d, "graphic", "showFramerate", ref ShowFrameRate);
            Xml.ReadBool(d, "graphic", "advancedEffect", ref AdvancedEffect);
            Xml.ReadBool(d, "graphic", "vsync", ref Vsync);
            Xml.ReadBool(d, "graphic", "showEnchantEffect", ref ShowEnchantEffect);
        }

        /// <summary>LoadGameOptionGraphic in its default form (nDefault 1, gameoptionstruct.cpp:1938-1951 and :1980-1986):
        /// quality from the benchmark, hdr and shimmer by the graphic option table, multisampling 0, effect step and shadow
        /// from the quality, the row of the screen size (FUN_14125eb00), then the common keys of the document.</summary>
        public void LoadDefault(XDocument d, int benchmark, Func<int, bool> hdrSupported, Func<int, bool> shimmerSupported,
            ScreenModes modes, int screenWidth, int screenHeight)
        {
            Quality = benchmark;
            DisplayHdr = hdrSupported(Quality);
            DisplayShimmer = shimmerSupported(Quality);
            MultisamplingIndex = 0;
            EffectStep = Math.Clamp(Quality, 1, 3);
            ShadowLevel = Quality;
            modes?.PickRow(this, screenWidth, screenHeight);
            Xml.ReadInt(d, "graphic", "fullScreen", ref ScreenMode);
            Xml.ReadInt(d, "graphic", "brightness", ref Brightness);
            Xml.ReadBool(d, "graphic", "showFramerate", ref ShowFrameRate);
            Xml.ReadBool(d, "graphic", "advancedEffect", ref AdvancedEffect);
            Xml.ReadBool(d, "graphic", "vsync", ref Vsync);
            Xml.ReadBool(d, "graphic", "showEnchantEffect", ref ShowEnchantEffect);
        }

        public Graphic Clone() => (Graphic)MemberwiseClone();

        /// <summary>advancedEffect is read, never written (:1983).</summary>
        public void Save(XDocument d)
        {
            Xml.WriteInt(d, "graphic", "quality", Quality);
            Xml.WriteInt(d, "graphic", "resolution", ResolutionIndex);
            Xml.WriteInt(d, "graphic", "fullScreen", ScreenMode);
            Xml.WriteInt(d, "graphic", "brightness", Brightness);
            Xml.WriteBool(d, "graphic", "showFramerate", ShowFrameRate);
            Xml.WriteInt(d, "graphic", "effectStep", EffectStep);
            Xml.WriteInt(d, "graphic", "resolutionWidth", Width);
            Xml.WriteInt(d, "graphic", "resolutionHeight", Height);
            Xml.WriteBool(d, "graphic", "displayHdr", DisplayHdr);
            Xml.WriteBool(d, "graphic", "displayShimmer", DisplayShimmer);
            Xml.WriteInt(d, "graphic", "multisamplingIdx", MultisamplingIndex);
            Xml.WriteInt(d, "graphic", "shadowLevel", ShadowLevel);
            Xml.WriteBool(d, "graphic", "vsync", Vsync);
            Xml.WriteBool(d, "graphic", "showEnchantEffect", ShowEnchantEffect);
        }
    }

    /// <summary>Sound (Unk_SoundState): reset FUN_14125f740 gameoptionstruct.cpp:2612-2628, load LoadGameOptionSound
    /// :2632-2694, save :2697-2713. Steps are 0..10.</summary>
    public sealed class Sound
    {
        public bool MuteAll, PlayAll;
        public int MainSoundStep, EffectSoundStep, EnviromentSoundStep, UiSoundStep, NpcSoundStep, BossSoundStep, BgmSoundStep;
        public int SystemSoundStep, MovieSoundStep, CharSoundStep = 7, InstrumentSoundStep = 10;
        public bool OffBgmWhilePlayInstrument = true;

        public Sound Clone() => (Sound)MemberwiseClone();

        public void Load(XDocument d)
        {
            Xml.ReadBool(d, "sound", "bMuteAll", ref MuteAll);
            Xml.ReadBool(d, "sound", "bPlayAll", ref PlayAll);
            Xml.ReadInt(d, "sound", "mainSoundStep", ref MainSoundStep);
            Xml.ReadInt(d, "sound", "effectSoundStep", ref EffectSoundStep);
            Xml.ReadInt(d, "sound", "enviromentSound", ref EnviromentSoundStep);
            Xml.ReadInt(d, "sound", "uiSoundStep", ref UiSoundStep);
            Xml.ReadInt(d, "sound", "npcSoundStep", ref NpcSoundStep);
            Xml.ReadInt(d, "sound", "bossSoundStep", ref BossSoundStep);
            Xml.ReadInt(d, "sound", "bgmSoundStep", ref BgmSoundStep);
            Xml.ReadInt(d, "sound", "systemSoundStep", ref SystemSoundStep);
            Xml.ReadInt(d, "sound", "movieSoundStep", ref MovieSoundStep);
            Xml.ReadInt(d, "sound", "instrumentSoundStep", ref InstrumentSoundStep);
            Xml.ReadInt(d, "sound", "charSoundStep", ref CharSoundStep);
            Xml.ReadBool(d, "sound", "bOffBgmWhilePlayInstrument", ref OffBgmWhilePlayInstrument);
            MainSoundStep = Math.Clamp(MainSoundStep, 0, 10);
            BgmSoundStep = Math.Clamp(BgmSoundStep, 0, 10);
            EffectSoundStep = Math.Clamp(EffectSoundStep, 0, 10);
            UiSoundStep = Math.Clamp(UiSoundStep, 0, 10);
            EnviromentSoundStep = Math.Clamp(EnviromentSoundStep, 0, 10);
            MovieSoundStep = Math.Clamp(MovieSoundStep, 0, 10);
            NpcSoundStep = Math.Clamp(NpcSoundStep, 0, 10);
            BossSoundStep = Math.Clamp(BossSoundStep, 0, 10);
            SystemSoundStep = Math.Clamp(SystemSoundStep, 0, 10);
            InstrumentSoundStep = Math.Clamp(InstrumentSoundStep, 0, 10);
            CharSoundStep = Math.Clamp(CharSoundStep, 0, 10);
        }

        public void Save(XDocument d)
        {
            Xml.WriteBool(d, "sound", "bMuteAll", MuteAll);
            Xml.WriteBool(d, "sound", "bPlayAll", PlayAll);
            Xml.WriteInt(d, "sound", "mainSoundStep", MainSoundStep);
            Xml.WriteInt(d, "sound", "effectSoundStep", EffectSoundStep);
            Xml.WriteInt(d, "sound", "enviromentSound", EnviromentSoundStep);
            Xml.WriteInt(d, "sound", "uiSoundStep", UiSoundStep);
            Xml.WriteInt(d, "sound", "npcSoundStep", NpcSoundStep);
            Xml.WriteInt(d, "sound", "bossSoundStep", BossSoundStep);
            Xml.WriteInt(d, "sound", "bgmSoundStep", BgmSoundStep);
            Xml.WriteInt(d, "sound", "systemSoundStep", SystemSoundStep);
            Xml.WriteInt(d, "sound", "movieSoundStep", MovieSoundStep);
            Xml.WriteInt(d, "sound", "instrumentSoundStep", InstrumentSoundStep);
            Xml.WriteInt(d, "sound", "charSoundStep", CharSoundStep);
            Xml.WriteBool(d, "sound", "bOffBgmWhilePlayInstrument", OffBgmWhilePlayInstrument);
        }

        /// <summary>GetCategoryVolume (gameoptionstruct.cpp:2757-2783): a category's step times the main step over 100.
        /// Categories: 0 and 9 effect, 1 environment, 2 bgm, 3 ui, 4 and 10 npc, 5 and 8 system, 6 boss, 7 character.</summary>
        public float CategoryVolume(int category) => category switch
        {
            0 or 9 => EffectSoundStep * MainSoundStep / 100f,
            1 => EnviromentSoundStep * MainSoundStep / 100f,
            2 => BgmSoundStep * MainSoundStep / 100f,
            3 => UiSoundStep * MainSoundStep / 100f,
            4 or 10 => NpcSoundStep * MainSoundStep / 100f,
            7 => CharSoundStep * MainSoundStep / 100f,
            5 or 8 => SystemSoundStep * MainSoundStep / 100f,
            6 => BossSoundStep * MainSoundStep / 100f,
            _ => 0f,
        };

        /// <summary>Unk_SoundState::GetMovieVolume (gameoptionstruct.cpp:2786-2789).</summary>
        public float MovieVolume => MovieSoundStep * MainSoundStep / 100f;
    }

    /// <summary>Connect (Unk_GsConnect): reset FUN_141260550 gameoptionstruct.cpp:2862-2872, load :2876-2891, save :2895-2906;
    /// the character list is "id,id,..." (ParseIdList :4036-4046 with _strtoui64 per token, placeholder/xmlattr.h:87;
/// written by FUN_141260ce0 :2989-3001).</summary>
    public sealed class Connect
    {
        public string Region = "", WorldName = "", Channel = "", UserName = "", CharName = "";
        public ulong CharSN;
        public List<ulong> CharList = new();
        public bool SaveUserName, LoginByLocaleName;

        public void Load(XDocument d)
        {
            Xml.ReadString(d, "connect", "Region", ref Region);
            Xml.ReadString(d, "connect", "WorldName", ref WorldName);
            Xml.ReadString(d, "connect", "Channel", ref Channel);
            Xml.ReadString(d, "connect", "UserName", ref UserName);
            Xml.ReadUInt64(d, "connect", "CharSN", ref CharSN);
            Xml.ReadString(d, "connect", "CharName", ref CharName);
            CharList.Clear();
            string list = "";
            Xml.ReadString(d, "connect", "CharList", ref list);
            foreach (var token in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
                CharList.Add(Xml.ParseUInt64Prefix(token));
            Xml.ReadBool(d, "connect", "LoginByLocaleName", ref LoginByLocaleName);
        }

        /// <summary>The character name is cleared once written (:2903).</summary>
        public void Save(XDocument d)
        {
            Xml.WriteString(d, "connect", "Region", Region);
            Xml.WriteString(d, "connect", "WorldName", WorldName);
            Xml.WriteString(d, "connect", "Channel", Channel);
            Xml.WriteString(d, "connect", "UserName", UserName);
            Xml.WriteString(d, "connect", "CharName", CharName);
            Xml.WriteString(d, "connect", "CharSN", CharSN.ToString(CultureInfo.InvariantCulture));
            CharName = "";
            Xml.WriteString(d, "connect", "CharList", string.Join(",", CharList.Select(c => c.ToString(CultureInfo.InvariantCulture))));
            Xml.WriteBool(d, "connect", "LoginByLocaleName", LoginByLocaleName);
        }
    }

    /// <summary>Optimizer (Unk_GraphicState2): reset FUN_14125ec50 gameoptionstruct.cpp:2305-2316, load :2320-2335,
    /// save :2339-2349.</summary>
    public sealed class Optimizer
    {
        public int OptimalHide, CustomCount;
        public bool HideAlways = true, HideBossBattle, ShowGuild, ShowParty, ShowCustom, ShowPhysx;

        public Optimizer Clone() => (Optimizer)MemberwiseClone();

        public void Load(XDocument d)
        {
            Xml.ReadInt(d, "optimizer", "optimalHide", ref OptimalHide);
            Xml.ReadBool(d, "optimizer", "hideAlways", ref HideAlways);
            Xml.ReadBool(d, "optimizer", "hideBossBattle", ref HideBossBattle);
            Xml.ReadBool(d, "optimizer", "showGuild", ref ShowGuild);
            Xml.ReadBool(d, "optimizer", "showParty", ref ShowParty);
            Xml.ReadBool(d, "optimizer", "showCustom", ref ShowCustom);
            Xml.ReadInt(d, "optimizer", "customCount", ref CustomCount);
            Xml.ReadBool(d, "optimizer", "showphysx", ref ShowPhysx);
            OptimalHide = Math.Clamp(OptimalHide, 0, 2);
        }

        public void Save(XDocument d)
        {
            Xml.WriteInt(d, "optimizer", "optimalHide", OptimalHide);
            Xml.WriteBool(d, "optimizer", "hideAlways", HideAlways);
            Xml.WriteBool(d, "optimizer", "hideBossBattle", HideBossBattle);
            Xml.WriteBool(d, "optimizer", "showGuild", ShowGuild);
            Xml.WriteBool(d, "optimizer", "showParty", ShowParty);
            Xml.WriteBool(d, "optimizer", "showCustom", ShowCustom);
            Xml.WriteInt(d, "optimizer", "customCount", CustomCount);
            Xml.WriteBool(d, "optimizer", "showphysx", ShowPhysx);
        }
    }

    /// <summary>Chroma (Unk_GoChroma): reset FUN_1412616a0 gameoptionstruct.cpp:3219-3222, load :3226-3230, save :3234-3238
    /// (written only when set).</summary>
    public sealed class Chroma
    {
        public bool Disable;
        public void Load(XDocument d) => Xml.ReadBool(d, "chroma", "disable", ref Disable);
        public void Save(XDocument d) { if (Disable) Xml.WriteBool(d, "chroma", "disable", Disable); }
    }

    /// <summary>Language (Unk_LgState): reset FUN_14125f0c0 gameoptionstruct.cpp:2466-2490 (language and voice = the region
    /// index 0..3), load :2494-2499, save :2503-2507.</summary>
    public sealed class Language
    {
        public int LanguageIndex, VoiceIndex;
        public Language(int regionIndex) { LanguageIndex = regionIndex; VoiceIndex = regionIndex; }

        public void Load(XDocument d)
        {
            Xml.ReadInt(d, "language", "languageIndex", ref LanguageIndex);
            Xml.ReadInt(d, "language", "voiceIndex", ref VoiceIndex);
        }

        public void Save(XDocument d)
        {
            Xml.WriteInt(d, "language", "languageIndex", LanguageIndex);
            Xml.WriteInt(d, "language", "voiceIndex", VoiceIndex);
        }
    }

    /// <summary>GamePad (Unk_GpState, 0x10 bytes; the character file's "gamepad" section): reset FUN_14125f2c0
    /// gameoptionstruct.cpp:2525-2532, load :2537-2565 (the sensitivities clamp to 0.01..0.5; vibration with feature 0xc3 and the
    /// left stick with 0xd6, that one clamped to 0..100), save :2571-2582. <see cref="LStickDefault"/> is the client constant
    /// read at reset (clientconstants +0xe5b0), which the tables here do not carry.</summary>
    public sealed class GamePad
    {
        public static int LStickDefault;
        public bool Use, ShowSkillName = true, Vibration;
        public float SensitivityH = 0.25f, SensitivityV = 0.25f;
        public int LStick = LStickDefault;

        public void Reset()
        {
            Use = false; ShowSkillName = true; Vibration = false;
            SensitivityH = 0.25f; SensitivityV = 0.25f; LStick = LStickDefault;
        }

        public void Load(XDocument d, bool vibration, bool lstick)
        {
            Xml.ReadBool(d, "gamepad", "bGamePadUse", ref Use);
            Xml.ReadBool(d, "gamepad", "bShowSkillName", ref ShowSkillName);
            Xml.ReadFloat(d, "gamepad", "sensitivityH", ref SensitivityH);
            Xml.ReadFloat(d, "gamepad", "sensitivityV", ref SensitivityV);
            SensitivityH = Math.Clamp(SensitivityH, 0.01f, 0.5f);
            SensitivityV = Math.Clamp(SensitivityV, 0.01f, 0.5f);
            if (vibration) Xml.ReadBool(d, "gamepad", "bVibration", ref Vibration);
            if (lstick)
            {
                Xml.ReadInt(d, "gamepad", "gamePadLStickMoveSensitivity", ref LStick);
                LStick = Math.Clamp(LStick, 0, 100);
            }
        }

        public void Save(XDocument d, bool vibration, bool lstick)
        {
            Xml.WriteBool(d, "gamepad", "bGamePadUse", Use);
            Xml.WriteBool(d, "gamepad", "bShowSkillName", ShowSkillName);
            Xml.WriteFloat(d, "gamepad", "sensitivityH", SensitivityH);
            Xml.WriteFloat(d, "gamepad", "sensitivityV", SensitivityV);
            if (vibration) Xml.WriteBool(d, "gamepad", "bVibration", Vibration);
            if (lstick) Xml.WriteInt(d, "gamepad", "gamePadLStickMoveSensitivity", LStick);
        }

        /// <summary>FUN_14125f640, the 0x10-byte copy.</summary>
        public void CopyFrom(GamePad o)
        {
            Use = o.Use; ShowSkillName = o.ShowSkillName; Vibration = o.Vibration;
            SensitivityH = o.SensitivityH; SensitivityV = o.SensitivityV; LStick = o.LStick;
        }

        /// <summary>The memcmp of CGameOptionGamePad::vfunc_14 (@0x140a57db0).</summary>
        public bool SameAs(GamePad o) => Use == o.Use && ShowSkillName == o.ShowSkillName && Vibration == o.Vibration
            && BitConverter.SingleToInt32Bits(SensitivityH) == BitConverter.SingleToInt32Bits(o.SensitivityH)
            && BitConverter.SingleToInt32Bits(SensitivityV) == BitConverter.SingleToInt32Bits(o.SensitivityV) && LStick == o.LStick;
    }

    /// <summary>The client's option-file helpers (gameoptionstruct.cpp:3719-4046) over TinyXML (commonfiles/tinyxml.cpp).</summary>
    internal static class Xml
    {
        static XElement Section(XDocument d, string section) => d.Root?.Element(section);

        /// <summary>ReadGameOptionBool (:3911-3929): an empty or missing attribute leaves the value; "true"/"false" in any case
        /// set it; any other text leaves it. True when the attribute has text (the client returns 1 then).</summary>
        public static bool ReadBool(XDocument d, string section, string key, ref bool value)
        {
            var s = (string)Section(d, section)?.Attribute(key);
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) value = true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) value = false;
            return true;
        }

        /// <summary>ReadGameOptionInt (:3932-3939): TiXmlElement::Attribute(name, int*) (tinyxml.cpp:562-576) parses with
        /// sscanf "%d" (QueryIntValue, tinyxml.cpp:1160); text that does not start with a number leaves the value.</summary>
        public static void ReadInt(XDocument d, string section, string key, ref int value)
        {
            var e = Section(d, section);
            if (e != null) ReadInt(e, key, ref value);
        }

        public static void ReadInt(XElement e, string key, ref int value)
        {
            var m = IntPrefix.Match((string)e.Attribute(key) ?? "");
            if (m.Success && long.TryParse(m.Value.TrimStart(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v))
                value = (int)v;
        }

        /// <summary>ReadGameOptionFloat (:3954-3968): Attribute(name, double*) runs sscanf "%lf" (tinyxml.cpp:579-590, :1167)
        /// into a local that starts at 0.0 and stores it whenever the attribute exists, so text that does not parse stores 0.</summary>
        public static void ReadFloat(XDocument d, string section, string key, ref float value)
        {
            var s = (string)Section(d, section)?.Attribute(key);
            if (s == null) return;
            var m = FloatPrefix.Match(s);
            value = m.Success && double.TryParse(m.Value.TrimStart(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (float)v : 0f;
        }

        /// <summary>ReadGameOptionString / StringW (:3972-3998): true when the attribute is there.</summary>
        public static bool ReadString(XDocument d, string section, string key, ref string value)
        {
            var s = (string)Section(d, section)?.Attribute(key);
            if (s == null) return false;
            value = s;
            return true;
        }

        /// <summary>ReadGameOptionUInt64 (:4011-4018): XmlQuery with _strtoui64 base 10 (commonfiles/tinyxmlhelper.cpp:261-267).</summary>
        public static void ReadUInt64(XDocument d, string section, string key, ref ulong value)
        {
            var s = (string)Section(d, section)?.Attribute(key);
            if (s != null) value = ParseUInt64Prefix(s);
        }

        /// <summary>_strtoui64(s, NULL, 10): leading space, an optional sign (a minus negates modulo 2^64), digits; no digits
        /// gives 0, overflow gives the maximum.</summary>
        public static ulong ParseUInt64Prefix(string s)
        {
            var m = UIntPrefix.Match(s);
            if (!m.Success) return 0;
            ulong v = ulong.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong p) ? p : ulong.MaxValue;
            return m.Groups[1].Value == "-" && v != ulong.MaxValue ? unchecked(0 - v) : v;
        }

        static readonly Regex IntPrefix = new(@"^\s*[+-]?\d+", RegexOptions.CultureInvariant);
        static readonly Regex UIntPrefix = new(@"^\s*([+-]?)(\d+)", RegexOptions.CultureInvariant);
        static readonly Regex FloatPrefix = new(@"^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?", RegexOptions.CultureInvariant);

        /// <summary>WriteGameOption* (:3719-3909): set the attribute on the first section of that name, or append a new
        /// section element to the root. No root, nothing written.</summary>
        public static void WriteString(XDocument d, string section, string key, string value)
        {
            if (d.Root == null) return;
            var e = Section(d, section);
            if (e == null) d.Root.Add(e = new XElement(section));
            e.SetAttributeValue(key, value);
        }

        public static void WriteBool(XDocument d, string section, string key, bool value) => WriteString(d, section, key, value ? "true" : "false");
        public static void WriteInt(XDocument d, string section, string key, int value) => WriteString(d, section, key, value.ToString(CultureInfo.InvariantCulture));

        /// <summary>SetDoubleAttribute, "%g" (tinyxml.cpp:676, TiXmlAttribute::SetDoubleValue).</summary>
        public static void WriteFloat(XDocument d, string section, string key, float value) => WriteString(d, section, key, ((double)value).ToString("G6", CultureInfo.InvariantCulture));

        /// <summary>FUN_141263d30 (:4022-4032): drops an attribute of the section.</summary>
        public static void Remove(XDocument d, string section, string key) => Section(d, section)?.Attribute(key)?.Remove();
    }
}
