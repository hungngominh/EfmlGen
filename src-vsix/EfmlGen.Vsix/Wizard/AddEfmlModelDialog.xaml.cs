using System.Linq;
using System.Windows;
using EfmlGen.Wpf.Services;

namespace EfmlGen.Vsix.Wizard
{
    public sealed class AddEfmlModelOptions
    {
        public string ProfileName { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string Tables { get; set; } = "";
    }

    public partial class AddEfmlModelDialog : Window
    {
        public AddEfmlModelOptions Result { get; private set; }

        public AddEfmlModelDialog(string defaultModelName, string defaultNamespace)
        {
            InitializeComponent();
            ModelNameBox.Text = defaultModelName ?? "Entities";
            NamespaceBox.Text = defaultNamespace ?? "";

            var settings = ProfileStore.Load();
            var names = settings.Profiles.Select(p => p.Name).OrderBy(n => n).ToArray();
            ProfileCombo.ItemsSource = names;
            if (names.Length == 0)
            {
                NoProfileWarning.Visibility = Visibility.Visible;
                OkButton.IsEnabled = false;
            }
            else
            {
                ProfileCombo.SelectedItem = settings.LastUsedProfileName != null
                    && names.Contains(settings.LastUsedProfileName)
                    ? settings.LastUsedProfileName
                    : names[0];
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileCombo.SelectedItem == null) return;
            Result = new AddEfmlModelOptions
            {
                ProfileName = ProfileCombo.SelectedItem.ToString(),
                ModelName = (ModelNameBox.Text ?? "").Trim(),
                Namespace = (NamespaceBox.Text ?? "").Trim(),
                Tables = (TablesBox.Text ?? "").Trim(),
            };
            DialogResult = true;
        }
    }
}
