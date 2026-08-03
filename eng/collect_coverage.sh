#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
sharpcpp_root="$(cd -- "$script_directory/.." && pwd)"
results_directory="${SHARPCPP_COVERAGE_RESULTS_DIR:-$sharpcpp_root/TestResults/coverage}"
minimum_coverage="${SHARPCPP_COVERAGE_MINIMUM:-95}"
cmake_root="${SHARPCPP_CMAKE_ROOT:-/home/rk/.local/share/uv/tools/cmake/bin}"
ninja_root="${SHARPCPP_NINJA_ROOT:-/home/rk/.local/share/uv/tools/ninja/bin}"

if [[ -d "$cmake_root" ]]; then
    PATH="$cmake_root:$PATH"
fi
if [[ -d "$ninja_root" ]]; then
    PATH="$ninja_root:$PATH"
fi
export PATH

# Force the real E2E generator to run while the process-level collector is
# active. Its generated C# and C++ are then compiled and exercised by E2E.

# ClangSharp needs the GCC builtin headers when the selected compiler target
# differs from the host triple spelling.
if command -v gcc >/dev/null 2>&1; then
    gcc_include="$(gcc -print-file-name=include)"
    if [[ -d "$gcc_include" ]]; then
        CPATH="$gcc_include${CPATH:+:$CPATH}"
        export CPATH
    fi
fi

mkdir -p "$results_directory"
dotnet tool restore
rm -f "$sharpcpp_root/build/native/dev/generated/sharpcpp-e2e/generate.stamp"

coverage_file="$results_directory/coverage.cobertura.xml"
test_command=(
    dotnet test "$sharpcpp_root/tests/SharpCpp.Tests.E2E/SharpCpp.Tests.E2E.csproj"
    --configuration Debug
    --results-directory "$results_directory"
    "$@"
)
test_command_line="${test_command[0]}"
for argument in "${test_command[@]:1}"; do
    argument="${argument//\\/\\\\}"
    argument="${argument//\"/\\\"}"
    test_command_line+=" \"$argument\""
done

dotnet dotnet-coverage collect "$test_command_line" \
    --settings "$sharpcpp_root/coverage.runsettings" \
    --output "$coverage_file" \
    --output-format cobertura

if [[ -z "$coverage_file" ]]; then
    printf 'Coverage report was not produced under %s\n' "$results_directory" >&2
    exit 1
fi

coverage_header="$(rg -o '<coverage[^>]*' "$coverage_file" | head -1)"
line_rate="$(printf '%s\n' "$coverage_header" | sed -n 's/.*line-rate="\([^"]*\)".*/\1/p')"
lines_covered="$(printf '%s\n' "$coverage_header" | sed -n 's/.*lines-covered="\([^"]*\)".*/\1/p')"
lines_valid="$(printf '%s\n' "$coverage_header" | sed -n 's/.*lines-valid="\([^"]*\)".*/\1/p')"

awk -v line_rate="$line_rate" \
    -v lines_covered="$lines_covered" \
    -v lines_valid="$lines_valid" \
    -v minimum_coverage="$minimum_coverage" \
    'BEGIN {
        printf "Line coverage: %.2f%% (%s/%s lines)\n", 100 * line_rate, lines_covered, lines_valid
        if (100 * line_rate < minimum_coverage) {
            printf "Coverage gate failed: line coverage must be at least %.2f%%\n", minimum_coverage > "/dev/stderr"
            exit 1
        }
    }'

printf 'Cobertura report: %s\n' "$coverage_file"
