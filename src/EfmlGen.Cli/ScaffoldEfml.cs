using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EfmlGen.Core;
using EfmlGen.Db;
using EfmlGen.Xml;

namespace EfmlGen.Cli;

internal static class ScaffoldEfml
{
    public static int Run(Dictionary<string, string> opts)
    {
        var connStr = opts.TryGetValue("--conn", out var c)
            ? c
            : opts.TryGetValue("--conn-env", out var e)
                ? Environment.GetEnvironmentVariable(e)
                  ?? throw new InvalidOperationException($"Env var {e} not set")
                : throw new ArgumentException("Missing --conn or --conn-env");

        var providerStr = opts.TryGetValue("--provider", out var p) ? p : "Postgres";
        var provider = providerStr.Equals("Postgres", StringComparison.OrdinalIgnoreCase) || providerStr.Equals("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? DbProvider.Postgres
            : throw new NotSupportedException($"Provider {providerStr} not supported in MVP (Postgres only).");

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
        Console.WriteLine($"  Got {dbModel.Tables.Count} tables.");

        // Client-side filter since Npgsql doesn't honor table filter
        if (tablesFilter != null)
        {
            var keep = dbModel.Tables.Where(t => tablesFilter.Contains(t.Name)).ToList();
            dbModel.Tables.Clear();
            foreach (var t in keep) dbModel.Tables.Add(t);
            Console.WriteLine($"  Filtered to {dbModel.Tables.Count} tables: {string.Join(", ", dbModel.Tables.Select(t => t.Name))}");
        }

        var forceDateTime = opts.ContainsKey("--force-datetime");

        var model = DatabaseModelMapper.Map(dbModel, new DatabaseModelMapper.MapOptions
        {
            Name = name,
            Namespace = ns,
            ContextNamespace = contextNs,
            ForceDateTime = forceDateTime
        });

        Console.WriteLine($"Mapped {model.Classes.Count} classes, {model.Associations.Count} associations.");

        // Merge with existing efml if present (preserve p1:Guid + user renames)
        var overwriteFlag = opts.ContainsKey("--overwrite");
        if (File.Exists(outPath) && !overwriteFlag)
        {
            Console.WriteLine($"Merging with existing {outPath}...");
            var existing = EfmlReader.ReadFile(outPath);
            var (merged, report) = EfmlMerger.Merge(model, existing);
            model = merged;
            PrintMergeReport(report);
        }
        else if (overwriteFlag)
        {
            Console.WriteLine("[overwrite] Discarding existing efml; using fresh GUIDs.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        EfmlWriter.WriteFile(model, outPath);
        Console.WriteLine($"Wrote {outPath}");

        if (!opts.ContainsKey("--skip-view"))
        {
            var diagramName = opts.TryGetValue("--diagram-name", out var dn) && !string.IsNullOrWhiteSpace(dn)
                ? dn
                : "Diagram1";
            var viewPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(outPath))!,
                $"{Path.GetFileNameWithoutExtension(outPath)}.{diagramName}.view");
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
    }

    private static string Required(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out var v)
            ? v
            : throw new ArgumentException($"Missing required option: {key}");
}
