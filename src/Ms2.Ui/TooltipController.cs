using Ms2.Avm2;

namespace Ms2.Ui;

/// <summary>
/// GFxMovieTooltip (gfxui/gfxmovietooltip.cpp; movie id 5, ./Data/Resource/gfx/CommonTooltip.gfx, gfxmanager.cpp:91), the
/// C++ side of the tooltip movie the client loads once for the run. The movie's root (CommonTooltip) sends
/// "initializeClass" from configUI; every other movie's TooltipManager sends "Tooltip" calls, which GFxMovieMain forwards
/// here (<see cref="UiHost.Tooltips"/>). The root's createTooltip makes the named tooltip class, places it by the owner
/// rectangle and gives it the data; destroyTooltip removes it.
/// </summary>
public sealed class TooltipController : UiController, ITooltipMovie
{
    public override string Movie => "CommonTooltip";
    protected override string EntityMethod => "createTooltip";
    protected override bool IsDialog => false;

    AsObject root;                                  // m_90 (ASUIComponent of the root)
    readonly List<object> open = new();             // m_a0: the ids of the tooltips shown

    /// <summary>The ids of the tooltips shown, oldest first.</summary>
    public IReadOnlyList<object> Open => open;

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>vfunc_4 @0x1422f0d00: "initializeClass"(root) keeps the root (after RegisterFormatFunction and
    /// InvokeCommonCallback, which the host runs for every movie); the movie's own "Tooltip" "destroyTooltip"(id).</summary>
    public override bool OnMovieEvent(string ev, object[] args)
    {
        if (Is(ev, "initializeClass"))
        {
            if (args.Length > 0 && args[0] is AsObject r) root = r;
            return true;
        }
        if (Is(ev, "Tooltip"))
        {
            if (args.Length > 1 && Is(Rt.ToString(args[0]), "destroyTooltip")) DestroyTooltip(args[1]);
            return true;
        }
        return false;
    }

    /// <summary>vfunc_3 @0x1422f0a90 (nothing before the root is known): "createTooltip"(id, data, rect) with both objects
    /// copied into this movie (GFxObjectAdapter), "destroyTooltip"(id).</summary>
    public void Call(string method, object[] args)
    {
        if (root == null) return;
        if (Is(method, "createTooltip"))
        {
            if (args.Length < 3) return;
            CreateTooltip(args[0], Copy(args[1]), Copy(args[2]));
        }
        else if (Is(method, "destroyTooltip"))
        {
            if (args.Length < 1) return;
            DestroyTooltip(args[0]);
        }
    }

    /// <summary>VisitMembers into a new object: the members of an object, an empty one for anything else.</summary>
    object Copy(object v) => v is IDictionary<string, object> ? Host.ToAs(v) : Host.Obj();

    int Find(object id) => open.FindIndex(o => Equals(o, id) || (IsNumber(o) && IsNumber(id) && Rt.ToNumber(o) == Rt.ToNumber(id)));
    static bool IsNumber(object o) => o is int or uint or double or float or long;

    /// <summary>CreateTooltip @0x1422f1150: the id joins the list if new, then the root's createTooltip.</summary>
    void CreateTooltip(object id, object data, object rect)
    {
        if (Find(id) < 0) open.Add(id);
        Host.Invoke(root, "createTooltip", id, data, rect);
    }

    /// <summary>DestroyTooltip @0x1422f12b0: the id leaves the list, then the root's destroyTooltip.</summary>
    void DestroyTooltip(object id)
    {
        int i = Find(id);
        if (i >= 0) open.RemoveAt(i);
        if (root != null) Host.Invoke(root, "destroyTooltip", id);
    }

    /// <summary>DestroyTooltips @0x1422f1020: every shown tooltip is destroyed.</summary>
    public bool DestroyTooltips()
    {
        if (open.Count == 0) return false;
        foreach (var id in open)
            if (root != null) Host.Invoke(root, "destroyTooltip", id);
        open.Clear();
        return true;
    }
}
