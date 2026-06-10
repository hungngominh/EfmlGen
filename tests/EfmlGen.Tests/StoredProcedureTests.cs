using System.Linq;
using EfmlGen.Core;
using EfmlGen.Templates;
using EfmlGen.Xml;
using Xunit;

namespace EfmlGen.Tests;

public class StoredProcedureTests
{
    private static EfmlModel ModelWithStoredProcedure()
    {
        var model = new EfmlModel { Name = "Ctx", Namespace = "App", ContextNamespace = "App" };

        var ct = new EfComplexType { Name = "sp_GetUserResult" };
        ct.Properties.Add(new EfProperty
        {
            Name = "Id",
            Type = EfType.Int64,
            Column = new EfColumn { Name = "`Id`", NotNull = true, SqlType = "bigint" }
        });
        ct.Properties.Add(new EfProperty
        {
            Name = "Name",
            Type = EfType.String,
            IsNullable = true,
            Column = new EfColumn { Name = "`Name`", SqlType = "nvarchar(max)", Unicode = true }
        });
        model.ComplexTypes.Add(ct);

        var sp = new EfStoredProcedure
        {
            Name = "sp_GetUser",
            Procedure = "dbo.sp_GetUser",
            Schema = "dbo",
            ReturnComplexType = "sp_GetUserResult"
        };
        sp.ReturnProperties.Add(new EfReturnProperty { Name = "Id", Column = "Id" });
        sp.ReturnProperties.Add(new EfReturnProperty { Name = "Name", Column = "Name" });
        sp.Parameters.Add(new EfParameter
        {
            Name = "UserId",
            Type = EfType.Int64,
            SqlType = "bigint",
            Precision = 19,
            Scale = 0,
            Direction = EfParamDirection.Input
        });
        model.StoredProcedures.Add(sp);

        return model;
    }

    private static EfmlModel ModelWithVoidOutputProc()
    {
        var model = new EfmlModel { Name = "Ctx", Namespace = "App", ContextNamespace = "App" };
        var sp = new EfStoredProcedure { Name = "sp_DoThing", Procedure = "dbo.sp_DoThing", Schema = "dbo" };
        sp.Parameters.Add(new EfParameter { Name = "jsonParam", Type = EfType.String, SqlType = "nvarchar", Direction = EfParamDirection.Input });
        sp.Parameters.Add(new EfParameter { Name = "jsonOutput", Type = EfType.String, SqlType = "nvarchar", Direction = EfParamDirection.InputOutput });
        model.StoredProcedures.Add(sp);
        return model;
    }

    // ---- XML round-trip ----

    [Fact]
    public void Efml_RoundTrip_PreservesComplexTypeAndStoredProcedure()
    {
        var model = ModelWithStoredProcedure();
        var doc = EfmlWriter.Write(model);
        var reloaded = EfmlReader.Read(doc);

        var ct = Assert.Single(reloaded.ComplexTypes);
        Assert.Equal("sp_GetUserResult", ct.Name);
        Assert.Equal(2, ct.Properties.Count);

        var sp = Assert.Single(reloaded.StoredProcedures);
        Assert.Equal("sp_GetUser", sp.Name);
        Assert.Equal("dbo.sp_GetUser", sp.Procedure);
        Assert.Equal("dbo", sp.Schema);
        Assert.Equal("sp_GetUserResult", sp.ReturnComplexType);
        Assert.Equal(2, sp.ReturnProperties.Count);
        var prm = Assert.Single(sp.Parameters);
        Assert.Equal("UserId", prm.Name);
        Assert.Equal(EfType.Int64, prm.Type);
        Assert.Equal(19, prm.Precision);
    }

    [Fact]
    public void Efml_RoundTrip_PreservesParameterDirection()
    {
        var model = ModelWithVoidOutputProc();
        var doc = EfmlWriter.Write(model);
        var reloaded = EfmlReader.Read(doc);

        var sp = Assert.Single(reloaded.StoredProcedures);
        Assert.Null(sp.ReturnComplexType);
        Assert.Equal(EfParamDirection.Input, sp.Parameters[0].Direction);
        Assert.Equal(EfParamDirection.InputOutput, sp.Parameters[1].Direction);
    }

