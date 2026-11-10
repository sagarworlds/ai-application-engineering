# Enterprise Knowledge Assistant

Users ask questions about internal documents and get cited answers. .NET 10 Minimal API,
layered (Domain → Application → Infrastructure → Api). Read `docs/02-architecture.md` before
changing structure, schema or API contracts.

## Commands

```bash
dotnet build                      # warnings are errors
dotnet test                       # offline; no API key needed
dotnet format --verify-no-changes # style check
dotnet list package --vulnerable --include-transitive
node tools/mock-llm/server.mjs    # OpenAI-compatible fake provider on :5099
dotnet run --project src/KnowledgeAssistant.Api
scripts/smoke-test.sh             # real user flows against a running API + mock provider
```

## Architecture rules

- Dependencies point inward. Application defines ports (`Abstractions/`); Infrastructure implements them.
  Endpoints never use `DbContext` or `HttpClient` directly.
- One reason to change per class. New behaviour = new class behind a port, not another branch in an existing one.
- All I/O is async and takes a `CancellationToken` as the last parameter. Pass it all the way down.
- Outbound HTTP only through `IHttpClientFactory` typed clients.
- DTOs live in `Api/Contracts`; never return Domain entities from endpoints.

## Security rules (non-negotiable)

- Tenant id comes from the token (`ICurrentUser`), never from a request body, route or query.
- Every repository/retriever method takes the tenant (or `UserContext`) and filters in the query.
- Permissions are enforced in code. Never rely on prompt text to hide or protect data.
- "Not accessible" is reported as 404, not 403, so existence does not leak across tenants.
- No secrets in the repo. Use `dotnet user-secrets` or environment variables (`Llm__ApiKey`).
- Never log document text, prompts, model output or tokens. Log ids, counts and durations.

## Error handling

- Throw the specific exceptions in `Application/Exceptions`; `GlobalExceptionHandler` maps them to ProblemDetails.
- No empty `catch`. Catch specific exception types, log with context, rethrow or translate.
- `OperationCanceledException` from the caller's token is not an error — let it propagate.

## Code style

- File-scoped namespaces, `sealed` by default, records for DTOs/value objects.
- XML doc comments on public types/members; comments explain *why*, not *what*.
- Logging via source-generated `[LoggerMessage]` methods.

## Tests

- Unit tests for pure logic and adapters; integration tests through `WebApplicationFactory` with real auth.
- Any change touching data access needs a tenant-boundary test.
- Test names: `Method_Scenario_Expectation`.

## Git

- Branch before changing code; branch names start with `sp/` (e.g. `sp/feature-add-user-auth`).
- Small commits, imperative subject line. No `Co-Authored-By` trailers.
