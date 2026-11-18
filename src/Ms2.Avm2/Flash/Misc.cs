namespace Ms2.Avm2.Flash;

/// <summary>ExternalInterface: call() goes to the host bridge (the game side), addCallback() registers AS functions the host can call.</summary>
[AsClass("flash.external.ExternalInterface")]
public class AsExternalInterface : AsObject
{
    [As] public static bool available => true;
    [As] public static string objectID => "ms2";
    [As] public static bool marshallExceptions { get; set; }
    [As] public static object call(string name, params object[] args)
    {
        var p = Runtime.Current?.Player;
        return p?.External == null ? null : p.External(name, args);
    }
    [As] public static void addCallback(string name, object fn)
    {
        var p = Runtime.Current?.Player;
        if (p == null) return;
        if (fn is AsFunction f) p.Callbacks[name] = f; else p.Callbacks.Remove(name);
    }
}

[AsClass("flash.system.Capabilities")]
public class AsCapabilities : AsObject
{
    [As] public static string version => "WIN 10,2,0,0";
    [As] public static string playerType => "StandAlone";
    [As] public static string os => "Windows 7";
    [As] public static string manufacturer => "Scaleform";
    [As] public static string language => "en";
    [As] public static bool isDebugger => false;
    [As] public static bool hasIME => true;
    [As] public static double screenResolutionX => Runtime.Current?.Player?.StageW ?? 1920;
    [As] public static double screenResolutionY => Runtime.Current?.Player?.StageH ?? 1080;
    [As] public static double screenDPI => 96;
    [As] public static string cpuArchitecture => "x86";
    [As] public static bool supports64BitProcesses => true;
    [As] public static bool avHardwareDisable => true;
    [As] public static bool localFileReadDisable => false;
    [As] public static string serverString => "";
    [As] public static double pixelAspectRatio => 1;
    [As] public static string touchscreenType => "none";
}

[AsClass("flash.system.System")]
public class AsSystem : AsObject
{
    [As] public static uint totalMemory => 64 << 20;
    [As] public static double totalMemoryNumber => 64 << 20;
    [As] public static double freeMemory => 16 << 20;
    [As] public static double privateMemory => 96 << 20;
    [As] public static bool useCodePage { get; set; }
    [As] public static AsObject ime => (AsObject)Runtime.Current.GetClass("flash.system.IME");
    [As] public static void gc() { }
    [As] public static void pause() { }
    [As] public static void resume() { }
    [As] public static void exit(uint code = 0) { }
    [As] public static void setClipboard(string s) { }
    [As] public static void disposeXML(object x) { }
    [As] public static void pauseForGCIfCollectionImminent(double f = 0.75) { }
}

[AsClass("flash.system.IME", "flash.events.EventDispatcher")]
public class AsIME : AsEventDispatcher
{
    [As] public static bool enabled { get; set; }
    [As] public static string conversionMode { get; set; } = "ALPHANUMERIC_HALF";
    [As] public static bool isSupported => true;
    [As] public static void setCompositionString(string s) { }
    [As] public static void doConversion() { }
    [As] public static void compositionSelectionChanged(int a, int b) { }
    [As] public static void compositionAbandoned() { }
}

[AsClass("flash.system.IMEConversionMode")]
public class AsIMEConversionMode : AsObject
{
    [As] public const string ALPHANUMERIC_FULL = "ALPHANUMERIC_FULL", ALPHANUMERIC_HALF = "ALPHANUMERIC_HALF", CHINESE = "CHINESE", JAPANESE_HIRAGANA = "JAPANESE_HIRAGANA",
        JAPANESE_KATAKANA_FULL = "JAPANESE_KATAKANA_FULL", JAPANESE_KATAKANA_HALF = "JAPANESE_KATAKANA_HALF", KOREAN = "KOREAN", UNKNOWN = "UNKNOWN";
}

