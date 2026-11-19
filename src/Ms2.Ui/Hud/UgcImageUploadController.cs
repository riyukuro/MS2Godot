using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIUGCImageUploadDialog (ms2/client/ms2/uiugcimageuploaddialog.cpp; UI id 0x4e56), the C++ side of
/// uiugcimageuploaddialog: the image picker of an upload (here the profile picture's, CUgcManager::OpenProfileUpload).
/// <list type="bullet">
/// <item>It lists the upload folder's images (Custom\Image) as 107 x 107 thumbnails, 16 at a time (buildUGCFiles /
/// buildScrollPosition).</item>
/// <item>A click previews the image on the kind's viewer (the profile: the character window's picture), the upload
/// button asks the terms box and uploads the previewed file's thumbnail.</item>
/// <item>Add copies a picked image into the folder, Open shows the folder, a double click opens the image in an
/// editor.</item>
/// </list>
/// The ctor (@0x141b39830) turns the tween on, m_188 0.
/// </summary>
public sealed class UgcImageUploadController : UiController, IMessageBoxListener
{
    public UgcImageUploadController(UgcManager manager) { Manager = manager; }

    public UgcManager Manager { get; }

    public override string Movie => "uiugcimageuploaddialog";
    protected override string EntityMethod => "buildUGCFiles";
    public override string TypeName => "UGCImageUploadDialog";

    /// <summary>vfunc_48: the dialog's UI id.</summary>
    public int DialogId => UgcManager.UploadDialogId;

    /// <summary>m_188: the open kind (Unk_UgcOpenType), 0 when closed.</summary>
    public int OpenKind { get; private set; }

    /// <summary>m_18c: a thumbnail changed (vfunc_ugc_4), redrawn on the next tick.</summary>
    bool thumbnailChanged;

    /// <summary>UpdateBannerPrice's money check (kind 3) and the banner price (STILL-OPEN: the banner kind does not open
    /// here).</summary>
    public Func<long> Money = () => 0;

