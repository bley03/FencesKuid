using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FencesWPF.Views;

namespace FencesWPF.Services
{
    /// <summary>
    /// Handles the Peek feature: Win+Space brings all FencePanels to the front
    /// with a fade-in animation, then returns them to the desktop on release.
    /// </summary>
    public class PeekService : IDisposable
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static PeekService? _instance;
        public static PeekService Instance => _instance ??= new PeekService();
        private PeekService() { }

        // ── State ──────────────────────────────────────────────────────────────
        private IntPtr _hookHandle  = IntPtr.Zero;
        // The delegate MUST be stored as a field AND pinned via GCHandle.
        // If the GC collects it before UnhookWindowsHookEx, Windows calls into
        // freed memory → AccessViolationException crash.
        private LowLevelKeyboardProc? _hookProc;
        private System.Runtime.InteropServices.GCHandle _hookProcPin;
        private bool _isPeeking  = false;
        private bool _disposed   = false;

        // Virtual-key codes
        private const int VK_SPACE  = 0x20;
        private const int VK_LWIN   = 0x5B;
        private const int VK_RWIN   = 0x5C;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN   = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP     = 0x0101;
        private const int WM_SYSKEYUP  = 0x0105;

        // Win key tracking (GetAsyncKeyState is simpler but less reliable cross-thread)
        private bool _winKeyDown = false;

        // Animation durations
        private static readonly Duration FadeInDuration  = new Duration(TimeSpan.FromMilliseconds(180));
        private static readonly Duration FadeOutDuration = new Duration(TimeSpan.FromMilliseconds(250));

        // HWND_TOPMOST / HWND_BOTTOM
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_BOTTOM  = new IntPtr(1);
        private const uint SWP_NOMOVE     = 0x0002;
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        // ── Public API ─────────────────────────────────────────────────────────
        public void Start()
        {
            if (_hookHandle != IntPtr.Zero) return;

            _hookProc    = HookCallback;
            // Pin the delegate so the GC cannot move or collect it
            // while the unmanaged hook holds a function pointer to it.
            _hookProcPin = System.Runtime.InteropServices.GCHandle.Alloc(_hookProc);
            _hookHandle  = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                               GetModuleHandle(null), 0);

            if (_hookHandle == IntPtr.Zero)
            {
                // Hook failed — free pin and clean up
                _hookProcPin.Free();
                _hookProc = null;
                System.Diagnostics.Debug.WriteLine("[PeekService] SetWindowsHookEx failed");
            }
        }

        public void Stop()
        {
            if (_hookHandle == IntPtr.Zero) return;

            // Unhook first, THEN free the pin.
            // Freeing before unhooking = crash if Windows fires the hook between both lines.
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;

            if (_hookProcPin.IsAllocated)
                _hookProcPin.Free();

            _hookProc = null;

            if (_isPeeking)
                Application.Current?.Dispatcher.Invoke(() => EndPeek());
        }

        // ── Hook callback ──────────────────────────────────────────────────────
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int msg    = wParam.ToInt32();

                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp   = msg == WM_KEYUP   || msg == WM_SYSKEYUP;

                // Track Win key state
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if (isDown) _winKeyDown = true;
                    if (isUp)
                    {
                        _winKeyDown = false;
                        // Win released → end peek
                        if (_isPeeking)
                            Application.Current?.Dispatcher.BeginInvoke(
                                DispatcherPriority.Input, new Action(EndPeek));
                    }
                }

                // Win+Space pressed → start peek
                if (vkCode == VK_SPACE && isDown && _winKeyDown && !_isPeeking)
                {
                    Application.Current?.Dispatcher.BeginInvoke(
                        DispatcherPriority.Input, new Action(BeginPeek));

                    // Don't pass Win+Space to the system (prevent language switch)
                    return new IntPtr(1);
                }

                // Win+Space released → also end peek
                if (vkCode == VK_SPACE && isUp && _isPeeking)
                {
                    Application.Current?.Dispatcher.BeginInvoke(
                        DispatcherPriority.Input, new Action(EndPeek));
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // ── Peek Begin ────────────────────────────────────────────────────────
        private void BeginPeek()
        {
            if (_isPeeking) return;
            _isPeeking = true;

            foreach (var fence in FenceManager.Instance.Fences)
                fence.PeekShow(FadeInDuration);
            foreach (var group in FenceManager.Instance.TabGroups)
                group.PeekShow(FadeInDuration);
        }

        // ── Peek End ──────────────────────────────────────────────────────────
        private void EndPeek()
        {
            if (!_isPeeking) return;
            _isPeeking = false;

            foreach (var fence in FenceManager.Instance.Fences)
                fence.PeekHide(FadeOutDuration);
            foreach (var group in FenceManager.Instance.TabGroups)
                group.PeekHide(FadeOutDuration);
        }

        // ── Dispose ───────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        // ── Native API ────────────────────────────────────────────────────────
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter,
            int x, int y, int cx, int cy, uint uFlags);
    }
}