[AsClass("flash.system.Security")]
public class AsSecurity : AsObject
{
    [As] public const string LOCAL_TRUSTED = "localTrusted", LOCAL_WITH_FILE = "localWithFile", LOCAL_WITH_NETWORK = "localWithNetwork", REMOTE = "remote", APPLICATION = "application";
    [As] public static string sandboxType => "localTrusted";
    [As] public static bool exactSettings { get; set; }
    [As] public static void allowDomain(params object[] a) { }
    [As] public static void allowInsecureDomain(params object[] a) { }
    [As] public static void loadPolicyFile(string u) { }
    [As] public static void showSettings(string p = "default") { }
}

[AsClass("flash.system.SecurityDomain")]
public class AsSecurityDomain : AsObject { [As] public static AsObject currentDomain => Runtime.Current.GetClass("flash.system.SecurityDomain").Construct(); }

[AsClass("flash.system.ApplicationDomain")]
public class AsApplicationDomain : AsObject
{
    static AsApplicationDomain current;
    [AsCtor] void Ctor(object[] a) { }
    [As] public static AsApplicationDomain currentDomain
    {
        get
        {
            var rt = Runtime.Current;
            if (current == null || current.Class.Runtime != rt) current = (AsApplicationDomain)rt.GetClass("flash.system.ApplicationDomain").Construct();
            return current;
        }
    }
    [As] public AsApplicationDomain parentDomain => null;
    [As] public object getDefinition(string name)
    {
        var v = Class.Runtime.GetGlobal(name.Replace("::", "."));
        if (Rt.IsUndef(v)) { Class.Runtime.NoteMissing("definition " + name); throw Class.Runtime.ReferenceError($"Error #1065: Variable {name} is not defined."); }
        return v;
    }
    [As] public bool hasDefinition(string name) => !Rt.IsUndef(Class.Runtime.GetGlobal(name.Replace("::", ".")));
    [As] public uint domainMemory { get; set; }
}

[AsClass("flash.system.LoaderContext")]
public class AsLoaderContext : AsObject
{
    [As] public bool checkPolicyFile { get; set; }
    [As] public AsApplicationDomain applicationDomain { get; set; }
    [As] public AsObject securityDomain { get; set; }
    [As] public bool allowCodeImport { get; set; }
    [AsCtor] void Ctor(object[] a) { checkPolicyFile = a.Length > 0 && Rt.ToBoolean(a[0]); applicationDomain = a.Length > 1 ? a[1] as AsApplicationDomain : null; }
}

[AsClass("flash.ui.Mouse")]
public class AsMouse : AsObject
{
    [As] public static void hide() { }
    [As] public static void show() { }
    [As] public static string cursor { get; set; } = "auto";
    [As] public static bool supportsCursor => true;
    [As] public static bool supportsNativeCursor => false;
    [As] public static void registerCursor(string name, object c) { }
    [As] public static void unregisterCursor(string name) { }
}

[AsClass("flash.ui.MouseCursor")]
public class AsMouseCursor : AsObject { [As] public const string ARROW = "arrow", AUTO = "auto", BUTTON = "button", HAND = "hand", IBEAM = "ibeam"; }

