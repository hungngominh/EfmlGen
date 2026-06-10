using System.Globalization;
using System.Linq;
using System.Text;
using EfmlGen.Core;

namespace EfmlGen.Templates;

public static class ContextEmitter
{
    public static string Emit(EfmlModel model, GenerationContext ctx)
    {
        var sb = new StringBuilder(8192);

        HeaderEmitter.Write(sb, ctx);

        sb.Append("using System;\r\n");
        sb.Append("using System.Collections.Generic;\r\n");
        sb.Append("using System.ComponentModel;\r\n");
        sb.Append("using System.Data;\r\n");
        sb.Append("using System.Data.Common;\r\n");
        sb.Append("using System.Linq;\r\n");
        sb.Append("using System.Linq.Expressions;\r\n");
        sb.Append("using System.Reflection;\r\n");
        sb.Append("using System.Threading.Tasks;\r\n");
        sb.Append("using Microsoft.EntityFrameworkCore;\r\n");
        sb.Append("using Microsoft.EntityFrameworkCore.Infrastructure;\r\n");
        sb.Append("using Microsoft.EntityFrameworkCore.Internal;\r\n");
        sb.Append("using Microsoft.EntityFrameworkCore.Metadata;\r\n");
        sb.Append("\r\n");

        sb.Append("namespace ").Append(model.ContextNamespace).Append("\r\n");
        sb.Append("{\r\n");
        sb.Append("\r\n");
        sb.Append("    public partial class ").Append(model.Name).Append(" : DbContext\r\n");
        sb.Append("    {\r\n");
        sb.Append("\r\n");

        sb.Append("        public ").Append(model.Name).Append("() :\r\n");
        sb.Append("            base()\r\n");
        sb.Append("        {\r\n");
        sb.Append("            OnCreated();\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");

        sb.Append("        public ").Append(model.Name).Append("(DbContextOptions<").Append(model.Name).Append("> options) :\r\n");
        sb.Append("            base(options)\r\n");
        sb.Append("        {\r\n");
        sb.Append("            OnCreated();\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");

        sb.Append("        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)\r\n");
        sb.Append("        {\r\n");
        sb.Append("            if (!optionsBuilder.IsConfigured ||\r\n");
        sb.Append("                (!optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Any(ext => !string.IsNullOrEmpty(ext.ConnectionString) || ext.Connection != null) &&\r\n");
        sb.Append("                 !optionsBuilder.Options.Extensions.Any(ext => !(ext is RelationalOptionsExtension) && !(ext is CoreOptionsExtension))))\r\n");
        sb.Append("            {\r\n");
        sb.Append("                optionsBuilder.Use").Append(ctx.Provider)
          .Append("(@\"").Append(ctx.ConnectionString ?? "").Append("\");\r\n");
        sb.Append("            }\r\n");
        sb.Append("            CustomizeConfiguration(ref optionsBuilder);\r\n");
        sb.Append("            base.OnConfiguring(optionsBuilder);\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");
        sb.Append("        partial void CustomizeConfiguration(ref DbContextOptionsBuilder optionsBuilder);\r\n");
        sb.Append("\r\n");

        foreach (var c in model.Classes)
        {
            sb.Append("        public virtual DbSet<").Append(CsKeywords.SafeId(c.Name)).Append("> ").Append(CsKeywords.SafeId(c.EntitySet)).Append("\r\n");
            sb.Append("        {\r\n");
            sb.Append("            get;\r\n");
            sb.Append("            set;\r\n");
            sb.Append("        }\r\n");
            sb.Append("\r\n");
        }

        sb.Append("        protected override void OnModelCreating(ModelBuilder modelBuilder)\r\n");
        sb.Append("        {\r\n");
        sb.Append("            base.OnModelCreating(modelBuilder);\r\n");
        sb.Append("\r\n");
        foreach (var c in model.Classes)
        {
            var nm = IdentifierSanitizer.SafeName(c.Name);
            sb.Append("            this.").Append(nm).Append("Mapping(modelBuilder);\r\n");
            sb.Append("            this.Customize").Append(nm).Append("Mapping(modelBuilder);\r\n");
            sb.Append("\r\n");
        }
        sb.Append("            RelationshipsMapping(modelBuilder);\r\n");
        sb.Append("            CustomizeMapping(ref modelBuilder);\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");

        foreach (var c in model.Classes)
            EmitClassMapping(sb, c);

        EmitRelationshipsMapping(sb, model);

        sb.Append("        partial void CustomizeMapping(ref ModelBuilder modelBuilder);\r\n");
        sb.Append("\r\n");

        EmitStoredProcedures(sb, model);

        sb.Append("        public bool HasChanges()\r\n");
        sb.Append("        {\r\n");
        sb.Append("            return ChangeTracker.Entries().Any(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added || e.State == Microsoft.EntityFrameworkCore.EntityState.Modified || e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted);\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");
        sb.Append("        partial void OnCreated();\r\n");
        sb.Append("    }\r\n");
        sb.Append("}\r\n");

        return sb.ToString();
    }

