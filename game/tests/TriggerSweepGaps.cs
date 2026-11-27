using System.Collections.Generic;

namespace Ms2Godot.Tests;

/// <summary>The trigger actions this port drops or only partly applies, with what is missing: the trigger sweep's known list
/// (tests/TriggerSweepCheck.cs fails on a dropped action not listed here and on a listed one no longer dropped). Written by
/// the sweep's generator from its table; the reasons cite the client's handler where the client has one.</summary>
public static class TriggerSweepGaps
{
    public static readonly Dictionary<string, string> Known = new()
    {
        ["arcade_boom_boom_ocean_clear_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_boom_boom_ocean_end_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch and HideAndSeek End (RecvOp 0x12B, opcodes.h:296; OnHideAndSeek, scenestategame.cpp:5210) has no branch",
        ["arcade_boom_boom_ocean_start_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_spring_farm_clear_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_spring_farm_end_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch and HideAndSeek End (RecvOp 0x12B, opcodes.h:296; OnHideAndSeek, scenestategame.cpp:5210) has no branch",
        ["arcade_spring_farm_start_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one2_end_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch and HideAndSeek End (RecvOp 0x12B, opcodes.h:296; OnHideAndSeek, scenestategame.cpp:5210) has no branch",
        ["arcade_three_two_one2_result_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one2_result_round2"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one2_start_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one2_start_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one3_end_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch and HideAndSeek End (RecvOp 0x12B, opcodes.h:296; OnHideAndSeek, scenestategame.cpp:5210) has no branch",
        ["arcade_three_two_one3_result_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one3_result_round2"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one3_start_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one3_start_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one_end_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch and HideAndSeek End (RecvOp 0x12B, opcodes.h:296; OnHideAndSeek, scenestategame.cpp:5210) has no branch",
        ["arcade_three_two_one_result_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one_result_round2"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one_start_game"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["arcade_three_two_one_start_round"] = "PlayArcade (RecvOp 0xD8, opcodes.h:221; CSceneStateGame::OnPlayArcade, scenestategame.cpp:8208) has no branch",
        ["create_field_game"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["create_widget"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["dark_stream_clear_round"] = "DarkStream (RecvOp 0xC5, opcodes.h:204; OnDarkStream, scenestategame.cpp:8120) has no branch",
        ["dark_stream_start_game"] = "DarkStream (RecvOp 0xC5, opcodes.h:204; OnDarkStream, scenestategame.cpp:8120) has no branch",
        ["dungeon_clear"] = "DungeonMission (RecvOp 0x100, opcodes.h:258: the mission update and stage flag) has no branch; the result (RoomDungeon mode 10, the closing photo and result) is read and not applied",
        ["dungeon_enable_give_up"] = "DungeonMission (RecvOp 0x100, opcodes.h:258: the mission update and stage flag) has no branch: no give-up button",
        ["dungeon_fail"] = "DungeonMission (RecvOp 0x100, opcodes.h:258: the mission update and stage flag) has no branch; the result (RoomDungeon mode 10, the closing photo and result) is read and not applied",
        ["dungeon_mission_complete"] = "DungeonMission (RecvOp 0x100, opcodes.h:258: the mission update and stage flag) has no branch",
        ["end_mini_game"] = "DungeonReward (RecvOp 0x1B, opcodes.h:35) has no branch (probe: its writer needs live reward objects)",
        ["end_mini_game_round"] = "DungeonReward (RecvOp 0x1B, opcodes.h:35) has no branch (probe: its writer needs live reward objects)",
        ["face_emotion"] = "other pcs' faces are not ported (LiveSession.TriggerFace); the names Trigger_Talk01_A, 02_A and 03_A have no entry in the face group on either data set, so they change nothing (the client's result there is not traced)",
        ["field_game_constant"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["field_war_end"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["give_exp"] = "the Maple2 server only logs it: a server gap",
        ["give_guild_exp"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["give_reward_content"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["guild_vs_game_end_game"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["guild_vs_game_give_contribution"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["guild_vs_game_give_reward"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["guild_vs_game_log_result"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["guild_vs_game_log_won_by_default"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["guild_vs_game_result"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["guild_vs_game_score_by_user"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["lock_my_pc"] = "FieldProperty 4 (lock the own pc) is read and not applied (FieldProperties.Applies)",
        ["look_center_camera_on_off"] = "Trigger type 6 (the look-center camera) is not ported (TriggerObjects.NotPorted)",
        ["look_center_camera_radian"] = "Trigger type 6 (the look-center camera) is not ported (TriggerObjects.NotPorted)",
        ["look_center_camera_setting"] = "Trigger type 6 (the look-center camera) is not ported (TriggerObjects.NotPorted)",
        ["mini_game_give_reward"] = "PlayerHost (RecvOp 0x105, opcodes.h:262; its handler scenestategame.cpp:5086) has no branch",
        ["random_additional_effect"] = "Trigger types 0x17-0x19 (the random-target effects) are not ported (TriggerObjects.NotPorted)",
        ["room_expire"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
        ["score_board_create"] = "the Maple2 server writes the title as a one-byte string; the client reads it wide (FieldGameScoreBoardCli open, @0x140f9c3a0 uishadowexpeditionbossbar.cpp:107, calls stream slot 8, ReadUnicodeString): a server writer bug",
        ["set_breakable"] = "the moving platforms' move cycle is not ported (another lane's work); the packet reads clean",
        ["set_gravity"] = "FieldProperty 1 (gravity) is read and not applied (FieldProperties.Applies)",
        ["set_pc_rotation"] = "Trigger Ui sub 10 with its move flag (turn and walk) is not ported; the plain turn is",
        ["set_photo_studio"] = "FieldProperty 11 (photo studio) is read and not applied (FieldProperties.Applies)",
        ["set_visible_ui"] = "Trigger type 0x14 sub 1 (MessengerBrowser, GroupMessengerBrowser, HighlightGameMenu) is read and dropped",
        ["shadow_expedition_open_boss_gauge"] = "the Maple2 server writes the title as a one-byte string; the client reads it wide (FieldGameScoreBoardCli open, @0x140f9c3a0 uishadowexpeditionbossbar.cpp:107, calls stream slot 8, ReadUnicodeString): a server writer bug",
        ["start_mini_game"] = "PlayerHost (RecvOp 0x105, opcodes.h:262; its handler scenestategame.cpp:5086) has no branch",
        ["wedding_broken"] = "the Maple2 server only logs it: a server gap",
        ["wedding_mutual_agree"] = "the Wedding mode is read and not applied (WeddingManager::OnPacket, scenestategame.cpp:1955) (mode 30)",
        ["wedding_mutual_cancel"] = "the Wedding mode is read and not applied (WeddingManager::OnPacket, scenestategame.cpp:1955) (mode 31)",
        ["wedding_set_user_emotion"] = "the Wedding mode is read and not applied (WeddingManager::OnPacket, scenestategame.cpp:1955) (mode 39)",
        ["wedding_set_user_look_at"] = "Trigger Ui sub 11 (look at a point, TriggerObjects.Ui.cs:128) is read and not applied",
        ["wedding_set_user_rotation"] = "Trigger Ui sub 10 with its move flag (turn and walk) is not ported; the plain turn is",
        ["wedding_vow_complete"] = "the Wedding mode is read and not applied (WeddingManager::OnPacket, scenestategame.cpp:1955) (mode 25; UpdateMarriage is a probe)",
        ["widget_action"] = "STILL-OPEN: the server route was not traced to its end (see the route's note)",
    };

    /// <summary>Listed actions whose packets are taken but whose effect is not complete; the sweep cannot see that, so they
    /// stay listed while their packets read clean.</summary>
    public static readonly HashSet<string> Semantic = new()
    {
        "face_emotion",
        "set_breakable",
    };
}
