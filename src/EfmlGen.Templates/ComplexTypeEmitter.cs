using System.Text;
using EfmlGen.Core;

namespace EfmlGen.Templates;

/// <summary>
/// Emits the partial class for a stored-procedure result DTO (Devart "complex type").
/// Keyless, no navigations — mirrors <see cref="EntityEmitter"/> in reduced form.
/// </summary>
public static class ComplexTypeEmitter
{
    public static string Emit(EfmlModel model, EfComplexType ct, GenerationContext ctx)
    {
        var sb = new StringBuilder(1024);

        HeaderEmitter.Write(sb, ctx);

        sb.Append("using System;\r\n");
        sb.Append("using System.Collections.Generic;\r\n");
        sb.Append("using System.ComponentModel;\r\n");
        sb.Append("using System.Data;\r\n");
        sb.Append("using System.Data.Common;\r\n");
        sb.Append("using System.Linq;\r\n");
        sb.Append("using System.Linq.Expressions;\r\n");
        sb.Append("\r\n");

        var classRef = CsKeywords.SafeId(ct.Name);

        sb.Append("namespace ").Append(model.Namespace).Append("\r\n");
        sb.Append("{\r\n");
        sb.Append("    public partial class ").Append(classRef).Append(" {\r\n");
        sb.Append("\r\n");

        sb.Append("        public ").Append(classRef).Append("()\r\n");
        sb.Append("        {\r\n");
        sb.Append("            OnCreated();\r\n");
        sb.Append("        }\r\n");

        foreach (var p in ct.Properties)
        {
            sb.Append("\r\n");
            sb.Append("        public virtual ")
              .Append(TypeMap.CSharpTypeWithNullability(p))
              .Append(' ')
              .Append(CsKeywords.SafeId(p.Name))
              .Append(" { get; set; }\r\n");
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
}