    private static void EmitClassMapping(StringBuilder sb, EfClass c)
    {
        var classMethodName = IdentifierSanitizer.SafeName(c.Name);
        sb.Append("        #region ").Append(classMethodName).Append(" Mapping\r\n");
        sb.Append("\r\n");
        sb.Append("        private void ").Append(classMethodName).Append("Mapping(ModelBuilder modelBuilder)\r\n");
        sb.Append("        {\r\n");

        var tableName = UnquoteBackticks(c.Table);
        var classRef = CsKeywords.SafeId(c.Name);
        var mapMethod = c.IsView ? "ToView" : "ToTable";
        sb.Append("            modelBuilder.Entity<").Append(classRef).Append(">().").Append(mapMethod).Append("(@\"")
          .Append(tableName).Append("\", @\"").Append(c.Schema).Append("\");\r\n");

        if (c.Id != null!)
            EmitPropertyMapping(sb, classRef, c.Id);
        foreach (var p in c.Properties)
            EmitPropertyMapping(sb, classRef, p);

        if (c.Id != null!)
            sb.Append("            modelBuilder.Entity<").Append(classRef).Append(">().HasKey(@\"")
              .Append(IdentifierSanitizer.SafeName(c.Id.Name)).Append("\");\r\n");
        else
            sb.Append("            modelBuilder.Entity<").Append(classRef).Append(">().HasNoKey();\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");
        sb.Append("        partial void Customize").Append(classMethodName).Append("Mapping(ModelBuilder modelBuilder);\r\n");
        sb.Append("\r\n");
        sb.Append("        #endregion\r\n");
        sb.Append("\r\n");
    }

    private static void EmitPropertyMapping(StringBuilder sb, string className, EfProperty p)
    {
        sb.Append("            modelBuilder.Entity<").Append(className).Append(">().Property(x => x.")
          .Append(CsKeywords.SafeId(p.Name)).Append(")");

        var colName = UnquoteBackticks(p.Column.Name);
        sb.Append(".HasColumnName(@\"").Append(colName).Append("\")");

        if (!string.IsNullOrEmpty(p.Column.SqlType))
            sb.Append(".HasColumnType(@\"").Append(p.Column.SqlType).Append("\")");

        if (p.Column.NotNull)
            sb.Append(".IsRequired()");

        if (p.ValueGenerated == "OnAdd")
            sb.Append(".ValueGeneratedOnAdd()");
        else if (p.ValueGenerated == "OnAddOrUpdate")
            sb.Append(".ValueGeneratedOnAddOrUpdate()");
        else
            sb.Append(".ValueGeneratedNever()");

        if (p.IsRowVersion)
            sb.Append(".IsRowVersion()");
        else if (p.IsConcurrencyToken)
            sb.Append(".IsConcurrencyToken()");

        if (p.Column.Length.HasValue)
            sb.Append(".HasMaxLength(").Append(p.Column.Length.Value.ToString(CultureInfo.InvariantCulture)).Append(")");

        if (p.Column.Precision.HasValue && p.Column.Scale.HasValue)
            sb.Append(".HasPrecision(")
              .Append(p.Column.Precision.Value.ToString(CultureInfo.InvariantCulture))
              .Append(", ")
              .Append(p.Column.Scale.Value.ToString(CultureInfo.InvariantCulture))
              .Append(")");

        if (!string.IsNullOrEmpty(p.Column.Computed))
            sb.Append(".HasComputedColumnSql(@\"").Append(EscapeVerbatim(p.Column.Computed)).Append("\")");
        else if (!string.IsNullOrEmpty(p.Column.Default))
            sb.Append(".HasDefaultValueSql(@\"").Append(EscapeVerbatim(p.Column.Default)).Append("\")");

        sb.Append(";\r\n");
    }

