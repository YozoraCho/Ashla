#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Building the solution also copies every plugin into the host's plugins folder.
dotnet build "$root/Ashla.slnx" -c Debug
dotnet run --no-build -c Debug --project "$root/src/Ashla.Host/Ashla.Host.csproj" -- --gui --demo "$@"
