#!/usr/bin/env bash
# Shared helpers for scripts that run the real API against the mock LLM provider.
# Source this file; the caller must set ROOT (repository root) and WORK (scratch directory).
#
# Provides: SIGNING_KEY, ISSUER, AUDIENCE, PIDS, cleanup, token, start_mock, start_api.

ISSUER="eka-local"; AUDIENCE="api://knowledge-assistant"
PIDS=()

cleanup() {
  for pid in "${PIDS[@]}"; do kill "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true
  rm -f "$WORK"/*.db*
}
trap cleanup EXIT

rm -rf "$WORK"; mkdir -p "$WORK"

# Throwaway signing key: exists only for this run and is never written to the repository.
SIGNING_KEY="$(openssl rand -base64 32)"

# Mints an HS256 JWT:  token <tenant> <user> <role>...
token() {
  node -e '
    const { createHmac } = require("node:crypto");
    const [key, iss, aud, tid, sub, ...roles] = process.argv.slice(1);
    const b64 = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
    const now = Math.floor(Date.now() / 1000);
    const body = `${b64({ alg: "HS256", typ: "JWT" })}.${b64({ iss, aud, tid, sub, roles, nbf: now - 60, iat: now, exp: now + 900 })}`;
    console.log(`${body}.${createHmac("sha256", Buffer.from(key, "base64")).update(body).digest("base64url")}`);
  ' "$SIGNING_KEY" "$ISSUER" "$AUDIENCE" "$@"
}

start_mock() { # start_mock <port> <log-name> [extra env assignments...]
  local port="$1" log="$2"; shift 2
  env "$@" node "$ROOT/tools/mock-llm/server.mjs" "$port" >"$WORK/$log.log" 2>&1 &
  PIDS+=($!)
}

start_api() { # start_api <port> <mock-port> <log-name> [extra env assignments...]
  local port="$1" mock_port="$2" log="$3"; shift 3
  env "$@" \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://127.0.0.1:$port" \
    ConnectionStrings__KnowledgeDb="Data Source=$WORK/$log.db" \
    Authentication__Schemes__Bearer__ValidIssuer="$ISSUER" \
    Authentication__Schemes__Bearer__ValidAudiences__0="$AUDIENCE" \
    Authentication__Schemes__Bearer__SigningKeys__0__Issuer="$ISSUER" \
    Authentication__Schemes__Bearer__SigningKeys__0__Value="$SIGNING_KEY" \
    Llm__BaseUrl="http://127.0.0.1:$mock_port/v1/" Llm__ChatModel=mock-chat Llm__EmbeddingModel=mock-embedding \
    dotnet run --no-build --no-launch-profile --project "$ROOT/src/KnowledgeAssistant.Api" >"$WORK/$log.log" 2>&1 &
  PIDS+=($!)
  for _ in $(seq 1 60); do
    curl -sf "http://127.0.0.1:$port/health/ready" >/dev/null 2>&1 && return 0
    sleep 0.5
  done
  echo "API on port $port did not become ready; see $WORK/$log.log"; exit 1
}
