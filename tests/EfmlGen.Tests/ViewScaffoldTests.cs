using System.Linq;
using EfmlGen.Core;
using EfmlGen.Db;
using EfmlGen.Templates;
using EfmlGen.Xml;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Xunit;

namespace EfmlGen.Tests;

public class ViewScaffoldTests
{
    // ---- Mapper: DatabaseView → keyless EfClass ----

    private static DatabaseModel BuildDbModelWithView()
    {
        var model = new DatabaseModel();

        var table = new DatabaseTable { Name = "User", Schema = "dbo" };
        var idCol = new DatabaseColumn { Table = table, Name = "Id", StoreType = "bigint", IsNullable = false };
        table.Columns.Add(idCol);
        table.Columns.Add(new DatabaseColumn { Table = table, Name = "Name", StoreType = "nvarchar(100)", IsNullable = true });
        table.PrimaryKey = new DatabasePrimaryKey { Table = table, Name = "PK_User" };
        table.PrimaryKey.Columns.Add(idCol);
        model.Tables.Add(table);

        var view = new DatabaseView { Name = "ActiveUser", Schema = "dbo" };
        view.Columns.Add(new DatabaseColumn { Table = view, Name = "Id", StoreType = "bigint", IsNullable = false });
        view.Columns.Add(new DatabaseColumn { Table = view, Name = "Name", StoreType = "nvarchar(100)", IsNullable = true });
        model.Tables.Add(view);

        return model;
    }

    [Fact]
    public void Mapper_View_IsKeyless_AllColumnsAreProperties()
    {
        var efml = DatabaseModelMapper.Map(BuildDbModelWithView(), new DatabaseModelMapper.MapOptions
        {
            Name = "M",
            Namespace = "N",
            ContextNamespace = "N",
            Provider = DbProvider.SqlServer
        });

        var view = efml.Classes.Single(c => c.Name == "ActiveUser");
        Assert.True(view.IsView);
        Assert.True(view.Id == null!);
        Assert.Equal(2, view.Properties.Count);
        Assert.Empty(view.Indexes);
    }

    [Fact]
    public void Mapper_Table_StillHasKey()
    {
        var efml = DatabaseModelMapper.Map(BuildDbModelWithView(), new DatabaseModelMapper.MapOptions
        {
            Name = "M",
            Namespace = "N",
            ContextNamespace = "N",
            Provider = DbProvider.SqlServer
        });

        var table = efml.Classes.Single(c => c.Name == "User");
        Assert.False(table.IsView);
        Assert.False(table.Id == null!);
        Assert.Equal("Id", table.Id.Name);
    }

    // ---- Emitters: view → .ToView() + .HasNoKey() ----

    private static EfmlModel ModelWithKeylessView()
    {
        var model = new EfmlModel { Name = "Ctx", Namespace = "App", ContextNamespace = "App" };
        var view = new EfClass { Name = "ActiveUser", EntitySet = "ActiveUser", Table = "`ActiveUser`", Schema = "dbo", IsView = true };
        view.Properties.Add(new EfProperty { Name = "Id", Type = EfType.Int64, Column = new EfColumn { Name = "`Id`", NotNull = true } });
        view.Properties.Add(new EfProperty { Name = "Name", Type = EfType.String, IsNullable = true, Column = new EfColumn { Name = "`Name`" } });
        model.Classes.Add(view);
        return model;
    }

    [Fact]
    public void ContextEmitter_View_EmitsToViewAndHasNoKey()
    {
        var ctx = new GenerationContext { Provider = "SqlServer" };
        var output = ContextEmitter.Emit(ModelWithKeylessView(), ctx);

        Assert.Contains("modelBuilder.Entity<ActiveUser>().ToView(@\"ActiveUser\", @\"dbo\")", output);
        Assert.Contains("modelBuilder.Entity<ActiveUser>().HasNoKey();", output);
        Assert.DoesNotContain("HasKey(", output);
        Assert.DoesNotContain(".ToTable(", output);
    }

    [Fact]
    public void EntityEmitter_View_EmitsPropertiesWithoutNre()
    {
        var model = ModelWithKeylessView();
        var view = model.Classes[0];
        var ctx = new GenerationContext { Provider = "SqlServer" };

        var output = EntityEmitter.Emit(model, view, new System.Collections.Generic.List<AssociationLayout.Nav>(), ctx);

        Assert.Contains("public partial class ActiveUser", output);
        Assert.Contains("Id { get; set; }", output);
        Assert.Contains("Name { get; set; }", output);
    }

    // ---- XML round-trip preserves IsView + keyless ----

    [Fact]
    public void Efml_RoundTrip_PreservesViewAndKeyless()
    {
        var model = ModelWithKeylessView();
        var doc = EfmlWriter.Write(model);
        var reloaded = EfmlReader.Read(doc);

        var view = reloaded.Classes.Single(c => c.Name == "ActiveUser");
        Assert.True(view.IsView);
        Assert.True(view.Id == null!);
        Assert.Equal(2, view.Properties.Count);
    }
}