[AsClass("flash.ui.Keyboard")]
public class AsKeyboard : AsObject
{
    [As] public static bool capsLock => false;
    [As] public static bool numLock => false;
    [As] public static bool hasVirtualKeyboard => false;
    [As] public static bool isAccessible() => false;
    [As] public const uint BACKSPACE = 8, TAB = 9, ENTER = 13, COMMAND = 15, SHIFT = 16, CONTROL = 17, ALTERNATE = 18, CAPS_LOCK = 20, ESCAPE = 27, SPACE = 32,
        PAGE_UP = 33, PAGE_DOWN = 34, END = 35, HOME = 36, LEFT = 37, UP = 38, RIGHT = 39, DOWN = 40, INSERT = 45, DELETE = 46,
        NUMBER_0 = 48, NUMBER_1 = 49, NUMBER_2 = 50, NUMBER_3 = 51, NUMBER_4 = 52, NUMBER_5 = 53, NUMBER_6 = 54, NUMBER_7 = 55, NUMBER_8 = 56, NUMBER_9 = 57,
        A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74, K = 75, L = 76, M = 77, N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84,
        U = 85, V = 86, W = 87, X = 88, Y = 89, Z = 90,
        NUMPAD_0 = 96, NUMPAD_1 = 97, NUMPAD_2 = 98, NUMPAD_3 = 99, NUMPAD_4 = 100, NUMPAD_5 = 101, NUMPAD_6 = 102, NUMPAD_7 = 103, NUMPAD_8 = 104, NUMPAD_9 = 105,
        NUMPAD_MULTIPLY = 106, NUMPAD_ADD = 107, NUMPAD_ENTER = 108, NUMPAD_SUBTRACT = 109, NUMPAD_DECIMAL = 110, NUMPAD_DIVIDE = 111,
        F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117, F7 = 118, F8 = 119, F9 = 120, F10 = 121, F11 = 122, F12 = 123, F13 = 124, F14 = 125, F15 = 126,
        SEMICOLON = 186, EQUAL = 187, COMMA = 188, MINUS = 189, PERIOD = 190, SLASH = 191, BACKQUOTE = 192, LEFTBRACKET = 219, BACKSLASH = 220, RIGHTBRACKET = 221, QUOTE = 222;
}

[AsClass("flash.ui.KeyLocation")]
public class AsKeyLocation : AsObject { [As] public const uint STANDARD = 0, LEFT = 1, RIGHT = 2, NUM_PAD = 3, D_PAD = 4; }

[AsClass("flash.ui.Multitouch")]
public class AsMultitouch : AsObject
{
    [As] public static string inputMode { get; set; } = "none";
    [As] public static bool supportsTouchEvents => false;
    [As] public static bool supportsGestureEvents => false;
    [As] public static int maxTouchPoints => 0;
}

[AsClass("flash.ui.ContextMenu", "flash.events.EventDispatcher")]
public class AsContextMenu : AsEventDispatcher
{
    [As] public void hideBuiltInItems() { }
    [As] public AsArray customItems { get; set; }
    [As] public AsObject builtInItems { get; set; }
}

[AsClass("flash.ui.ContextMenuItem", "flash.events.EventDispatcher")]
public class AsContextMenuItem : AsEventDispatcher { [As] public string caption { get; set; } [AsCtor] void Ctor(object[] a) { caption = a.Length > 0 ? Rt.ToString(a[0]) : ""; } }

// ---- net ------------------------------------------------------------------------------------------------

[AsClass("flash.net.URLRequest")]
public class AsURLRequest : AsObject
{
    [As] public string url { get; set; }
    [As] public object data { get; set; }
    [As] public string method { get; set; } = "GET";
    [As] public string contentType { get; set; }
    [As] public AsArray requestHeaders { get; set; }
    [AsCtor] void Ctor(object[] a) { url = a.Length > 0 && !Rt.IsNullish(a[0]) ? Rt.ToString(a[0]) : null; }
}

[AsClass("flash.net.URLRequestMethod")] public class AsURLRequestMethod : AsObject { [As] public const string GET = "GET", POST = "POST"; }
[AsClass("flash.net.URLLoaderDataFormat")] public class AsURLLoaderDataFormat : AsObject { [As] public const string BINARY = "binary", TEXT = "text", VARIABLES = "variables"; }

[AsClass("flash.net.URLVariables", "Object", Dynamic = true)]
public class AsURLVariables : AsObject { [AsCtor] void Ctor(object[] a) { } [As] public void decode(string s) { } }

