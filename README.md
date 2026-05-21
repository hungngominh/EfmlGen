# EfmlGen — Hướng dẫn sử dụng

Tool **EfmlGen** thay thế workflow của Devart Entity Developer: đọc schema database → sinh file `.efml` → sinh file C# entity + DbContext cho EF Core. Mã nguồn mở; ba cách dùng song song:

- **CLI** (`EfmlGen.Cli.exe`) — script, CI, headless.
- **WPF GUI** (`EfmlGen.Designer.exe`) — desktop app trên Windows.
- **Visual Studio 2022 extension** (`EfmlGen.Vsix.vsix`) — tích hợp Solution Explorer + Tool Window + Add New Item Wizard. Xem [src-vsix/](src-vsix/).

Lịch sử thay đổi: [CHANGELOG.md](CHANGELOG.md).

---

## 1. Yêu cầu hệ thống

| Thành phần | Phiên bản |
|---|---|
| .NET SDK | 8.0 trở lên (build từ source) |
| OS | Windows (WPF + VSIX) / Linux / macOS (chỉ CLI) |
| Database | PostgreSQL · SQL Server |
| Visual Studio | 2022 (17.0+) — chỉ khi dùng VSIX |

---

## 2. Build & cài đặt

### 2.1. Build từ source

```powershell
# Tại thư mục gốc của repo
dotnet build EfmlGen.sln -c Release
```

Sau khi build xong, executable nằm tại:

- CLI: [src/EfmlGen.Cli/bin/Release/net8.0/EfmlGen.Cli.exe](src/EfmlGen.Cli/bin/Release/net8.0/EfmlGen.Cli.exe)
- GUI: [src/EfmlGen.Wpf/bin/Release/net8.0-windows/EfmlGen.Wpf.exe](src/EfmlGen.Wpf/bin/Release/net8.0-windows/EfmlGen.Wpf.exe)

### 2.2. Publish bản self-contained (không cần .NET runtime trên máy đích)

```powershell
dotnet publish src/EfmlGen.Wpf/EfmlGen.Wpf.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o publish-wpf-single
```

Sau khi publish xong, chạy trực tiếp `publish-wpf-single/EfmlGen.Wpf.exe`.

---

## 3. Pipeline tổng quan

```
┌──────────┐  scaffold-efml   ┌──────────┐  gen-code   ┌────────────┐
│ Database │ ───────────────▶ │  .efml   │ ──────────▶ │ .cs files  │
└──────────┘                  └──────────┘             └────────────┘
                                   ▲
                                   │ merge với efml cũ
                                   │ (giữ GUID + custom)
                              ┌─────────┐
                              │ old.efml│
                              └─────────┘
```

3 lệnh CLI chính:

| Lệnh | Mục đích |
|---|---|
| `scaffold-efml` | Đọc schema DB → ghi/merge file `.efml` (bảo toàn `p1:Guid` cũ + tên class user đã đổi tay) |
| `gen-code` | Sinh file `.cs` từ `.efml` đã có |
| `db-smoke` | Test kết nối DB nhanh, in danh sách table |

---

## 4. Sử dụng CLI

### 4.1. Test kết nối database

```powershell
# Postgres
EfmlGen.Cli.exe db-smoke `
  --conn-env PG_CONN `
  --schemas public,dbo

# SQL Server
EfmlGen.Cli.exe db-smoke `
  --provider SqlServer `
  --conn-env MSSQL_CONN `
  --schemas dbo
```

Trong đó `PG_CONN` / `MSSQL_CONN` là biến môi trường chứa connection string (khuyến nghị, tránh log mật khẩu). Có thể truyền thẳng bằng `--conn "..."`:

- Postgres: `Host=...;Port=5432;Username=...;Password=...;Database=...`
- SQL Server: `Server=host,1433;Database=...;User Id=...;Password=...;TrustServerCertificate=true`

`--provider` chấp nhận `Postgres` (alias: `Npgsql`, `PostgreSQL`) hoặc `SqlServer` (alias: `MSSQL`). Mặc định `Postgres`. Schema mặc định: `public,dbo` cho Postgres, `dbo` cho SQL Server.

Thêm `--detail` để xem chi tiết column của 3 table đầu tiên.

### 4.2. Lần đầu: scaffold file `.efml` từ DB

```powershell
EfmlGen.Cli.exe scaffold-efml `
  --conn-env PG_CONN `
  --provider Postgres `
  --schemas dbo `
  --tables "ConfigState,Department" `
  --name CategoryEntities `
  --namespace MyApp.Data `
  --out Categories/CategoryEntities.efml
```

