using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfmlGen.Db;

public enum DbProvider
{
    Postgres,
    SqlServer
}

public sealed record SchemaReadOptions(
    IReadOnlyList<string>? Schemas = null,
    IReadOnlyList<string>? Tables = null);

/// <summary>
/// Wrap EF Core's IDatabaseModelFactory để đọc DB schema cho cả Postgres + SQL Server.
/// IDatabaseModelFactory là read-only — không bao giờ ghi DB.
/// </summary>
public static class DatabaseSchemaReader
{
    public static DatabaseModel Read(
        string connectionString,
        DbProvider provider,
        SchemaReadOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        options ??= new SchemaReadOptions();
        loggerFactory ??= NullLoggerFactory.Instance;

        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);

        switch (provider)
        {
            case DbProvider.Postgres:
                new Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal.NpgsqlDesignTimeServices()
                    .ConfigureDesignTimeServices(services);
                break;
            default:
                throw new System.NotSupportedException($"Provider {provider} not yet supported in MVP.");
        }

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDatabaseModelFactory>();

        var factoryOptions = new DatabaseModelFactoryOptions(
            tables: options.Tables ?? new List<string>(),
            schemas: options.Schemas ?? new List<string>());

        return factory.Create(connectionString, factoryOptions);
    }
}