    /// <summary>
    /// OnLoadComplete (@0x141b39c70): UIDialog's, then closed with no current upload, one of another dialog or of a kind
    /// IsUploadKind refuses; else the kind kept, changeOpenType(kind), BuildFileList and (kind 3) UpdateBannerPrice.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        var ctrl = Manager.Current;
        if (ctrl == null || ctrl.Dialog != DialogId || !IsUploadKind(ctrl))
        {
            Close();
            return;
        }
        OpenKind = ctrl.Kind;
        Host.Invoke(Host.Entity, "changeOpenType", OpenKind);
        BuildFileList();
    }

    /// <summary>IsUploadKind (@0x141b3a780) for the kind ported: the profile kind with the profile viewer.</summary>
    static bool IsUploadKind(UgcUploadCtrl ctrl) => ctrl.Kind == UgcManager.ProfileKind && ctrl.Viewer is ProfileUploadViewer;

    /// <summary>vfunc_67 (@0x141b39b40): UIDialog's close, then CUgcManager::OnUploadResult(0x4e56), m_188 0.</summary>
    public override void Close()
    {
        base.Close();
        Manager.OnUploadResult(DialogId);
        OpenKind = 0;
    }

    /// <summary>vfunc_72 (@0x141b39c10), each frame: a changed thumbnail redraws (UpdateThumbnailImage).</summary>
    public void Tick()
    {
        if (!thumbnailChanged) return;
        UpdateThumbnailImage();
        thumbnailChanged = false;
    }

    /// <summary>vfunc_ugc_4 (@0x141b3a630): UIUGCEventDispatcher's thumbnail change for this dialog sets m_18c.</summary>
    public void ThumbnailChanged(int dialog)
    {
        if (dialog == DialogId) thumbnailChanged = true;
    }

    /// <summary>vfunc_28 (@0x141b3a020) after UIDialog's: the movie's calls (_stricmp).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        int Arg() => (int)Rt.ToNumber(args.ElementAtOrDefault(0));
        switch (name.ToLowerInvariant())
        {
            case "onopenfolder": Manager.OpenUploadFolder(OpenKind); return true;
            case "onpreview": if (args.Length > 0) PreviewFile(Arg()); return true;
            case "onupload": if (args.Length > 0) UploadFile(Arg()); return true;
            case "onnewtemplate": if (Manager.HasViewerContent()) BuildFileList(); return true;   // OnNewTemplate
            case "onaddimage": OnAddImage(); return true;
            case "onscrollposition":
                if (args.Length > 0)
                {
                    int i = Arg();
                    if (i == -1) return false;
                    BuildScrollPosition(i);
                }
                return true;
            case "onitemdoubleclick":
                if (args.Length > 0)
                {
                    int i = Arg();
                    if (i == -1) { Manager.Box?.Invoke(0x2e0, null, ""); return true; }
                    Manager.OpenFileInPaint(i);
                }
                return true;
        }
        return false;
    }

    /// <summary>OnAddImage: CUgcManager::AddImage("./", the image filter); an added file rebuilds the list unselected
    /// (FUN_141b3a600).</summary>
    void OnAddImage() => Manager.AddImage("./", UgcManager.ImageFilter, added => { if (added && !IsClosed) Rebuild(); });

    /// <summary>FUN_141b3a600 (@0x141b3a600): BuildFileList, SelectThumbnail(-1).</summary>
    void Rebuild()
    {
        BuildFileList();
        SelectThumbnail(-1);
    }

    /// <summary>
    /// vfunc_49 (@0x141b3a3d0): a thumbnail's tooltip ("image" with its renderer): the renderer's data.tooltip as a text
    /// tooltip, align 2, offset (-4, 1).
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (Rt.ToString(args.ElementAtOrDefault(0)) != "image") return null;
        if (args.ElementAtOrDefault(1) is not AsObject renderer) return null;
        if (Host.Get(renderer, "data") is not AsObject data) return null;
        var tip = new AsTooltip();
        if (!tip.ApplyTextTooltip(Rt.ToString(Host.Get(data, "tooltip")) ?? "")) return null;
        tip.SetTooltipAlign(2);
        tip["offsetX"] = -4;
        tip["offsetY"] = 1;
        return tip;
    }

    /// <summary>
    /// OnMessage (@0x141b39e30), a box's answer: yes on "noexist" rebuilds the list unselected and restores the viewer; yes
    /// on the terms box (FUN_140d74e80) starts the upload; then FUN_14151f200 (the next box), handled. Any other answer:
    /// the next box, not handled.
    /// </summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (button == 0)
        {
            if (tag == "noexist")
            {
                Rebuild();
                Manager.Current?.Viewer?.Restore();
            }
            else if (MessageBoxQueue.TermsAgreed(tag)) Manager.StartUpload();
            MessageBoxQueue.Next();
            return true;
        }
        MessageBoxQueue.Next();
        return false;
    }

    /// <summary>
    /// OnUploadResult (@0x141b3a690): 0 gives notice 0x467 for the profile (0x469 for a banner); another result its
    /// GetUploadResultMessage as a system notice when it is not empty; then closed.
    /// </summary>
    public void OnUploadResult(int n)
    {
        if (n == 0)
        {
            if (OpenKind == 5) Manager.CommonNotice?.Invoke(0x467);
            else if (OpenKind == 3) Manager.CommonNotice?.Invoke(0x469);
        }
        else if (UgcManager.ResultMessage(n) is { Length: > 0 } text) Manager.Notice?.Invoke(text);
        Close();
    }

    /// <summary>PreviewFile: -1 gives box 0x2e0; a file gone from the disk the "noexist" box 0x2e2; a refused preview
    /// notice 0x2e1.</summary>
    bool PreviewFile(int i)
    {
        if (i == -1) { Manager.Box?.Invoke(0x2e0, null, ""); return false; }
        if (!Manager.FileExists(i)) { Manager.Box?.Invoke(0x2e2, this, "noexist"); return false; }
        if (!Manager.Preview(OpenKind, i)) { Manager.CommonNotice?.Invoke(0x2e1); return false; }
        return true;
    }

    /// <summary>
    /// UploadFile: -1 gives box 0x2e0; a file that is not the previewed one on disk the "noexist" box 0x2e2; else the terms
    /// box of bit 3, title 0x9d5 and text 0x9cf (kind 6: bit 4 / 0x9d6; kind 8: bit 9 / 0x9d7). The banner's price check
    /// (0x34) and the blueprint kinds (9, 0xd1) are not opened here.
    /// </summary>
    void UploadFile(int i)
    {
        if (i == -1) { Manager.Box?.Invoke(0x2e0, null, ""); return; }
        if (!Manager.IsCurrentUploadFile(i)) { Manager.Box?.Invoke(0x2e2, this, "noexist"); return; }
        var (bit, title) = OpenKind switch { 6 => (4, 0x9d6), 8 => (9, 0x9d7), _ => (3, 0x9d5) };
        MessageBoxQueue.ShowTerms(this, bit, title, 0x9cf);
    }

    /// <summary>BuildFileList: RefreshLocalFiles(kind), the first 16 rows, buildUGCFiles(file count, rows).</summary>
    void BuildFileList()
    {
        Manager.RefreshLocalFiles(OpenKind);
        Host.Invoke(Host.Entity, "buildUGCFiles", Manager.FileCount, (object)Manager.Rows(16, 0));
    }

    /// <summary>BuildScrollPosition(row): the 16 rows from row x 4, buildScrollPosition(start, rows).</summary>
    void BuildScrollPosition(int row)
    {
        int start = row << 2;
        Host.Invoke(Host.Entity, "buildScrollPosition", start, (object)Manager.Rows(16, start));
    }

    /// <summary>UpdateThumbnailImage: updateThumbnailImage when the upload has a file.</summary>
    void UpdateThumbnailImage()
    {
        if (Manager.Current?.File != null) Host.Invoke(Host.Entity, "updateThumbnailImage");
    }

    /// <summary>SelectThumbnail(i): -1 is passed on; another index only for a file there, then previewed.</summary>
    void SelectThumbnail(int i)
    {
        if (i == -1) { Host.Invoke(Host.Entity, "selectThumbnail", -1); return; }
        if (Manager.GetFile(i) == null) return;
        Host.Invoke(Host.Entity, "selectThumbnail", i);
        PreviewFile(i);
    }
}
