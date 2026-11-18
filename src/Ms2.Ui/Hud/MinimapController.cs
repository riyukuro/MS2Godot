using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>UIMinimapDialog (ms2/client/ms2/uiminimapentity.cpp): the minimap frame, field image and markers.</summary>
public sealed class MinimapController : UiController
{
    public override string Movie => "uiminimapdialog";
    protected override string EntityMethod => "createEntity";

    /// <summary>The field's minimap image (field minimap table m_strImageName, e.g. map/minimap/02000001.dds); null = none.</summary>
    public string ImageName;
    /// <summary>The field hides its minimap (m_nMinimapInvisible).</summary>
    public bool MinimapInvisible;
    public bool Minimize, FullMap;

    public AsObject FieldClip { get; private set; }

    /// <summary>OnLoadComplete (uiminimapentity.cpp:1104): BuildUIData, BuildField, then the entities.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BuildUIData();
        BuildField();
    }

    /// <summary>BuildUIData (uiminimapentity.cpp:3456): initData(obj).</summary>
    void BuildUIData()
    {
        bool force = MinimapInvisible || string.IsNullOrEmpty(ImageName);
        var f = Model.Field;
        string chPrefix = Model.Tables.Common("s_word_ch") ?? "";
        var channels = new List<object>();
        int selected = 0;
        foreach (var id in f.Channels)
        {
            // BuildChannelList (uiserverlistdialog.cpp): "{0} {1}" of the channel word and the id
            string label = (chPrefix.Length > 0 ? chPrefix + " " : "") + id;
            if (id == f.Channel) selected = channels.Count;
            channels.Add(new Dictionary<string, object> { ["id"] = id, ["label"] = label, ["labelDropdown"] = label });
        }
        Host.Invoke("initData", Host.Obj(
            ("fieldname", f.Name),
            ("minimize", Minimize),
            ("fullmap", FullMap),
            ("forceMinimize", force),
            ("reportVisible", false),
            ("width", 0), ("height", 0), ("scale", 1.0f), ("bgAlpha", 30), ("mapAlpha", 100),
            ("showentity", new List<object>()),           // m_showAttrs: the player's minimap icon filter (option), none set
            ("channel", channels),
            ("selectedchannel", selected),
            ("channelprefix", chPrefix),
            ("channelgroup", ""),
            ("mapAlphaPrefix", Model.Tables.Common("s_word_minimap_alpha") ?? ""),
            ("bgAlphaPrefix", Model.Tables.Common("s_word_mapbg_alpha") ?? "")));
    }

    /// <summary>BuildField (uiminimapentity.cpp:2697): createFieldEntity(0, image name).</summary>
    void BuildField()
    {
        Host.Invoke("removeField");
        if (string.IsNullOrEmpty(ImageName)) return;
        FieldClip = Host.Invoke("createFieldEntity", 0, ImageName) as AsObject;
    }

    /// <summary>createEntity(UIMinimapEntity::BuildData, uiminimapentity.cpp:375); x / y in field image pixels.</summary>
    public AsObject AddEntity(string mcname, string frame, string layer, double x, double y, string name = "", bool showText = false, bool inFieldNpc = false)
        => Host.Invoke("createEntity", Host.Obj(("mcname", mcname), ("frame", frame), ("layer", layer), ("x", x), ("y", y), ("visible", true),
            ("showtext", showText), ("showguide", false), ("name", name ?? ""), ("guideName", ""), ("inFieldNpc", inFieldNpc))) as AsObject;

    public override bool OnCallback(string name, object[] args)
    {
        switch (name)
        {
            case "OnChangeChannel": case "OnChangeMode": case "OnToggleMode": case "OnCheckIcon": case "OnUpdateCenter":
            case "OnWheelStepChange": case "onFieldLoadComplete": case "OnWorldMap": case "OnMorePlayers": case "OnReport": case "OnPopupMenu":
                return true;
        }
        return false;
    }
}
