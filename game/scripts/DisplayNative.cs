using System.Runtime.InteropServices;
using Ms2.Formats.Options;

namespace Ms2Godot;

/// <summary>
/// The operating system's display modes. Windows: the client's own calls, EnumDisplaySettings for the list and
/// ChangeDisplaySettings to switch (NiDX9Renderer::SetDisplayMode / RestoreDisplayMode, NiDX9Renderer.cpp:4129-4162,
/// the desktop mode remembered at device creation, :785). Linux: the list only, read from XRandR (the primary output's
/// modes; Wine builds its EnumDisplaySettings list from the same source); the desktop's mode is never changed there (see
/// <see cref="ScreenMode"/>).
/// </summary>
public static class DisplayNative
{
    /// <summary>The display's modes in the system's order, or null when they cannot be read.</summary>
    public static List<DisplayMode> Enumerate()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return Win.Enumerate();
            if (OperatingSystem.IsLinux()) return X11.Enumerate();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Godot.GD.PushWarning("display: mode list unavailable: " + ex.Message);
        }
        return null;
    }

    /// <summary>Windows only: SetDisplayMode(w, h), 32 bpp. True when the display is at w x h afterwards.</summary>
    public static bool SetDisplayMode(int width, int height) => OperatingSystem.IsWindows() && Win.SetDisplayMode(width, height);

    /// <summary>Windows only: RestoreDisplayMode, back to the desktop's mode if it was changed.</summary>
    public static void RestoreDisplayMode()
    {
        if (OperatingSystem.IsWindows()) Win.Restore();
    }

    static class Win
    {
        const int ENUM_CURRENT_SETTINGS = -1;
        const uint DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000;
        const uint CDS_FULLSCREEN = 4;
        const int DISP_CHANGE_SUCCESSFUL = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public uint dmFields;
            public int dmPositionX, dmPositionY;
            public uint dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string device, IntPtr dm, IntPtr hwnd, uint flags, IntPtr param);

        static bool changed;           // m_bMS2DisplayModeChanged
        static int curW, curH;         // m_kMS2DisplayMode
        static int origW, origH;       // m_kMS2OriginalMode
        static bool hooked;

        static DEVMODE New() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

        public static List<DisplayMode> Enumerate()
        {
            var list = new List<DisplayMode>();
            var dm = New();
            for (int i = 0; EnumDisplaySettingsW(null, i, ref dm); i++)
                list.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmBitsPerPel));
            return list;
        }

        /// <summary>
        /// SetDisplayMode (NiDX9Renderer.cpp:4129-4148): nothing when the display is already at that size, else
        /// ChangeDisplaySettings to 32 bpp w x h. Deviation: the client passes CDS_UPDATEREGISTRY, which also makes the mode
        /// the desktop's stored one; this passes CDS_FULLSCREEN, a temporary change Windows undoes when the process ends, so
        /// a crash cannot leave the desktop at the game's resolution. RestoreDisplayMode also runs on exit and on an
        /// unhandled exception.
        /// </summary>
        public static bool SetDisplayMode(int width, int height)
        {
            if (curW == 0)
            {
                var cur = New();
                if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref cur)) { origW = curW = (int)cur.dmPelsWidth; origH = curH = (int)cur.dmPelsHeight; }
            }
            if (curW == width && curH == height) return true;
            var dm = New();
            dm.dmPelsWidth = (uint)width;
            dm.dmPelsHeight = (uint)height;
            dm.dmBitsPerPel = 32;
            dm.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT;
            Hook();
            if (ChangeDisplaySettingsExW(null, ref dm, IntPtr.Zero, CDS_FULLSCREEN, IntPtr.Zero) != DISP_CHANGE_SUCCESSFUL) return false;
            changed = true;
            curW = width;
            curH = height;
            return true;
        }

        /// <summary>RestoreDisplayMode (NiDX9Renderer.cpp:4151-4162): back to the remembered desktop mode (here: the
        /// registry mode, which the temporary change left untouched).</summary>
        public static void Restore()
        {
            if (!changed) return;
            ChangeDisplaySettingsExW(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            changed = false;
            curW = origW;
            curH = origH;
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => Restore();
        }
    }

    static class X11
    {
        [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr dpy);
        [DllImport("libX11.so.6")] static extern ulong XDefaultRootWindow(IntPtr dpy);
        [DllImport("libXrandr.so.2")] static extern IntPtr XRRGetScreenResourcesCurrent(IntPtr dpy, ulong window);
        [DllImport("libXrandr.so.2")] static extern void XRRFreeScreenResources(IntPtr res);
        [DllImport("libXrandr.so.2")] static extern ulong XRRGetOutputPrimary(IntPtr dpy, ulong window);
        [DllImport("libXrandr.so.2")] static extern IntPtr XRRGetOutputInfo(IntPtr dpy, IntPtr res, ulong output);
        [DllImport("libXrandr.so.2")] static extern void XRRFreeOutputInfo(IntPtr info);

        // XRRScreenResources (Xrandr.h): timestamp, configTimestamp, ncrtc, crtcs, noutput, outputs, nmode, modes
        const int ResNOutput = 32, ResOutputs = 40, ResNMode = 48, ResModes = 56;
        // XRRModeInfo: id, width, height, ..., 80 bytes
        const int ModeSize = 80, ModeWidth = 8, ModeHeight = 12;
        // XRROutputInfo: ..., connection at 48, nmode at 80, modes at 88
        const int OutConnection = 48, OutNMode = 80, OutModes = 88;

        /// <summary>The primary output's modes (the first connected output's when no primary is set), in the output's
        /// order. X screens of depth 24 store 32-bit pixels, so each mode counts as 32 bpp.</summary>
        public static List<DisplayMode> Enumerate()
        {
            var dpy = XOpenDisplay(IntPtr.Zero);
            if (dpy == IntPtr.Zero) return null;
            try
            {
                ulong root = XDefaultRootWindow(dpy);
                var res = XRRGetScreenResourcesCurrent(dpy, root);
                if (res == IntPtr.Zero) return null;
                try
                {
                    var outputs = new List<ulong>();
                    ulong primary = XRRGetOutputPrimary(dpy, root);
                    if (primary != 0) outputs.Add(primary);
                    int nOut = Marshal.ReadInt32(res, ResNOutput);
                    var outPtr = Marshal.ReadIntPtr(res, ResOutputs);
                    for (int i = 0; i < nOut; i++) outputs.Add((ulong)Marshal.ReadInt64(outPtr, i * 8));
                    foreach (var output in outputs)
                    {
                        var info = XRRGetOutputInfo(dpy, res, output);
                        if (info == IntPtr.Zero) continue;
                        try
                        {
                            if (Marshal.ReadInt16(info, OutConnection) != 0) continue;   // RR_Connected = 0
                            var list = ModesOf(res, info);
                            if (list.Count > 0) return list;
                        }
                        finally { XRRFreeOutputInfo(info); }
                    }
                    return null;
                }
                finally { XRRFreeScreenResources(res); }
            }
            finally { XCloseDisplay(dpy); }
        }

        static List<DisplayMode> ModesOf(IntPtr res, IntPtr info)
        {
            int nModes = Marshal.ReadInt32(res, ResNMode);
            var modes = Marshal.ReadIntPtr(res, ResModes);
            var byId = new Dictionary<ulong, (int, int)>();
            for (int i = 0; i < nModes; i++)
            {
                var m = modes + i * ModeSize;
                byId[(ulong)Marshal.ReadInt64(m)] = (Marshal.ReadInt32(m, ModeWidth), Marshal.ReadInt32(m, ModeHeight));
            }
            var list = new List<DisplayMode>();
            int n = Marshal.ReadInt32(info, OutNMode);
            var ids = Marshal.ReadIntPtr(info, OutModes);
            for (int i = 0; i < n; i++)
                if (byId.TryGetValue((ulong)Marshal.ReadInt64(ids, i * 8), out var wh)) list.Add(new DisplayMode(wh.Item1, wh.Item2, 32));
            return list;
        }
    }
}
