#!/usr/bin/env bash
# One-time setup of automatic deployment. Run by the repository owner from the repo root:
#   bash deploy/setup-deploy-key.sh
#
# 1. The site directory becomes user1:fastuser with setgid 2775 (as the other sites deployed by user1).
# 2. A new SSH key for GitHub Actions is added to user1's authorized_keys, restricted with rrsync:
#    it can only rsync inside the site directory (no shell, no other paths). A backup of authorized_keys is kept.
# 3. GitHub Actions secrets are set: DEPLOY_SSH_KEY, DEPLOY_HOST, DEPLOY_USER, DEPLOY_KNOWN_HOSTS.
#    The private key exists only in GitHub secrets afterwards.
# Re-running replaces the previous deploy key.
set -euo pipefail

HOST=${HOST:-fp.cloud28.umolab.ru}
DEPLOY_USER=${DEPLOY_USER:-user1}
SITE=${SITE:-/var/www/fastuser/data/www/device-check.umolab.ru}
REPO=${REPO:-BlkDem/device-check}
COMMENT="github-actions device-check deploy"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
ssh-keygen -q -t ed25519 -N "" -C "$COMMENT" -f "$TMP/key"
PUB=$(cat "$TMP/key.pub")

echo "== server: site directory and restricted key"
ssh "$DEPLOY_USER@$HOST" "set -e
sudo chown -R $DEPLOY_USER:fastuser '$SITE'
sudo chmod 2775 '$SITE'
ls -ld '$SITE'
cp ~/.ssh/authorized_keys ~/.ssh/authorized_keys.bak-\$(date +%Y%m%d%H%M%S)
sed -i '/$COMMENT/d' ~/.ssh/authorized_keys
echo 'command=\"/usr/bin/rrsync $SITE\",restrict $PUB' >> ~/.ssh/authorized_keys"

echo "== check: the deploy key must not give a shell"
if ssh -i "$TMP/key" -o IdentitiesOnly=yes -o BatchMode=yes "$DEPLOY_USER@$HOST" id >/dev/null 2>&1; then
  echo "ERROR: shell access works with the deploy key" >&2
  exit 1
fi
echo "shell denied (expected)"

echo "== GitHub secrets ($REPO)"
gh secret set DEPLOY_SSH_KEY --repo "$REPO" < "$TMP/key"
gh secret set DEPLOY_HOST --repo "$REPO" --body "$HOST"
gh secret set DEPLOY_USER --repo "$REPO" --body "$DEPLOY_USER"
ssh-keyscan -t ed25519,ecdsa,rsa "$HOST" 2>/dev/null | gh secret set DEPLOY_KNOWN_HOSTS --repo "$REPO"
echo "done"
