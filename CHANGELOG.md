# Changelog

Toàn bộ thay đổi đáng chú ý của EfmlGen được liệt kê tại đây. Format theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning theo [SemVer](https://semver.org/).

## [0.8.6] — 2026-08-20

### Fixed — WPF: tab "Tables" / "Stored procedures" bị ẩn header
- `MainWindow.xaml`: style ẩn header của TabControl ngoài cùng (điều hướng Connection/Scaffold/Diagram) được khai báo dạng implicit style (`TabControl.Resources` → `Style TargetType="TabItem"`), nên bị lan xuống ẩn luôn header của TabControl con "Tables"/"Stored procedures" trong tab Scaffold — người dùng không có cách nào chuyển giữa 2 tab đó.
- Chuyển style này sang `TabControl.ItemContainerStyle` để chỉ áp dụng cho chính TabControl ngoài cùng, không lan xuống TabControl lồng bên trong.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.8.6`.

## [0.8.5] — 2026-08-20

### Added — WPF: hỗ trợ Stored Procedures đầy đủ (ngang bằng CLI)
- Tab **Scaffold** giờ có 2 tab con "Tables" và "Stored procedures" (search, select all, clear, checkbox list) — trước đây WPF hoàn toàn không đọc/generate stored procedures dù CLI đã hỗ trợ.
- Nút "Load Schema Tables" đổi thành "Load Schema Tables + SPs": đọc luôn danh sách SP từ DB và tự pre-select những SP đã có trong `.efml` hiện có.
- `GenWorker.Scaffold` nhận thêm `spFilter` — `null` = đọc toàn bộ SP (giữ hành vi mặc định như CLI), mảng rỗng `[]` = người dùng chủ động bỏ chọn hết → không lấy SP nào (phân biệt rõ "chưa load SP" và "đã load nhưng bỏ chọn hết").
- `GenWorker.GenCode` giờ sinh đúng các class DTO kết quả stored procedure (`EfComplexType`) — trước đây bị bỏ sót âm thầm trong luồng WPF, gây lỗi biên dịch code sinh ra khi model có SP.

### Fixed — Postgres: function trả scalar bị mất kiểu trả về
- Function Postgres dạng `RETURNS int` (không có tham số `OUT`/`TABLE`) trước đây bị coi là không có kết quả trả về (Npgsql gọi qua `SELECT * FROM schema.fn(...)`, cột kết quả được đặt tên theo chính function đó). Giờ cột kết quả scalar này được nhận diện và map đúng.
- Function trả kiểu composite/`record` không có `OUT`/`TABLE` params giờ phát cảnh báo rõ ràng thay vì âm thầm mất dữ liệu.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.8.5`.

## [0.8.4] — 2026-07-27

### Fixed — Postgres: cột Id với sequence default thủ công không tự tăng
- Trước: nếu cột Id có `DEFAULT nextval(sequence)` nhưng sequence được tạo tay (không phải `SERIAL`/`GENERATED AS IDENTITY`, không có `OWNED BY` gắn với cột), Npgsql scaffolder trả về `ValueGenerated=None` dù `DefaultValueSql` vẫn là `nextval(...)`. `DatabaseModelMapper` sinh ra `.ValueGeneratedNever().HasDefaultValueSql(@"nextval(...)")` — hai chỉ định mâu thuẫn nhau khiến EF Core gửi giá trị `0` (default C# `int`) vào INSERT thay vì để Postgres tự tăng sequence.
- Giờ `DatabaseModelMapper.MapProperty` promote `ValueGenerated="OnAdd"` ngay khi phát hiện `DefaultValueSql` khớp pattern sequence (`nextval(`/`next value for`), kể cả khi scaffolder chưa tự gắn cờ OnAdd. Default dư thừa vẫn được lược bỏ như cũ.
- Thêm test hồi quy `SequenceDefaultMapperTests` tái hiện đúng case (bảng `B2B_Server_Request`, cột `Id`).

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.8.4`.

## [0.8.3] — 2026-06-11

### Changed — WPF: Log Output có thể thu gọn; Tables tự scroll trong card
- Log Output panel: thêm nút **▼/▶** ở header — click để thu gọn (chỉ còn thanh header), click lại để mở ra 240px. Nút "Clear Console" vẫn hiển thị cạnh bên.
- Tab Scaffold: bỏ ScrollViewer bao toàn màn hình, thay bằng layout Grid 2 row — title cố định trên, content chiếm toàn bộ chiều cao còn lại. ListBox tables tự scroll bên trong card thay vì dựa vào scroll toàn trang.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.8.3`.

## [0.8.2] — 2026-06-11

### Fixed — Auto-update chạy installer ở chế độ wizard (nhảy UI)
- Trước: bấm "Update to vX.Y.Z" → installer Inno Setup mở đầy đủ wizard, yêu cầu chọn lại thư mục cài đặt như lần đầu.
- Giờ truyền `/SILENT /SUPPRESSMSGBOXES /NORESTART` vào Inno Setup → installer chạy ngầm, giữ nguyên thư mục và tùy chọn của lần cài trước, không hiện wizard.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.8.2`.

## [0.8.1] — 2026-06-11

### Changed — WPF tab "Scaffold + Generate": cải thiện layout UX
- Button "Scaffold + Generate" chuyển lên header row cạnh title — luôn thấy ngay khi vào tab, không cần scroll xuống.
- Tách card settings trái thành 2 card có section header: **Scaffold** (Schemas / Model name / File base name / Namespace / Output .efml / Overwrite / Force DateTime) và **Generate** (Output directory / Context class / DataContext template / Connection string / Skip* / Force).
- Chuyển "Selected: N", "Select all", "Clear" từ card trái sang panel phải — đặt ngay trên danh sách table, đúng vị trí logic.
- Rút ngắn label dài → tooltip: "File base name (override; empty = use .efml filename)" → "File base name" + tooltip; "Overwrite (discard existing GUIDs)" → "Overwrite" + tooltip; tương tự cho DataContext template, Connection string, Force.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.8.1`.

## [0.8.0] — 2026-06-10

### Added — Scaffold + gen STORED PROCEDURES (Postgres + SQL Server)
- Trước: tool chỉ map tables + views; stored procedures bị bỏ qua hoàn toàn — phải viết tay wrapper gọi SP trên DbContext.
- Giờ `scaffold-efml` đọc stored procedures/functions từ DB và `gen-code` sinh wrapper methods trên DbContext, khớp 1:1 output của Devart Entity Developer.
- EF Core `IDatabaseModelFactory` không surface routines → thêm `StoredProcedureReader` đọc bằng ADO.NET thuần (read-only, không bao giờ ghi DB):
  - **SQL Server**: liệt kê `sys.procedures`, params qua `sys.parameters`, result-set qua `sp_describe_first_result_set` (bọc try/catch per-proc — proc dùng dynamic SQL/temp table sẽ throw → coi như void + warning).
  - **Postgres**: `information_schema.routines` + `parameters` (OUT/TABLE → result columns, INOUT → output param).

### Added — Round-trip stored procedures + complex types trong `.efml`
- `<class name="$ComplexTypes">` chứa các `<component>` result DTO + các `<method>` sibling (`<return>`/`<return-property>`/`<parameter>` kèm direction) round-trip qua `EfmlWriter`/`EfmlReader`, giữ Devart format.

### Added — Sinh sync + async wrapper methods
- `ContextEmitter` thêm `#region Methods`: mỗi SP sinh cả `sp_X(...)` (sync) và `sp_XAsync(...)` (async) bằng ADO.NET thuần — `CommandType.StoredProcedure`, `DbParameter` (DbType + Precision/Scale, null→`DBNull`), map `IDataReader` theo `return-property`. SP void + output param dùng `ref` (sync) / `Task<Tuple<...>>` (async).
- `ComplexTypeEmitter` (mới) sinh 1 file/result DTO: `partial class {Proc}Result` với virtual props + region Extensibility.

### Added — Merge giữ chỉnh tay cho SP + complex types
- `EfmlMerger` mở rộng: match complex type theo Name, SP theo `Procedure` → giữ `p1:Guid` + user rename khi re-scaffold (như cơ chế class/property hiện có). Report thêm bucket Added/Removed/Renamed stored procedures.

### Added — CLI `--skip-stored-procedures`
- Default: stored procedures được include. `scaffold-efml --skip-stored-procedures` bỏ qua. Log scaffold in số procs + result types + warning.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.8.0`.
- `EfmlGen.Db.csproj`: thêm explicit `PackageReference` `Microsoft.Data.SqlClient` + `Npgsql` (trước chỉ có transitively qua design-time services).

## [0.7.0] — 2026-06-10

### Added — Scaffold database VIEWS thành keyless entity
- Trước: EF Core trả views ngay trong `dbModel.Tables` (dưới dạng `DatabaseView`) nhưng tool map nhầm thành table — tổng hợp PK giả từ cột đầu, emit `.ToTable()` + `.HasKey()`. Sai ngữ nghĩa cho view chỉ-đọc, có thể gây lỗi runtime EF khi track/update.
- Giờ `DatabaseModelMapper` detect `table is DatabaseView` → set cờ `EfClass.IsView`, map **keyless**: mọi cột vào `Properties`, không có `Id`, bỏ qua synthesize-PK và index methods. `ContextEmitter` emit `.ToView(@"name", @"schema")` + `.HasNoKey()` thay cho `.ToTable()` + `.HasKey()`. Theo đúng cách reference `EntityFrameworkCore.Generator` xử lý view.
- `EfClass.AllProperties` giờ null-safe với `Id` (view keyless) → mọi consumer (entity emitter, diagram, collision detector) tự an toàn.

### Added — Round-trip `is-view` trong `.efml`
- `<class is-view="True">` (không có `<id>`) round-trip qua `EfmlWriter`/`EfmlReader`, giữ trạng thái keyless khi re-scaffold/gen.

### Added — CLI `--skip-views`
- `scaffold-efml --skip-views` loại views khỏi output (default: views được include). Log scaffold giờ tách số tables vs views.

### Build
- (Bump version + build các asset là bước release riêng.)

## [0.6.0] — 2026-06-10

### Changed — WPF gộp 2 tab Scaffold + Generate thành 1
- Trước: workflow tách làm 2 tab rời ("Scaffold DB" → `.efml`, "Generate .cs" → `.cs`), phải bấm scaffold ở tab này rồi chuyển tab kia bấm generate — 2 bước, 2 lần chờ.
- Giờ gộp thành **1 tab "Scaffold + Generate"** với **một nút** chạy tuần tự cả hai: scaffold `.efml` rồi gen `.cs` trong một lần. Sidebar còn 3 mục (Connection / Scaffold + Generate / Diagram).
- Bỏ ô efml-path trùng lặp (`GenEfmlPathBox`) — giờ chỉ còn một ô duy nhất. Output dir để trống thì gen vào thư mục của `.efml`.
- Lỗi rõ ràng: scaffold throw (lỗi DB) → gen không chạy; gen throw (name collision khi không tick Force) → báo "scaffold OK, gen aborted".
- Scope: chỉ WPF. VSIX đã có sẵn nút Sync; CLI không đổi.

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.6.0`.

## [0.5.0] — 2026-06-10

### Added — Version stamping vào header file generated
- Header của file `.cs` generated giờ ghi rõ version tool: `// This code was generated by EfmlGen tool v0.5.0 using EF Core template.` → khi build lỗi do output cũ, nhìn header là biết ngay bản tool nào sinh ra.
- `GenerationContext.ToolVersion` mặc định lấy từ `VersionInfo.Current` (đọc `AssemblyInformationalVersion` của entry assembly: CLI gen → version CLI, WPF gen → version WPF).

### Added — CLI `--version`
- `efmlgen --version` (hoặc `-v` / `version`) in ra version tool rồi exit. Giúp user biết bản đang chạy có phải bản cũ không.

### Added — WPF update-check + nút Update tự động
- Khi mở Designer, app gọi GitHub Releases API (`releases/latest`) check bản mới. Nếu có version mới hơn, hiện nút `⬇ Update` ở status bar.
- Bấm Update → confirm → tải installer (`EfmlGen-Setup-v{ver}.exe`) về `%TEMP%` kèm progress → chạy installer → tự shutdown app. Lỗi tải thì fallback mở trang release.
- Scope: chỉ WPF (VSIX để sau).

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.5.0`.

## [0.4.4] — 2026-06-03

### Fixed — EfmlMerger crash khi `.efml` có duplicate association name/fingerprint
- `Merge()` gọi `ToDictionary` trực tiếp trên `existing.Associations` → `ArgumentException` nếu file có 2 association trùng tên hoặc trùng structural fingerprint (vd sau khi edit tay hoặc merge conflict).
- Đổi sang `GroupBy(...).ToDictionary(g => g.Key, g => g.First())` để tolerate duplicates — first-one-wins, phần còn lại bị bỏ qua thay vì crash.

### Fixed — DatabaseModelMapper sinh ra association name trùng nhau
- Khi map từ DB, nhiều FK có thể tạo ra association cùng tên (vd 2 FK từ bảng `Order` sang `User`) → tên trùng gây lỗi downstream.
- Thêm `MakeUniqueName` — nếu tên đã dùng thì thêm suffix số: `Order_User`, `Order_User1`, `Order_User2`…

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.4.4`.

## [0.4.3] — 2026-05-26

### Fixed — Generated context không compile khi DB có PG sequence default
- `ContextEmitter` emit thẳng `Column.Default`/`Column.Computed` vào C# verbatim string `@"..."` mà **không escape `"`**. Với PostgreSQL serial column, scaffolder trả `nextval('dbo."Xxx_seq"'::regclass)` → output `@"nextval('dbo."Xxx_seq"'::regclass)"` đóng string sai chỗ → compile fail (CS1003 hàng loạt).
- Thêm helper `EscapeVerbatim(s) => s.Replace("\"", "\"\"")` và áp dụng cho cả `HasComputedColumnSql` lẫn `HasDefaultValueSql`.

### Changed — Bỏ `HasDefaultValueSql(nextval(...))` thừa cho serial column
- EF Core Npgsql scaffolder trả về cả `ValueGenerated=OnAdd` **và** `DefaultValueSql="nextval(...)"` cho `serial`/`bigserial`/`smallserial`. Hai info này trùng nghĩa — `ValueGeneratedOnAdd()` đã đủ để EF hiểu identity behavior.
- `DatabaseModelMapper.MapProperty` giờ detect pattern (`nextval(` cho Postgres, `next value for ` cho SQL Server) và drop `Default` khi `ValueGenerated==OnAdd` → output gọn, đồng nhất với SQL Server IDENTITY (vốn dĩ không có DefaultValueSql).

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `0.4.3`.

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