| Flag | Bắt buộc | Ý nghĩa |
|---|---|---|
| `--conn` hoặc `--conn-env` | Có | Connection string trực tiếp hoặc tên biến môi trường chứa nó |
| `--provider` | Không | `Postgres` (mặc định) hoặc `SqlServer` |
| `--schemas` | Không | Danh sách schema, cách nhau bằng dấu `,` (mặc định `dbo`) |
| `--tables` | Không | Lọc bảng cụ thể (mặc định lấy tất cả) |
| `--name` | Có | Tên model, ví dụ `CategoryEntities` |
| `--namespace` | Có | Namespace cho code C# sinh ra |
| `--context-namespace` | Không | Namespace cho DbContext (mặc định trùng `--namespace`) |
| `--out` | Có | Đường dẫn file `.efml` đầu ra |
| `--overwrite` | Không | Bỏ qua file cũ, scaffold lại từ đầu (sẽ mất `p1:Guid` cũ) |
| `--force-datetime` | Không | Bỏ timezone offset: Postgres `timestamptz`/`timetz` → `DateTime`/`TimeSpan`; SQL Server `datetimeoffset` → `DateTime` |
| `--diagram-name` | Không | Hậu tố file `.view` (mặc định `Diagram1`) |
| `--skip-view` | Không | Không sinh file diagram |

### 4.3. Sinh file `.cs` từ `.efml`

```powershell
EfmlGen.Cli.exe gen-code `
  --efml Categories/CategoryEntities.efml `
  --out Categories/ `
  --provider Npgsql `
  --context-class CategoryDataContext
```

| Flag | Bắt buộc | Ý nghĩa |
|---|---|---|
| `--efml` | Có | Đường dẫn file `.efml` |
| `--out` | Có | Thư mục đầu ra |
| `--provider` | Không | `Npgsql` (mặc định) hoặc `SqlServer` — quyết định `optionsBuilder.UseNpgsql(...)` vs `UseSqlServer(...)` trong DbContext sinh ra |
| `--connection-string` | Không | Connection string nhúng vào `OnConfiguring` của DbContext |
| `--context-class` | Không | Tên class wrapper (mặc định `{Model}DataContext`) |
| `--datacontext-template` | Không | File template tùy biến cho `DataContext.cs` |
| `--skip-datacontext` | Không | Không sinh file `DataContext.cs` |
| `--skip-info` | Không | Không sinh file `.info` |
| `--skip-view` | Không | Không sinh file `.view` |
| `--timestamp <iso>` | Không | Override timestamp trong header (để build reproducible) |
| `--force` | Không | Sinh code dù `CollisionDetector` báo lỗi |

### 4.4. Khi schema DB thay đổi

Chạy lại `scaffold-efml` với cùng `--out`. Tool sẽ **merge**:

- Giữ nguyên `p1:Guid` của các class/property đã có.
- Giữ nguyên các class user đã đổi tên tay (ví dụ `ConfigState` → `State`).
- Thêm class/property mới từ DB.
- Báo các thay đổi ra console: `"Added classes: NewTable; Renamed classes: ConfigState → State"`.

Sau đó chạy lại `gen-code` để cập nhật file `.cs`.

---

## 5. Sử dụng GUI WPF

Mở `EfmlGen.Wpf.exe` (đã build hoặc publish ở bước 2). Cửa sổ chính có 4 vùng:

1. **Profile selector** — lưu/load cấu hình kết nối + output để dùng lại lần sau.
   - `Import from .efml...` — đọc file `.efml` có sẵn để fill nhanh namespace, tên model, schema, output dir.
   - `Save Profile` / `Delete` — quản lý profile.
2. **Connection panel** — chọn provider (`Postgres` hoặc `SqlServer`), nhập host, port, user, password, database, schema. Khi đổi provider, port/schema mặc định tự đảo (5432/`public` ↔ 1433/`dbo`); giá trị user đã nhập tay không bị ghi đè.
3. **Output panel** — chọn thư mục đầu ra, đặt model name, namespace, context class, provider.
4. **Log panel** — xem output realtime khi chạy.

Nút action dưới cùng:

