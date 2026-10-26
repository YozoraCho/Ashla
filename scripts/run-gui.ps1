$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Building the solution also copies every plugin into the host's plugins folder.
dotnet build (Join-Path $root 'Ashla.slnx') -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

dotnet run --no-build -c Debug --project (Join-Path $root 'src/Ashla.Host/Ashla.Host.csproj') -- --gui --demo @args
if ($LASTEXITCODE -ne 0) { throw "Ashla exited with code $LASTEXITCODE" }
