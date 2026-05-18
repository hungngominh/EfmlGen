# EfmlGen — Design Document

> Tool replicate workflow của Devart Entity Developer: **Database → `.efml` → `.cs`**, full open-source, CLI-first.

---

## 1. Mục tiêu

| # | Mục tiêu | Ưu tiên |
|---|---|---|
| G1 | Reverse-engineer DB schema → `.efml` đúng format Devart (XML namespace `http://devart.com/schemas/EntityDeveloper/1.0`) | P0 |
| G2 | Re-gen `.efml` mà **giữ nguyên `p1:Guid` cũ** và tùy biến do user edit tay (renamed class, validation…) | P0 |
| G3 | Gen file `.cs` partial entity từ `.efml`, **byte-identical** với output Entity Developer (header, indent, thứ tự property…) | P0 |
| G4 | Gen `DbContext` + Fluent API Configuration | P1 |
| G5 | Gen navigation properties từ `<associations>` | P1 |
| G6 | Hỗ trợ PostgreSQL + SQL Server | P0 (Postgres trước) |

**Non-goal:**
- Không làm GUI designer (giai đoạn này — CLI là đủ).
- Không tự sinh migration; chỉ scaffold.
- Không hỗ trợ EF6 / Linq-to-SQL.

---

## 2. Pipeline tổng

```
┌──────────┐  scaffold-efml   ┌──────────┐  gen-code   ┌────────────┐
│ Database │ ───────────────▶ │  .efml   │ ──────────▶ │ .cs files  │
└──────────┘   (Module 1+2)   └──────────┘ (Module 3)  └────────────┘
                                   ▲
                                   │ merge với efml cũ
                                   │ (giữ GUID + custom)
                              ┌─────────┐
                              │ old.efml│
                              └─────────┘
```

3 lệnh CLI:
- `efmlgen scaffold-efml --provider postgres --conn "..." --out Model.efml` (chạy Module 1+2, merge nếu file tồn tại)
- `efmlgen gen-code --efml Model.efml --out src/Entities/` (Module 3)
- `efmlgen sync --provider postgres --conn "..." --efml Model.efml --out src/Entities/` (full pipeline)

---

## 2.1. Full file inventory (đã reverse-engineer từ `E:\Work\CoShareAsyncAPI\AllianceMiddlemanWebAPI.Core\Data\Categories`)

Mỗi model (`{Model}` = vd `CategoryEntities`) sinh ra **3 loại file** với 3 chế độ regen khác nhau:

### A. Auto-generated mỗi lần (overwrite hoàn toàn)

| File | Nội dung | Số lượng |
|---|---|---|
| `{Model}.efml` | XML source — ghi lại sau khi merge với schema DB | 1 |
| `{Model}.{Model}.cs` | DbContext chính: `DbSet<>` cho từng entity + `OnModelCreating` + `{Class}Mapping()` cho từng class + `RelationshipsMapping()` | 1 |
| `{Model}.{ClassName}.cs` | Partial class entity (properties + ctor + `OnCreated`) | N (mỗi class 1 file) |
| `{Model}.info` | Plain text: `"Model code generation succeeded."` | 1 |
| `{Model}.{Diagram}.view` | Diagram layout XML (Devart ED format) — auto grid layout. ED tự vẽ FK lines từ `<associations>` của efml. Overwrite mỗi lần (user-edited layout không preserve). Tên diagram default `Diagram1`, override bằng `--diagram-name`. Disable bằng `--skip-view`. | 1 |

### B. Generated 1 lần, không overwrite (scaffolding cho user code)

| File | Nội dung | Khi nào gen |
|---|---|---|
| `{Model}DataContext.cs` | User-facing wrapper. **User cung cấp content qua input** (CLI flag `--datacontext-template <file>` hoặc paste vào prompt khi scaffold). Tool chỉ replace placeholder `{Model}` / `{Namespace}` / `{ContextClass}`. | Lần đầu — **skip nếu tồn tại** |
| `{Model}.edps` | Entity Developer Project Settings (connection string, generation options) | Lần đầu — nếu có thì merge connection string |

