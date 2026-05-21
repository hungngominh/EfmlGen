# Changelog

Toàn bộ thay đổi đáng chú ý của EfmlGen được liệt kê tại đây. Format theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning theo [SemVer](https://semver.org/).

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

[0.3.1]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.3.1
[0.2.0]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.2.0
[0.1.0]: https://github.com/hungngominh/EfmlGen/releases/tag/v0.1.0
