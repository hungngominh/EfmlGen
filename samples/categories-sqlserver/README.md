# Sample: categories-sqlserver

Mẫu EfmlGen cho **SQL Server**, song song với [../categories-postgres/](../categories-postgres/). Cùng 7 bảng (ConfigState, ConfigReportOrderReason, ConfigReportUserReason, ConfigRentalServiceRatingPoint, Mioto_VehicleOwner, Department, Product_Company_Mapping) nhưng dùng type của SQL Server.

## File trong thư mục

| File | Vai trò |
|---|---|
| [setup.sql](setup.sql) | DDL tạo schema mẫu trên SQL Server (chạy 1 lần) |
| [CategoryEntities.efml](CategoryEntities.efml) | File `.efml` nguồn (kết quả của `scaffold-efml`) |
| `CategoryEntities.{ClassName}.cs` | 7 partial class entity (sinh từ `gen-code`) |
| `CategoryEntities.CategoryEntities.cs` | DbContext chính + `OnModelCreating` (sinh) |
| `CategoryEntities.info` | Marker plain text (sinh) |
| `CategoryEntities.Diagram1.view` | Layout XML (sinh) |
| `CategoryDataContext.cs` | Wrapper user-facing (chỉ sinh lần đầu, không overwrite) |

## Cách tạo lại từ DB

```powershell
# 1. Tạo DB rỗng + chạy setup.sql
sqlcmd -S localhost -d SampleDb -i setup.sql

# 2. Set connection string trong env var (tránh log password)
$env:MSSQL_CONN = "Server=localhost;Database=SampleDb;User Id=sa;Password=YourPassword;TrustServerCertificate=true"

# 3. Test kết nối
EfmlGen.Cli.exe db-smoke --provider SqlServer --conn-env MSSQL_CONN --schemas dbo

# 4. Scaffold .efml
EfmlGen.Cli.exe scaffold-efml `
  --conn-env MSSQL_CONN --provider SqlServer --schemas dbo `
  --name CategoryEntities --namespace SampleApp.Data.Categories `
  --out CategoryEntities.efml

# 5. Sinh .cs
EfmlGen.Cli.exe gen-code `
  --efml CategoryEntities.efml --out . `
  --provider SqlServer --context-class CategoryDataContext
```

## Khác biệt với phiên bản Postgres

| Khía cạnh | Postgres | SQL Server |
|---|---|---|
| Provider flag | `--provider Postgres` (mặc định) | `--provider SqlServer` |
| `gen-code --provider` | `Npgsql` (mặc định) | `SqlServer` |
| Default cho bool | `default="false"` | `default="((0))"` |
| Default cho int 0 | `default="0"` | `default="((0))"` |
| GUID default | `uuid_generate_v7()` | `(newid())` |
| `sql-type` cho Guid | `uuid` | (mặc định `uniqueidentifier` — omit) |
| `sql-type` cho bool | `bool` | (mặc định `bit` — omit) |
| `sql-type` cho int | `int4` | (mặc định `int` — omit) |
| Sinh code dùng | `UseNpgsql(...)` | `UseSqlServer(...)` |
| `[TimeStamp]` của Department | `bytea` (Blob) | `rowversion` (Blob) |

## Type mapping reference

SQL Server `sql-type` chỉ được ghi vào `.efml` khi khác mặc định của provider EF SQL Server. Các type mặc định bị **omit** trong `.efml`:

- `int`, `bigint`, `smallint`, `tinyint`, `bit`
- `uniqueidentifier`
- `datetime2`, `datetimeoffset`, `time`
- `nvarchar(N)` với N hữu hạn (chỉ ghi `length="N"`)
- `decimal(p,s)` (chỉ ghi `precision="p" scale="s"`)
- `real`, `float`

Các type giữ `sql-type` rõ ràng:

- `nvarchar(max)`, `varchar(max)`, `varbinary(max)` — không có cách biểu diễn khác
- `money`, `smallmoney`, `ntext`, `text`, `image`, `xml`, `json`
- `datetime`, `smalldatetime`, `date` (khác `datetime2`)
- `binary(N)`, `rowversion`/`timestamp`

Xem [src/EfmlGen.Db/SqlServerTypeMap.cs](../../src/EfmlGen.Db/SqlServerTypeMap.cs) cho mapping đầy đủ.
