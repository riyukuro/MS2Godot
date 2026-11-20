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
    /// target: an npc's number goes to <see cref="DamageText.OnNpc"/>, the own player's to <see cref="DamageText.OnPlayer"/>.
    /// STILL-OPEN: vfunc_111's DPS log (UIDPSDialog::AddDamage) is not ported.
    /// </summary>
    void WatchDamageNumbers()
    {
        unwatchDamage?.Invoke();
        if (Net.LiveSession.Instance is not { } live) return;
        Action<DamageNumber, FieldNpcs.Placed> on = (n, npc) =>
        {
            if (damageText == null) return;
            if (n.TargetId == live.SelfObjectId) damageText.OnPlayer(n.Value, n.Grade);
            else if (npc != null) damageText.OnNpc(npc, n.Value, n.Grade);
        };
        live.DamageNumbered += on;
        unwatchDamage = () => live.DamageNumbered -= on;
    }
}
