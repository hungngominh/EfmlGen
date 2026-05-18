using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace EfmlGen.Xml;

public sealed record ClassPosition(double X, double Y, double Width, double Height);

public sealed record ViewLayout(IReadOnlyDictionary<Guid, ClassPosition> Classes);

/// <summary>
/// Parse a Devart Entity Developer diagram (.view) file into a layout map keyed by
/// EfClass.Guid. Only top-level class positions/sizes are extracted; nested property
/// rows are ignored (rendered procedurally from the .efml).
/// </summary>
public static class ViewReader
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XName XsiType = Xsi + "type";

    public static ViewLayout ReadFile(string path) => Read(XDocument.Load(path));

    public static ViewLayout Read(XDocument doc)
    {
        var classes = new Dictionary<Guid, ClassPosition>();
        if (doc.Root is null) return new ViewLayout(classes);

        foreach (var model in doc.Descendants("Model"))
        {
            if ((string?)model.Attribute(XsiType) != "ClassVwModel") continue;

            var oid = model.Element("Oid");
            var pathEl = oid?.Element("Path");
            if (pathEl is null || !Guid.TryParse(pathEl.Value, out var classGuid)) continue;

            // First class in a diagram often omits <Location> (implicit origin 0,0).
            var location = model.Element("Location");
            var size = model.Element("Size");

            var x = ParsePx(location?.Element("X")?.Value);
            var y = ParsePx(location?.Element("Y")?.Value);
            var w = ParsePx(size?.Element("Width")?.Value);
            var h = ParsePx(size?.Element("Height")?.Value);

            classes[classGuid] = new ClassPosition(x, y, w, h);
        }

        return new ViewLayout(classes);
    }

    private static double ParsePx(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var trimmed = raw.Trim();
        var spaceIdx = trimmed.IndexOf(' ');
        var numberPart = spaceIdx >= 0 ? trimmed.Substring(0, spaceIdx) : trimmed;
        return double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }
}
