using System.Collections.Generic;
using System.Linq;

namespace EfmlGen.Core;

/// <summary>
/// Phát hiện collision potential trong EfmlModel mà KHÔNG tự rename.
/// Chỉ log warning để user tự xử lý trong efml.
/// User wants: "tên ở DB sao thì giữ nguyên ở code vậy không refine" — chỉ detect.
/// </summary>
public static class CollisionDetector
{
    public enum Severity { Warning, Error }

    public sealed record Warning(Severity Severity, string Message);

    /// <summary>
    /// DbContext members mà DbSet không được trùng tên — sẽ shadow/override.
    /// </summary>
    private static readonly HashSet<string> DbContextReservedMembers = new(System.StringComparer.Ordinal)
    {
        "ChangeTracker", "Configuration", "Database", "Model", "Set", "ContextId",
        "Add", "AddAsync", "AddRange", "AddRangeAsync", "Attach", "AttachRange",
        "Entry", "Find", "FindAsync", "Remove", "RemoveRange", "Update", "UpdateRange",
        "SaveChanges", "SaveChangesAsync", "Dispose", "DisposeAsync", "Equals",
        "GetHashCode", "GetType", "ToString",
        // Names ED uses in generated context
        "OnCreated", "OnConfiguring", "OnModelCreating", "HasChanges",
        "CustomizeConfiguration", "CustomizeMapping", "RelationshipsMapping"
    };

    public static IReadOnlyList<Warning> Validate(EfmlModel model)
    {
        var warnings = new List<Warning>();

        // 1. Class names colliding with each other (after schema strip)
        var classNameCounts = model.Classes
            .GroupBy(c => c.Name, System.StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var g in classNameCounts)
        {
            warnings.Add(new Warning(Severity.Error,
                $"Class name '{g.Key}' is used by {g.Count()} classes — won't compile. Rename in efml."));
        }

        // 2. DbSet entity-set colliding with DbContext built-in members
        foreach (var c in model.Classes)
        {
            if (DbContextReservedMembers.Contains(c.EntitySet))
            {
                warnings.Add(new Warning(Severity.Warning,
                    $"Class '{c.Name}' entity-set '{c.EntitySet}' shadows DbContext member — may break EF behavior. Rename entity-set in efml."));
            }
        }

        // 3. Property name == class name (forbidden in C#)
        foreach (var c in model.Classes)
        {
            foreach (var p in c.AllProperties)
            {
                if (string.Equals(p.Name, c.Name, System.StringComparison.Ordinal))
                {
                    warnings.Add(new Warning(Severity.Error,
                        $"Property '{c.Name}.{p.Name}' has same name as its class — won't compile. Rename in efml."));
                }
            }
        }

        // 4. Duplicate property names within a class (col + nav, or 2 cols)
        foreach (var c in model.Classes)
        {
            var navNames = model.Associations
                .SelectMany(a => GetNavNamesOnClass(a, c.Name))
                .ToList();

            var allNames = c.AllProperties.Select(p => p.Name).Concat(navNames);
            var dups = allNames.GroupBy(n => n, System.StringComparer.Ordinal).Where(g => g.Count() > 1);
            foreach (var g in dups)
            {
                warnings.Add(new Warning(Severity.Error,
                    $"Class '{c.Name}' has {g.Count()} members named '{g.Key}' — won't compile. Rename in efml."));
            }
        }

        // 5. Reserved C# keyword as identifier — informational (emitter will @-escape automatically)
        foreach (var c in model.Classes)
        {
            if (CsKeywords.IsReserved(c.Name))
                warnings.Add(new Warning(Severity.Warning,
                    $"Class '{c.Name}' is a C# reserved keyword — will be emitted as '@{c.Name}'. Consider renaming class in efml."));
            foreach (var p in c.AllProperties)
            {
                if (CsKeywords.IsReserved(p.Name))
                    warnings.Add(new Warning(Severity.Warning,
                        $"Property '{c.Name}.{p.Name}' is a C# reserved keyword — will be emitted as '@{p.Name}'."));
            }
        }

        // 6. Empty/whitespace names
        foreach (var c in model.Classes)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
                warnings.Add(new Warning(Severity.Error, $"Class with table '{c.Table}' has empty Name."));
            foreach (var p in c.AllProperties)
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                    warnings.Add(new Warning(Severity.Error, $"Class '{c.Name}' has property with empty Name (column '{p.Column.Name}')."));
            }
        }

        return warnings;
    }

    private static IEnumerable<string> GetNavNamesOnClass(EfAssociation a, string className)
    {
        if (a.End1.ClassName == className) yield return a.End2.Name;
        if (a.End2.ClassName == className) yield return a.End1.Name;
    }
}
