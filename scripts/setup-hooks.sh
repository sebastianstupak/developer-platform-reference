#!/usr/bin/env sh
# Run after cloning to install git hooks.
# Requires lefthook: https://github.com/evilmartians/lefthook

set -e

HOOKS_DIR="$(git rev-parse --git-dir)/hooks"

install_hook() {
  HOOK="$HOOKS_DIR/$1"
  printf '#!/usr/bin/env sh\nlefthook run %s "$@"\n' "$1" > "$HOOK"
  chmod +x "$HOOK"
  echo "  installed $1"
}

echo "Installing lefthook wrappers into .git/hooks/..."
install_hook "pre-commit"
install_hook "commit-msg"
echo "Done. Run 'lefthook run pre-commit' to test."
