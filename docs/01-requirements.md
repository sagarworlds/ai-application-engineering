# 1. Requirements, constraints and acceptance criteria

## Product

**Enterprise Knowledge Assistant (EKA)** — employees ask natural-language questions about
internal documents and receive answers that cite the exact document, page and section the
answer came from. When the documents do not contain the answer, the assistant says so instead
of guessing.

### Actors

| Actor | Role claim | Can do |
|---|---|---|
| Reader | `Knowledge.Reader` | List/read document metadata they may access, chat, ask questions |
| Contributor | `Knowledge.Contributor` | Everything a Reader can, plus ingest and delete documents |
| Admin | `Knowledge.Admin` | Everything, including documents restricted to other roles in the tenant |

Every actor belongs to exactly one **tenant** (the `tid` claim of their token).

## Functional requirements

| ID | Requirement | Day |
|---|---|---|
| FR-1 | Liveness and readiness health endpoints | 2 |
| FR-2 | List and fetch document metadata (paged) | 2 |
| FR-3 | Ask a question, receive an answer with citations (file, page, section, snippet) | 2, 5 |
| FR-4 | Every request is authenticated with a JWT; authorization uses role-based policies | 3 |
| FR-5 | Documents carry tenant, uploader and optional role restrictions (ACL) | 3 |
| FR-6 | A user never sees, retrieves or is answered from another tenant's documents | 3 |
| FR-7 | Free-form chat endpoint (non-streaming and streaming) with timeout and cancellation | 4 |
| FR-8 | Answers are grounded only in retrieved sources; insufficient evidence yields an explicit "unknown" answer | 5 |
| FR-9 | Model output is structured JSON, validated server-side before it reaches the client | 5 |
| FR-10 | Conversations keep a bounded history (message count and character budget) | 5 |
| FR-11 | Ingest extracted PDF/text content (pages of text) into chunks with filename, page, section and tenant metadata | 6 |
| FR-12 | Generate embeddings per chunk and persist them in a development vector store | 6 |

## Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-1 | **Security**: permissions are enforced in API/data-access code, never delegated to the prompt. Tenant id comes from the token only, never from the request body. |
| NFR-2 | **Secrets**: no secrets in source control. API keys come from user-secrets / environment variables / a vault. |
| NFR-3 | **Reliability**: every outbound model call has a timeout; client disconnects cancel in-flight work. |
| NFR-4 | **Consistency**: all errors are RFC 9457 `application/problem+json` with a `traceId`. |
| NFR-5 | **Observability**: structured logs with tenant/user scope; document text and prompts are never logged. |
| NFR-6 | **Testability**: model, embedding and store are behind interfaces; the test suite runs offline with no API key. |
| NFR-7 | **Replaceability**: the dev vector store and LLM provider can be swapped (Azure AI Search, pgvector, Azure OpenAI) without touching Application code. |

## Constraints

- .NET 10, ASP.NET Core Minimal APIs, C#.
- LLM access through an OpenAI-compatible HTTP API (Azure OpenAI v1 endpoint, OpenAI, Ollama, …) — no provider SDK lock-in.
- Development store is SQLite with brute-force cosine similarity. It is intentionally not a production search service.
- Ingestion receives **already extracted** text (pages). PDF binary parsing is out of scope until a later day.
- Ingestion is synchronous in the request for now (bounded by page/character limits).
- No UI yet; the API is the product surface for days 1–6.

## Acceptance criteria

1. `dotnet build` succeeds with warnings-as-errors; `dotnet test` passes offline.
2. `GET /health/live` and `GET /health/ready` return 200 without a token.
3. Any `/api/v1/*` call without a valid token returns 401; with a token lacking the needed role, 403.
4. A token without a `tid` claim is rejected with 403.
5. Invalid request bodies return 400 `ValidationProblemDetails` listing each invalid field.
6. A Contributor ingests a multi-page document; the response reports the chunk count, and each
   stored chunk has tenant, filename, page and section metadata plus an embedding.
7. Tenant B cannot list, fetch (404, not 403 — existence is not leaked), delete or get answers from tenant A's documents.
8. A document restricted to a role is invisible to tenant members without that role — in listings **and** in retrieval.
9. `POST /api/v1/questions` returns `answerFound: true` with ≥ 1 citation that maps to a retrieved chunk,
   or `answerFound: false` with the fixed unknown-answer text and no citations. There is no third shape.
10. When retrieval finds nothing, the model is **not called** and the unknown answer is returned.
11. Citations the model invents (source numbers that were not supplied) are dropped; an answer left with no valid citation is downgraded to unknown.
12. A model call exceeding the configured timeout returns 504; a client disconnect cancels the upstream call.
13. Conversation history sent to the model never exceeds the configured message and character limits.
14. `GET /openapi/v1.json` describes all endpoints (Development environment).
15. Model-backed endpoints (chat, questions, ingestion) are limited per user per minute (429) and reject oversized bodies (413).
16. `dotnet list package --vulnerable --include-transitive` reports no known vulnerabilities; no secret is committed.
