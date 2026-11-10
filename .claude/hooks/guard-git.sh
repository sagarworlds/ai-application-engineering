#!/usr/bin/env bash
# PreToolUse hook (Bash): enforce the repo's git conventions before a command runs.
#   - no Co-Authored-By trailers in commit messages
#   - no direct pushes to main/master
set -euo pipefail

command="$(jq -r '.tool_input.command // ""' <<<"$(cat)")"

if grep -qE '\bgit\b.*\bcommit\b' <<<"$command" && grep -qi 'co-authored-by' <<<"$command"; then
  echo "Blocked: this repository does not use Co-Authored-By trailers. Remove it from the commit message." >&2
  exit 2
fi

# Match the branch as a whole argument (or refspec target) so "sp/main-fix" is still allowed.
if grep -qE '\bgit\b.*\bpush\b.*[ :](main|master)( |$)' <<<"$command"; then
  echo "Blocked: do not push directly to main. Push a feature branch (sp/...) and open a pull request." >&2
  exit 2
fi
exit 0