### C. KHÔNG gen (user tự code)

| File | Lý do |
|---|---|
| `Ext/{ClassName}.cs` | User tự code tay (confirm từ user) |
| `IProjectEntity.cs` | User đã có sẵn trong project (confirm từ user) |

### D. Optional / không cần gen ở MVP

| File | Lý do bỏ |
|---|---|
| `{Model}.BatchJob.cs` | File này là entity được user thêm tay vào model rồi gen — được cover bởi rule A |

### Quan sát quan trọng từ file mẫu

1. **Header timestamp** cùng giá trị trong tất cả file gen 1 lần: `4/13/2026 6:02:01 PM` → format `M/d/yyyy h:mm:ss tt` culture invariant
2. **DbContext có hardcoded connection string** trong `OnConfiguring` (từ `.edps`): `optionsBuilder.UseNpgsql(@"Host=...;Username=...;Database=...");` — nhưng **bị override bởi `CustomizeConfiguration` partial** mà wrapper `DataContext` implement
3. **Mỗi class có 2 partial method extension:**
   - `OnCreated()` trong entity class
   - `Customize{ClassName}Mapping(ModelBuilder)` trong DbContext
4. **Provider dispatch trong template:**
   - Postgres → `UseNpgsql`, `HasColumnType(@"int4"|"bool"|"numeric"|"uuid")`, default `uuid_generate_v7()`
   - SQL Server → `UseSqlServer`, `HasColumnType(@"int"|"bit"|"decimal"|"uniqueidentifier")`, default `NEWSEQUENTIALID()`
5. **`RelationshipsMapping`** dịch `<association>` thành cặp `HasMany().WithOne().HasForeignKey()` + `HasOne().WithMany().HasForeignKey()` (gen cả 2 chiều)
6. **Navigation property trong entity class:**
   - One side: `public virtual IList<X> {AssocName1} { get; set; }` + init `new List<X>()` trong ctor
   - Many side: `public virtual X {AssocName} { get; set; }`
7. **DataContext wrapper hardcoded `Ezy.Module.MSSQLRepository.Connection.EzyEFConnectionSettingItem`** + `GetDataConnectionString_Postgres` — đây là internal framework của bạn → **cần config được provider/namespace** trong template

---

## 3. Kiến trúc giải pháp

```
e:\Work\EntityDeveloperLocal\
├── DESIGN.md                       ← file này
├── EfmlGen.sln
├── src/
│   ├── EfmlGen.Core/               ← domain model: EfmlModel, EfClass, EfProperty, EfAssociation
│   ├── EfmlGen.Db/                 ← DB schema reader (Postgres + SqlServer)
│   ├── EfmlGen.Xml/                ← .efml read/write (XDocument)
│   ├── EfmlGen.Templates/          ← Scriban templates + renderer
│   └── EfmlGen.Cli/                ← dotnet tool entry (System.CommandLine)
├── samples/
│   ├── CategoryEntities.efml       ← input mẫu user đã cung cấp
│   └── Mioto_VehicleOwner.cs       ← output mẫu (golden file)
└── tests/
    ├── EfmlGen.Core.Tests/
    ├── EfmlGen.Xml.Tests/
    └── EfmlGen.Templates.Tests/    ← golden-file tests (so byte-by-byte)
```

### Tech stack

