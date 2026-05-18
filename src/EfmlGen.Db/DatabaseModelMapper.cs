using System;
using System.Linq;
using EfmlGen.Core;
using Microsoft.EntityFrameworkCore.Metadata;
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
    }

    public static EfmlModel Map(DatabaseModel dbModel, MapOptions opt)
    {
        var model = new EfmlModel
        {
            Name = opt.Name,
            Namespace = opt.Namespace,
            ContextNamespace = opt.ContextNamespace,
            Guid = Guid.NewGuid()
        };

        foreach (var table in dbModel.Tables)
            model.Classes.Add(MapClass(table, opt));

        var includedClasses = new System.Collections.Generic.HashSet<string>(
            model.Classes.Select(c => c.Name), System.StringComparer.Ordinal);

        // Build associations, dedup by structural fingerprint
        // (Postgres metadata occasionally returns the same logical FK multiple times)
        var seenFingerprints = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var table in dbModel.Tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                // Skip FK whose principal table is not in the included class set
                // (e.g. user filtered tables via --tables and the target was excluded).
                if (!includedClasses.Contains(fk.PrincipalTable.Name)) continue;

                var assoc = MapAssociation(table, fk);
                if (assoc == null) continue;

                var fp = $"{assoc.End1.ClassName}|{assoc.End1.PropertyName}|{assoc.End2.ClassName}|{assoc.End2.PropertyName}";
                if (!seenFingerprints.Add(fp)) continue;  // skip duplicate

                model.Associations.Add(assoc);
            }
        }

        SimplifyEndNames(model);

        return model;
    }

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
            Name = table.Name,
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

        return cls;
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

        return new EfProperty
        {
            Name = col.Name,
            Type = t.EfType,
            IsNullable = col.IsNullable,
            ValueGenerated = col.ValueGenerated switch
            {
                ValueGenerated.OnAdd => "OnAdd",
                ValueGenerated.OnAddOrUpdate => "OnAddOrUpdate",
                _ => null
            },
            ValidateRequired = !col.IsNullable,
            ValidateMaxLength = t.Length,
            Guid = Guid.NewGuid(),
            Column = new EfColumn
            {
                Name = $"`{col.Name}`",
                NotNull = !col.IsNullable,
                Default = col.DefaultValueSql,
                SqlType = t.SqlType,
                Length = t.Length,
                Precision = t.Precision,
                Scale = t.Scale,
                Unicode = t.Unicode
            }
        };
    }

    private static EfAssociation? MapAssociation(DatabaseTable dependentTable, DatabaseForeignKey fk)
    {
        // MVP: skip composite FK
        if (fk.Columns.Count != 1 || fk.PrincipalColumns.Count != 1)
            return null;

        var fkCol = fk.Columns[0];
        var principalTable = fk.PrincipalTable;
        var principalCol = fk.PrincipalColumns[0];

        var assocName = string.IsNullOrEmpty(fk.Name)
            ? $"{principalTable.Name}_{dependentTable.Name}"
            : fk.Name;

        // Determine multiplicity
        var dependentMult = fkCol.IsNullable ? Multiplicity.ZeroOrOne : Multiplicity.One;

        return new EfAssociation
        {
            Name = assocName,
            Cardinality = Cardinality.OneToMany,
            Guid = Guid.NewGuid(),
            End1 = new EfAssociationEnd
            {
                Multiplicity = dependentMult,
                Name = $"{principalTable.Name}_{fkCol.Name}",
                ClassName = principalTable.Name,
                RelationClass = principalTable.Name,
                Constrained = true,
                Lazy = false,
                Guid = Guid.NewGuid(),
                PropertyName = principalCol.Name
            },
            End2 = new EfAssociationEnd
            {
                Multiplicity = Multiplicity.Many,
                Name = $"{dependentTable.Name}_{fkCol.Name}1",
                ClassName = dependentTable.Name,
                RelationClass = dependentTable.Name,
                Constrained = false,
                Lazy = false,
                Guid = Guid.NewGuid(),
                PropertyName = fkCol.Name
            }
        };
    }
}
