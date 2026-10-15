#!/usr/bin/env bash
# Measures the build-time overhead of ZeroAlloc.Analyzers on a fixed sample project.
#
# For each scenario, the same sources are built twice side by side: once without the package
# and once with it. Builds alternate between the two so that drift on the machine hits both
# equally. Three kinds of build are timed:
#
#   full         dotnet build --no-incremental: every target runs and csc recompiles everything
#   edit         one source file changed, then a normal dotnet build: csc reruns once
#   no-op        nothing changed, then a normal dotnet build: csc is skipped
#
# Every build runs its own compiler and MSBuild nodes, -p:UseSharedCompilation=false and
# -nodeReuse:false, so a compiler server that other builds on the machine share cannot skew it.
# Every build also passes -p:ReportAnalyzer=true and -v:d, so the compiler reports how long each
# analyzer assembly ran. That figure is summed across the compiler's analysis threads, so it can exceed
# the wall-clock difference.
#
# The result is relative: for each pair of builds, the build with the package divided by the one
# without it. On a shared or virtual machine absolute times say little, but both builds of a pair
# run back to back on the same CPUs, so their ratio holds. Pin the run to fixed CPUs if you can:
#
#   taskset -c 2-5 tools/build-overhead/measure.sh
#
# Usage: tools/build-overhead/measure.sh [-n runs] [-k copies] [-v version] [-o outdir]
#   -n  timed builds per kind and variant, default 15
#   -k  copies of the sample sources, default 40: 240 files, about 18,000 lines
#   -v  ZeroAlloc.Analyzers package version from NuGet, default 1.11.0
#   -o  work and output directory, default a new temporary directory
#
# Needs the .NET 10 SDK, the net8.0 and netstandard2.0 reference packs from NuGet, and python3.
# Close other heavy processes first: the timings are only as quiet as the machine.

set -euo pipefail

runs=15
copies=40
version=1.11.0
out=""
while getopts "n:k:v:o:" opt; do
  case "$opt" in
    n) runs=$OPTARG ;;
    k) copies=$OPTARG ;;
    v) version=$OPTARG ;;
    o) out=$OPTARG ;;
    *) echo "usage: $0 [-n runs] [-k copies] [-v version] [-o outdir]" >&2; exit 2 ;;
  esac
done

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
out=${out:-$(mktemp -d -t za-build-overhead.XXXXXX)}
mkdir -p "$out"
csv="$out/raw.csv"
echo "scenario,variant,kind,run,wall_ms,analyzer_s" > "$csv"

# scenario name | TargetFrameworks | extra NoWarn
scenarios=(
  "netstandard2.0|netstandard2.0|"
  "net8.0|net8.0|"
  "net8.0+netstandard2.0|net8.0;netstandard2.0|"
  "net8.0, ZA0607 and ZA0502 off|net8.0|;ZA0607;ZA0502"
)

make_project() { # dir tfms nowarn with_package
  local dir=$1 tfms=$2 nowarn=$3 with=$4
  rm -rf "$dir"
  mkdir -p "$dir"
  # Empty Directory.Build files stop MSBuild from importing anything above the work directory.
  echo "<Project />" > "$dir/Directory.Build.props"
  echo "<Project />" > "$dir/Directory.Build.targets"
  echo "<Project />" > "$dir/Directory.Packages.props"
  local ref=""
  if [ "$with" = "on" ]; then
    ref="<ItemGroup><PackageReference Include=\"ZeroAlloc.Analyzers\" Version=\"$version\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$dir/Sample.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>$tfms</TargetFrameworks>
    <LangVersion>7.3</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <NoWarn>\$(NoWarn);CS0168;CS0219$nowarn</NoWarn>
  </PropertyGroup>
  $ref
</Project>
EOF
  local i
  for ((i = 0; i < copies; i++)); do
    mkdir -p "$dir/Unit$i"
    for f in "$here"/sample/*.cs; do
      sed "s/__N__/$i/g" "$f" > "$dir/Unit$i/$(basename "$f")"
    done
  done
  dotnet restore "$dir" -nologo -v:q > /dev/null
}

# Runs one build and appends a CSV row. Prints nothing.
build() { # scenario variant kind run dir extra-args...
  local scenario=$1 variant=$2 kind=$3 run=$4 dir=$5
  shift 5
  local log="$out/last-build.log" start end
  start=$(date +%s%N)
  if ! dotnet build "$dir" -c Release --no-restore -nologo -v:d -nodeReuse:false -p:UseSharedCompilation=false -p:ReportAnalyzer=true "$@" > "$log" 2>&1; then
    cat "$log" >&2
    echo "build failed: $scenario $variant $kind" >&2
    exit 1
  fi
  end=$(date +%s%N)
  # The compiler reports one line per analyzer assembly; sum ours across target frameworks.
  local secs
  # It only prints at detailed verbosity, and not at all when csc does not run.
  secs=$({ grep -E '^\s+[0-9.<]+\s+[0-9]+\s+ZeroAlloc\.Analyzers, Version' "$log" || true; } \
    | awk '{ v = $1; sub("<", "", v); s += v } END { printf "%.3f", s + 0 }')
  echo "\"$scenario\",$variant,$kind,$run,$(( (end - start) / 1000000 )),$secs" >> "$csv"
}

touch_source() { # dir run
  echo "// edit $2" >> "$1/Unit0/OrderService.cs"
}

for entry in "${scenarios[@]}"; do
  IFS='|' read -r name tfms nowarn <<< "$entry"
  echo "== $name" >&2
  safe=$(echo "$name" | tr -c 'a-zA-Z0-9.+\n' '_')
  off="$out/$safe-off"
  on="$out/$safe-on"
  make_project "$off" "$tfms" "$nowarn" off
  make_project "$on" "$tfms" "$nowarn" on

  for v in off on; do # warm the build server and the file cache
    d=$off; [ "$v" = on ] && d=$on
    build "$name" "$v" warmup 0 "$d" --no-incremental
    build "$name" "$v" warmup 0 "$d" --no-incremental
  done

  for ((r = 1; r <= runs; r++)); do
    build "$name" off full "$r" "$off" --no-incremental
    build "$name" on full "$r" "$on" --no-incremental
  done
  for ((r = 1; r <= runs; r++)); do
    touch_source "$off" "$r"; build "$name" off edit "$r" "$off"
    touch_source "$on" "$r"; build "$name" on edit "$r" "$on"
  done
  for ((r = 1; r <= runs; r++)); do
    build "$name" off no-op "$r" "$off"
    build "$name" on no-op "$r" "$on"
  done
done

python3 -I "$here/summarize.py" "$csv" > "$out/results.md"
{
  echo
  echo "ZeroAlloc.Analyzers $version, $copies copies of the sample, $runs builds per cell."
  echo "SDK $(dotnet --version), $(uname -sr), $(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2- | sed 's/^ //')"
} >> "$out/results.md"
cat "$out/results.md"
echo "raw data: $csv" >&2
