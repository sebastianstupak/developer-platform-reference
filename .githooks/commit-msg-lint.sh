#!/usr/bin/env bash
# Validates conventional commits format:
# type(scope): description
# Types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert

COMMIT_MSG_FILE="$1"
COMMIT_MSG=$(cat "$COMMIT_MSG_FILE")

# Skip merge commits and fixup commits
if echo "$COMMIT_MSG" | grep -qE "^(Merge|fixup!|squash!)"; then
  exit 0
fi

PATTERN="^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\(.+\))?(!)?: .{1,100}$"

if ! echo "$COMMIT_MSG" | head -1 | grep -qE "$PATTERN"; then
  echo ""
  echo "❌ Invalid commit message format."
  echo ""
  echo "  Expected: type(scope): description"
  echo "  Got:      $COMMIT_MSG"
  echo ""
  echo "  Valid types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert"
  echo "  Examples:"
  echo "    feat(api-keys): add key rotation endpoint"
  echo "    fix(auth): handle expired token edge case"
  echo "    chore: update dependencies"
  echo ""
  exit 1
fi

exit 0
