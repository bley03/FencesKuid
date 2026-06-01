using System.Collections.Generic;
using System.Windows.Media;

namespace FencesWPF.Models
{
    public enum FenceMode { Static, AutoRoll, Collapsed }
    public enum IconSize { Small = 32, Medium = 48, Large = 64 }

    /// <summary>Runtime shortcut (includes loaded ImageSource icon).</summary>
    public class FenceShortcut
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public ImageSource? Icon { get; set; }
    }

    /// <summary>Serializable shortcut (only name + path, no ImageSource).</summary>
    public class ShortcutData
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>Complete serializable state of one fence panel.</summary>
    public class FenceData
    {
        public string Title          { get; set; } = "Nuevo Fence";
        public double X              { get; set; } = 100;
        public double Y              { get; set; } = 100;
        public double Width          { get; set; } = 280;
        public double Height         { get; set; } = 320;
        public bool   IsCollapsed    { get; set; } = false;
        public double Opacity        { get; set; } = 0.92;
        public string BackgroundColor{ get; set; } = "#CC1E1E2E";
        public string BorderColor    { get; set; } = "#FF4A90D9";
        public string TitleColor     { get; set; } = "#FF2A2A4A";
        public FenceMode Mode        { get; set; } = FenceMode.Static;
        public IconSize IconSize     { get; set; } = IconSize.Medium;
        public bool IsLocked         { get; set; } = false;
        public List<ShortcutData> Shortcuts { get; set; } = new();
    }

    /// <summary>Style of the tab bar in a TabGroup.</summary>
    public enum TabStyle { Flat, Segmented, Rounded }

    /// <summary>A group of fence panels stacked behind a shared tab bar.</summary>
    public class TabGroupData
    {
        public double   X              { get; set; } = 100;
        public double   Y              { get; set; } = 100;
        public double   Width          { get; set; } = 280;
        public double   Height         { get; set; } = 320;
        public double   Opacity        { get; set; } = 0.92;
        public bool     IsLocked       { get; set; } = false;
        public int      ActiveTabIndex { get; set; } = 0;
        public TabStyle TabStyle       { get; set; } = TabStyle.Rounded;
        public List<FenceData> Tabs    { get; set; } = new();
    }

    /// <summary>Global application settings.</summary>
    public class AppSettings
    {
        public bool     StartWithWindows     { get; set; } = false;
        public bool     EnableSnapping       { get; set; } = true;
        public double   SnapTolerance        { get; set; } = 15;
        public bool     AutoSave             { get; set; } = true;
        public int      AutoSaveInterval     { get; set; } = 30;
        public string   DefaultBackground    { get; set; } = "#CC1E1E2E";
        public FenceMode DefaultMode         { get; set; } = FenceMode.Static;
        public IconSize  DefaultIconSize     { get; set; } = IconSize.Medium;

        // ── Grid layout ───────────────────────────────────────────────────────
        public bool GridSnapping  { get; set; } = false;  // off by default, user enables in settings
        public int  GridColumns   { get; set; } = 12;     // virtual grid columns (like CSS grid)
        public int  GridRows      { get; set; } = 8;      // virtual grid rows
    }
}
