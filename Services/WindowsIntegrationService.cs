using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FencesWPF.Services
{
    /// <summary>
    /// Centralises all Windows OS integration:
    ///   - DWM Acrylic (Win11) / BlurBehind (Win10) per-panel
    ///   - Reactive dark/light mode detection
    ///   - Monitor change recovery (keeps panels on screen)
    ///   - DPI-change notification
    /// </summary>
    public sealed class WindowsIntegrationService : IDisposable
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static WindowsIntegrationService? _instance;
        public static WindowsIntegrationService Instance =>
            _instance ??= new WindowsIntegrationService();
        private WindowsIntegrationService() { }

        // ── State ──────────────────────────────────────────────────────────────
        private bool _disposed;

        // Cached OS version checks (computed once)
        public static readonly bool IsWindows11 = IsWin11();
        public static readonly bool IsWindows10 = Environment.OSVersion.Version.Major >= 10;

        // ── Dark mode ──────────────────────────────────────────────────────────
        public bool IsDarkMode { get; private set; } = ReadDarkMode();

        public event Action<bool>? ThemeChanged;  // true = dark

        // ── Startup ───────────────────────────────────────────────────────────
        public void Start()
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        public void Stop()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }

        // ── DWM Glass effect ──────────────────────────────────────────────────
        /// <summary>
        /// Applies the best available background blur to a WPF window handle.
        /// Win11 → DwmSetWindowAttribute (Acrylic/Mica), Win10 → BlurBehind.
        /// </summary>
        public static void ApplyGlassEffect(IntPtr hwnd)
        {
            try
            {
                if (IsWindows11)
                    ApplyAcrylic(hwnd);
                else if (IsWindows10)
                    ApplyBlurBehind(hwnd);
            }
            catch { /* non-critical visual enhancement */ }
        }

        // ── Win11 Acrylic ──────────────────────────────────────────────────────
        private static void ApplyAcrylic(IntPtr hwnd)
        {
            // DWMWA_SYSTEMBACKDROP_TYPE (38) — requires Windows 11 22H2+
            // 3 = Acrylic, 2 = Mica, 4 = Tabbed
            int backdropType = 3; // Acrylic
            DwmSetWindowAttribute(hwnd, 38, ref backdropType, sizeof(int));

            // Also enable DWM transitions (cosmetic)
            int transitions = 1;
            DwmSetWindowAttribute(hwnd, 3, ref transitions, sizeof(int));
        }

        // ── Win10 BlurBehind ──────────────────────────────────────────────────
        private static void ApplyBlurBehind(IntPtr hwnd)
        {
            var bb = new DWM_BLURBEHIND { dwFlags = 0x1, fEnable = true };
            DwmEnableBlurBehindWindow(hwnd, ref bb);
        }

        // ── Dark mode ─────────────────────────────────────────────────────────
        private static bool ReadDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Tells Windows that this window prefers dark title bar chrome.
        /// </summary>
        public static void ApplyDarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int use = dark ? 1 : 0;
                // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
                DwmSetWindowAttribute(hwnd, 20, ref use, sizeof(int));
            }
            catch { }
        }

        // ── Monitor change recovery ────────────────────────────────────────────
        /// <summary>
        /// Ensures the given window position is visible on at least one connected screen.
        /// Call after DisplaySettingsChanged.
        /// </summary>
        public static void EnsureOnScreen(Window window)
        {
            try
            {
                // Collect virtual screen bounds across all screens
                var screens = System.Windows.Forms.Screen.AllScreens;
                bool onScreen = false;

                foreach (var screen in screens)
                {
                    var wa = screen.WorkingArea;
                    // Check if at least the title bar (top 40px) is visible
                    if (window.Left + window.Width  > wa.Left   &&
                        window.Left                 < wa.Right  &&
                        window.Top                  > wa.Top - 1 &&
                        window.Top + 40             < wa.Bottom)
                    {
                        onScreen = true;
                        break;
                    }
                }

                if (!onScreen)
                {
                    // Move to primary screen top-left
                    var primary = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
                    window.Left = Math.Max(0, primary.Left + 40);
                    window.Top  = Math.Max(0, primary.Top  + 40);
                }
            }
            catch { }
        }

        // ── DPI helpers ───────────────────────────────────────────────────────
        /// <summary>Returns the DPI scale factor for a given window (1.0 = 96dpi).</summary>
        public static double GetDpiScale(Window window)
        {
            try
            {
                var source = PresentationSource.FromVisual(window);
                return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            }
            catch { return 1.0; }
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;

            bool dark = ReadDarkMode();
            if (dark == IsDarkMode) return;

            IsDarkMode = dark;
            Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => ThemeChanged?.Invoke(dark)));
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    foreach (var fence in FenceManager.Instance.Fences)
                        EnsureOnScreen(fence);

                    FenceManager.Instance.SaveLayout();
                }));
        }

        // ── OS version detection ──────────────────────────────────────────────
        private static bool IsWin11()
        {
            // Win11 = build 22000+
            try
            {
                var ver = Environment.OSVersion.Version;
                if (ver.Major < 10) return false;
                // Read build from registry — OSVersion.Build is capped at 9999 in older runtimes
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var build = key?.GetValue("CurrentBuildNumber");
                if (build is string s && int.TryParse(s, out int b))
                    return b >= 22000;
                return ver.Build >= 22000;
            }
            catch { return false; }
        }

        // ── Dispose ───────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        // ── Native API ────────────────────────────────────────────────────────
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmEnableBlurBehindWindow(
            IntPtr hwnd, ref DWM_BLURBEHIND pBlurBehind);

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_BLURBEHIND
        {
            public uint   dwFlags;
            public bool   fEnable;
            public IntPtr hRgnBlur;
            public bool   fTransitionOnMaximized;
        }
    }
}
