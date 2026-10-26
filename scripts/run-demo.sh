#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
host="$root/src/Ashla.Host/Ashla.Host.csproj"
out="$root/src/Ashla.Host/bin/Debug/net10.0"
dotnet build "$host" -c Debug
for name in HelloWorld CombatCounter HelloWorldOverlay; do
  dotnet build "$root/samples/$name/$name.csproj" -c Debug
  mkdir -p "$out/plugins/$name"
  cp -R "$root/samples/$name/bin/Debug/net10.0/." "$out/plugins/$name/"
done
dotnet run --no-build -c Debug --project "$host" -- --demo
