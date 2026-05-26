using System.Collections.Generic;
using System.Text;
using EfmlGen.Core;

namespace EfmlGen.Templates;

public static class EntityEmitter
{
    public static string Emit(EfmlModel model, EfClass cls, IReadOnlyList<AssociationLayout.Nav> navs, GenerationContext ctx)
    {
        var sb = new StringBuilder(2048);

        HeaderEmitter.Write(sb, ctx);

        sb.Append("using System;\r\n");
        sb.Append("using System.Collections.Generic;\r\n");
        sb.Append("using System.ComponentModel;\r\n");
        sb.Append("using System.Data;\r\n");
        sb.Append("using System.Data.Common;\r\n");
        sb.Append("using System.Linq;\r\n");
        sb.Append("using System.Linq.Expressions;\r\n");
        sb.Append("\r\n");

        var classRef = CsKeywords.SafeId(cls.Name);

        sb.Append("namespace ").Append(model.Namespace).Append("\r\n");
        sb.Append("{\r\n");
        sb.Append("    public partial class ").Append(classRef).Append(" {\r\n");
        sb.Append("\r\n");

        // Constructor: name của ctor phải match class name (đã escape)
        sb.Append("        public ").Append(classRef).Append("()\r\n");
        sb.Append("        {\r\n");

        // Property default initializers (efml property order)
        foreach (var p in cls.Properties)
        {
            var init = DefaultLiterals.CSharpInitializer(p);
            if (init != null)
                sb.Append("            this.").Append(CsKeywords.SafeId(p.Name)).Append(" = ").Append(init).Append(";\r\n");
        }

        // Collection nav initializers (after property inits)
        foreach (var nav in navs)
        {
            if (nav.IsCollection)
            {
                sb.Append("            this.").Append(CsKeywords.SafeId(nav.Name))
                  .Append(" = new List<").Append(nav.TargetClass).Append(">();\r\n");
            }
        }

        sb.Append("            OnCreated();\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");

        // Id first
        EmitProperty(sb, cls.Id);

        // Then all properties
        foreach (var p in cls.Properties)
        {
            sb.Append("\r\n");
            EmitProperty(sb, p);
        }

        // Navigation properties
        foreach (var nav in navs)
        {
            sb.Append("\r\n");
            sb.Append("        public virtual ");
            if (nav.IsCollection)
                sb.Append("IList<").Append(nav.TargetClass).Append(">");
            else
                sb.Append(nav.TargetClass);
            sb.Append(' ').Append(CsKeywords.SafeId(nav.Name)).Append(" { get; set; }\r\n");
        }

        if (ctx.GenerateIndexMethods)
            EmitIndexMethods(sb, classRef, cls);

        sb.Append("\r\n");
        sb.Append("        #region Extensibility Method Definitions\r\n");
        sb.Append("\r\n");
        sb.Append("        partial void OnCreated();\r\n");
        sb.Append("\r\n");
        sb.Append("        #endregion\r\n");
        sb.Append("    }\r\n");
        sb.Append("\r\n");
        sb.Append("}\r\n");

        return sb.ToString();
    }

    private static void EmitProperty(StringBuilder sb, EfProperty p)
    {
        sb.Append("        public virtual ")
          .Append(TypeMap.CSharpTypeWithNullability(p))
          .Append(' ')
          .Append(CsKeywords.SafeId(p.Name))
          .Append(" { get; set; }\r\n");
    }

    private static void EmitIndexMethods(StringBuilder sb, string classRef, EfClass cls)
    {
        foreach (var idx in cls.Indexes)
        {
            var props = new List<EfProperty>(idx.ColumnNames.Count);
            foreach (var col in idx.ColumnNames)
            {
                var p = FindPropertyByColumn(cls, col);
                if (p != null) props.Add(p);
            }
            if (props.Count == 0) continue;

            var suffix = string.Concat(props.ConvertAll(p => IdentifierSanitizer.SafeName(p.Name)));
            var paramList = string.Join(", ", props.ConvertAll(p =>
                TypeMap.CSharpTypeWithNullability(p) + " " + ToCamel(IdentifierSanitizer.SafeName(p.Name))));
            var lambdaBody = string.Join(" && ", props.ConvertAll(p =>
                $"x.{CsKeywords.SafeId(p.Name)} == {ToCamel(IdentifierSanitizer.SafeName(p.Name))}"));

            sb.Append("\r\n");
            if (idx.IsUnique)
            {
                sb.Append("        public static ").Append(classRef).Append("? GetBy").Append(suffix)
                  .Append("(IQueryable<").Append(classRef).Append("> queryable, ").Append(paramList).Append(")\r\n");
                sb.Append("            => queryable.FirstOrDefault(x => ").Append(lambdaBody).Append(");\r\n");
            }
            else
            {
                sb.Append("        public static IQueryable<").Append(classRef).Append("> GetBy").Append(suffix)
                  .Append("(IQueryable<").Append(classRef).Append("> queryable, ").Append(paramList).Append(")\r\n");
                sb.Append("            => queryable.Where(x => ").Append(lambdaBody).Append(");\r\n");
            }
        }
    }

    private static EfProperty? FindPropertyByColumn(EfClass cls, string columnName)
    {
        foreach (var p in cls.AllProperties)
        {
            var n = p.Column.Name;
            if (n.Length >= 2 && n[0] == '`' && n[^1] == '`') n = n[1..^1];
            if (string.Equals(n, columnName, System.StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    private static string ToCamel(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsLower(name[0])) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