| Concern | Choice | Lý do |
|---|---|---|
| Runtime | .NET 8 | LTS, cross-platform |
| DB schema reader | `Microsoft.EntityFrameworkCore.Design` + `Npgsql.EntityFrameworkCore.PostgreSQL` + `Microsoft.EntityFrameworkCore.SqlServer` | Có sẵn `IDatabaseModelFactory` — engine của `dotnet ef dbcontext scaffold`. Không phải tự viết SQL meta-query |
| Template engine | **Scriban** | Cross-platform, không cần VS host, syntax sạch, hot-reload-friendly |
| XML | `System.Xml.Linq` (XDocument) | Đủ; format efml không phức tạp |
| CLI | `System.CommandLine` (beta) | Standard MS, hỗ trợ verb tốt |
| Test | xUnit + `Verify.Xunit` | Verify framework lý tưởng cho golden-file test |
| Packaging | `dotnet tool` (global tool) | `dotnet tool install -g EfmlGen` |

---

## 4. Domain Model (`EfmlGen.Core`)

```csharp
public sealed class EfmlModel
{
    public string ContextNamespace { get; set; }
    public string Namespace { get; set; }
    public string Name { get; set; }                    // p1:name (vd "CategoryEntities")
    public Guid Guid { get; set; }                      // p1:Guid của <efcore>
    public List<EfClass> Classes { get; } = new();
    public List<EfAssociation> Associations { get; } = new();
}

public sealed class EfClass
{
    public string Name { get; set; }
    public string EntitySet { get; set; }
    public string Table { get; set; }                   // "`ConfigState`"
    public string Schema { get; set; }                  // "dbo"
    public Guid Guid { get; set; }
    public EfProperty IdProperty { get; set; }
    public List<EfProperty> Properties { get; } = new();
}

public sealed class EfProperty
{
    public string Name { get; set; }
    public EfType Type { get; set; }                    // Int64, String, Boolean, ...
    public bool IsNullable { get; set; }                // p1:nullable
    public string? ValueGenerated { get; set; }         // "OnAdd"
    public bool ValidateRequired { get; set; }
    public int? ValidateMaxLength { get; set; }
    public Guid Guid { get; set; }
    public EfColumn Column { get; set; }
}

public sealed class EfColumn
{
    public string Name { get; set; }                    // "`Id`"
    public bool NotNull { get; set; }
    public string? Default { get; set; }
    public string? SqlType { get; set; }
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool Unicode { get; set; }
}

public sealed class EfAssociation
{
    public string Name { get; set; }
    public Cardinality Cardinality { get; set; }
    public Guid Guid { get; set; }
    public EfAssociationEnd End1 { get; set; }
    public EfAssociationEnd End2 { get; set; }
}

public enum EfType { Int32, Int64, String, Boolean, DateTimeOffset, DateTime, Decimal, Guid, Blob, Double, Single, Byte, Int16 }
public enum Cardinality { OneToOne, OneToMany, ManyToMany }
```

---

## 5. Module 1: DB Schema Reader (`EfmlGen.Db`)

### Interface

```csharp
public interface IDbSchemaReader
{
    DatabaseModel Read(string connectionString, SchemaReadOptions options);
}

public sealed record SchemaReadOptions(
    string[] Schemas,
    string[] Tables,
    bool IncludeViews = false);
```

### Implementation

Wrap `IDatabaseModelFactory` từ EF Core Design:

```csharp
var services = new ServiceCollection();
services.AddEntityFrameworkDesignTimeServices();
services.AddEntityFrameworkNpgsql();
new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);

var sp = services.BuildServiceProvider();
var factory = sp.GetRequiredService<IDatabaseModelFactory>();
var dbModel = factory.Create(connStr, new DatabaseModelFactoryOptions(tables, schemas));
```

Tương tự với `Microsoft.EntityFrameworkCore.SqlServer.Design.Internal.SqlServerDesignTimeServices`.

### Output

Trả về `DatabaseModel` của EF Core (đã chứa Tables, Columns, ForeignKeys, Indexes, Sequences). Module 2 sẽ map sang `EfmlModel`.

---

## 6. Module 2: DB → efml Mapper (`EfmlGen.Db`)

### Type mapping

#### PostgreSQL

