using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>
/// A refused game start: CNetworkService::OnLoginToGame @0x14153b570 (networkservice.cpp:910-925) for a result other than 0,
/// whose box is built here, and UICharacterListDialog::vfunc_79's case 0xe (uicharacterlistdialog.cpp:591-597), which pops
/// the list up again for any such result (ShowPopup), so the character select takes input again.
/// </summary>
public static class StartRefusal
{
    /// <summary>The name must change first: the rename box (the character id follows the text, serverpacketdef.cpp:299-301).</summary>
    public const int RenameRequired = 0x31;
    /// <summary>A text titled 0x8c6 whose OK ends the client (the maintenanceExit tag).</summary>
    public const int Maintenance = 0x1a;
    /// <summary>A text titled 0x8c6.</summary>
    public const int Notice = 0x19;

    /// <summary>
    /// The box of a refused start, not shown yet; null for result 0. 0x1a and 0x19 show the packet's text with its "\n"
    /// pairs made line breaks, titled common string 0x8c6, 0x1a with the maintenanceExit tag; any other result shows
    /// GetFieldMoveErrorMessage's text (<see cref="FieldMoveError"/>).
    /// </summary>
    public static MessageBoxData Build(int result, string message, Func<string, string> commonByKey)
    {
        switch (result)
        {
            case 0: return null;
            case Maintenance:
            case Notice:
            {
                var d = MessageBoxQueue.Create(0, (message ?? "").Replace("\\n", "\n"), null, result == Maintenance ? LoginRefusalBox.MaintenanceExitTag : "");
                d.Title = MessageBoxData.Common(0x8c6);
                return d;
            }
            default:
                return MessageBoxQueue.Create(0, FieldMoveError.Text(result, commonByKey), null, "");
        }
    }
}