- **Test Connection** — tương đương `db-smoke`.
- **Scaffold .efml** — tương đương `scaffold-efml` (merge nếu file đã có).
- **Generate .cs** — tương đương `gen-code`.
- **Sync (full pipeline)** — chạy cả 2 bước trên liên tiếp.

Profile được lưu dưới dạng JSON tại `%AppData%\EfmlGen\profiles.json`. Password được mã hoá bằng DPAPI (per-user, per-machine — không transfer được sang user/máy khác).

### 5.1. Dùng lại profile WPF từ CLI

CLI đọc cùng file `profiles.json` — pass `--profile <name>` để bỏ các flag dài (`--conn`, `--provider`, `--schemas`, `--name`, `--namespace`, `--out`, `--context-class`):

```powershell
# Sau khi đã Save Profile "MyDb" trong WPF
EfmlGen.Cli.exe db-smoke --profile MyDb
EfmlGen.Cli.exe scaffold-efml --profile MyDb --tables "ConfigState,Department"
EfmlGen.Cli.exe gen-code --profile MyDb --efml Categories/MyDb.efml
```

Quy tắc resolution: **CLI flag > profile value > default**. Ví dụ `--profile MyDb --schemas onlydbo` sẽ dùng schema `onlydbo`, các field còn lại lấy từ profile.

Khi DPAPI không decrypt được password (chạy ở user/máy khác với lúc save), CLI báo lỗi rõ ràng. Vẫn có thể dùng `--profile MyDb --conn-env PG_CONN` để override toàn bộ connection string.

Profile file ở vị trí khác: `--profile-file path/to/profiles.json`.

---

## 6. Sử dụng Visual Studio 2022 extension (VSIX)

Cài đặt: double-click `EfmlGen.Vsix.vsix` từ release. VS 2022 (Community/Pro/Enterprise, 17.0+) sẽ load extension. VSIX bundle sẵn `EfmlGen.Cli.exe` self-contained nên không cần cài CLI riêng.

Ba điểm tích hợp:

1. **Tool Window** — `View → Other Windows → EfmlGen` (hoặc `Tools → EfmlGen Tool Window`). Panel kết nối + scaffold + generate; chia sẻ `profiles.json` với WPF GUI nên profile bạn save bên này nhìn thấy bên kia (cùng user, cùng máy do DPAPI).
2. **Right-click `.efml` trong Solution Explorer**:
   - `EfmlGen: Update Model from Database…` — chạy `scaffold-efml` với profile last-used (merge giữ `p1:Guid` + rename tay).
   - `EfmlGen: Generate Code` — chạy `gen-code`. Khi gặp collision (exit 3) hỏi rerun `--force` qua MessageBox.
3. **`Add → New Item → Visual C# → Data → EfmlGen Entity Model`** — wizard hỏi profile + model name + namespace + table filter; scaffold xong tự thêm `.efml` vào project.

Log realtime ở `Output → EfmlGen` pane.

Workflow điển hình:
1. Mở Tool Window, nhập connection, `Save Profile` (lần đầu).
2. `Add → New Item → EfmlGen Entity Model` (lần đầu) hoặc right-click `.efml` có sẵn → `Update Model from Database…` (lần sau).
3. Right-click `.efml` → `Generate Code` để sinh/refresh `.cs`.

Build VSIX từ source: xem [src-vsix/README.md](src-vsix/README.md).

---

## 7. File đầu ra

Với model tên `CategoryEntities`, sau khi gen, thư mục output sẽ có:

### Sinh lại mỗi lần (overwrite)

| File | Nội dung |
|---|---|
| `CategoryEntities.efml` | XML nguồn (ghi sau merge với DB) |
| `CategoryEntities.CategoryEntities.cs` | DbContext chính + `OnModelCreating` + mapping từng class |
| `CategoryEntities.{ClassName}.cs` | Partial class entity (1 file/class) |
| `CategoryEntities.info` | Plain text báo gen thành công |
| `CategoryEntities.Diagram1.view` | Diagram layout XML (auto grid layout) |

### Sinh 1 lần, không overwrite (user được chỉnh tay)

| File | Khi nào sinh |
|---|---|
| `{ContextClass}.cs` | Lần đầu — nếu đã tồn tại thì bỏ qua. Đây là wrapper user-facing, chỉ thay placeholder `{Model}`, `{Namespace}`, `{ContextClass}` trong template. |

### Không tự sinh (user tự viết)

