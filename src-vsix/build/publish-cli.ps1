<#
.SYNOPSIS
  Publish EfmlGen.Cli as self-contained, single-file Windows x64 binary into
  src-vsix/EfmlGen.Vsix/tools/cli/ — for manual standalone runs.

.DESCRIPTION
  The VSIX MSBuild target `PublishBundledCli` does this automatically before
  packing the .vsix. This script is for cases when bạn muốn publish CLI tay
  (vd để smoke-test trước khi đóng .vsix), hoặc khi CI tách step.

.PARAMETER Configuration
  Debug | Release (default Release).

.EXAMPLE
  pwsh src-vsix/build/publish-cli.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$cliProj = Join-Path $repoRoot 'src\EfmlGen.Cli\EfmlGen.Cli.csproj'
$outDir = Join-Path $repoRoot 'src-vsix\EfmlGen.Vsix\tools\cli'

if (-not (Test-Path $cliProj)) {
    throw "Không tìm thấy CLI project tại: $cliProj"
}

Write-Host "[publish-cli] Publishing $cliProj ($Configuration, win-x64, self-contained, single-file)..."
& dotnet publish $cliProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $outDir 'EfmlGen.Cli.exe'
if (-not (Test-Path $exe)) {
    throw "Publish thành công nhưng không thấy EfmlGen.Cli.exe tại $exe"
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "[publish-cli] OK: $exe ($size MB)"