| pg `data_type` / store_type | EfType | `unicode` | Note |
|---|---|---|---|
| `bigint`, `int8` | Int64 | False | |
| `integer`, `int4` | Int32 | False | |
| `smallint`, `int2` | Int16 | False | |
| `boolean`, `bool` | Boolean | False | |
| `text`, `varchar`, `character varying` | String | True | length nếu có |
| `uuid` | Guid | False | |
| `timestamp with time zone`, `timestamptz` | DateTimeOffset | False | |
| `timestamp` | DateTime | False | |
| `numeric`, `decimal` | Decimal | False | precision/scale |
| `real`, `float4` | Single | False | |
| `double precision`, `float8` | Double | False | |
| `bytea` | Blob | False | |

#### SQL Server

| SQL Server type | EfType | `unicode` |
|---|---|---|
| `bigint` | Int64 | False |
| `int` | Int32 | False |
| `smallint` | Int16 | False |
| `bit` | Boolean | False |
| `nvarchar`, `nchar`, `ntext` | String | True |
| `varchar`, `char`, `text` | String | False |
| `uniqueidentifier` | Guid | False |
| `datetimeoffset` | DateTimeOffset | False |
| `datetime`, `datetime2`, `smalldatetime` | DateTime | False |
| `decimal`, `numeric`, `money` | Decimal | False |
| `real` | Single | False |
| `float` | Double | False |
| `varbinary`, `binary`, `image`, `timestamp`, `rowversion` | Blob | False |

### Mapping rules DB → EfmlModel

| DatabaseModel | EfmlModel |
|---|---|
| `DatabaseTable` | `EfClass { Name = Table.Name, EntitySet = Table.Name, Table = $"\`{Table.Name}\`", Schema = Table.Schema }` |
| `DatabaseColumn` thuộc PK | `EfClass.IdProperty` |
| `DatabaseColumn` khác | thêm vào `EfClass.Properties` |
| `column.IsNullable == true` | `EfProperty.IsNullable = true`, `EfColumn.NotNull = false`, `ValidateRequired = false` |
| `column.IsNullable == false` | `ValidateRequired = true`, `EfColumn.NotNull = true` |
| `column.MaxLength` | `EfColumn.Length`, `EfProperty.ValidateMaxLength` (chỉ với String) |
| `column.DefaultValueSql` | `EfColumn.Default = sql` |
| `column.ValueGenerated == OnAdd` | `EfProperty.ValueGenerated = "OnAdd"` (typical cho identity, uuid_generate_v7) |
| `column.StoreType` "numeric(20,6)" | `EfColumn.Precision=20, Scale=6, SqlType="numeric"` |
| `DatabaseForeignKey` | một `EfAssociation` (cardinality dựa trên unique constraint trên FK column: unique → OneToOne, ngược lại → OneToMany) |

### Merge logic (giữ GUID + custom)

```csharp
public EfmlModel Merge(EfmlModel fromDb, EfmlModel? existing)
{
    if (existing is null) return fromDb;

    // 1. Reuse model-level GUID
    fromDb.Guid = existing.Guid;

    // 2. Match class theo Table.Name (case-insensitive)
    foreach (var newClass in fromDb.Classes)
    {
        var oldClass = existing.Classes
            .FirstOrDefault(c => Unquote(c.Table).Equals(Unquote(newClass.Table), OrdinalIgnoreCase));

        if (oldClass is null) continue;

        // Reuse class GUID + class Name (user có thể đã rename, vd "tbl_user" → "User")
        newClass.Guid = oldClass.Guid;
        newClass.Name = oldClass.Name;
        newClass.EntitySet = oldClass.EntitySet;

        // 3. Match property theo column name
        foreach (var newProp in newClass.AllProperties)
        {
            var oldProp = oldClass.AllProperties
                .FirstOrDefault(p => Unquote(p.Column.Name).Equals(Unquote(newProp.Column.Name), OrdinalIgnoreCase));

            if (oldProp is null) continue;

            newProp.Guid = oldProp.Guid;
            newProp.Name = oldProp.Name;                          // giữ rename
            newProp.ValidateMaxLength = oldProp.ValidateMaxLength; // giữ custom validation
            // Type, IsNullable, Default → lấy từ DB (source of truth)
        }
    }

    // 4. Associations: match theo (End1.Class, End1.Property, End2.Class, End2.Property)
    // Giữ GUID + Name nếu match.

    return fromDb;
}
```

