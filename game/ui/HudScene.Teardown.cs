namespace Ms2Godot.Ui;

/// <summary>
/// The session handlers this HUD adds outside its unwatch list, removed as the HUD leaves the tree (a logout to character
/// select, a field change): the live session outlives the HUD, so a handler left on it would run a freed HUD's code on the
/// next login's packets. The box dialogs close, so their answer handlers (ItemScriptBoxResulted, ItemScriptTreasureMapped,
/// ItemBoxResulted) go with them and the gacha machine's rolling sound stops with its unload.
/// </summary>
public partial class HudScene
{
    void UnwatchSessionLeftovers()
    {
        CloseGachaBoxMovie();
        CloseLulluBoxMovie();
        CloseTreasureMapMovie();
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && itemBoxAnswer != null) live.ItemBoxResulted -= itemBoxAnswer;
        itemBoxAnswer = null;
        if (live != null) live.TalkDialogOpen -= OnTalkDialogOpen;
        unwatchMastery?.Invoke();
        unwatchMastery = null;
        unwatchOwnSkill?.Invoke();
        unwatchOwnSkill = null;
        unwatchStorage?.Invoke();
        unwatchStorage = null;
        unwatchAttend?.Invoke();
        unwatchAttend = null;
        CinematicUnwire();
        if (cooltimesWired != null && playerInfo != null)
        {
            cooltimesWired.Started -= playerInfo.OnCooldownStarted;
            cooltimesWired.Ended -= playerInfo.OnCooldownEnded;
        }
        cooltimesWired = null;
    }
}
