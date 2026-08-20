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
    private readonly ObservableCollection<SpItem> _sps = new();
    private readonly ObservableCollection<SpItem> _filteredSps = new();

    private bool _suppressProfileFilter;
    private readonly string? _initialEfmlPath;
    private UpdateInfo? _pendingUpdate;

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
        SpsList.ItemsSource = _filteredSps;

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
            _ = CheckForUpdatesAsync();
        };
    }

    /// <summary>
    /// Fire-and-forget startup check against GitHub Releases. Reveals the Update button
    /// only when a newer build exists; stays silent on offline/errors.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        var info = await UpdateChecker.CheckAsync(VersionInfo.Current);
        if (info is null)
            return;

        _pendingUpdate = info;
        Dispatcher.Invoke(() =>
        {
            UpdateButton.Content = $"⬇ Update to v{info.LatestVersion}";
            UpdateButton.ToolTip =
                $"Bản mới v{info.LatestVersion} đã có (bạn đang dùng v{info.CurrentVersion}). Bấm để tải và cài.";
            UpdateButton.Visibility = Visibility.Visible;
        });
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var info = _pendingUpdate;
        if (info is null)
            return;

        if (string.IsNullOrEmpty(info.InstallerUrl))
        {
            OpenUrl(info.ReleaseUrl);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Tải và cài đặt bản mới v{info.LatestVersion}?\n\nỨng dụng sẽ đóng để trình cài đặt ghi đè.",
            "Cập nhật EfmlGen", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
            return;

        try
        {
            UpdateButton.IsEnabled = false;
            var lastPercent = -1;
            var progress = new Progress<double>(p =>
            {
                var pct = (int)(p * 100);
                if (pct == lastPercent)
                    return;
                lastPercent = pct;
                SetStatus($"Đang tải bản cập nhật… {pct}%", busy: true);
            });
            SetStatus("Đang tải bản cập nhật…", busy: true);

            var installer = await Updater.DownloadInstallerAsync(info.InstallerUrl, info.LatestVersion, progress);

            Updater.RunInstaller(installer);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateButton.IsEnabled = true;
            SetStatus("Tải bản cập nhật thất bại", busy: false);
            MessageBox.Show(this,
                $"Không tải được bản cập nhật:\n{ex.Message}\n\nBạn có thể tải thủ công từ trang Releases.",
                "Cập nhật EfmlGen", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenUrl(info.ReleaseUrl);
        }
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // best-effort; nothing actionable if the shell can't open a browser
        }
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
        ContextClassBox.Text = p.ContextClass;
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;

        SyncNavToSelectedTab();

        if (DiagramViewer is null) return;
        if (MainTabs.SelectedIndex == 2)
        {
            if (string.IsNullOrWhiteSpace(DiagramViewer.CurrentEfmlPath))
            {
                var fallback = EfmlPathBox.Text;
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
            2 => NavDiagram,
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
        var forceDateTime = ForceDateTimeChk.IsChecked == true;
        await RunAsync("Loading tables...", () =>
        {
            var schemas = profile.Schemas.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            var dbModel = GenWorker.ReadSchema(connStr, dbProvider, schemas);

            var preselectFull = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preselectTableOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preselectSps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    foreach (var sp in existing.StoredProcedures) preselectSps.Add(sp.Procedure);
                    Console.WriteLine($"Found existing efml with {existing.Classes.Count} classes, {existing.StoredProcedures.Count} stored procedures — will pre-select matches.");
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

            var spResult = GenWorker.ReadStoredProcedures(connStr, dbProvider, schemas, forceDateTime);
            var spItems = spResult.Procedures
                .OrderBy(p => p.Procedure)
                .Select(p =>
                {
                    var it = new SpItem(p.Schema, p.Name, p.Procedure, p.Parameters.Count, p.ReturnComplexType != null);
                    if (preselectSps.Contains(p.Procedure)) it.IsSelected = true;
                    return it;
                })
                .ToList();
            var preselectedSpCount = spItems.Count(i => i.IsSelected);

            Dispatcher.Invoke(() =>
            {
                _tables.Clear();
                foreach (var it in items) _tables.Add(it);
                ApplyTableSearch();
                TableTotalText.Text = $"({items.Count} total)";
                SelectedCountText.Text = $"Selected: {preselectedCount}";

                _sps.Clear();
                foreach (var it in spItems) _sps.Add(it);
                ApplySpSearch();
                SpTotalText.Text = $"({spItems.Count} total)";
                SpSelectedCountText.Text = $"Selected: {preselectedSpCount}";

                MainTabs.SelectedIndex = 1;
            });
            Console.WriteLine($"Loaded {items.Count} tables ({preselectedCount} pre-selected), {spItems.Count} stored procedures ({preselectedSpCount} pre-selected). Switch to 'Scaffold' tab to pick.");
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

    private void SpSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySpSearch();

    private void ApplySpSearch()
    {
        var q = SpSearchBox.Text?.Trim() ?? "";
        _filteredSps.Clear();
        foreach (var s in _sps)
        {
            if (q.Length == 0 || s.Procedure.Contains(q, StringComparison.OrdinalIgnoreCase))
                _filteredSps.Add(s);
        }
    }

    private void SpCheck_Changed(object sender, RoutedEventArgs e)
    {
        var count = _sps.Count(s => s.IsSelected);
        SpSelectedCountText.Text = $"Selected: {count}";
    }

    private void SelectAllSps_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _filteredSps) s.IsSelected = true;
        RefreshSpsListBinding();
    }

    private void ClearSps_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _sps) s.IsSelected = false;
        RefreshSpsListBinding();
    }

    private void RefreshSpsListBinding()
    {
        var src = SpsList.ItemsSource;
        SpsList.ItemsSource = null;
        SpsList.ItemsSource = src;
        SpCheck_Changed(this, new RoutedEventArgs());
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

    // ------------------- Scaffold + Generate -------------------

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

    private async void ScaffoldAndGen_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfileFromForm();
        var connStr = ProfileStore.BuildConnectionString(profile, PasswordBox.Password);

        var selectedTables = _tables.Where(t => t.IsSelected).Select(t => t.Name).ToArray();
        if (selectedTables.Length == 0)
        {
            MessageBox.Show("Select at least one table from the list.", "Scaffold + Generate", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // No SPs loaded yet (user never clicked "Load Schema Tables + SPs") vs. explicitly
        // deselecting all of them are different intents — only pass a filter in the latter case.
        var selectedSps = _sps.Count == 0 ? null : _sps.Where(s => s.IsSelected).Select(s => s.Procedure).ToArray();

        var modelName = ModelNameBox.Text.Trim();
        var ns = NamespaceBox.Text.Trim();
        var efmlPath = EfmlPathBox.Text.Trim();
        if (string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(efmlPath))
        {
            MessageBox.Show("Fill model name, namespace, and output efml path.", "Scaffold + Generate", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Snapshot ALL UI state on the UI thread before background work.
        var fileBaseOverride = FileBaseNameBox.Text.Trim();
        var overwrite = OverwriteChk.IsChecked == true;
        var forceDateTime = ForceDateTimeChk.IsChecked == true;
        var dbProvider = ToDbProvider(profile.Provider);

        // gen-code defaults to the .efml folder when no explicit output dir is given.
        var outDir = OutputDirBox.Text.Trim();
        if (string.IsNullOrEmpty(outDir))
            outDir = Path.GetDirectoryName(Path.GetFullPath(efmlPath)) ?? "";
        var contextClass = ContextClassBox.Text.Trim();
        if (string.IsNullOrEmpty(contextClass))
        {
            contextClass = $"{modelName}DataContext";
            ContextClassBox.Text = contextClass;
        }
        var dcTemplate = DataContextTemplateBox.Text.Trim();
        var genConnStr = GenConnStringBox.Text;
        var genProviderStr = profile.Provider == "SqlServer" ? "SqlServer" : "Npgsql";
        var skipDataContext = SkipDataContextChk.IsChecked == true;
        var skipInfo = SkipInfoChk.IsChecked == true;
        var force = ForceChk.IsChecked == true;
        var fileBaseArg = string.IsNullOrEmpty(fileBaseOverride) ? null : fileBaseOverride;

        var spCountLabel = selectedSps == null ? "" : $", {selectedSps.Length} SPs";
        await RunAsync($"Scaffold + Generate ({selectedTables.Length} tables{spCountLabel})...", () =>
        {
            var schemas = profile.Schemas.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

            Console.WriteLine("=== Scaffold ===");
            var scaffold = GenWorker.Scaffold(connStr, dbProvider, schemas, selectedTables, modelName, ns, ns, efmlPath, overwrite, forceDateTime,
                fileBaseNameOverride: fileBaseArg, spFilter: selectedSps);
            PrintMergeReport(scaffold.MergeReport);
            PrintWarnings(scaffold.Warnings);

            Console.WriteLine();
            Console.WriteLine("=== Generate ===");
            var gen = GenWorker.GenCode(
                efmlPath, outDir, genProviderStr, genConnStr,
                contextClass,
                string.IsNullOrEmpty(dcTemplate) ? null : dcTemplate,
                skipDataContext: skipDataContext,
                skipInfo: skipInfo,
                force: force,
                timestamp: null,
                fileBaseNameOverride: fileBaseArg);

            PrintWarnings(gen.Warnings);
            if (gen.DeletedFiles.Count > 0)
            {
                Console.WriteLine($"Deleted {gen.DeletedFiles.Count} stale .cs file(s):");
                foreach (var f in gen.DeletedFiles) Console.WriteLine($"  - {f}");
            }
            Console.WriteLine($"Generated {gen.WrittenFiles.Count} files:");
            foreach (var f in gen.WrittenFiles) Console.WriteLine($"  {f}");

            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(OutputDirBox.Text))
                    OutputDirBox.Text = outDir;
            });
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

    private bool _logCollapsed;
    private void LogToggle_Click(object sender, RoutedEventArgs e)
    {
        _logCollapsed = !_logCollapsed;
        LogToggleBtn.Content = _logCollapsed ? "▶" : "▼";
        LogBox.Visibility = _logCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ContentGrid.RowDefinitions[1].Height = _logCollapsed
            ? GridLength.Auto
            : new GridLength(240);
    }

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

public sealed class SpItem : INotifyPropertyChanged
{
    public string Schema { get; }
    public string Name { get; }
    /// <summary>Fully-qualified "schema.name" — matches EfStoredProcedure.Procedure.</summary>
    public string Procedure { get; }
    public int ParamCount { get; }
    public bool HasResult { get; }
    public string Display => $"{Procedure}  ({ParamCount} params{(HasResult ? ", returns result" : "")})";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public SpItem(string schema, string name, string procedure, int paramCount, bool hasResult)
    { Schema = schema; Name = name; Procedure = procedure; ParamCount = paramCount; HasResult = hasResult; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
