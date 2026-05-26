using System;
using System.Collections.Generic;
using System.IO;

namespace EfmlGen.Core;

public sealed class EfmlModel
{
    public string ContextNamespace { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid Guid { get; set; }
    /// <summary>
    /// Optional override for the generated .cs filename prefix. Empty means derive from
    /// the .efml filename (matches Entity Developer's default behavior).
    /// </summary>
    public string FileBaseName { get; set; } = "";
    public List<EfClass> Classes { get; } = new();
    public List<EfAssociation> Associations { get; } = new();
}

public static class EfmlPathing
{
    public static string ResolveFileBaseName(EfmlModel model, string efmlPath) =>
        !string.IsNullOrEmpty(model.FileBaseName)
            ? model.FileBaseName
            : Path.GetFileNameWithoutExtension(efmlPath);
}

public sealed class EfClass
{
    public string Name { get; set; } = "";
    public string EntitySet { get; set; } = "";
    public string Table { get; set; } = "";
    public string Schema { get; set; } = "";
    public Guid Guid { get; set; }
    public EfProperty Id { get; set; } = null!;
    public List<EfProperty> Properties { get; } = new();
    public List<EfIndex> Indexes { get; } = new();

    public IEnumerable<EfProperty> AllProperties
    {
        get
        {
            yield return Id;
            foreach (var p in Properties) yield return p;
        }
    }
}

public sealed class EfIndex
{
    public string Name { get; set; } = "";
    public bool IsUnique { get; set; }
    public List<string> ColumnNames { get; } = new();
}

public sealed class EfProperty
{
    public string Name { get; set; } = "";
    public EfType Type { get; set; }
    public bool IsNullable { get; set; }
    public string? ValueGenerated { get; set; }
    public bool ValidateRequired { get; set; }
    public int? ValidateMaxLength { get; set; }
    public Guid Guid { get; set; }
    public bool IsConcurrencyToken { get; set; }
    public bool IsRowVersion { get; set; }
    public EfColumn Column { get; set; } = new();
}

public sealed class EfColumn
{
    public string Name { get; set; } = "";
    public bool NotNull { get; set; }
    public string? Default { get; set; }
    public string? Computed { get; set; }
    public string? SqlType { get; set; }
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool Unicode { get; set; }
}

public sealed class EfAssociation
{
    public string Name { get; set; } = "";
    public Cardinality Cardinality { get; set; }
    public Guid Guid { get; set; }
    public bool CascadeDelete { get; set; }
    public EfAssociationEnd End1 { get; set; } = new();
    public EfAssociationEnd End2 { get; set; } = new();
}

public sealed class EfAssociationEnd
{
    public Multiplicity Multiplicity { get; set; }
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string RelationClass { get; set; } = "";
    public bool Constrained { get; set; }
    public bool Lazy { get; set; }
    public Guid Guid { get; set; }
    public List<string> PropertyNames { get; } = new();

    /// <summary>
    /// Convenience accessor for the first property name. For composite FKs use <see cref="PropertyNames"/>.
    /// </summary>
    public string PropertyName
    {
        get => PropertyNames.Count > 0 ? PropertyNames[0] : "";
        set
        {
            PropertyNames.Clear();
            if (!string.IsNullOrEmpty(value)) PropertyNames.Add(value);
        }
    }
}

public enum EfType
{
    Int16,
    Int32,
    Int64,
    String,
    Boolean,
    DateTime,
    DateTimeOffset,
    TimeSpan,
    Decimal,
    Double,
    Single,
    Byte,
    Guid,
    Blob
}

public enum Cardinality
{
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

public enum Multiplicity
{
    One,
    ZeroOrOne,
    Many
}
