namespace Ms2.Ui;

/// <summary>
/// Whether a field move's loading screen is up, so no input reaches the field, the player or the HUD under it. In the client
/// the move tears the field and MyPC down before the next field loads: OnLoginToGame (networkservice.cpp:913), OnGameToLogin
/// (:935), OnGameToGame (:953, :969) and scenestategame.cpp:1319 call WorldService's field teardown (FUN_141bbc580,
/// worldservice_1.cpp:9963); OnMoveResult (:897-905) tears nothing down. So there is no
/// player to read the move keys and no field to click until the next field is built and its loading screen has faded
/// (UILoadingProgress::StopProgress, then NotifyFadeInComplete). Ours keeps the old field and HUD until the scene changes, and
/// a GameToGame keeps them until the next server's entry came, so they read this.
/// The message box is not frozen: its keys and clicks still work under the cover.
/// STILL-OPEN: whether the client's HUD movies, which outlive the field, take clicks under the loading screen's movie.
/// </summary>
public static class FieldMoveCover
{
    /// <summary>True from the move's start until the next field's loading screen has faded.</summary>
    public static bool Up { get; private set; }

    /// <summary>The field, the player and the HUD take input.</summary>
    public static bool TakesInput => !Up;

    /// <summary>A field move started (its loading screen comes up), or a field was entered under a loading screen.</summary>
    public static void Raise() => Up = true;

    /// <summary>The loading screen faded, or the field shows without one.</summary>
    public static void Lower() => Up = false;
}
