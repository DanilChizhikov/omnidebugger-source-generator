#!/bin/sh
# Tests and builds the source generator, then copies the DLL into an OmniDebugger package.
#
#   ./build.sh [path to OmniDebugger]
#
# The package path comes from the first argument or from OMNIDEBUGGER_PATH. Without one the DLL is only built.
set -e
cd "$(dirname "$0")"

PROJECT=src/DTech.OmniDebugger.SourceGenerator
DLL=$PROJECT/bin/Release/netstandard2.0/DTech.OmniDebugger.SourceGenerator.dll
PACKAGE=${1:-$OMNIDEBUGGER_PATH}

if [ -n "$PACKAGE" ] && [ ! -f "$PACKAGE/package.json" ]; then
	echo "'$PACKAGE' is not an OmniDebugger package: it holds no package.json." >&2
	exit 1
fi

dotnet test tests/DTech.OmniDebugger.SourceGenerator.Tests -c Release
dotnet build $PROJECT -c Release

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' $PROJECT/DTech.OmniDebugger.SourceGenerator.csproj)

if [ -z "$PACKAGE" ]; then
	echo "Built DTech.OmniDebugger.SourceGenerator $VERSION: $DLL"
	exit 0
fi

mkdir -p "$PACKAGE/Runtime/Analyzers"
cp "$DLL" "$PACKAGE/Runtime/Analyzers/"
echo "Copied DTech.OmniDebugger.SourceGenerator $VERSION to $PACKAGE/Runtime/Analyzers/"
