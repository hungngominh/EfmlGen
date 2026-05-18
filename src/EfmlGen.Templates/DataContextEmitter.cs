using System.Collections.Generic;
using System.IO;

namespace EfmlGen.Templates;

/// <summary>
/// Emit {Model}DataContext.cs từ user-provided template (string-replace).
/// User cung cấp file template với placeholder {{Model}}, {{Namespace}}, {{ContextClass}}, {{Provider}}.
/// Rule: SKIP nếu file output đã tồn tại (giữ tùy biến của user).
/// </summary>
public static class DataContextEmitter
{
    public const string DefaultTemplate = """
using Ezy.Module.Library.Utilities;
using Ezy.Module.MSSQLRepository.Connection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace {{Namespace}}
{
    public partial class {{Model}}
    {
        public {{Model}}(string connectionString)
        {
            _connectionString = connectionString;
        }

        private readonly string _connectionString;

        partial void CustomizeConfiguration(ref DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.Use{{Provider}}(_connectionString);
        }
    }

    public class {{ContextClass}} : {{Model}}
    {
        private static EzyEFConnectionSettingItem ConnManager = new EzyEFConnectionSettingItem(
       typeof({{ContextClass}}), "");

        public static {{ContextClass}} GetInstance()
        {
            return GetInstance(false);
        }

        public static {{ContextClass}} GetInstance(bool isDevMode)
        {
            return GetInstance(isDevMode, null);
        }

        public static {{ContextClass}} GetInstance(bool isDevMode, Func<string> fGetConnectionString)
        {
            string sConnection = ConnManager.GetDataConnectionString_{{Provider}}(fGetConnectionString, isDevMode);
            var db = new {{ContextClass}}(sConnection);
            return db;
        }

        public {{ContextClass}}(string connectionString)
            : base(connectionString)
        {
        }
    }
}
""";

    public static string Render(string template, IReadOnlyDictionary<string, string> vars)
    {
        var result = template;
        foreach (var (key, value) in vars)
            result = result.Replace("{{" + key + "}}", value);
        return result;
    }

    /// <summary>
    /// Render và ghi file. Trả về true nếu ghi mới; false nếu skip do file đã tồn tại.
    /// </summary>
    public static bool RenderToFile(string outputPath, string template, IReadOnlyDictionary<string, string> vars)
    {
        if (File.Exists(outputPath)) return false;
        var content = Render(template, vars);
        File.WriteAllText(outputPath, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return true;
    }
}
