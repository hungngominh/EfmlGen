# EfmlGen — Visual Studio 2022 Extension (VSIX)

Tích hợp EfmlGen vào Visual Studio 2022 thay cho việc gọi CLI hoặc mở WPF GUI riêng. Engine vẫn là `EfmlGen.Cli.exe` được bundle sẵn vào `.vsix`.

> Folder này độc lập với solution chính (`EfmlGen.sln` net8.0). VSIX target net472 vì `devenv.exe` host trên .NET Framework 4.x.

## Yêu cầu build

- Visual Studio 2022 (17.0+) có workload **Visual Studio extension development**.
- .NET 8 SDK (để publish bundled CLI).
- PowerShell 5.1+ (sẵn trên Windows).

## Build

```powershell
# Mở src-vsix/EfmlGen.Vsix.sln trong VS 2022 và build,
# hoặc dòng lệnh:
msbuild src-vsix\EfmlGen.Vsix.sln /t:Restore,Build /p:Configuration=Release
```

Build sẽ:
1. Chạy `dotnet publish src/EfmlGen.Cli` (self-contained, win-x64, single-file) vào `src-vsix/EfmlGen.Vsix/tools/cli/`.
2. Đóng gói `.vsix` tại `src-vsix/EfmlGen.Vsix/bin/Release/EfmlGen.Vsix.vsix` (≈ 25–30 MB).

## Debug (F5)

F5 trên project `EfmlGen.Vsix` chạy `devenv /RootSuffix Exp` — Experimental Instance của VS 2022, sandbox không ảnh hưởng VS chính.

## Cài đặt

Double-click file `.vsix` → `VSIXInstaller.exe` tự cài. Hoặc qua `Extensions → Manage Extensions → Install from file`.

## Cấu trúc

```
src-vsix/
  EfmlGen.Vsix.sln
  build/publish-cli.ps1        # helper standalone (build không cần)
  EfmlGen.Vsix/
    EfmlGen.Vsix.csproj        # legacy non-SDK csproj
    source.extension.vsixmanifest
    EfmlGenPackage.cs          # AsyncPackage entry point
    PackageGuids.cs            # tất cả GUID hằng số
    Commands/                  # Solution Explorer right-click commands (phase 3)
    ToolWindow/                # Tool Window (phase 4)
    Wizard/                    # New Item Wizard (phase 5)
    Services/                  # CliRunner, OutputPaneLogger, ProfileStore (linked)
    Resources/                 # Icons
    tools/cli/                 # Publish drop từ MSBuild target — KHÔNG check-in
```

## Phase hiện tại

Đang ở **Phase 1** (skeleton + bundle pipeline). Xem plan chi tiết ở `.claude/plans/proud-sauteeing-sundae.md` của thư mục home.
