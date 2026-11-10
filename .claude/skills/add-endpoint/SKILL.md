---
name: add-endpoint
description: Checklist for adding a new Minimal API endpoint that follows this repo's layering, validation, security and testing conventions.
---

Follow in order; each step should compile before moving on.

1. **Contract first** — add the row to the API table in `docs/02-architecture.md` (path, policy, request, success, errors).
2. **DTOs** — `src/KnowledgeAssistant.Api/Contracts/`. Records with DataAnnotations (`[Required]`, `[StringLength]`, `[Range]`).
   Never expose Domain entities. Never accept a tenant id from the client.
3. **Use case** — a focused service in `src/KnowledgeAssistant.Application/` taking `UserContext` and a `CancellationToken`.
   Need new I/O? Add a port in `Abstractions/` and implement it in Infrastructure.
4. **Endpoint** — in the matching `Endpoints/*Endpoints.cs` group:
   - `.RequireAuthorization(AuthorizationPolicies.X)` (groups are deny-by-default)
   - `TypedResults` with an explicit `Results<...>` return type
   - `.WithName`, `.WithSummary`, `.ProducesProblem(...)` for each error status
   - accept `CancellationToken` and pass it down
5. **Errors** — throw exceptions from `Application/Exceptions`; add a mapping in `GlobalExceptionHandler` only for a new type.
6. **Tests** — integration tests for: happy path, 400 validation, 401 no token, 403 wrong role, and a
   cross-tenant test if any data is read or written.
7. Run the `verify` skill.
