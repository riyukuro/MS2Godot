using System;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Ui;

// The live damage numbers: the skill damage records of the field through UIDamageText.
public partial class HudScene
{
    Action unwatchDamage;

    /// <summary>
    /// PCBase::vfunc_111 (@0x141674c60, pcbase.cpp:2844) finds UIDamageText (0x4e32) and calls FUN_141911e90 with the
    /// target: an npc's number goes to <see cref="DamageText.OnNpc"/>, another player's to <see cref="DamageText.OnPc"/>,
    /// the own player's to <see cref="DamageText.OnPlayer"/>; the caster is the local player when it is the own object.
    /// The gains: OnExpUp's exp (an npc's at the end of its death sequence), a picked meso or type 3 item (CDropItem::SetState(3)) to DamageText's OnExp / OnMeso / OnItem.
    /// STILL-OPEN: vfunc_111's DPS log (UIDPSDialog::AddDamage) is not ported.
    /// </summary>
    void WatchDamageNumbers()
    {
        unwatchDamage?.Invoke();
        if (Net.LiveSession.Instance is not { } live) return;
        Action<DamageNumber, FieldNpcs.Placed, DamageTarget> on = (n, npc, pc) =>
        {
            if (damageText == null) return;
            bool casterMe = n.CasterId == live.SelfObjectId;
            if (n.TargetId == live.SelfObjectId) damageText.OnPlayer(n.Value, n.Grade);
            else if (npc != null) damageText.OnNpc(npc, n.Value, n.Grade, casterMe);
            else if (pc != null) damageText.OnPc(pc, n.Value, n.Grade, casterMe);
        };
        Action<long, int> exp = (v, type) => damageText?.OnExp(v, type);
        Action<FieldNpcs.Placed, long> npcExp = (npc, v) => damageText?.OnNpcExp(npc, v);
        Action<DamageTarget, long> meso = (pc, v) => damageText?.OnMeso(pc, v);
        Action<DamageTarget, int, string> item = (pc, n, icon) => damageText?.OnItem(pc, n, icon);
        live.DamageNumbered += on;
        live.ExpGained += exp;
        live.NpcExpShown += npcExp;
        live.MesoGained += meso;
        live.ItemGained += item;
        unwatchDamage = () => { live.DamageNumbered -= on; live.ExpGained -= exp; live.NpcExpShown -= npcExp; live.MesoGained -= meso; live.ItemGained -= item; };
    }
}