**Conflict policy:** schema DB là source of truth cho type/nullable/default. User customization chỉ giữ ở: `Name` (renamed identifier), `ValidateMaxLength`, `ValidateRequired` (nếu khác với NotNull), association multiplicity.

**Element bị xóa khỏi DB:** drop khỏi efml mới (không giữ ghost). Log warning.
**Element mới trong DB:** thêm vào efml mới với GUID mới (Guid.NewGuid()).

---

## 7. Module 3: efml → cs Generator (`EfmlGen.Xml` + `EfmlGen.Templates`)

### Template inventory

| Template file | Output | Rule |
|---|---|---|
| `Entity.scriban` | `{Model}.{ClassName}.cs` — 1 file/class | Overwrite |
| `Context.scriban` | `{Model}.{Model}.cs` — 1 file | Overwrite |
| **User-provided file** | `{Model}DataContext.cs` — 1 file | **Skip nếu tồn tại**. Nội dung do user input, tool chỉ thay placeholder |
| `Info.scriban` | `{Model}.info` — 1 file | Overwrite |
| `Edps.scriban` | `{Model}.edps` — 1 file | Merge connection string nếu tồn tại |

**Không** gen: `Ext/`, `IProjectEntity.cs`, `*.view`.

### Templates (Scriban)

#### `Entity.scriban` — gen `{Model}.{ClassName}.cs`

```scriban
//------------------------------------------------------------------------------
// This is auto-generated code.
//------------------------------------------------------------------------------
// This code was generated by Entity Developer tool using EF Core template.
// Code is generated on: {{ now | date.to_string "M/d/yyyy h:mm:ss tt" }}
//
// Changes to this file may cause incorrect behavior and will be lost if
// the code is regenerated.
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;

namespace {{ model.namespace }}
{
    public partial class {{ class.name }} {

        public {{ class.name }}()
        {
{{~ for p in class.properties_with_defaults ~}}
            this.{{ p.name }} = {{ p.csharp_default_literal }};
{{~ end ~}}
            OnCreated();
        }

        public virtual {{ class.id.csharp_type }} {{ class.id.name }} { get; set; }
{{~ for p in class.properties ~}}

        public virtual {{ p.csharp_type }} {{ p.name }} { get; set; }
{{~ end ~}}

        #region Extensibility Method Definitions

        partial void OnCreated();

        #endregion
    }

}
```

#### `Context.scriban` — gen `{Model}.{Model}.cs`

Cấu trúc match đúng file mẫu (`CategoryEntities.CategoryEntities.cs`):