    // Escape `"` for inclusion inside a C# verbatim string (@"...").
    // Raw SQL from columns like PG sequences contains `"` around identifiers, e.g. nextval('dbo."X_seq"'::regclass).
    private static string EscapeVerbatim(string value)
        => value.Replace("\"", "\"\"");

    private static void EmitRelationshipsMapping(StringBuilder sb, EfmlModel model)
    {
        sb.Append("        private void RelationshipsMapping(ModelBuilder modelBuilder)\r\n");
        sb.Append("        {\r\n");

        foreach (var a in model.Associations)
        {
            var fkProperties = a.End2.Multiplicity == Multiplicity.Many || a.End2.PropertyNames.Count > 0
                ? a.End2.PropertyNames
                : a.End1.PropertyNames;
            // HasForeignKey args reference the C# property names on the entity, which may have been
            // sanitized if the underlying column name wasn't a valid identifier.
            var fkArgs = string.Join(", ", fkProperties.Select(p => "@\"" + IdentifierSanitizer.SafeName(p) + "\""));

            var isRequired = !(a.End1.Multiplicity == Multiplicity.ZeroOrOne || a.End2.Multiplicity == Multiplicity.ZeroOrOne);
            var isRequiredStr = isRequired ? "true" : "false";

            var end1ClassRef = CsKeywords.SafeId(a.End1.ClassName);
            var end1NavRef = CsKeywords.SafeId(a.End1.Name);
            var end2NavRef = CsKeywords.SafeId(a.End2.Name);
            var end2ClassRef = CsKeywords.SafeId(a.End2.ClassName);

            var cascadeSuffix = a.CascadeDelete ? ".OnDelete(DeleteBehavior.Cascade)" : "";

            if (a.Cardinality == Cardinality.OneToOne)
            {
                sb.Append("            modelBuilder.Entity<").Append(end1ClassRef)
                  .Append(">().HasOne(x => x.").Append(end2NavRef)
                  .Append(").WithOne(op => op.").Append(end1NavRef)
                  .Append(").HasForeignKey<").Append(end2ClassRef).Append(">(").Append(fkArgs)
                  .Append(").IsRequired(").Append(isRequiredStr).Append(")")
                  .Append(cascadeSuffix).Append(";\r\n");
            }
            else
            {
                sb.Append("            modelBuilder.Entity<").Append(end1ClassRef)
                  .Append(">().HasMany(x => x.").Append(end2NavRef)
                  .Append(").WithOne(op => op.").Append(end1NavRef)
                  .Append(").HasForeignKey(").Append(fkArgs)
                  .Append(").IsRequired(").Append(isRequiredStr).Append(")")
                  .Append(cascadeSuffix).Append(";\r\n");

                sb.Append("            modelBuilder.Entity<").Append(end2ClassRef)
                  .Append(">().HasOne(x => x.").Append(end1NavRef)
                  .Append(").WithMany(op => op.").Append(end2NavRef)
                  .Append(").HasForeignKey(").Append(fkArgs)
                  .Append(").IsRequired(").Append(isRequiredStr).Append(")")
                  .Append(cascadeSuffix).Append(";\r\n");
            }
        }

        sb.Append("        }\r\n");
        sb.Append("\r\n");
    }

