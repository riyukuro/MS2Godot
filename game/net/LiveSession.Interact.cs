using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The live field's interact objects (WorldService m_678, OnInteractObject 0x63, InteractAction / GatheringAction /
/// StateInteraction, <see cref="LiveInteractObjects"/>). Created per RequestFieldEnter and attached once the field's
/// xblock entities are loaded, mirroring <see cref="Portals"/>.
/// </summary>
public partial class LiveSession
{
    public LiveInteractObjects InteractObjects { get; private set; }
    /// <summary>The HUD's casting bar (UICastingBar 0x4e72); set by HudScene while the field HUD is up.</summary>
    public CastingBarController CastingBar;
    /// <summary>Cross-channel/field interact notices (FUN_1412751b0); the HUD sets it when chat is up.</summary>
    public Action<string> ChatNoticeSink;
    public int InteractSends { get; private set; }

    void NewFieldInteractObjects(int mapId)
    {
        InteractObjects?.CancelCast();
        var packs = PackSet.Shared(Settings.DataDir);
        InteractObjectTable tbl = null;
        try { tbl = InteractObjectTable.Shared(packs); }
        catch (Exception e) { GD.Print($"live: interact object table unavailable ({e.Message})"); }
        InteractObjects = new LiveInteractObjects(new InteractHost(this, mapId), tbl);
    }

    /// <summary>Binds the loaded field's xblock MS2InteractObject entities and applies any held 0x63 messages.</summary>
    public void AttachInteractObjects(string xblock, IReadOnlyList<FieldEntity> entities)
    {
        if (InteractObjects == null || InteractObjects.Attached) return;
        string want = null;
        try { want = FieldData.FindById(PackSet.Shared(Settings.DataDir), fieldEntry.Current?.MapId ?? 0)?.XBlock; } catch { }
        bool same = want != null && string.Equals(want, xblock, StringComparison.OrdinalIgnoreCase);
        InteractObjects.Attach(same ? entities : Array.Empty<FieldEntity>());
        GD.Print($"live: interact objects attached to xblock {xblock}{(same ? "" : $" (different xblock: no entities)")}: {InteractObjects.All.Count} registered");
    }

    void OnInteractObject(InteractObjectMessage m)
    {
        if (InteractObjects == null) return;
        InteractObjects.Apply(m);
        if (m.Mode == InteractObjectMessage.ModeStates)
            GD.Print($"live: InteractObject mode 8 ({m.Entries?.Count ?? 0} states)");
        else if (m.Mode is InteractObjectMessage.ModeState or InteractObjectMessage.ModeAdd or InteractObjectMessage.ModeRemove or InteractObjectMessage.ModeResult or InteractObjectMessage.ModeMessage)
            GD.Print($"live: InteractObject mode {m.Mode} name '{m.Name}' kind {m.Kind} state {m.State} result {m.Result}");
    }

    /// <summary>Per-frame action-key poll for interact objects (0 B/frame).</summary>
    public bool PollInteract(Vec3 at) => InteractObjects != null && InteractObjects.Poll(at);

    /// <summary>The billboard action's poll (key 0x18, after the portal; 0 B/frame).</summary>
    public bool PollInteractBillBoard(Vec3 at) => InteractObjects != null && InteractObjects.PollBillBoard(at);

    /// <summary>Per-frame tick for gathering respawns and active StateInteraction cast (0 B/frame).</summary>
    public void TickInteract(Vec3 at, bool playerMoved) => InteractObjects?.Tick(at, playerMoved);

    /// <summary>Action key press on the polled interact object.</summary>
    public bool UseInteract(Vec3 at) => InteractObjects != null && InteractObjects.Use(at);

    sealed class InteractHost : IInteractHost
    {
        readonly LiveSession s;
        readonly int fieldId;
        Dictionary<string, string> actionNames, noticeTexts, itemNames;
        float? masteryDist;

        public InteractHost(LiveSession s, int fieldId) { this.s = s; this.fieldId = fieldId; }
        static PackSet Packs => PackSet.Shared(Settings.DataDir);
        static GameStrings Strings => new(Packs, GameOptions.StringLanguage);

        public bool HasPlayer => s.selfObject >= 0 && s.player != null && IsInstanceValid(s.player);
        public bool IsRestricted(int kind) => false;
        public int FieldId => fieldId;
        public ushort Channel => 0;
        public long NowMs => (long)Time.GetTicksMsec();
        public bool PlayerInSkillState => s.Me.State == 0x10;
        public bool PlayerRiding => s.player != null && IsInstanceValid(s.player) && s.player.Mount != null;

