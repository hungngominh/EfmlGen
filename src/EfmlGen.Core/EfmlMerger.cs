using System;
using System.Collections.Generic;
using System.Linq;

namespace EfmlGen.Core;

/// <summary>
/// Merge model mới đọc từ DB với model cũ trong efml hiện tại.
/// Source-of-truth rules:
///   - DB là source-of-truth cho: type, nullable, default, sql-type, length, precision/scale, value-generated
///   - efml cũ là source-of-truth cho: p1:Guid, class.Name (user rename), property.Name (user rename),
///     ValidateMaxLength (custom validation), association.Name + end names
///
/// Identity keys (theo thứ tự ưu tiên):
///   - Class: match theo (Schema, unquoted Table)
///   - Property: match theo unquoted Column.Name
///   - Association: match theo Name → fallback (End1.Class, End1.PropertyName, End2.Class, End2.PropertyName)
/// </summary>
public static class EfmlMerger
{
    public sealed class MergeReport
    {
        public List<string> AddedClasses { get; } = new();
        public List<string> RemovedClasses { get; } = new();
        public List<string> RenamedClasses { get; } = new();           // "oldName → newName"
        public List<string> AddedProperties { get; } = new();          // "Class.Property"
        public List<string> RemovedProperties { get; } = new();
        public List<string> RenamedProperties { get; } = new();
        public List<string> AddedAssociations { get; } = new();
        public List<string> RemovedAssociations { get; } = new();
        public bool HasChanges =>
            AddedClasses.Count + RemovedClasses.Count + RenamedClasses.Count +
            AddedProperties.Count + RemovedProperties.Count + RenamedProperties.Count +
            AddedAssociations.Count + RemovedAssociations.Count > 0;
    }

