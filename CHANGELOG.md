# Changelog

Toàn bộ thay đổi đáng chú ý của EfmlGen được liệt kê tại đây. Format theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning theo [SemVer](https://semver.org/).

## [0.4.2] — 2026-05-26

### Fixed — v0.4.1 block GenCode khi DB có tên không hợp lệ làm C# identifier
- v0.4.1 thêm rule `CollisionDetector` với **Severity.Error** cho identifier không hợp lệ (leading digit, dash, space, dot, …) → block generation trong UI WPF/VSIX. Nhiều DB schema thực tế có những tên này → user không generate được nếu không tick Force.
- v0.4.2 đổi sang **auto-sanitize ở emission**, theo strategy của `EntityFrameworkCore.Generator` ([ModelGenerator.ToLegalName](ref/EntityFrameworkCore.Generator/src/EntityFrameworkCore.Generator.Core/ModelGenerator.cs)).

### Changed — Identifier auto-sanitization
- `IdentifierSanitizer.SafeName(name)` strip leading non-alpha, split trên `[\W_]+`, ghép PascalCase. Tên hợp lệ → giữ nguyên (policy "DB sao thì code vậy"). Tên không hợp lệ → sanitize:
  - `1stName` → `StName`
  - `user-id` → `UserId`
  - `customer name` → `CustomerName`
  - `Order.Total` → `OrderTotal`
- `CsKeywords.SafeId(name)` = sanitize + escape reserved keyword (`@class` etc).
- Emitters (`EntityEmitter`, `ContextEmitter`) dùng `SafeId` cho mọi C# identifier (class name, property name, nav, method, `x => x.Prop` lambda); `HasForeignKey(@"...")` và `HasKey(@"...")` dùng `SafeName` (string version, không có `@`).
- DB-facing strings (`HasColumnName(@"customer name")`, `ToTable(@"raw-name")`) vẫn giữ raw → EF map đúng cột/bảng thật.

### Changed — `CollisionDetector` rule #7 Error → Warning
- Vì tool đã auto-sanitize, không cần block. Message mới: `Class 'X' is not a valid C# identifier — will be emitted as 'Y'. Rename in efml if you want a different name.`

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.4.2`.
- 66/66 test pass (golden tests vẫn byte-identical vì sample dùng identifier hợp lệ).

## [0.4.1] — 2026-05-26

Audit cross-check với `EntityFrameworkCore.Generator` (ref) → fix các gap về tính đúng + tính bao quát của model mapping. Toàn bộ behavior mặc định backward-compatible (54 byte-identical golden test cũ vẫn pass).

### Fixed — Association / relationship correctness
- **Composite FK không còn bị skip.** `DatabaseModelMapper.MapAssociation` trước đây sớm `return null` cho FK có nhiều hơn 1 column → các bảng nối có composite FK bị mất relationship. Giờ mọi FK column được serialize qua `EfAssociationEnd.PropertyNames` (List), và `ContextEmitter` emit `.HasForeignKey(@"X", @"Y")` đầy đủ.
- **One-to-one detection.** Khi tập FK column trùng với PK của bảng phụ thuộc → cardinality = `OneToOne`, emit `HasOne/WithOne/HasForeignKey<T>(...)` thay vì `HasMany/WithOne` sai nghĩa.
- **CascadeDelete.** `EfAssociation.CascadeDelete` đọc từ `DatabaseForeignKey.OnDelete == Cascade`, round-trip qua `.efml` (`p1:CascadeDelete`), và emit `.OnDelete(DeleteBehavior.Cascade)` trong `RelationshipsMapping`.

### Fixed — Column metadata fidelity
- **Rowversion / timestamp.** SQL Server columns `rowversion`/`timestamp` giờ tự set `IsConcurrencyToken=true` + `IsRowVersion=true`, emit `.IsRowVersion()` thay cho `.IsConcurrencyToken()`.
- **Computed column.** Đọc `DatabaseColumn.ComputedColumnSql` → set `ValueGenerated=OnAddOrUpdate` và emit `.HasComputedColumnSql(@"...")`. Round-trip qua attribute `computed` trên `<column>`.
- **SQL Server edge types.** Thêm map cho `hierarchyid`, `geography`, `geometry` → `Blob` (giữ `sql-type` gốc); `sql_variant` → `String`. Trước đây fallback string mất sql-type.

### Added — Naming options (opt-in, default `Preserve`)
- `MapOptions.EntityNaming` (Preserve / Singular / Plural) và `RelationshipNaming` (Preserve / Pluralize / Suffix) — apply Inflector (singularize/pluralize) tự viết, không thêm dependency.
- Default vẫn là `Preserve` → tên class/property/column = tên DB nguyên bản (policy "giữ nguyên DB sao thì code vậy").

### Added — Index methods (opt-in)
- `EfClass.Indexes` (List<EfIndex>) populate từ `table.Indexes` (loại trừ PK index), round-trip qua `<index>` + `<column>` trong `.efml`.
- `GenerationContext.GenerateIndexMethods=true` → `EntityEmitter` emit `public static T? GetByXxx(...)` (unique index) hoặc `IQueryable<T> GetByXxx(...)` (non-unique). Default false để giữ parity với Devart Entity Developer template.

### Added — Collision detection: invalid C# identifier
- `CollisionDetector` rule mới: cảnh báo Error nếu class/property name không phải C# identifier hợp lệ (leading digit, dash, space, dot, …). KHÔNG tự rename — theo policy "giữ nguyên DB, chỉ detect". User được hướng dẫn rename trong `.efml`.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.4.1`.
- 66/66 test pass (54 golden + 12 collision detector tests mới).

## [0.4.0] — 2026-05-25

### Fixed — `RelationshipsMapping` sai entity cho cross-class association
- Statement thứ 2 trong `RelationshipsMapping` (cặp `HasOne/WithMany`) trước đây gọi sai trên `modelBuilder.Entity<End1>` thay vì `Entity<End2>`. Hậu quả: với association A↔B mà A và B là 2 class khác nhau, EF Core ném `InvalidOperationException` khi build model vì navigation tên `End1Nav` không tồn tại trên class End1 (nó nằm trên End2).
- Lỗi đã có từ initial commit nhưng chỉ lộ ra khi gặp model thực tế (cross-class). Self-reference (vd `Department.ParentId → Department`) tình cờ chạy được vì End1.ClassName == End2.ClassName.
- Sau fix, generator emit đúng pattern "config từ 2 chiều" của EntityFrameworkCore.Generator: `Entity<A>().HasMany(B).WithOne(A)` + `Entity<B>().HasOne(A).WithMany(B)`.

### Added — VSIX: mở `.efml` tự bind profile
- `EfmlDocumentWatcher` hook `DTE.DocumentEvents.DocumentOpened`. Khi user double-click `.efml` trong Solution Explorer:
  1. Tool window EfmlGen tự bật.
  2. Match profile theo `EfmlPath` (case-insensitive full-path) — nếu có thì load; nếu không thì tạo profile mới (Name = file basename, OutputDir = file dir, ModelName = basename) và persist vào `profiles.json`.
- Bỏ thao tác chọn profile tay mỗi lần mở model khác.

### Added — WPF: shell association mở `.efml` tự load profile
- `EfmlGen.Designer.exe <path.efml>` (qua double-click khi đã associate, hoặc command line) áp dụng cùng logic match/create profile như VSIX watcher.
- `App.xaml` chuyển từ `StartupUri` sang `Application_Startup` handler để đọc `e.Args` trước khi tạo `MainWindow`.

### Changed — VSIX Tool Window theme
- Refactor `EfmlGenToolWindowControl.xaml` (+407 dòng): tách styles ra `EfmlGenTheme.xaml`, áp dụng theme nhất quán với VS dark/light, polish layout panels Profile/Scaffold/Generate.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.4.0`.

## [0.3.2] — 2026-05-21

### Fixed — Import `.efml` với type Devart-style
- Thêm alias cho 3 efml type mà importer chưa hiểu (gây lỗi `Failed to read .efml: Unknown efml type: 'X'` khi import efml sinh từ Devart Entity Developer):
  - `VarBinary` → `byte[]` (map sang `EfType.Blob`)
  - `Clob` → `string` (map sang `EfType.String`)
  - `Time` → `TimeSpan`
- Phát hiện từ scan 348 file `.efml` thực tế trong workspace.

### Added — Concurrency token (rowversion) support
- Reader đọc `<concurrency>` element (trước đây bỏ qua âm thầm) và thêm vào `EfClass.Properties` với cờ `IsConcurrencyToken=true`. Writer round-trip lại dưới dạng `<concurrency>`.
- DbContext mapping emit thêm `.IsConcurrencyToken()` cho property này, đồng thời support `value-generated="OnAddOrUpdate"` → `.ValueGeneratedOnAddOrUpdate()` (trước đây chỉ có `OnAdd`).
- Kết quả: cột SQL Server `rowversion`/`timestamp` giờ được sinh đúng `.HasColumnType("rowversion").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken()` thay vì bị mất hoàn toàn khỏi context.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.3.2`.

## [0.3.1] — 2026-05-21

### Added — `FileBaseName` override (legacy efml support)
- **`FileBaseName` attribute trên `<efcore>` efml**: override prefix cho tên file `.cs` sinh ra (default: lấy từ tên file `.efml`). Hỗ trợ trường hợp tên file `.efml` khác `p1:name` — ví dụ `ExternalChecklistDataModel.efml` có `p1:name="ExternalChecklistEntities"` → output `ExternalChecklistDataModel.{Class}.cs` (match Entity Developer), không phải `ExternalChecklistEntities.{Class}.cs`.
- **CLI flag `--file-base-name`** cho `gen-code` và `scaffold-efml` (stamp vào efml).
- **WPF**: thêm field "File base name (override)" ở tab Scaffold; `Import .efml` tự detect khi tên file ≠ model name và set field này.

### Fixed
- **Profile lưu `EfmlPath` đầy đủ** thay vì tái tạo từ `OutputDir + ModelName + ".efml"`. Trước đây khi import efml có tên file khác model name, save/load lại profile bị reset path sai. Profile cũ tự fallback về compose-from-OutputDir khi `EfmlPath` rỗng.

### Added — Visual Studio 2022 extension (VSIX)
Tích hợp EfmlGen vào Visual Studio 2022, thay thế việc gọi CLI tay hoặc mở WPF GUI riêng. Source ở [src-vsix/](src-vsix/), output `.vsix` ~37 MB chứa CLI bundle.

- **Tool Window** `View → Other Windows → EfmlGen` (hoặc `Tools → EfmlGen Tool Window`): panel kết nối + scaffold + generate; chia sẻ `profiles.json` với WPF GUI qua DPAPI.
- **Solution Explorer commands** trên `.efml`: "Update Model from Database…" và "Generate Code" — chạy `scaffold-efml` / `gen-code` với profile last-used.
- **New Item Wizard**: `Add → New Item → Visual C# → Data → EfmlGen Entity Model` mở dialog chọn profile + model name + namespace + table filter, scaffold xong tự thêm `.efml` vào project.
- **Output pane "EfmlGen"** stream stdout/stderr realtime từ CLI subprocess.
- **Collision retry**: khi `gen-code` exit 3 (CollisionDetector), hiện MessageBox cho phép rerun với `--force`.
- Target: VS 2022 (17.0+), `net472`. Engine bundle: `EfmlGen.Cli.exe` self-contained win-x64 single-file.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.3.1`.

## [0.2.0] — 2026-04 (approx)

### Added
- **SQL Server support** song song với PostgreSQL. `--provider SqlServer`/`MSSQL`. Schema mặc định đảo theo provider (`public` ↔ `dbo`).
- **`--profile <name>`** cho cả CLI và WPF: lưu/dùng lại cấu hình kết nối + output từ `%AppData%\EfmlGen\profiles.json`. Password DPAPI per-user-per-machine.
- **WPF GUI redesign**: sidebar nav + card layout + modern theme; chạy độc lập hoặc reuse profile từ CLI.
- **Inno Setup installer** (`EfmlGen-Setup-v{version}.exe`): bundle WPF + CLI, optional PATH + `.efml` association.
- **App icon + logo** cho window/exe/installer.
- **Version display** trên status bar WPF.

### Changed
- Default: `Skip DataContext.cs` và `Skip .info file` được tick sẵn (tránh ghi đè wrapper user đã sửa).
- Polish error messages: rõ hơn khi DPAPI fail cross-user/machine, missing flag, collision exit 3.

## [0.1.0] — 2025

### Added
- Initial release: CLI `db-smoke` / `scaffold-efml` / `gen-code` + PostgreSQL provider.
- File `.efml` XML format + `.cs` emitter (entities + DbContext + Diagram).
- Merge logic: scaffold lại bảo toàn `p1:Guid` cũ + tên class user đã đổi tay.
- Vietnamese usage guide.

[0.4.0]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.4.0
[0.3.2]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.3.2
[0.3.1]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.3.1
[0.2.0]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.2.0
[0.1.0]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.1.0
