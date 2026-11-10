---
name: smoke-test
description: Exercise the real user flows (ingest, list, ask, unknown answer, tenant isolation, streaming chat) against a running API process and the mock LLM provider.
---

1. Run `scripts/smoke-test.sh` from the repository root. It:
   - starts `tools/mock-llm/server.mjs` (OpenAI-compatible fake) on port 5099,
   - starts the API on port 5080 with a throwaway SQLite database and a throwaway signing key,
   - mints JWTs for two tenants and drives the endpoints with curl,
   - stops both processes and deletes the database on exit.
2. The script prints `PASS`/`FAIL` per check and exits non-zero on any failure.
3. On failure, read `$TMPDIR/eka-smoke/api.log` (path is printed) before changing code.

Do not point the smoke test at a real provider; it is meant to be free and deterministic.