    private static void EmitStoredProcedures(StringBuilder sb, EfmlModel model)
    {
        if (model.StoredProcedures.Count == 0) return;

        sb.Append("        #region Methods\r\n");
        sb.Append("\r\n");

        foreach (var sp in model.StoredProcedures)
        {
            EmitStoredProcedure(sb, model, sp, async: false);
            sb.Append("\r\n");
            EmitStoredProcedure(sb, model, sp, async: true);
            sb.Append("\r\n");
        }

        sb.Append("        #endregion\r\n");
        sb.Append("\r\n");
    }

    private static bool IsRefType(EfType t) => t == EfType.String || t == EfType.Blob;

    // Signature type: value types become nullable (long?), reference types stay (string, byte[]).
    private static string ParamSigType(EfParameter p)
    {
        var bt = TypeMap.ToCSharp(p.Type);
        return IsRefType(p.Type) ? bt : bt + "?";
    }

    private static bool IsOutput(EfParameter p) =>
        p.Direction == EfParamDirection.InputOutput || p.Direction == EfParamDirection.Output;

    private static void EmitStoredProcedure(StringBuilder sb, EfmlModel model, EfStoredProcedure sp, bool async)
    {
        var name = CsKeywords.SafeId(sp.Name) + (async ? "Async" : "");
        var hasResult = !string.IsNullOrEmpty(sp.ReturnComplexType);
        var resultType = hasResult ? CsKeywords.SafeId(sp.ReturnComplexType!) : null;
        var outParams = sp.Parameters.Where(IsOutput).ToList();

        // ---- signature ----
        sb.Append("        public ");
        if (async)
        {
            sb.Append("async ");
            if (hasResult)
                sb.Append("Task<List<").Append(resultType).Append(">> ");
            else if (outParams.Count > 0)
                sb.Append("Task<Tuple<")
                  .Append(string.Join(", ", outParams.Select(ParamSigType)))
                  .Append(">> ");
            else
                sb.Append("Task ");
        }
        else
        {
            sb.Append(hasResult ? "List<" + resultType + "> " : "void ");
        }

        sb.Append(name).Append(" (");
        sb.Append(string.Join(", ", sp.Parameters.Select(p =>
        {
            var refPrefix = (!async && IsOutput(p)) ? "ref " : "";
            return refPrefix + ParamSigType(p) + " " + p.Name;
        })));
        sb.Append(")\r\n");
        sb.Append("        {\r\n");
        sb.Append("\r\n");

        if (hasResult)
            sb.Append("            List<").Append(resultType).Append("> result = new List<").Append(resultType).Append(">();\r\n");

        sb.Append("            DbConnection connection = this.Database.GetDbConnection();\r\n");
        sb.Append("            bool needClose = false;\r\n");
        sb.Append("            if (connection.State != ConnectionState.Open)\r\n");
        sb.Append("            {\r\n");
        sb.Append(async ? "                await connection.OpenAsync();\r\n" : "                connection.Open();\r\n");
        sb.Append("                needClose = true;\r\n");
        sb.Append("            }\r\n");
        sb.Append("\r\n");
        sb.Append("            try\r\n");
        sb.Append("            {\r\n");
        sb.Append("                using (DbCommand cmd = connection.CreateCommand())\r\n");
        sb.Append("                {\r\n");
        sb.Append("                    if (this.Database.GetCommandTimeout().HasValue)\r\n");
        sb.Append("                        cmd.CommandTimeout = this.Database.GetCommandTimeout().Value;\r\n");
        sb.Append("                    cmd.CommandType = CommandType.StoredProcedure;\r\n");
        sb.Append("                    cmd.CommandText = @\"").Append(sp.Procedure).Append("\";\r\n");
        sb.Append("\r\n");

        foreach (var p in sp.Parameters)
            EmitParameterBlock(sb, p);

        if (hasResult)
            EmitResultReader(sb, model, sp, resultType!, async);
        else
        {
            sb.Append(async ? "                    await cmd.ExecuteNonQueryAsync();\r\n" : "                    cmd.ExecuteNonQuery();\r\n");
            sb.Append("\r\n");
            foreach (var p in outParams)
                EmitOutputReadback(sb, p);
        }

        sb.Append("                }\r\n");
        sb.Append("            }\r\n");
        sb.Append("            finally\r\n");
        sb.Append("            {\r\n");
        sb.Append("                if (needClose)\r\n");
        sb.Append("                    connection.Close();\r\n");
        sb.Append("            }\r\n");

        if (hasResult)
            sb.Append("            return result;\r\n");
        else if (async && outParams.Count > 0)
            sb.Append("            return new Tuple<")
              .Append(string.Join(", ", outParams.Select(ParamSigType)))
              .Append(">(")
              .Append(string.Join(", ", outParams.Select(p => p.Name)))
              .Append(");\r\n");

        sb.Append("        }\r\n");
    }

