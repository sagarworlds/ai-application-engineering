#!/usr/bin/env bash
# End-to-end smoke test: runs the real API process against the mock LLM provider and drives
# the main user flows over HTTP. Free, offline and deterministic.
#
# Usage: scripts/smoke-test.sh
# Requires: dotnet 10 SDK, node, curl, jq, openssl.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="${TMPDIR:-/tmp}/eka-smoke"
API_PORT=5080; SLOW_API_PORT=5081; MOCK_PORT=5099; SLOW_MOCK_PORT=5098
API="http://127.0.0.1:$API_PORT"
FAILED=0

# shellcheck source=lib/local-stack.sh
source "$ROOT/scripts/lib/local-stack.sh"

check() { # check <description> <expected> <actual>
  if [[ "$2" == "$3" ]]; then echo "PASS  $1"; else echo "FAIL  $1 (expected '$2', got '$3')"; FAILED=1; fi
}

# request <token|-> <method> <path> [json-body]  -> writes body to $WORK/body, prints status code
request() {
  local auth=(); [[ "$1" != "-" ]] && auth=(-H "Authorization: Bearer $1")
  local data=(); [[ $# -ge 4 ]] && data=(-H 'Content-Type: application/json' -d "$4")
  curl -sS -o "$WORK/body" -w '%{http_code}' -X "$2" "${auth[@]}" "${data[@]}" "${BASE:-$API}$3"
}

echo "Logs: $WORK/api.log"
dotnet build "$ROOT" --nologo -v quiet >/dev/null

start_mock "$MOCK_PORT" mock
start_api "$API_PORT" "$MOCK_PORT" api

CONTRIBUTOR="$(token contoso alice Knowledge.Contributor)"
READER="$(token contoso bob Knowledge.Reader)"
OTHER_TENANT_ADMIN="$(token fabrikam mallory Knowledge.Admin)"
QUESTION='{"question":"How many days in advance must flights be booked?"}'
DOCUMENT='{"fileName":"travel-policy.pdf","title":"Travel Policy 2026","pages":[
  {"pageNumber":1,"text":"# Scope\n\nThis travel policy applies to all employees of Contoso."},
  {"pageNumber":2,"text":"# Booking\n\nFlights must be booked at least 14 days in advance.\n\n# Meals\n\nThe daily meal allowance is 45 euros."}]}'

echo "--- health and authentication"
check "liveness is anonymous"                     200 "$(request - GET /health/live)"
check "readiness is anonymous"                    200 "$(request - GET /health/ready)"
check "documents without token -> 401"            401 "$(request - GET /api/v1/documents)"
check "forged token -> 401"                       401 "$(request "${READER}x" GET /api/v1/documents)"
check "reader cannot ingest -> 403"               403 "$(request "$READER" POST /api/v1/documents "$DOCUMENT")"

echo "--- ingestion"
check "contributor ingests document -> 201"       201 "$(request "$CONTRIBUTOR" POST /api/v1/documents "$DOCUMENT")"
DOCUMENT_ID="$(jq -r .id "$WORK/body")"
check "document was chunked (3 sections)"         3   "$(jq -r .chunkCount "$WORK/body")"
check "same content again -> 409"                 409 "$(request "$CONTRIBUTOR" POST /api/v1/documents "$DOCUMENT")"
check "invalid body -> 400"                       400 "$(request "$CONTRIBUTOR" POST /api/v1/documents '{"fileName":"x.pdf","pages":[]}')"
check "error is problem+json with traceId"        true "$(jq -r 'has("traceId") and has("errors")' "$WORK/body")"
check "reader lists the document"                 200 "$(request "$READER" GET '/api/v1/documents?pageSize=50')"
check "listing contains the new document"         true "$(jq --arg id "$DOCUMENT_ID" '[.items[].id] | index($id) != null' "$WORK/body")"

echo "--- cited answers"
check "reader asks a covered question -> 200"     200 "$(request "$READER" POST /api/v1/questions "$QUESTION")"
check "answer was found"                          true "$(jq -r .answerFound "$WORK/body")"
check "citation names the file"                   travel-policy.pdf "$(jq -r '.citations[0].fileName' "$WORK/body")"
check "citation names the page"                   2   "$(jq -r '.citations[0].pageNumber' "$WORK/body")"
check "citation names the section"                Booking "$(jq -r '.citations[0].section' "$WORK/body")"
CONVERSATION_ID="$(jq -r .conversationId "$WORK/body")"
check "follow-up keeps the conversation"          "$CONVERSATION_ID" "$(request "$READER" POST /api/v1/questions \
  "{\"question\":\"What is the daily meal allowance?\",\"conversationId\":\"$CONVERSATION_ID\"}" >/dev/null; jq -r .conversationId "$WORK/body")"
