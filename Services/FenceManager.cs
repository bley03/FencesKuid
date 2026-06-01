using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using FencesWPF.Models;
using FencesWPF.Views;
using Microsoft.Win32;

// Resolve WinForms vs WPF conflict
using Application = System.Windows.Application;

namespace FencesWPF.Services
{
    /// <summary>
    /// Singleton that owns all FencePanel instances, the system-tray icon,
    /// and the auto-save timer.
    /// </summary>
    public class FenceManager
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static FenceManager? _instance;
        public static FenceManager Instance => _instance ??= new FenceManager();
        private FenceManager() { }

        // ── State ──────────────────────────────────────────────────────────────
        public List<FencePanel> Fences { get; } = new();
        public List<FenceTabGroup> TabGroups { get; } = new();
        private AppSettings _settings = new();
        public AppSettings Settings => _settings;

        private NotifyIcon? _trayIcon;
        private System.Threading.Timer? _autoSaveTimer;
        private System.Threading.Timer? _saveDebounceTimer;   // coalesces rapid SaveLayout calls
        private bool _isExiting = false;  // guards against SaveLayout during shutdown
        private const int SaveDebounceMs = 800;               // wait 800ms of inactivity before writing

        // ── Initialize ─────────────────────────────────────────────────────────
        public void Initialize()
        {
            _settings = StorageService.LoadSettings();
            CreateTrayIcon();
            LoadLayout();
            StartAutoSave();
            PeekService.Instance.Start();
            WindowsIntegrationService.Instance.Start();
            WindowsIntegrationService.Instance.ThemeChanged += OnSystemThemeChanged;
            DesktopWatcherService.Instance.Start();
        }

