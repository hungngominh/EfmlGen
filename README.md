# EfmlGen — Hướng dẫn sử dụng

Tool **EfmlGen** thay thế workflow của Devart Entity Developer: đọc schema database → sinh file `.efml` → sinh file C# entity + DbContext cho EF Core. Mã nguồn mở, ưu tiên dùng qua CLI, có thêm GUI WPF cho thao tác nhanh.

---

## 1. Yêu cầu hệ thống

| Thành phần | Phiên bản |
|---|---|
| .NET SDK | 8.0 trở lên |
| OS | Windows (WPF GUI) / Linux / macOS (chỉ CLI) |
| Database | PostgreSQL (hỗ trợ đầy đủ). SQL Server đang phát triển. |

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
EfmlGen.Cli.exe db-smoke `
  --conn-env PG_CONN `
  --schemas public,dbo
```

Trong đó `PG_CONN` là biến môi trường chứa connection string (khuyến nghị, tránh log mật khẩu). Có thể truyền thẳng bằng `--conn "Host=...;Username=...;Password=...;Database=..."`.

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
| `--provider` | Không | Mặc định Postgres (SQL Server chưa hỗ trợ đầy đủ) |
| `--schemas` | Không | Danh sách schema, cách nhau bằng dấu `,` (mặc định `dbo`) |
| `--tables` | Không | Lọc bảng cụ thể (mặc định lấy tất cả) |
| `--name` | Có | Tên model, ví dụ `CategoryEntities` |
| `--namespace` | Có | Namespace cho code C# sinh ra |
| `--context-namespace` | Không | Namespace cho DbContext (mặc định trùng `--namespace`) |
| `--out` | Có | Đường dẫn file `.efml` đầu ra |
| `--overwrite` | Không | Bỏ qua file cũ, scaffold lại từ đầu (sẽ mất `p1:Guid` cũ) |
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
| `--provider` | Không | `Npgsql` (mặc định) hoặc `SqlServer` |
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
2. **Connection panel** — nhập host, user, password, database, schema.
3. **Output panel** — chọn thư mục đầu ra, đặt model name, namespace, context class, provider.
4. **Log panel** — xem output realtime khi chạy.

Nút action dưới cùng:

- **Test Connection** — tương đương `db-smoke`.
- **Scaffold .efml** — tương đương `scaffold-efml` (merge nếu file đã có).
- **Generate .cs** — tương đương `gen-code`.
- **Sync (full pipeline)** — chạy cả 2 bước trên liên tiếp.

---

## 6. File đầu ra

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

## 7. Tham khảo nhanh — workflow thông dụng

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

---

## 8. Mẫu tham khảo

Xem [samples/categories-postgres/](samples/categories-postgres/) để có ví dụ một model hoàn chỉnh sinh từ PostgreSQL.

Tài liệu thiết kế chi tiết (kiến trúc, format `.efml`, quy tắc merge, mapping type…): [DESIGN.md](DESIGN.md).

---

## 9. Mã exit code

| Code | Ý nghĩa |
|---|---|
| 0 | Thành công |
| 1 | Sai cú pháp lệnh / thiếu lệnh |
| 2 | Lỗi runtime (kết nối DB, file không tồn tại…) |
| 3 | `CollisionDetector` phát hiện lỗi — dùng `--force` để bỏ qua |

---

## 10. Khắc phục sự cố

| Triệu chứng | Nguyên nhân | Cách xử lý |
|---|---|---|
| `Missing required option: --name` | Quên flag bắt buộc | Xem lại `EfmlGen.Cli.exe --help` |
| `Aborted due to errors` khi `gen-code` | Trùng tên property/class | Sửa `.efml` rồi gen lại, hoặc `--force` (không khuyến nghị) |
| File `.cs` mất phần custom của user | User code vào file thuộc nhóm "overwrite mỗi lần" | Đưa code vào `Ext/{ClassName}.cs` hoặc partial method `OnCreated()` |
| `p1:Guid` bị thay đổi sau scaffold | Đã chạy `--overwrite` | Tránh dùng `--overwrite` trừ khi muốn reset |

Khi gặp lỗi không có trong bảng, bật log chi tiết bằng cách thêm `--detail` (với `db-smoke`) hoặc chạy lại với verbose output trong WPF GUI.
