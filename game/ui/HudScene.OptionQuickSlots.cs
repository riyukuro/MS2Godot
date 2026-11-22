using System.Collections.Generic;
using Ms2.Formats.Options;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

// The quick slots the game option dialog's input pages snapshot and put back.
public partial class HudScene
{
    /// <summary>The input pages' view of the HUD's quick slots: the key table's bindings and page, QuickSlotEdit.Add (FUN_14092f690)
    /// and the main player info (0x4e5f) the client finds with FindMainPlayerInfo.</summary>
    sealed class OptionQuickSlots(HudScene hud) : IQuickSlots
    {
        public int Page => hud.hudModel.ServerKeys?.CurrentPage ?? 0;

        public void GetBoundKeys(List<int> into)
        {
            into.Clear();
            if (hud.hudModel?.Keys is { } keys && hud.hudModel.ServerKeys is { } slots) keys.GetBoundKeys(into, slots);
        }

        /// <summary>GetSkillIdByKey as MyPC's key request asks it (mypc.cpp:1701).</summary>
        public int SkillIdByKey(int key)
        {
            if (hud.hudModel?.Keys is not { } keys) return 0;
            hud.castKeyWorld = hud.QuickSlotWorld();
            hud.castKeyHeld = hud.castKeyWorld.Weapon is { } h ? hud.weaponActions.FindAction(h.Right, h.Left) : null;
            return keys.GetSkillIdByKey(key, hud.castKeyWorld.HasPc, hud.CastAttackSkill, null, hud.hudModel.ServerKeys);
        }

        public long ItemUidByKey(int key)
        {
            if (hud.hudModel?.Keys is not { } keys) return 0;
            hud.castSlotItemUid ??= i => hud.playerInfo?.SlotItemUid(i) ?? 0;
            return keys.GetQuickSlotItemUidByKey(key, hud.playerInfo != null ? hud.castSlotItemUid : null);
        }

        public int FindSlot(int skillId, long itemUid) => hud.hudModel.ServerKeys?.FindQuickSlotBySkillAndItemUid(skillId, itemUid) ?? -1;

        public void Put(int skillId, int itemId, long itemUid, int page, int slot)
        {
            if (hud.playerInfo is not { } info) return;
            QuickSlotEdit.Add(hud.QuickSlotWorld(), hud.hudModel.IsFeatureEnabled, info.Send, info.Notice, skillId, itemId, itemUid, page, slot);
        }

        public void Refresh() => hud.playerInfo?.RefreshQuickSlots();

        /// <summary>STILL-OPEN: UIMainPlayerInfo::UpdateModifiedKeys (@0x1419f1e60) sets the game pad modifier key texts
        /// (setModifiedKey, only with the expanded slot bar); the key text builder FUN_141431820 is not ported, so nothing is set.</summary>
        public void UpdateModifiedKeys() { }
    }
}
