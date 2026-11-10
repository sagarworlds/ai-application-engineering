# 3. Implementation plan

Each step is small, compiles on its own and has its own tests. One commit per step.

| # | Step | Verifiable by |
|---|---|---|
| **Day 1** | | |
| 1.1 | Toolchain check (.NET 10 SDK, Git, Node LTS, Docker) | `dotnet --version`, `node -v`, `git --version`, `docker --version` |
| 1.2 | Solution skeleton: 4 src projects + 2 test projects, central package management, warnings-as-errors | `dotnet build` |
| 1.3 | Requirements, architecture, plan, backlog; Claude project instructions, skills, hooks | docs reviewed |
| **Day 2** | | |
| 2.1 | Domain `Document`; Application `DocumentQueryService` + `IDocumentRepository` port; EF Core SQLite adapter | unit tests |
| 2.2 | Health endpoints (live/ready with DB check), OpenAPI document | integration tests |
| 2.3 | Document metadata endpoints with DTOs, validation, paging | integration tests |
| 2.4 | Global exception handler → ProblemDetails; structured logging | integration tests (400/404 shapes) |
| 2.5 | Question endpoint contract (wired to the QA service port) | integration tests |
| **Day 3** | | |
| 3.1 | JWT bearer authentication; claims → `UserContext` | 401 tests |
| 3.2 | RBAC policies + tenant-claim requirement | 403 tests |
| 3.3 | Tenant + uploader + ACL metadata on documents; filters in repository | tenant-boundary tests |
| **Day 4** | | |
| 4.1 | `IChatModel` port; OpenAI-compatible client via `IHttpClientFactory`; options + secrets | unit tests with stub handler |
| 4.2 | Timeout vs. caller cancellation handling; error mapping (503/504) | unit tests |
| 4.3 | `/chat` and `/chat/stream` (SSE) endpoints | integration tests + mock provider |
| **Day 5** | | |
| 5.1 | Grounded system instruction + prompt builder (sources as delimited data) | unit tests |
| 5.2 | Structured JSON output + `GroundedAnswerParser` validation, one retry, safe unknown | unit tests |
| 5.3 | Bounded conversation history (`IConversationStore`) | unit tests |
| 5.4 | `QuestionAnsweringService` end to end | unit + integration tests |
| **Day 6** | | |
| 6.1 | Document-aware chunker (pages → sections → paragraphs → sentences; size/overlap) | unit tests incl. invariants |
| 6.2 | `IEmbeddingGenerator` port + OpenAI-compatible client (batched) | unit tests |
| 6.3 | `DocumentIngestionService`; chunks + embeddings persisted; `IChunkRetriever` cosine search with tenant/ACL filter | unit + integration tests |
| 6.4 | Ingestion and delete endpoints | integration tests |
| **Verification** | | |
| V.1 | Independent code review and security review (subagents) | findings triaged and fixed |
| V.2 | Vulnerable-package scan, secret scan | clean output |
| V.3 | Real user flows against a running API + mock provider (`scripts/smoke-test.sh`) | script exits 0 |

## Testing strategy

- **Unit tests** (`tests/KnowledgeAssistant.UnitTests`): pure logic (chunker, parser, prompt builder, access rules)
  and adapters against stub `HttpMessageHandler`s / a temp SQLite file.
- **Integration tests** (`tests/KnowledgeAssistant.Api.IntegrationTests`): `WebApplicationFactory` with real
  auth pipeline (test-signed JWTs), real SQLite, fake chat/embedding ports.
- **Smoke test**: the real API process talking HTTP to `tools/mock-llm` (an OpenAI-compatible fake), driven by curl.