```csharp
// [header comment]

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace {model.namespace}
{
    public partial class {Model} : DbContext
    {
        public {Model}() : base() { OnCreated(); }
        public {Model}(DbContextOptions<{Model}> options) : base(options) { OnCreated(); }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured || /* ... boilerplate ... */)
            {
                optionsBuilder.Use{Provider}(@"{ConnectionString from .edps}");
            }
            CustomizeConfiguration(ref optionsBuilder);
            base.OnConfiguring(optionsBuilder);
        }

        partial void CustomizeConfiguration(ref DbContextOptionsBuilder optionsBuilder);

        // foreach class: DbSet
        public virtual DbSet<{Class}> {Class} { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // foreach class:
            this.{Class}Mapping(modelBuilder);
            this.Customize{Class}Mapping(modelBuilder);

            RelationshipsMapping(modelBuilder);
            CustomizeMapping(ref modelBuilder);
        }

        // foreach class:
        #region {Class} Mapping
        private void {Class}Mapping(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<{Class}>().ToTable(@"{table}", @"{schema}");
            // foreach property in (id + properties):
            modelBuilder.Entity<{Class}>()
                .Property(x => x.{PropName})
                .HasColumnName(@"{ColumnName}")
                .HasColumnType(@"{sql-type}")     // chỉ khi efml có sql-type
                .IsRequired()                      // nếu not-null
                .ValueGenerated{OnAdd|Never}()
                .HasMaxLength({len})               // nếu có
                .HasPrecision({p}, {s})            // nếu Decimal có precision
                .HasDefaultValueSql(@"{default}"); // nếu có default
            modelBuilder.Entity<{Class}>().HasKey(@"{IdName}");
        }
        partial void Customize{Class}Mapping(ModelBuilder modelBuilder);
        #endregion

        private void RelationshipsMapping(ModelBuilder modelBuilder)
        {
            // foreach association:
            modelBuilder.Entity<{End1.Class}>()
                .HasMany(x => x.{End2.Name})
                .WithOne(op => op.{End1.Name})
                .HasForeignKey(@"{FK}")
                .IsRequired({true|false});
            modelBuilder.Entity<{End1.Class}>()
                .HasOne(x => x.{End1.Name})
                .WithMany(op => op.{End2.Name})
                .HasForeignKey(@"{FK}")
                .IsRequired({true|false});
        }

        partial void CustomizeMapping(ref ModelBuilder modelBuilder);

        public bool HasChanges()
        {
            return ChangeTracker.Entries().Any(e =>
                e.State == EntityState.Added ||
                e.State == EntityState.Modified ||
                e.State == EntityState.Deleted);
        }

        partial void OnCreated();
    }
}
```

#### `{Model}DataContext.cs` — user-provided content (SKIP nếu tồn tại)

**Cách hoạt động:** User cung cấp file template (vd `--datacontext-template ./my-datacontext.cs.tmpl`). Tool đọc file đó, thay placeholder `{{Model}}`, `{{Namespace}}`, `{{ContextClass}}`, ghi ra `{Model}DataContext.cs`. **Không** có Scriban logic phức tạp — chỉ string replace để user có toàn quyền kiểm soát.

Template mặc định (built-in, dùng khi user không cung cấp `--datacontext-template`):

```csharp
using Ezy.Module.Library.Utilities;
using Ezy.Module.MSSQLRepository.Connection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace {model.namespace}
{
    public partial class {Model}
    {
        public {Model}(string connectionString)
        {
            _connectionString = connectionString;
        }

        private readonly string _connectionString;

        partial void CustomizeConfiguration(ref DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.Use{Provider}(_connectionString);   // UseNpgsql / UseSqlServer
        }
    }

    public class {ContextClass} : {Model}    // {ContextClass} = e.g. "CategoryDataContext"
    {
        private static EzyEFConnectionSettingItem ConnManager =
            new EzyEFConnectionSettingItem(typeof({ContextClass}), "");

        public static {ContextClass} GetInstance() => GetInstance(false);
        public static {ContextClass} GetInstance(bool isDevMode) => GetInstance(isDevMode, null);
        public static {ContextClass} GetInstance(bool isDevMode, Func<string> fGetConnectionString)
        {
            string sConnection = ConnManager.GetDataConnectionString_{Provider}(fGetConnectionString, isDevMode);
            return new {ContextClass}(sConnection);
        }

        public {ContextClass}(string connectionString) : base(connectionString) { }

        // SP_Exce_JsonSP, GetDataByTableName, Serialize, SerializeRow methods (block sao chép từ template)
    }
}
```

**Note:** `{Provider}` ∈ `{Npgsql, SqlServer}`. `{ContextClass}` = config-driven (vd `CategoryDataContext`).

