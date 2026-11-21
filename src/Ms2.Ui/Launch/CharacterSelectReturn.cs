namespace Ms2.Ui.Launch;

/// <summary>
/// The flow's answer to the login server's character list after a GameToLogin (LiveSession.ReturnedToCharacterSelect): the client
/// switches the scene from the field to character select (FUN_1417e25a0(0x620000d)); any other scene is left alone.
/// </summary>
public static class CharacterSelectReturn
{
    public static bool Applies(bool onField) => onField;
}