    private static void EmitParameterBlock(StringBuilder sb, EfParameter p)
    {
        var v = p.Name + "Parameter";
        var dir = p.Direction switch
        {
            EfParamDirection.InputOutput => "InputOutput",
            EfParamDirection.Output => "Output",
            EfParamDirection.ReturnValue => "ReturnValue",
            _ => "Input"
        };

        sb.Append("                    DbParameter ").Append(v).Append(" = cmd.CreateParameter();\r\n");
        sb.Append("                    ").Append(v).Append(".ParameterName = \"").Append(p.Name).Append("\";\r\n");
        sb.Append("                    ").Append(v).Append(".Direction = ParameterDirection.").Append(dir).Append(";\r\n");
        sb.Append("                    ").Append(v).Append(".DbType = DbType.").Append(TypeMap.ToDbType(p.Type)).Append(";\r\n");
        if (p.Precision.HasValue)
            sb.Append("                    ").Append(v).Append(".Precision = ").Append(p.Precision.Value.ToString(CultureInfo.InvariantCulture)).Append(";\r\n");
        if (p.Scale.HasValue)
            sb.Append("                    ").Append(v).Append(".Scale = ").Append(p.Scale.Value.ToString(CultureInfo.InvariantCulture)).Append(";\r\n");

        var check = IsRefType(p.Type) ? p.Name + " != null" : p.Name + ".HasValue";
        var valExpr = IsRefType(p.Type) ? p.Name : p.Name + ".Value";

        sb.Append("                    if (").Append(check).Append(")\r\n");
        sb.Append("                    {\r\n");
        sb.Append("                        ").Append(v).Append(".Value = ").Append(valExpr).Append(";\r\n");
        sb.Append("                    }\r\n");
        sb.Append("                    else\r\n");
        sb.Append("                    {\r\n");
        sb.Append("                        ").Append(v).Append(".Size = -1;\r\n");
        sb.Append("                        ").Append(v).Append(".Value = DBNull.Value;\r\n");
        sb.Append("                    }\r\n");
        sb.Append("                    cmd.Parameters.Add(").Append(v).Append(");\r\n");
    }