/// <summary>URLLoader: text loads (the movies read XML from the data packs) go through the player's resolver and complete on the next frame.</summary>
[AsClass("flash.net.URLLoader", "flash.events.EventDispatcher")]
public class AsURLLoader : AsEventDispatcher
{
    [As] public object data { get; set; }
    [As] public string dataFormat { get; set; } = "text";
    [As] public uint bytesLoaded { get; set; }
    [As] public uint bytesTotal { get; set; }
    [AsCtor] void Ctor(object[] a) { if (a.Length > 0 && a[0] is AsURLRequest r) load(r); }
    [As] public void load(AsURLRequest r)
    {
        var p = RT.Player; var url = r?.url;
        p?.Later(() =>
        {
            var text = url == null ? null : p.ReadText?.Invoke(url);
            if (text == null) { Send("flash.events.IOErrorEvent", "ioError", false, e => e.SetPublic("text", "Error #2032: Stream Error. URL: " + url)); return; }
            data = text; bytesLoaded = bytesTotal = (uint)text.Length;
            Send("flash.events.Event", "open");
            Send("flash.events.Event", "complete");
        });
    }
    [As] public void close() { }
}

[AsClass("flash.net.SharedObject", "flash.events.EventDispatcher")]
public class AsSharedObject : AsEventDispatcher
{
    static readonly Dictionary<string, AsSharedObject> all = new();
    [As] public AsObject data { get; set; }
    [As] public static AsSharedObject getLocal(string name, string path = null, bool secure = false)
    {
        var rt = Runtime.Current;
        if (!all.TryGetValue(name, out var so) || so.Class.Runtime != rt)
        {
            so = (AsSharedObject)rt.GetClass("flash.net.SharedObject").Construct();
            so.data = new AsObject(rt.ObjectClass);
            all[name] = so;
        }
        return so;
    }
    [As] public object flush(int minDiskSpace = 0) => "flushed";
    [As] public void clear() => data = new AsObject(RT.ObjectClass);
    [As] public uint size => 0;
}

// ---- media ----------------------------------------------------------------------------------------------

[AsClass("flash.media.SoundTransform")]
public class AsSoundTransform : AsObject
{
    [As] public double volume { get; set; } = 1;
    [As] public double pan { get; set; }
    [As] public double leftToLeft { get; set; } = 1;
    [As] public double rightToRight { get; set; } = 1;
    [As] public double leftToRight { get; set; }
    [As] public double rightToLeft { get; set; }
    [AsCtor] void Ctor(object[] a) { if (a.Length > 0) volume = Rt.ToNumber(a[0]); if (a.Length > 1) pan = Rt.ToNumber(a[1]); }
}

[AsClass("flash.media.SoundChannel", "flash.events.EventDispatcher")]
public class AsSoundChannel : AsEventDispatcher
{
    [As] public double position => 0;
    [As] public double leftPeak => 0;
    [As] public double rightPeak => 0;
    [As] public AsSoundTransform soundTransform { get; set; }
    [As] public void stop() { }
}

[AsClass("flash.media.Sound", "flash.events.EventDispatcher")]
public class AsSound : AsEventDispatcher
{
    [As] public double length => 0;
    [As] public AsSoundChannel play(double start = 0, int loops = 0, AsSoundTransform t = null) => (AsSoundChannel)RT.GetClass("flash.media.SoundChannel").Construct();
    [As] public void load(params object[] a) { }
    [As] public void close() { }
}

[AsClass("flash.media.SoundMixer")]
public class AsSoundMixer : AsObject
{
    [As] public static void stopAll() { }
    [As] public static AsObject soundTransform { get; set; }
    [As] public static double bufferTime { get; set; }
}

// ---- filters (kept as values; the renderer may read them) -----------------------------------------------


[AsClass("flash.filters.BitmapFilter")] public class AsBitmapFilter : AsObject { [As] public AsBitmapFilter clone() => this; }

