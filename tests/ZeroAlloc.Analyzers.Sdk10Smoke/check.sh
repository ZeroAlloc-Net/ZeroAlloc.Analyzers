#!/usr/bin/env bash
# Builds the SDK 10.0.1xx consumer and fails unless the build reports ZA0105, which proves the
# compiler loaded the analyzer. A skipped analyzer fails the build with CS9057 first.
# Usage: check.sh <version of the packed ZeroAlloc.Analyzers>
set -euo pipefail
version="${1:?pass the version of the packed ZeroAlloc.Analyzers package}"
cd "$(dirname "$0")/Consumer"
dotnet --version
log="$(mktemp)"
trap 'rm -f "$log"' EXIT
dotnet build -c Release -p:ZeroAllocAnalyzersVersion="$version" 2>&1 | tee "$log"
if ! grep -q "warning ZA0105" "$log"; then
  echo "FAIL: the build did not report ZA0105, so the analyzer did not run" >&2
  exit 1
fi
echo "SDK 10 consumer: analyzer loaded, ZA0105 reported."
