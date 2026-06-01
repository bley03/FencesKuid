using FencesWPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

// Resolve WinForms vs WPF conflicts
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FencesWPF.Views
{
    public partial class SearchDialog : Window
    {
        public SearchDialog() => InitializeComponent();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TbSearch.Focus();
            RunSearch("");
        }

        private void TbSearch_KeyUp(object sender, KeyEventArgs e)
        {
            RunSearch(TbSearch.Text);
            if (e.Key == Key.Enter && LbResults.Items.Count > 0)
                LbResults.SelectedIndex = 0;
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) =>
            RunSearch(TbSearch.Text);

        private void RunSearch(string query)
        {
            var results = new List<SearchResult>();
            string q = query.Trim().ToLowerInvariant();

            foreach (var fence in FenceManager.Instance.Fences)
            {
                foreach (var sc in fence.Shortcuts)
                {
                    if (string.IsNullOrEmpty(q) ||
                        sc.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        sc.Path.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new SearchResult
                        {
                            ShortcutName = sc.Name,
                            FenceName    = fence.FenceTitle,
                            Path         = sc.Path,
                            FullPath     = sc.Path
                        });
                    }
                }
            }

            LbResults.ItemsSource = results;
        }

        private void LbResults_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

        private void LbResults_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

        private void BtnOpen_Click(object sender, RoutedEventArgs e) => OpenSelected();

        private void OpenSelected()
        {
            if (LbResults.SelectedItem is SearchResult r)
            {
                try { Process.Start(new ProcessStartInfo { FileName = r.FullPath, UseShellExecute = true }); }
                catch { }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();
    }

    public class SearchResult
    {
        public string ShortcutName { get; set; } = "";
        public string FenceName    { get; set; } = "";
        public string Path         { get; set; } = "";
        public string FullPath     { get; set; } = "";
    }
}