    private static void EmitResultReader(StringBuilder sb, EfmlModel model, EfStoredProcedure sp, string resultType, bool async)
    {
        var execute = async ? "await cmd.ExecuteReaderAsync()" : "cmd.ExecuteReader()";
        sb.Append("                    using (IDataReader reader = ").Append(execute).Append(")\r\n");
        sb.Append("                    {\r\n");
        sb.Append("                        var fieldNames = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToArray();\r\n");
        sb.Append("                        while (reader.Read())\r\n");
        sb.Append("                        {\r\n");
        sb.Append("                            ").Append(resultType).Append(" row = new ").Append(resultType).Append("();\r\n");

        var returns = EffectiveReturnProperties(model, sp);
        var ctProps = ComplexTypePropertyLookup(model, sp);
        var single = returns.Count == 1;

        foreach (var (rName, rColumn) in returns)
        {
            var prop = ctProps.TryGetValue(rName, out var pp) ? pp : null;
            var castType = prop != null ? TypeMap.ToCSharp(prop.Type) : "object";
            var nullable = prop != null && prop.IsNullable;
            var nameRef = CsKeywords.SafeId(rName);

            if (single)
            {
                sb.Append("                            if (fieldNames.Length == 1 && string.IsNullOrEmpty(fieldNames[0]))\r\n");
                sb.Append("                                row.").Append(nameRef).Append(" = (").Append(castType)
                  .Append(")Convert.ChangeType(reader.GetValue(0), typeof(").Append(castType).Append("));\r\n");
                sb.Append("                            else\r\n");
            }

            sb.Append("                            if (fieldNames.Contains(\"").Append(rColumn)
              .Append("\") && !reader.IsDBNull(reader.GetOrdinal(\"").Append(rColumn).Append("\")))\r\n");
            sb.Append("                                row.").Append(nameRef).Append(" = (").Append(castType)
              .Append(")Convert.ChangeType(reader.GetValue(reader.GetOrdinal(@\"").Append(rColumn)
              .Append("\")), typeof(").Append(castType).Append("));\r\n");
            if (nullable)
            {
                sb.Append("                            else\r\n");
                sb.Append("                                row.").Append(nameRef).Append(" = null;\r\n");
            }
            sb.Append("\r\n");
        }

        sb.Append("                            result.Add(row);\r\n");
        sb.Append("                        }\r\n");
        sb.Append("                    }\r\n");
    }

    private static void EmitOutputReadback(StringBuilder sb, EfParameter p)
    {
        var sigType = ParamSigType(p);
        var baseType = TypeMap.ToCSharp(p.Type);
        sb.Append("                    if (cmd.Parameters[\"").Append(p.Name).Append("\"].Value != null && !(cmd.Parameters[\"")
          .Append(p.Name).Append("\"].Value is System.DBNull))\r\n");
        sb.Append("                        ").Append(p.Name).Append(" = (").Append(sigType)
          .Append(")Convert.ChangeType(cmd.Parameters[\"").Append(p.Name).Append("\"].Value, typeof(").Append(baseType).Append("));\r\n");
        sb.Append("                    else\r\n");
        sb.Append("                        ").Append(p.Name).Append(" = default(").Append(sigType).Append(");\r\n");
    }

    private static System.Collections.Generic.List<(string Name, string Column)> EffectiveReturnProperties(EfmlModel model, EfStoredProcedure sp)
    {
        if (sp.ReturnProperties.Count > 0)
            return sp.ReturnProperties.Select(rp => (rp.Name, rp.Column)).ToList();

        var ct = model.ComplexTypes.FirstOrDefault(c => c.Name == sp.ReturnComplexType);
        if (ct != null)
            return ct.Properties.Select(p => (p.Name, UnquoteBackticks(p.Column.Name))).ToList();

        return new();
    }

    private static System.Collections.Generic.Dictionary<string, EfProperty> ComplexTypePropertyLookup(EfmlModel model, EfStoredProcedure sp)
    {
        var dict = new System.Collections.Generic.Dictionary<string, EfProperty>(System.StringComparer.Ordinal);
        var ct = model.ComplexTypes.FirstOrDefault(c => c.Name == sp.ReturnComplexType);
        if (ct != null)
            foreach (var p in ct.Properties)
                dict[p.Name] = p;
        return dict;
    }

    private static string UnquoteBackticks(string s) =>
        s.StartsWith('`') && s.EndsWith('`') ? s[1..^1] : s;
}