        public float MasteryInteractionDistance
        {
            get
            {
                if (masteryDist is { } d) return d;
                float v = 300f;
                try { v = FieldContents.Constants(Packs).Float("MasteryObjectInteractionDistance", 300f); } catch { }
                masteryDist = v;
                return v;
            }
        }

        public QuestMaskTable.QuestStatus QuestStatusOf(int questId) => s.QuestStatusOf(questId);

        public bool HasRequiredItem(uint code, int rank, uint consume)
        {
            if (code == 0) return true;
            foreach (var it in s.Items.All)
            {
                if ((uint)it.Id != code) continue;
                if (rank != 0 && it.Rarity != rank) continue;
                if ((uint)it.Amount >= consume) return true;
            }
            return false;
        }

        // STILL-OPEN: PCBase additional effect active check (FUN_140970020) is not wired to LiveSession buffs yet
        public bool HasAdditionalEffect(uint effectId, ushort level) => false;
        public string AdditionalEffectName(uint effectId, ushort level)
        {
            try { return GameTables.Shared(Packs, GameOptions.StringLanguage).AdditionalDescription(effectId, level, "name") ?? ""; }
            catch { return ""; }
        }

        public bool IsPartyLeader => s.Me.User != null && s.Party.IsLeader(s.Me.User.CharacterId);
        // STILL-OPEN: mastery grade & recipe checks from MasteryData
        public int MasteryGrade(int masteryType) => 0;
        public bool CanGatherRecipe(int recipeId) => true;

        public void Send(Ms2.Net.Wire.IOutPacket packet)
        {
            s.Send(packet);
            s.InteractSends++;
            GD.Print($"live: interact send {packet}");
        }

        public string Common(int id) => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
        public string CommonByKey(string key) => MessageBoxData.CommonByKey(key) ?? "";

        public string ItemName(uint itemCode)
        {
            try { itemNames ??= Strings.Table("itemname", "name"); } catch { itemNames = new(); }
            return itemNames.TryGetValue(itemCode.ToString(), out var n) ? n : itemCode.ToString();
        }

        public string InteractActionName(int rowId)
        {
            try { actionNames ??= Strings.Table("stringinteractobjectaction", "name"); } catch { actionNames = new(); }
            return actionNames.TryGetValue(rowId.ToString(), out var n) ? n : "";
        }

        public string InteractNoticeText(int nameId)
        {
            try { noticeTexts ??= Strings.Table("stringinteractobjectnotice", "name"); } catch { noticeTexts = new(); }
            return noticeTexts.TryGetValue(nameId.ToString(), out var n) ? n : "";
        }

        public string MasteryTypeName(int type) => type.ToString();
        public string MasteryGradeName(int type, int value) => value.ToString();

        public void SystemNotice(string text, int style = 1)
        {
            if (string.IsNullOrEmpty(text)) return;
            GD.Print($"live: interact notice (style {style}) '{text}'");
            s.SystemNoticeSink?.Invoke(text);
        }

        public void ChatNotice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GD.Print($"live: interact chat notice '{text}'");
            if (s.ChatNoticeSink != null) s.ChatNoticeSink(text);
            else s.SystemNoticeSink?.Invoke(text);
        }

        public void StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            if (s.CastingBar != null)
                s.CastingBar.StartCastingText(listener, text ?? "", durationMs, showTime: false, activeBar: true, endText: "");
            else
                listener.OnCastingFinished();
        }

        public void CancelCasting() => s.CastingBar?.Cancel();

        public void PlayPlayerAnim(string animName, bool loop)
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StartInteractAnim(animName, loop);
        }

        public void StopPlayerInteractAnim()
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StopInteractAnim();
        }

        public void OnObjectStateChanged(LiveInteractObject obj)
        {
            // STILL-OPEN: dynamic mesh swap / visibility / PhysX box toggle on the rendered field entity
        }

        public void OnObjectSuccessEffect(LiveInteractObject obj, string effectAsset)
        {
            GD.Print($"live: interact effect '{effectAsset}' at {obj.Name}");
        }

        public void OnOpenUiDialog(string swf, string value)
        {
            GD.Print($"live: interact open UI '{swf}' value '{value}' (STILL-OPEN)");
        }

        public void OnGuideTrigger(int triggerType, int rowId) => s.guide?.AddTrigger((Ms2.Ui.Guide.GuideTrigger)triggerType, rowId);

        public void StillOpen(string what) => GD.Print($"live: STILL-OPEN {what}; nothing sent");
    }
}
