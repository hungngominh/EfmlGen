using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EfmlGen.Core;
using EfmlGen.Db;
using EfmlGen.Xml;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace EfmlGen.Cli;

internal static class ScaffoldEfml
{
    public static int Run(Dictionary<string, string> opts)
    {
        ProfileResolver.ApplyProfile(opts);

        // For scaffold-efml, --out is the .efml file path. If profile supplied OutputDir + ModelName
        // we can synthesize it. User-provided --out always wins.
        if (!opts.ContainsKey("--out")
            && opts.TryGetValue("--profile-output-dir", out var poDir) && !string.IsNullOrWhiteSpace(poDir)
            && opts.TryGetValue("--profile-model-name", out var poName) && !string.IsNullOrWhiteSpace(poName))
        {
            opts["--out"] = Path.Combine(poDir, poName + ".efml");
        }

        var connStr = ResolveConnectionString(opts);

        var providerStr = opts.TryGetValue("--provider", out var p) ? p : "Postgres";
        var provider = ParseProvider(providerStr);

        var schemas = opts.TryGetValue("--schemas", out var sc)
            ? sc.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "dbo" };
        var tablesFilter = opts.TryGetValue("--tables", out var tf)
            ? new HashSet<string>(tf.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase)
            : null;

        var name = Required(opts, "--name");
        var ns = Required(opts, "--namespace");
        var contextNs = opts.TryGetValue("--context-namespace", out var cn) ? cn : ns;
        var outPath = Required(opts, "--out");

        Console.WriteLine($"Reading schema from DB (schemas: {string.Join(",", schemas)})...");
        var dbModel = DatabaseSchemaReader.Read(connStr, provider, new SchemaReadOptions(Schemas: schemas));
        var viewCount = dbModel.Tables.Count(t => t is DatabaseView);
        Console.WriteLine($"  Got {dbModel.Tables.Count - viewCount} tables, {viewCount} views.");

        // EF Core returns views in dbModel.Tables (as DatabaseView). Drop them when --skip-views.
        if (opts.ContainsKey("--skip-views") && viewCount > 0)
        {
            var noViews = dbModel.Tables.Where(t => t is not DatabaseView).ToList();
            dbModel.Tables.Clear();
            foreach (var t in noViews) dbModel.Tables.Add(t);
            Console.WriteLine($"  [skip-views] Excluded {viewCount} views.");
        }

        // Client-side filter since Npgsql doesn't honor table filter
        if (tablesFilter != null)
        {
            var keep = dbModel.Tables.Where(t => tablesFilter.Contains(t.Name)).ToList();
            dbModel.Tables.Clear();
            foreach (var t in keep) dbModel.Tables.Add(t);
            Console.WriteLine($"  Filtered to {dbModel.Tables.Count} objects: {string.Join(", ", dbModel.Tables.Select(t => t.Name))}");
        }

        var forceDateTime = opts.ContainsKey("--force-datetime");

        var model = DatabaseModelMapper.Map(dbModel, new DatabaseModelMapper.MapOptions
        {
            Name = name,
            Namespace = ns,
            ContextNamespace = contextNs,
            Provider = provider,
            ForceDateTime = forceDateTime
        });

        Console.WriteLine($"Mapped {model.Classes.Count} classes, {model.Associations.Count} associations.");

        if (!opts.ContainsKey("--skip-stored-procedures"))
        {
            Console.WriteLine("Reading stored procedures...");
            var sp = StoredProcedureReader.Read(connStr, provider, schemas, forceDateTime);
            model.ComplexTypes.AddRange(sp.ComplexTypes);
            model.StoredProcedures.AddRange(sp.Procedures);
            Console.WriteLine($"  Got {sp.Procedures.Count} stored procedures, {sp.ComplexTypes.Count} result types.");
            foreach (var w in sp.Warnings)
                Console.WriteLine($"  [warn] {w}");
        }

