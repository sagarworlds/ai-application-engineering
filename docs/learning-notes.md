# Learning notes

Short notes on the concepts behind each day, with pointers to where they show up in the code.

## Day 3 — Identity, authorization and tenancy

- **OAuth 2.0 / OpenID Connect**: the UI signs the user in with the identity provider and receives an
  *access token* for this API (audience `api://knowledge-assistant`). The API never sees passwords; it only
  validates tokens.
- **JWT validation** (`AddJwtBearer`): signature (keys from the authority's metadata endpoint), issuer,
  audience and lifetime. A token that fails any check is a 401. Tests cover forged key, expiry and wrong audience.
- **Microsoft Entra ID**: set `Authentication:Schemes:Bearer:Authority` to
  `https://login.microsoftonline.com/<tenant-id>/v2.0` (or `/organizations/v2.0` + issuer validation for
  multi-tenant apps). Define *app roles* (`Knowledge.Reader`, …) on the app registration; they arrive in the
  `roles` claim. The directory tenant arrives in `tid`, the user in `oid`.
- **Authentication vs. authorization**: 401 = "we don't know who you are"; 403 = "we know, and you may not".
- **RBAC via policies** (`AuthorizationPolicies`): endpoints name a policy (`Documents.Write`), the policy names
  roles. Changing who may do what is a one-line change in one file.
- **Tenant boundary** is data-level authorization, not a role: every query filters on the token's tenant
  (`EfDocumentRepository`, `DocumentAccessQueryExtensions`). Cross-tenant access returns 404 so ids cannot be probed.
- **Why not in the prompt**: a model can be talked out of instructions (prompt injection) and cannot be audited.
  If a chunk reaches the prompt, assume the user can read it — so only authorised chunks may reach the prompt.
