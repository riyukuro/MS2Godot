using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The home save slot dialog's outside (<see cref="IHomeSaveSlotHost"/>): the cube map service's slot lists (<see cref="CubeMapInfo.Designs"/>,
/// <see cref="CubeMapInfo.Blueprints"/>, PlotMode is f330) and the RequestCube sends. STILL-OPEN: the account's GM level and the plot group's
/// admin-only flag (ugcmapdata_1.cpp:150) are not known here: no GM, no admin-only group.
/// </summary>
sealed class HomeSaveSlotHost(Net.LiveSession live, Func<int, bool> fieldIsHome, int language) : IHomeSaveSlotHost
{
    public bool HasService => live.FieldId != null;
    public bool PlotModeIs(int mode) => live.CubeMap.PlotMode == mode;
    public IReadOnlyList<UgcMapLayout> Designs => live.CubeMap.Designs;
    public IReadOnlyList<UgcMapLayout> Blueprints => live.CubeMap.Blueprints;
    public int GmLevel => 0;
    public bool InHomeField => live.FieldId is int id && fieldIsHome(id);
    public bool PlotAdminOnly => false;
    public int Language => language;
    public void SendSaveDesign(int slot, string name) => live.SendHomeDesignSave(slot, name);
    public void SendLoadDesignBlocked(int slot) => live.SendHomeDesignLoadBlocked(slot);
    public void SendLoadDesign(int slot) => live.SendHomeDesignLoad(slot);
    public void SendSaveBlueprint(int slot, string name) => live.SendHomeBlueprintSave(slot, name);
    public void SendLoadBlueprint(int slot) => live.SendHomeBlueprintLoad(slot);
}