    [Fact]
    public void Writer_EmitsComplexTypeAndMethodElements()
    {
        var doc = EfmlWriter.Write(ModelWithStoredProcedure());
        var xml = doc.ToString();

        Assert.Contains("<class name=\"$ComplexTypes\">", xml);
        Assert.Contains("class=\"sp_GetUserResult\"", xml);
        Assert.Contains("procedure=\"dbo.sp_GetUser\"", xml);
        Assert.Contains("<return-property name=\"Id\" column=\"Id\" />", xml);
    }

    // ---- ComplexTypeEmitter ----

    [Fact]
    public void ComplexTypeEmitter_EmitsPartialClassWithProps()
    {
        var model = ModelWithStoredProcedure();
        var ctx = new GenerationContext { Provider = "SqlServer" };
        var output = ComplexTypeEmitter.Emit(model, model.ComplexTypes[0], ctx);

        Assert.Contains("public partial class sp_GetUserResult", output);
        Assert.Contains("public virtual long Id { get; set; }", output);
        Assert.Contains("public virtual string Name { get; set; }", output);
        Assert.Contains("partial void OnCreated();", output);
    }

    // ---- ContextEmitter: SP methods ----

    [Fact]
    public void ContextEmitter_EmitsSyncAndAsyncMethods()
    {
        var ctx = new GenerationContext { Provider = "SqlServer" };
        var output = ContextEmitter.Emit(ModelWithStoredProcedure(), ctx);

        Assert.Contains("#region Methods", output);
        Assert.Contains("public List<sp_GetUserResult> sp_GetUser (long? UserId)", output);
        Assert.Contains("public async Task<List<sp_GetUserResult>> sp_GetUserAsync (long? UserId)", output);
        Assert.Contains("cmd.CommandType = CommandType.StoredProcedure;", output);
        Assert.Contains("cmd.CommandText = @\"dbo.sp_GetUser\";", output);
        Assert.Contains("UserIdParameter.DbType = DbType.Int64;", output);
        Assert.Contains("await cmd.ExecuteReaderAsync()", output);
        Assert.Contains("row.Id = (long)Convert.ChangeType", output);
    }

    [Fact]
    public void ContextEmitter_VoidProc_UsesRefAndTuple()
    {
        var ctx = new GenerationContext { Provider = "SqlServer" };
        var output = ContextEmitter.Emit(ModelWithVoidOutputProc(), ctx);

        Assert.Contains("public void sp_DoThing (string jsonParam, ref string jsonOutput)", output);
        Assert.Contains("public async Task<Tuple<string>> sp_DoThingAsync (string jsonParam, string jsonOutput)", output);
        Assert.Contains("cmd.ExecuteNonQuery();", output);
        Assert.Contains("return new Tuple<string>(jsonOutput);", output);
    }

    // ---- Merge preserves Guid + user rename ----

    [Fact]
    public void Merge_PreservesStoredProcedureGuidAndUserRename()
    {
        var existing = ModelWithStoredProcedure();
        existing.StoredProcedures[0].Guid = System.Guid.NewGuid();
        existing.StoredProcedures[0].Name = "GetUser";   // user renamed the method
        var keptGuid = existing.StoredProcedures[0].Guid;

        var fromDb = ModelWithStoredProcedure();          // DB still calls it sp_GetUser

        var (merged, report) = EfmlMerger.Merge(fromDb, existing);

        var sp = Assert.Single(merged.StoredProcedures);
        Assert.Equal("GetUser", sp.Name);
        Assert.Equal(keptGuid, sp.Guid);
        Assert.Empty(report.AddedStoredProcedures);
        Assert.Empty(report.RemovedStoredProcedures);
        Assert.Single(report.RenamedStoredProcedures);
    }
}
