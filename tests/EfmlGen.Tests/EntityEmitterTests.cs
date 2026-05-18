using System;
using System.IO;
using EfmlGen.Core;
using EfmlGen.Templates;
using EfmlGen.Xml;
using Xunit;

namespace EfmlGen.Tests;

public class EntityEmitterTests
{
    private static readonly string SampleDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "categories-postgres"));

    private static readonly DateTime FixedTimestamp = new(2026, 4, 13, 18, 2, 1);

    [Theory]
    [InlineData("ConfigState")]
    [InlineData("ConfigReportOrderReason")]
    [InlineData("ConfigReportUserReason")]
    [InlineData("ConfigRentalServiceRatingPoint")]
    [InlineData("Mioto_VehicleOwner")]
    [InlineData("Department")]
    public void GeneratesByteIdenticalEntityFile(string className)
    {
        var efmlPath = Path.Combine(SampleDir, "CategoryEntities.efml");
        var goldenPath = Path.Combine(SampleDir, $"CategoryEntities.{className}.cs");

        var model = EfmlReader.ReadFile(efmlPath);
        var cls = model.Classes.Find(c => c.Name == className)!;
        Assert.NotNull(cls);

        var navsByClass = AssociationLayout.Build(model);
        var navs = navsByClass.TryGetValue(className, out var n) ? n : new();

        var ctx = new GenerationContext { Timestamp = FixedTimestamp, Provider = "Npgsql" };
        var generated = EntityEmitter.Emit(model, cls, navs, ctx);

        var expected = File.ReadAllText(goldenPath);

        Assert.Equal(expected, generated);
    }
}
