#!/usr/bin/env bash
# PreToolUse hook (Write|Edit): refuse to write content that looks like a real credential.
# Exit code 2 blocks the tool call and feeds stderr back to Claude.
set -euo pipefail

input="$(cat)"
content="$(jq -r '[.tool_input.content, .tool_input.new_string] | map(select(. != null)) | join("\n")' <<<"$input")"
file="$(jq -r '.tool_input.file_path // ""' <<<"$input")"

# This script and the verify skill necessarily contain the patterns themselves.
case "$file" in
  */.claude/hooks/*|*/.claude/skills/*) exit 0 ;;
esac

patterns=(
  'sk-[A-Za-z0-9_-]{20,}'                                   # OpenAI-style keys
  '-----BEGIN [A-Z ]*PRIVATE KEY-----'                      # PEM private keys
  '"(ApiKey|api[_-]?key|ClientSecret|Password)"[[:space:]]*:[[:space:]]*"[^"<$ ]{16,}"'  # JSON config secrets
  'AccountKey=[A-Za-z0-9+/=]{20,}'                          # Azure storage connection strings
)

for pattern in "${patterns[@]}"; do
  if grep -qE -- "$pattern" <<<"$content"; then
    echo "Blocked: content written to '$file' looks like a secret (pattern: $pattern)." >&2
    echo "Use 'dotnet user-secrets' or an environment variable instead; commit only placeholders." >&2
    exit 2
  fi
done
exit 0
