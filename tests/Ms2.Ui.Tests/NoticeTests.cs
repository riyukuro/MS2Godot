using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>OnNotice's routing (NoticeController), the trigger server strings and GetLocalizedText.</summary>
public class NoticeTests
{
    sealed class Sinks
    {
        public readonly List<string> Calls = new();
        public NoticeController Controller()
        {
            return new NoticeController
            {
                ShowMessage = (t, type, o) => Calls.Add($"text {type} {o} {t}"),
                ChatLine = (t, k) => Calls.Add($"chat 0x{k:x} {t}"),
                MessageBox = (t, tag) => Calls.Add($"box '{tag}' {t}"),
                SystemSound = k => Calls.Add($"sound {k}"),
                ForcedQuit = () => Calls.Add("quit"),
                MaintenanceExit = () => Calls.Add("maintenance"),
            };
        }
    }

    static NoticeMessage Notice(byte kind, ushort flags, string text, ushort arg = 0, int top = 0) =>
        new(kind, flags, new StringIdText(false, 0, 0, Array.Empty<string>(), text), arg, top);

    static List<string> Route(NoticeMessage n, bool withSystemText = true)
    {
        var s = new Sinks();
        var c = s.Controller();
        if (!withSystemText) c.ShowMessage = null;
        c.OnNotice(n);
        return s.Calls;
    }

    [Test]
    public void FlagsPickThePlaces()
    {
        Assert.That(Route(Notice(4, 0, "a")), Is.EqualTo(new[] { "chat 0x14 a" }));
        Assert.That(Route(Notice(4, 2 | 1, "a")), Is.Empty);
        Assert.That(Route(Notice(4, 0x20 | 1, "bgm")), Is.EqualTo(new[] { "sound bgm" }));
        Assert.That(Route(Notice(4, 4 | 1, "a")), Is.EqualTo(new[] { "text 6 0 a", "chat 0x14 a" }));
        Assert.That(Route(Notice(4, 0x10, "a", 3000)), Is.EqualTo(new[] { "text 9 3000 a" }));
        Assert.That(Route(Notice(4, 0x400, "a")), Is.EqualTo(new[] { "text 2 0 a", "chat 0x14 a" }));
        Assert.That(Route(Notice(4, 0x100 | 1, "a")), Is.EqualTo(new[] { "chat 0x1d a" }));
        Assert.That(Route(Notice(4, 0x40, "a")), Is.EqualTo(new[] { "box '' a" }));
        Assert.That(Route(Notice(4, 0x80, "a")), Is.EqualTo(new[] { "box 'maintenanceExit' a", "maintenance" }));
        Assert.That(Route(Notice(5, 0x40, "bye")), Is.EqualTo(new[] { "quit", "box '' bye" }));
        Assert.That(Route(Notice(5, 0x41, "bye")), Is.Empty);
        Assert.That(Route(Notice(3, 0, "a")), Is.Empty);
    }

    [Test]
    public void TopLayerWithoutItsWindowShowsNothing() =>
        Assert.That(Route(Notice(4, 0x200, "a", 0, 5000)), Is.Empty);

    /// <summary>Flags 0x200: UITopLayerNotify::ShowMessage(text, value), then the chat line 0x1d.</summary>
    [Test]
    public void TopLayerNoticeReachesItsWindow()
    {
        var s = new Sinks();
        var c = s.Controller();
        var top = new TopLayerNotifyController();
        c.TopLayerNotify = (t, v) => { s.Calls.Add($"top {v} {t}"); top.ShowMessage(t, v); };
        int loads = 0;
        top.LoadMovie = () => loads++;
        c.OnNotice(Notice(4, 0x200, "a", 0, 5000));
        Assert.That(s.Calls, Is.EqualTo(new[] { "top 5000 a", "chat 0x1d a" }));
        Assert.That((loads, top.Pending), Is.EqualTo((1, ("a", 5000))));      // kept for OnLoadComplete
        top.Hide();
        Assert.That(top.Pending, Is.EqualTo(("", 0)));
    }

