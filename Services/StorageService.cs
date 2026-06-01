using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FencesWPF.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FencesWPF.Services
{
    /// <summary>
    /// Handles all file I/O for fences layout and settings.
    /// Saves to %AppData%\FencesWPF\
    /// Uses atomic write-then-replace to avoid corruption.
    /// </summary>
    public static class StorageService
    {
        // ─────────────────────────────────────────────────────────────
        // Paths
        // ─────────────────────────────────────────────────────────────
        private static readonly string RootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FencesWPF");

        public static readonly string LayoutFile =
            Path.Combine(RootFolder, "layout.json");

        public static readonly string SettingsFile =
            Path.Combine(RootFolder, "settings.json");

        public static readonly string BackupFolder =
            Path.Combine(RootFolder, "backups");

        public static readonly string TabGroupsFile =
            Path.Combine(RootFolder, "tabgroups.json");

        // ─────────────────────────────────────────────────────────────
        // Atomic Write
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Writes safely using temp file + replace.
        /// Prevents corruption if app crashes while saving.
        /// </summary>
        private static void AtomicWrite(string targetPath, string content)
        {
            EnsureRoot();

            string tempPath = targetPath + ".tmp";

            File.WriteAllText(tempPath, content, Encoding.UTF8);

            // First save
            if (!File.Exists(targetPath))
            {
                File.Move(tempPath, targetPath);
                return;
            }

            // Replace existing safely
            File.Replace(
                tempPath,
                targetPath,
                targetPath + ".bak",
                true);
        }

        // ─────────────────────────────────────────────────────────────
        // Layout Save / Load
        // ─────────────────────────────────────────────────────────────
        public static void SaveLayout(List<FenceData> fences,
                                          List<TabGroupData>? groups = null)
        {
            try
            {
                string json = JsonConvert.SerializeObject(fences, Formatting.Indented);
                AtomicWrite(LayoutFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Storage] Save layout error: {ex.Message}");
            }

            if (groups != null)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(groups, Formatting.Indented);
                    AtomicWrite(TabGroupsFile, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Storage] Save tabgroups error: {ex.Message}");
                }
            }
        }

        public static (List<TabGroupData>? data, string? error) LoadTabGroups()
        {
            try
            {
                if (!File.Exists(TabGroupsFile)) return (null, null);
                var json = File.ReadAllText(TabGroupsFile, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return (null, null);
                var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                if (token.Type != Newtonsoft.Json.Linq.JTokenType.Array) return (null, "Formato inválido");
                var list = token.ToObject<List<TabGroupData>>();
                return (list ?? new List<TabGroupData>(), null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        public static (List<FenceData>? data, string? error) LoadLayout()
        {
            // 1. Try main file
            var (data, error) = TryLoadLayout(LayoutFile);

            if (data != null)
                return (data, null);

            // 2. Try automatic backup
            var (bakData, _) = TryLoadLayout(LayoutFile + ".bak");

            if (bakData != null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Storage] Main layout corrupt. Restored from .bak");

                return (
                    bakData,
                    "El layout principal estaba dañado y se restauró automáticamente."
                );
            }

            // 3. Try latest manual backup
            try
            {
                if (Directory.Exists(BackupFolder))
                {
                    string? latestBackup = Directory
                        .GetFiles(BackupFolder, "*.json")
                        .OrderByDescending(File.GetCreationTime)
                        .FirstOrDefault();

                    if (latestBackup != null)
                    {
                        var (backupData, _) = TryLoadLayout(latestBackup);

                        if (backupData != null)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Storage] Restored from backup: {latestBackup}");

                            return (
                                backupData,
                                $"El layout se restauró desde: {Path.GetFileName(latestBackup)}"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Storage] Backup restore error: {ex.Message}");
            }

            return (null, error);
        }

        private static (List<FenceData>? data, string? error)
            TryLoadLayout(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return (null, null);

                string json = File.ReadAllText(path, Encoding.UTF8);

                if (string.IsNullOrWhiteSpace(json))
                    return (null, "Archivo vacío");

                // Validate JSON
                JToken token = JToken.Parse(json);

                if (token.Type != JTokenType.Array)
                    return (null, "Formato inválido");

                List<FenceData>? list =
                    token.ToObject<List<FenceData>>();

                return (list ?? new List<FenceData>(), null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Settings
        // ─────────────────────────────────────────────────────────────
        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(
                    settings,
                    Formatting.Indented);

                AtomicWrite(SettingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Storage] Save settings error: {ex.Message}");
            }
        }

        public static AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                    return new AppSettings();

                string json = File.ReadAllText(
                    SettingsFile,
                    Encoding.UTF8);

                return JsonConvert.DeserializeObject<AppSettings>(json)
                       ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Backups
        // ─────────────────────────────────────────────────────────────
        public static void CreateBackup()
        {
            try
            {
                if (!File.Exists(LayoutFile))
                    return;

                Directory.CreateDirectory(BackupFolder);

                string backupName =
                    $"layout_{DateTime.Now:yyyyMMdd_HHmmss}.json";

                string backupPath =
                    Path.Combine(BackupFolder, backupName);

                File.Copy(LayoutFile, backupPath, true);

                // Keep only latest 10 backups
                var oldBackups = Directory
                    .GetFiles(BackupFolder, "layout_*.json")
                    .OrderByDescending(File.GetCreationTime)
                    .Skip(10);

                foreach (string file in oldBackups)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // ignore delete failures
                    }
                }
            }
            catch
            {
                // backup is best-effort
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Import / Export
        // ─────────────────────────────────────────────────────────────
        public static void ExportLayout(string destinationPath)
        {
            if (File.Exists(LayoutFile))
            {
                File.Copy(LayoutFile, destinationPath, true);
            }
        }

        public static bool ImportLayout(string sourcePath)
        {
            try
            {
                // Validate before overwrite
                var (data, _) = TryLoadLayout(sourcePath);

                if (data == null)
                    return false;

                EnsureRoot();

                CreateBackup();

                File.Copy(sourcePath, LayoutFile, true);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────
        private static void EnsureRoot()
        {
            Directory.CreateDirectory(RootFolder);
            Directory.CreateDirectory(BackupFolder);
        }

        public static string StorageInfo =>
            $"Layout: {LayoutFile}\n" +
            $"Settings: {SettingsFile}\n" +
            $"Backups: {BackupFolder}";
    }
}