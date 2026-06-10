using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EfmlGen.Core;

namespace EfmlGen.Xml;

/// <summary>
/// Ghi EfmlModel ra file .efml XML, format match Devart Entity Developer.
/// Namespace: xmlns:p1="http://devart.com/schemas/EntityDeveloper/1.0"
/// </summary>
public static class EfmlWriter
{
    private static readonly XNamespace P1 = "http://devart.com/schemas/EntityDeveloper/1.0";

    public static void WriteFile(EfmlModel model, string path)
    {
        var doc = Write(model);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(true),
            NewLineChars = "\r\n",
            OmitXmlDeclaration = false
        };
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    public static XDocument Write(EfmlModel model)
    {
        var root = new XElement("efcore",
            new XAttribute("context-namespace", model.ContextNamespace),
            new XAttribute("namespace", model.Namespace),
            new XAttribute(XNamespace.Xmlns + "p1", P1.NamespaceName),
            new XAttribute(P1 + "name", model.Name),
            new XAttribute(P1 + "Guid", model.Guid));

        if (!string.IsNullOrEmpty(model.FileBaseName))
            root.Add(new XAttribute(P1 + "FileBaseName", model.FileBaseName));

        if (model.ComplexTypes.Count > 0)
            root.Add(WriteComplexTypes(model.ComplexTypes));

        foreach (var c in model.Classes)
            root.Add(WriteClass(c));

        if (model.Associations.Count > 0)
        {
            var assocs = new XElement("associations");
            foreach (var a in model.Associations)
                assocs.Add(WriteAssociation(a));
            root.Add(assocs);
        }

        foreach (var sp in model.StoredProcedures)
            root.Add(WriteMethod(sp));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XElement WriteClass(EfClass c)
    {
        var el = new XElement("class",
            new XAttribute("name", c.Name),
            new XAttribute("entity-set", c.EntitySet),
            new XAttribute("table", c.Table),
            new XAttribute("schema", c.Schema),
            new XAttribute(P1 + "Guid", c.Guid));

        if (c.IsView)
            el.SetAttributeValue("is-view", "True");

        if (c.Id != null!)
            el.Add(WriteProperty("id", c.Id));
        foreach (var p in c.Properties)
            el.Add(WriteProperty(p.IsConcurrencyToken ? "concurrency" : "property", p));

        foreach (var idx in c.Indexes)
        {
            var iel = new XElement("index",
                new XAttribute("name", idx.Name));
            if (idx.IsUnique)
                iel.SetAttributeValue("unique", "True");
            foreach (var cn in idx.ColumnNames)
                iel.Add(new XElement("column", new XAttribute("name", cn)));
            el.Add(iel);
        }

        return el;
    }

    private static XElement WriteProperty(string elementName, EfProperty p)
    {
        var el = new XElement(elementName,
            new XAttribute("name", p.Name),
            new XAttribute("type", TypeMap.ToEfml(p.Type)));

        if (p.IsNullable)
            el.SetAttributeValue(P1 + "nullable", "True");
        if (!string.IsNullOrEmpty(p.ValueGenerated))
            el.SetAttributeValue("value-generated", p.ValueGenerated);
        if (p.ValidateMaxLength.HasValue)
            el.SetAttributeValue(P1 + "ValidateMaxLength", p.ValidateMaxLength.Value.ToString(CultureInfo.InvariantCulture));
        el.SetAttributeValue(P1 + "ValidateRequired", p.ValidateRequired ? "true" : "false");
        if (p.IsRowVersion)
            el.SetAttributeValue(P1 + "IsRowVersion", "True");
        el.SetAttributeValue(P1 + "Guid", p.Guid);

        el.Add(WriteColumn(p.Column));
        return el;
    }

    private static XElement WriteColumn(EfColumn col)
    {
        var el = new XElement("column",
            new XAttribute("name", col.Name));

        if (!string.IsNullOrEmpty(col.Default))
            el.SetAttributeValue("default", col.Default);
        if (!string.IsNullOrEmpty(col.Computed))
            el.SetAttributeValue("computed", col.Computed);
        el.SetAttributeValue("not-null", col.NotNull ? "True" : "False");
        if (col.Length.HasValue)
            el.SetAttributeValue("length", col.Length.Value.ToString(CultureInfo.InvariantCulture));
        if (col.Precision.HasValue)
            el.SetAttributeValue("precision", col.Precision.Value.ToString(CultureInfo.InvariantCulture));
        if (col.Scale.HasValue)
            el.SetAttributeValue("scale", col.Scale.Value.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(col.SqlType))
            el.SetAttributeValue("sql-type", col.SqlType);
        el.SetAttributeValue(P1 + "unicode", col.Unicode ? "True" : "False");

        return el;
    }

    private static XElement WriteComplexTypes(System.Collections.Generic.List<EfComplexType> complexTypes)
    {
        var el = new XElement("class", new XAttribute("name", "$ComplexTypes"));
        foreach (var ct in complexTypes)
        {
            var comp = new XElement("component",
                new XAttribute("class", ct.Name),
                new XAttribute(P1 + "Guid", ct.Guid));
            foreach (var p in ct.Properties)
                comp.Add(WriteProperty("property", p));
            el.Add(comp);
        }
        return el;
    }

    private static XElement WriteMethod(EfStoredProcedure sp)
    {
        var el = new XElement("method",
            new XAttribute("name", sp.Name),
            new XAttribute(P1 + "procedure", sp.Procedure),
            new XAttribute(P1 + "Guid", sp.Guid));

        if (!string.IsNullOrEmpty(sp.ReturnComplexType))
        {
            var ret = new XElement("return", new XAttribute("class", sp.ReturnComplexType!));
            foreach (var rp in sp.ReturnProperties)
                ret.Add(new XElement("return-property",
                    new XAttribute("name", rp.Name),
                    new XAttribute("column", rp.Column)));
            el.Add(ret);
        }

        foreach (var prm in sp.Parameters)
            el.Add(WriteParameter(prm));

        return el;
    }

    private static XElement WriteParameter(EfParameter p)
    {
        var el = new XElement("parameter", new XAttribute("name", p.Name));

        if (p.Direction != EfParamDirection.Input)
            el.SetAttributeValue(P1 + "parameter-direction", ToDirectionString(p.Direction));

        el.SetAttributeValue("type", TypeMap.ToEfml(p.Type));

        if (!string.IsNullOrEmpty(p.SqlType))
            el.SetAttributeValue("sql-type", p.SqlType);
        if (p.Length.HasValue)
            el.SetAttributeValue("length", p.Length.Value.ToString(CultureInfo.InvariantCulture));
        if (p.Precision.HasValue)
            el.SetAttributeValue("precision", p.Precision.Value.ToString(CultureInfo.InvariantCulture));
        if (p.Scale.HasValue)
            el.SetAttributeValue("scale", p.Scale.Value.ToString(CultureInfo.InvariantCulture));

        return el;
    }

    private static string ToDirectionString(EfParamDirection d) => d switch
    {
        EfParamDirection.InputOutput => "InOut",
        EfParamDirection.Output => "Out",
        EfParamDirection.ReturnValue => "ReturnValue",
        _ => "In"
    };

    private static XElement WriteAssociation(EfAssociation a)
    {
        var el = new XElement("association",
            new XAttribute("name", a.Name),
            new XAttribute("cardinality", a.Cardinality.ToString()),
            new XAttribute(P1 + "Guid", a.Guid));

        if (a.CascadeDelete)
            el.SetAttributeValue(P1 + "CascadeDelete", "True");

        el.Add(WriteEnd("end1", a.End1));
        el.Add(WriteEnd("end2", a.End2));
        return el;
    }

    private static XElement WriteEnd(string name, EfAssociationEnd e)
    {
        var el = new XElement(name,
            new XAttribute("multiplicity", e.Multiplicity.ToString()),
            new XAttribute("name", e.Name),
            new XAttribute("class", e.ClassName),
            new XAttribute("relation-class", e.RelationClass));

        if (e.Constrained)
            el.SetAttributeValue("constrained", "True");
        el.SetAttributeValue("lazy", e.Lazy ? "True" : "False");
        el.SetAttributeValue(P1 + "Guid", e.Guid);

        if (e.PropertyNames.Count == 0)
        {
            el.Add(new XElement("property", new XAttribute("name", "")));
        }
        else
        {
            foreach (var pn in e.PropertyNames)
                el.Add(new XElement("property", new XAttribute("name", pn)));
        }
        return el;
    }
}
