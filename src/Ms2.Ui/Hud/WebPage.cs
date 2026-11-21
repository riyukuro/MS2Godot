namespace Ms2.Ui.Hud;

/// <summary>
/// What UIWebBrowserBase 0x4e6a (FindWebBrowser, Navigate(url, true)) is given: a page address. This client has no embedded browser, so the host
/// opens the address in the system browser. Only http and https addresses go out; an empty constant (PersonalInfoAgreementURL's default is "")
/// opens nothing, like the client navigating to nothing.
/// </summary>
public static class WebPage
{
    /// <summary>The address to open, or null when there is none to open.</summary>
    public static string Target(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u.AbsoluteUri : null;
    }
}
