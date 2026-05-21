using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EfmlGen.Vsix.Services;
using EfmlGen.Wpf.Services;

namespace EfmlGen.Vsix.ToolWindow
{
    /// <summary>
    /// View model for <see cref="EfmlGenToolWindowControl"/>. Drives the form,
    /// runs the bundled CLI through <see cref="CliRunner"/>, persists profiles via
    /// the shared-source <see cref="ProfileStore"/>.
    /// </summary>
    internal sealed class ToolWindowViewModel : INotifyPropertyChanged
    {
        private readonly OutputPaneLogger _outputPane;
        private bool _isBusy;
        private string _selectedProfileName;
        private string _provider = "Postgres";
        private string _host = "localhost";
        private int _port = 5432;
        private string _database = "";
        private string _username = "";
        private string _password = "";
        private string _schemas = "dbo";
        private string _modelName = "Entities";
        private string _namespace = "";
        private string _outputDir = "";
        private string _contextClass = "";

        public ToolWindowViewModel(OutputPaneLogger outputPane)
        {
            _outputPane = outputPane;
            Profiles = new ObservableCollection<string>();
            LogLines = new ObservableCollection<string>();
            RefreshProfilesCommand = new RelayCommand(_ => RefreshProfiles());
            SaveProfileCommand = new RelayCommand(_ => SaveProfile(), _ => !IsBusy && !string.IsNullOrWhiteSpace(SelectedProfileName));
            TestConnectionCommand = new RelayCommand(_ => RunVerb("db-smoke", BuildSmokeArgs()), _ => !IsBusy);
            ScaffoldCommand = new RelayCommand(_ => RunVerb("scaffold-efml", BuildScaffoldArgs()), _ => !IsBusy && CanScaffold());
            GenerateCommand = new RelayCommand(_ => RunVerb("gen-code", BuildGenArgs()), _ => !IsBusy && CanGenerate());
            SyncCommand = new RelayCommand(_ => FireAndForget(SyncAsync()), _ => !IsBusy && CanScaffold() && CanGenerate());
            ClearLogCommand = new RelayCommand(_ => LogLines.Clear());

            RefreshProfiles();
        }

        public ObservableCollection<string> Profiles { get; }
        public ObservableCollection<string> LogLines { get; }

        public IReadOnlyList<string> ProviderOptions { get; } = new[] { "Postgres", "SqlServer" };

        public bool IsBusy
        {
            get => _isBusy;
            set { if (_isBusy != value) { _isBusy = value; OnChanged(); } }
        }

        public string SelectedProfileName
        {
            get => _selectedProfileName;
            set { if (_selectedProfileName != value) { _selectedProfileName = value; OnChanged(); LoadFromProfile(value); } }
        }

        public string Provider
        {
            get => _provider;
            set { if (_provider != value) { _provider = value; OnChanged(); AutoFlipPortAndSchema(); } }
        }

        public string Host { get => _host; set { if (_host != value) { _host = value; OnChanged(); } } }
        public int Port { get => _port; set { if (_port != value) { _port = value; OnChanged(); } } }
        public string Database { get => _database; set { if (_database != value) { _database = value; OnChanged(); } } }
        public string Username { get => _username; set { if (_username != value) { _username = value; OnChanged(); } } }
        public string Password { get => _password; set { if (_password != value) { _password = value; OnChanged(); } } }
        public string Schemas { get => _schemas; set { if (_schemas != value) { _schemas = value; OnChanged(); } } }
        public string ModelName { get => _modelName; set { if (_modelName != value) { _modelName = value; OnChanged(); } } }
        public string Namespace { get => _namespace; set { if (_namespace != value) { _namespace = value; OnChanged(); } } }
        public string OutputDir { get => _outputDir; set { if (_outputDir != value) { _outputDir = value; OnChanged(); } } }
        public string ContextClass { get => _contextClass; set { if (_contextClass != value) { _contextClass = value; OnChanged(); } } }

        public RelayCommand RefreshProfilesCommand { get; }
        public RelayCommand SaveProfileCommand { get; }
        public RelayCommand TestConnectionCommand { get; }
        public RelayCommand ScaffoldCommand { get; }
        public RelayCommand GenerateCommand { get; }
        public RelayCommand SyncCommand { get; }
        public RelayCommand ClearLogCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void RefreshProfiles()
        {
            var settings = ProfileStore.Load();
            Profiles.Clear();
            foreach (var p in settings.Profiles.OrderBy(p => p.Name))
                Profiles.Add(p.Name);

            if (!string.IsNullOrEmpty(settings.LastUsedProfileName) && Profiles.Contains(settings.LastUsedProfileName))
            {
                SelectedProfileName = settings.LastUsedProfileName;
            }
            else if (Profiles.Count > 0)
            {
                SelectedProfileName = Profiles[0];
            }
        }

        private void LoadFromProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var settings = ProfileStore.Load();
            var p = settings.Profiles.FirstOrDefault(x => x.Name == name);
            if (p == null) return;
            Provider = p.Provider;
            Host = p.Host;
            Port = p.Port;
            Database = p.Database;
            Username = p.Username;
            Password = ProfileStore.DecryptPassword(p.EncryptedPassword);
            Schemas = p.Schemas;
            ModelName = p.ModelName;
            Namespace = p.Namespace;
            OutputDir = p.OutputDir;
            ContextClass = p.ContextClass;
        }

