using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EfmlGen.Core;
using EfmlGen.Templates;
using EfmlGen.Xml;

namespace EfmlGen.Cli;

internal static class FileIO
{
    public static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static void Write(string path, string content) =>
        File.WriteAllText(path, content, Utf8Bom);
}

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "gen-code" => GenCode.Run(ParseArgs(args, 1)),
                "scaffold-efml" => ScaffoldEfml.Run(ParseArgs(args, 1)),
                "db-smoke" => SmokeTest.Run(ParseArgs(args, 1)),
                "--help" or "-h" or "help" => PrintHelp(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
            var inner = ex.InnerException;
            while (inner != null)
            {
                Console.Error.WriteLine($"  caused by: {inner.GetType().Name}: {inner.Message}");
                inner = inner.InnerException;
            }
            return 2;
        }
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: '{cmd}'. Run with --help to see available commands.");
        return 1;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            EfmlGen — generate EF Core .cs files from Devart Entity Developer .efml or from a database

            Commands:
              scaffold-efml   Read DB schema, write/merge .efml file (preserves p1:Guid + user renames)
              gen-code        Generate .cs files from an existing .efml
              db-smoke        Quick connection test, print table list

            Profile (all commands):
              --profile <name>         Load saved profile (skips repeating common flags).
                                       Default location: %AppData%\EfmlGen\profiles.json (saved by the WPF UI).
                                       Explicit CLI flags override profile values.
              --profile-file <path>    Override default profiles file location.

            scaffold-efml options:
              --conn <s>              Connection string (or use --conn-env)
              --conn-env <NAME>       Env var name holding connection string (avoid logging password)
              --provider <p>          Postgres | SqlServer (default: Postgres)
              --schemas <s1,s2>       DB schemas to scan (default: dbo)
              --tables <t1,t2>        Filter to specific table names (optional; default: all)
              --name <s>              Model name, e.g. CategoryEntities (required)
              --namespace <s>         C# namespace (required)
              --context-namespace <s> Context namespace (default: same as --namespace)
              --out <path>            Output .efml path (required). If exists, merges; pass --overwrite for fresh GUIDs.
              --overwrite             Discard existing efml, scaffold fresh
              --diagram-name <s>      Diagram file suffix (default: Diagram1). Output: {model}.{diagram-name}.view
              --skip-view             Do not write the .view diagram layout file
              --force-datetime        Map DB datetime columns to System.DateTime even if provider would use DateTimeOffset
              --file-base-name <s>    Stamp FileBaseName in efml (overrides filename when later generating .cs)

            gen-code options:
              --efml <path>                   Path to .efml file (required)
              --out <dir>                     Output directory (required)
              --provider <Npgsql|SqlServer>   EF Core provider (default: Npgsql)
              --connection-string <s>         Connection string baked into DbContext OnConfiguring (default: empty)
              --context-class <name>          Wrapper class name in DataContext.cs (default: {Model}DataContext)
              --datacontext-template <path>   User-provided DataContext template (default: built-in)
              --skip-datacontext              Do not generate DataContext.cs
              --skip-info                     Do not generate .info file
              --skip-view                     Do not generate .view diagram layout file
              --diagram-name <s>              Diagram file suffix (default: Diagram1)
              --timestamp <iso>               Override timestamp in file headers (for reproducible builds)
              --file-base-name <s>            Override filename prefix for output .cs files (default: efml filename)
              --force                         Generate even if CollisionDetector finds errors

            db-smoke options:
              --conn / --conn-env             Connection (same as scaffold-efml)
              --provider <p>                  Postgres | SqlServer (default: Postgres)
              --schemas <s1,s2>               Default: public,dbo for Postgres; dbo for SqlServer
              --tables <t1,t2>                Filter (client-side)
              --detail                        Print column-level details for first 3 tables

            Typical workflow:
              # 1. First time: scaffold .efml from DB
              efmlgen scaffold-efml --conn-env PG_CONN --schemas dbo \
                --tables "ConfigState,Department" \
                --name CategoryEntities --namespace MyApp.Data \
                --out Categories/CategoryEntities.efml

              # 2. Gen .cs files
              efmlgen gen-code --efml Categories/CategoryEntities.efml --out Categories/ \
                --context-class CategoryDataContext

              # 3. Re-scaffold after DB schema changes (merge: GUIDs + your renames preserved)
              efmlgen scaffold-efml --conn-env PG_CONN --schemas dbo \
                --tables "ConfigState,Department,NewTable" \
                --name CategoryEntities --namespace MyApp.Data \
                --out Categories/CategoryEntities.efml
              # → "Added classes: NewTable; Renamed classes: ConfigState → State"

              # 4. Re-gen .cs
              efmlgen gen-code --efml Categories/CategoryEntities.efml --out Categories/
            """);
        return 0;
    }

    private static Dictionary<string, string> ParseArgs(string[] args, int startIndex)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = startIndex; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--"))
                throw new ArgumentException($"Expected flag (--name), got positional arg: '{key}'. Run with --help for usage.");

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            {
                dict[key] = "true";
                continue;
            }

            dict[key] = args[++i];
        }
        return dict;
    }
}

internal static class GenCode
{
    public static int Run(Dictionary<string, string> opts)
    {
        ProfileResolver.ApplyProfile(opts);

        // Fill --out from profile's OutputDir (gen-code wants a directory).
        if (!opts.ContainsKey("--out")
            && opts.TryGetValue("--profile-output-dir", out var poDir) && !string.IsNullOrWhiteSpace(poDir))
        {
            opts["--out"] = poDir;
        }

        // Profile stores Provider as "Postgres"/"SqlServer"; gen-code expects EF Core provider name.
        if (opts.TryGetValue("--provider", out var pv) && string.Equals(pv, "Postgres", StringComparison.OrdinalIgnoreCase))
            opts["--provider"] = "Npgsql";

        var efmlPath = Required(opts, "--efml");
        var outDir = Required(opts, "--out");
        var provider = Optional(opts, "--provider") ?? "Npgsql";
        var connStr = Optional(opts, "--connection-string") ?? "";
        var skipDataContext = opts.ContainsKey("--skip-datacontext");
        var skipInfo = opts.ContainsKey("--skip-info");
        var skipView = opts.ContainsKey("--skip-view");
        var diagramName = Optional(opts, "--diagram-name") is { Length: > 0 } dn ? dn : "Diagram1";
        var dataContextTemplatePath = Optional(opts, "--datacontext-template");

        if (!File.Exists(efmlPath))
            throw new FileNotFoundException($".efml file not found: {efmlPath}");
        Directory.CreateDirectory(outDir);

        var model = EfmlReader.ReadFile(efmlPath);
        var fileBaseOverride = Optional(opts, "--file-base-name");
        if (!string.IsNullOrEmpty(fileBaseOverride))
            model.FileBaseName = fileBaseOverride!;
        var fileBase = EfmlPathing.ResolveFileBaseName(model, efmlPath);
        var contextClass = Optional(opts, "--context-class") ?? $"{model.Name}DataContext";

        var warnings = CollisionDetector.Validate(model);
        var hasError = WarningPrinter.Print(warnings);
        if (hasError && !opts.ContainsKey("--force"))
        {
            var errCount = warnings.Count(w => w.Severity == CollisionDetector.Severity.Error);
            Console.Error.WriteLine($"Aborted due to {errCount} error(s) above. Pass --force to generate anyway.");
            return 3;
        }

        var timestampStr = Optional(opts, "--timestamp");
        var timestamp = timestampStr != null
            ? DateTime.Parse(timestampStr, System.Globalization.CultureInfo.InvariantCulture)
            : DateTime.Now;

        var ctx = new GenerationContext
        {
            Timestamp = timestamp,
            Provider = provider,
            ConnectionString = connStr
        };

        var navsByClass = AssociationLayout.Build(model);
        var written = new List<string>();

        foreach (var c in model.Classes)
        {
            var navs = navsByClass.TryGetValue(c.Name, out var n) ? n : new();
            var content = EntityEmitter.Emit(model, c, navs, ctx);
            var path = Path.Combine(outDir, $"{fileBase}.{c.Name}.cs");
            FileIO.Write(path, content);
            written.Add(path);
        }

        var contextContent = ContextEmitter.Emit(model, ctx);
        var contextPath = Path.Combine(outDir, $"{fileBase}.{model.Name}.cs");
        FileIO.Write(contextPath, contextContent);
        written.Add(contextPath);

        if (!skipInfo)
        {
            var infoPath = Path.Combine(outDir, $"{fileBase}.info");
            FileIO.Write(infoPath, InfoEmitter.Content);
            written.Add(infoPath);
        }

        if (!skipView)
        {
            var viewPath = Path.Combine(outDir, $"{fileBase}.{diagramName}.view");
            ViewWriter.WriteFile(model, viewPath, diagramName);
            written.Add(viewPath);
        }

        if (!skipDataContext)
        {
            var template = dataContextTemplatePath != null
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
                Console.WriteLine($"[skip] {dcPath} already exists");
            }
            else
            {
                FileIO.Write(dcPath, DataContextEmitter.Render(template, vars));
                written.Add(dcPath);
            }
        }

        Console.WriteLine($"Generated {written.Count} files:");
        foreach (var p in written) Console.WriteLine($"  {p}");
        return 0;
    }

    private static string Required(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out var v)
            ? v
            : throw new ArgumentException($"Missing required option: {key}");

    private static string? Optional(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out var v) ? v : null;
}
