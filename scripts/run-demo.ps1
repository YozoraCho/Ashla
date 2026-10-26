$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostProject = Join-Path $root 'src/Ashla.Host/Ashla.Host.csproj'
$hostOutput = Join-Path $root 'src/Ashla.Host/bin/Debug/net10.0'

dotnet build $hostProject -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Host build failed' }
foreach ($name in @('HelloWorld', 'CombatCounter', 'HelloWorldOverlay')) {
    $pluginProject = Join-Path $root "samples/$name/$name.csproj"
    dotnet build $pluginProject -c Debug
    if ($LASTEXITCODE -ne 0) { throw "$name build failed" }
    $from = Join-Path $root "samples/$name/bin/Debug/net10.0"
    $to = Join-Path $hostOutput "plugins/$name"
    New-Item -ItemType Directory -Force $to | Out-Null
    Copy-Item (Join-Path $from '*') $to -Recurse -Force
}

dotnet run --no-build -c Debug --project $hostProject -- --demo
if ($LASTEXITCODE -ne 0) { throw 'Demo failed' }
