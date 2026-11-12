<#
  Сборка NEXORA.exe и NEXORA-Setup.exe.
  Запуск из корня проекта (PowerShell):   .\build\build.ps1
  Нужно: .NET 10 SDK и Inno Setup 6  (winget install JRSoftware.InnoSetup)
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dist    = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

# Версия берётся из Directory.Build.props
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
Write-Host "Версия: $version"

Write-Host '1/4 Тесты...'
dotnet test (Join-Path $root 'NEXORA.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Тесты не прошли — сборка остановлена.' }

Write-Host '2/4 Публикация (self-contained, win-x64, один файл)...'
dotnet publish (Join-Path $root 'src\NEXORA.App\NEXORA.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish завершился с ошибкой.' }
Copy-Item (Join-Path $publish 'NEXORA.exe') (Join-Path $dist 'NEXORA.exe')

Write-Host '3/4 Установщик (Inno Setup)...'
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 не найден. Установите: winget install JRSoftware.InnoSetup' }
& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\NEXORA.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC завершился с ошибкой.' }

Write-Host '4/4 Контрольные суммы...'
Copy-Item (Join-Path $root 'assets\NEXORA.ico') (Join-Path $dist 'NEXORA.ico')
Get-ChildItem $dist -File | ForEach-Object {
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    '{0,-18} {1,8:N1} МБ  SHA256 {2}' -f $_.Name, ($_.Length / 1MB), $h
}
Write-Host "Готово: $dist"
