using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using EfmlGen.Core;
using EfmlGen.Db;
using EfmlGen.Wpf.Services;
using EfmlGen.Xml;
using Microsoft.Win32;

namespace EfmlGen.Wpf;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private readonly ObservableCollection<TableItem> _tables = new();
    private readonly ObservableCollection<TableItem> _filteredTables = new();

    private bool _suppressProfileFilter;
    private readonly string? _initialEfmlPath;

    private static readonly string AppVersion =
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version is { } v
            ? $"v{v.Major}.{v.Minor}.{v.Build}"
            : "";

    public MainWindow() : this(null) { }

    public MainWindow(string? initialEfmlPath)
    {
        _initialEfmlPath = initialEfmlPath;
        InitializeComponent();

        Console.SetOut(new CallbackTextWriter(line =>
            Dispatcher.Invoke(() => LogLine(line))));

        TablesList.ItemsSource = _filteredTables;

        ProfileCombo.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(ProfileCombo_TextChanged));
        ProfileCombo.DropDownClosed += ProfileCombo_DropDownClosed;

        Loaded += (_, _) =>
        {
            LoadProfiles();
            if (!string.IsNullOrWhiteSpace(_initialEfmlPath))
            {
                LoadOrCreateProfileForEfml(_initialEfmlPath!);
            }
            SetStatus("Ready", busy: false);
        };
    }

    /// <summary>
    /// Match an existing profile by <see cref="ConnectionProfile.EfmlPath"/> (case-insensitive
    /// full-path compare) and load it into the form. If none matches, create and persist a new
    /// profile bound to this file (Name = file basename, OutputDir = file dir, ModelName = basename).
    /// </summary>
    private void LoadOrCreateProfileForEfml(string efmlPath)
    {
        string normalized;
        try { normalized = Path.GetFullPath(efmlPath); }
        catch { normalized = efmlPath; }

        var match = _settings.Profiles.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.EfmlPath) &&
            string.Equals(SafeFullPath(p.EfmlPath), normalized, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            _settings.LastUsedProfileName = match.Name;
            ProfileStore.Save(_settings);
            RefreshProfileCombo();
            ProfileCombo.SelectedItem = match.Name;
            LoadProfileIntoForm(match);
            Console.WriteLine($"Loaded profile '{match.Name}' for {normalized}.");
            SetStatus($"Loaded profile '{match.Name}'.");
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(normalized);
        var newName = MakeUniqueProfileName(baseName, _settings);
        var dir = Path.GetDirectoryName(normalized) ?? "";
        var profile = new ConnectionProfile
        {
            Name = newName,
            EfmlPath = normalized,
            OutputDir = dir,
            ModelName = baseName,
        };
        _settings.Profiles.Add(profile);
        _settings.LastUsedProfileName = newName;
        ProfileStore.Save(_settings);
        RefreshProfileCombo();
        ProfileCombo.SelectedItem = newName;
        LoadProfileIntoForm(profile);
        Console.WriteLine($"Created new profile '{newName}' for {normalized}. Fill in connection details and Save Profile.");
        SetStatus($"Created profile '{newName}' — fill in connection details.");
    }

    private static string SafeFullPath(string p)
    {
        try { return Path.GetFullPath(p); }
        catch { return p; }
    }

    private static string MakeUniqueProfileName(string baseName, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Profile";
        if (settings.Profiles.All(p => p.Name != baseName)) return baseName;
        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (settings.Profiles.All(p => p.Name != candidate)) return candidate;
        }
        return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    private void ProfileCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressProfileFilter) return;
        if (ProfileCombo.ItemsSource is null) return;

        var view = CollectionViewSource.GetDefaultView(ProfileCombo.ItemsSource);
        if (view == null) return;

        var query = ProfileCombo.Text?.Trim() ?? "";
        view.Filter = string.IsNullOrEmpty(query)
            ? null
            : item => item is string s && s.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        if (ProfileCombo.IsKeyboardFocusWithin && !string.IsNullOrEmpty(query) && !ProfileCombo.IsDropDownOpen)
            ProfileCombo.IsDropDownOpen = true;
    }

    private void ProfileCombo_DropDownClosed(object? sender, EventArgs e)
    {
        if (ProfileCombo.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(ProfileCombo.ItemsSource);
        if (view?.Filter != null) view.Filter = null;
    }

    // ------------------- Profile management -------------------

    private void LoadProfiles()
    {
        _settings = ProfileStore.Load();
        RefreshProfileCombo();
        if (!string.IsNullOrEmpty(_settings.LastUsedProfileName))
        {
            var match = _settings.Profiles.FirstOrDefault(p => p.Name == _settings.LastUsedProfileName);
            if (match != null) LoadProfileIntoForm(match);
        }
    }

    private void RefreshProfileCombo()
    {
        _suppressProfileFilter = true;
        try
        {
            ProfileCombo.ItemsSource = null;
            ProfileCombo.ItemsSource = _settings.Profiles.Select(p => p.Name).ToList();
            if (!string.IsNullOrEmpty(_settings.LastUsedProfileName))
                ProfileCombo.SelectedItem = _settings.LastUsedProfileName;
        }
        finally
        {
            _suppressProfileFilter = false;
        }
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        var profile = _settings.Profiles.FirstOrDefault(p => p.Name == name);
        if (profile != null) LoadProfileIntoForm(profile);
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Provider switch flips DB defaults the user almost always wants:
        //   Postgres  → port 5432, schema "public"
        //   SqlServer → port 1433, schema "dbo"
        // Only overwrite when the field currently holds the *other* provider's default
        // so we don't stomp on a user-entered custom value.
        if (PortBox is null || SchemasBox is null) return;
        var provider = SelectedProvider();
        if (provider == "SqlServer")
        {
            if (PortBox.Text.Trim() is "" or "5432") PortBox.Text = "1433";
            if (SchemasBox.Text.Trim() is "" or "public") SchemasBox.Text = "dbo";
        }
        else
        {
            if (PortBox.Text.Trim() is "" or "1433") PortBox.Text = "5432";
        }
    }

    private string SelectedProvider() =>
        (ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() == "SqlServer"
            ? "SqlServer" : "Postgres";

    private static DbProvider ToDbProvider(string s) =>
        string.Equals(s, "SqlServer", StringComparison.OrdinalIgnoreCase)
            ? DbProvider.SqlServer : DbProvider.Postgres;

    private void LoadProfileIntoForm(ConnectionProfile p)
    {
        ProfileNameBox.Text = p.Name;
        foreach (ComboBoxItem item in ProviderCombo.Items)
            if ((string)item.Content == p.Provider) { ProviderCombo.SelectedItem = item; break; }
        HostBox.Text = p.Host;
        PortBox.Text = p.Port.ToString();
        DatabaseBox.Text = p.Database;
        UsernameBox.Text = p.Username;
        PasswordBox.Password = ProfileStore.DecryptPassword(p.EncryptedPassword);
        SchemasBox.Text = p.Schemas;
        ModelNameBox.Text = p.ModelName;
        FileBaseNameBox.Text = p.FileBaseName;
        NamespaceBox.Text = p.Namespace;
        OutputDirBox.Text = p.OutputDir;
        // Prefer the explicit EfmlPath stored in the profile — it preserves the actual file
        // chosen on import, including cases where the filename differs from ModelName.
        // Fall back to the legacy compose-from-OutputDir-and-ModelName behavior only when the
        // profile predates the EfmlPath field.
        EfmlPathBox.Text = !string.IsNullOrEmpty(p.EfmlPath)
            ? p.EfmlPath
            : (!string.IsNullOrEmpty(p.OutputDir) && !string.IsNullOrEmpty(p.ModelName)
                ? Path.Combine(p.OutputDir, p.ModelName + ".efml") : "");
        GenEfmlPathBox.Text = EfmlPathBox.Text;
        ContextClassBox.Text = p.ContextClass;
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;

        SyncNavToSelectedTab();

        if (DiagramViewer is null) return;
        if (MainTabs.SelectedIndex == 3)
        {
            if (string.IsNullOrWhiteSpace(DiagramViewer.CurrentEfmlPath))
            {
                var fallback = !string.IsNullOrWhiteSpace(GenEfmlPathBox.Text)
                    ? GenEfmlPathBox.Text
                    : EfmlPathBox.Text;
                if (!string.IsNullOrWhiteSpace(fallback))
                    DiagramViewer.CurrentEfmlPath = fallback;
            }
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabs is null) return;
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out var idx))
        {
            if (MainTabs.SelectedIndex != idx) MainTabs.SelectedIndex = idx;
        }
    }

    private void SyncNavToSelectedTab()
    {
        if (SidebarNav is null) return;
        var idx = MainTabs.SelectedIndex;
        RadioButton? target = idx switch
        {
            0 => NavConnection,
            1 => NavScaffold,
            2 => NavGenerate,
            3 => NavDiagram,
            _ => null
        };
        if (target != null && target.IsChecked != true) target.IsChecked = true;
    }

    private ConnectionProfile BuildProfileFromForm()
    {
        return new ConnectionProfile
        {
            Name = ProfileNameBox.Text.Trim(),
            Provider = (ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Postgres",
            Host = HostBox.Text.Trim(),
            Port = int.TryParse(PortBox.Text, out var p) ? p : 5432,
            Database = DatabaseBox.Text.Trim(),
            Username = UsernameBox.Text.Trim(),
            EncryptedPassword = ProfileStore.EncryptPassword(PasswordBox.Password),
            Schemas = SchemasBox.Text.Trim(),
            ModelName = ModelNameBox.Text.Trim(),
            Namespace = NamespaceBox.Text.Trim(),
            OutputDir = OutputDirBox.Text.Trim(),
            ContextClass = ContextClassBox.Text.Trim(),
            EfmlPath = EfmlPathBox.Text.Trim(),
            FileBaseName = FileBaseNameBox.Text.Trim()
        };
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameBox.Text))
        {
            MessageBox.Show("Enter a profile name first.", "Save profile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var profile = BuildProfileFromForm();
        _settings.Profiles.RemoveAll(p => p.Name == profile.Name);
        _settings.Profiles.Add(profile);
        _settings.LastUsedProfileName = profile.Name;
        ProfileStore.Save(_settings);
        RefreshProfileCombo();
        ProfileCombo.SelectedItem = profile.Name;
        SetStatus($"Saved profile '{profile.Name}'.");
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        if (MessageBox.Show($"Delete profile '{name}'?", "Delete", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _settings.Profiles.RemoveAll(p => p.Name == name);
        if (_settings.LastUsedProfileName == name) _settings.LastUsedProfileName = null;
        ProfileStore.Save(_settings);
        RefreshProfileCombo();
        SetStatus($"Deleted profile '{name}'.");
    }

    private void ImportFromEfml_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Entity Developer Model (*.efml)|*.efml|All files (*.*)|*.*",
            DefaultExt = "efml",
            Title = "Import profile from existing .efml"
        };
        if (dlg.ShowDialog(this) != true) return;

        var path = dlg.FileName;
        EfmlModel model;
        try
        {
            model = EfmlReader.ReadFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to read .efml:\n{ex.Message}", "Import .efml",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var outDir = Path.GetDirectoryName(path) ?? "";
        var schemas = model.Classes
            .Select(c => c.Schema)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var provider = DetectProvider(model);
        var profileName = string.IsNullOrWhiteSpace(model.Name)
            ? Path.GetFileNameWithoutExtension(path)
            : model.Name;

        // Compute filename base from the .efml path. When this differs from model.Name
        // (legacy case — e.g. ExternalChecklistDataModel.efml vs p1:name="ExternalChecklistEntities"),
        // surface it as an explicit FileBaseName override so generated .cs filenames match
        // the original Entity Developer output. If the efml already has FileBaseName, use it.
        var pathBase = Path.GetFileNameWithoutExtension(path);
        var fileBaseName = !string.IsNullOrEmpty(model.FileBaseName)
            ? model.FileBaseName
            : (!string.Equals(pathBase, model.Name, StringComparison.Ordinal) ? pathBase : "");

        var profile = new ConnectionProfile
        {
            Name = profileName,
            Provider = provider,
            Port = provider == "SqlServer" ? 1433 : 5432,
            Schemas = schemas.Length > 0 ? string.Join(",", schemas) : "dbo",
            ModelName = model.Name,
            Namespace = model.Namespace,
            OutputDir = outDir,
            ContextClass = string.IsNullOrEmpty(model.Name) ? "" : model.Name + "DataContext",
            EfmlPath = path,
            FileBaseName = fileBaseName
        };

        LoadProfileIntoForm(profile);
        EfmlPathBox.Text = path;
        GenEfmlPathBox.Text = path;

        Console.WriteLine($"Imported profile from {path}");
        Console.WriteLine($"  Name={profile.Name}, Provider={provider}, Namespace={profile.Namespace}");
        Console.WriteLine($"  Schemas=[{profile.Schemas}], Classes={model.Classes.Count}, Associations={model.Associations.Count}");
        SetStatus($"Imported '{profile.Name}' — fill connection details then click Save Profile.");
    }

    private static string DetectProvider(EfmlModel model)
    {
        int pg = 0, mssql = 0;
        foreach (var c in model.Classes)
        {
            ScoreColumn(c.Id?.Column, ref pg, ref mssql);
            foreach (var p in c.Properties)
                ScoreColumn(p.Column, ref pg, ref mssql);
        }
        return mssql > pg ? "SqlServer" : "Postgres";

        static void ScoreColumn(EfColumn? col, ref int pg, ref int mssql)
        {
            var t = col?.SqlType;
            if (string.IsNullOrEmpty(t)) return;
            t = t.ToLowerInvariant();
            if (t is "int4" or "int8" or "int2" or "bool" or "uuid" or "bytea" or "float4" or "float8"
                || t.StartsWith("timestamptz") || t.StartsWith("timetz")) pg++;
            else if (t is "uniqueidentifier" or "nvarchar" or "nchar" or "ntext" or "datetime2"
                or "datetimeoffset" or "bit" or "tinyint" or "rowversion" or "image"
                or "smalldatetime" or "money") mssql++;
        }
    }

    // ------------------- Connection tab -------------------

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfileFromForm();
        var connStr = ProfileStore.BuildConnectionString(profile, PasswordBox.Password);
        GenConnStringBox.Text = connStr;

        var dbProvider = ToDbProvider(profile.Provider);
        await RunAsync("Testing connection...", () =>
        {
            var schemas = profile.Schemas.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            var dbModel = GenWorker.ReadSchema(connStr, dbProvider, schemas);
            Console.WriteLine($"OK. DB: {dbModel.DatabaseName}, Tables in schemas: {dbModel.Tables.Count}");
        });
    }

    private async void LoadTables_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfileFromForm();
        var connStr = ProfileStore.BuildConnectionString(profile, PasswordBox.Password);
        GenConnStringBox.Text = connStr;
        var efmlPath = EfmlPathBox.Text.Trim();

        var dbProvider = ToDbProvider(profile.Provider);
        await RunAsync("Loading tables...", () =>
        {
            var schemas = profile.Schemas.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            var dbModel = GenWorker.ReadSchema(connStr, dbProvider, schemas);

            var preselectFull = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preselectTableOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(efmlPath) && File.Exists(efmlPath))
            {
                try
                {
                    var existing = EfmlReader.ReadFile(efmlPath);
                    foreach (var c in existing.Classes)
                    {
                        var tbl = UnquoteTable(c.Table);
                        preselectFull.Add(TableKey(c.Schema, tbl));
                        // Legacy efml files store schema only on the root <efcore>, leaving
                        // <class> elements with no schema. Also Devart often stamps schema="dbo"
                        // even when the DB schema is "public" (Postgres). Fall back to
                        // matching by unqualified table name so these still preselect.
                        preselectTableOnly.Add(tbl);
                    }
                    Console.WriteLine($"Found existing efml with {existing.Classes.Count} classes — will pre-select matching tables.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[warn] Could not read existing efml ({efmlPath}): {ex.Message}");
                }
            }

            var items = dbModel.Tables
                .OrderBy(t => t.Name)
                .Select(t =>
                {
                    var it = new TableItem(t.Schema ?? "", t.Name, t.Columns.Count, t.ForeignKeys.Count);
                    if (preselectFull.Contains(TableKey(t.Schema ?? "", t.Name))
                        || preselectTableOnly.Contains(t.Name))
                        it.IsSelected = true;
                    return it;
                })
                .ToList();

            var preselectedCount = items.Count(i => i.IsSelected);

            Dispatcher.Invoke(() =>
            {
                _tables.Clear();
                foreach (var it in items) _tables.Add(it);
                ApplyTableSearch();
                TableTotalText.Text = $"({items.Count} total)";
                SelectedCountText.Text = $"Selected: {preselectedCount}";
                MainTabs.SelectedIndex = 1;
            });
            Console.WriteLine($"Loaded {items.Count} tables ({preselectedCount} pre-selected). Switch to 'Scaffold' tab to pick.");
        });
    }

    private static string TableKey(string schema, string table) => $"{schema}|{table}";

    private static string UnquoteTable(string s) =>
        s.Length >= 2 && s.StartsWith('`') && s.EndsWith('`') ? s[1..^1] : s;

    // ------------------- Scaffold tab -------------------

    private void TableSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyTableSearch();

    private void ApplyTableSearch()
    {
        var q = TableSearchBox.Text?.Trim() ?? "";
        _filteredTables.Clear();
        foreach (var t in _tables)
        {
            if (q.Length == 0 || t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                _filteredTables.Add(t);
        }
    }

    private void TableCheck_Changed(object sender, RoutedEventArgs e)
    {
        var count = _tables.Count(t => t.IsSelected);
        SelectedCountText.Text = $"Selected: {count}";
    }

    private void SelectAllTables_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in _filteredTables) t.IsSelected = true;
        RefreshTablesListBinding();
    }

    private void ClearTables_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in _tables) t.IsSelected = false;
        RefreshTablesListBinding();
    }

    private void RefreshTablesListBinding()
    {
        var src = TablesList.ItemsSource;
        TablesList.ItemsSource = null;
        TablesList.ItemsSource = src;
        TableCheck_Changed(this, new RoutedEventArgs());
    }

    private void BrowseEfml_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Entity Developer Model (*.efml)|*.efml|All files (*.*)|*.*",
            DefaultExt = "efml",
            FileName = string.IsNullOrEmpty(ModelNameBox.Text) ? "Model.efml" : ModelNameBox.Text + ".efml"
        };
        if (dlg.ShowDialog(this) == true)
        {
            EfmlPathBox.Text = dlg.FileName;
            GenEfmlPathBox.Text = dlg.FileName;
            OutputDirBox.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
            // If the user picked an existing .efml, treat it as Import: auto-fill model name,
            // namespace, file base name and context class from the file so they don't have
            // to retype things that the file already knows.
            if (File.Exists(dlg.FileName))
                TryImportEfmlMetadata(dlg.FileName);
        }
    }

    /// <summary>
    /// Read an existing .efml and update form fields derived from it (model name, namespace,
    /// file base name, context class). Does NOT touch connection details (Host/Port/Username/
    /// Password) or profile name. Safe to call after Browse picks an existing file.
    /// </summary>
    private void TryImportEfmlMetadata(string path)
    {
        EfmlModel model;
        try { model = EfmlReader.ReadFile(path); }
        catch (Exception ex)
        {
            Console.WriteLine($"[warn] Picked existing .efml but failed to read it ({ex.Message}); leaving form fields untouched.");
            return;
        }

        if (!string.IsNullOrEmpty(model.Name)) ModelNameBox.Text = model.Name;
        if (!string.IsNullOrEmpty(model.Namespace)) NamespaceBox.Text = model.Namespace;
        if (string.IsNullOrEmpty(ContextClassBox.Text) && !string.IsNullOrEmpty(model.Name))
            ContextClassBox.Text = model.Name + "DataContext";

        var pathBase = Path.GetFileNameWithoutExtension(path);
        var fileBaseName = !string.IsNullOrEmpty(model.FileBaseName)
            ? model.FileBaseName
            : (!string.Equals(pathBase, model.Name, StringComparison.Ordinal) ? pathBase : "");
        FileBaseNameBox.Text = fileBaseName;

        Console.WriteLine($"Loaded metadata from existing {path}");
        Console.WriteLine($"  Name={model.Name}, Namespace={model.Namespace}, Classes={model.Classes.Count}");
        SetStatus($"Loaded model from existing .efml ({model.Classes.Count} classes).");
    }

    private async void Scaffold_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfileFromForm();
        var connStr = ProfileStore.BuildConnectionString(profile, PasswordBox.Password);

        var selectedTables = _tables.Where(t => t.IsSelected).Select(t => t.Name).ToArray();
        if (selectedTables.Length == 0)
        {
            MessageBox.Show("Select at least one table from the list.", "Scaffold", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var modelName = ModelNameBox.Text.Trim();
        var ns = NamespaceBox.Text.Trim();
        var efmlPath = EfmlPathBox.Text.Trim();
        var fileBaseOverride = FileBaseNameBox.Text.Trim();
        var overwrite = OverwriteChk.IsChecked == true;
        var forceDateTime = ForceDateTimeChk.IsChecked == true;

        if (string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(efmlPath))
        {
            MessageBox.Show("Fill model name, namespace, and output efml path.", "Scaffold", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dbProvider = ToDbProvider(profile.Provider);
        await RunAsync($"Scaffolding {selectedTables.Length} tables...", () =>
        {
            var schemas = profile.Schemas.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            var result = GenWorker.Scaffold(connStr, dbProvider, schemas, selectedTables, modelName, ns, ns, efmlPath, overwrite, forceDateTime,
                fileBaseNameOverride: string.IsNullOrEmpty(fileBaseOverride) ? null : fileBaseOverride);
            PrintMergeReport(result.MergeReport);
            PrintWarnings(result.Warnings);

            Dispatcher.Invoke(() =>
            {
                GenEfmlPathBox.Text = efmlPath;
                if (string.IsNullOrEmpty(OutputDirBox.Text))
                    OutputDirBox.Text = Path.GetDirectoryName(efmlPath) ?? "";
            });
        });
    }

    // ------------------- Generate tab -------------------

    private void BrowseGenEfml_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Entity Developer Model (*.efml)|*.efml|All files (*.*)|*.*",
            DefaultExt = "efml"
        };
        if (dlg.ShowDialog(this) == true)
        {
            GenEfmlPathBox.Text = dlg.FileName;
            EfmlPathBox.Text = dlg.FileName;
            if (string.IsNullOrEmpty(OutputDirBox.Text))
                OutputDirBox.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
            // Picking an existing .efml here means the user wants to generate code for it —
            // load model name / namespace / file base name so the Gen Code step has what it needs.
            if (File.Exists(dlg.FileName))
                TryImportEfmlMetadata(dlg.FileName);
        }
    }

    private void BrowseOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select output directory" };
        if (dlg.ShowDialog(this) == true) OutputDirBox.Text = dlg.FolderName;
    }

    private void BrowseDataContextTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "C# template (*.cs;*.cs.tmpl)|*.cs;*.cs.tmpl|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true) DataContextTemplateBox.Text = dlg.FileName;
    }

    private async void GenCode_Click(object sender, RoutedEventArgs e)
    {
        var efmlPath = GenEfmlPathBox.Text.Trim();
        var outDir = OutputDirBox.Text.Trim();
        var contextClass = ContextClassBox.Text.Trim();
        var dcTemplate = DataContextTemplateBox.Text.Trim();
        var connStr = GenConnStringBox.Text;

        if (!File.Exists(efmlPath)) { MessageBox.Show($".efml file not found:\n{efmlPath}", "Gen-code", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        if (string.IsNullOrEmpty(outDir)) { MessageBox.Show("Pick output directory.", "Gen-code", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        if (string.IsNullOrEmpty(contextClass))
        {
            var model = EfmlReader.ReadFile(efmlPath);
            contextClass = $"{model.Name}DataContext";
            ContextClassBox.Text = contextClass;
        }

        // Snapshot ALL UI state on UI thread before bg work
        var providerStr = ((ProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() == "SqlServer") ? "SqlServer" : "Npgsql";
        var skipDataContext = SkipDataContextChk.IsChecked == true;
        var skipInfo = SkipInfoChk.IsChecked == true;
        var force = ForceChk.IsChecked == true;
        var contextClassLocal = contextClass;
        var fileBaseOverride = FileBaseNameBox.Text.Trim();

        await RunAsync("Generating .cs files...", () =>
        {
            var result = GenWorker.GenCode(
                efmlPath, outDir, providerStr, connStr,
                contextClassLocal,
                string.IsNullOrEmpty(dcTemplate) ? null : dcTemplate,
                skipDataContext: skipDataContext,
                skipInfo: skipInfo,
                force: force,
                timestamp: null,
                fileBaseNameOverride: string.IsNullOrEmpty(fileBaseOverride) ? null : fileBaseOverride);

            PrintWarnings(result.Warnings);
            if (result.DeletedFiles.Count > 0)
            {
                Console.WriteLine($"Deleted {result.DeletedFiles.Count} stale .cs file(s):");
                foreach (var f in result.DeletedFiles) Console.WriteLine($"  - {f}");
            }
            Console.WriteLine($"Generated {result.WrittenFiles.Count} files:");
            foreach (var f in result.WrittenFiles) Console.WriteLine($"  {f}");
        });
    }

    // ------------------- Async runner + helpers -------------------

    private async Task RunAsync(string status, Action work)
    {
        SetStatus(status, busy: true);
        await Task.Run(() =>
        {
            try { work(); }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace ?? "(no stack trace)");
                var inner = ex.InnerException;
                while (inner != null)
                {
                    Console.WriteLine($"  caused by: {inner.GetType().Name}: {inner.Message}");
                    Console.WriteLine(inner.StackTrace ?? "(no stack trace)");
                    inner = inner.InnerException;
                }
            }
        });
        SetStatus("Ready", busy: false);
    }

    private void SetStatus(string text, bool busy = false)
    {
        Dispatcher.Invoke(() =>
        {
            StatusBarText.Text = $"EfmlGen Designer {AppVersion} · {text}";
            StatusText.Text = text;
            ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ProgressBar.IsIndeterminate = busy;
        });
    }

    private void LogLine(string line)
    {
        LogBox.AppendText(line + "\r\n");
        LogBox.ScrollToEnd();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void PrintMergeReport(EfmlMerger.MergeReport? r)
    {
        if (r == null || !r.HasChanges) return;
        Console.WriteLine();
        Console.WriteLine("--- Merge report ---");
        void S(string label, List<string> items)
        {
            if (items.Count == 0) return;
            Console.WriteLine($"  {label} ({items.Count}):");
            foreach (var it in items) Console.WriteLine($"    - {it}");
        }
        S("Added classes", r.AddedClasses);
        S("Removed classes", r.RemovedClasses);
        S("Renamed classes (preserved user names)", r.RenamedClasses);
        S("Added properties", r.AddedProperties);
        S("Removed properties", r.RemovedProperties);
        S("Renamed properties (preserved user names)", r.RenamedProperties);
        S("Added associations", r.AddedAssociations);
        S("Removed associations", r.RemovedAssociations);
    }

    private void PrintWarnings(IReadOnlyList<CollisionDetector.Warning> ws)
    {
        if (ws.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine($"--- {ws.Count} validation issue(s) ---");
        foreach (var w in ws)
        {
            var prefix = w.Severity == CollisionDetector.Severity.Error ? "[error]  " : "[warning]";
            Console.WriteLine($"  {prefix} {w.Message}");
        }
    }
}

public sealed class TableItem : INotifyPropertyChanged
{
    public string Schema { get; }
    public string Name { get; }
    public string Display => $"{Schema}.{Name}  ({Cols} cols, {Fks} FKs)";
    public int Cols { get; }
    public int Fks { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public TableItem(string schema, string name, int cols, int fks) { Schema = schema; Name = name; Cols = cols; Fks = fks; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
