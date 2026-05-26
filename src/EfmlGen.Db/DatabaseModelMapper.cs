using System;
using System.Linq;
using EfmlGen.Core;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace EfmlGen.Db;

/// <summary>
/// Map từ EF Core's DatabaseModel sang EfmlGen.Core.EfmlModel.
/// MVP: hỗ trợ single-column PK, FK 1:N. Composite PK / M:N junction → warning + skip relationship.
/// </summary>
public static class DatabaseModelMapper
{
    public sealed class MapOptions
    {
        public string Name { get; init; } = "Model";
        public string Namespace { get; init; } = "Generated";
        public string ContextNamespace { get; init; } = "Generated";

        /// <summary>
        /// Type-map selection. Must match the provider used to read the schema.
        /// </summary>
        public DbProvider Provider { get; init; } = DbProvider.Postgres;

        /// <summary>
        /// When true, timezone-aware types are stripped of their offset:
        ///   Postgres: timestamptz → DateTime, timetz → TimeSpan.
        ///   SQL Server: datetimeoffset → DateTime.
        /// Default false (timezone preserved as DateTimeOffset).
        /// </summary>
        public bool ForceDateTime { get; init; } = false;

        /// <summary>How to derive entity class names from table names. Default: Preserve.</summary>
        public EntityNaming EntityNaming { get; init; } = EntityNaming.Preserve;

        /// <summary>How to derive the reverse (collection) nav property name. Default: Preserve.</summary>
        public RelationshipNaming RelationshipNaming { get; init; } = RelationshipNaming.Preserve;
    }

    public enum EntityNaming { Preserve, Singular, Plural }
    public enum RelationshipNaming { Preserve, Pluralize, Suffix }

    public static EfmlModel Map(DatabaseModel dbModel, MapOptions opt)
    {
        var model = new EfmlModel
        {
            Name = opt.Name,
            Namespace = opt.Namespace,
            ContextNamespace = opt.ContextNamespace,
            Guid = Guid.NewGuid()
        };

        // table.Name → class name (after EntityNaming transform). Used by association mapping
        // to resolve principal/dependent class references.
        var tableToClassName = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var table in dbModel.Tables)
        {
            var cls = MapClass(table, opt);
            tableToClassName[table.Name] = cls.Name;
            model.Classes.Add(cls);
        }

        // Build associations, dedup by structural fingerprint
        // (Postgres metadata occasionally returns the same logical FK multiple times)
        var seenFingerprints = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var table in dbModel.Tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                // Skip FK whose principal table is not in the included class set
                // (e.g. user filtered tables via --tables and the target was excluded).
                if (!tableToClassName.ContainsKey(fk.PrincipalTable.Name)) continue;

                var assoc = MapAssociation(table, fk, tableToClassName, opt);
                if (assoc == null) continue;

                var fp = $"{assoc.End1.ClassName}|{string.Join(",", assoc.End1.PropertyNames)}|{assoc.End2.ClassName}|{string.Join(",", assoc.End2.PropertyNames)}";
                if (!seenFingerprints.Add(fp)) continue;  // skip duplicate