        private void SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(SelectedProfileName))
            {
                Append("[error] Enter a profile name first.");
                return;
            }
            var settings = ProfileStore.Load();
            var existing = settings.Profiles.FirstOrDefault(x => x.Name == SelectedProfileName);
            if (existing == null)
            {
                existing = new ConnectionProfile { Name = SelectedProfileName };
                settings.Profiles.Add(existing);
            }
            existing.Provider = Provider;
            existing.Host = Host;
            existing.Port = Port;
            existing.Database = Database;
            existing.Username = Username;
            existing.EncryptedPassword = ProfileStore.EncryptPassword(Password ?? "");
            existing.Schemas = Schemas;
            existing.ModelName = ModelName;
            existing.Namespace = Namespace;
            existing.OutputDir = OutputDir;
            existing.ContextClass = ContextClass;
            settings.LastUsedProfileName = SelectedProfileName;
            ProfileStore.Save(settings);
            Append($"[info] Saved profile '{SelectedProfileName}'.");
            RefreshProfiles();
        }

        private void AutoFlipPortAndSchema()
        {
            if (Provider == "Postgres" && Port == 1433) Port = 5432;
            else if (Provider == "SqlServer" && Port == 5432) Port = 1433;
            if (Provider == "Postgres" && Schemas == "dbo") Schemas = "public";
            else if (Provider == "SqlServer" && Schemas == "public") Schemas = "dbo";
        }

        private bool CanScaffold() =>
            !string.IsNullOrWhiteSpace(ModelName) && !string.IsNullOrWhiteSpace(Namespace) && !string.IsNullOrWhiteSpace(OutputDir);

        private bool CanGenerate() =>
            !string.IsNullOrWhiteSpace(ModelName) && !string.IsNullOrWhiteSpace(OutputDir);

        private List<string> BuildSmokeArgs()
        {
            return new List<string>
            {
                "--provider", Provider,
                "--conn", ProfileStore.BuildConnectionString(SnapshotProfile(), Password ?? ""),
                "--schemas", Schemas ?? "dbo",
            };
        }

        private List<string> BuildScaffoldArgs()
        {
            EnsureOutputDir();
            return new List<string>
            {
                "--provider", Provider,
                "--conn", ProfileStore.BuildConnectionString(SnapshotProfile(), Password ?? ""),
                "--schemas", Schemas ?? "dbo",
                "--name", ModelName,
                "--namespace", Namespace,
                "--out", Path.Combine(OutputDir, ModelName + ".efml"),
            };
        }

        private List<string> BuildGenArgs()
        {
            EnsureOutputDir();
            var efml = Path.Combine(OutputDir, ModelName + ".efml");
            var args = new List<string>
            {
                "--efml", efml,
                "--out", OutputDir,
                "--provider", Provider == "Postgres" ? "Npgsql" : "SqlServer",
            };
            if (!string.IsNullOrWhiteSpace(ContextClass))
            {
                args.Add("--context-class");
                args.Add(ContextClass);
            }
            return args;
        }

        private ConnectionProfile SnapshotProfile() => new ConnectionProfile
        {
            Name = SelectedProfileName ?? "(unsaved)",
            Provider = Provider,
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Schemas = Schemas,
            ModelName = ModelName,
            Namespace = Namespace,
            OutputDir = OutputDir,
            ContextClass = ContextClass,
        };

        private void EnsureOutputDir()
        {
            if (!string.IsNullOrWhiteSpace(OutputDir) && !Directory.Exists(OutputDir))
            {
                Directory.CreateDirectory(OutputDir);
            }
        }

        private void RunVerb(string verb, IReadOnlyList<string> args)
        {
            FireAndForget(RunVerbAsync(verb, args));
        }

        private static void FireAndForget(Task task)
        {
            // Top-level catch — async commands shouldn't crash the package on an unhandled exception.
            _ = task.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    System.Diagnostics.Debug.WriteLine("[EfmlGen] background task failed: " + t.Exception);
                }
            }, TaskScheduler.Default);
        }

        private async Task<int> RunVerbAsync(string verb, IReadOnlyList<string> args)
        {
            IsBusy = true;
            try
            {
                Append("");
                Append($"=== {verb} ===");
                var exit = await CliRunner.RunAsync(
                    verb: verb,
                    args: args,
                    onStdout: new Progress<string>(Append),
                    onStderr: new Progress<string>(s => Append("[stderr] " + s)),
                    workingDirectory: string.IsNullOrWhiteSpace(OutputDir) ? null : OutputDir);
                Append($"=== {verb} exit: {exit} ===");
                if (exit == 3) Append("[hint] Exit 3 = CollisionDetector. Fix duplicate names in .efml.");
                return exit;
            }
            catch (Exception ex)
            {
                Append($"[error] {ex.GetType().Name}: {ex.Message}");
                return -1;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SyncAsync()
        {
            var s = await RunVerbAsync("scaffold-efml", BuildScaffoldArgs());
            if (s == 0)
            {
                await RunVerbAsync("gen-code", BuildGenArgs());
            }
            else
            {
                Append("[info] scaffold-efml failed; skipping gen-code.");
            }
        }

        private void Append(string line)
        {
            // ObservableCollection requires UI thread for adds bound to ItemsControl.
            // The XAML root TextBox uses TwoWay binding to a joined string instead — keep simple.
            // We marshal to UI by invoking via dispatcher in the View.
            LogLines.Add(line);
            _outputPane?.WriteLine(line);
        }
    }
}
