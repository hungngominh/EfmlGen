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

        var classRef = CsKeywords.Escape(cls.Name);

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
                sb.Append("            this.").Append(CsKeywords.Escape(p.Name)).Append(" = ").Append(init).Append(";\r\n");
        }

        // Collection nav initializers (after property inits)
        foreach (var nav in navs)
        {
            if (nav.IsCollection)
            {
                sb.Append("            this.").Append(CsKeywords.Escape(nav.Name))
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
            sb.Append(' ').Append(CsKeywords.Escape(nav.Name)).Append(" { get; set; }\r\n");
        }

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
          .Append(CsKeywords.Escape(p.Name))
          .Append(" { get; set; }\r\n");
    }
}
