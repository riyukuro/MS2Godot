using System;
using Godot;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

// The own player's casts from the keyboard: the attack key and the quick slot keys.
public partial class HudScene
{
    readonly CastWorld castWorld = new();          // CSkillSystem's last cast time / skill, kept across casts
    Ms2.Formats.World.SkillTable castMotions;      // the motion effects, the table the replayed casts read

    /// <summary>What the own skill state's motion controller reads (the remote casts' CastMove, RemoteLayer.Casts): the motion's
    /// movetype, keyMove, movedistance and moveangle, the skill's stateAttr, and the record's m_kStart
    /// (<paramref name="dir"/>, MS2 axes). Zero motion data without the motion row.</summary>
    Ms2.Net.Replay.CastMove OwnCastMove(int skillId, int level, int motionIndex, System.Numerics.Vector3 dir)
    {
        castMotions ??= Ms2.Formats.World.SkillTable.For(assets.Packs);
        var st = castMotions.StateMove(skillId);
        var m = castMotions.Motion(skillId, level, motionIndex);
        return m == null
            ? new Ms2.Net.Replay.CastMove(0, 0, 0, 0, st.MoveType, st.MovingInMovekey, st.MagicControl, dir)
            : new Ms2.Net.Replay.CastMove(m.MoveType, m.KeyMove, m.MoveDistance, m.MoveAngle, st.MoveType, st.MovingInMovekey, st.MagicControl, dir);
    }
    // StateSkill's record and m_nAttack for the own cast whose sequence plays, the sequence, and the next point's text key time
    CastRecord ownCast;
    int ownAttack;
    string ownSequence;
    readonly ChargeSync ownSync = new();          // StateSkill's last sent mode 2 sync
    CastRecord ownQueued;                          // StateSkill's m_spCharge: the cooling press queued behind the running skill
    ulong ownStateStartMs;                         // when the own skill state began (StateSkill's m_nElapsed counts from it)
    double ownPointTime = -1;
    readonly SkillLoop ownLoop = new();            // StateSkill's continue loop fields
    // the sequence's "loopstart" / "loopend" times (FUN_141b6fe90 at speed 1), -1 without; the sequence time the text keys
    // were read up to
    float ownLoopStartKey = -1, ownLoopEndKey = -1;
    // a charging skill's "charging_attack" / "charging_combo" times (-1 without) and m_nChargingComboTick (stateskill.cpp:769)
    float ownChargingAttackKey = -1, ownChargingComboKey = -1;
    uint ownChargingComboTick;
    double ownKeyPos = -1;
    SlotWorld ownHoldWorld;                        // the hold check's cast test world, taken once per state
    static readonly SerialIds serialIds = new();   // GenerateSerialId's function static
    static readonly Random crtRandom = new();
    static readonly Func<int> crtRand = () => crtRandom.Next(0x8000);
    Func<long> nextSerial;
    readonly System.Collections.Generic.List<HitCandidate> hitObjects = new();
    readonly System.Collections.Generic.List<HitCandidate> hitQueried = new();
    readonly System.Collections.Generic.List<int> hitIds = new();

