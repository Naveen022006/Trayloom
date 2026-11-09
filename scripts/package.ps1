# Trayloom packaging: publish -> add the setup script -> zip.
#
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1             # publish app, portable zip and standalone setup exe
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -NoPublish  # package an existing dist\Trayloom
#
# The standalone setup bundles the portable zip and starts Trayloom's own per-user installer. Both remain unsigned;
# an unsigned download can be blocked by Smart App Control until the release is signed with a trusted certificate.
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
$setupProject = Join-Path $PSScriptRoot 'Setup\Trayloom.Setup.csproj'
$setupBuild = Join-Path $OutDir 'setup-build'
$setupExe = Join-Path $OutDir 'Trayloom-Setup.exe'

if (-not $NoPublish) {
    Write-Host 'Publishing Trayloom (self-contained, win-x64)...'
    & dotnet publish (Join-Path $root 'src\Trayloom.App\Trayloom.App.csproj') -c $Configuration -r win-x64 --self-contained true -o $app --nologo -v q -m:1
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    Get-ChildItem $app -Filter *.pdb -Recurse | Remove-Item -Force
}
if (-not (Test-Path (Join-Path $app 'Trayloom.exe'))) { throw "No Trayloom.exe in $app" }

Copy-Item $setup (Join-Path $app 'Trayloom-Setup.cmd') -Force
Copy-Item $setup (Join-Path $OutDir 'Trayloom-Setup.cmd') -Force

Write-Host 'Creating the zip...'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host 'Building the standalone setup executable...'
& dotnet publish $setupProject -c $Configuration -r win-x64 --self-contained true -o $setupBuild -p:TrayloomPayload=$zip -m:1 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed' }
Copy-Item (Join-Path $setupBuild 'Trayloom-Setup.exe') $setupExe -Force

Write-Host 'Writing SHA256SUMS.txt...'
$sums = Join-Path $OutDir 'SHA256SUMS.txt'
$lines = foreach ($f in @($setupExe, $zip, (Join-Path $OutDir 'Trayloom-Setup.cmd'))) {
    $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant()
    "$h  $(Split-Path -Leaf $f)"
}
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host 'Done:'
Write-Host "  $setupExe   (standalone installer)"
Write-Host "  $zip"
Write-Host "  $(Join-Path $OutDir 'Trayloom-Setup.cmd')   (double-click next to the zip, or inside the unzipped folder)"
Write-Host "  $sums   (SHA-256 of the downloadable files; publish it with the release)"
Write-Host "  $app\Trayloom.exe   (runs directly; offers Install / Just run it)"
