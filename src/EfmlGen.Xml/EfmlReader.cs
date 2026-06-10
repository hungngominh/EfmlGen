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
            Guid = ParseGuid(root.Attribute(P1 + "Guid")?.Value),
            FileBaseName = root.Attribute(P1 + "FileBaseName")?.Value ?? ""
        };

        foreach (var classEl in root.Elements("class"))
        {
            if (classEl.Attribute("name")?.Value == "$ComplexTypes")
            {
                foreach (var compEl in classEl.Elements("component"))
                    model.ComplexTypes.Add(ReadComplexType(compEl));
                continue;
            }
            model.Classes.Add(ReadClass(classEl));
        }

        var assocs = root.Element("associations");
        if (assocs != null)
        {
            foreach (var aEl in assocs.Elements("association"))
                model.Associations.Add(ReadAssociation(aEl));
        }

        foreach (var mEl in root.Elements("method"))
            model.StoredProcedures.Add(ReadMethod(mEl));

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
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value),
            IsView = ParseBool(el.Attribute("is-view")?.Value)
        };

        var idEl = el.Element("id");
        if (idEl != null) c.Id = ReadProperty(idEl);

        foreach (var pEl in el.Elements("property"))
            c.Properties.Add(ReadProperty(pEl));

        foreach (var cEl in el.Elements("concurrency"))
            c.Properties.Add(ReadProperty(cEl, isConcurrencyToken: true));

        foreach (var iEl in el.Elements("index"))
        {
            var idx = new EfIndex
            {
                Name = iEl.Attribute("name")?.Value ?? "",
                IsUnique = ParseBool(iEl.Attribute("unique")?.Value)
            };
            foreach (var col in iEl.Elements("column"))
            {
                var cn = col.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(cn))
                    idx.ColumnNames.Add(cn);
            }
            c.Indexes.Add(idx);
        }

        return c;
    }

    private static EfProperty ReadProperty(XElement el, bool isConcurrencyToken = false)
    {
        var p = new EfProperty
        {
            Name = el.Attribute("name")?.Value ?? "",
            Type = TypeMap.Parse(el.Attribute("type")?.Value ?? "String"),
            IsNullable = ParseBool(el.Attribute(P1 + "nullable")?.Value),
            ValueGenerated = el.Attribute("value-generated")?.Value,
            ValidateRequired = ParseBool(el.Attribute(P1 + "ValidateRequired")?.Value),
            ValidateMaxLength = ParseIntNullable(el.Attribute(P1 + "ValidateMaxLength")?.Value),
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value),
            IsConcurrencyToken = isConcurrencyToken,
            IsRowVersion = ParseBool(el.Attribute(P1 + "IsRowVersion")?.Value)
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
            Computed = el.Attribute("computed")?.Value,
            SqlType = el.Attribute("sql-type")?.Value,
            Length = ParseIntNullable(el.Attribute("length")?.Value),
            Precision = ParseIntNullable(el.Attribute("precision")?.Value),
            Scale = ParseIntNullable(el.Attribute("scale")?.Value),
            Unicode = ParseBool(el.Attribute(P1 + "unicode")?.Value)
        };
    }

    private static EfComplexType ReadComplexType(XElement el)
    {
        var ct = new EfComplexType
        {
            Name = el.Attribute("class")?.Value ?? "",
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value)
        };
        foreach (var pEl in el.Elements("property"))
            ct.Properties.Add(ReadProperty(pEl));
        return ct;
    }

    private static EfStoredProcedure ReadMethod(XElement el)
    {
        var sp = new EfStoredProcedure
        {
            Name = el.Attribute("name")?.Value ?? "",
            Procedure = el.Attribute(P1 + "procedure")?.Value ?? "",
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value)
        };

        var procName = sp.Procedure;
        var dot = procName.LastIndexOf('.');
        sp.Schema = dot > 0 ? procName.Substring(0, dot) : "";

        var ret = el.Element("return");
        if (ret != null)
        {
            sp.ReturnComplexType = ret.Attribute("class")?.Value;
            foreach (var rp in ret.Elements("return-property"))
                sp.ReturnProperties.Add(new EfReturnProperty
                {
                    Name = rp.Attribute("name")?.Value ?? "",
                    Column = rp.Attribute("column")?.Value ?? ""
                });
        }

        foreach (var prm in el.Elements("parameter"))
            sp.Parameters.Add(ReadParameter(prm));

        return sp;
    }

    private static EfParameter ReadParameter(XElement el)
    {
        return new EfParameter
        {
            Name = el.Attribute("name")?.Value ?? "",
            Type = TypeMap.Parse(el.Attribute("type")?.Value ?? "String"),
            SqlType = el.Attribute("sql-type")?.Value,
            Length = ParseIntNullable(el.Attribute("length")?.Value),
            Precision = ParseIntNullable(el.Attribute("precision")?.Value),
            Scale = ParseIntNullable(el.Attribute("scale")?.Value),
            Direction = ParseDirection(el.Attribute(P1 + "parameter-direction")?.Value)
        };
    }

    private static EfParamDirection ParseDirection(string? s) => s switch
    {
        "InOut" => EfParamDirection.InputOutput,
        "Out" => EfParamDirection.Output,
        "ReturnValue" => EfParamDirection.ReturnValue,
        _ => EfParamDirection.Input
    };

    private static EfAssociation ReadAssociation(XElement el)
    {
        var a = new EfAssociation
        {
            Name = el.Attribute("name")?.Value ?? "",
            Cardinality = ParseCardinality(el.Attribute("cardinality")?.Value),
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value),
            CascadeDelete = ParseBool(el.Attribute(P1 + "CascadeDelete")?.Value)
        };

        var end1 = el.Element("end1");
        var end2 = el.Element("end2");
        if (end1 != null) a.End1 = ReadEnd(end1);
        if (end2 != null) a.End2 = ReadEnd(end2);

        return a;
    }

    private static EfAssociationEnd ReadEnd(XElement el)
    {
        var end = new EfAssociationEnd
        {
            Multiplicity = ParseMultiplicity(el.Attribute("multiplicity")?.Value),
            Name = el.Attribute("name")?.Value ?? "",
            ClassName = el.Attribute("class")?.Value ?? "",
            RelationClass = el.Attribute("relation-class")?.Value ?? "",
            Constrained = ParseBool(el.Attribute("constrained")?.Value),
            Lazy = ParseBool(el.Attribute("lazy")?.Value),
            Guid = ParseGuid(el.Attribute(P1 + "Guid")?.Value)
        };
        foreach (var prop in el.Elements("property"))
        {
            var name = prop.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(name))
                end.PropertyNames.Add(name);
        }
        return end;
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