    /// <summary>
    /// The whole list of OnNotice's modes: every single flag bit and 0, for every kind; and the flag sets the servers' notice
    /// helpers write (NoticePacket.Flags: Message 1, Alert 4, Mint 0x10, MessageBox 0x40, Disconnect 0x80, LargeAlert 0x200,
    /// Banner 0x400; the /notice default Message|Alert).
    /// </summary>
    [Test]
    public void EveryNoticeModeHasItsPlace()
    {
        var kind4 = new Dictionary<int, string[]>
        {
            [0] = new[] { "chat 0x14 a" },
            [1] = new[] { "chat 0x14 a" },
            [4] = new[] { "text 6 0 a" },
            [0x10] = new[] { "text 9 7 a" },
            [0x20] = new[] { "sound a" },
            [0x40] = new[] { "box '' a" },
            [0x80] = new[] { "box 'maintenanceExit' a", "maintenance" },
            [0x200] = new[] { "top 9 a", "chat 0x1d a" },
            [0x400] = new[] { "text 2 0 a", "chat 0x14 a" },
        };
        var bad = new List<string>();
        for (int kind = 0; kind < 8; kind++)
            foreach (int flags in Enumerable.Range(0, 16).Select(b => 1 << b).Prepend(0))
            {
                string[] want = kind == 4 ? kind4.GetValueOrDefault(flags, Array.Empty<string>())
                    : kind == 5 && flags == 0x40 ? new[] { "quit", "box '' a" } : Array.Empty<string>();
                var got = RouteWithTop(Notice((byte)kind, (ushort)flags, "a", 7, 9));
                if (!got.SequenceEqual(want)) bad.Add($"kind {kind} flags 0x{flags:x}: [{string.Join(", ", got)}]");
            }
        foreach (var (flags, want) in new (int, string[])[]
                 {
                     (1 | 4, new[] { "text 6 0 a", "chat 0x14 a" }),
                     (1 | 0x10, new[] { "text 9 7 a", "chat 0x14 a" }),
                     (1 | 0x200, new[] { "top 9 a", "chat 0x1d a", "chat 0x14 a" }),
                     (4 | 0x200, new[] { "text 6 0 a" }),
                 })
        {
            var got = RouteWithTop(Notice(4, (ushort)flags, "a", 7, 9));
            if (!got.SequenceEqual(want)) bad.Add($"server flags 0x{flags:x}: [{string.Join(", ", got)}]");
        }
        Assert.That(bad, Is.Empty);
    }

    static List<string> RouteWithTop(NoticeMessage n)
    {
        var s = new Sinks();
        var c = s.Controller();
        c.TopLayerNotify = (t, v) => s.Calls.Add($"top {v} {t}");
        c.OnNotice(n);
        return s.Calls;
    }

    [Test]
    public void SystemNoticeNeedsTheSystemText() =>
        Assert.That(Route(Notice(4, 4 | 0x400, "a"), withSystemText: false), Is.Empty);

    [Test]
    public void TriggerKeyMissing() =>
        Assert.That(Route(Notice(4, 0, "$nope$")), Is.EqualTo(new[] { "chat 0x14 NO KEY : $nope$" }));

    [Test]
    public void TextKinds()
    {
        var c = new NoticeController();
        Assert.That(c.Text(new StringIdText(false, 0, 0, Array.Empty<string>(), "x")), Is.EqualTo("x"));
        Assert.That(c.Text(new StringIdText(false, 3, 0, Array.Empty<string>(), "x")), Is.EqualTo(""));
        Assert.That(c.Text(new StringIdText(true, 2, 5, new[] { "a" }, "")), Is.EqualTo(""));
    }

