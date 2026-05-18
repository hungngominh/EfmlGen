using Ezy.Module.Library.Utilities;
using Ezy.Module.MSSQLRepository.Connection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace AllianceMiddlemanWebAPI.Core.Data.Categories
{
    public partial class CategoryEntities
    {
        public CategoryEntities(string connectionString)
        {
            _connectionString = connectionString;
        }

        private readonly string _connectionString;

        partial void CustomizeConfiguration(ref DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connectionString);
        }
    }

    public class CategoryDataContext : CategoryEntities
    {
        private static EzyEFConnectionSettingItem ConnManager = new EzyEFConnectionSettingItem(
       typeof(CategoryDataContext), "");

        public static CategoryDataContext GetInstance()
        {
            return GetInstance(false);
        }

        public static CategoryDataContext GetInstance(bool isDevMode)
        {
            return GetInstance(isDevMode, null);
        }

        public static CategoryDataContext GetInstance(bool isDevMode, Func<string> fGetConnectionString)
        {
            string sConnection = ConnManager.GetDataConnectionString_Postgres(fGetConnectionString, isDevMode);
            var db = new CategoryDataContext(sConnection);
            return db;
        }

        public CategoryDataContext(string connectionString)
            : base(connectionString)
        {
        }
        private Dictionary<string, object> SerializeRow(IEnumerable<string> cols,
                                                DbDataReader reader)
        {
            var result = new Dictionary<string, object>();
            foreach (var col in cols)
                result.Add(col, reader[col]);
            return result;
        }
        public IEnumerable<Dictionary<string, object>> Serialize(DbDataReader reader)
        {
            var results = new List<Dictionary<string, object>>();
            var cols = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
                cols.Add(reader.GetName(i));

            while (reader.Read())
                results.Add(SerializeRow(cols, reader));

            return results;
        }
        public virtual string SP_Exce_JsonSP(string spName, string jsonParam, out string sError)
        {
            sError = string.Empty;
            string jsonOutput = string.Empty;
            DbConnection connection = this.Database.GetDbConnection();
            bool needClose = false;
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
                needClose = true;
            }

            try
            {
                using (DbCommand cmd = connection.CreateCommand())
                {
                    if (this.Database.GetCommandTimeout().HasValue)
                        cmd.CommandTimeout = this.Database.GetCommandTimeout().Value;
                    cmd.CommandText = $"select * from dbo.\"{spName}\"('{jsonParam}')";

                    DbParameter jsonParamParameter = cmd.CreateParameter();
                    jsonParamParameter.ParameterName = "jsonParam";
                    jsonParamParameter.Direction = ParameterDirection.Input;
                    jsonParamParameter.DbType = DbType.String;
                    if (jsonParam != null)
                    {
                        jsonParamParameter.Value = jsonParam;
                        jsonParamParameter.Size = -1;
                    }
                    else
                    {
                        jsonParamParameter.Size = -1;
                        jsonParamParameter.Value = DBNull.Value;
                    }
                    cmd.Parameters.Add(jsonParamParameter);

                    DbParameter jsonOutputParameter = cmd.CreateParameter();
                    jsonOutputParameter.ParameterName = "jsonOutput";
                    jsonOutputParameter.Direction = ParameterDirection.InputOutput;
                    jsonOutputParameter.DbType = DbType.String;
                    if (jsonOutput != null)
                    {
                        jsonOutputParameter.Value = jsonOutput;
                        jsonOutputParameter.Size = -1;
                    }
                    else
                    {
                        jsonOutputParameter.Size = -1;
                        jsonOutputParameter.Value = DBNull.Value;
                    }
                    cmd.Parameters.Add(jsonOutputParameter);
                    cmd.ExecuteNonQuery();

                    if (cmd.Parameters["jsonOutput"].Value != null && !(cmd.Parameters["jsonOutput"].Value is System.DBNull))
                        jsonOutput = (string)Convert.ChangeType(cmd.Parameters["jsonOutput"].Value, typeof(string));
                    else
                        jsonOutput = default(string);
                }
            }
            catch (Exception ex)
            {
                sError = ex.Message;
            }
            finally
            {
                if (needClose)
                    connection.Close();
            }

            return jsonOutput;
        }
        public virtual Dictionary<string, object>[] GetDataByTableName(string tableName, out string sError)
        {
            sError = null;
            Dictionary<string, object>[] result = null;
            if (tableName.StartsWith("sp_"))
            {
                result = JsonHelper.DeserializeObject<Dictionary<string, object>[]>(SP_Exce_JsonSP(tableName, null, out sError));
            }
            else
            {
                DbConnection connection = this.Database.GetDbConnection();
                bool needClose = false;
                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                    needClose = true;
                }
                try
                {
                    using (DbCommand cmd = connection.CreateCommand())
                    {
                        if (this.Database.GetCommandTimeout().HasValue)
                            cmd.CommandTimeout = this.Database.GetCommandTimeout().Value;
                        cmd.CommandText = $"select * from dbo.\"{tableName}\" where \"IsDeleted\" = false";
                        using (var reader = cmd.ExecuteReader())
                        {
                            result = Serialize(reader)?.ToArray();
                        }
                    }
                }
                catch (Exception ex)
                {
                    sError = ex.Message;
                }
                finally
                {
                    if (needClose)
                        connection.Close();
                }
            }
            return result;
        }
    }
}
