using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using EfmlGen.Core;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace EfmlGen.Db;

/// <summary>
/// Reads stored procedures / functions from a database into <see cref="EfComplexType"/> +
/// <see cref="EfStoredProcedure"/>. EF Core's IDatabaseModelFactory does not surface routines,
/// so this uses raw ADO.NET (read-only — never writes to the DB).
///
/// SQL Server: enumerates sys.procedures, params via sys.parameters, result-set columns via
/// sp_describe_first_result_set (wrapped per-proc; dynamic-SQL/temp-table procs throw → treated
/// as a void result with a warning).
/// Postgres: enumerates information_schema.routines + parameters; OUT params model the result set.
/// </summary>
public static class StoredProcedureReader
{
    public sealed record Result(
        List<EfComplexType> ComplexTypes,
        List<EfStoredProcedure> Procedures,
        List<string> Warnings);

    public static Result Read(string connectionString, DbProvider provider, IReadOnlyList<string> schemas, bool forceDateTime)
        => provider == DbProvider.SqlServer
            ? ReadSqlServer(connectionString, schemas, forceDateTime)
            : ReadPostgres(connectionString, schemas, forceDateTime);

    // ---------------------------------------------------------------- SQL Server

    private static Result ReadSqlServer(string connectionString, IReadOnlyList<string> schemas, bool forceDateTime)
    {
        var complexTypes = new List<EfComplexType>();
        var procedures = new List<EfStoredProcedure>();
        var warnings = new List<string>();

        using var conn = new SqlConnection(connectionString);
        conn.Open();

        var schemaSet = new HashSet<string>(schemas, StringComparer.OrdinalIgnoreCase);

        var procs = new List<(string Schema, string Name)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.name AS SchemaName, p.name AS ProcName
                FROM sys.procedures p
                JOIN sys.schemas s ON p.schema_id = s.schema_id
                ORDER BY s.name, p.name";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var schema = rdr.GetString(0);
                if (schemaSet.Count > 0 && !schemaSet.Contains(schema)) continue;
                procs.Add((schema, rdr.GetString(1)));
            }
        }

        foreach (var (schema, name) in procs)
        {
            var sp = new EfStoredProcedure
            {
                Name = name,
                Procedure = $"{schema}.{name}",
                Schema = schema,
                Guid = Guid.NewGuid()
            };

            ReadSqlServerParameters(conn, schema, name, forceDateTime, sp);

            var resultProps = TryReadSqlServerResultSet(conn, schema, name, forceDateTime, warnings);
            if (resultProps.Count > 0)
            {
                var ct = new EfComplexType { Name = name + "Result", Guid = Guid.NewGuid() };
                ct.Properties.AddRange(resultProps);
                complexTypes.Add(ct);

                sp.ReturnComplexType = ct.Name;
                foreach (var p in resultProps)
                    sp.ReturnProperties.Add(new EfReturnProperty { Name = p.Name, Column = p.Name });
            }

            procedures.Add(sp);
        }

        return new Result(complexTypes, procedures, warnings);
    }

    private static void ReadSqlServerParameters(SqlConnection conn, string schema, string name, bool forceDateTime, EfStoredProcedure sp)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT par.name, t.name AS type_name, par.max_length, par.precision, par.scale, par.is_output
            FROM sys.parameters par
            JOIN sys.types t ON par.user_type_id = t.user_type_id
            WHERE par.object_id = OBJECT_ID(@proc)
            ORDER BY par.parameter_id";
        var p = cmd.CreateParameter();
        p.ParameterName = "@proc";
        p.Value = $"{schema}.{name}";
        cmd.Parameters.Add(p);

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var paramName = rdr.GetString(0);
            if (string.IsNullOrEmpty(paramName)) continue;        // return-value slot
            paramName = paramName.TrimStart('@');

            var typeName = rdr.GetString(1);
            var maxLength = rdr.GetInt16(2);
            var precision = rdr.GetByte(3);
            var scale = rdr.GetByte(4);
            var isOutput = rdr.GetBoolean(5);

            var t = SqlServerTypeMap.Map(typeName, forceDateTime);

            var ep = new EfParameter
            {
                Name = paramName,
                Type = t.EfType,
                SqlType = typeName,
                Direction = isOutput ? EfParamDirection.InputOutput : EfParamDirection.Input
            };

            if (IsStringOrBinary(t.EfType))
            {
                if (maxLength > 0)
                {
                    // nvarchar/nchar store byte length; halve for char count.
                    var isUnicode = typeName.StartsWith("n", StringComparison.OrdinalIgnoreCase);
                    ep.Length = isUnicode ? maxLength / 2 : maxLength;
                }
            }
            else if (IsNumeric(t.EfType))
            {
                ep.Precision = precision;
                ep.Scale = scale;
            }

            sp.Parameters.Add(ep);
        }
    }

    private static List<EfProperty> TryReadSqlServerResultSet(SqlConnection conn, string schema, string name, bool forceDateTime, List<string> warnings)
    {
        var props = new List<EfProperty>();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "sys.sp_describe_first_result_set";
            cmd.CommandType = CommandType.StoredProcedure;

            var tsql = cmd.CreateParameter();
            tsql.ParameterName = "@tsql";
            tsql.Value = $"[{schema}].[{name}]";
            cmd.Parameters.Add(tsql);

            var prms = cmd.CreateParameter();
            prms.ParameterName = "@params";
            prms.Value = DBNull.Value;
            cmd.Parameters.Add(prms);

            var mode = cmd.CreateParameter();
            mode.ParameterName = "@browse_information_mode";
            mode.Value = 0;
            cmd.Parameters.Add(mode);

            using var rdr = cmd.ExecuteReader();
            int nameOrd = rdr.GetOrdinal("name");
            int nullableOrd = rdr.GetOrdinal("is_nullable");
            int typeOrd = rdr.GetOrdinal("system_type_name");
            while (rdr.Read())
            {
                var colName = rdr.IsDBNull(nameOrd) ? "" : rdr.GetString(nameOrd);
                var isNullable = !rdr.IsDBNull(nullableOrd) && Convert.ToBoolean(rdr.GetValue(nullableOrd));
                var storeType = rdr.IsDBNull(typeOrd) ? "" : rdr.GetString(typeOrd);
                props.Add(MakeResultProperty(colName, storeType, isNullable, forceDateTime, isSqlServer: true));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not determine result set for {schema}.{name} (likely dynamic SQL or temp tables): {ex.Message}");
            props.Clear();
        }
        return props;
    }

    // ---------------------------------------------------------------- Postgres

    private static Result ReadPostgres(string connectionString, IReadOnlyList<string> schemas, bool forceDateTime)
    {
        var complexTypes = new List<EfComplexType>();
        var procedures = new List<EfStoredProcedure>();
        var warnings = new List<string>();

        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();

        var schemaSet = new HashSet<string>(schemas, StringComparer.OrdinalIgnoreCase);

        var routines = new List<(string Schema, string Name, string SpecificName)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT specific_schema, routine_name, specific_name
                FROM information_schema.routines
                WHERE routine_type IN ('FUNCTION', 'PROCEDURE')
                ORDER BY specific_schema, routine_name";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var schema = rdr.GetString(0);
                if (schemaSet.Count > 0 && !schemaSet.Contains(schema)) continue;
                routines.Add((schema, rdr.GetString(1), rdr.GetString(2)));
            }
        }

        foreach (var (schema, name, specificName) in routines)
        {
            var sp = new EfStoredProcedure
            {
                Name = name,
                Procedure = $"{schema}.{name}",
                Schema = schema,
                Guid = Guid.NewGuid()
            };

            var resultProps = new List<EfProperty>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT parameter_name, data_type, parameter_mode, character_maximum_length,
                           numeric_precision, numeric_scale
                    FROM information_schema.parameters
                    WHERE specific_schema = @schema AND specific_name = @specific
                    ORDER BY ordinal_position";
                AddPgParam(cmd, "@schema", schema);
                AddPgParam(cmd, "@specific", specificName);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var paramName = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                    var dataType = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                    var mode = rdr.IsDBNull(2) ? "IN" : rdr.GetString(2);
                    int? charLen = rdr.IsDBNull(3) ? null : Convert.ToInt32(rdr.GetValue(3));
                    int? precision = rdr.IsDBNull(4) ? null : Convert.ToInt32(rdr.GetValue(4));
                    int? scale = rdr.IsDBNull(5) ? null : Convert.ToInt32(rdr.GetValue(5));

                    var t = PostgresTypeMap.Map(dataType, forceDateTime);

                    if (mode == "OUT" || mode == "TABLE")
                    {
                        resultProps.Add(MakeResultProperty(paramName, dataType, isNullable: true, forceDateTime, isSqlServer: false));
                        continue;
                    }

                    if (string.IsNullOrEmpty(paramName)) continue;

                    var ep = new EfParameter
                    {
                        Name = paramName,
                        Type = t.EfType,
                        SqlType = dataType,
                        Direction = mode == "INOUT" ? EfParamDirection.InputOutput : EfParamDirection.Input
                    };
                    if (IsStringOrBinary(t.EfType) && charLen.HasValue) ep.Length = charLen;
                    else if (IsNumeric(t.EfType)) { ep.Precision = precision; ep.Scale = scale; }

                    sp.Parameters.Add(ep);
                }
            }

            if (resultProps.Count > 0)
            {
                var ct = new EfComplexType { Name = name + "Result", Guid = Guid.NewGuid() };
                ct.Properties.AddRange(resultProps);
                complexTypes.Add(ct);

                sp.ReturnComplexType = ct.Name;
                foreach (var p in resultProps)
                    sp.ReturnProperties.Add(new EfReturnProperty { Name = p.Name, Column = p.Name });
            }

            procedures.Add(sp);
        }

        return new Result(complexTypes, procedures, warnings);
    }

    private static void AddPgParam(NpgsqlCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // ---------------------------------------------------------------- shared

    private static EfProperty MakeResultProperty(string name, string storeType, bool isNullable, bool forceDateTime, bool isSqlServer)
    {
        var t = isSqlServer
            ? ToMapped(SqlServerTypeMap.Map(storeType, forceDateTime))
            : ToMapped(PostgresTypeMap.Map(storeType, forceDateTime));

        return new EfProperty
        {
            Name = name,
            Type = t.EfType,
            IsNullable = isNullable,
            ValidateRequired = !isNullable,
            Guid = Guid.NewGuid(),
            Column = new EfColumn
            {
                Name = $"`{name}`",
                NotNull = !isNullable,
                SqlType = storeType,
                Unicode = t.Unicode
            }
        };
    }

    private readonly record struct Mapped(EfType EfType, bool Unicode);

    private static Mapped ToMapped(SqlServerTypeMap.TypeInfo t) => new(t.EfType, t.Unicode);
    private static Mapped ToMapped(PostgresTypeMap.TypeInfo t) => new(t.EfType, t.Unicode);

    private static bool IsStringOrBinary(EfType t) => t == EfType.String || t == EfType.Blob;

    private static bool IsNumeric(EfType t) =>
        t is EfType.Int16 or EfType.Int32 or EfType.Int64 or EfType.Byte or EfType.Decimal;
}
