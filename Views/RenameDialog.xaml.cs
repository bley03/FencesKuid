using System.Windows;
using System.Windows.Input;

namespace FencesWPF.Views
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; } = string.Empty;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            TbName.Text = currentName;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TbName.Focus();
            TbName.SelectAll();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) => Confirm();
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TbName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)  Confirm();
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        }

        private void Confirm()
        {
            NewName = TbName.Text.Trim();
            DialogResult = true;
            Close();
        }
    }
}