        // Merge with existing efml if present (preserve p1:Guid + user renames)
        var overwriteFlag = opts.ContainsKey("--overwrite");
        if (File.Exists(outPath) && !overwriteFlag)
        {
            Console.WriteLine($"Merging with existing {outPath}...");
            var existing = EfmlReader.ReadFile(outPath);
            var (merged, report) = EfmlMerger.Merge(model, existing);
            model = merged;
            // Preserve any FileBaseName override stored in the existing efml.
            if (string.IsNullOrEmpty(model.FileBaseName) && !string.IsNullOrEmpty(existing.FileBaseName))
                model.FileBaseName = existing.FileBaseName;
            PrintMergeReport(report);
        }
        else if (overwriteFlag)
        {
            Console.WriteLine("[overwrite] Discarding existing efml; using fresh GUIDs.");
        }

        if (opts.TryGetValue("--file-base-name", out var fbn) && !string.IsNullOrWhiteSpace(fbn))
            model.FileBaseName = fbn.Trim();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        EfmlWriter.WriteFile(model, outPath);
        Console.WriteLine($"Wrote {outPath}");

        if (!opts.ContainsKey("--skip-view"))
        {
            var diagramName = opts.TryGetValue("--diagram-name", out var dn) && !string.IsNullOrWhiteSpace(dn)
                ? dn
                : "Diagram1";
            var fileBase = EfmlPathing.ResolveFileBaseName(model, outPath);
            var viewPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(outPath))!,
                $"{fileBase}.{diagramName}.view");
            ViewWriter.WriteFile(model, viewPath, diagramName);
            Console.WriteLine($"Wrote {viewPath}");
        }

        // Validate naming for compile-safety (do not auto-rename — only report)
        WarningPrinter.Print(CollisionDetector.Validate(model));

        return 0;
    }

    private static void PrintMergeReport(EfmlMerger.MergeReport r)
    {
        if (!r.HasChanges)
        {
            Console.WriteLine("  No structural changes since last scaffold.");
            return;
        }

        void Section(string label, List<string> items)
        {
            if (items.Count == 0) return;
            Console.WriteLine($"  {label} ({items.Count}):");
            foreach (var item in items) Console.WriteLine($"    - {item}");
        }

        Section("Added classes", r.AddedClasses);
        Section("Removed classes", r.RemovedClasses);
        Section("Renamed classes (preserved user names)", r.RenamedClasses);
        Section("Added properties", r.AddedProperties);
        Section("Removed properties", r.RemovedProperties);
        Section("Renamed properties (preserved user names)", r.RenamedProperties);
        Section("Added associations", r.AddedAssociations);
        Section("Removed associations", r.RemovedAssociations);
        Section("Added stored procedures", r.AddedStoredProcedures);
        Section("Removed stored procedures", r.RemovedStoredProcedures);
        Section("Renamed stored procedures (preserved user names)", r.RenamedStoredProcedures);
    }

    private static string Required(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out var v)
            ? v
            : throw new ArgumentException($"Missing required option: {key}");

    internal static string ResolveConnectionString(Dictionary<string, string> opts)
    {
        if (opts.TryGetValue("--conn", out var c))
        {
            if (string.IsNullOrWhiteSpace(c))
                throw new ArgumentException("--conn was provided but its value is empty.");
            return c;
        }
        if (opts.TryGetValue("--conn-env", out var envName))
        {
            if (string.IsNullOrWhiteSpace(envName))
                throw new ArgumentException("--conn-env was provided but no env-var name followed it.");
            var val = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrEmpty(val))
                throw new InvalidOperationException(
                    $"Environment variable '{envName}' (referenced by --conn-env) is not set or empty. " +
                    $"Set it first, e.g.  $env:{envName} = \"Host=...;Username=...;Password=...;Database=...\"");
            return val;
        }
        throw new ArgumentException("Missing connection string. Pass --conn \"<connection string>\" or --conn-env <ENV_VAR_NAME>.");
    }

    internal static DbProvider ParseProvider(string s) =>
        s.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("Npgsql", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
            ? DbProvider.Postgres
            : s.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
              s.Equals("MSSQL", StringComparison.OrdinalIgnoreCase) ||
              s.Equals("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase)
                ? DbProvider.SqlServer
                : throw new NotSupportedException($"Provider '{s}' not supported. Use Postgres or SqlServer.");
}
