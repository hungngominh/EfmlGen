using Ezy.Module.Library.Utilities;
using Ezy.Module.MSSQLRepository.Connection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace SampleApp.Data.Categories
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
            optionsBuilder.UseSqlServer(_connectionString);
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
            string sConnection = ConnManager.GetDataConnectionString_SqlServer(fGetConnectionString, isDevMode);
            var db = new CategoryDataContext(sConnection);
            return db;
        }

        public CategoryDataContext(string connectionString)
            : base(connectionString)
        {
        }
    }
}