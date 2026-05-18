using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using EfmlGen.Core;

namespace EfmlGen.Xml;

public static class EfmlReader
{
    private static readonly XNamespace P1 = "http://devart.com/schemas/EntityDeveloper/1.0";

    public static EfmlModel ReadFile(string path)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException($"Failed to parse .efml as XML ({path}): {ex.Message}", ex);
        }
        return Read(doc, path);
    }

    public static EfmlModel Read(XDocument doc, string? sourcePath = null)
    {
        var src = sourcePath != null ? $" ({sourcePath})" : "";
        var root = doc.Root ?? throw new InvalidDataException($"Empty .efml document{src}");
        if (root.Name.LocalName != "efcore")
            throw new InvalidDataException($"Expected root <efcore> in .efml{src}, got <{root.Name.LocalName}>");

        var model = new EfmlModel
        {
            ContextNamespace = root.Attribute("context-namespace")?.Value ?? "",
            Namespace = root.Attribute("namespace")?.Value ?? "",
            Name = root.Attribute(P1 + "name")?.Value ?? "",
            Guid = ParseGuid(root.Attribute(P1 + "Guid")?.Value)
        };

        foreach (var classEl in root.Elements("class"))
            model.Classes.Add(ReadClass(classEl));

        var assocs = root.Element("associations");
        if (assocs != null)
        {
            foreach (var aEl in assocs.Elements("association"))
                model.Associations.Add(ReadAssociation(aEl));
        }

        return model;
    }

    private static EfClass ReadClass(XElement el)
    {
        var c = new EfClass
        {
            Name = el.Attribute("name")?.Value ?? "",
            EntitySet = el.Attribute("entity-set")?.Value ?? "",
            Table = el.Attribute("table")?.Value ?? "",
            Schema = el.Attribute("schema")?.Value ?? "",
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value)
        };

        var idEl = el.Element("id");
        if (idEl != null) c.Id = ReadProperty(idEl);

        foreach (var pEl in el.Elements("property"))
            c.Properties.Add(ReadProperty(pEl));

        return c;
    }

    private static EfProperty ReadProperty(XElement el)
    {
        var p = new EfProperty
        {
            Name = el.Attribute("name")?.Value ?? "",
            Type = TypeMap.Parse(el.Attribute("type")?.Value ?? "String"),
            IsNullable = ParseBool(el.Attribute(P1 + "nullable")?.Value),
            ValueGenerated = el.Attribute("value-generated")?.Value,
            ValidateRequired = ParseBool(el.Attribute(P1 + "ValidateRequired")?.Value),
            ValidateMaxLength = ParseIntNullable(el.Attribute(P1 + "ValidateMaxLength")?.Value),
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value)
        };

        var col = el.Element("column");
        if (col != null) p.Column = ReadColumn(col);

        return p;
    }

    private static EfColumn ReadColumn(XElement el)
    {
        return new EfColumn
        {
            Name = el.Attribute("name")?.Value ?? "",
            NotNull = ParseBool(el.Attribute("not-null")?.Value),
            Default = el.Attribute("default")?.Value,
            SqlType = el.Attribute("sql-type")?.Value,
            Length = ParseIntNullable(el.Attribute("length")?.Value),
            Precision = ParseIntNullable(el.Attribute("precision")?.Value),
            Scale = ParseIntNullable(el.Attribute("scale")?.Value),
            Unicode = ParseBool(el.Attribute(P1 + "unicode")?.Value)
        };
    }

    private static EfAssociation ReadAssociation(XElement el)
    {
        var a = new EfAssociation
        {
            Name = el.Attribute("name")?.Value ?? "",
            Cardinality = ParseCardinality(el.Attribute("cardinality")?.Value),
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value)
        };

        var end1 = el.Element("end1");
        var end2 = el.Element("end2");
        if (end1 != null) a.End1 = ReadEnd(end1);
        if (end2 != null) a.End2 = ReadEnd(end2);

        return a;
    }

    private static EfAssociationEnd ReadEnd(XElement el)
    {
        return new EfAssociationEnd
        {
            Multiplicity = ParseMultiplicity(el.Attribute("multiplicity")?.Value),
            Name = el.Attribute("name")?.Value ?? "",
            ClassName = el.Attribute("class")?.Value ?? "",
            RelationClass = el.Attribute("relation-class")?.Value ?? "",
            Constrained = ParseBool(el.Attribute("constrained")?.Value),
            Lazy = ParseBool(el.Attribute("lazy")?.Value),
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value),
            PropertyName = el.Element("property")?.Attribute("name")?.Value ?? ""
        };
    }

    private static Guid ParseGuid(string? s) =>
        Guid.TryParse(s, out var g) ? g : Guid.Empty;

    private static bool ParseBool(string? s) =>
        bool.TryParse(s, out var b) && b;

    private static int? ParseIntNullable(string? s) =>
        int.TryParse(s, out var i) ? i : null;

    private static Cardinality ParseCardinality(string? s) => s switch
    {
        "OneToOne" => Cardinality.OneToOne,
        "OneToMany" => Cardinality.OneToMany,
        "ManyToOne" => Cardinality.ManyToOne,
        "ManyToMany" => Cardinality.ManyToMany,
        _ => Cardinality.OneToMany
    };

    private static Multiplicity ParseMultiplicity(string? s) => s switch
    {
        "One" => Multiplicity.One,
        "ZeroOrOne" => Multiplicity.ZeroOrOne,
        "Many" => Multiplicity.Many,
        _ => Multiplicity.Many
    };
}
