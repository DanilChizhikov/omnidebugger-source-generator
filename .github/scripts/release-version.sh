#!/bin/sh
# Reads the release version from a release/X.Y.Z or hotfix/X.Y.Z branch and checks the pull request is ready to ship:
# the project carries that version, CHANGELOG.md has its section, the description (the release notes) is not empty
# and the tag does not exist yet. Writes `version` to $GITHUB_OUTPUT.
#
# Input comes through the environment only: HEAD_REF (the PR branch) and PR_BODY (the PR description).
set -e

PROJECT=src/DTech.OmniDebugger.SourceGenerator/DTech.OmniDebugger.SourceGenerator.csproj

fail() {
	echo "::error::$1"
	exit 1
}

case "$HEAD_REF" in
	release/*) VERSION=${HEAD_REF#release/} ;;
	hotfix/*) VERSION=${HEAD_REF#hotfix/} ;;
	*) fail "'$HEAD_REF' is not a release/X.Y.Z or hotfix/X.Y.Z branch." ;;
esac

if ! printf '%s' "$VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
	fail "'$VERSION' from the branch name is not a X.Y.Z version."
fi

PROJECT_VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT")
if [ "$PROJECT_VERSION" != "$VERSION" ]; then
	fail "<Version> in $PROJECT is '$PROJECT_VERSION'; bump it to $VERSION."
fi

if ! grep -qF "## [$VERSION]" CHANGELOG.md; then
	fail "CHANGELOG.md has no '## [$VERSION]' section."
fi

if [ -z "$(printf '%s' "$PR_BODY" | tr -d '[:space:]')" ]; then
	fail "The pull request description is empty; it becomes the release notes of v$VERSION."
fi

TAG=$(git ls-remote --tags origin "refs/tags/v$VERSION") || fail "Could not read the tags of origin."
if [ -n "$TAG" ]; then
	fail "Tag v$VERSION already exists."
fi

echo "Release v$VERSION is ready."
echo "version=$VERSION" >> "$GITHUB_OUTPUT"