    [Test]
    public void LocalizedText()
    {
        const string xml = "<ms2><text KR=\"k\" EN=\"\" DE=\"d\"/></ms2>";
        Assert.That(ServerStringGroup.LocalizedText(xml, "KR", 0), Is.EqualTo("k"));
        Assert.That(ServerStringGroup.LocalizedText(xml, "EN", 2), Is.EqualTo("d"));     // EN empty: the region's EN, DE, PT
        Assert.That(ServerStringGroup.LocalizedText(xml, "JP", 1), Is.EqualTo(xml));     // no JP text: unchanged
        Assert.That(ServerStringGroup.LocalizedText("plain", "KR", 0), Is.EqualTo("plain"));
        Assert.That(ServerStringGroup.LocalizedText("<ms2><bad", "KR", 0), Is.EqualTo("<ms2><bad"));
    }

    [Test]
    public void TimePrefixNeedsFlag8()
    {
        var c = new NoticeController();
        Assert.That(c.TimePrefix(1, "a"), Is.EqualTo("a"));
    }

    [Test]
    public void TriggerStringsFromData()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(new PackSet(d), "kr");
        var g = tables.TriggerStrings;
        Assert.That(g.Count, Is.GreaterThan(1000));
        string s = g.GetString("$02000313_BF__BOSSSPAWN__43$");
        Assert.That(s, Does.Not.StartWith("NO KEY"));
        Assert.That(s, Does.Not.Contain("$map"));                      // ReplaceObjectWords at load
        Assert.That(tables.Common(0xa9), Is.Not.Empty);
        var c = new NoticeController { Tables = tables, ServerTime = () => 0 };
        Assert.That(c.TimePrefix(8, "x"), Does.Contain("x").And.Not.EqualTo("x"));
    }

    /// <summary>
    /// The common string ids the servers put into their notices (InterfaceText with a StringCode: StringIdText kind 1, read by
    /// StringIdInterface::FUN_141024e60 as the common string of the id): every StringCode both servers' code names, by its
    /// server enum id and key. The enum is the GMS2 client's table (Gms2CommonStringIds), so a GMS2 server sends these ids
    /// as they are; the KMS fork sends the KMS client's id of the same key. Each must come out as its key's text in its
    /// region's data, or the notice shows an empty or a wrong line.
    /// </summary>
    static readonly (int Id, string Key)[] ServerStringCodes =
    {
        (41, "s_change_charname_err_bad_words"), (42, "s_change_charname_err_already_taken"), (44, "s_change_charname_err_system"), (45, "s_err_gender"),
        (50, "s_err_lack_super_coupon"), (51, "s_err_lack_money"), (52, "s_err_lack_meso"), (53, "s_err_lack_merat"), (55, "s_err_lack_merat_red"), (67,
        "s_err_inventory"), (74, "s_err_invalid_item"), (76, "s_err_cannot_find_user"), (77, "s_err_input_whisper_target"), (78, "s_err_cannot_find_club"),
        (134, "s_err_lack_itemcount"), (142, "s_err_cash_taxi_cannot_departure"), (143, "s_err_cash_taxi_cannot_destination"), (160,
        "s_err_timeevent_move_field"), (161, "s_err_timeevent_battle"), (162, "s_err_timeevent_samefield"), (204, "s_item_err_code"), (205,
        "s_item_err_puton"), (210, "s_item_err_Invalid_slot"), (213, "s_item_err_twohand"), (214, "s_item_err_drop"), (215, "s_item_err_cannot_drop"), (223,
        "s_item_err_puton_low_level"), (224, "s_item_err_puton_expired"), (225, "s_item_err_puton_pcbang"), (227, "s_item_invalid_function_item"), (229,
        "s_item_invalid_do_not_have"), (231, "s_item_err_puton_job"), (234, "s_item_err_invalid_count"), (241, "s_item_err_store_full"), (252,
        "s_item_err_puton_invalid_binding"), (268, "s_item_opt_sa_improve_acquire_potion"), (629, "s_inventory_err_expand_max"), (646,
        "s_beauty_goto_map_invalid_dead"), (647, "s_beauty_goto_map_invalid_battle"), (648, "s_beauty_goto_map_invalid_samefield"), (774,
        "s_change_gender_err_equip_items"), (775, "s_change_gender_result_success"), (776, "s_change_gender_result_failed"), (895,
        "s_club_err_create_no_party"), (966, "s_guild_notify_change_mark"), (990, "s_guild_search_join_reject_mail_sender"), (991,
        "s_guild_search_join_reject_mail_title"), (992, "s_guild_search_join_reject_mail_content"), (1091, "s_whisper_err_myself"), (1092,
        "s_whisper_err_target"), (1094, "s_admin_block_velma_notice_chat"), (1109, "s_admin_block_chat"), (1554, "s_title_scroll_duplicate_err"), (1646,
        "s_action_cant_ride"), (1791, "s_ugcmap_already_taken_interior_grade_gift"), (1796, "s_ugcmap_not_use_blueprint_item"), (2190,
        "s_home_returnable_invalid_state"), (2191, "s_home_returnable_forbidden"), (2192, "s_home_returnable_forbidden_to_sameplace"), (2214,
        "s_home_invite_self"), (2221, "s_fail_enterfield_invaliduser"), (2232, "s_hunting_kill_boss"), (2266, "s_ban_check_err_any"), (2280,
        "s_chat_restrict_samechat"), (2281, "s_chat_restrict_fastchat"), (2282, "s_chat_restrict_addtime"), (2283, "s_chat_restrict_accumwarning"), (2284,
        "s_chat_unknown_command"), (2339, "s_blackmarket_mail_to_sender"), (2340, "s_blackmarket_mail_to_cancel_direct"), (2341,
        "s_blackmarket_mail_to_cancel_expired"), (2343, "s_blackmarket_mail_to_seller_title"), (2346, "s_blackmarket_mail_to_seller_content"), (2347,
        "s_blackmarket_mail_to_seller_content_soldout"), (2348, "s_blackmarket_mail_to_vipseller_content"), (2349,
        "s_blackmarket_mail_to_vipseller_content_soldout"), (2354, "s_blackmarket_mail_to_buyer_title"), (2355, "s_blackmarket_mail_to_buyer_content"),
        (2356, "s_blackmarket_mail_to_cancel_title"), (2359, "s_blackmarket_mail_to_cancel_content"), (2360, "s_blackmarket_mail_to_fail_add_title"), (2361,
        "s_blackmarket_mail_to_fail_add_content"), (2443, "s_shadowworld_player_kill_slayer"), (2444, "s_shadowworld_player_kill_ruler"), (2450,
        "s_shadowworld_kill_log_die"), (2451, "s_shadowworld_kill_log_kill"), (2493, "s_field_limit_admin"), (2653, "s_meratmarket_gift_mail_sender"), (2654,
        "s_meratmarket_gift_mail_title"), (2655, "s_meratmarket_gift_mail_content"), (2662, "s_mesoMarket_mail_to_sender"), (2665,
        "s_mesoMarket_mail_to_seller_title"), (2666, "s_mesoMarket_mail_to_seller_content"), (2667, "s_mesoMarket_mail_to_buyer_title"), (2668,
        "s_mesoMarket_mail_to_buyer_content"), (2669, "s_mentoring_new_mentor_mail_sender"), (2670, "s_mentoring_new_mentor_mail_title"), (2671,
        "s_mentoring_new_mentor_mail_content"), (2755, "s_gem_error_inventory_full"), (2786, "s_worldchat_use_coupon"), (2787, "s_channelchat_use_coupon"),
        (2789, "s_revival_use_coupon"), (2853, "s_home_password_mismatch"), (2982, "s_err_ugcmap_package_cant_use"), (2984,
        "s_err_ugcmap_package_should_use_in_indoor"), (2985, "s_err_ugcmap_package_not_a_valid_package_item"), (2986,
        "s_err_ugcmap_package_cant_use_in_others_home"), (2987, "s_err_ugcmap_package_clear_indoor_first"), (2988,
        "s_err_ugcmap_package_not_a_valid_indoor"), (2990, "s_err_ugcmap_package_not_a_indoor_for_this_package_item"), (2991,
        "s_err_ugcmap_package_failed_to_consume_package_item"), (2994, "s_ugcmap_package_automatic_creation_completed"), (2995,
        "s_ugcmap_package_automatic_removal_completed"), (3105, "s_err_wedding_chat_cannot_use_no_reservation"), (3106,
        "s_err_wedding_chat_cannot_use_complete"), (3111, "s_pet_summon_on"), (3121, "s_pet_inventory_not_sendin"), (3122,
        "s_pet_inventory_not_sendin_petitem"), (3207, "s_ugcmap_fun_host_gravity_change"), (3257, "s_ugcmap_fun_roll"), (3267,
        "s_ugcmap_fun_pvp_ffa_finished"), (3268, "s_ugcmap_fun_pvp_ffa_finished_draw"), (3269, "s_ugcmap_fun_pvp_ffa_winnter_name"), (3303,
        "s_vip_coupon_extend_msg"), (3304, "s_vip_coupon_new_msg"), (3311, "s_timeevent_boss_lifetimetext1"), (3312, "s_timeevent_boss_lifetimetext2"),
        (3316, "s_ugcox_create_portal"), (3317, "s_ugcox_host_commission_meso_get"), (3318, "s_ugcox_entry_prize_meso_get"), (3319,
        "s_ugcox_entry_fee_refund_meso_get"), (3320, "s_ugcox_enter_fail_full"), (3321, "s_couple_effect_error_openbox_unknown"), (3322,
        "s_couple_effect_error_openbox_charname"), (3323, "s_couple_effect_error_openbox_myself_char"), (3324,
        "s_couple_effect_error_openbox_myself_account"), (3325, "s_couple_effect_mail_sender"), (3326, "s_couple_effect_mail_title_receiver"), (3327,
        "s_couple_effect_mail_content_receiver"), (3328, "s_couple_effect_mail_send_partner"), (3393, "s_closet_msg_success"), (3394,
        "s_closet_msg_item_not_exist"), (3429, "s_wedding_mail_title_receiver"), (3430, "s_wedding_mail_contents_receiver"), (3431,
        "s_wedding_mail_change_title_receiver"), (3432, "s_wedding_mail_change_contents_receiver"), (3433, "s_wedding_mail_cancel_title_receiver"), (3434,
        "s_wedding_mail_cancel_contents_receiver")
    };

    static int KmsId(string key)
    {
        for (int id = 0; id < 5000; id++) if (Ms2.Formats.Text.CommonStringIds.Key(id) == key) return id;
        return -1;
    }

    [Test]
    public void TheServerEnumIsTheGms2ClientTable()
    {
        var bad = ServerStringCodes.Where(c => Ms2.Formats.Text.Gms2CommonStringIds.Key(c.Id) != c.Key)
            .Select(c => $"{c.Id}: GMS2 key {Ms2.Formats.Text.Gms2CommonStringIds.Key(c.Id) ?? "none"}, not {c.Key}").ToList();
        Assert.That(bad, Is.Empty);
        // the tables part at 74: KMS registers s_err_shop_expired there, GMS2 goes on with s_err_invalid_item
        Assert.That(Ms2.Formats.Text.CommonStringIds.Key(74), Is.EqualTo("s_err_shop_expired"));
        Assert.That(Ms2.Formats.Text.Gms2CommonStringIds.Key(74), Is.EqualTo("s_err_invalid_item"));
        Assert.That(Ms2.Formats.Text.Gms2CommonStringIds.Key(633), Is.Null);
    }

    [Test]
    public void AGms2ServerIdIsReadInTheGms2Table()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(d);
        var tables = new GameTables(packs, "en");
        var t = new StringIdText(true, 1, 74, Array.Empty<string>(), "");     // s_err_invalid_item from a GMS2 server
        var gms2 = new NoticeController { Tables = tables, ServerStringKey = Ms2.Formats.Text.Gms2CommonStringIds.Key };
        Assert.That(tables.Common("s_err_invalid_item"), Is.Not.Empty);
        Assert.That(gms2.Text(t), Is.EqualTo(tables.Common("s_err_invalid_item")));
        Assert.That(new NoticeController { Tables = tables }.Text(t), Is.Not.EqualTo(gms2.Text(t)));
    }

    [Test]
    public void EveryStringCodeTheServersSendHasItsCommonText()
    {
        var bad = new List<string>();
        var labels = new List<string>();
        int sets = 0;
        foreach (var (label, dir, gms2) in new[] { ("KMS", Environment.GetEnvironmentVariable("MS2_DATA"), false),
                                                   ("GMS2", Environment.GetEnvironmentVariable("MS2_GMS2_DATA"), true) })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            sets++;
            labels.Add(label);
            using var packs = new PackSet(dir);
            string lang = packs.Exists("Xml", "string/kr/stringcommon.xml") ? "kr" : "en";
            var c = new NoticeController { Tables = new GameTables(packs, lang) };
            if (gms2) c.ServerStringKey = Ms2.Formats.Text.Gms2CommonStringIds.Key;
            foreach (var (id, key) in ServerStringCodes)
            {
                // the fork: StringCodeMap.ToClient(code), the KMS id of the same key (0, s_empty_string, when KMS has none)
                int sent = gms2 ? id : KmsId(key);
                if (sent < 0) { bad.Add($"{label}: {key} has no KMS id"); continue; }
                string text = c.Text(new StringIdText(true, 1, sent, Array.Empty<string>(), ""));
                if (text.Length == 0) bad.Add($"{label}: {id} {key} has no text");
                else if (text != c.Tables.ReservedWords.ReplaceObjectWords(Ms2.Formats.Text.ArgumentFormat.Format(c.Tables.Common(key), Array.Empty<string>()))) bad.Add($"{label}: {id} {key} shows another key's text");
            }
        }
        if (sets == 0) Assert.Ignore("MS2_DATA is not set");
        TestContext.WriteLine($"{ServerStringCodes.Length} ids, {sets} data sets");
        foreach (var b in bad) TestContext.WriteLine("gap " + b);
        Assert.That(bad, Is.EquivalentTo(DataGaps.Where(g => labels.Contains(g[..g.IndexOf(':')]))));
    }

    /// <summary>
    /// The keys a server sends that its region's client cannot show (logged in server-gaps.md): the KMS client registers no
    /// id for the first (the fork sends 0, s_empty_string); the GMS2 data has no text for the rest, so a GMS2 client shows an
    /// empty line for them too.
    /// </summary>
    static readonly string[] DataGaps =
    {
        "KMS: s_item_opt_sa_improve_acquire_potion has no KMS id",
        "GMS2: 2669 s_mentoring_new_mentor_mail_sender has no text", "GMS2: 2670 s_mentoring_new_mentor_mail_title has no text",
        "GMS2: 2671 s_mentoring_new_mentor_mail_content has no text", "GMS2: 3105 s_err_wedding_chat_cannot_use_no_reservation has no text",
        "GMS2: 3106 s_err_wedding_chat_cannot_use_complete has no text", "GMS2: 3429 s_wedding_mail_title_receiver has no text",
        "GMS2: 3430 s_wedding_mail_contents_receiver has no text", "GMS2: 3431 s_wedding_mail_change_title_receiver has no text",
        "GMS2: 3432 s_wedding_mail_change_contents_receiver has no text", "GMS2: 3433 s_wedding_mail_cancel_title_receiver has no text",
        "GMS2: 3434 s_wedding_mail_cancel_contents_receiver has no text",
    };
}