[AsClass("flash.filters.DropShadowFilter", "flash.filters.BitmapFilter", Dynamic = true)]
public class AsDropShadowFilter : AsBitmapFilter
{
    [As] public double distance { get; set; } = 4;
    [As] public double angle { get; set; } = 45;
    [As] public uint color { get; set; }
    [As] public double alpha { get; set; } = 1;
    [As] public double blurX { get; set; } = 4;
    [As] public double blurY { get; set; } = 4;
    [As] public double strength { get; set; } = 1;
    [As] public int quality { get; set; } = 1;
    [As] public bool inner { get; set; }
    [As] public bool knockout { get; set; }
    [As] public bool hideObject { get; set; }
    [AsCtor] void Ctor(object[] a)
    {
        if (a.Length > 0) distance = Rt.ToNumber(a[0]); if (a.Length > 1) angle = Rt.ToNumber(a[1]); if (a.Length > 2) color = Rt.ToUint32(a[2]);
        if (a.Length > 3) alpha = Rt.ToNumber(a[3]); if (a.Length > 4) blurX = Rt.ToNumber(a[4]); if (a.Length > 5) blurY = Rt.ToNumber(a[5]);
        if (a.Length > 6) strength = Rt.ToNumber(a[6]); if (a.Length > 7) quality = Rt.ToInt32(a[7]);
    }
}

[AsClass("flash.filters.GlowFilter", "flash.filters.BitmapFilter", Dynamic = true)]
public class AsGlowFilter : AsBitmapFilter
{
    [As] public uint color { get; set; } = 0xff0000;
    [As] public double alpha { get; set; } = 1;
    [As] public double blurX { get; set; } = 6;
    [As] public double blurY { get; set; } = 6;
    [As] public double strength { get; set; } = 2;
    [As] public int quality { get; set; } = 1;
    [As] public bool inner { get; set; }
    [As] public bool knockout { get; set; }
    [AsCtor] void Ctor(object[] a)
    {
        if (a.Length > 0) color = Rt.ToUint32(a[0]); if (a.Length > 1) alpha = Rt.ToNumber(a[1]); if (a.Length > 2) blurX = Rt.ToNumber(a[2]);
        if (a.Length > 3) blurY = Rt.ToNumber(a[3]); if (a.Length > 4) strength = Rt.ToNumber(a[4]); if (a.Length > 5) quality = Rt.ToInt32(a[5]);
    }
}

[AsClass("flash.filters.BlurFilter", "flash.filters.BitmapFilter", Dynamic = true)]
public class AsBlurFilter : AsBitmapFilter
{
    [As] public double blurX { get; set; } = 4;
    [As] public double blurY { get; set; } = 4;
    [As] public int quality { get; set; } = 1;
    [AsCtor] void Ctor(object[] a) { if (a.Length > 0) blurX = Rt.ToNumber(a[0]); if (a.Length > 1) blurY = Rt.ToNumber(a[1]); if (a.Length > 2) quality = Rt.ToInt32(a[2]); }
}

[AsClass("flash.filters.ColorMatrixFilter", "flash.filters.BitmapFilter", Dynamic = true)]
public class AsColorMatrixFilter : AsBitmapFilter
{
    [As] public AsArray matrix { get; set; }
    [AsCtor] void Ctor(object[] a) { matrix = a.Length > 0 ? a[0] as AsArray : null; }
}

[AsClass("flash.filters.BevelFilter", "flash.filters.BitmapFilter", Dynamic = true)]
public class AsBevelFilter : AsBitmapFilter { [AsCtor] void Ctor(object[] a) { } }

[AsClass("flash.filters.GradientGlowFilter", "flash.filters.BitmapFilter", Dynamic = true)]
public class AsGradientGlowFilter : AsBitmapFilter { [AsCtor] void Ctor(object[] a) { } }

[AsClass("flash.filters.GradientBevelFilter", "flash.filters.BitmapFilter", Dynamic = true)]
public class AsGradientBevelFilter : AsBitmapFilter { [AsCtor] void Ctor(object[] a) { } }

