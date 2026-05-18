using System;
using System.Linq;
using EfmlGen.Core;
using Xunit;

namespace EfmlGen.Tests;

public class EfmlMergerTests
{
    private static EfmlModel BuildSimpleModel(string className, params (string col, EfType type, int? maxLen)[] props)
    {
        var m = new EfmlModel
        {
            Name = "TestModel",
            Namespace = "Test.Ns",
            ContextNamespace = "Test.Ns",
            Guid = Guid.NewGuid()
        };
        var c = new EfClass
        {
            Name = className,
            EntitySet = className,
            Table = $"`{className}`",
            Schema = "dbo",
            Guid = Guid.NewGuid(),
            Id = new EfProperty
            {
                Name = "Id",
                Type = EfType.Int64,
                ValidateRequired = true,
                ValueGenerated = "OnAdd",
                Guid = Guid.NewGuid(),
                Column = new EfColumn { Name = "`Id`", NotNull = true }
            }
        };
        foreach (var (col, type, maxLen) in props)
        {
            c.Properties.Add(new EfProperty
            {
                Name = col,
                Type = type,
                ValidateMaxLength = maxLen,
                Guid = Guid.NewGuid(),
                Column = new EfColumn { Name = $"`{col}`", NotNull = true, Length = maxLen }
            });
        }
        m.Classes.Add(c);
        return m;
    }

    [Fact]
    public void Merge_PreservesClassAndPropertyGuids()
    {
        var existing = BuildSimpleModel("Customer", ("Name", EfType.String, null));
        var existingClassGuid = existing.Classes[0].Guid;
        var existingNameGuid = existing.Classes[0].Properties[0].Guid;
        var existingModelGuid = existing.Guid;

        var fromDb = BuildSimpleModel("Customer", ("Name", EfType.String, null));

        var (merged, _) = EfmlMerger.Merge(fromDb, existing);

        Assert.Equal(existingModelGuid, merged.Guid);
        Assert.Equal(existingClassGuid, merged.Classes[0].Guid);
        Assert.Equal(existingNameGuid, merged.Classes[0].Properties[0].Guid);
    }

    [Fact]
    public void Merge_PreservesUserClassRename()
    {
        // User edited efml: "Customer" → "tbl_Customer" stays as "Customer" (the friendly name)
        var existing = BuildSimpleModel("Customer");
        existing.Classes[0].Name = "Customer";
        existing.Classes[0].Table = "`tbl_Customer`";   // user kept table mapping different from class name

        // DB sees the underlying table as tbl_Customer
        var fromDb = BuildSimpleModel("tbl_Customer");
        fromDb.Classes[0].Table = "`tbl_Customer`";

        var (merged, report) = EfmlMerger.Merge(fromDb, existing);

        Assert.Equal("Customer", merged.Classes[0].Name);
        Assert.Single(report.RenamedClasses);
    }

    [Fact]
    public void Merge_PreservesValidateMaxLengthIfDbHasNone()
    {
        // User set ValidateMaxLength=200 even though DB column has no length constraint
        var existing = BuildSimpleModel("Customer", ("Note", EfType.String, 200));
        var fromDb = BuildSimpleModel("Customer", ("Note", EfType.String, null));

        var (merged, _) = EfmlMerger.Merge(fromDb, existing);

        Assert.Equal(200, merged.Classes[0].Properties[0].ValidateMaxLength);
    }

    [Fact]
    public void Merge_TypeFromDbWinsOverExistingType()
    {
        var existing = BuildSimpleModel("Customer", ("Code", EfType.String, null));
        var fromDb = BuildSimpleModel("Customer", ("Code", EfType.Int32, null));

        var (merged, _) = EfmlMerger.Merge(fromDb, existing);

        Assert.Equal(EfType.Int32, merged.Classes[0].Properties[0].Type);
    }

    [Fact]
    public void Merge_ReportsAddedRemovedRenamed()
    {
        var existing = BuildSimpleModel("Customer", ("OldCol", EfType.String, null));
        // Existing extra class
        existing.Classes.Add(new EfClass
        {
            Name = "Removed",
            Table = "`Removed`",
            Schema = "dbo",
            Guid = Guid.NewGuid(),
            Id = new EfProperty { Name = "Id", Column = new EfColumn { Name = "`Id`" } }
        });

        var fromDb = BuildSimpleModel("Customer", ("NewCol", EfType.String, null));
        fromDb.Classes.Add(new EfClass
        {
            Name = "AddedClass",
            Table = "`AddedClass`",
            Schema = "dbo",
            Guid = Guid.NewGuid(),
            Id = new EfProperty { Name = "Id", Column = new EfColumn { Name = "`Id`" } }
        });

        var (_, report) = EfmlMerger.Merge(fromDb, existing);

        Assert.Contains("AddedClass", report.AddedClasses);
        Assert.Contains(report.RemovedClasses, s => s.StartsWith("Removed"));
        Assert.Contains(report.AddedProperties, s => s == "Customer.NewCol");
        Assert.Contains(report.RemovedProperties, s => s.StartsWith("Customer.OldCol"));
    }

    [Fact]
    public void Merge_PreservesAssociationGuidByName()
    {
        var existing = BuildSimpleModel("Department");
        var existingAssoc = new EfAssociation
        {
            Name = "FK_Department_Department",
            Guid = Guid.NewGuid(),
            End1 = new EfAssociationEnd { Name = "MyParent", ClassName = "Department", PropertyName = "Id", Guid = Guid.NewGuid() },
            End2 = new EfAssociationEnd { Name = "MyChildren", ClassName = "Department", PropertyName = "ParentId", Guid = Guid.NewGuid() }
        };
        existing.Associations.Add(existingAssoc);

        var fromDb = BuildSimpleModel("Department");
        fromDb.Associations.Add(new EfAssociation
        {
            Name = "FK_Department_Department",
            Guid = Guid.NewGuid(),  // would be new GUID
            End1 = new EfAssociationEnd { Name = "Department_ParentId", ClassName = "Department", PropertyName = "Id", Guid = Guid.NewGuid() },
            End2 = new EfAssociationEnd { Name = "Department_ParentId1", ClassName = "Department", PropertyName = "ParentId", Guid = Guid.NewGuid() }
        });

        var (merged, _) = EfmlMerger.Merge(fromDb, existing);

        Assert.Equal(existingAssoc.Guid, merged.Associations[0].Guid);
        Assert.Equal("MyParent", merged.Associations[0].End1.Name);    // preserved user nav names
        Assert.Equal("MyChildren", merged.Associations[0].End2.Name);
    }
}
