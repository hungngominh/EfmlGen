using System;
using System.Linq;
using EfmlGen.Core;
using Xunit;

namespace EfmlGen.Tests;

public class CollisionDetectorTests
{
    private static EfmlModel ModelWith(EfClass cls)
    {
        var m = new EfmlModel { Name = "M", Namespace = "Ns", ContextNamespace = "Ns", Guid = Guid.NewGuid() };
        m.Classes.Add(cls);
        return m;
    }

    private static EfClass NewClass(string name, params (string col, string propName)[] props)
    {
        var c = new EfClass
        {
            Name = name,
            EntitySet = name,
            Table = $"`{name}`",
            Schema = "dbo",
            Guid = Guid.NewGuid(),
            Id = new EfProperty { Name = "Id", Type = EfType.Int64, Column = new EfColumn { Name = "`Id`", NotNull = true } }
        };
        foreach (var (col, propName) in props)
        {
            c.Properties.Add(new EfProperty
            {
                Name = propName,
                Type = EfType.String,
                Column = new EfColumn { Name = $"`{col}`" }
            });
        }
        return c;
    }

    [Fact]
    public void Validate_NoCollisions_Returns_Empty()
    {
        var m = ModelWith(NewClass("Customer", ("Name", "Name"), ("Email", "Email")));
        Assert.Empty(CollisionDetector.Validate(m));
    }

    [Fact]
    public void Validate_DuplicateClassNames_Error()
    {
        var m = new EfmlModel { Name = "M", Namespace = "Ns", ContextNamespace = "Ns" };
        m.Classes.Add(NewClass("Foo"));
        m.Classes.Add(NewClass("Foo"));

        var ws = CollisionDetector.Validate(m);
        Assert.Contains(ws, w => w.Severity == CollisionDetector.Severity.Error && w.Message.Contains("'Foo'"));
    }

    [Fact]
    public void Validate_PropertyNameEqualsClassName_Error()
    {
        var m = ModelWith(NewClass("Customer", ("Customer", "Customer")));
        var ws = CollisionDetector.Validate(m);
        Assert.Contains(ws, w => w.Severity == CollisionDetector.Severity.Error && w.Message.Contains("same name as its class"));
    }

    [Fact]
    public void Validate_ReservedClassName_Warning()
    {
        var m = ModelWith(NewClass("class"));
        var ws = CollisionDetector.Validate(m);
        Assert.Contains(ws, w => w.Severity == CollisionDetector.Severity.Warning && w.Message.Contains("@class"));
    }

    [Fact]
    public void Validate_ReservedPropertyName_Warning()
    {
        var m = ModelWith(NewClass("Customer", ("event", "event")));
        var ws = CollisionDetector.Validate(m);
        Assert.Contains(ws, w => w.Severity == CollisionDetector.Severity.Warning && w.Message.Contains("'@event'"));
    }

    [Fact]
    public void Validate_DbSetCollidesWithBuiltInMember_Warning()
    {
        var cls = NewClass("Foo");
        cls.EntitySet = "Database";
        var m = ModelWith(cls);
        var ws = CollisionDetector.Validate(m);
        Assert.Contains(ws, w => w.Message.Contains("Database"));
    }

    [Fact]
    public void Validate_NavCollidesWithColumn_Error()
    {
        var dept = NewClass("Department", ("ParentId", "ParentId"));
        var m = new EfmlModel { Name = "M", Namespace = "Ns", ContextNamespace = "Ns" };
        m.Classes.Add(dept);
        m.Associations.Add(new EfAssociation
        {
            Name = "FK_Department_Department",
            Cardinality = Cardinality.OneToMany,
            End1 = new EfAssociationEnd { Multiplicity = Multiplicity.ZeroOrOne, Name = "ParentId", ClassName = "Department", PropertyName = "Id" },
            End2 = new EfAssociationEnd { Multiplicity = Multiplicity.Many, Name = "Children", ClassName = "Department", PropertyName = "ParentId" }
        });

        var ws = CollisionDetector.Validate(m);
        // End1.Name="ParentId" is added as nav on End2.class=Department — collides with column "ParentId"
        Assert.Contains(ws, w => w.Severity == CollisionDetector.Severity.Error && w.Message.Contains("'ParentId'"));
    }
}
