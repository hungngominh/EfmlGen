using System;
using System.IO;
using System.Linq;
using EfmlGen.Core;
using EfmlGen.Xml;
using Xunit;

namespace EfmlGen.Tests;

public class ViewReaderTests
{
    private static readonly string SampleDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "categories-postgres"));

    [Fact]
    public void RoundTripWriteThenReadPreservesClassPositions()
    {
        var model = EfmlReader.ReadFile(Path.Combine(SampleDir, "CategoryEntities.efml"));
        var tmp = Path.Combine(Path.GetTempPath(), $"viewreader-test-{Guid.NewGuid():N}.view");
        try
        {
            ViewWriter.WriteFile(model, tmp);
            var layout = ViewReader.ReadFile(tmp);

            Assert.Equal(model.Classes.Count, layout.Classes.Count);
            foreach (var c in model.Classes)
            {
                Assert.True(layout.Classes.ContainsKey(c.Guid), $"Missing class {c.Name} ({c.Guid})");
                var pos = layout.Classes[c.Guid];
                Assert.Equal(DiagramLayout.ClassWidth, pos.Width);
                Assert.Equal(DiagramLayout.ComputeClassHeight(c), pos.Height);
            }
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void ParsesPxUnitsCorrectly()
    {
        var model = new EfmlModel { Guid = Guid.NewGuid(), Name = "T", Namespace = "T" };
        var c = new EfClass
        {
            Guid = Guid.NewGuid(),
            Name = "A",
            Id = new EfProperty { Guid = Guid.NewGuid(), Name = "Id", Type = EfType.Int64, Column = new EfColumn { Name = "Id" } }
        };
        model.Classes.Add(c);

        var doc = ViewWriter.Write(model);
        var layout = ViewReader.Read(doc);

        Assert.True(layout.Classes.ContainsKey(c.Guid));
        var pos = layout.Classes[c.Guid];
        Assert.Equal(0, pos.X);
        Assert.Equal(0, pos.Y);
        Assert.Equal(DiagramLayout.ClassWidth, pos.Width);
    }

    [Fact]
    public void ReadsRealSampleFromCrmHf()
    {
        var samplePath = @"e:\Work\CRM_HF\PostgreSQL_Backup\Data\CoreDataEntities.Diagram1.view";
        if (!File.Exists(samplePath)) return; // Skip if sample not present on this machine.

        var layout = ViewReader.ReadFile(samplePath);
        Assert.True(layout.Classes.Count >= 2,
            $"Expected ≥2 classes parsed from {samplePath}, got {layout.Classes.Count}");
        foreach (var pos in layout.Classes.Values)
        {
            Assert.True(pos.Width > 0);
            Assert.True(pos.Height > 0);
        }
    }
}
