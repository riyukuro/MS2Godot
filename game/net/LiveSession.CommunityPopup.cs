using System;
using Godot;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>GmCommand (in 0x106): mode 1 shows a line in the community popup (CSceneStateGame::FUN_14173c0f0).</summary>
public partial class LiveSession
{
    /// <summary>GmCommand mode 1 with feature 0x88 on: the line (Name) and its address (A) for UICommunityPopup's Show.</summary>
    public event Action<GmCommand> GmPopupText;

    bool? popupTextFeature;
    /// <summary>Feature 0x88 (FUN_14173c0f0's gate), as UiModel.IsFeatureEnabled reads it.</summary>
    bool PopupTextFeature()
    {
        if (popupTextFeature is { } on) return on;
        try { on = Ms2.Ui.LobbyFeatures.IsEnabledByName(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), Ms2.Ui.FeatureTable.Names[GmCommand.PopupTextFeature - 1]); }
        catch (Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); on = false; }
        popupTextFeature = on;
        return on;
    }
}
