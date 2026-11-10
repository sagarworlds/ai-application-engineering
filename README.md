# Enterprise Knowledge Assistant

Employees ask questions about internal documents and get answers with citations (file, page,
section). When the documents don't contain the answer, the assistant says so instead of guessing.

```
UI ──JWT──▶ .NET Minimal API ──▶ retrieval (tenant + ACL filtered) ──▶ model ──▶ validated, cited answer
                    └─ ingestion ──▶ document-aware chunking ──▶ embeddings ──▶ vector store
```

| Document | What it covers |
|---|---|
| [Requirements](docs/01-requirements.md) | Use case, constraints, acceptance criteria |
| [Architecture](docs/02-architecture.md) | Projects, security model, database schema, API contracts |
| [Implementation plan](docs/03-implementation-plan.md) | Step-by-step plan and testing strategy |
| [Learning notes](docs/learning-notes.md) | Concepts behind each day and where they live in the code |
| [Backlog](docs/backlog.md) | Done / next / later |

## Prerequisites

| Tool | Used for | Check |
|---|---|---|
| .NET 10 SDK | build, test, run | `dotnet --version` |
| Node.js LTS | mock model provider, smoke test | `node -v` |
| Git | source control | `git --version` |
| Docker Desktop | later days (pgvector, containers) | `docker --version` |
| jq, curl, openssl | smoke test | – |

## Build and test

```bash
dotnet build
```

```bash
dotnet test
```

Tests run offline: the model and embedding ports are replaced by fakes, the store is a temporary SQLite file.

## Run locally (no API key needed)

1. Start the mock provider (an OpenAI-compatible fake; `appsettings.Development.json` already points at it):

   ```bash
   node tools/mock-llm/server.mjs
   ```

2. Start the API on <http://localhost:5080>:

   ```bash
   dotnet run --project src/KnowledgeAssistant.Api
   ```

3. Create a development token. `dotnet user-jwts` stores its signing key in user-secrets and registers the
   issuer in `appsettings.Development.json`:

   ```bash
   dotnet user-jwts create --project src/KnowledgeAssistant.Api --audience api://knowledge-assistant --claim tid=contoso --role Knowledge.Contributor
   ```

4. Ingest a document and ask about it (replace `$TOKEN`):

   ```bash
   curl -s http://localhost:5080/api/v1/documents -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"fileName":"travel-policy.pdf","pages":[{"pageNumber":1,"text":"# Booking\n\nFlights must be booked at least 14 days in advance."}]}'
   ```

   ```bash
   curl -s http://localhost:5080/api/v1/questions -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"question":"How many days in advance must flights be booked?"}'
   ```

The OpenAPI document is served at `/openapi/v1.json` in Development.

Or run every flow automatically (starts and stops everything itself):

```bash
scripts/smoke-test.sh
```

## Use a real model

Any OpenAI-compatible endpoint works. Keep the key out of source control:

```bash
dotnet user-secrets set "Llm:ApiKey" "<your key>" --project src/KnowledgeAssistant.Api
```

| Setting | Azure OpenAI | OpenAI |
|---|---|---|
| `Llm:BaseUrl` | `https://<resource>.openai.azure.com/openai/v1/` | `https://api.openai.com/v1/` |
| `Llm:ApiKeyHeaderName` | `api-key` | `Authorization` |
| `Llm:ChatModel` / `Llm:EmbeddingModel` | deployment names | model names |
| `Llm:MaxTokensParameterName` | `max_completion_tokens` for newer models | same |

Set them as environment variables (`Llm__BaseUrl`, …) or user-secrets. After changing the embedding model,
re-ingest documents: vectors from different models are not comparable and old chunks are ignored by search.
Tune `Retrieval:MinScore` for the model you use.

## Authentication in production

Point `Authentication:Schemes:Bearer:Authority` at Microsoft Entra ID
(`https://login.microsoftonline.com/<tenant-id>/v2.0`), keep `ValidAudiences` in sync with the app registration,
and define the app roles `Knowledge.Reader`, `Knowledge.Contributor`, `Knowledge.Admin`. The API reads the
tenant from `tid`, the user from `oid`/`sub` and roles from `roles`. With no authority and no signing key
configured, every request is rejected (401).

## Project layout

```
src/
  KnowledgeAssistant.Domain          entities and access rules
  KnowledgeAssistant.Application     use cases, ports, chunker, prompt builder, output validation
  KnowledgeAssistant.Infrastructure  EF Core/SQLite store, vector search, OpenAI-compatible clients
  KnowledgeAssistant.Api             endpoints, DTOs, auth, error handling, OpenAPI
tests/
  KnowledgeAssistant.UnitTests
  KnowledgeAssistant.Api.IntegrationTests
tools/mock-llm                       dependency-free fake provider
scripts/smoke-test.sh                end-to-end user flows
.claude/                             project instructions' companions: skills (verify, add-endpoint, smoke-test) and guard hooks
```