    /// <summary>
    /// A key press that gives a skill: MyPC's cast request (mypc.cpp:1690-1704) asks the key's quick slot for an item first
    /// (CKeyTable::GetQuickSlotItemUidByKey), else CKeyTable::GetSkillIdByKey (keytable.cpp:1452): the attack key's normal
    /// attack, or the skill of the quick slot the key is bound to on the current page (GetKeySkillId). A non-zero skill goes to
    /// the request (<see cref="CastOwn"/>). True when the key gave a skill.
    /// The key is read through Unk_PCMove::WasKeyPressed (@0x1416830b0, from MyPC::FUN_141526730), which gives nothing while
    /// Unk_PCMove::FUN_141683ef0 (@0x141683ef0) says no: it asks CInputKeyHandler::FUN_14116d7a0(0xf), so a focused text field
    /// of a movie (CUIService::IsTextFieldFocused) or the loading screen keeps the casts off (<see cref="Ms2.Ui.InputBlock"/>).
    /// STILL-OPEN: the held item key (0x493e2) has no held item here; the skill expansion keys are not read; the item's own
    /// request fields (the item uid on the skill request) are not carried.
    /// </summary>
    bool CastKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || Ms2.Ui.InputBlock.Active || hudModel?.Keys == null) return false;
        for (int dik = 1; dik < 0xd4; dik++)
        {
            if (GodotKeyOfDik(dik) != k.PhysicalKeycode) continue;
            if (CastKey(dik) is { } r) return r;
        }
        return false;
    }

    /// <summary>One key of <see cref="CastKeys"/> (MyPC::vfunc_104, mypc.cpp:1687-1701): the item the key's shown quick slot finds
    /// (<see cref="QuickSlotEdit.KeyItemSkill"/>) refuses the press (false) or gives its skill; with none the key's skill. Null when
    /// the key gives no skill, true when it went to the request (<see cref="RequestOwn"/>, then <see cref="RouteOwn"/>).</summary>
    bool? CastKey(int dik)
    {
        int k = KeyRequest(dik, out var r);
        if (k == 0) return null;
        if (k == 1) return false;
        framePress = r;                                  // FUN_141526730 takes the key just pressed first (mypc.cpp:1794-1808)
        framePressFrame = Engine.GetProcessFrames();
        if (r != null) RouteOwn(r);
        return true;
    }

    /// <summary>MyPC::vfunc_104 (mypc.cpp:1687-1729) for a key: 0 when it gives no skill, 1 when its quick slot item refuses,
    /// else 2 with the request's record in <paramref name="r"/> (null when the request refused).</summary>
    int KeyRequest(int dik, out CastRecord r)
    {
        r = null;
        if (hudModel?.Keys is not { } keys || CubeModeHoldsPick()) return 0;
        var slots = hudModel.ServerKeys;
        castKeyWorld = QuickSlotWorld();
        castSlotItemUid ??= i => playerInfo?.SlotItemUid(i) ?? 0;
        // mypc.cpp:1689-1700: the key's quick slot item; a found item refuses or gives its skill
        int id = QuickSlotEdit.KeyItemSkill(castKeyWorld, keys.GetQuickSlotItemUidByKey(dik, playerInfo != null ? castSlotItemUid : null), out bool refused);
        if (refused) return 1;
        castKeyHeld = castKeyWorld.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        if (id == 0) id = keys.GetSkillIdByKey(dik, castKeyWorld.HasPc, CastAttackSkill, null, slots);
        if (id == 0) return 0;
        // mypc.cpp:1682-1685: the compact pick comes first, and with one the key pick is not asked (the request's key is 0)
        if (CompactPick(false, false) is var cp && cp != 0) { r = RequestOwn(CompactWorld(), cp, compactHeld, 0); return 2; }
        r = RequestOwn(castKeyWorld, (uint)id, castKeyHeld, dik);
        return 2;
    }

    /// <summary>MyPC::vfunc_104 gives no skill in cube mode (mypc.cpp:1673, <see cref="Ms2.Ui.CubeCraftState.HoldsSkillPick"/>):
    /// while a lifted object is carried the keys cast nothing.</summary>
    static bool CubeModeHoldsPick() => Net.LiveSession.Instance is { } live && live.CubeCraft.HoldsSkillPick;

    // STILL-OPEN: the right-hand item's own skill (an action whose normalAttack is 0) is not read; 0 here
    Func<int> CastAttackSkill => castAttackSkill ??= () => (int)SkillCast.AttackKeySkill(castKeyWorld, castWorld, castKeyHeld, weaponActions, 0);
    CastRecord framePress;                         // this frame's key press record (CastKey), for the state's end
    ulong framePressFrame = ulong.MaxValue;

    /// <summary>
    /// A clicked quick slot (SkillSlot::vfunc_13): its key, which WorldService::FUN_141bc9fd0 (worldservice_1.cpp:724) queues as
    /// pressed, goes the key's way (<see cref="CastKey"/>); a slot without a key gives FUN_14092f4e0's skill
    /// (<see cref="QuickSlotEdit.UseSkill"/>) to the cast request. STILL-OPEN: the queue itself (the press is read on the next
    /// world tick there, at once here).
    /// </summary>
    void ClickedSlotKey(int dik) => CastKey(dik);

    /// <summary>
    /// Unk_Inventory::FUN_140a635f0 (inventory.cpp:1379), a double-clicked bag item without a function: a type 2, skill or ride
    /// item casts FUN_141769320's skill (<see cref="QuickSlotEdit.ItemSkillId"/>) at level 1, after MyPC::FUN_141526c80's swap,
    /// with the item uid on the record (request.item): CreateSkillRecord directly (<see cref="SkillCast.ItemRequest"/>), not
    /// the quick slot chain (CSkillSystem::FUN_141764e30), then the record to the state (<see cref="RouteOwn"/>, the port's
    /// MyPC::vfunc_102); a ride item's record reaches the ride item use.
    /// STILL-OPEN: the guide restriction 8 (notice 2184).
    /// </summary>
    void CastItemSkill(Ms2.Ui.Items.Item item)
    {
        if (item?.Data is not { Type: 2 or 5 or 9 } data || item.Uid == 0) return;
        if (GetTree().GetFirstNodeInGroup(Player.Group) is not Player me) return;
        var w = QuickSlotWorld();
        int skill = QuickSlotEdit.ItemSkillId(w, data);
        if (skill == 0) return;
        var req = OwnCastRequest(me, 0, 1, 0);
        FillCastWorld(w, me);
        var res = SkillCast.ItemRequest(w, castWorld, req, (uint)skill, item.Uid);
        if (res.Record is { } r) RouteOwn(r);
        else
        {
            GD.Print($"cast: item skill {skill} refused, reason {res.Why}{(res.Notice != null ? " " + res.Notice : "")}");
            ShowCastRefusal(res.Why);
        }
    }

    /// <summary>The center notice and system sound of a refused cast (FUN_141769440: FUN_141275260(id, 1, 0) and the sound),
    /// the ids and sounds of <see cref="CastNotice.Of"/>; a reason without a notice shows nothing. Shared by the own cast, the
    /// item cast and the compact pick.</summary>
    void ShowCastRefusal(int why)
    {
        var (id, sound) = CastNotice.Of(why);
        if (id == 0) return;
        string text = Ms2.Ui.Hud.MessageBoxData.Common(id);
        if (!string.IsNullOrEmpty(text)) ShowSystemText(text);
        if (sound != null) FieldAudio.Instance?.PlayUi(sound);
    }

    /// <summary>A request of the own pc (the Unk_InventorySkillRequest ctor: a new cast id, the server tick) at the body's
    /// point, facing and rotation, with the key.</summary>
    static CastRequest OwnCastRequest(Player me, uint skillId, ushort level, int key)
    {
        var g = me.GlobalPosition;
        float rotZ = me.FacingRotZ, rad = Mathf.DegToRad(rotZ);
        return new CastRequest(skillId, level, Net.LiveSession.NewCastId(), Net.LiveSession.CastTick())
        {
            Position = new System.Numerics.Vector3(g.X, -g.Z, g.Y),
            Direction = new System.Numerics.Vector3(MathF.Sin(rad), -MathF.Cos(rad), 0),
            RotZ = rotZ,
            Key = key,
        };
    }

    void ClickedSlotSkill(uint skillId)
    {
        var w = QuickSlotWorld();
        if (!QuickSlotEdit.UseSkill(w, skillId)) return;
        var held = w.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        CastOwn(w, skillId, held, 0);
    }
    SlotWorld castKeyWorld;
    WeaponAction castKeyHeld;
    Func<int> castAttackSkill;
    Func<int, long> castSlotItemUid, castKeyItemUid;
    Func<long, bool?> castItemAtLevel;

    /// <summary>
    /// CSkillSystem::FUN_141764e30 for the own player (<see cref="SkillCast.Request"/>) at MyPC::GetSkillLevel, then
    /// MyPC::vfunc_102 @0x141521fc0 (mypc.cpp:1146-1234): FUN_141525580's refusals and ride off (<see cref="OwnUseAllowed"/>),
    /// the cooldown gate (<see cref="SkillCast.CooldownGate"/>), the mode 0 send, FUN_141766cc0's reuse record, the record's
    /// motion on the own body, the cast's cooldown (FUN_141525ff0) and the charge use (OnRechargeSkillUsed), then the motion
    /// effect.
    /// STILL-OPEN, not fired here: the item kind and quick slot item checks with the binding confirm (:1167-1193);
    /// NotifyUseSKill and the DPS meter (:1206, :1210); a level with a sequence while riding, and a level without one's
    /// ridee FUN_141706650 / FUN_141524a80 (:1220-1224); its StateSkill::FireFirstAttack (:1225) is
    /// <see cref="FireOwnAttack"/>.
    /// <paramref name="key"/>: the DIK key of the press (kReq.m_n34, mypc.cpp:1690-1729), 0 for a clicked slot without a key;
    /// the record keeps it (m_nA4, skillrecord.cpp:192) for the charging skill's release (<see cref="TickOwnSync"/>).
    /// </summary>
    void CastOwn(SlotWorld w, uint skillId, WeaponAction held, int key)
    {
        if (RequestOwn(w, skillId, held, key) is { } r) RouteOwn(r);
    }

    /// <summary>The request part of <see cref="CastOwn"/>: the key-down skill's hold limit (<see cref="HeldKeyPoll.HeldTooLong"/>,
    /// mypc.cpp:1711-1726, a silent refusal), then CSkillSystem::FUN_141764e30; the record, or null when refused.</summary>
    CastRecord RequestOwn(SlotWorld w, uint skillId, WeaponAction held, int key)
    {
        if (Net.LiveSession.Instance is { TaxiHoldsSkill: true }) return null;   // the client never calls vfunc_104 in 0x66-0x6a (statetaxicalldata.cpp:304)
        if (GetTree().GetFirstNodeInGroup(Player.Group) is not Player me) return null;
        var level = w.Jobs?.SkillLevel(skillId) ?? 1;
        var d = w.Tables.GetSkillData(skillId);
        var lv = d?.GetLevelData(level);
        slotConstants ??= Ms2.Ui.FeatureElements.Constants(assets.Packs);
        if (lv != null && heldKeys.HeldTooLong(d, key, (uint)Net.LiveSession.CastTick(),
                (uint)slotConstants.Int("KeyIsDownSkill_MaxDurationTick", 30000))) return null;
        // mypc.cpp:1728: MyPC::FUN_141526c80, the changeSkill swap, before CSkillSystem::FUN_141764e30
        if (SkillCast.OwnSkill(w, ref skillId, ref level, out bool hasSequence))
            GD.Print($"cast: skill {d.Id} level {lv.Level} changed to skill {skillId} level {level}");
        var req = OwnCastRequest(me, skillId, level, key);
        req.HasSequence = hasSequence;
        FillCastWorld(w, me);
        castSlotItemUid ??= i => playerInfo?.SlotItemUid(i) ?? 0;
        castKeyItemUid ??= k => hudModel?.Keys?.GetQuickSlotItemUidByKey(k, playerInfo != null ? castSlotItemUid : null) ?? 0;
        castItemAtLevel ??= uid => Net.LiveSession.Instance?.ItemAtOwnLevel(uid);
        var res = SkillCast.Request(w, castWorld, req, _ => SkillCast.NormalAttackSkillId(held, weaponActions, 0),
            key != 0 ? castKeyItemUid : null, castItemAtLevel);
        if (res.Record is not { } r)
        {
            GD.Print($"cast: skill {skillId} refused, reason {res.Why}{(res.Notice != null ? " " + res.Notice : "")}");
            ShowCastRefusal(res.Why);
            return null;
        }
        return r;
    }

    /// <summary>What CreateSkillRecord reads of the own pc now (<see cref="CastWorld"/>), and its aim.</summary>
    void FillCastWorld(SlotWorld w, Player me)
    {
        SkillCast.OwnDead(w, Net.LiveSession.Instance?.Me);     // FUN_14176a380's 0xc on PCBase::IsDead
        var sp = me.Velocity;
        castWorld.Jumping = me.State == Player.MoveState.Jump;
        castWorld.Rising = sp.Y > 0;
        castWorld.Falling = me.State == Player.MoveState.Fall;
        castWorld.GameTime = Time.GetTicksMsec() / 1000.0;
        // vfunc_100(rec, 1), skillslot.cpp:2178: the same-skill keep (pcbase.cpp:2575-2591), then the auto target from it
        castWorld.Aim = ownAutoTarget ??= rec => AimOwn(rec, true);
    }

    /// <summary>
    /// StateSkill::FUN_1417f0570 @0x1417f0570 (stateskill.cpp:2161) for the own skill state: the running record's auto change
    /// (<see cref="SkillCast.AutoChangePoll"/>), which sets the window's m_b11d / m_b11e; null for none. 0 B for a running
    /// skill without an autoChange or with its window closed.
    /// </summary>
    CastRecord OwnAutoChange(int kind)
    {
        if (ownCast == null || player == null || !SkillFollow.AutoChangeWanted(ownCast, kind, ownWindow)) return null;
        var w = OwnPollWorld();
        FillCastWorld(w, player);
        ownAutoCastId ??= Net.LiveSession.NewCastId;
        ownAutoTick ??= Net.LiveSession.CastTick;
        return ownAuto.Get(w, castWorld, ownCast, kind, ref ownWindow, ownAutoCastId, ownAutoTick);
    }

    /// <summary>FUN_1417eeb10 (stateskill.cpp:3008-3015) with no record polled: the auto change FUN_1417f0570(0) as the
    /// press would go (<see cref="OwnPress"/>). The same record queued again is left as it is (the per-frame chain runs).</summary>
    void OwnAutoPress()
    {
        var r = OwnAutoChange(0);
        if (r == null || r == ownNext) return;
        OwnPress(r, false);
    }

    readonly SkillCast.AutoChangePoll ownAuto = new();
    Func<long> ownAutoCastId;
    Func<int> ownAutoTick;

    /// <summary>
    /// Where a requested record goes: while the own skill state's sequence runs, StateSkill::FUN_1417eeb10
    /// (stateskill.cpp:2996-3016) makes it the state's next cast (<see cref="OwnPress"/>); after the sequence's end, while the
    /// state holds on, FUN_1417ee120 takes it at its next step (m_spNext); else MyPC::vfunc_102 (<see cref="UseOwnRecord"/>).
    /// </summary>
    void RouteOwn(CastRecord r, bool log = true)
    {
        if (OwnSkillStateRuns())
        {
            if (ownEnded) ownNext = r;
            else OwnPress(r, log);
            return;
        }
        if (GetTree().GetFirstNodeInGroup(Player.Group) is Player me) UseOwnRecord(me, r);
    }

    /// <summary>
    /// StateSkill::FUN_1417eeb10 @0x1417eeb10 (stateskill.cpp:2996-3016) for a press while the own skill state runs: a record
    /// <see cref="SkillFollow.MayFollow"/> (FUN_1417eed70) lets follow becomes the state's next cast (m_spNext, a later
    /// accepted press replacing it), started by <see cref="TryChainOwn"/> (FUN_1417ef4a0) now for a chain type, else at the
    /// combo keys or the sequence's end; any other press is dropped, so the running motion is not restarted by its own key.
    /// With no record polled the auto change goes instead (<see cref="OwnAutoPress"/>, :3009).
    /// STILL-OPEN: the skill system's queued record for a record with m_nB0 (FUN_1417f0230, :3003) is not ported, nor the
    /// auto change after a press this frame that the request refused; a record a motion other than the last would chain is kept
    /// until the next motion starts (<see cref="OwnNextMotion"/>; the state's enter clears m_spNext).
    /// </summary>
    bool OwnPress(CastRecord r, bool log = true)
    {
        var w = OwnPressWindow();
        if (!SkillFollow.MayFollow(ownCast, r.Skill, w, hudModel.IsFeatureEnabled(0xca), ref ownChainType))
        {
            if (log) GD.Print($"cast: skill {r.Skill.Id} pressed while skill {ownCast.Skill.Id} runs, dropped");
            return false;
        }
        ownNext = r;
        if (ownChainType != 0) TryChainOwn(ownCast);
        return true;
    }

    /// <summary>The combo flags with the running key held (IsKeyDown, :3043) and m_fChargeTime 0 (:3041), as MayFollow reads them.</summary>
    SkillFollow.Window OwnPressWindow()
    {
        var w = ownWindow;
        w.KeyHeld = CastKeyDown(ownCast.Key);
        w.ChargeTimeZero = MathF.Abs(ownLoop.ChargeTime) < 1e-06f;
        return w;
    }

    /// <summary>
    /// StateSkill::vfunc_8 @0x1417e6990 (stateskill.cpp:633-641) each frame while the own sequence runs: FUN_1417eeb10 polls
    /// MyPC::vfunc_104 with bAuto (mypc.cpp:1668), so a key held down (<see cref="PollOwnKey"/>) gives a record each frame as
    /// a press would (<see cref="OwnPress"/>): holding the attack key queues the basic attack again at each window, and it
    /// chains at "selfcombo" or the end. A press this frame went its way already (<see cref="CastKeys"/>). The same key and
    /// skill as the queued next cast, or one refused in the same window, is not asked again, so a held key costs 0 B/frame
    /// (the client makes a record per frame; the outcome is the same).
    /// </summary>
    void PollOwnPress()
    {
        if (framePressFrame != Engine.GetProcessFrames())
        {
            // mypc.cpp:1682: the compact pick first; with one the key pick is not asked
            var cw = OwnPressWindow();
            var cr = CompactPress(cw, out bool picked);
            if (picked)
            {
                if (cr == null) OwnAutoPress();
                else if (cr != ownQueued && (!OwnSkillStateRuns() || !OwnPress(cr, false))) RefusePoll(0, cr.Skill, cw);
                return;
            }
        }
        int dik = PollOwnKey(out bool pressed);
        if (pressed) return;
        if (dik == 0) { OwnAutoPress(); return; }
        var s = OwnKeySkill(dik);
        if (s == null) { OwnAutoPress(); return; }
        if (ownNext != null && ownNext.Key == dik && (object)ownNext.Skill == s) return;
        var w = OwnPressWindow();
        if (PollRefused(dik, s, w)) { if (pollRefusedNoRecord) OwnAutoPress(); return; }
        KeyRequest(dik, out var r);
        if (r == null)
        {
            RefusePoll(dik, s, w);
            pollRefusedNoRecord = true;
            OwnAutoPress();
            return;
        }
        if (!OwnSkillStateRuns() || !OwnPress(r, false)) RefusePoll(dik, s, w);
    }

    readonly HeldKeyPoll heldKeys = new();          // MyPC m_map17c0, m_n15ec / m_n15f0
    readonly System.Collections.Generic.List<int> boundKeys = new();
    Func<int, bool> pollKeyDown, pollNoPress;
    Func<int, SkillData> pollKeySkill;
    SlotWorld ownPollWorld;                         // the poll's world for the keys' skills, taken once per state
    WeaponAction ownPollHeld;
    // the last polled key the request or MayFollow refused, with the state and window it was refused in
    int pollRefusedKey;
    SkillData pollRefusedSkill;
    CastRecord pollRefusedIn;
    SkillFollow.Window pollRefusedWindow;

    bool PollRefused(int dik, SkillData s, in SkillFollow.Window w) =>
        pollRefusedKey == dik && (object)pollRefusedSkill == s && (object)pollRefusedIn == ownCast && pollRefusedWindow.Same(w);

    void RefusePoll(int dik, SkillData s, in SkillFollow.Window w)
    {
        pollRefusedKey = dik; pollRefusedSkill = s; pollRefusedIn = ownCast; pollRefusedWindow = w; pollRefusedNoRecord = false;
    }

    bool pollRefusedNoRecord;                      // the refused poll made no record (vfunc_104 null: the auto change goes)

    /// <summary>The poll's world for the keys' skills (QuickSlotWorld), taken once per state.</summary>
    SlotWorld OwnPollWorld()
    {
        if (ownPollWorld == null)
        {
            ownPollWorld = QuickSlotWorld();
            ownPollHeld = ownPollWorld.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        }
        return ownPollWorld;
    }

    /// <summary>
    /// MyPC::FUN_141526730 @0x141526730 (mypc.cpp:1767-1859) with bAuto over the bound keys (CKeyTable::GetBoundKeys): the
    /// key <see cref="HeldKeyPoll.Pick"/> takes, 0 for none. The pressed keys come as events (<see cref="CastKeys"/>), so the
    /// poll reads only the keys down; <paramref name="pressed"/> is true in a frame a press went its way. 0 B.
    /// </summary>
    int PollOwnKey(out bool pressed)
    {
        pressed = framePressFrame == Engine.GetProcessFrames();
        if (hudModel?.Keys is not { } keys || Ms2.Ui.InputBlock.Active) return 0;   // the keys read nothing then (OwnKeyDown)
        keys.GetBoundKeys(boundKeys, hudModel.ServerKeys);
        castKeyWorld = OwnPollWorld();
        castKeyHeld = ownPollHeld;
        pollKeyDown ??= CastKeyDown;
        pollNoPress ??= _ => false;
        pollKeySkill ??= OwnKeySkill;
        return heldKeys.Pick(boundKeys, true, pollKeyDown, pollNoPress, pollKeySkill, true, (uint)Time.GetTicksMsec());
    }

    /// <summary>GetSkillIdByKey and the skill table's row (FUN_141526730 :1820-1829), null for none.</summary>
    SkillData OwnKeySkill(int dik)
    {
        if (hudModel?.Keys is not { } keys || ownPollWorld == null) return null;
        castKeyWorld = ownPollWorld;
        castKeyHeld = ownPollHeld;
        int id = keys.GetSkillIdByKey(dik, ownPollWorld.HasPc, CastAttackSkill, null, hudModel.ServerKeys);
        return id == 0 ? null : ownPollWorld.Tables.GetSkillData((uint)id);
    }

    /// <summary>
    /// StateSkill::FUN_1417ef4a0 @0x1417ef4a0 (stateskill.cpp:3091-3184): the next cast goes through MyPC::vfunc_102
    /// (<see cref="UseOwnRecord"/>) once <see cref="SkillFollow.ChainsNow"/> lets it and the auto change does not veto it
    /// (<see cref="SkillCast.AutoChangeVetoes"/>); m_spNext is cleared then (:3181).
    /// </summary>
    void TryChainOwn(CastRecord running)
    {
        if (ownNext == null || player == null) return;
        if (!SkillFollow.ChainsNow(running, ownNext.Skill, ownChainType, ownWindow, hudModel.IsFeatureEnabled(0xca), ownLoop.Loop)) return;
        if (SkillCast.AutoChangeVetoes(OwnPollWorld(), running, ownNext, ownWindow)) return;   // :3150-3163
        var r = ownNext;
        ownNext = null;
        AimOwn(r, false);                          // :3170 vfunc_100(m_spNext, 0)
        r.HasSequence = r.Level.HasSequenceName;   // :3172-3174, m_bAD from its level
        UseOwnRecord(player, r);
    }

    /// <summary>
    /// The own sequence reached its end (<see cref="Player.SkillSequenceEnding"/>, before any movement frame): the text keys
    /// left up to the end, EndOfSequence @0x1417e9130 (stateskill.cpp:1062-1070: m_b48, m_b49, m_b112, m_b116, m_fStartTime),
    /// then FUN_1417ee120's end step (<see cref="OwnEndStep"/>) while still in the skill state. True when the state goes on.
    /// STILL-OPEN: EndOfSequence's hold while charging or with a repeat left (<see cref="SkillLoop.HoldsEnd"/>) ends here.
    /// </summary>
    bool OwnSequenceEnding()
    {
        if (ownCast == null || ownEnded || player == null || (object)player.SkillSequence != ownSequence) return false;
        if (!OwnKeys(Math.Max(ownKeyPos, ownSequenceLength))) return ownCast != null;
        ownWindow.EndOfSequence();
        ownEnded = true;
        ownEndMs = Time.GetTicksMsec();
        if (OwnEndStep()) return true;
        EndOwnState();
        return false;
    }

    /// <summary>
    /// FUN_1417ee120 @0x1417ee120 (stateskill.cpp:2893-2928) after the end, at the end and each frame after while the state
    /// holds on: in the last motion the next cast (m_spNext), else the key polled then (vfunc_104(1, 1, 1): this frame's
    /// press, else <see cref="PollOwnKey"/>), goes through vfunc_102 (<see cref="UseOwnRecord"/>) with the running state's
    /// elapsed time, unless <see cref="SkillFollow.CastsAtEnd"/> sends it to idle; m_spNext is cleared. A queued cooling cast
    /// or a cast without a sequence keeps the state (the body holds the last frame), so the next step polls again. False =
    /// idle (FUN_1417f0070).
    /// A record not in its last motion casts its next motion instead (<see cref="OwnNextMotion"/>).
    /// With nothing to cast the auto change FUN_1417f0570(2) goes when <see cref="SkillFollow.AutoCastsAtEnd"/> (:2905-2915).
    /// </summary>
    bool OwnEndStep()
    {
        ownEndStepFrame.Take(Engine.GetProcessFrames());
        var running = ownCast;
        if (running == null) { ownNext = null; return false; }
        if (!SkillFollow.IsLastMotion(running)) return OwnNextMotion(running);
        var next = ownNext ?? OwnEndPoll();
        ownNext = null;
        if (next == null && OwnAutoChange(2) is { } auto && SkillFollow.AutoCastsAtEnd(running, auto)) next = auto;
        if (next == null) return false;
        if (next == ownQueued) { player.HoldSkillSequence(); return true; }   // the same cooling press again: still queued
        if (!SkillFollow.CastsAtEnd(running, next.Skill)) return false;
        AimOwn(next, false);                       // :2939 vfunc_100(m_spNext, 0)
        next.HasSequence = next.Level.HasSequenceName;
        if (!UseOwnRecord(player, next)) return false;
        if (ownCast == running) player.HoldSkillSequence();
        return true;
    }

    /// <summary>
    /// FUN_1417ee120 (stateskill.cpp:2965-2980) for a record not in its last motion: the next motion's record
    /// (<see cref="SkillCast.NextMotion"/>, from where the player is now) through vfunc_102 (<see cref="UseOwnRecord"/>),
    /// idle (FUN_1417f0070) when that fails. m_spNext is left as it is; the next motion's state enter clears it.
    /// </summary>
    bool OwnNextMotion(CastRecord running)
    {
        var g = player.GlobalPosition;
        var next = SkillCast.NextMotion(running, new System.Numerics.Vector3(g.X, -g.Z, g.Y), Net.LiveSession.CastTick());
        if (next == null) return true;             // Init failed: return before the idle and the timeout (:2973)
        if (!UseOwnRecord(player, next)) return false;
        if (ownCast == running) player.HoldSkillSequence();
        return true;
    }

    /// <summary>PCBase::vfunc_100 (pcbase.cpp:2561) on a next cast in the skill state: <see cref="SkillCast.Aim"/> from where
    /// the player is and faces now, not where it was at the press (a continue skill again keeps the running record's aim), then
    /// the auto target (<see cref="AutoTargetOwn"/>); <paramref name="turn"/> is vfunc_100's n.</summary>
    void AimOwn(CastRecord r, bool turn)
    {
        var g = player.GlobalPosition;
        SkillCast.Aim(r, new System.Numerics.Vector3(g.X, -g.Z, g.Y), player.FacingRotZ, ownCast, hudModel.IsFeatureEnabled(0xca));
        AutoTargetOwn(r, turn);
    }

    Action<CastRecord> ownAutoTarget;

    /// <summary>
    /// PCBase::vfunc_101 (pcbase.cpp:2598-2599) for the own player: <see cref="SkillCast.AutoTarget"/> over the live
    /// candidates and the owner (<see cref="FillHitObjects"/>); with <paramref name="turn"/> (vfunc_100's n) a pick sets the
    /// player's target yaw (SetTargetYaw, :2684-2685; PCBase::SetTargetYaw @pcbase.cpp:1431), which the player turns to over
    /// the next frames (<see cref="Player.TurnTo"/>); the loop end snaps to the record's rotation (<see cref="OwnLoopEnd"/>).
    /// </summary>
    void AutoTargetOwn(CastRecord r, bool turn)
    {
        if (r?.Skill == null || r.Skill.AutoTargeting == 0) return;
        if (GetTree().GetFirstNodeInGroup(Player.Group) is not Player me) return;
        var pos = FillHitObjects(me);
        if (!SkillCast.AutoTarget(r, pos, me.FacingRotZ, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hitObjects),
                hitQueried, hitIds, (slotField?.PkMode ?? 0) != 0, out float yaw) || !turn) return;
        float rad = Mathf.DegToRad(yaw);   // MS2 forward (sin z, -cos z) is godot (sin z, 0, cos z)
        me.TurnTo(new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad)));
    }

    /// <summary>The objects an own area pick tests: the live candidates and the owner last; the owner's MS2 position.</summary>
    System.Numerics.Vector3 FillHitObjects(Player me)
    {
        var live = Net.LiveSession.Instance;
        live?.HitCandidates(hitObjects);
        if (live == null) hitObjects.Clear();
        var g = me.GlobalPosition;
        var pos = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        hitObjects.Add(new HitCandidate { Id = live?.SelfObjectId ?? 0, Position = pos, Body = SkillHit.PlayerBody(pos, me.FacingRotZ), IsOwner = true,
            Kind = HitKind.Pc, Team = 1, Hp = 1, PartyId = live?.Party?.PartyId ?? 0,
            PvpOnOff = ownBuffs?.IsPvpOnOff ?? false,
            ForceApplyTarget = ownBuffs?.ForceApplyTarget ?? 0, Hidden = ownBuffs?.IsHidden ?? false,
            StateId = live?.OwnCurStateId(OwnSkillStateRuns()) ?? 0, Invincible = ownBuffs?.Invincible ?? 0 });
        return pos;
    }

    /// <summary>vfunc_104(1, 1, 1) at the end step: this frame's press, else the key held (<see cref="PollOwnKey"/>); the queued
    /// cooling record for its own key and skill; null for none or a key refused before in this state.</summary>
    CastRecord OwnEndPoll()
    {
        if (framePressFrame != Engine.GetProcessFrames())
        {
            var cr = CompactPress(ownWindow, out bool picked);   // mypc.cpp:1682: the compact pick first
            if (picked) return cr;
        }
        int dik = PollOwnKey(out bool pressed);
        if (pressed) return framePress;
        if (dik == 0) return null;
        var s = OwnKeySkill(dik);
        if (s == null) return null;
        if (ownQueued != null && ownQueued.Key == dik && (object)ownQueued.Skill == s) return ownQueued;
        if (PollRefused(dik, s, ownWindow)) return null;
        KeyRequest(dik, out var r);
        if (r == null) RefusePoll(dik, s, ownWindow);
        return r;
    }

    /// <summary>Each frame after the end while the state holds on: 5 s after EndOfSequence (stateskill.cpp:2985-2991) or a step
    /// with nothing to cast goes to idle. Not in the frame the sequence ended: <see cref="OwnSequenceEnding"/> ran its step.</summary>
    void TickOwnEnded()
    {
        if (!player.SkillSequenceHeld) { EndOwnState(); return; }
        if (OwnSwingCancel()) return;
        if (!ownEndStepFrame.Take(Engine.GetProcessFrames())) return;   // the sequence's end ran this frame's step
        var running = ownCast;
        if (Time.GetTicksMsec() - ownEndMs > 5000 || (!OwnEndStep() && ownCast == running)) { player.CancelSkillSequence(); EndOwnState(); }
    }

    void EndOwnState()
    {
        CancelOwnMagics();
        EndOwnTagEffects();
        ownCast = null;
        ownNext = null;
        ownEnded = false;
    }

    /// <summary>StateSkill's exits vfunc_5 and vfunc_7 (stateskill.cpp:520, :582): CMagicService::RemoveCasterModels(m_nCastId, 0, 0),
    /// the running cast's delay-0 magics that have not fired are dropped (<see cref="OwnTagMagics.CancelCaster"/>).</summary>
    void CancelOwnMagics()
    {
        if (ownCast != null) ownTagMagics.CancelCaster(ownCast.CastId, (int)Time.GetTicksMsec());
    }

    FrameOnce ownEndStepFrame;                     // the end step runs once a frame (StateSkill::vfunc_8)
    bool ownEnded;                                 // StateSkill m_b48: EndOfSequence came, the state holds on
    ulong ownEndMs;                                // m_fStartTime from EndOfSequence (stateskill.cpp:1068)
    double ownSequenceLength;                      // the own sequence's kf duration
    Func<bool> ownEndingStep;

    int ownChainType;                              // StateSkill m_nChainType, 0 from the enter (stateskill.cpp:412)
    CastRecord ownNext;                            // StateSkill m_spNext: the accepted press waiting for its chain
    SkillFollow.Window ownWindow;                  // the combo flags the sequence's text keys set (m_b4a, m_b49, m_b114-m_b116)
    SkillSwingCancel ownSwing;                     // StateSkill m_ptPush / m_b110: the move and jump kept in the input window
    readonly SkillFollow.ComboKeys ownComboKeys = new();
    readonly Ms2.Net.Replay.SkillTagEffects.Keys ownTagKeys = new();   // the motion's m_mapTagEffects keys in the sequence
    readonly Ms2.Net.Replay.SkillTagEffects.Handles ownTagIds = new();  // the record's m_map138: the tag effects' ids by key
    Action<int> stopOwnTagEffect, releaseOwnTagEffect;

    // MyPC::FUN_141525580's view of the own pc, made once; its delegates read the live session and the buff list
    UseCheckWorld ownUseCheck;

    /// <summary>
    /// MyPC::FUN_141525580 @0x141525580 (mypc.cpp:2434), vfunc_102's first check (:1150): false refuses the use, and a mounted
    /// IsUnrideOnUse skill sends its ride off first (<see cref="Net.LiveSession.RideOffForSkill"/>) and is refused too.
    /// The own pc is in none of the states the check refuses (0x2b, 0x2f, the busy hold 0x20). STILL-OPEN: the buffs' no-fly
    /// (AdditionalCliList::FUN_140970a00) is not kept, and PC::FUN_14165f150 (m_n1050) is taken as set.
    /// </summary>
    bool OwnUseAllowed(Player me, Net.LiveSession live, CastRecord r)
    {
        ownUseCheck ??= new UseCheckWorld
        {
            EmotionLearned = (id, lv) => Net.LiveSession.Instance is { } s && s.Emotions.IsLearned((int)id, lv, s.ServerTime),
            HasEnabledEffect = id => ownBuffs?.Has(id, 0, 0, 0) == true,
        };
        var w = ownUseCheck;
        w.Riding = me.Mount != null;
        w.OnWater = me.OnWater;
        var held = live?.HeldItem;
        w.HasHeldItem = held is { Held: true } && held.ItemId != 0;
        w.HeldItemSkill = w.HasHeldItem ? hudModel.Tables.Items.Get(held.ItemId)?.SkillId ?? 0 : 0;
        switch (SkillUseCheck.Check(r, w, out var rideOff))
        {
            case SkillUseCheck.Result.Refused:
                GD.Print($"cast: skill {r.Skill.Id} refused");
                return false;
            case SkillUseCheck.Result.RideOff:
                live?.RideOffForSkill(rideOff);
                GD.Print($"cast: skill {r.Skill.Id} rides off first");
                return false;
        }
        return true;
    }

    /// <summary>
    /// MyPC::vfunc_102 @0x141521fc0 (mypc.cpp:1146) from the cooldown gate on, for a new request or the press queued behind the
    /// running skill (<see cref="TickOwnQueued"/>). True when it went (cast or queued), as vfunc_102's return.
    /// </summary>
    bool UseOwnRecord(Player me, CastRecord r)
    {
        var g = me.GlobalPosition;
        var live = Net.LiveSession.Instance;
        if (!OwnUseAllowed(me, live, r)) return false;
        uint now = (uint)Net.LiveSession.CastTick();
        // StateTaxiCall::vfunc_8 (statetaxicalldata.cpp:302-304): no skill fires in the taxi call's phases 0x66-0x6a
        if (live != null && live.TaxiHoldsSkill) return false;
        // MyPC::vfunc_102 (mypc.cpp:1152-1166): a skill still cooling is never sent; in the skill state it becomes the state's
        // queued press (StateSkill::SetChargeRecord @0x1417ed740, stateskill.cpp:1491, a later press replacing it), else dropped.
        // STILL-OPEN: the running record's +0x90 (= 0 here, mypc.cpp:1165; CSkillRecord::FUN_141759fc0 sets it to 1) has no
        // reader in this port.
        if (live != null)
        {
            var gate = SkillCast.CooldownGate(live.Cooltimes, r, OwnSkillStateRuns(), now);
            if (gate == CoolGate.Queued) { ownQueued = r; r.QueuedCharge = true; }
            if (gate != CoolGate.Cast)
            {
                GD.Print($"cast: skill {r.Skill.Id} cooling, {(gate == CoolGate.Queued ? "queued" : "refused")}");
                return gate == CoolGate.Queued;
            }
        }
        // :1197-1203: the running skill state's elapsed time (StateSkill m_nElapsed, FUN_14028d280: ms to seconds)
        float elapsed = OwnSkillStateRuns() ? (Time.GetTicksMsec() - ownStateStartMs) * 0.001f : 0f;
        // StateSkill::FUN_1417ea570: the skill state and the mode 0 send
        bool sent = live?.SendCast(r, hudModel.IsFeatureEnabled(174)) ?? false;
        SkillCast.Casted(castWorld, r);
        SampleAbilityRate();   // FUN_141766cc0 (skillslot.cpp:2350): DAT_143aea120, before the cooldown starts (mypc.cpp:2627)
        float delay = 0f;
        Ms2.Formats.Model.KfAnimation seq = null;
        var motion = r.Motion < r.Level.Motions.Count ? r.Level.Motions[r.Motion] : null;
        // FUN_141525ff0 (mypc.cpp:2557-2563) reads the motion data's sequence from the actor manager (FindSequenceID of
        // m_strSeq), whether or not the skill state plays it
        if (motion != null && !string.IsNullOrEmpty(motion.SequenceName) && me.Anims?.Sequence(motion.SequenceName, false) is { } seqKey)
            me.Anims.Kf.TryGetValue(seqKey, out seq);
        if (r.Level.HasSequenceName)
        {
            // :1213-1216: a level with a sequence enters the skill state (vfunc_59(0x10), which also ends a taxi call in 0x65); the cooldown counts the old state's time off
            live?.TaxiSkillCast(true);
            if (motion != null && !string.IsNullOrEmpty(motion.SequenceName) && me.PlaySkillSequence(motion.SequenceName, motion.SequenceSpeed,
                    OwnCastMove((int)r.Skill.Id, r.Level.Level, r.Motion, r.Direction), r.Rotation.Z))
                StartOwnAttacks(me, r);
            delay = elapsed;
        }
        else if (SkillAttackFire.FirstAttack(r) is int first and >= 0) FireOwnAttack(me, r, first);   // :1225 FireFirstAttack
        CompactUsed(r);   // :1228 FUN_141767990, whether or not it was sent
        // :1230: after the send, the cast's own cooldown (FUN_141525ff0) from the sequence's text keys
        if (sent) live.Cooltimes.StartCast(r, seq, delay, now);
        // :1232: PCBase::FUN_141675ed0 is 0 without a ridee (STILL-OPEN: rides), so the charge use counts
        live?.Cooltimes.OnRechargeSkillUsed(r.Skill, now);
        castMotions ??= Ms2.Formats.World.SkillTable.For(assets.Packs);
        if (castMotions.Motion((int)r.Skill.Id, r.Level.Level, r.Motion)?.MotionEffect is { } fx) live?.PlayFieldEffect(fx, g, me.Visual?.Rotation.Y ?? 0);
        GD.Print($"cast: skill {r.Skill.Id} level {r.Level.Level} motion {r.Motion}{(sent ? " sent" : " (offline, not sent)")}");
        return true;
    }

    /// <summary>
    /// Each frame while the own skill state runs: StateSkill::FUN_1417f11d0 @0x1417f11d0 (stateskill.cpp:1578, from vfunc_3
    /// :623): the queued press, once <see cref="SkillCast.QueuedReady"/>, takes the player's position (:1611), its m_bAD from
    /// its level's sequence (:1612) and goes through MyPC::vfunc_102 (<see cref="UseOwnRecord"/>); the state's enter and exit
    /// (the enter, vfunc_4 :416; the ctor :340) and the state's end here drop it. A record a continue round queued (m_n98,
    /// :1589-1607) plays the loop again instead: the round's sends (<see cref="SendOwnRound"/>) and the seek
    /// (<see cref="SeekOwnLoop"/>, whose body seek runs FUN_1417f1a90, the loop's move0 / move2, :1592). 0 B/frame until it goes.
    /// </summary>
    void TickOwnQueued()
    {
        if (ownQueued == null) return;
        if (!OwnSkillStateRuns()) { ownQueued = null; return; }
        var live = Net.LiveSession.Instance;
        if (live == null || !SkillCast.QueuedReady(live.Cooltimes, ownQueued, (uint)Net.LiveSession.CastTick())) return;
        var r = ownQueued;
        ownQueued = null;
        r.QueuedCharge = false;
        if (r.Charged)
        {
            SendOwnRound(live, r, (uint)Net.LiveSession.CastTick());
            SeekOwnLoop(player);
            return;
        }
        var g = player.GlobalPosition;
        r.Position = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
        r.HasSequence = r.Level.HasSequenceName;
        UseOwnRecord(player, r);
    }

    /// <summary>StateSkill's attack count at the start of the own cast's motion: attack 0, its point's text key.</summary>
    void StartOwnAttacks(Player me, CastRecord r)
    {
        CancelOwnMagics();                         // the running skill state's exit
        EndOwnTagEffects();
        ownCast = r;
        ownQueued = null;                          // StateSkill::vfunc_4 (stateskill.cpp:416)
        ownChainType = 0;                          // :412
        ownNext = null;                            // the enter clears m_spNext and the combo flags (:403-412)
        ownWindow = default;                       // m_b11c-m_b11e too (:413-415)
        ownSwing.Reset();                          // the motion's start (FUN_1417f0940 :2215-2216)
        ownAuto.Clear();
        var motion = r.Motion < r.Level.Motions.Count ? r.Level.Motions[r.Motion] : null;
        Ms2.Formats.Model.KfAnimation kf = null;
        if (me.SkillSequence != null) me.Anims?.Kf.TryGetValue(me.SkillSequence, out kf);
        ownComboKeys.Set(kf?.TextKeys, motion?.SelfInputCombo);
        castMotions ??= Ms2.Formats.World.SkillTable.For(assets.Packs);
        ownTagKeys.Set(kf?.TextKeys, castMotions.Motion((int)r.Skill.Id, r.Level.Level, r.Motion)?.TagEffects);
        ownSequenceLength = kf?.Duration ?? 0;
        ownEnded = false;
        ownPollWorld = null;
        me.SkillSequenceEnding = ownEndingStep ??= OwnSequenceEnding;
        ownSync.Reset(new System.Numerics.Vector3(0, 0, me.FacingRotZ));   // vfunc_4 (:484-487): the owner's rotation
        ownAttack = 0;
        ownSequence = me.SkillSequence;
        ownStateStartMs = Time.GetTicksMsec();
        ownPointTime = OwnPointTime(me);
        // vfunc_4 (stateskill.cpp:395-420) and the sequence start (FUN_1417f0940 :2226-2238): the loop fields, the repeats
        ownLoop.Reset();
        ownLoop.StartSequence(r.Motion < r.Level.Motions.Count ? r.Level.Motions[r.Motion].LoopCount : 0);
        ownLoopStartKey = me.Anims?.TextKey(ownSequence, "loopstart") ?? -1;
        ownLoopEndKey = me.Anims?.TextKey(ownSequence, "loopend") ?? -1;
        ownChargingAttackKey = me.Anims?.TextKey(ownSequence, "charging_attack") ?? -1;
        ownChargingComboKey = me.Anims?.TextKey(ownSequence, "charging_combo") ?? -1;
        // :769: m_nStartTick (the enter's tick, :421) plus the key's time at the state's speed in ms (FUN_14028d260)
        ownChargingComboTick = (uint)ownStateStartMs + (uint)(long)(ownChargingComboKey / SkillCast.MotionSpeedScale(r) * 1000.0f);
        ownKeyPos = -1;
        ownHoldWorld = null;
    }

    /// <summary>The own skill state (0x10) runs: the own cast's sequence still plays.</summary>
    bool OwnSkillStateRuns() => ownCast != null && player != null && (object)player.SkillSequence == ownSequence;

    double OwnPointTime(Player me)
    {
        var a = SkillAttackFire.AttackAt(ownCast, ownAttack);
        if (a == null || string.IsNullOrEmpty(a.Point) || me.Anims == null) return -1;
        return me.Anims.TextKey(ownSequence, a.Point);
    }

    /// <summary>
    /// Each frame while the own skill state runs (StateSkill::vfunc_8 @0x1417e6990): the key-down poll
    /// (<see cref="PollOwnPress"/>, FUN_1417eeb10 :638-639), the text keys passed since the last frame (<see cref="OwnKeys"/>),
    /// then FUN_1417ee120's per-frame FUN_1417ef4a0 (:2889-2891) for an accepted press. After the sequence's end the state's
    /// end step each frame (<see cref="TickOwnEnded"/>). The hold of a key-down skill's key ends with the key let go outside
    /// the state (<see cref="HeldKeyPoll.Release"/>). 0 B/frame until a point fires or a key goes to a request.
    /// </summary>
    void TickOwnAttacks()
    {
        pollKeyDown ??= CastKeyDown;
        heldKeys.Release(pollKeyDown, OwnSkillStateRuns());
        if (ownCast == null) return;
        if (player == null || (object)player.SkillSequence != ownSequence) { EndOwnState(); return; }   // cancelled
        if (ownEnded) { TickOwnEnded(); return; }
        double pos = player.SkillSequencePosition;
        if (pos < 0) return;
        PollOwnPress();
        if (!OwnSkillStateRuns() || ownEnded) return;
        if (!OwnKeys(pos)) return;
        var running = ownCast;
        if (ownNext != null) TryChainOwn(running);
        if (ownCast == running && OwnSkillStateRuns()) OwnSwingCancel();   // vfunc_8's last step (:725)
    }

    /// <summary>
    /// StateSkill::FUN_1417ed7f0 @0x1417ed7f0 (stateskill.cpp:1505-1563), the swing cancel at the end of the own skill state's
    /// frame (<see cref="SkillSwingCancel"/>): a move held or a jump pressed in the motion's input window leaves the attack
    /// from its "combo" / "selfcombo" key or its end, with the attack key let go: SendSkillMode4
    /// (<see cref="Net.LiveSession.SendSkillStop"/>), then the walk or the jump. True when the state ended.
    /// STILL-OPEN: vfunc_63 (the PC's state flags, m_unk_158) is not ported, so the PC can always act here.
    /// </summary>
    bool OwnSwingCancel()
    {
        var me = player;
        var r = ownCast;
        var held = me.SwingHeld;
        var pressed = me.TakeSwingPressed();
        var i = new SwingInput
        {
            CanAct = true,
            Falling = me.SwingFalling,
            Held = new System.Numerics.Vector2(held.X, held.Y),
            Pressed = new System.Numerics.Vector2(pressed.X, pressed.Y),
            KeyIsDown = Player.CancelSwingKeyIsDown != 0,
            JumpPressed = me.TakeSwingJumpPressed(),
            ContinueKeyHeld = r.Skill.Continue && CastKeyDown(r.Key),           // :1533-1538
            Charging = ownQueued != null,                                        // m_spCharge (:1539)
            ExitKeyHeld = CastKeyDown(ownNext != null ? ownNext.Key : r.Key),    // :1543-1547
            MagicControl = r.Skill.MagicControlEnable != 0,                      // m_n10 0x3c (vfunc_4 :496-497)
        };
        var exit = ownSwing.Step(ownWindow, i);
        if (exit == SwingExit.None) return false;
        Net.LiveSession.Instance?.SendSkillStop(r.CastId);
        EndOwnState();
        me.LeaveSkill(exit == SwingExit.Jump, new Vector2(ownSwing.Push.X, ownSwing.Push.Y));
        return true;
    }

    /// <summary>
    /// StateSkill::TextKeyEvent (stateskill.cpp:844-1030) for the text keys the own sequence passed since the last read up to
    /// <paramref name="pos"/> (a key after the last read time and not after <paramref name="pos"/>), in time order: "loopstart"
    /// (<see cref="SkillLoop.OnLoopStart"/>), "charging_combo" (<see cref="SkillLoop.OnChargingCombo"/>), the current attack's
    /// point (the charging skill's m_bLoop, <see cref="SkillLoop.OnAttackPoint"/>, then FireAttacksAtPoint,
    /// <see cref="FireOwnPoint"/>), "charging_attack" (<see cref="SkillLoop.OnChargingAttack"/>), "loopend"
    /// (<see cref="OwnLoopEnd"/>), then the combo keys (<see cref="SkillFollow.ComboKeys.Advance"/>; CanChainCombo :1725: not
    /// for m_b111, the charging skill flag :766, with m_b112 or repeats left) with the accepted press's chain at "combo" and
    /// "selfcombo" (FUN_1417ef4a0, :1004, :1027), so an attack point before the key fires (and sends) first. False when the
    /// state stopped reading on: a point ended it, a chain took over, or a loopend sought the next round.
    /// The move0-3 keys go to the body's skill motion (Player.SkillMotionMove). A key the motion's tag effects name plays its
    /// effect first (<see cref="OwnTagEffects"/>, :792-834).
    /// STILL-OPEN: charging_combo's m_b112 (:952) is not kept in the window. A tag key at the same time as one of the keys
    /// above is taken before it here; the client takes them in the sequence's key order.
    /// </summary>
    bool OwnKeys(double pos)
    {
        double prev = ownKeyPos;
        ownKeyPos = pos;
        uint now = (uint)Time.GetTicksMsec();   // GetTickCountMs2
        bool charging = ownCast.Skill.ChargingSkill;
        while (true)
        {
            double tk = SkillLoop.NextTextKey(prev, pos, ownLoopStartKey, ownChargingComboKey, ownPointTime, ownChargingAttackKey,
                ownLoopEndKey);
            double tc = ownComboKeys.Next(prev, pos);
            double tt = ownTagKeys.Next(prev, pos);
            double t = tk < 0 ? tc : tc < 0 ? tk : Math.Min(tk, tc);
            if (tt >= 0 && (t < 0 || tt < t)) t = tt;
            if (t < 0) return true;
            if (tt == t) OwnTagEffects(t);
            if (tk == t)
            {
                if (ownLoopStartKey == t) ownLoop.OnLoopStart(ownLoopStartKey, now);
                if (ownChargingComboKey == t) ownLoop.OnChargingCombo(charging, CastKeyDown(ownCast.Key), now, ownChargingComboTick);
                while (ownPointTime == t)
                {
                    int attack = ownAttack;
                    ownLoop.OnAttackPoint(charging);
                    FireOwnPoint(player);
                    if (ownCast == null) return false;
                    if (ownPointTime == t && ownAttack == attack) break;
                }
                if (ownChargingAttackKey == t) ownLoop.OnChargingAttack(charging);
                if (ownLoopEndKey == t)
                {
                    OwnLoopEnd(player, now);
                    if (ownKeyPos != pos) return false;   // the next round's seek: the keys read on from there next frame
                }
            }
            if (tc == t)
            {
                var running = ownCast;
                bool canChain = !(running.Skill.ChargingSkill && (ownWindow.Ended || ownLoop.Repeat != 0));
                if (ownComboKeys.Advance(ref ownWindow, prev, t, canChain) && ownNext != null)
                {
                    TryChainOwn(running);
                    if (ownCast != running) return false;   // the chained cast's state took over
                }
            }
            prev = t;
        }
    }

    /// <summary>
    /// StateSkill::TextKeyEvent's tag effects (stateskill.cpp:792-834) for the own sequence's tag keys at <paramref name="t"/>:
    /// each plays its effect on the player unless <see cref="Ms2.Net.Replay.SkillTagEffects.Plays"/> says no (the loop's
    /// m_b117 and m_bLoop, the record's m_n9c), kept on the player by its key (m_map138) for the state's exit
    /// (<see cref="EndOwnTagEffects"/>).
    /// </summary>
    void OwnTagEffects(double t)
    {
        for (int i = 0; i < ownTagKeys.Count; i++)
        {
            var (time, key, path) = ownTagKeys[i];
            if (time != t) continue;
            float at = player.Anims?.TextKey(ownSequence, key) ?? -1;
            if (!Ms2.Net.Replay.SkillTagEffects.Plays(at, ownLoop.InLoop, ownCast.QueuedCharge, ownLoop.Loop, ownLoopStartKey,
                    ownLoopEndKey, ownChargingAttackKey)) continue;
            ownTagIds.Set(key, Net.LiveSession.Instance?.SpawnKeptEffect(path, player.Visual,
                Ms2.Audio.SoundMusicEffectState.CasterMyPc) ?? 0);
        }
    }

    /// <summary>
    /// StateSkill::vfunc_5 (stateskill.cpp:515-524) for the own cast's tag effects (<see cref="Ms2.Net.Replay.SkillTagEffects.Handles.End"/>):
    /// stopped at once when the player is dead, hit (the next state 0xd) or the skill has notKeepEffect, else released.
    /// STILL-OPEN: the stun test (AdditionalCliList::IsStunned) is not made.
    /// </summary>
    void EndOwnTagEffects()
    {
        if (ownTagIds.Count == 0) return;
        bool hard = player == null || player.DeadFlag || player.State is Player.MoveState.Dead or Player.MoveState.Hit
            || (ownCast != null && castMotions?.NotKeepEffect((int)ownCast.Skill.Id) == true);
        ownTagIds.End(hard, stopOwnTagEffect ??= h => Net.LiveSession.Instance?.StopKeptEffect(h),
            releaseOwnTagEffect ??= h => Net.LiveSession.Instance?.ReleaseKeptEffect(h));
    }

    /// <summary>
    /// TextKeyEvent "loopend" (stateskill.cpp:869-944) for the own player: <see cref="SkillLoop.OnLoopEnd"/>; for a next round
    /// m_nAttack 0 and m_bSentCharging off, the record marked (m_n98), then a skill still cooling without a charge use (and not a
    /// charging skill) queues the running record (SetChargeRecord), else the round goes (<see cref="SendOwnRound"/>); an
    /// unqueued round plays the loop again (<see cref="SeekOwnLoop"/>).
    /// A next round first aims the record again (vfunc_100(m_spRecord, 1), :880-881: a continue skill keeps its own aim, see
    /// <see cref="SkillCast.Aim"/>, with the position now) and turns the owner to the record's rotation (SetTargetYaw /
    /// vfunc_21, :893-894).
    /// STILL-OPEN: the record's loop effect stop (m_n88), the camera mode 10 yaw (FUN_1417f4ea0, :885-889) and the weapon
    /// PC's sequence (FUN_141663be0).
    /// </summary>
    void OwnLoopEnd(Player me, uint now)
    {
        // fEventTime: the key's own time here (only its being non-zero is read, by the hold check)
        if (!ownLoop.OnLoopEnd(ownLoopEndKey, ownLoopEndKey, SkillCast.MotionSpeedScale(ownCast), now)) return;
        AimOwn(ownCast, true);
        float rad = Mathf.DegToRad(ownCast.Rotation.Z);   // MS2 forward (sin z, -cos z) is godot (sin z, 0, cos z)
        me.Face(new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad)));
        ownAttack = 0;
        ownSync.ClearCharging();
        ownCast.Charged = true;
        if (Net.LiveSession.Instance is { } live)
        {
            uint tick = (uint)Net.LiveSession.CastTick();
            bool ready = live.Cooltimes.CanUseRechargeSkill(ownCast.Skill, tick);
            float cool = live.Cooltimes.Remain(ownCast.Skill.Id, false, tick);
            if (!ready && cool > 0f && !ownCast.Skill.ChargingSkill) { ownQueued = ownCast; ownCast.QueuedCharge = true; }
            else SendOwnRound(live, ownCast, tick);
        }
        if (!ownCast.QueuedCharge) SeekOwnLoop(me);
        else ownPointTime = OwnPointTime(me);
    }

    /// <summary>
    /// A continue round's sends (loopend :921-924, FUN_1417f11d0 :1591-1595): the mode 3 (CSkillRecord::SendSkill), the round's
    /// cooldown (FUN_1417f16f0 @0x1417f16f0: a continue skill, not a charging skill's marked record; the loop's length at the
    /// state's speed less the charge tick, FUN_1417664e0 from the send tick with the combo flag) and the charge use
    /// (OnRechargeSkillUsed of the running record), with FUN_141767990's compact chain use (:926, :1594) before it.
    /// </summary>
    void SendOwnRound(Net.LiveSession live, CastRecord r, uint now)
    {
        live.SendRound(r);
        if (r.Skill.Continue && !(r.Skill.ChargingSkill && r.Charged))
            live.Cooltimes.Start((uint)r.SendTick, r.Skill.Id,
                SkillLoop.RoundCooldown(ownLoopStartKey, ownLoopEndKey, ownLoop.Speed, ownLoop.ChargeTick), 0f, true, now);
        CompactUsed(r);
        live.Cooltimes.OnRechargeSkillUsed(r.Skill, now);
        GD.Print($"cast: skill {r.Skill.Id} round {r.SendCount}");
    }

    /// <summary>
    /// The next round's start (loopend :930-942, FUN_1417f11d0 :1596-1607): the loop sequence again at loopstart plus the charge
    /// tick. The forced restart reactivates the sequence, so the next frame reads the keys from 0 up to the new time
    /// (<see cref="Ms2.Net.Replay.SkillMotion.Restarted"/>): the loopstart key comes again then (its m_fChargeTime = 0 lets the
    /// hold check run for the next round), with the earlier combo keys and the current attack's point if it lies there.
    /// FUN_1417f1a90 (move0 / move2) runs in the body's seek (Player.SeekSkillSequence).
    /// STILL-OPEN: m_nStartTick (the combo ticks) is not ported.
    /// </summary>
    void SeekOwnLoop(Player me)
    {
        double t = ownLoop.LoopStart + ownLoop.ChargeTick * 0.001;
        me.SeekSkillSequence(t, true);
        ownKeyPos = Ms2.Net.Replay.SkillMotion.KeysFromStart;
        ownPointTime = OwnPointTime(me);
    }

    /// <summary>
    /// Each frame while the own skill state runs: StateSkill::SendChargeState @0x1417ec2f0 (stateskill.cpp:1271, from vfunc_8
    /// :724), the mode 2 sync once the state starts and whenever the owner moves (<see cref="SkillCast.SyncIfChanged"/>).
    /// Before it the continue skill's hold check (vfunc_8 :649-694, <see cref="SkillLoop.Hold"/>): a key bound to the skill's
    /// origin id held (<see cref="OwnSkillKeyHeld"/>), the loop keys, the charging skill flag (m_b111 = the skill's
    /// chargingSkill, :766) and FUN_141765a70 (<see cref="CastGate.CanCast"/>, only asked while a key is held).
    /// 0 B/frame while nothing changed and no key is held.
    /// Then the charging skill's release (:698-722, <see cref="SkillLoop.Release"/>): its cast key (the record's m_nA4,
    /// <see cref="CastKeyDown"/>) up outside m_bLoop skips the sequence to "charging_attack" (at 1/frequency, the kf's time
    /// here) and returns before the sync that frame.
    /// STILL-OPEN: mouse and pad
    /// bindings are not read; the record's rotation does not turn during the state (FUN_1417edc20).
    /// </summary>
    void TickOwnSync()
    {
        if (!OwnSkillStateRuns()) return;
        if (ownLoop.HoldCheckRuns(ownCast.Skill))
        {
            // :651-660 a key of the skill down, :662-667 else the compact pick with bAny (FUN_1417674a0(1)) is the origin id
            bool held = OwnSkillKeyHeld((int)ownCast.Skill.OriginId) || CompactPick(true, true) == ownCast.Skill.OriginId, charging = ownCast.Skill.ChargingSkill;
            bool castable = held && !charging && CastGate.CanCast(OwnHoldWorld(), ownCast.Skill.Id);
            ownLoop.Hold(held, ownLoopStartKey >= 0 && ownLoopEndKey >= 0, charging, castable);
        }
        if (ownCast.Skill.ChargingSkill && !ownLoop.Loop)   // the release's own first test, before the key and length reads
        {
            // FUN_141b6fe90 without the key: the sequence's length
            float chargingAttack = ownChargingAttackKey >= 0 ? ownChargingAttackKey : (float)(player.Anim?.CurrentAnimationLength ?? 0);
            if (ownLoop.Release(true, CastKeyDown(ownCast.Key), ownCast.Skill.DisableChargingAttackSkipFrame, chargingAttack, out bool seek))
            {
                if (seek) SeekOwnRelease(chargingAttack);
                return;
            }
        }
        var live = Net.LiveSession.Instance;
        if (live == null) return;
        var g = player.GlobalPosition;
        var req = SkillCast.SyncIfChanged(ownSync, ownCast, new System.Numerics.Vector3(g.X, -g.Z, g.Y), ownLoop.Charging, ownLoop.Loop);
        if (req != null) live.SendSync(req);
    }

    /// <summary>
    /// The release's skip (vfunc_8 :708-714): the sequence restarted (SetTargetAnimation forced) and moved to the
    /// charging_attack key, so the next frame reads the keys from 0 up to there
    /// (<see cref="Ms2.Net.Replay.SkillMotion.Restarted"/>) and the attack whose point is the key fires.
    /// </summary>
    void SeekOwnRelease(float t)
    {
        player.SeekSkillSequence(t, false);
        ownKeyPos = Ms2.Net.Replay.SkillMotion.KeysFromStart;
    }

    /// <summary>IsKeyDown(m_nA4) (stateskill.cpp:701, :949): the cast's DIK key is down (<see cref="OwnKeyDown"/>); false
    /// for 0 (a clicked slot).</summary>
    bool CastKeyDown(int dik) => dik != 0 && GodotKeyOfDik(dik) is { } g && OwnKeyDown(g);

    /// <summary>vfunc_8 :651-660: a keyboard key CKeyTable::FUN_14142c6c0 lists for the skill (<see cref="KeyTable.IsKeyOfSkill"/>)
    /// is down (IsKeyDown, <see cref="OwnKeyDown"/>).</summary>
    bool OwnSkillKeyHeld(int originId)
    {
        if (Ms2.Ui.InputBlock.Active || hudModel?.Keys is not { } keys) return false;
        var slots = hudModel.ServerKeys;
        for (int dik = 1; dik < 0xd4; dik++)
            if (keys.IsKeyOfSkill(dik, originId, slots) && GodotKeyOfDik(dik) is { } g && OwnKeyDown(g)) return true;
        return false;
    }

    /// <summary>
    /// Unk_PCMove::IsKeyDown (@0x141682f80; StateSkill reads it through its Unk_SsMove view of the owner's move): the key is
    /// held, but nothing is while FUN_141683ef0 (@0x141683ef0) finds CInputKeyHandler::FUN_14116d7a0(0xf) set, a focused
    /// text field (a movie's or an engine control's) or the loading screen (<see cref="Ms2.Ui.InputBlock"/>).
    /// </summary>
    static bool OwnKeyDown(Key g) => !Ms2.Ui.InputBlock.Active && Input.IsPhysicalKeyPressed(g);

    /// <summary>The hold check's world: the quick slots' one, taken once per state, with the player's level, hp, sp, ep and
    /// meso read again each call (the cost check, FUN_14176a380).</summary>
    /// <summary>
    /// PCBase::vfunc_103 @0x1416738c0 (pcbase.cpp:2703): the last used record unless CSkillRecord::FUN_141759e40 says it is
    /// done (its motion is past the level's motions).
    /// STILL-OPEN: the record's m_bEnd and m_nAttack are not kept, and the client keeps the record after the state ends
    /// while here it goes with the state.
    /// </summary>
    CastRecord RunningOwnCast() => ownCast is { } r && r.Motion < r.Level.Motions.Count ? r : null;

    SlotWorld OwnHoldWorld()
    {
        if (ownHoldWorld == null) return ownHoldWorld = QuickSlotWorld();
        var p = hudModel.Player;
        ownHoldWorld.Level = p.Level; ownHoldWorld.Hp = p.HpCur; ownHoldWorld.Sp = p.SpCur; ownHoldWorld.Ep = p.EpCur;
        ownHoldWorld.Meso = p.Mesos;
        return ownHoldWorld;
    }

    /// <summary>
    /// StateSkill::FireAttacksAtPoint @0x1417f0f10: every attack in a row at the point fires (FUN_1417ecce0 through
    /// <see cref="SkillAttackFire.Fire"/>, the send through LiveSession.SendDamage), then the next attack's point.
    /// STILL-OPEN: CSkillRecord::FUN_141759ad0's z offset (the motion's m_fAC / m_fB8 and the record's cooldown and aim are not
    /// read) is 0.
    /// </summary>
    void FireOwnPoint(Player me)
    {
        if (ownCast.QueuedCharge) return;   // FireAttacksAtPoint :2276: a queued record fires nothing
        int n = SkillAttackFire.AttacksAtPoint(ownCast, ownAttack);
        for (int i = 0; i < n; i++) FireOwnAttack(me, ownCast, ownAttack++);
        ownPointTime = OwnPointTime(me);
    }

    /// <summary>
    /// StateSkill::FUN_1417ecce0 @0x1417ecce0 for the own record <paramref name="r"/>'s attack <paramref name="attack"/>: the
    /// area pick and the send (<see cref="SkillAttackFire.Fire"/>, LiveSession.SendDamage), a magic model per target record
    /// started at the attack's tick (:1347), then the models run once.
    /// </summary>
    void FireOwnAttack(Player me, CastRecord r, int attack)
    {
        var live = Net.LiveSession.Instance;
        var pos = FillHitObjects(me);
        nextSerial ??= () => serialIds.Next(Net.LiveSession.Instance?.SelfObjectId ?? 0);
        var req = SkillAttackFire.Fire(r, attack, pos, me.FacingRotZ, 0,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hitObjects), crtRand, nextSerial, hitQueried, hitIds,
            (slotField?.PkMode ?? 0) != 0);
        if (req != null)
        {
            ownNodePosition ??= OwnNodePosition;
            ownRayCast ??= MagicRayCast;
            ownTagMagics.Add(r.Skill, SkillAttackFire.AttackAt(r, attack), r, req.Targets, (int)Time.GetTicksMsec(),
                hudModel.Tables.MagicPaths, ownNodePosition, null, new TagIdentity(r.CastId, (int)r.Skill.Id,
                    (short)(r.Level?.Level ?? 0), r.Motion, (byte)attack), live?.SelfObjectId ?? 0, pos, ownRayCast,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hitObjects));
        }
        bool sent = req != null && (live?.SendDamage(req) ?? false);
        TickOwnTagMagics();
        GD.Print($"cast: skill {r.Skill.Id} attack {attack} targets {req?.Targets.Count ?? 0}{(sent ? " sent" : " (offline, not sent)")}"
            + (req?.Targets.Count is null or 0 ? NoTargetDetail(pos) : "")
            + (req == null ? "" : $" [sub0 serial/target/hit/links: {string.Join(", ", req.Targets.Select(t => $"{t.Serial}/{t.TargetId}/{t.Hit}/{t.Links.Count}"))}]"));
    }

    /// <summary>The tail of an attack line with no target: how many candidates the pick had and the nearest npc (id, distance), built
    /// only for that line.</summary>
    string NoTargetDetail(System.Numerics.Vector3 pos)
    {
        int n = 0, nearest = 0; float best = float.MaxValue;
        foreach (var h in hitObjects)
        {
            if (h.IsOwner) continue;
            n++;
            float d = System.Numerics.Vector3.Distance(h.Position, pos);
            if (h.Kind == HitKind.Npc && d < best) { best = d; nearest = h.Id; }
        }
        return nearest == 0 ? $", candidates {n}, no npc" : $", candidates {n}, nearest npc object {nearest} dist {best:0}";
    }

    readonly OwnTagMagics ownTagMagics = new();
    bool ownHitVolumeRead;
    // the client's s_random (an Unk_Random seeded by GetTickCountMs2() + rand(), questmasktable.cpp:381) for the hit sound pick
    static readonly Func<uint> hitSoundRand =
        new Ms2.Quest.QuestRandom(unchecked((int)System.Environment.TickCount64 + System.Random.Shared.Next(0x8000))).Next;
    readonly System.Collections.Generic.List<Ms2.Net.Packets.SkillHitRequest> ownHitSends = new();
    readonly System.Collections.Generic.List<Ms2.Net.Packets.SkillSplashRequest> ownSplashSends = new();
    readonly System.Collections.Generic.List<MagicEndEffect> ownEndEffects = new();
    readonly System.Collections.Generic.List<MagicHitSound> ownHitSounds = new();
    Func<string, System.Numerics.Vector3> ownNodePosition;
    MagicRayBlocked ownRayBlocked;
    MagicRayCast ownRayCast;
    // CMagic::FUN_141464d00's RayCastFromPoint mask (magicdata.cpp:1786): groups 0 and 9
    readonly RayQuery magicRay = RayQuery.CastRay(0x201);
    System.Collections.Generic.IReadOnlyList<Ms2.Formats.World.EffectDummy> ownDummies;
    int ownDummiesGender = -1;

    /// <summary>
    /// CMagic::FUN_14145de80 for the own cast's magics (<see cref="OwnTagMagics"/>): each due one's region fill over the live
    /// objects, its send (SkillDamage 1/1) and its area registration into the live session's tomb tag pass, posed from the
    /// player now. An arrow's end effects play in the field (CEffectService::InsertEffect, the object the own PC), and each
    /// fill's hit sounds (CMagic::PlayHitEffects) before its send. The latency lead is 0 (STILL-OPEN in
    /// <see cref="OwnTagMagics"/>); no stun state is kept, so the owner is never stunned. The owner's weapon material is the
    /// worn RH weapon's attackMaterial (<see cref="Ms2.Ui.Items.WornEquipment.WeaponMaterial"/>, kept by RefreshOwnWorn), the
    /// hit volume the client constant VolumeMyPcToNpc (CMagic +0x13a0, magicdata.cpp:1721), read once; the hit sounds are
    /// the own PC's (relation 1) heard from the player, picked with the client's s_random (<see cref="hitSoundRand"/>).
    /// </summary>
    void TickOwnTagMagics()
    {
        if (ownTagMagics.Count == 0) return;
        var live = Net.LiveSession.Instance;
        if (player == null || live == null) { ownTagMagics.Clear(); return; }
        ownTagMagics.HitEffects = live.HitEffects;
        int now = (int)Time.GetTicksMsec();
        if (!ownTagMagics.AnyDue(now, 0)) return;
        var pos = FillHitObjects(player);
        var owner = new MagicOwner(pos, player.FacingRotZ, true, live.Me.Dead, false, ownWeaponMaterial);
        if (!ownHitVolumeRead)
        {
            slotConstants ??= Ms2.Ui.FeatureElements.Constants(assets.Packs);
            ownTagMagics.HitVolume = slotConstants.Float("VolumeMyPcToNpc", 1f);
            ownHitVolumeRead = true;
        }
        ownRayBlocked ??= MagicRayBlocked;
        ownRayCast ??= MagicRayCast;
        ownHitSends.Clear();
        ownSplashSends.Clear();
        ownEndEffects.Clear();
        ownHitSounds.Clear();
        ownTagMagics.Tick(now, 0, owner, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hitObjects), ownCast,
            live.TombTags, ownHitSends, crtRand, ownRayBlocked, (slotField?.PkMode ?? 0) != 0, ownRayCast, ownSplashSends, ownEndEffects,
            ownHitSounds);
        // PlayHitEffects runs before the fill's send (magicdata.cpp:1838, :1955)
        foreach (var hs in ownHitSounds)
            FieldAudio.Instance?.PlayHitSound(hs.Attack, hs.Defence, Ms2.Audio.SoundMusicEffectState.CasterMyPc,
                new Vector3(hs.At.X, hs.At.Z, -hs.At.Y), hs.Volume, player.GlobalPosition, hitSoundRand);
        foreach (var req in ownHitSends)
        {
            GD.Print($"cast: sub1 hit attack {req.Attack} serial {req.Serial} targets {req.Targets.Count} [{string.Join(", ", req.Targets.Select(t => $"{t.Id}/{t.Hit}"))}]");
            live.SendHit(req);
        }
        foreach (var req in ownSplashSends)
        {
            GD.Print($"cast: sub2 splash attack {req.Attack} hit {req.Hit}");
            live.SendSplash(req);
        }
        foreach (var fx in ownEndEffects)
            live.PlayFieldEffect(fx.File, new Vector3(fx.At.X, fx.At.Z, -fx.At.Y), EffectBasis(fx), Ms2.Audio.SoundMusicEffectState.CasterMyPc);
        ownHitSends.Clear();
        ownSplashSends.Clear();
        ownEndEffects.Clear();
        ownHitSounds.Clear();
    }

    /// <summary>An end effect's client rotation columns as a godot basis: client (x, y, z) is godot (x, z, -y), so the godot
    /// x, y and z columns are the client x, z and -y columns mapped the same way.</summary>
    static Basis EffectBasis(in MagicEndEffect fx)
    {
        static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Z, -v.Y);
        return new Basis(G(fx.AxisX), G(fx.AxisZ), G(-fx.AxisY));
    }

    /// <summary>The player's vf36_GetNodePosition (MS2 coordinates): the effect dummy <paramref name="node"/>, else the player's
    /// position.</summary>
    System.Numerics.Vector3 OwnNodePosition(string node)
    {
        if (player == null) return System.Numerics.Vector3.Zero;
        var at = player.GlobalPosition;
        if (!string.IsNullOrEmpty(node))
        {
            if (ownDummiesGender != player.Gender)
            {
                ownDummies = Ms2.Formats.World.EffectDummyTable.Load(assets.Packs).Pc(player.Gender);
                ownDummiesGender = player.Gender;
            }
            at = Ms2Godot.World.DummyAnchor.Point(ownDummies, node, player.Visual?.GetNodeOrNull<Node3D>(ModelPath), player.RootName) ?? at;
        }
        return new System.Numerics.Vector3(at.X, -at.Z, at.Y);
    }
    static readonly NodePath ModelPath = "Model";

    /// <summary>CPhysXHelper::RayCastFromPoint(from, out, dir, length, 0x201) != 0, in MS2 coordinates, past the player's own body.</summary>
    bool MagicRayBlocked(System.Numerics.Vector3 from, System.Numerics.Vector3 dir, float length)
    {
        if (player == null) return false;
        var to = from + dir * length;
        var a = new Vector3(from.X, from.Z, -from.Y);
        var b = new Vector3(to.X, to.Z, -to.Y);
        return magicRay.Nearest(player.GetWorld3D().DirectSpaceState, a, b, player.GetRid(), 0x201) != null;
    }

    /// <summary>CPhysXHelper::RayCastFromPoint(from, out hit, dir, length, 0x201), in MS2 coordinates, past the player's own
    /// body: the arrow magic's start and flight rays (CMagic::FUN_1414609e0, FUN_14145ec50).</summary>
    bool MagicRayCast(System.Numerics.Vector3 from, System.Numerics.Vector3 dir, float length, out System.Numerics.Vector3 hit)
    {
        hit = default;
        if (player == null) return false;
        var to = from + dir * length;
        var a = new Vector3(from.X, from.Z, -from.Y);
        var b = new Vector3(to.X, to.Z, -to.Y);
        if (magicRay.Nearest(player.GetWorld3D().DirectSpaceState, a, b, player.GetRid(), 0x201) is not { } h) return false;
        hit = new System.Numerics.Vector3(h.Position.X, -h.Position.Z, h.Position.Y);
        return true;
    }

    Action unwatchOwnSkill;
    Func<uint, int, float> slotCooldownRemain;
    Func<uint, (float, bool)> slotCooldownTotal;
    Func<SkillData, int> slotRechargeRemain;

    /// <summary>
    /// The live session's own skill packets: the cooldowns look up the HUD's skill rows, and the own state's cancel (State::vfunc_7(1)
    /// from OnSkillCancel / OnSkillUseFailed) ends the own cast's sequence when the player is in the skill state (0x10) of the uid.
    /// STILL-OPEN: the player has no other state ids, so mode 6's cancel of a state other than 0x10 does nothing.
    /// </summary>
    void WatchOwnSkill()
    {
        unwatchOwnSkill?.Invoke();
        if (Net.LiveSession.Instance is not { } live) return;
        live.SkillRows = id => hudModel?.Tables.Skills.GetSkillData(id);
        _ = hudModel?.Tables.MagicPaths;   // read now, not in the frame of the first own attack (LoadMagicPath runs at the table load)
        Action<long, int> on = (uid, state) =>
        {
            if (state != 0x10 || ownCast == null || player == null || (object)player.SkillSequence != ownSequence) return;
            if (uid != 0 && uid != ownCast.CastId) return;
            GD.Print($"cast: skill {ownCast.Skill.Id} cancelled by the server");
            player.CancelSkillSequence();
            CancelOwnMagics();
            EndOwnTagEffects();
            ownCast = null;
        };
        Action<Ms2.Net.Packets.SkillSync> sync = OwnSkillSync;
        live.OwnStateCancelled += on;
        live.OwnSkillSync += sync;
        unwatchOwnSkill = () => { live.OwnStateCancelled -= on; live.OwnSkillSync -= sync; };
    }

    /// <summary>
    /// OnSkillSync @0x141718150 (scenestategame.cpp:3378) for the own player: another cast than the own running one (no record,
    /// another uid or motion, or the sequence no longer playing) starts the packet's: its motion sequence at the packet's speed
    /// and its motion effect.
    /// STILL-OPEN: that cast has no CSkillRecord here (CSkillRecord::Init of the request), so its attacks do not fire; the running
    /// cast's update (m_kRot, vfunc_22(move), StateSkill::FUN_1417ec630's charge flags) is not ported.
    /// </summary>
    void OwnSkillSync(Ms2.Net.Packets.SkillSync s)
    {
        if (player == null) return;
        bool running = ownCast != null && (object)player.SkillSequence == ownSequence;
        if (running && ownCast.CastId == s.CastUid && ownCast.Motion == s.Motion) return;
        CancelOwnMagics();
        ownCast = null;
        castMotions ??= Ms2.Formats.World.SkillTable.For(assets.Packs);
        var motion = castMotions.Motion(s.SkillId, s.Level, s.Motion);
        if (motion == null) return;
        if (!string.IsNullOrEmpty(motion.SequenceName))
        {
            // the sync's cast has no record here (STILL-OPEN below): the motion starts from the PC's facing
            float rotZ = player.FacingRotZ, rad = Mathf.DegToRad(rotZ);
            player.PlaySkillSequence(motion.SequenceName, MathF.Max(s.Speed, 0.1f),
                OwnCastMove(s.SkillId, s.Level, s.Motion, new System.Numerics.Vector3(MathF.Sin(rad), -MathF.Cos(rad), 0)), rotZ);
        }
        var g = player.GlobalPosition;
        if (!string.IsNullOrEmpty(motion.MotionEffect)) Net.LiveSession.Instance?.PlayFieldEffect(motion.MotionEffect, g, player.Visual?.Rotation.Y ?? 0);
        GD.Print($"cast: skill {s.SkillId} motion {s.Motion} started by the server");
    }

    /// <summary>Each frame: CSkillSystem::Update's cooldown part at the server tick (0 B/frame).</summary>
    void TickCooltimes()
    {
        if (Net.LiveSession.Instance is not { } live) return;
        var cool = live.Cooltimes;
        if (!ReferenceEquals(cool, cooltimesLevelWired)) { cooltimesLevelWired = cool; cool.UseJobSystem(EnsureSlotJobs); }
        if (playerInfo != null && !ReferenceEquals(cool, cooltimesWired)) WireCooltimes(cool);
        cool.Update((uint)Net.LiveSession.CastTick());
    }

    Ms2.Ui.Skills.SkillCooltimes cooltimesWired, cooltimesLevelWired;

    /// <summary>The cooldown start and end events (UIStatusEventDispatcher FUN_141b0e260 / FUN_141b0e2f0) reach the quick slots
    /// (UIMainPlayerInfo vfunc_oa0_25 / 26). Wired once per session's table (the level cooldowns, which the table's own restarts read, are wired in TickCooltimes without the window).</summary>
    void WireCooltimes(Ms2.Ui.Skills.SkillCooltimes cool)
    {
        if (cooltimesWired != null)
        {
            cooltimesWired.Started -= playerInfo.OnCooldownStarted;
            cooltimesWired.Ended -= playerInfo.OnCooldownEnded;
        }
        cooltimesWired = cool;
        cool.Started += playerInfo.OnCooldownStarted;
        cool.Ended += playerInfo.OnCooldownEnded;
    }

    /// <summary>The quick slots' cooldown reads: the live session's CSkillSystem table at the server tick, idle offline.</summary>
    void SlotCooldowns(SlotWorld w)
    {
        if (Net.LiveSession.Instance is not { } live) return;
        slotCooldownRemain ??= (id, mode) => Net.LiveSession.Instance?.Cooltimes.Remain(id, mode != 0, (uint)Net.LiveSession.CastTick()) ?? 0f;
        slotCooldownTotal ??= id =>
        {
            float total = 0; bool byGroup = false;
            Net.LiveSession.Instance?.Cooltimes.Remain(id, true, (uint)Net.LiveSession.CastTick(), out _, out total, out byGroup);
            return (total, byGroup);
        };
        slotRechargeRemain ??= s => Net.LiveSession.Instance?.Cooltimes.RechargeRemain(s) ?? 0;
        w.CooldownRemain = slotCooldownRemain;
        w.CooldownTotal = slotCooldownTotal;
        w.RechargeRemain = slotRechargeRemain;
    }
}
