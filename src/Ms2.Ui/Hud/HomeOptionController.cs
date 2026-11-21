namespace Ms2.Ui.Hud;

/// <summary>What the home option dialog reaches outside itself (UIHomeOptionDialog, uihomeoptiondialog.cpp).</summary>
public interface IHomeOptionHost
{
    /// <summary>CubeMapInformationService is up (the service pointer is not null).</summary>
    bool HasService { get; }
    /// <summary>CubeMapInformationService::FUN_1410d5640: the options may be changed now.</summary>
    bool CanChange { get; }
    /// <summary>The home's open type (UserUGCMapInfo +0x40); null without the local player's map info.</summary>
    int? OpenType { get; }
    /// <summary>FUN_1410c3e00(row): the row's permission is on.</summary>
    bool RowOn(int row);
    /// <summary>FUN_1410c3e80(row): the row's level.</summary>
    int RowLevel(int row);
    /// <summary>MyPC::SendHouseSetting; false without the local player.</summary>
    bool SendHouseSetting(int kind, string value, bool flag);
    /// <summary>SendRequestCubeMode42 and SendRequestCubeMode43 of one row.</summary>
    void SendRow(int row, bool on, int level);
}

/// <summary>
/// The logic of UIHomeOptionDialog (uihomeoptiondialog.cpp:112-275) apart from its movie: the values setData gets, and "apply".
/// STILL-OPEN: the HomeOptionDialog movie and the "reset", "kick user" and confirm message boxes are not ported, and nothing gives it an
/// <see cref="IHomeOptionHost"/> yet (the live session does not carry the home's map info or the cube map service state).
/// </summary>
public sealed class HomeOptionController
{
    public const int Rows = 9;
    readonly IHomeOptionHost host;
    public HomeOptionController(IHomeOptionHost host) { this.host = host; }

    /// <summary>FUN_140d73bc0: a box of common string id; the message box queue by default.</summary>
    public Action<int> ShowCommon = id => MessageBoxQueue.ShowCommon(id, 0, null, "");

    /// <summary>FUN_140fa97a0 (@0x140fa97a0, :201): "opentype" when the map info is known, then the nine row values (0 off, else level + 1).
    /// Null without the service: the client returns before setData.</summary>
    public (int? OpenType, int[] Rows)? SetData()
    {
        if (!host.HasService) return null;
        var rows = new int[Rows];
        for (int i = 0; i < Rows; i++) rows[i] = host.RowOn(i) ? host.RowLevel(i) + 1 : 0;
        return (host.OpenType, rows);
    }

    /// <summary>The "apply" call (vfunc_28, :128-157): the options must be changeable (else common string 0x6e1) and the password six characters (else 0xb0a); then
    /// SetHomeOpenType and the nine rows in order. Returns whether anything was sent.</summary>
    public bool Apply(int openType, string password, int[] rowValues)
    {
        if (!host.HasService) return false;
        if (!host.CanChange) { ShowCommon(0x6e1); return false; }
        password = (password ?? "").Trim();
        if (password.Length != 6) { ShowCommon(0xb0a); return false; }
        SetHomeOpenType((byte) openType, password);
        for (int i = 0; i < Rows; i++) SetPermissionRow(i, i < rowValues.Length ? rowValues[i] : 0);
        return true;
    }

    /// <summary>SetHomeOpenType (@0x140fa99b0, :233): the password only goes with kind 1.</summary>
    public void SetHomeOpenType(byte openType, string password)
    {
        if (openType != 1) password = "";
        host.SendHouseSetting(openType, password, true);
    }

    /// <summary>SetPermissionRow (@0x140fa9a50, :244): mode 42 with value &gt; 0, mode 43 with max(0, value - 1).</summary>
    public void SetPermissionRow(int row, int value) => host.SendRow(row, value > 0, Math.Max(0, value - 1));
}