[AsClass("flash.filters.BitmapFilterQuality")] public class AsBitmapFilterQuality : AsObject { [As] public const int HIGH = 3, LOW = 1, MEDIUM = 2; }
[AsClass("flash.filters.BitmapFilterType")] public class AsBitmapFilterType : AsObject { [As] public const string FULL = "full", INNER = "inner", OUTER = "outer"; }

/// <summary>
/// Scaleform's native extension methods. The movies declare scaleform.gfx.* as AS3 classes whose methods are native;
/// these are their bodies, keyed "pkg.Class/name" (getter and setter share a key: a setter gets one argument).
/// </summary>
public static class ScaleformNatives
{
    public static void Install(Runtime rt)
    {
        void N(string k, Func<object[], object> f) => rt.Natives[k] = new NativeMethod(k, (s, a) => f(a));
        object Undef(object[] a) => Rt.Undef;
        Player P() => rt.Player;

        // Extensions
        N("scaleform.gfx.Extensions/enabled", a => a.Length > 0 ? Rt.Undef : Rt.Box(true));
        N("scaleform.gfx.Extensions/noInvisibleAdvance", a => a.Length > 0 ? Rt.Undef : Rt.Box(false));
        N("scaleform.gfx.Extensions/isScaleform", a => Rt.Box(true));
        N("scaleform.gfx.Extensions/isGFxPlayer", a => Rt.Box(false));
        N("scaleform.gfx.Extensions/getTopMostEntity", a => P()?.TopMost(Rt.ToNumber(a[0]), Rt.ToNumber(a[1]), a.Length < 3 || Rt.ToBoolean(a[2])));
        N("scaleform.gfx.Extensions/getMouseTopMostEntity", a => P()?.TopMost(P().MouseX, P().MouseY, a.Length < 1 || Rt.ToBoolean(a[0])));
        N("scaleform.gfx.Extensions/setEdgeAAMode", Undef);
        N("scaleform.gfx.Extensions/getEdgeAAMode", a => Rt.Box(0));
        N("scaleform.gfx.Extensions/setIMEEnabled", Undef);
        N("scaleform.gfx.Extensions/visibleRect", a => AsRectangle.New(rt, 0, 0, P()?.StageW ?? 0, P()?.StageH ?? 0));
        N("scaleform.gfx.Extensions/numControllers", a => Rt.Box(1));
        N("scaleform.gfx.Extensions/CLIK_addedToStageCallback", a => a.Length > 0 ? Rt.Undef : null);
        N("scaleform.gfx.Extensions/gfxProcessSound", a => a.Length > 0 ? Rt.Undef : null);

        // FocusManager
        N("scaleform.gfx.FocusManager/alwaysEnableArrowKeys", a => a.Length > 0 ? Rt.Undef : Rt.Box(false));
        N("scaleform.gfx.FocusManager/disableFocusKeys", a => a.Length > 0 ? Rt.Undef : Rt.Box(false));
        N("scaleform.gfx.FocusManager/moveFocus", a => { P()?.MoveFocus(a.Length > 0 ? Rt.ToString(a[0]) : "down"); return P()?.Focus; });
        N("scaleform.gfx.FocusManager/findFocus", a => null);
        N("scaleform.gfx.FocusManager/setFocus", a => { P()?.SetFocus(a.Length > 0 ? a[0] as AsInteractiveObject : null, null); return Rt.Undef; });
        N("scaleform.gfx.FocusManager/getFocus", a => P()?.Focus);
        N("scaleform.gfx.FocusManager/numFocusGroups", a => Rt.Box(1));
        N("scaleform.gfx.FocusManager/setFocusGroupMask", Undef);
        N("scaleform.gfx.FocusManager/getFocusGroupMask", a => Rt.Box(1));
        N("scaleform.gfx.FocusManager/setControllerFocusGroup", a => Rt.Box(true));
        N("scaleform.gfx.FocusManager/getControllerFocusGroup", a => Rt.Box(0));
        N("scaleform.gfx.FocusManager/getControllerMaskByFocusGroup", a => Rt.Box(1));
        N("scaleform.gfx.FocusManager/getModalClip", a => P()?.ModalClip);
        N("scaleform.gfx.FocusManager/setModalClip", a => { if (P() != null) P().ModalClip = a.Length > 0 ? a[0] as AsDisplayObject : null; return Rt.Undef; });

        // TextFieldEx
        N("scaleform.gfx.TextFieldEx/appendHtml", a => { if (a[0] is AsTextField t) { t.SetHtml((t.Html ?? System.Net.WebUtility.HtmlEncode(t.Text)) + Rt.ToString(a[1])); t.Version++; } return Rt.Undef; });
        N("scaleform.gfx.TextFieldEx/setIMEEnabled", Undef);
        N("scaleform.gfx.TextFieldEx/setVerticalAlign", a => { if (a[0] is AsTextField t) t.VerticalAlign = Rt.ToString(a[1]); return Rt.Undef; });
        N("scaleform.gfx.TextFieldEx/getVerticalAlign", a => (a[0] as AsTextField)?.VerticalAlign ?? "none");
        N("scaleform.gfx.TextFieldEx/setVerticalAutoSize", Undef);
        N("scaleform.gfx.TextFieldEx/getVerticalAutoSize", a => "none");
        N("scaleform.gfx.TextFieldEx/setTextAutoSize", a => { if (a[0] is AsTextField t) t.TextAutoSize = Rt.ToString(a[1]); return Rt.Undef; });
        N("scaleform.gfx.TextFieldEx/getTextAutoSize", a => (a[0] as AsTextField)?.TextAutoSize ?? "none");
        N("scaleform.gfx.TextFieldEx/setImageSubstitutions", Undef);
        N("scaleform.gfx.TextFieldEx/updateImageSubstitution", Undef);
        N("scaleform.gfx.TextFieldEx/setNoTranslate", Undef);
        N("scaleform.gfx.TextFieldEx/getNoTranslate", a => Rt.Box(false));
        N("scaleform.gfx.TextFieldEx/setBidirectionalTextEnabled", Undef);
        N("scaleform.gfx.TextFieldEx/getBidirectionalTextEnabled", a => Rt.Box(false));
        N("scaleform.gfx.TextFieldEx/setSelectionTextColor", Undef);
        N("scaleform.gfx.TextFieldEx/getSelectionTextColor", a => Rt.Uint(0xffffff));
        N("scaleform.gfx.TextFieldEx/setSelectionBkgColor", Undef);
        N("scaleform.gfx.TextFieldEx/getSelectionBkgColor", a => Rt.Uint(0));
        N("scaleform.gfx.TextFieldEx/setInactiveSelectionTextColor", Undef);
        N("scaleform.gfx.TextFieldEx/getInactiveSelectionTextColor", a => Rt.Uint(0xffffff));
        N("scaleform.gfx.TextFieldEx/setInactiveSelectionBkgColor", Undef);
        N("scaleform.gfx.TextFieldEx/getInactiveSelectionBkgColor", a => Rt.Uint(0));

        // InteractiveObjectEx / DisplayObjectEx
        N("scaleform.gfx.InteractiveObjectEx/setHitTestDisable", a => { if (a[0] is AsDisplayObject d) d.HitTestDisabled = Rt.ToBoolean(a[1]); return Rt.Undef; });
        N("scaleform.gfx.InteractiveObjectEx/getHitTestDisable", a => Rt.Box((a[0] as AsDisplayObject)?.HitTestDisabled ?? false));
        N("scaleform.gfx.InteractiveObjectEx/setTopmostLevel", a => { if (a[0] is AsDisplayObject d) d.TopmostLevel = Rt.ToBoolean(a[1]) ? 1 : 0; return Rt.Undef; });
        N("scaleform.gfx.InteractiveObjectEx/getTopmostLevel", a => Rt.Box((a[0] as AsDisplayObject)?.TopmostLevel > 0));
        N("scaleform.gfx.DisplayObjectEx/disableBatching", Undef);
        N("scaleform.gfx.DisplayObjectEx/isBatchingDisabled", a => Rt.Box(false));
        N("scaleform.gfx.DisplayObjectEx/setRendererString", Undef);
        N("scaleform.gfx.DisplayObjectEx/getRendererString", a => null);
        N("scaleform.gfx.DisplayObjectEx/setRendererFloat", Undef);
        N("scaleform.gfx.DisplayObjectEx/getRendererFloat", a => Rt.Num(double.NaN));

        // SystemEx / MouseEx / GamePad
        N("scaleform.gfx.SystemEx/actionVerbose", a => a.Length > 0 ? Rt.Undef : Rt.Box(false));
        N("scaleform.gfx.SystemEx/setBackgroundAlpha", Undef);
        N("scaleform.gfx.SystemEx/getBackgroundAlpha", a => Rt.Num(1));
        N("scaleform.gfx.SystemEx/isDebugVersion", a => Rt.Box(false));
        N("scaleform.gfx.SystemEx/getStageAutoOrients", a => Rt.Box(false));
        N("scaleform.gfx.SystemEx/setStageAutoOrients", Undef);
        N("scaleform.gfx.SystemEx/printObjectsReport", Undef);
        N("scaleform.gfx.SystemEx/getCodeVersion", a => "4.1");
        N("scaleform.gfx.MouseEx/mouseButtons", a => Rt.Box(0));
        N("scaleform.gfx.MouseEx/getButtonsState", a => Rt.Box(0));
        N("scaleform.gfx.MouseEx/getTopMostEntity", a => P()?.TopMost(P().MouseX, P().MouseY, true));
        N("scaleform.gfx.MouseEx/setCursorType", Undef);
        N("scaleform.gfx.GamePad/supportsAnalogEvents", a => Rt.Box(false));
    }
}

