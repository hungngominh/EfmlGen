using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EfmlGen.Core;

namespace EfmlGen.Xml;

/// <summary>
/// Write a Devart Entity Developer diagram file (.view) — XML describing visual layout
/// of classes on the diagram canvas. Entity Developer renders FK lines from .efml
/// <c>&lt;associations&gt;</c> at open time; this file only stores positions and sizes.
/// </summary>
public static class ViewWriter
{
    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XName XsiType = Xsi + "type";

    private const string PropertyTypeName = "EntityDeveloper.EntityFrameworkCore.EntityProperty";
    private const string ClassTypeName = "EntityDeveloper.EntityFrameworkCore.EntityClass";
    private const string ContextTypeName = "EntityDeveloper.EntityFrameworkCore.EntityContextModel";

    public static void WriteFile(EfmlModel model, string path,
        string diagramName = "Diagram1",
        IReadOnlyDictionary<Guid, (int X, int Y)>? layout = null)
    {
        var doc = Write(model, diagramName, layout);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(true),
            NewLineChars = "\r\n",
            OmitXmlDeclaration = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    public static XDocument Write(EfmlModel model,
        string diagramName = "Diagram1",
        IReadOnlyDictionary<Guid, (int X, int Y)>? layout = null)
    {
        _ = diagramName; // reserved for future use; ED uses the file name, not an in-file attribute
        var resolvedLayout = layout ?? DiagramLayout.ComputeGrid(model);

        int nextOid = 0;
        int rootOid = nextOid++;

        var context = new XElement("Model",
            new XAttribute(XsiType, "ContextVwModel"),
            CustomProperties(rootOid, parent: null,
                new XElement("BackgroundColor", "Window")));

        var contextChildren = new XElement("Children");
        foreach (var c in model.Classes)
        {
            var pos = resolvedLayout.TryGetValue(c.Guid, out var p) ? p : (X: 0, Y: 0);
            contextChildren.Add(WriteClass(c, pos.X, pos.Y, rootOid, ref nextOid));
        }
        context.Add(contextChildren);

        context.Add(
            new XElement("GridSize", "8 px"),
            new XElement("ViewPort",
                new XElement("ScaleMode", "Free"),
                new XElement("Scale", "1"),
                new XElement("Location",
                    new XElement("X", "0 px"),
                    new XElement("Y", "0 px"))),
            new XElement("Oid",
                new XAttribute(XsiType, "SchemaModelOID"),
                new XElement("Path", model.Guid.ToString()),
                new XElement("TypeName", ContextTypeName)));

        var root = new XElement("EntityDeveloperDiagram",
            new XElement("Diagram",
                new XAttribute("Version", "1.22.3.0"),
                new XElement("DiagramModel",
                    WithXmlSchemaNs(context))),
            BuildDiagramOptions());

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XElement WriteClass(EfClass c, int posX, int posY, int parentOid, ref int nextOid)
    {
        int classOid = nextOid++;
        var classEl = new XElement("Model",
            new XAttribute(XsiType, "ClassVwModel"),
            CustomProperties(classOid, parent: parentOid));

        // Children: PropertiesVwModel + RelationPropertiesVwModel
        var childrenEl = new XElement("Children");

        int propsOid = nextOid++;
        var propsEl = new XElement("Model",
            new XAttribute(XsiType, "PropertiesVwModel"),
            CustomProperties(propsOid, parent: classOid));

        var propsChildrenEl = new XElement("Children");
        int propIndex = 0;
        int propCount = 0;
        foreach (var p in c.AllProperties)
        {
            int propOid = nextOid++;
            propsChildrenEl.Add(WritePropertyRow(p, propIndex, propOid, propsOid));
            propIndex++;
            propCount++;
        }
        propsEl.Add(propsChildrenEl);

        int propsHeight = 1 + propCount * DiagramLayout.PropertyRowHeight;
        propsEl.Add(
            new XElement("Size",
                new XElement("Width", "146 px"),
                new XElement("Height", Px(propsHeight))),
            new XElement("MaxSize",
                new XElement("Width", "0 px"),
                new XElement("Height", Px(propsHeight))));

        childrenEl.Add(propsEl);

        int relOid = nextOid++;
        childrenEl.Add(
            new XElement("Model",
                new XAttribute(XsiType, "RelationPropertiesVwModel"),
                CustomProperties(relOid, parent: classOid),
                new XElement("Children"),
                new XElement("Location",
                    new XElement("X", "0 px"),
                    new XElement("Y", Px(propsHeight - 2))),
                new XElement("Size",
                    new XElement("Width", "146 px"),
                    new XElement("Height", "19 px")),
                new XElement("MaxSize",
                    new XElement("Width", "0 px"),
                    new XElement("Height", "19 px")),
                new XElement("Hidden", "false")));

        classEl.Add(childrenEl);

        int classHeight = DiagramLayout.ComputeClassHeight(c);
        classEl.Add(
            new XElement("Location",
                new XElement("X", Px(posX)),
                new XElement("Y", Px(posY))),
            new XElement("Size",
                new XElement("Width", Px(DiagramLayout.ClassWidth)),
                new XElement("Height", Px(classHeight))),
            new XElement("MinSize",
                new XElement("Width", "100 px"),
                new XElement("Height", Px(classHeight))),
            new XElement("MaxSize",
                new XElement("Width", "800 px"),
                new XElement("Height", Px(classHeight))),
            new XElement("Ports"),
            new XElement("Oid",
                new XAttribute(XsiType, "SchemaModelOID"),
                new XElement("Path", c.Guid.ToString()),
                new XElement("TypeName", ClassTypeName)),
            new XElement("FixedHeight", "35 px"));

        return classEl;
    }

    private static XElement WritePropertyRow(EfProperty p, int index, int oid, int parentOid)
    {
        int y = 1 + index * DiagramLayout.PropertyRowHeight;
        return new XElement("Model",
            new XAttribute(XsiType, "PropertyVwModel"),
            CustomProperties(oid, parent: parentOid),
            new XElement("Children"),
            new XElement("Location",
                new XElement("X", "1 px"),
                new XElement("Y", Px(y))),
            new XElement("Size",
                new XElement("Width", "144 px"),
                new XElement("Height", "18 px")),
            new XElement("Oid",
                new XAttribute(XsiType, "SchemaModelOID"),
                new XElement("Path", p.Guid.ToString()),
                new XElement("TypeName", PropertyTypeName)));
    }

    private static XElement CustomProperties(int oid, int? parent, params object[] extra)
    {
        var el = new XElement("CustomProperties",
            new XElement("OID", oid.ToString(CultureInfo.InvariantCulture)));
        if (parent.HasValue)
            el.Add(new XElement("Parent", parent.Value.ToString(CultureInfo.InvariantCulture)));
        foreach (var x in extra) el.Add(x);
        return el;
    }

    /// <summary>
    /// Attach xsd/xsi namespace declarations on the given element (root of a model subtree)
    /// to match Entity Developer's serialization style.
    /// </summary>
    private static XElement WithXmlSchemaNs(XElement el)
    {
        el.Add(new XAttribute(XNamespace.Xmlns + "xsd", Xsd.NamespaceName));
        el.Add(new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName));
        return el;
    }

    private static XElement BuildDiagramOptions()
    {
        return new XElement("DiagramOptions",
            new XAttribute("Version", "v2.0"),
            WithXmlSchemaNs(new XElement("Options",
                new XAttribute(XsiType, "PageOptions"),
                new XElement("TopLeftMargins",
                    new XElement("Width", "39.3700787401575 in/100"),
                    new XElement("Height", "39.3700787401575 in/100")),
                new XElement("BottomRightMargins",
                    new XElement("Width", "39.3700787401575 in/100"),
                    new XElement("Height", "39.3700787401575 in/100")),
                new XElement("PaperSize",
                    new XElement("Width", "827 in/100"),
                    new XElement("Height", "1169 in/100")))),
            WithXmlSchemaNs(new XElement("Options",
                new XAttribute(XsiType, "PrintOptions"))),
            WithXmlSchemaNs(new XElement("Options",
                new XAttribute(XsiType, "ViewOptions"),
                new XElement("ShadowOffset",
                    new XElement("X", "4 px"),
                    new XElement("Y", "4 px")),
                new XElement("EnableShadows", "false"),
                new XElement("CustomProperties"))),
            WithXmlSchemaNs(new XElement("EdDiagramOptions",
                new XElement("SkinType", "Simple"),
                new XElement("CustomProperties"))));
    }

    private static string Px(int v) => v.ToString(CultureInfo.InvariantCulture) + " px";
}
