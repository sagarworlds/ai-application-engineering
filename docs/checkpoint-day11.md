# Day 11 checkpoint — is RAG working end to end?

**Verdict: the pipeline works end to end, but only against the mock model provider.** Every stage runs
for real except the two model calls (chat and embeddings), which have never been made against a real
provider. Closing that gap, and adding a way to measure answer quality, should come before agents.

## What "end to end" covers today

| Stage | Works | How it is verified |
|---|---|---|
| Authenticate caller, resolve tenant and roles from the JWT | yes | integration tests (forged, expired, wrong audience, no tenant, wrong role) |
| Ingest extracted pages | yes | unit + integration tests, smoke test |
| Chunk by page → heading → paragraph → sentence, with overlap | yes | chunker invariants (size, no lost text, no cross-page overlap) |
| Embed chunks and store them with tenant, page, section, model | yes, with fake embeddings | ingestion tests; real HTTP client tested against a stub handler |
| Retrieve only chunks the caller may read | yes | tenant-isolation and role-restriction tests, smoke test |
| Build grounded prompt, call model, validate structured output | yes, with a fake model | question-service and parser tests |
| Return cited answer, or the fixed unknown answer | yes | integration tests, smoke test, demo |
| Timeouts, cancellation, provider errors → 503 / 504 / 502 | yes | client tests, smoke test with a slow provider |

Evidence from this checkpoint:

- `dotnet test`: 211 tests pass (144 unit, 67 integration).
- `scripts/smoke-test.sh`: all checks pass against the real API process over HTTP.
- `scripts/demo.sh`: recorded in [demo/demo.mp4](demo/demo.mp4); stills in [images/](images/).
- No vulnerable packages reported; no secrets in tracked files.

Not covered by any of the above: a real chat model, a real embedding model, real PDF files, more than a
handful of documents, and a user interface.

## Top five remaining gaps

Ranked by how much each one limits trust in the answers.

### 1. Never run against a real model or embedding provider

Everything end to end uses `tools/mock-llm`. Unverified as a result: whether the provider accepts the
request shape (`response_format`, token-limit parameter, key header), whether a real model follows the
grounding instruction and the JSON schema, real latency against the 30-second timeout, and cost.
`Retrieval:MinScore` (0.25) was chosen for the fake embeddings; real models have different similarity
ranges, so the threshold that decides "answer or unknown" is currently a guess.

*Next step:* configure one provider, run `scripts/smoke-test.sh` and `scripts/demo.sh` against it, and
record the settings that worked.

### 2. No evaluation set, so quality is unmeasured

Tests prove the mechanics (a citation maps to a retrieved chunk), not that answers are right. There is no
set of questions with expected answers and sources, so there is no number for retrieval hit rate,
groundedness, or how often the assistant wrongly says "unknown" or wrongly answers. Chunk size, overlap,
`TopK` and `MinScore` cannot be tuned without it.

*Next step:* 30–50 questions over a small real document set, including unanswerable ones; a script that
reports hit rate and unknown-answer precision.

### 3. Retrieval is vector-only and ignores the conversation

Search is cosine similarity over embeddings, nothing else. Exact terms such as product codes, names and
clause numbers are matched poorly by embeddings alone; there is no keyword or hybrid search and no
re-ranking. Follow-ups are retrieved using only the latest message, so "and for trains?" searches for
"and for trains?" — history reaches the model but not the retriever. The store is also a full scan of the
tenant's chunks per question.

*Next step:* rewrite follow-ups into standalone queries; add keyword search and merge the results; move to
a real index (Azure AI Search or pgvector) behind `IChunkRetriever`.

### 4. Ingestion needs pre-extracted text and runs inside the request

The API accepts page text, not files. There is no PDF or DOCX parsing, so tables, multi-column layouts,
scanned pages and repeated headers or footers are untested. Plain-text heading detection is a heuristic.
Ingestion is synchronous: a large document holds the request open for every embedding batch, and a
failure means starting again. There is no document status, re-ingestion or versioning.

*Next step:* file upload with text extraction; a background queue with `Pending / Ready / Failed` status.

### 5. Not operable beyond a developer machine

SQLite with `EnsureCreated` (no migrations), no retry or back-off when the provider returns 429 or 5xx,
no traces or metrics (token usage, latency, unknown-answer rate per tenant), no CI pipeline, no container
image, and no UI — the API is driven with curl. Remaining hardening items from the security review
(per-tenant quotas, startup failure when auth is unconfigured, conversation retention) are in the backlog.

*Next step:* CI (build, test, vulnerability scan), provider retries, OpenTelemetry, then a minimal UI.

## Before moving to agents

Gaps 1 and 2 are the gate: an agent that calls this retrieval as a tool inherits whatever quality it has,
and today that quality is unmeasured against a real model. Gaps 3–5 can proceed in parallel with agent work.