#### `Info.scriban` — gen `{Model}.info`

```
Model code generation succeeded.
```

#### `Edps.scriban` — gen `{Model}.edps`

Template gốc của Devart Entity Developer; phần lớn cố định, chỉ thay:
- `<Connection ConnectionString="..." Provider="Npgsql|System.Data.SqlClient" />`
- `<GeneratedFiles>` — list file `.cs` đã sinh
- `<Diagram Name="..." />` — diagram default name

### Byte-identical với Entity Developer

Để output match exact file mẫu (`Mioto_VehicleOwner.cs`):

| Rule | Cách enforce |
|---|---|
| Header timestamp format `M/d/yyyy h:mm:ss tt` | Scriban filter `\| date.to_string "M/d/yyyy h:mm:ss tt"`, culture invariant |
| Indent 4 spaces | Template hard-code, không dùng `\t` |
| Line ending `\r\n` | Set `File.WriteAllText(path, content.Replace("\n", "\r\n"))` trên platform khác Windows |
| BOM UTF-8 | `new UTF8Encoding(true)` khi ghi |
| Thứ tự property = thứ tự XML | Parse vào `List<EfProperty>`, không bao giờ sort |
| Constructor chỉ khởi tạo property có `<column default=...>` | Trong template: filter `properties \| where p.column.default != null` |
| Default literal correct theo type | Function `csharp_default_literal`: bool→`false`/`true`, decimal→`0m`, int→`0`, string→quoted |
| Blank line giữa property | Template syntax `{{~ for ~}}` + `\n\n` |
| Trailing blank line cuối file | Template end với `\n` |

**Test strategy:** golden-file test — input `samples/CategoryEntities.efml`, generate, so `File.ReadAllBytes` với `samples/Mioto_VehicleOwner.cs`. Một byte sai = test fail.

---

## 8. CLI Design (`EfmlGen.Cli`)

```
efmlgen scaffold-efml \
  --provider <postgres|sqlserver> \
  --conn "<connection-string>" \
  --schemas dbo,public \
  --tables ConfigState,Department,...    (optional, default = all) \
  --namespace AllianceMiddlemanWebAPI.Core.Data.Categories \
  --context-namespace AllianceMiddlemanWebAPI.Core.Data.Categories \
  --name CategoryEntities \
  --out ./Model/CategoryEntities.efml

efmlgen gen-code \
  --efml ./Model/CategoryEntities.efml \
  --out ./Generated/ \
  --templates ./Templates/        (optional, default = embedded)

efmlgen sync \
  --provider postgres \
  --conn "..." \
  --efml ./Model/CategoryEntities.efml \
  --code-out ./Generated/

efmlgen new-template --type entity --out ./Templates/Entity.scriban
```

### Connection string

Đọc từ:
1. `--conn` flag (ưu tiên cao nhất)
2. `--conn-env <NAME>` (vd `--conn-env PG_CONN`) — không leak secret vào shell history
3. `efmlgen.json` config file trong cwd

### Config file (optional)

```jsonc
// efmlgen.json
{
  "provider": "postgres",
  "connectionStringEnv": "PG_CONN",
  "schemas": ["dbo"],
  "namespace": "AllianceMiddlemanWebAPI.Core.Data.Categories",
  "models": [
    { "name": "CategoryEntities", "efml": "Model/CategoryEntities.efml", "tables": ["Config*", "Department"] },
    { "name": "OrderEntities", "efml": "Model/OrderEntities.efml", "tables": ["Order*"] }
  ],
  "codeOutput": "Generated/"
}
```

Cho phép split nhiều `.efml` theo group table, mỗi group 1 namespace — chạy `efmlgen sync` 1 phát gen tất cả.

---

## 9. Implementation phases

