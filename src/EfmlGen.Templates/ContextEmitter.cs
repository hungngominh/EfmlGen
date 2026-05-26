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
            sb.Append("        public virtual DbSet<").Append(CsKeywords.Escape(c.Name)).Append("> ").Append(CsKeywords.Escape(c.EntitySet)).Append("\r\n");
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
            sb.Append("            this.").Append(c.Name).Append("Mapping(modelBuilder);\r\n");
            sb.Append("            this.Customize").Append(c.Name).Append("Mapping(modelBuilder);\r\n");
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
        sb.Append("        #region ").Append(c.Name).Append(" Mapping\r\n");
        sb.Append("\r\n");
        sb.Append("        private void ").Append(c.Name).Append("Mapping(ModelBuilder modelBuilder)\r\n");
        sb.Append("        {\r\n");

        var tableName = UnquoteBackticks(c.Table);
        var classRef = CsKeywords.Escape(c.Name);
        sb.Append("            modelBuilder.Entity<").Append(classRef).Append(">().ToTable(@\"")
          .Append(tableName).Append("\", @\"").Append(c.Schema).Append("\");\r\n");

        EmitPropertyMapping(sb, classRef, c.Id);
        foreach (var p in c.Properties)
            EmitPropertyMapping(sb, classRef, p);

        sb.Append("            modelBuilder.Entity<").Append(classRef).Append(">().HasKey(@\"")
          .Append(c.Id.Name).Append("\");\r\n");
        sb.Append("        }\r\n");
        sb.Append("\r\n");
        sb.Append("        partial void Customize").Append(c.Name).Append("Mapping(ModelBuilder modelBuilder);\r\n");
        sb.Append("\r\n");
        sb.Append("        #endregion\r\n");
        sb.Append("\r\n");
    }

    private static void EmitPropertyMapping(StringBuilder sb, string className, EfProperty p)
    {
        sb.Append("            modelBuilder.Entity<").Append(className).Append(">().Property(x => x.")
          .Append(CsKeywords.Escape(p.Name)).Append(")");

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
            sb.Append(".HasComputedColumnSql(@\"").Append(p.Column.Computed).Append("\")");
        else if (!string.IsNullOrEmpty(p.Column.Default))
            sb.Append(".HasDefaultValueSql(@\"").Append(p.Column.Default).Append("\")");

        sb.Append(";\r\n");
    }

    private static void EmitRelationshipsMapping(StringBuilder sb, EfmlModel model)
    {
        sb.Append("        private void RelationshipsMapping(ModelBuilder modelBuilder)\r\n");
        sb.Append("        {\r\n");

        foreach (var a in model.Associations)
        {
            var fkProperties = a.End2.Multiplicity == Multiplicity.Many || a.End2.PropertyNames.Count > 0
                ? a.End2.PropertyNames
                : a.End1.PropertyNames;
            var fkArgs = string.Join(", ", fkProperties.Select(p => "@\"" + p + "\""));

            var isRequired = !(a.End1.Multiplicity == Multiplicity.ZeroOrOne || a.End2.Multiplicity == Multiplicity.ZeroOrOne);
            var isRequiredStr = isRequired ? "true" : "false";

            var end1ClassRef = CsKeywords.Escape(a.End1.ClassName);
            var end1NavRef = CsKeywords.Escape(a.End1.Name);
            var end2NavRef = CsKeywords.Escape(a.End2.Name);
            var end2ClassRef = CsKeywords.Escape(a.End2.ClassName);

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

    private static string UnquoteBackticks(string s) =>
        s.StartsWith('`') && s.EndsWith('`') ? s[1..^1] : s;
}