                model.Associations.Add(assoc);
            }
        }

        SimplifyEndNames(model);

        return model;
    }

    private static string ApplyEntityNaming(string tableName, EntityNaming naming) => naming switch
    {
        EntityNaming.Singular => Inflector.Singularize(tableName),
        EntityNaming.Plural => Inflector.Pluralize(tableName),
        _ => tableName
    };

    private static string ApplyRelationshipNaming(string baseName, RelationshipNaming naming) => naming switch
    {
        RelationshipNaming.Pluralize => Inflector.Pluralize(baseName),
        RelationshipNaming.Suffix => baseName + "List",
        _ => baseName
    };

    /// <summary>
    /// Match ED behavior: for an ordered (principal, dependent) pair with a single FK
    /// and no self-reference, use the target class name directly instead of the
    /// disambiguated `Class_FK` / `Class_FK1` form.
    /// </summary>
    private static void SimplifyEndNames(EfmlModel model)
    {
        var groups = model.Associations
            .GroupBy(a => (a.End1.ClassName, a.End2.ClassName))
            .ToList();

        foreach (var grp in groups)
        {
            var isSelfRef = grp.Key.Item1 == grp.Key.Item2;
            if (isSelfRef) continue;
            if (grp.Count() > 1) continue;

            var a = grp.First();
            a.End1.Name = a.End1.ClassName;
            a.End2.Name = a.End2.ClassName;
        }
    }

    private static EfClass MapClass(DatabaseTable table, MapOptions opt)
    {
        var cls = new EfClass
        {
            Name = ApplyEntityNaming(table.Name, opt.EntityNaming),
            EntitySet = table.Name,
            Table = $"`{table.Name}`",
            Schema = table.Schema ?? "",
            Guid = Guid.NewGuid()
        };

        var pkCols = table.PrimaryKey?.Columns ?? new System.Collections.Generic.List<DatabaseColumn>();

        foreach (var col in table.Columns)
        {
            var prop = MapProperty(col, opt);
            if (pkCols.Count == 1 && pkCols[0] == col)
                cls.Id = prop;
            else
                cls.Properties.Add(prop);
        }

        // No PK case: synthesize from first column to avoid null Id
        if (cls.Id == null! && cls.Properties.Count > 0)
        {
            cls.Id = cls.Properties[0];
            cls.Properties.RemoveAt(0);
        }

        // Indexes (skip the PK index — already represented by EfClass.Id)
        foreach (var idx in table.Indexes)
        {
            if (IsPrimaryKeyIndex(idx, table)) continue;
            var ei = new EfIndex
            {
                Name = idx.Name ?? "",
                IsUnique = idx.IsUnique
            };
            foreach (var c in idx.Columns)
                ei.ColumnNames.Add(c.Name);
            if (ei.ColumnNames.Count > 0)
                cls.Indexes.Add(ei);
        }

        return cls;
    }

    private static bool IsPrimaryKeyIndex(DatabaseIndex idx, DatabaseTable table)
    {
        var pk = table.PrimaryKey;
        if (pk == null) return false;
        if (pk.Columns.Count != idx.Columns.Count) return false;
        for (int i = 0; i < pk.Columns.Count; i++)
            if (!string.Equals(pk.Columns[i].Name, idx.Columns[i].Name, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private readonly record struct MappedType(
        EfType EfType, string? SqlType, int? Length, int? Precision, int? Scale, bool Unicode);

    private static MappedType MapStoreType(string storeType, MapOptions opt)
    {
        if (opt.Provider == DbProvider.SqlServer)
        {
            var s = SqlServerTypeMap.Map(storeType, opt.ForceDateTime);
            return new MappedType(s.EfType, s.SqlType, s.Length, s.Precision, s.Scale, s.Unicode);
        }
        var p = PostgresTypeMap.Map(storeType, opt.ForceDateTime);
        return new MappedType(p.EfType, p.SqlType, p.Length, p.Precision, p.Scale, p.Unicode);
    }

    private static EfProperty MapProperty(DatabaseColumn col, MapOptions opt)
    {
        var t = MapStoreType(col.StoreType ?? "", opt);
        var isRowVersion = IsRowVersionColumn(col, opt);
        var valueGenerated = col.ValueGenerated switch
        {
            ValueGenerated.OnAdd => "OnAdd",
            ValueGenerated.OnAddOrUpdate => "OnAddOrUpdate",
            _ => null
        };
        // Computed columns are server-generated on add+update
        if (!string.IsNullOrEmpty(col.ComputedColumnSql) && valueGenerated == null)
            valueGenerated = "OnAddOrUpdate";
        if (isRowVersion && valueGenerated == null)
            valueGenerated = "OnAddOrUpdate";

        return new EfProperty
        {
            Name = col.Name,
            Type = t.EfType,
            IsNullable = col.IsNullable,
            ValueGenerated = valueGenerated,
            ValidateRequired = !col.IsNullable,
            ValidateMaxLength = t.Length,
            Guid = Guid.NewGuid(),
            IsConcurrencyToken = isRowVersion,
            IsRowVersion = isRowVersion,
            Column = new EfColumn
            {
                Name = $"`{col.Name}`",
                NotNull = !col.IsNullable,
                Default = col.DefaultValueSql,
                Computed = col.ComputedColumnSql,
                SqlType = t.SqlType,
                Length = t.Length,
                Precision = t.Precision,
                Scale = t.Scale,
                Unicode = t.Unicode
            }
        };
    }

    /// <summary>
    /// Detect rowversion/timestamp columns. EF Core scaffolder usually sets
    /// ValueGenerated=OnAddOrUpdate AND IsRowVersion via store type, but providers
    /// differ — fall back to store type sniffing.
    /// </summary>
    private static bool IsRowVersionColumn(DatabaseColumn col, MapOptions opt)
    {
        if (opt.Provider != DbProvider.SqlServer) return false;
        var st = (col.StoreType ?? "").ToLowerInvariant();
        return st == "rowversion" || st == "timestamp";
    }

    private static EfAssociation? MapAssociation(
        DatabaseTable dependentTable,
        DatabaseForeignKey fk,
        System.Collections.Generic.Dictionary<string, string> tableToClassName,
        MapOptions opt)
    {
        if (fk.Columns.Count == 0 || fk.PrincipalColumns.Count == 0) return null;
        if (fk.Columns.Count != fk.PrincipalColumns.Count) return null;  // malformed

        var principalTable = fk.PrincipalTable;
        var firstFkCol = fk.Columns[0];

        var principalClassName = tableToClassName.TryGetValue(principalTable.Name, out var pcn) ? pcn : principalTable.Name;
        var dependentClassName = tableToClassName.TryGetValue(dependentTable.Name, out var dcn) ? dcn : dependentTable.Name;

        var assocName = string.IsNullOrEmpty(fk.Name)
            ? $"{principalClassName}_{dependentClassName}"
            : fk.Name;

        // Required if all FK columns are non-nullable
        var allRequired = fk.Columns.All(c => !c.IsNullable);
        var dependentMult = allRequired ? Multiplicity.One : Multiplicity.ZeroOrOne;

        // One-to-one: FK column(s) match the dependent table's primary key exactly
        // (i.e. dependent PK *is* the FK → at most one dependent row per principal).
        var isOneToOne = IsOneToOneFk(dependentTable, fk);
        var cardinality = isOneToOne ? Cardinality.OneToOne : Cardinality.OneToMany;
        var principalMult = isOneToOne ? Multiplicity.Many : Multiplicity.Many;
        // For 1:1, the principal end is also non-collection (One/ZeroOrOne).
        if (isOneToOne) principalMult = allRequired ? Multiplicity.One : Multiplicity.ZeroOrOne;

        // CascadeDelete from the FK's delete rule
        var cascadeDelete = fk.OnDelete == ReferentialAction.Cascade;

        // Name suffix uses the first FK column (matches ED single-column behavior).
        // For composite FK we keep the same naming since the property element list disambiguates.
        var nameSuffix = firstFkCol.Name;

        // Reverse-nav (the collection side) naming. By default we keep the legacy
        // `Class_FK1` form for backward compat with existing .efml files.
        var reverseBase = $"{dependentClassName}_{nameSuffix}1";
        var reverseNavName = isOneToOne
            ? reverseBase
            : ApplyRelationshipNaming(reverseBase, opt.RelationshipNaming);

        var assoc = new EfAssociation
        {
            Name = assocName,
            Cardinality = cardinality,
            Guid = Guid.NewGuid(),
            CascadeDelete = cascadeDelete,
            End1 = new EfAssociationEnd
            {
                Multiplicity = dependentMult,
                Name = $"{principalClassName}_{nameSuffix}",
                ClassName = principalClassName,
                RelationClass = principalClassName,
                Constrained = true,
                Lazy = false,
                Guid = Guid.NewGuid()
            },
            End2 = new EfAssociationEnd
            {
                Multiplicity = isOneToOne ? Multiplicity.One : Multiplicity.Many,
                Name = reverseNavName,
                ClassName = dependentClassName,
                RelationClass = dependentClassName,
                Constrained = false,
                Lazy = false,
                Guid = Guid.NewGuid()
            }
        };

        foreach (var pc in fk.PrincipalColumns)
            assoc.End1.PropertyNames.Add(pc.Name);
        foreach (var fc in fk.Columns)
            assoc.End2.PropertyNames.Add(fc.Name);

        return assoc;
    }

    private static bool IsOneToOneFk(DatabaseTable dependentTable, DatabaseForeignKey fk)
    {
        var pk = dependentTable.PrimaryKey;
        if (pk == null || pk.Columns.Count == 0) return false;
        if (pk.Columns.Count != fk.Columns.Count) return false;

        var pkSet = new System.Collections.Generic.HashSet<string>(
            pk.Columns.Select(c => c.Name), System.StringComparer.OrdinalIgnoreCase);
        return fk.Columns.All(c => pkSet.Contains(c.Name));
    }
}
