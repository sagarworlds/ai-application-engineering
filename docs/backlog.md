# Backlog

Status: ✅ done · ⏭ next · 🗂 later

## Done (days 1–6)

- ✅ Repo, solution skeleton, requirements, architecture, plan
- ✅ Health, document metadata, question endpoints; DTOs, validation, ProblemDetails, OpenAPI
- ✅ Layered projects; JWT auth, RBAC policies, tenant + ACL metadata, tenant-boundary tests
- ✅ OpenAI-compatible chat client with timeout/cancellation; `/chat` and `/chat/stream`
- ✅ Grounded system instruction, structured output validation, safe unknown answer, bounded history
- ✅ Document-aware chunking, embeddings, SQLite dev vector store, ingestion endpoint
- ✅ Independent code + security review; fixes: linear-time heading parsing, section/chunk caps, per-user rate
  limit and body limits on model-backed endpoints, blank-role rejection, no redirects on the model client

## Next

- ⏭ **Retrieval quality**: hybrid (keyword + vector) search, re-ranking, query rewriting using conversation history
- ⏭ **Evaluation set**: golden questions with expected citations; track groundedness and unknown-answer precision
- ⏭ **Production search service**: Azure AI Search or pgvector adapter behind `IChunkRetriever`
- ⏭ **Background ingestion**: queue + worker, document status (`Pending/Ready/Failed`), retries
- ⏭ **PDF/DOCX extraction**: binary upload endpoint, text extraction (e.g. PdfPig / Azure Document Intelligence)
- ⏭ **Resilience**: retry with backoff + circuit breaker for 429/5xx from the model provider
- ⏭ **Quotas**: per-tenant chunk/storage quota, concurrency limit on streams, conversation retention policy
- ⏭ **Auth hardening**: fail startup outside Development when no authority/keys are configured; accept the
  `role` claim only in Development; tenant allow-list for multi-tenant Entra authorities
- ⏭ **Prompt hardening**: per-request random source delimiters; escape delimiter look-alikes in history turns

## Later

- 🗂 UI (React/Blazor) with streaming answers and citation previews
- 🗂 EF Core migrations instead of `EnsureCreated`
- 🗂 Tool calling (e.g. "look up document by title") and streaming grounded answers
- 🗂 Token-accurate budgeting (tokenizer) instead of the chars/4 estimate
- 🗂 OpenTelemetry traces/metrics; token usage per tenant
- 🗂 Dockerfile + compose (API + pgvector), CI workflow (build, test, vulnerability scan)
- 🗂 Audit log of questions/answers per tenant with retention policy
- 🗂 Content safety filtering and PII redaction on ingestion
- 🗂 Move tests from xunit 2 (marked legacy on NuGet) to xunit.v3
- 🗂 Document versioning (re-ingest replaces previous version atomically)