| Phase | Scope | Status |
|---|---|---|
| **P1 — efml ↔ cs** | `EfmlGen.Core` + `EfmlGen.Xml` + `EfmlGen.Templates` (Entity template only). Golden test với file mẫu user đã gửi. | ✅ Done |
| **P2 — Context + Configuration** | 2 template còn lại + association → nav property | ✅ Done |
| **P3 — DB → efml (Postgres)** | `EfmlGen.Db` cho Postgres, mapping rules, merge logic | ✅ Done |
| **P4 — CLI** | `EfmlGen.Cli` với 3 verbs, packaging dotnet tool | ✅ Done |
| **P5 — SQL Server** | Thêm provider thứ 2, share mapping rules (`SqlServerTypeMap`, dispatch trong `DatabaseSchemaReader` + `DatabaseModelMapper`, CLI + WPF chấp nhận `SqlServer`) | ✅ Done |
| **P6 — Polish** | Config file, error messages, README, sample project | In progress |

**Total MVP: ~2 tuần** (1 dev full-time).

---

## 10. Open questions — RESOLVED

| # | Câu hỏi | Resolution |
|---|---|---|
| 1 | Strip backtick từ table | ✅ Confirmed |
| 2 | Không pluralize entity-set | ✅ Confirmed |
| 3 | Self-ref association naming | ✅ Confirmed |
| 4 | Navigation `IList<X>` many-side, `X` one-side | ✅ Confirmed |
| 5 | Composite PK | ⚠️ MVP P1 chỉ single-column + warning |
| 6 | `{Model}DataContext.cs` pattern | ✅ **User input, không cố định.** Tool chỉ replace placeholder + skip-if-exists |
| 7 | `EzyEFConnectionSettingItem` cho SQL Server | ✅ User tự edit file `{Model}DataContext.cs` cho SQL — tool không cần biết |
| 8 | `IProjectEntity` gen? | ✅ **Không gen** — user đã có |
| 9 | `Ext/{Class}.cs` gen? | ✅ **Không gen** — user tự code |

**Đã verify từ 2 folder mẫu** (`AllianceMiddlemanWebAPI.Core` + `AllianceMiddlemanBase.Core`):
- Cùng namespace pattern, cùng file structure (`{Model}.efml` + `{Model}.{Model}.cs` + N `{Model}.{Class}.cs` + `.info` + `.edps`)
- `CategoryDataContext.cs` 2 folder identical 100% trừ namespace
- Base folder lớn hơn (~100 entities) — confirm pattern scale tốt

---

## 11. Trạng thái sẵn sàng để code

### Đã sẵn sàng ✅
- File mẫu đầy đủ (input `.efml` + output `.cs`)
- Open questions resolved
- Scope rõ ràng

### Còn cần (không block code Phase 1+2)
- **Connection string Postgres test DB** — cần khi build Phase 3 (DB → efml). Phase 1+2 (efml → cs) test bằng file `.efml` có sẵn, không cần DB.

---

## 12. Quyết định thiết kế gọn (post-clarification)

1. **DataContext file:** không phải Scriban template. Là **plain `.cs.tmpl`** với placeholder `{{Model}}`, `{{Namespace}}`, `{{ContextClass}}`, user cung cấp file mẫu của mình; tool chỉ string-replace. Skip-if-exists.

2. **CLI thêm flag:**
   ```
   --datacontext-template <path>     # path tới file .cs.tmpl của user
   --context-class <name>            # tên class wrapper, vd "CategoryDataContext"
   ```
   Nếu không truyền, tool dùng template built-in default (Postgres + Ezy.Module).

3. **Không gen:** `Ext/`, `IProjectEntity.cs`.

4. **Provider dispatch:** chỉ áp dụng cho `Context.scriban` (`UseNpgsql` vs `UseSqlServer`, `int4` vs `int`, `uuid` vs `uniqueidentifier`, default `uuid_generate_v7()` vs `NEWSEQUENTIALID()`). Phần `DataContext.cs` user tự lo.
