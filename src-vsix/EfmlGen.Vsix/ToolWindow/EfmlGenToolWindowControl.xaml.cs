using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace EfmlGen.Vsix.ToolWindow
{
    public partial class EfmlGenToolWindowControl : UserControl
    {
        private ToolWindowViewModel _vm;

        public EfmlGenToolWindowControl()
        {
            InitializeComponent();
        }

        internal void Initialize(ToolWindowViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            // Sync PasswordBox with VM (PasswordBox doesn't allow binding for security).
            if (!string.IsNullOrEmpty(vm.Password))
                PasswordBox.Password = vm.Password;
            vm.LogLines.CollectionChanged += LogLines_CollectionChanged;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ToolWindowViewModel.Password)
                    && PasswordBox.Password != vm.Password)
                {
                    PasswordBox.Password = vm.Password ?? "";
                }
            };
        }

        private void LogLines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
        }

        private void PasswordBox_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_vm != null) _vm.Password = PasswordBox.Password;
        }

        private void BrowseOutputDir_Click(object sender, RoutedEventArgs e)
        {
            // Lightweight folder picker using OpenFileDialog hack (avoids WindowsForms dependency).
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select this folder",
                Filter = "Folders|no.files",
                Title = "Choose output directory",
            };
            if (_vm != null && !string.IsNullOrWhiteSpace(_vm.OutputDir))
            {
                dlg.InitialDirectory = _vm.OutputDir;
            }
            if (dlg.ShowDialog() == true)
            {
                var dir = System.IO.Path.GetDirectoryName(dlg.FileName);
                if (_vm != null && !string.IsNullOrEmpty(dir)) _vm.OutputDir = dir;
            }
        }
    }
}
