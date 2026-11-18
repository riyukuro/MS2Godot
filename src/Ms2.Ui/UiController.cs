using Ms2.Avm2;

namespace Ms2.Ui;

/// <summary>
/// One UI movie's game-side controller (the client's UISwfEntity / UIDialog subclass). Overrides mirror the client's
/// virtuals: <see cref="OnLoadComplete"/>, <see cref="OnCallback"/> (vfunc_28), <see cref="GetUserData"/> (vfunc_45),
/// <see cref="OnStatus"/> (UIStatusEventListener vfunc_oa0_3+N), <see cref="Layout"/> (vfunc_43).
/// </summary>
public abstract class UiController
{
    public UiHost Host { get; internal set; }
    public UiModel Model => Host.Model;
    protected PlayerState Pc => Host.Model.Player;

    /// <summary>The movie file name without extension ("UI" + GetTypeName(), lower case).</summary>
    public abstract string Movie { get; }

    /// <summary>A method only the driven object has; the host takes the first display object with it as m_30.</summary>
    protected abstract string EntityMethod { get; }

    /// <summary>UIDialog subclasses open with the movie's tween (UIDialog::OnLoadComplete invokes startOpen, uidialog.cpp:704).</summary>
    protected virtual bool IsDialog => true;

    public virtual AsObject FindEntity() => Host.Player.FindOwner(EntityMethod);

    /// <summary>UIObject vfunc_43, on load and on resize (stage units). UIDialog's (uidialog.cpp:822) re-aligns the dialog
    /// through ASGameEntity::vfunc_25 = Invoke("alignment") (asgameentity.cpp:26).</summary>
    public virtual void Layout(double stageW, double stageH)
    {
        if (Host.Player.FindOwner("alignment") is { } al) Host.Invoke(al, "alignment");
    }

    public virtual void OnLoadComplete()
    {
        if (!IsDialog) return;
        Host.SetVisible(Host.Entity, true);
        Host.Invoke("startOpen");
    }

    /// <summary>ExternalInterface callback from the movie; true = handled.</summary>
    public virtual bool OnCallback(string name, object[] args) => false;

    /// <summary>MS2DataModel.getUserData(id, name, args): the members of the returned object, or null.</summary>
    public virtual IDictionary<string, object> GetUserData(string name, object[] args) => null;

    public virtual void OnStatus(StatusEvent e) { }

    /// <summary>The value a DataBinding slot name currently holds (null = empty).</summary>
    public virtual object GetBinding(string name) => null;

    /// <summary>GameDataModel.getBagSlotBindings(bag): the slot binding names of a bag.</summary>
    public virtual IEnumerable<string> GetBagSlotBindings(string bag) => Array.Empty<string>();

    protected static string Thousands(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
