#!/usr/bin/env bash
# Opens a pull request with the Device Check Agent manifests in microsoft/winget-pkgs (run by the repository owner):
#   bash deploy/winget/submit.sh 0.3.0
# Uses gh: forks winget-pkgs (no clone — it is huge), creates a branch, uploads the manifest files, opens the PR.
# After the PR is opened, Microsoft's CLA bot asks you to reply "@microsoft-github-policy-service agree" once.
set -euo pipefail

VERSION=${1:?usage: submit.sh <version>}
ID=BlkDem.DeviceCheckAgent
REL=manifests/b/BlkDem/DeviceCheckAgent/$VERSION
SRC="$(cd "$(dirname "$0")" && pwd)/$REL"
UPSTREAM=microsoft/winget-pkgs
ME=$(gh api user -q .login)
BRANCH="$ID-$VERSION"

[ -d "$SRC" ] || { echo "no manifests in $SRC" >&2; exit 1; }
command -v winget >/dev/null && winget validate --manifest "$(cygpath -w "$SRC" 2>/dev/null || echo "$SRC")"

echo "== fork $UPSTREAM → $ME/winget-pkgs"
gh repo fork "$UPSTREAM" --clone=false >/dev/null 2>&1 || true
for _ in $(seq 1 30); do gh api "repos/$ME/winget-pkgs" >/dev/null 2>&1 && break; sleep 5; done
gh repo sync "$ME/winget-pkgs" --source "$UPSTREAM" --branch master >/dev/null

echo "== branch $BRANCH"
BASE=$(gh api "repos/$UPSTREAM/git/ref/heads/master" -q .object.sha)
gh api -X POST "repos/$ME/winget-pkgs/git/refs" -f ref="refs/heads/$BRANCH" -f sha="$BASE" >/dev/null

for f in "$SRC"/*.yaml; do
  name=$(basename "$f")
  echo "   + $REL/$name"
  gh api -X PUT "repos/$ME/winget-pkgs/contents/$REL/$name" \
    -f message="New version: $ID version $VERSION" -f branch="$BRANCH" \
    -f content="$(base64 -w0 < "$f" 2>/dev/null || base64 < "$f" | tr -d '\n')" >/dev/null
done

echo "== pull request"
gh pr create --repo "$UPSTREAM" --head "$ME:$BRANCH" --base master \
  --title "New package: $ID version $VERSION" \
  --body "$(cat <<EOF
- [x] Have you signed the [Contributor License Agreement](https://cla.opensource.microsoft.com/microsoft/winget-pkgs)?
- [x] Is there a linked Issue? — not applicable, new package
- [x] Have you checked that there aren't other open [pull requests](https://github.com/microsoft/winget-pkgs/pulls) for the same manifest update/change?
- [x] This PR only modifies one (1) manifest
- [x] Have you [validated](https://github.com/microsoft/winget-pkgs/blob/master/doc/Authoring.md#validation) your manifest locally with \`winget validate --manifest <path>\`?
- [x] Have you tested your manifest locally with \`winget install --manifest <path>\`? — silent install/uninstall of this installer is tested in the project's CI
- [x] Does your manifest conform to the [1.9 schema](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema/1.9.0)?

Device Check Agent — open-source (MIT) hardware report and drive health for Windows: https://github.com/BlkDem/device-check
Per-user Inno Setup installer, no administrator rights.
EOF
)"