- `Ext/{ClassName}.cs` — extension method viết tay.
- `IProjectEntity.cs` — interface dùng chung.

---

## 8. Tham khảo nhanh — workflow thông dụng

### 7.1. PostgreSQL

```powershell
# Lần đầu setup model
$env:PG_CONN = "Host=localhost;Username=postgres;Password=xxx;Database=mydb"

EfmlGen.Cli.exe db-smoke --conn-env PG_CONN --schemas dbo

EfmlGen.Cli.exe scaffold-efml `
  --conn-env PG_CONN --schemas dbo `
  --tables "ConfigState,Department" `
  --name CategoryEntities --namespace MyApp.Data `
  --out Categories/CategoryEntities.efml

EfmlGen.Cli.exe gen-code `
  --efml Categories/CategoryEntities.efml --out Categories/ `
  --provider Npgsql `
  --context-class CategoryDataContext

# Sau khi DB schema đổi (thêm bảng NewTable, đổi tên ConfigState → State)
EfmlGen.Cli.exe scaffold-efml `
  --conn-env PG_CONN --schemas dbo `
  --tables "ConfigState,Department,NewTable" `
  --name CategoryEntities --namespace MyApp.Data `
  --out Categories/CategoryEntities.efml
# → "Added classes: NewTable; Renamed classes: ConfigState → State"

EfmlGen.Cli.exe gen-code `
  --efml Categories/CategoryEntities.efml --out Categories/
```

### 7.2. SQL Server

```powershell
$env:MSSQL_CONN = "Server=localhost,1433;Database=mydb;User Id=sa;Password=xxx;TrustServerCertificate=true"

EfmlGen.Cli.exe db-smoke --provider SqlServer --conn-env MSSQL_CONN --schemas dbo

EfmlGen.Cli.exe scaffold-efml `
  --provider SqlServer `
  --conn-env MSSQL_CONN --schemas dbo `
  --tables "ConfigState,Department" `
  --name CategoryEntities --namespace MyApp.Data `
  --out Categories/CategoryEntities.efml

EfmlGen.Cli.exe gen-code `
  --efml Categories/CategoryEntities.efml --out Categories/ `
  --provider SqlServer `
  --context-class CategoryDataContext
```

Lưu ý sự khác biệt giữa `--provider` của hai lệnh:
- `scaffold-efml`: `Postgres` | `SqlServer` — chọn driver đọc schema.
- `gen-code`: `Npgsql` | `SqlServer` — chính là tên extension `optionsBuilder.UseXxx(...)` được nhúng vào `DbContext`.

---

## 9. Mẫu tham khảo

- [samples/categories-postgres/](samples/categories-postgres/) — model sinh từ **PostgreSQL** (7 bảng + 1 association).
- [samples/categories-sqlserver/](samples/categories-sqlserver/) — cùng schema trên **SQL Server**: kèm `setup.sql` để tạo DB từ đầu, đối chiếu type mapping PG ↔ MSSQL trong README của sample.

Tài liệu thiết kế chi tiết (kiến trúc, format `.efml`, quy tắc merge, mapping type…): [DESIGN.md](DESIGN.md).

---

## 10. Mã exit code

| Code | Ý nghĩa |
|---|---|
| 0 | Thành công |
| 1 | Sai cú pháp lệnh / thiếu lệnh |
| 2 | Lỗi runtime (kết nối DB, file không tồn tại…) |
| 3 | `CollisionDetector` phát hiện lỗi — dùng `--force` để bỏ qua |

---

## 11. Khắc phục sự cố

| Triệu chứng | Nguyên nhân | Cách xử lý |
|---|---|---|
| `Missing required option: --name` | Quên flag bắt buộc | Xem lại `EfmlGen.Cli.exe --help` |
| `Aborted due to errors` khi `gen-code` | Trùng tên property/class | Sửa `.efml` rồi gen lại, hoặc `--force` (không khuyến nghị) |
| File `.cs` mất phần custom của user | User code vào file thuộc nhóm "overwrite mỗi lần" | Đưa code vào `Ext/{ClassName}.cs` hoặc partial method `OnCreated()` |
| `p1:Guid` bị thay đổi sau scaffold | Đã chạy `--overwrite` | Tránh dùng `--overwrite` trừ khi muốn reset |

Khi gặp lỗi không có trong bảng, bật log chi tiết bằng cách thêm `--detail` (với `db-smoke`) hoặc chạy lại với verbose output trong WPF GUI.
