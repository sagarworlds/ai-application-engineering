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

## Day 4 — Calling a model from C#

- **Tokens** are the model's unit of text (roughly 4 characters of English). Cost, latency and limits are all
  measured in tokens: input (prompt) and output (completion). We cap output with `Generation:MaxOutputTokens`.
- **Context window**: the maximum tokens of prompt + reply per call. Everything the model "knows" about our
  documents must fit in it, which is why we retrieve a few relevant chunks instead of sending whole documents.
- **Roles**: `system` (our rules, highest priority), `user` (the person's input plus data we attach),
  `assistant` (earlier replies). See `ChatMessage`/`ChatRole`.
- **Temperature**: randomness of sampling. Chat default 0.7; grounded answers 0.1 because we want the sources
  restated, not creativity. Some reasoning models only accept the default — leave it unset for those.
- **Structured outputs**: `response_format: json_schema` constrains the reply to a schema
  (`StructuredOutputMode`). We still validate server-side; a schema guarantees shape, not truth.
- **Streaming**: the provider sends server-sent events (`data: {...}` … `data: [DONE]`). We read with
  `HttpCompletionOption.ResponseHeadersRead` and re-emit `delta`/`done`/`error` events from `/chat/stream`.
- **Tool calling**: the model returns a request to call a named function with JSON arguments; the app executes
  it and sends the result back. Not used yet (see backlog). Tool arguments are model output and must be
  validated and authorised exactly like user input.
- **Timeout vs. cancellation** (`OpenAiCompatibleChatModel`): a linked `CancellationTokenSource` cancels when
  the caller disconnects *or* the timeout elapses. If the caller's token did not fire, it was a timeout → 504.
  If it did, the `OperationCanceledException` propagates and nothing is written to the closed connection.
- **Secrets**: `Llm:ApiKey` comes from `dotnet user-secrets` locally and environment/Key Vault in deployment.
  The base URL must be https unless it is localhost (`LlmOptionsValidator`).
- **Provider**: any OpenAI-compatible endpoint. Azure OpenAI: `BaseUrl = https://<resource>.openai.azure.com/openai/v1/`,
  `ApiKeyHeaderName = api-key`, model = deployment name.

## Day 5 — Prompting and reliable outputs

- **System instruction** (`GroundedPromptBuilder.SystemInstruction`): answer only from the numbered sources,
  cite them, and set `answerFound: false` when evidence is insufficient. "Partial or merely related evidence
  counts as not enough" matters — without it models answer from the nearest-sounding passage.
- **Sources are data**: each chunk is wrapped in `<source id="n" …>` and our delimiter tags are neutralised in
  document text and in the question, so a document cannot close its block and inject instructions.
- **Structured output** (`GroundedAnswerParser`): the schema asks for `{answerFound, answer, citations}`.
  The server then checks what a schema cannot: citations must refer to sources that were actually sent.
  Invented citations are dropped; an answer with none left is downgraded to unknown; malformed output is
  retried once, then surfaces as 502.
- **Safe unknown** (`GroundedAnswer`): two shapes only. The unknown text is a server constant, never model
  text, and when retrieval returns nothing the model is not called at all.
- **Bounded history** (`BoundedHistory`, `ConversationOptions`): newest messages first, limited by count and by
  a character budget (≈ tokens × 4), whole messages only. History helps interpret a follow-up; it is not
  evidence — the system instruction says so, because earlier answers may rest on sources the user can no
  longer access.
- **Low temperature** (0.1) for grounded answers: restate, don't improvise.