        // ── Tray Icon ──────────────────────────────────────────────────────────
        private void CreateTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "FencesWPF",
                Visible = true
            };

            var menu = new ContextMenuStrip();

            var newItem = new ToolStripMenuItem("➕  Nuevo Fence");
            newItem.Click += (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(new Action(() => CreateFence()));

            var saveItem = new ToolStripMenuItem("💾  Guardar Layout");
            saveItem.Click += (_, _) => { StorageService.CreateBackup(); SaveLayout(); };

            var peekItem = new ToolStripMenuItem("👁  Peek  (Win+Espacio)");
            peekItem.Enabled = false;  // informativo — el shortcut es global

            var searchItem = new ToolStripMenuItem("🔍  Buscar Acceso Directo");
            searchItem.Click += (_, _) =>
                System.Windows.Application.Current.Dispatcher.Invoke(OpenSearch);

            var settingsItem = new ToolStripMenuItem("⚙️  Configuración Global");
            settingsItem.Click += (_, _) =>
                System.Windows.Application.Current.Dispatcher.Invoke(OpenGlobalSettings);

            var exportItem = new ToolStripMenuItem("📤  Exportar Layout...");
            exportItem.Click += (_, _) =>
                System.Windows.Application.Current.Dispatcher.Invoke(ExportLayout);

            var importItem = new ToolStripMenuItem("📥  Importar Layout...");
            importItem.Click += (_, _) =>
                System.Windows.Application.Current.Dispatcher.Invoke(ImportLayout);

            var exitItem = new ToolStripMenuItem("🚪  Salir");
            exitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(newItem);
            menu.Items.Add(saveItem);
            menu.Items.Add(peekItem);
            menu.Items.Add(searchItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(exportItem);
            menu.Items.Add(importItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) =>
                System.Windows.Application.Current.Dispatcher.Invoke(OpenGlobalSettings);
        }

        // ── Fence Creation ─────────────────────────────────────────────────────
        public FencePanel CreateFence(string title = "Nuevo Fence",
                                      double? x = null, double? y = null,
                                      double w = 280, double h = 320)
        {
            double cx = x ?? 120 + Fences.Count * 40;
            double cy = y ?? 120 + Fences.Count * 30;

            var data = new FenceData
            {
                Title = title,
                X = cx,
                Y = cy,
                Width = w,
                Height = h,
                BackgroundColor = _settings.DefaultBackground,
                Mode = _settings.DefaultMode,
                IconSize = _settings.DefaultIconSize,
            };

            return CreateFenceFromData(data);
        }

        private FencePanel CreateFenceFromData(FenceData data)
        {
            var fence = new FencePanel(data);

            fence.Closed += (_, _) =>
            {
                Fences.Remove(fence);
                if (!_isExiting) SaveLayout();  // don't overwrite during shutdown
            };

            Fences.Add(fence);
            fence.Show();
            SaveLayout();
            return fence;
        }

        public void RemoveFence(FencePanel fence)
        {
            if (!Fences.Contains(fence)) return;
            Fences.Remove(fence);
            fence.Close();
            SaveLayout();
        }

        // ── Tab Group management ───────────────────────────────────────────────
        /// <summary>
        /// Creates a standalone FencePanel from data that was previously a tab.
        /// Called when a tab is dragged out of a FenceTabGroup.
        /// </summary>
        public FencePanel CreateFenceFromTabData(FenceData data)
        {
            return CreateFenceFromData(data);
        }

        /// <summary>
        /// Removes a FenceTabGroup from the tracked list (does NOT close the window).
        /// Called from FenceTabGroup.OnClosed and DeleteGroup so the list stays in sync.
        /// </summary>
        public void RemoveTabGroup(FenceTabGroup group)
        {
            if (!TabGroups.Contains(group)) return;
            TabGroups.Remove(group);
            if (!_isExiting) SaveLayout();
        }

        /// <summary>
        /// Dissolves a FenceTabGroup that has only one tab left:
        /// closes the group window and converts the remaining tab into a standalone FencePanel.
        /// </summary>
        public void DissolveTabGroup(FenceTabGroup group, FenceData remainingTab)
        {
            TabGroups.Remove(group);
            group.Close();

            // Position the new standalone fence where the group was
            remainingTab.X = group.Left;
            remainingTab.Y = group.Top;
            remainingTab.Width = group.Width;
            remainingTab.Height = group.Height;

            CreateFenceFromData(remainingTab);
        }

        /// <summary>Creates and registers a new FenceTabGroup from saved data.</summary>
        public FenceTabGroup CreateTabGroup(TabGroupData data)
        {
            var group = new FenceTabGroup(data);
            group.Closed += (_, _) => RemoveTabGroup(group);
            TabGroups.Add(group);
            group.Show();
            SaveLayout();
            return group;
        }

        // ── Layout Persistence ─────────────────────────────────────────────────
        /// <summary>
        /// Schedules a save 800ms after the last call.
        /// Rapid calls (resize, drag) collapse into a single write.
        /// Pass force:true to write immediately (e.g. on exit or explicit save).
        /// </summary>
        public void SaveLayout(bool force = false)
        {
            if (_isExiting) return;

            if (force)
            {
                _saveDebounceTimer?.Change(
                    System.Threading.Timeout.Infinite,
                    System.Threading.Timeout.Infinite);

                // If called from a background thread, marshal to UI thread
                if (Application.Current?.Dispatcher.CheckAccess() == false)
                    Application.Current.Dispatcher.Invoke((Action)DoSaveLayout);
                else
                    DoSaveLayout();
                return;
            }

            // Reset debounce window — explicit Action cast avoids "Parameter count mismatch"
            _saveDebounceTimer ??= new System.Threading.Timer(
                _ => Application.Current?.Dispatcher.Invoke((Action)DoSaveLayout),
                null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            _saveDebounceTimer.Change(SaveDebounceMs, System.Threading.Timeout.Infinite);
        }

        private void DoSaveLayout()
        {
            if (_isExiting) return;

            // Snapshot fence data — must be on UI thread
            // (called via Dispatcher.Invoke so this is safe)
            var data = new List<FenceData>();
            foreach (var f in Fences)
            {
                try { data.Add(f.GetFenceData()); }
                catch { /* skip fences that are closing */ }
            }

            // File I/O is synchronous here — acceptable since it's debounced
            StorageService.CreateBackup();
            StorageService.SaveLayout(data);

            // Brief "✓" feedback on each panel title bar (already on UI thread)
            foreach (var fence in Fences)
            {
                try { fence.ShowSaveFeedback(); }
                catch { }
            }
        }

        private void LoadLayout()
        {
            var (list, error) = StorageService.LoadLayout();

            if (!string.IsNullOrEmpty(error))
            {
                System.Windows.MessageBox.Show(
                    $"Aviso al cargar el layout:\n{error}",
                    "FencesWPF — Restauración",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            if (list == null || list.Count == 0)
            {
                // First launch — show onboarding
                Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        var onboarding = new Views.OnboardingWindow();
                        onboarding.Show();
                    }));
                return;
            }

            foreach (var data in list)
            {
                // Guard: keep fences on screen (handles monitor disconnection)
                data.X = Math.Max(0, data.X);
                data.Y = Math.Max(0, data.Y);

                var fence = new FencePanel(data);
                fence.Closed += (_, _) =>
                {
                    Fences.Remove(fence);
                    if (!_isExiting) SaveLayout();
                };
                Fences.Add(fence);
                fence.Show();
            }
        }

        // ── Settings ───────────────────────────────────────────────────────────
        public void SaveSettings()
        {
            StorageService.SaveSettings(_settings);
            ApplyStartWithWindows(_settings.StartWithWindows);
        }

        private void ApplyStartWithWindows(bool enable)
        {
            try
            {
                const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using var reg = Registry.CurrentUser.OpenSubKey(key, true);
                if (reg == null) return;

                if (enable)
                {
                    string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                    reg.SetValue("FencesWPF", $"\"{exe}\"");
                }
                else
                {
                    reg.DeleteValue("FencesWPF", throwOnMissingValue: false);
                }
            }
            catch { /* non-critical */ }
        }

        // ── Auto-Save Timer ────────────────────────────────────────────────────
        private void StartAutoSave()
        {
            if (!_settings.AutoSave) return;
            int ms = Math.Max(10, _settings.AutoSaveInterval) * 1000;
            _autoSaveTimer = new System.Threading.Timer(_ =>
            {
                Application.Current?.Dispatcher.Invoke(() => SaveLayout());
            }, null, ms, ms);
        }

        public void RestartAutoSave()
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;
            if (_settings.AutoSave)
                StartAutoSave();
        }

        // ── Dialogs ────────────────────────────────────────────────────────────
        private void OpenGlobalSettings()
        {
            var win = new GlobalSettingsWindow();
            win.ShowDialog();
        }

        private void OpenSearch()
        {
            var win = new SearchDialog();
            win.Show();
        }

        private void ExportLayout()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar Layout",
                Filter = "JSON Files (*.json)|*.json",
                FileName = $"fences_backup_{DateTime.Now:yyyyMMdd}.json"
            };
            if (dlg.ShowDialog() == true)
            {
                StorageService.ExportLayout(dlg.FileName);
                System.Windows.MessageBox.Show(
                    $"Layout exportado correctamente:\n{dlg.FileName}",
                    "Exportar Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ImportLayout()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar Layout",
                Filter = "JSON Files (*.json)|*.json"
            };
            if (dlg.ShowDialog() == true)
            {
                if (StorageService.ImportLayout(dlg.FileName))
                {
                    // Close all current fences and reload
                    foreach (var f in new List<FencePanel>(Fences)) f.Close();
                    Fences.Clear();
                    LoadLayout();
                    System.Windows.MessageBox.Show(
                        "Layout importado correctamente.", "Importar Layout",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Error al importar el layout.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ── Default fence (onboarding) ────────────────────────────────────────
        /// <summary>Creates a starter fence centered on screen — called by OnboardingWindow.</summary>
        public void CreateDefaultFence()
        {
            var workArea = SystemParameters.WorkArea;
            var data = new FenceData
            {
                Title = "Mis Accesos",
                X = (workArea.Width - 280) / 2,
                Y = (workArea.Height - 200) / 2,
                Width = 280,
                Height = 200,
                Mode = _settings.DefaultMode,
                IconSize = _settings.DefaultIconSize,
                BackgroundColor = _settings.DefaultBackground,
                Opacity = 0.92,
                Shortcuts = new System.Collections.Generic.List<ShortcutData>()
            };

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var fence = new Views.FencePanel(data);
                fence.Closed += (_, _) =>
                {
                    Fences.Remove(fence);
                    if (!_isExiting) SaveLayout();
                };
                Fences.Add(fence);
                fence.Show();
                SaveLayout();
            });
        }

        // ── System theme ──────────────────────────────────────────────────────
        private void OnSystemThemeChanged(bool isDark)
        {
            foreach (var fence in Fences)
                fence.ApplySystemTheme(isDark);
        }

        // ── Snapping ───────────────────────────────────────────────────────────
        /// <summary>
        /// Professional snap engine — evaluates ALL panels and ALL edge combinations,
        /// picks the CLOSEST snap independently per axis, then applies both.
        ///
        /// X and Y are completely decoupled: snapping to the left edge of panel A
        /// on X does NOT force a Y snap to that same panel — Y finds its own best match
        /// from the entire panel list. This eliminates the "wrong corner" bug.
        /// </summary>
        /// <summary>
        /// Snap engine with cross-axis overlap guard.
        ///
        /// Each axis finds its best snap independently. Before applying, we check:
        /// - Y snap is only applied when moving panel shares X space with the Y-snap target.
        /// - X snap is only applied when moving panel shares Y space with the X-snap target.
        /// This prevents diagonal neighbours from dragging a panel into visible overlap.
        /// </summary>
        public void SnapToOtherPanels(FencePanel moving)
        {
            if (!_settings.EnableSnapping) return;
            double tol = _settings.SnapTolerance;

            double mL = moving.Left;
            double mR = moving.Left + moving.Width;
            double mT = moving.Top;
            double mB = moving.Top + moving.Height;
            double mW = moving.Width;
            double mH = moving.Height;

            var wa = System.Windows.SystemParameters.WorkArea;

            double bestXDist = tol + 1, bestYDist = tol + 1;
            double snapX = mL, snapY = mT;

            // Bounds of the panel that "won" each axis snap (for cross-check)
            double xWinL = 0, xWinR = 0, xWinT = 0, xWinB = 0;
            double yWinL = 0, yWinR = 0, yWinT = 0, yWinB = 0;
            bool xFromPanel = false, yFromPanel = false;

            // Build candidates: other fences + work-area boundary
            var candidates = new System.Collections.Generic.List<(double L, double R, double T, double B, bool isPanel)>();
            foreach (var other in Fences)
            {
                if (ReferenceEquals(other, moving)) continue;
                candidates.Add((other.Left, other.Left + other.Width,
                                other.Top, other.Top + other.Height, true));
            }
            candidates.Add((wa.Left, wa.Right, wa.Top, wa.Bottom, false));

            foreach (var c in candidates)
            {
                double oL = c.L, oR = c.R, oT = c.T, oB = c.B;
                bool ip = c.isPanel;

                // ── X: 4 combinations ────────────────────────────────────────
                double[] xDists = { Math.Abs(mL - oL), Math.Abs(mR - oR),
                                    Math.Abs(mL - oR), Math.Abs(mR - oL) };
                double[] xCands = { oL, oR - mW, oR, oL - mW };
                for (int i = 0; i < 4; i++)
                {
                    if (xDists[i] < bestXDist)
                    {
                        bestXDist = xDists[i];
                        snapX = xCands[i];
                        xWinL = oL; xWinR = oR; xWinT = oT; xWinB = oB;
                        xFromPanel = ip;
                    }
                }

                // ── Y: 4 combinations ────────────────────────────────────────
                double[] yDists = { Math.Abs(mT - oT), Math.Abs(mB - oB),
                                    Math.Abs(mT - oB), Math.Abs(mB - oT) };
                double[] yCands = { oT, oB - mH, oB, oT - mH };
                for (int i = 0; i < 4; i++)
                {
                    if (yDists[i] < bestYDist)
                    {
                        bestYDist = yDists[i];
                        snapY = yCands[i];
                        yWinL = oL; yWinR = oR; yWinT = oT; yWinB = oB;
                        yFromPanel = ip;
                    }
                }
            }

            bool applyX = bestXDist <= tol;
            bool applyY = bestYDist <= tol;

            // Cross-axis guard — only relevant when both axes want to snap to panels
            if (applyX && applyY && xFromPanel && yFromPanel)
            {
                double newL = snapX, newR = snapX + mW;
                double newT = snapY, newB = snapY + mH;

                // Y snap is valid only if the post-snap panel shares X range with its Y-winner
                bool sharesXforY = newR > yWinL && newL < yWinR;
                // X snap is valid only if the post-snap panel shares Y range with its X-winner
                bool sharesYforX = newB > xWinT && newT < xWinB;

                if (!sharesXforY) applyY = false;
                if (!sharesYforX) applyX = false;
            }

            if (applyX)
                moving.Left = Math.Max(wa.Left, Math.Min(wa.Right - mW, snapX));
            if (applyY)
                moving.Top = Math.Max(wa.Top, Math.Min(wa.Bottom - mH, snapY));
        }

        /// <summary>Updates best snap if this distance is closer than the current best.</summary>
        private static void TrySnapAxis(double dist, double candidate,
                                        ref double bestDist, ref double bestSnap)
        {
            if (dist < bestDist)
            {
                bestDist = dist;
                bestSnap = candidate;
            }
        }

        // ── Grid layout ────────────────────────────────────────────────────────
        /// <summary>
        /// Snaps a fence to a virtual grid dividing the screen into NxM cells.
        /// Called during resize to give Fences-6-style grid-aligned sizing.
        /// gridCols and gridRows come from AppSettings.
        /// </summary>
        public void SnapToGrid(FencePanel moving)
        {
            if (!_settings.EnableSnapping) return;

            var wa = System.Windows.SystemParameters.WorkArea;
            int cols = Math.Max(1, _settings.GridColumns);
            int rows = Math.Max(1, _settings.GridRows);
            double cellW = wa.Width / cols;
            double cellH = wa.Height / rows;
            double tol = _settings.SnapTolerance * 2; // grid snapping is more forgiving

            // Snap Left
            double snapL = Math.Round((moving.Left - wa.Left) / cellW) * cellW + wa.Left;
            if (Math.Abs(moving.Left - snapL) < tol) moving.Left = snapL;

            // Snap Top
            double snapT = Math.Round((moving.Top - wa.Top) / cellH) * cellH + wa.Top;
            if (Math.Abs(moving.Top - snapT) < tol) moving.Top = snapT;

            // Snap Width (right edge)
            double snapR = Math.Round((moving.Left + moving.Width - wa.Left) / cellW) * cellW + wa.Left;
            if (Math.Abs(moving.Left + moving.Width - snapR) < tol)
                moving.Width = Math.Max(moving.MinWidth, snapR - moving.Left);

            // Snap Height (bottom edge)
            double snapB = Math.Round((moving.Top + moving.Height - wa.Top) / cellH) * cellH + wa.Top;
            if (Math.Abs(moving.Top + moving.Height - snapB) < tol)
                moving.Height = Math.Max(moving.MinHeight, snapB - moving.Top);
        }

        // ── Exit ───────────────────────────────────────────────────────────────
        public void ExitApplication()
        {
            _autoSaveTimer?.Dispose();
            _saveDebounceTimer?.Dispose();
            PeekService.Instance.Stop();
            DesktopWatcherService.Instance.Stop();
            WindowsIntegrationService.Instance.Stop();
            // Save FIRST with full Fences list intact, then set _isExiting
            // so Closed handlers don't overwrite the clean snapshot.
            SaveLayout(force: true);
            SaveSettings();
            _isExiting = true;
            _trayIcon?.Dispose();
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                System.Windows.Application.Current.Shutdown());
        }
    }
}