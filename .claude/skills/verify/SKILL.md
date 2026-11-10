---
name: verify
description: Run the full local quality gate (build, tests, formatting, vulnerable packages, secret scan) before committing or opening a PR.
---

Run each step from the repository root and stop at the first failure. Report real output; never claim a step passed without running it.

1. `dotnet build --nologo` — must end with `0 Warning(s)` and `0 Error(s)`.
2. `dotnet test --nologo --no-build` — all tests pass.
3. `dotnet format --verify-no-changes --no-restore` — if it fails, run `dotnet format` and re-run the tests.
4. `dotnet list package --vulnerable --include-transitive` — no vulnerable packages.
5. Secret scan of what is about to be committed:
   `git diff --cached -U0 | grep -nEi '(api[_-]?key|secret|password)["'"'"' ]*[:=] *["'"'"'][^"'"'"'<$ ]{12,}|sk-[A-Za-z0-9]{20,}|BEGIN [A-Z ]*PRIVATE KEY' || echo "no secrets found"`
6. If data-access or auth code changed, confirm a tenant-boundary test covers it
   (`tests/KnowledgeAssistant.Api.IntegrationTests/TenantIsolationTests.cs`).

Summarise as a table: step, result, notes.
