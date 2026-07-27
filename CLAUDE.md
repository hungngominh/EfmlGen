# EfmlGen — Hướng dẫn cho Claude Code

## Quy trình Release (Bump version → Build → Push)

Mỗi lần release phải làm **đủ 4 bước** theo thứ tự này.

---

### Bước 1 — Bump version trong 4 file

Thay `OLD` → `NEW` (ví dụ `0.8.1` → `0.8.2`):

| File | Dòng cần sửa |
|------|-------------|
| `src/EfmlGen.Wpf/EfmlGen.Wpf.csproj` | `<Version>NEW</Version>` |
| `src/EfmlGen.Cli/EfmlGen.Cli.csproj` | `<Version>NEW</Version>` |
| `installer.iss` | `#define MyAppVersion "NEW"` |
| `src-vsix/EfmlGen.Vsix/source.extension.vsixmanifest` | `Version="NEW"` |

---

### Bước 2 — Cập nhật CHANGELOG.md

Thêm section mới ngay trên section cũ nhất:

```markdown
## [NEW] — YYYY-MM-DD

### <Changed/Added/Fixed> — ...
- ...

### Build
- Bump CLI + WPF + installer + VSIX manifest sang `NEW`.
```

---

### Bước 3 — Build 3 assets (dùng PowerShell, không dùng Bash cho MSBuild/ISCC)

```powershell
# 1. Publish WPF + CLI (cùng output folder)
dotnet publish src/EfmlGen.Wpf/EfmlGen.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-release/EfmlGen-win-x64
dotnet publish src/EfmlGen.Cli/EfmlGen.Cli.csproj  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-release/EfmlGen-win-x64

# 2. Zip
Compress-Archive -Path 'publish-release\EfmlGen-win-x64\*' -DestinationPath 'publish-release\EfmlGen-vNEW-win-x64.zip' -Force

# 3. Installer (Inno Setup — user-local, KHÔNG nằm trong Program Files)
& "C:\Users\NGOMI\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer.iss
# Output: publish-release/EfmlGen-Setup-vNEW.exe

# 4. VSIX — BẮT BUỘC dùng PowerShell (Bash/MSYS2 làm hỏng /p: switch)
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" `
  "src-vsix\EfmlGen.Vsix\EfmlGen.Vsix.csproj" `
  /t:Rebuild /p:Configuration=Release /p:DeployExtension=false /v:m /nologo
# Output: src-vsix\EfmlGen.Vsix\bin\Release\EfmlGen.Vsix.vsix
Copy-Item "src-vsix\EfmlGen.Vsix\bin\Release\EfmlGen.Vsix.vsix" "publish-release\EfmlGen-vNEW.vsix" -Force
```

**Sanity check kích thước** (drift lớn = build sai):
- zip ~104 MB, installer ~77 MB, vsix ~37 MB

---

### Bước 4 — Commit, tag, push, tạo GitHub Release

```bash
# Commit
git add CHANGELOG.md installer.iss \
  src-vsix/EfmlGen.Vsix/source.extension.vsixmanifest \
  src/EfmlGen.Cli/EfmlGen.Cli.csproj \
  src/EfmlGen.Wpf/EfmlGen.Wpf.csproj \
  src/EfmlGen.Wpf/MainWindow.xaml   # nếu có thay đổi UI
git commit -m "Release vNEW: <mô tả ngắn>"

# Tag + push
git tag vNEW
git push origin main
git push origin vNEW

# Tạo GitHub Release + upload assets
gh release create vNEW \
  --title "vNEW — <tiêu đề>" \
  --notes "<release notes>" \
  --latest
gh release upload vNEW \
  publish-release/EfmlGen-Setup-vNEW.exe \
  publish-release/EfmlGen-vNEW-win-x64.zip \
  publish-release/EfmlGen-vNEW.vsix
```

---

## Toolchain (máy NGOMI)

- `dotnet` — có trên PATH
- MSBuild — `C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe` (VS đã upgrade từ 2022 Community lên 18 Insiders — path đổi, nếu lại đổi thì dùng `vswhere.exe -all -products * -format json` để tìm `installationPath`)
- Inno Setup — `C:\Users\NGOMI\AppData\Local\Programs\Inno Setup 6\ISCC.exe` (user-local, `where iscc` không tìm thấy)
- `gh` — có trên PATH
