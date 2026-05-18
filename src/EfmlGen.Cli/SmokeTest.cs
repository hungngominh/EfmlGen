using System;
using System.Linq;
using EfmlGen.Db;

namespace EfmlGen.Cli;

internal static class SmokeTest
{
    public static int Run(System.Collections.Generic.Dictionary<string, string> opts)
    {
        ProfileResolver.ApplyProfile(opts);
        var connStr = ScaffoldEfml.ResolveConnectionString(opts);

        var providerStr = opts.TryGetValue("--provider", out var pv) ? pv : "Postgres";
        var provider = ScaffoldEfml.ParseProvider(providerStr);

        var schemaArg = opts.TryGetValue("--schemas", out var s) ? s : (provider == DbProvider.SqlServer ? "dbo" : "public,dbo");
        var schemas = schemaArg.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var tablesArg = opts.TryGetValue("--tables", out var t) ? t : null;
        var tables = tablesArg?.Split(',', StringSplitOptions.RemoveEmptyEntries);

        Console.WriteLine($"Connecting ({provider}, schemas: {string.Join(",", schemas)}, tables: {(tables == null ? "all" : string.Join(",", tables))})...");

        var model = DatabaseSchemaReader.Read(
            connStr,
            provider,
            new SchemaReadOptions(Schemas: schemas, Tables: tables));

        Console.WriteLine($"Connected. DB: {model.DatabaseName}");
        Console.WriteLine($"Tables: {model.Tables.Count}");

        if (opts.ContainsKey("--detail"))
        {
            foreach (var tbl in model.Tables.Take(3))
            {
                Console.WriteLine();
                Console.WriteLine($"=== {tbl.Schema}.{tbl.Name} ===");
                Console.WriteLine($"  Comment: {tbl.Comment}");
                Console.WriteLine($"  PK: {(tbl.PrimaryKey?.Columns.Count.ToString() ?? "none")} cols");
                if (tbl.PrimaryKey != null)
                    Console.WriteLine($"     [{string.Join(", ", tbl.PrimaryKey.Columns.Select(c => c.Name))}]");
                Console.WriteLine($"  Columns ({tbl.Columns.Count}):");
                foreach (var col in tbl.Columns.Take(10))
                {
                    Console.WriteLine($"    {col.Name,-30} {col.StoreType,-25} null={col.IsNullable} default={col.DefaultValueSql ?? "-"} valGen={col.ValueGenerated}");
                }
                if (tbl.Columns.Count > 10) Console.WriteLine($"    ... and {tbl.Columns.Count - 10} more");

                Console.WriteLine($"  ForeignKeys ({tbl.ForeignKeys.Count}):");
                foreach (var fk in tbl.ForeignKeys)
                {
                    Console.WriteLine($"    {fk.Name}: [{string.Join(",", fk.Columns.Select(c => c.Name))}] -> {fk.PrincipalTable.Schema}.{fk.PrincipalTable.Name}.[{string.Join(",", fk.PrincipalColumns.Select(c => c.Name))}]");
                }
            }
        }
        else
        {
            foreach (var tbl in model.Tables.Take(20))
                Console.WriteLine($"  {tbl.Schema}.{tbl.Name}  ({tbl.Columns.Count} cols, {tbl.ForeignKeys.Count} FKs)");
            if (model.Tables.Count > 20)
                Console.WriteLine($"  ... and {model.Tables.Count - 20} more");
        }

        return 0;
    }
}