    public static (EfmlModel merged, MergeReport report) Merge(EfmlModel fromDb, EfmlModel existing)
    {
        var report = new MergeReport();

        // Top-level: keep existing model-level Guid + Name + namespaces
        fromDb.Guid = existing.Guid;
        if (!string.IsNullOrEmpty(existing.Name)) fromDb.Name = existing.Name;
        if (!string.IsNullOrEmpty(existing.Namespace)) fromDb.Namespace = existing.Namespace;
        if (!string.IsNullOrEmpty(existing.ContextNamespace)) fromDb.ContextNamespace = existing.ContextNamespace;

        var oldByKey = existing.Classes.ToDictionary(c => ClassKey(c), c => c, StringComparer.OrdinalIgnoreCase);
        // Fallback index by unqualified table name. Legacy efml files often omit the
        // schema attribute on <class> elements, or stamp "dbo" even on Postgres DBs where
        // tables actually live in "public". Without this fallback the merger would treat
        // every legacy class as Removed and every DB-derived class as Added, losing all
        // Guid + custom-rename preservation.
        var oldByTable = new Dictionary<string, EfClass>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in existing.Classes)
        {
            var tbl = Unquote(c.Table);
            if (!oldByTable.ContainsKey(tbl)) oldByTable[tbl] = c;
        }
        var newKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var newClass in fromDb.Classes)
        {
            var key = ClassKey(newClass);
            newKeys.Add(key);

            if (!oldByKey.TryGetValue(key, out var oldClass)
                && !oldByTable.TryGetValue(Unquote(newClass.Table), out oldClass))
            {
                report.AddedClasses.Add(newClass.Name);
                continue;
            }
            matchedOldKeys.Add(ClassKey(oldClass));

            // Reuse class identity
            newClass.Guid = oldClass.Guid;
            if (!string.Equals(oldClass.Name, newClass.Name, StringComparison.Ordinal))
            {
                report.RenamedClasses.Add($"{newClass.Name} → {oldClass.Name}");
                newClass.Name = oldClass.Name;
                newClass.EntitySet = oldClass.EntitySet;
            }

            // Merge id + properties by column-name
            var allOldProps = new List<EfProperty>();
            if (oldClass.Id != null!) allOldProps.Add(oldClass.Id);
            allOldProps.AddRange(oldClass.Properties);
            var oldPropByCol = allOldProps.ToDictionary(p => Unquote(p.Column.Name), p => p, StringComparer.OrdinalIgnoreCase);

            var allNewProps = new List<EfProperty>();
            if (newClass.Id != null!) allNewProps.Add(newClass.Id);
            allNewProps.AddRange(newClass.Properties);

            var newColKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var newProp in allNewProps)
            {
                var colKey = Unquote(newProp.Column.Name);
                newColKeys.Add(colKey);

                if (!oldPropByCol.TryGetValue(colKey, out var oldProp))
                {
                    report.AddedProperties.Add($"{newClass.Name}.{newProp.Name}");
                    continue;
                }

                newProp.Guid = oldProp.Guid;
                if (!string.Equals(oldProp.Name, newProp.Name, StringComparison.Ordinal))
                {
                    report.RenamedProperties.Add($"{newClass.Name}.{newProp.Name} → {oldProp.Name}");
                    newProp.Name = oldProp.Name;
                }
                // Preserve user-set ValidateMaxLength (chỉ giữ nếu user set, không lấy lại nếu DB không có length)
                if (oldProp.ValidateMaxLength.HasValue && !newProp.ValidateMaxLength.HasValue)
                    newProp.ValidateMaxLength = oldProp.ValidateMaxLength;
            }

            foreach (var oldColKey in oldPropByCol.Keys)
            {
                if (!newColKeys.Contains(oldColKey))
                    report.RemovedProperties.Add($"{newClass.Name}.{oldPropByCol[oldColKey].Name} (column {oldColKey})");
            }
        }

        foreach (var oldKey in oldByKey.Keys)
        {
            // A class is removed only if neither the full key nor the table-name fallback matched it.
            if (!newKeys.Contains(oldKey) && !matchedOldKeys.Contains(oldKey))
                report.RemovedClasses.Add(oldByKey[oldKey].Name + $" ({oldKey})");
        }

        // Associations: match by Name first, then by structural fingerprint
        var oldAssocByName = existing.Associations.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
        var oldAssocByFingerprint = existing.Associations.ToDictionary(AssocFingerprint, a => a, StringComparer.OrdinalIgnoreCase);
        var newAssocNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var newAssoc in fromDb.Associations)
        {
            EfAssociation? oldAssoc = null;
            if (oldAssocByName.TryGetValue(newAssoc.Name, out var byName))
                oldAssoc = byName;
            else if (oldAssocByFingerprint.TryGetValue(AssocFingerprint(newAssoc), out var byFp))
                oldAssoc = byFp;

            if (oldAssoc == null)
            {
                report.AddedAssociations.Add(newAssoc.Name);
                continue;
            }

            newAssocNames.Add(oldAssoc.Name);

            // Reuse identity
            newAssoc.Guid = oldAssoc.Guid;
            newAssoc.Name = oldAssoc.Name;
            newAssoc.End1.Guid = oldAssoc.End1.Guid;
            newAssoc.End1.Name = oldAssoc.End1.Name;
            newAssoc.End2.Guid = oldAssoc.End2.Guid;
            newAssoc.End2.Name = oldAssoc.End2.Name;
        }

        foreach (var oldAssoc in existing.Associations)
        {
            if (!newAssocNames.Contains(oldAssoc.Name))
                report.RemovedAssociations.Add(oldAssoc.Name);
        }

        return (fromDb, report);
    }

    private static string ClassKey(EfClass c) =>
        $"{c.Schema}|{Unquote(c.Table)}";

    private static string AssocFingerprint(EfAssociation a) =>
        $"{a.End1.ClassName}|{string.Join(",", a.End1.PropertyNames)}|{a.End2.ClassName}|{string.Join(",", a.End2.PropertyNames)}";

    private static string Unquote(string s) =>
        s.Length >= 2 && s.StartsWith('`') && s.EndsWith('`') ? s[1..^1] : s;
}
