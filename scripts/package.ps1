# Trayloom packaging: publish -> add the setup script -> zip.
#
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1             # dist\Trayloom, dist\Trayloom-win-x64.zip, dist\Trayloom-Setup.cmd
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -NoPublish  # only re-make the zip from an existing dist\Trayloom
#
# There is no separate installer program: the installer is Trayloom.exe itself ("Trayloom.exe --install", also offered the
# first time Trayloom.exe runs from outside %LOCALAPPDATA%\Programs\Trayloom). It installs per user, with no administrator
# rights: copies the app, creates Start menu / Desktop shortcuts, registers an Apps & features entry (uninstall =
# "Trayloom.exe --uninstall"). Trayloom-Setup.cmd is the double-click wrapper for it (works from the unzipped folder, or
# next to the zip). A self-extracting .exe made with Windows IExpress was tried and does not run on a machine with Smart App
# Control, so it is not shipped. Nothing here is signed: sign dist\Trayloom\*.exe/dll with a code-signing certificate to avoid
# Smart App Control scanning/blocking (signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256 /a <file>).
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist'),
    [string]$Configuration = 'Release',
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$app = Join-Path $OutDir 'Trayloom'
$zip = Join-Path $OutDir 'Trayloom-win-x64.zip'
$setup = Join-Path $PSScriptRoot 'Trayloom-Setup.cmd'

if (-not $NoPublish) {
    Write-Host 'Publishing Trayloom (self-contained, win-x64)...'
    & dotnet publish (Join-Path $root 'src\Trayloom.App\Trayloom.App.csproj') -c $Configuration -r win-x64 --self-contained true -o $app --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    Get-ChildItem $app -Filter *.pdb -Recurse | Remove-Item -Force
}
if (-not (Test-Path (Join-Path $app 'Trayloom.exe'))) { throw "No Trayloom.exe in $app" }

Copy-Item $setup (Join-Path $app 'Trayloom-Setup.cmd') -Force
Copy-Item $setup (Join-Path $OutDir 'Trayloom-Setup.cmd') -Force

Write-Host 'Creating the zip...'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host ''
Write-Host 'Done:'
Write-Host "  $zip"
Write-Host "  $(Join-Path $OutDir 'Trayloom-Setup.cmd')   (double-click next to the zip, or inside the unzipped folder)"
Write-Host "  $app\Trayloom.exe   (runs directly; offers Install / Just run it)"
