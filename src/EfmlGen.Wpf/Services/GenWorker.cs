using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EfmlGen.Core;
using EfmlGen.Db;
using EfmlGen.Templates;
using EfmlGen.Xml;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace EfmlGen.Wpf.Services;

/// <summary>
/// Direct in-process wrappers around the EfmlGen pipeline. KHÔNG shell-out tới CLI.
/// Mọi method có thể throw — caller catch + show error.
/// </summary>
public static class GenWorker
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static DatabaseModel ReadSchema(string connectionString, DbProvider provider, string[] schemas) =>
        DatabaseSchemaReader.Read(connectionString, provider, new SchemaReadOptions(Schemas: schemas));

    public sealed record ScaffoldResult(
        EfmlModel Model,
        EfmlMerger.MergeReport? MergeReport,
        IReadOnlyList<CollisionDetector.Warning> Warnings);

    public static ScaffoldResult Scaffold(
        string connectionString,
        DbProvider provider,
        string[] schemas,
        string[]? tableFilter,
        string modelName,
        string @namespace,
        string contextNamespace,
        string outEfmlPath,
        bool overwriteFresh,
        bool forceDateTime = false)
    {
        Console.WriteLine($"Reading schema from DB (schemas: {string.Join(",", schemas)})...");
        var dbModel = DatabaseSchemaReader.Read(connectionString, provider, new SchemaReadOptions(Schemas: schemas));
        Console.WriteLine($"  Got {dbModel.Tables.Count} tables.");

        if (tableFilter != null && tableFilter.Length > 0)
        {
            var keepSet = new HashSet<string>(tableFilter, StringComparer.OrdinalIgnoreCase);
            var keep = dbModel.Tables.Where(t => keepSet.Contains(t.Name)).ToList();
            dbModel.Tables.Clear();
            foreach (var t in keep) dbModel.Tables.Add(t);
            Console.WriteLine($"  Filtered to {dbModel.Tables.Count} tables.");
        }

        var model = DatabaseModelMapper.Map(dbModel, new DatabaseModelMapper.MapOptions
        {
            Name = modelName,
            Namespace = @namespace,
            ContextNamespace = contextNamespace,
            Provider = provider,
            ForceDateTime = forceDateTime
        });

        Console.WriteLine($"Mapped {model.Classes.Count} classes, {model.Associations.Count} associations.");

        EfmlMerger.MergeReport? mergeReport = null;
        if (File.Exists(outEfmlPath) && !overwriteFresh)
        {
            Console.WriteLine($"Merging with existing {outEfmlPath}...");
            var existing = EfmlReader.ReadFile(outEfmlPath);
            var (merged, report) = EfmlMerger.Merge(model, existing);
            model = merged;
            mergeReport = report;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outEfmlPath))!);
        EfmlWriter.WriteFile(model, outEfmlPath);
        Console.WriteLine($"Wrote {outEfmlPath}");

        var viewPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(outEfmlPath))!,
            $"{Path.GetFileNameWithoutExtension(outEfmlPath)}.Diagram1.view");
        ViewWriter.WriteFile(model, viewPath);
        Console.WriteLine($"Wrote {viewPath}");

        var warnings = CollisionDetector.Validate(model);
        return new ScaffoldResult(model, mergeReport, warnings);
    }

    public sealed record GenCodeResult(
        IReadOnlyList<string> WrittenFiles,
        IReadOnlyList<string> DeletedFiles,
        IReadOnlyList<CollisionDetector.Warning> Warnings);

    public static GenCodeResult GenCode(
        string efmlPath,
        string outDir,
        string provider,
        string connectionString,
        string contextClass,
        string? dataContextTemplatePath,
        bool skipDataContext,
        bool skipInfo,
        bool force,
        DateTime? timestamp)
    {
        if (!File.Exists(efmlPath))
            throw new FileNotFoundException($".efml file not found: {efmlPath}");
        Directory.CreateDirectory(outDir);

        var model = EfmlReader.ReadFile(efmlPath);
        var warnings = CollisionDetector.Validate(model);
        var hasError = warnings.Any(w => w.Severity == CollisionDetector.Severity.Error);
        if (hasError && !force)
        {
            var errCount = warnings.Count(w => w.Severity == CollisionDetector.Severity.Error);
            throw new InvalidOperationException(
                $"Collision detector found {errCount} error(s). See log above for details. Tick 'Force' to generate anyway (not recommended).");
        }

        var ctx = new GenerationContext
        {
            Timestamp = timestamp ?? DateTime.Now,
            Provider = provider,
            ConnectionString = connectionString
        };

        var navsByClass = AssociationLayout.Build(model);
        var written = new List<string>();

        foreach (var c in model.Classes)
        {
            var navs = navsByClass.TryGetValue(c.Name, out var n) ? n : new();
            var content = EntityEmitter.Emit(model, c, navs, ctx);
            var path = Path.Combine(outDir, $"{model.Name}.{c.Name}.cs");
            File.WriteAllText(path, content, Utf8Bom);
            written.Add(path);
        }

        var contextContent = ContextEmitter.Emit(model, ctx);
        var contextPath = Path.Combine(outDir, $"{model.Name}.{model.Name}.cs");
        File.WriteAllText(contextPath, contextContent, Utf8Bom);
        written.Add(contextPath);

        // Sweep stale {Model}.{ClassName}.cs files where ClassName no longer exists in model.
        // Keep set: every class name in model + model.Name itself (context file).
        var keepNames = new HashSet<string>(model.Classes.Select(c => c.Name), StringComparer.Ordinal) { model.Name };
        var prefix = model.Name + ".";
        var deleted = new List<string>();
        foreach (var existing in Directory.EnumerateFiles(outDir, $"{model.Name}.*.cs"))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(existing);
            if (!nameNoExt.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var classPart = nameNoExt.Substring(prefix.Length);
            if (keepNames.Contains(classPart)) continue;
            try
            {
                File.Delete(existing);
                deleted.Add(existing);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[warn] Could not delete stale file {existing}: {ex.Message}");
            }
        }

        if (!skipInfo)
        {
            var infoPath = Path.Combine(outDir, $"{model.Name}.info");
            File.WriteAllText(infoPath, InfoEmitter.Content, Utf8Bom);
            written.Add(infoPath);
        }

        var viewPath = Path.Combine(outDir, $"{model.Name}.Diagram1.view");
        ViewWriter.WriteFile(model, viewPath);
        written.Add(viewPath);

        if (!skipDataContext)
        {
            var template = !string.IsNullOrEmpty(dataContextTemplatePath) && File.Exists(dataContextTemplatePath)
                ? File.ReadAllText(dataContextTemplatePath)
                : DataContextEmitter.DefaultTemplate;

            var vars = new Dictionary<string, string>
            {
                ["Model"] = model.Name,
                ["Namespace"] = model.Namespace,
                ["ContextClass"] = contextClass,
                ["Provider"] = provider
            };

            var dcPath = Path.Combine(outDir, $"{contextClass}.cs");
            if (File.Exists(dcPath))
            {
                Console.WriteLine($"[skip] {dcPath} already exists (user-edited file preserved)");
            }
            else
            {
                File.WriteAllText(dcPath, DataContextEmitter.Render(template, vars), Utf8Bom);
                written.Add(dcPath);
            }
        }

        return new GenCodeResult(written, deleted, warnings);
    }
}
