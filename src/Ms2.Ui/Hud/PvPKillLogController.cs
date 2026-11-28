using Ms2.Formats.Text;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIPvPKillLogDialog (UI id 0x4ea0, movie uipvpkilllogdialog, ms2/client/ms2/uipvpkilllogdialog.cpp): the kill log of a pk
/// field, fed by PlayerKillNotice mode 0 (CSceneStateGame::OnPlayerKillNotice @0x141733850, scenestategame.cpp:4344), which
/// opens it first when it is closed. The ctor (@0x141342570) sets flag 1 only: not closable, no tween. A kill that comes
/// before the movie is up waits in the attacker and target names until OnLoadComplete.
/// </summary>
public sealed class PvPKillLogController : UiController
{
    public const int UiId = 0x4ea0;

    /// <summary>s_shadowworld_kill_log_die / s_shadowworld_kill_log_kill (FormatStringId 0x972 / 0x973).</summary>
    public const int KillLogDieText = 0x972, KillLogKillText = 0x973;
    /// <summary>The ChatLog line kind of FUN_1412751b0(msg, 0x14).</summary>
    public const int ChatKind = 0x14;
    public const string KillSound = "BattleField_Kill", KillPlayerSound = "System_KillPlayer";

    public override string Movie => "uipvpkilllogdialog";
    public override string TypeName => "PvPKillLogDialog";        // GetTypeName (@0x141342670)
    protected override string EntityMethod => "alignment";

    /// <summary>The ctor's SetFlag(1, 1) (uipvpkilllogdialog.cpp:44).</summary>
    public override bool NotClosable => true;
    /// <summary>The ctor has no SetTweenEnabled.</summary>
    protected override bool TweenEnabled => false;

    /// <summary>FUN_1412751b0(msg, 0x14): a chat line of the kind.</summary>
    public System.Action<string, int> ChatLine = (_, _) => { };
    /// <summary>PlaySystemSound(name, "").</summary>
    public System.Action<string> Sound = _ => { };
    /// <summary>CReservedWordTable::ReplaceValueWords.</summary>
    public System.Func<string, string> ReplaceValueWords = t => t;
    /// <summary>The common string of an id (FormatStringId's table text).</summary>
    public System.Func<int, string> Common = id => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
    /// <summary>The local player's name (vfunc_77), or null when there is no local player.</summary>
    public System.Func<string> MyName = () => null;

    string pendingAttacker = "", pendingTarget = "";                // m_strAttacker, m_strTarget

    /// <summary>The kill waiting for the movie, for checks.</summary>
    public (string Attacker, string Target) Pending => (pendingAttacker, pendingTarget);

    /// <summary>vfunc_43 (@0x141342e10): UIDialog's, then the entity at align (5, 0x1e, 300).</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        Host.Align(Host.Entity, 5, 0x1e, 300);
    }

    /// <summary>OnLoadComplete (@0x141342710): UIDialog's, then the waiting kill when both names are set, and they clear.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (pendingAttacker.Length != 0 && pendingTarget.Length != 0)
        {
            AddKill(pendingAttacker, pendingTarget);
            pendingAttacker = "";
            pendingTarget = "";
        }
    }

    /// <summary>
    /// AddKill (@0x141342880): no movie yet, keep the pair and return; no local player, nothing. Else "addKill" with
    /// {attackerName, targetName, myName}; the own kill plays BattleField_Kill and logs 0x973 with the target, the own death
    /// logs 0x972 with the attacker (chat kind 0x14, after ReplaceValueWords); either one plays System_KillPlayer.
    /// </summary>
    public void AddKill(string attacker, string target)
    {
        attacker ??= "";
        target ??= "";
        if (Host?.Entity == null)
        {
            pendingAttacker = attacker;
            pendingTarget = target;
            return;
        }
        if (MyName() is not { } me) return;
        Host.Invoke("addKill", Host.Obj(("attackerName", attacker), ("targetName", target), ("myName", me)));
        if (attacker == me)
        {
            Sound(KillSound);
            if (target.Length != 0)
                ChatLine(ReplaceValueWords(ClientFormat.Format(Common(KillLogKillText), target)), ChatKind);
        }
        if (target == me && attacker.Length != 0)
            ChatLine(ReplaceValueWords(ClientFormat.Format(Common(KillLogDieText), attacker)), ChatKind);
        if (attacker == me || target == me) Sound(KillPlayerSound);
    }
}
