using System.Linq;
using EfmlGen.Db;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Xunit;

namespace EfmlGen.Tests;

// Regression: a Postgres column whose sequence was created manually and attached via
// `DEFAULT nextval(...)` (not `OWNED BY`) is reported by Npgsql's scaffolder as
// ValueGenerated=None, even though the DB clearly auto-generates the value on insert.
// Without promoting it to OnAdd, the mapper used to emit ValueGeneratedNever() alongside
// the nextval() default — telling EF to always supply the value itself, so inserts sent 0
// instead of letting the sequence assign one.
public class SequenceDefaultMapperTests
{
    private static DatabaseModel BuildDbModelWithUnownedSequenceDefault()
    {
        var model = new DatabaseModel();
        var table = new DatabaseTable { Name = "B2B_Server_Request", Schema = "dbo" };
        var idCol = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            StoreType = "integer",
            IsNullable = false,
            ValueGenerated = null,
            DefaultValueSql = "nextval('dbo.\"B2B_Sever_Request_Id_seq\"'::regclass)"
        };
        table.Columns.Add(idCol);
        table.PrimaryKey = new DatabasePrimaryKey { Table = table, Name = "PK_B2B_Server_Request" };
        table.PrimaryKey.Columns.Add(idCol);
        model.Tables.Add(table);
        return model;
    }

    [Fact]
    public void Mapper_UnownedSequenceDefault_PromotesToValueGeneratedOnAdd()
    {
        var efml = DatabaseModelMapper.Map(BuildDbModelWithUnownedSequenceDefault(), new DatabaseModelMapper.MapOptions
        {
            Name = "M",
            Namespace = "N",
            ContextNamespace = "N",
            Provider = DbProvider.Postgres
        });

        var cls = efml.Classes.Single(c => c.Name == "B2B_Server_Request");
        Assert.Equal("OnAdd", cls.Id.ValueGenerated);
        Assert.Null(cls.Id.Column.Default);
    }
}