// ---- video (the game plays its movies elsewhere; these keep the AS side working) --------------------------

[AsClass("flash.media.Video", "flash.display.DisplayObject")]
public class AsVideo : AsDisplayObject
{
    double w = 320, h = 240;
    [AsCtor] void Ctor(object[] a) { if (a.Length > 0) w = Rt.ToNumber(a[0]); if (a.Length > 1) h = Rt.ToNumber(a[1]); }
    [As] public void attachNetStream(object ns) { }
    [As] public void attachCamera(object c) { }
    [As] public void clear() { }
    [As] public bool smoothing { get; set; }
    [As] public int deblocking { get; set; }
    [As] public int videoWidth => 0;
    [As] public int videoHeight => 0;
    public override RectD LocalBounds() => new(0, 0, w, h);
}

[AsClass("flash.net.NetConnection", "flash.events.EventDispatcher")]
public class AsNetConnection : AsEventDispatcher
{
    [As] public void connect(params object[] a) { }
    [As] public void close() { }
    [As] public bool connected => true;
    [As] public object client { get; set; }
}

[AsClass("flash.net.NetStream", "flash.events.EventDispatcher")]
public class AsNetStream : AsEventDispatcher
{
    [AsCtor] void Ctor(object[] a) { }
    [As] public object client { get; set; }
    [As] public void play(params object[] a) { }
    [As] public void pause() { }
    [As] public void resume() { }
    [As] public void togglePause() { }
    [As] public void seek(double t) { }
    [As] public void close() { }
    [As] public double time => 0;
    [As] public double bufferTime { get; set; }
    [As] public double bufferLength => 0;
    [As] public AsSoundTransform soundTransform { get; set; }
}