check "follow-up cites the Meals section"         Meals "$(jq -r '.citations[0].section' "$WORK/body")"

echo "--- unknown answers"
check "uncovered question -> 200"                 200 "$(request "$READER" POST /api/v1/questions '{"question":"Who won the football world cup in 1998?"}')"
check "answerFound is false"                      false "$(jq -r .answerFound "$WORK/body")"
check "no citations for unknown answer"           0   "$(jq -r '.citations | length' "$WORK/body")"

echo "--- tenant isolation"
check "other tenant cannot fetch the document"    404 "$(request "$OTHER_TENANT_ADMIN" GET "/api/v1/documents/$DOCUMENT_ID")"
check "other tenant cannot delete the document"   404 "$(request "$OTHER_TENANT_ADMIN" DELETE "/api/v1/documents/$DOCUMENT_ID")"
check "other tenant lists nothing"                0   "$(request "$OTHER_TENANT_ADMIN" GET /api/v1/documents >/dev/null; jq -r .totalCount "$WORK/body")"
check "other tenant asks same question -> 200"    200 "$(request "$OTHER_TENANT_ADMIN" POST /api/v1/questions "$QUESTION")"
check "other tenant gets no answer"               false "$(jq -r .answerFound "$WORK/body")"

echo "--- chat"
check "chat -> 200"                               200 "$(request "$READER" POST /api/v1/chat '{"message":"What is a context window?"}')"
check "chat returns a reply"                      true "$(jq -r '.reply | length > 0' "$WORK/body")"
check "chat stream -> 200"                        200 "$(request "$READER" POST /api/v1/chat/stream '{"message":"Say hello"}')"
check "stream has delta events"                   true "$(grep -q '^event: delta' "$WORK/body" && echo true || echo false)"
check "stream ends with done event"               true "$(grep -q '^event: done' "$WORK/body" && echo true || echo false)"

echo "--- limits (enforced by the real server, so checked here rather than in the in-process tests)"
node -e 'process.stdout.write(JSON.stringify({ question: "x".repeat(200000) }))' >"$WORK/big.json"
check "oversized question body -> 413"            413 "$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $READER" \
  -H 'Content-Type: application/json' --data-binary "@$WORK/big.json" "$API/api/v1/questions")"
check "blank allowedRoles entry -> 400"           400 "$(request "$CONTRIBUTOR" POST /api/v1/documents \
  '{"fileName":"r.pdf","allowedRoles":[" "],"pages":[{"pageNumber":1,"text":"restricted text"}]}')"

echo "--- deletion"
check "contributor deletes the document -> 204"   204 "$(request "$CONTRIBUTOR" DELETE "/api/v1/documents/$DOCUMENT_ID")"
check "deleted document is no longer evidence"    false "$(request "$READER" POST /api/v1/questions "$QUESTION" >/dev/null; jq -r .answerFound "$WORK/body")"

echo "--- provider timeout (second API instance, 1 s timeout, provider answers after 3 s)"
start_mock "$SLOW_MOCK_PORT" slow-mock MOCK_LLM_DELAY_MS=3000
start_api "$SLOW_API_PORT" "$SLOW_MOCK_PORT" slow-api Llm__RequestTimeout=00:00:01 RateLimiting__ModelRequestsPerMinute=2
BASE="http://127.0.0.1:$SLOW_API_PORT"
check "slow provider -> 504"                      504 "$(request "$READER" POST /api/v1/chat '{"message":"hello"}')"
check "timeout is a problem response"             "Language model timed out" "$(jq -r .title "$WORK/body")"

check "second call within budget -> 504"          504 "$(request "$READER" POST /api/v1/chat '{"message":"hello"}')"
check "third call exceeds per-user limit -> 429"  429 "$(request "$READER" POST /api/v1/chat '{"message":"hello"}')"
check "another user is not affected"              504 "$(request "$CONTRIBUTOR" POST /api/v1/chat '{"message":"hello"}')"

echo "--- logs"
check "no document text or question in API logs"  0 "$(grep -cE '14 days in advance|football world cup' "$WORK/api.log" || true)"

if [[ "$FAILED" -ne 0 ]]; then echo "SMOKE TEST FAILED"; exit 1; fi
echo "SMOKE TEST PASSED"
