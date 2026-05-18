using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using EfmlGen.Core;
using EfmlGen.Xml;
using Xunit;

namespace EfmlGen.Tests;

public class ViewWriterTests
{
    private static readonly string SampleDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "categories-postgres"));

    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    [Fact]
    public void GeneratesOneClassVwModelPerEfClass()
    {
        var model = EfmlReader.ReadFile(Path.Combine(SampleDir, "CategoryEntities.efml"));

        var doc = ViewWriter.Write(model);

        var classNodes = doc.Descendants("Model")
            .Where(m => (string?)m.Attribute(Xsi + "type") == "ClassVwModel")
            .ToList();
        Assert.Equal(model.Classes.Count, classNodes.Count);
    }

    [Fact]
    public void EachClassReferencesItsGuid()
    {
        var model = EfmlReader.ReadFile(Path.Combine(SampleDir, "CategoryEntities.efml"));
        var doc = ViewWriter.Write(model);

        var classGuids = doc.Descendants("Model")
            .Where(m => (string?)m.Attribute(Xsi + "type") == "ClassVwModel")
            .Select(m =>
            {
                var oid = m.Element("Oid")!;
                return Guid.Parse(oid.Element("Path")!.Value);
            })
            .ToHashSet();

        foreach (var c in model.Classes)
            Assert.Contains(c.Guid, classGuids);
    }

    [Fact]
    public void PropertyCountMatchesAllPropertiesAcrossClasses()
    {
        var model = EfmlReader.ReadFile(Path.Combine(SampleDir, "CategoryEntities.efml"));
        var doc = ViewWriter.Write(model);

        int expected = model.Classes.Sum(c => c.AllProperties.Count());
        int actual = doc.Descendants("Model")
            .Count(m => (string?)m.Attribute(Xsi + "type") == "PropertyVwModel");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RootContextOidMatchesEfmlModelGuid()
    {
        var model = EfmlReader.ReadFile(Path.Combine(SampleDir, "CategoryEntities.efml"));
        var doc = ViewWriter.Write(model);

        var contextModel = doc.Descendants("Model")
            .First(m => (string?)m.Attribute(Xsi + "type") == "ContextVwModel");
        var rootOidPath = contextModel.Element("Oid")!.Element("Path")!.Value;
        Assert.Equal(model.Guid.ToString(), rootOidPath);
    }

    [Fact]
    public void ClassLocationsAreUnique()
    {
        var model = EfmlReader.ReadFile(Path.Combine(SampleDir, "CategoryEntities.efml"));
        var doc = ViewWriter.Write(model);

        var positions = doc.Descendants("Model")
            .Where(m => (string?)m.Attribute(Xsi + "type") == "ClassVwModel")
            .Select(m =>
            {
                var loc = m.Element("Location")!;
                return (X: loc.Element("X")!.Value, Y: loc.Element("Y")!.Value);
            })
            .ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    [Fact]
    public void WriteFileProducesParseableXml()
    {
        var model = EfmlReader.ReadFile(Path.Combine(SampleDir, "CategoryEntities.efml"));
        var tmp = Path.Combine(Path.GetTempPath(), $"viewwriter-test-{Guid.NewGuid():N}.view");
        try
        {
            ViewWriter.WriteFile(model, tmp);
            Assert.True(File.Exists(tmp));
            var parsed = XDocument.Load(tmp);
            Assert.Equal("EntityDeveloperDiagram", parsed.Root!.Name.LocalName);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void GridLayoutWrapsAtConfiguredColumnCount()
    {
        var model = new EfmlModel { Guid = Guid.NewGuid(), Name = "Test", Namespace = "T" };
        for (int i = 0; i < 7; i++)
        {
            var c = new EfClass
            {
                Guid = Guid.NewGuid(),
                Name = $"C{i}",
                Id = new EfProperty { Guid = Guid.NewGuid(), Name = "Id", Type = EfType.Int64, Column = new EfColumn { Name = "Id" } }
            };
            model.Classes.Add(c);
        }

        var layout = DiagramLayout.ComputeGrid(model, columns: 3, hGap: 50, vGap: 50);

        // 3 columns: positions 0,1,2 share Y; position 3 wraps; etc.
        var ys = model.Classes.Select(c => layout[c.Guid].Y).ToList();
        Assert.Equal(ys[0], ys[1]);
        Assert.Equal(ys[1], ys[2]);
        Assert.True(ys[3] > ys[2]);
        Assert.Equal(ys[3], ys[4]);
        Assert.Equal(ys[4], ys[5]);
        Assert.True(ys[6] > ys[5]);

        // X column index: 0, 200, 400, 0, 200, 400, 0
        Assert.Equal(0, layout[model.Classes[0].Guid].X);
        Assert.Equal(200, layout[model.Classes[1].Guid].X);
        Assert.Equal(400, layout[model.Classes[2].Guid].X);
        Assert.Equal(0, layout[model.Classes[3].Guid].X);
    }
}
