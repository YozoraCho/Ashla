# Builds the player release of Ashla: a folder and a zip with Ashla.exe (no console window, no .NET install
# needed) and its native libraries. Plugins aren't included: players install them from Holocron.
#
#   powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
#
# Output: artifacts\Ashla-<version>-win-x64\  and  artifacts\Ashla-<version>-win-x64.zip
param(
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostProject = Join-Path $root 'src/Ashla.Host/Ashla.Host.csproj'

$version = (dotnet msbuild $hostProject -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) { throw 'Could not read the version from Ashla.Host.csproj' }

$name = "Ashla-$version-$Runtime"
$artifacts = Join-Path $root 'artifacts'
$out = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "Publishing Ashla $version ($Runtime) to $out"
# Self-contained single-file exe. Native libraries (GLFW, cimgui) stay as loose files next to it: GLFW is
# loaded by Silk.NET's own loader, which looks in the application folder rather than inside the bundle.
dotnet publish $hostProject -c Release -r $Runtime --self-contained true `
    -p:AshlaPlayerBuild=true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publishing Ashla failed' }


Copy-Item (Join-Path $root 'docs/PLAYER-README.txt') (Join-Path $out 'README.txt')
# The AGPL-3.0 and the bundled libraries' licenses require their texts to travel with the program.
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $out 'LICENSE.txt')
Copy-Item (Join-Path $root 'docs/THIRD-PARTY-NOTICES.txt') (Join-Path $out 'THIRD-PARTY-NOTICES.txt')
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Done: $zip"
