#!/usr/bin/env bash
# Guided demo of the RAG flow: ingestion, cited answers, an unanswerable question and the tenant
# boundary. Every response shown is a live response from the real API process. The model behind
# it is tools/mock-llm (a deterministic offline stand-in), unless you point Llm__* at a real one.
#
# Usage: scripts/demo.sh            paced for recording or presenting (about 4 minutes)
#        DEMO_SPEED=0 scripts/demo.sh   no pauses (quick check)
# Requires: dotnet 10 SDK, node, curl, jq, openssl.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="${TMPDIR:-/tmp}/eka-demo"
API_PORT="${DEMO_API_PORT:-5090}"; MOCK_PORT="${DEMO_MOCK_PORT:-5097}"
API="http://127.0.0.1:$API_PORT"
SPEED="${DEMO_SPEED:-1}"
SAMPLES="$ROOT/docs/demo/sample-documents"

# shellcheck source=lib/local-stack.sh
source "$ROOT/scripts/lib/local-stack.sh"

BOLD=$'\e[1m'; DIM=$'\e[2m'; CYAN=$'\e[36m'; GREEN=$'\e[32m'; YELLOW=$'\e[33m'; RESET=$'\e[0m'

pause() { # pause <seconds>, scaled by DEMO_SPEED
  [[ "$SPEED" == "0" ]] || sleep "$(awk -v s="$1" -v k="$SPEED" 'BEGIN { print s * k }')"
}

scene() { # scene <number> <title>: clears the screen so each scene stands on its own
  printf '\e[2J\e[H%s━━ %s. %s ━━%s\n\n' "$BOLD$CYAN" "$1" "$2" "$RESET"
  pause 1.5
}

say() { # narration; reading time grows with the length of the text
  printf '%s%s%s\n' "$CYAN" "$1" "$RESET"
  pause "$(awk -v n="${#1}" 'BEGIN { t = n / 22; print (t < 2 ? 2 : t) }')"
}

# call <label> <token> <method> <path> [body-file-or-json]
# Prints the request the way you would type it, sends it, then prints status and JSON response.
call() {
  local label="$1" token="$2" method="$3" path="$4" body="${5:-}"
  local args=(-sS -o "$WORK/response" -w '%{http_code}' -X "$method" -H "Authorization: Bearer $token")
  local shown="curl -X $method \$API$path -H \"Authorization: Bearer \$$label\""
  if [[ -n "$body" ]]; then
    args+=(-H 'Content-Type: application/json')
    if [[ -f "$body" ]]; then
      args+=(--data-binary "@$body"); shown+=" -d @${body#"$ROOT"/}"
    else
      args+=(-d "$body"); shown+=" \\"$'\n'"     -d '$body'"
    fi
  fi

  printf '%s$ %s%s\n' "$BOLD" "$shown" "$RESET"
  pause 1.5
  STATUS="$(curl "${args[@]}" "$API$path")"
  local colour="$GREEN"; [[ "$STATUS" -ge 400 ]] && colour="$YELLOW"
  printf '%sHTTP %s%s\n' "$colour" "$STATUS" "$RESET"
  if [[ -s "$WORK/response" ]]; then jq -C "${JQ_FILTER:-.}" "$WORK/response"; fi
  echo
}

printf '\e[2J\e[H%sStarting the API and the mock model provider...%s\n' "$DIM" "$RESET"
dotnet build "$ROOT" --nologo -v quiet >/dev/null
start_mock "$MOCK_PORT" mock
start_api "$API_PORT" "$MOCK_PORT" api

ALICE="$(token contoso alice Knowledge.Contributor)"   # may ingest documents
BOB="$(token contoso bob Knowledge.Reader)"            # may only read and ask
MALLORY="$(token fabrikam mallory Knowledge.Admin)"    # admin of a DIFFERENT tenant

scene 1 "Enterprise Knowledge Assistant"
say "Employees ask questions about internal documents and get answers with citations."
say "If the documents do not contain the answer, the assistant says so instead of guessing."
echo
say "This demo runs the real .NET 10 API. The model is a local stand-in (tools/mock-llm),"
say "so it is free, offline and repeatable. Three users, identified by their JWT:"
printf '  %salice%s    tenant contoso   Knowledge.Contributor   (can ingest)\n' "$BOLD" "$RESET"
printf '  %sbob%s      tenant contoso   Knowledge.Reader        (can ask)\n' "$BOLD" "$RESET"
printf '  %smallory%s  tenant fabrikam  Knowledge.Admin         (another company)\n\n' "$BOLD" "$RESET"
pause 5

scene 2 "Ingestion: extracted pages in, searchable chunks out"
say "Alice uploads the extracted text of a 3-page PDF. Headings mark the sections:"
jq -C '.pages[] | {pageNumber, text: (.text[0:74] + "…")}' "$SAMPLES/travel-policy.json"
echo
pause 5
say "The API splits it by page and heading, embeds every chunk and stores it for her tenant."
call ALICE "$ALICE" POST /api/v1/documents "$SAMPLES/travel-policy.json"
pause 3
say "3 pages became $(jq -r .chunkCount "$WORK/response") chunks: one per section, each keeping its page number and heading."
pause 3

scene 3 "A second document, and a reader who may not ingest"
say "A plain-text file without Markdown works too. Its headings are detected heuristically."
JQ_FILTER='{id, fileName, title, pageCount, chunkCount}' \
  call ALICE "$ALICE" POST /api/v1/documents "$SAMPLES/remote-work-guidelines.json"
pause 3
say "Bob is only a Reader. The same request with his token is refused by the API:"
JQ_FILTER='{title, status}' call BOB "$BOB" POST /api/v1/documents "$SAMPLES/remote-work-guidelines.json"
pause 4

scene 4 "A question the documents answer: cited answer"
say "Bob asks a question. The API retrieves the best matching chunks he is allowed to read,"
say "sends only those to the model, and validates the model's JSON before answering."
call BOB "$BOB" POST /api/v1/questions '{"question":"How many days in advance must flights be booked?"}'
CONVERSATION="$(jq -r .conversationId "$WORK/response")"
pause 4
say "answerFound is true, and the citation names the file, page 2 and the Booking section."
pause 5

scene 5 "Follow-up in the same conversation, answered from the other document"
say "The conversationId carries bounded history. This answer comes from the second file:"
JQ_FILTER='{answerFound, answer, citations: [.citations[] | {fileName, pageNumber, section, snippet}]}' \
  call BOB "$BOB" POST /api/v1/questions \
  "{\"question\":\"How many days per week may employees work remotely?\",\"conversationId\":\"$CONVERSATION\"}"
pause 7

scene 6 "A question the documents cannot answer"
say "Nothing in either document covers this. A plain chat model would happily make something up."
call BOB "$BOB" POST /api/v1/questions '{"question":"What is the parental leave entitlement in Germany?"}'
pause 4
say "answerFound is false, there are no citations, and the text is a fixed server-side sentence."
say "When retrieval finds no evidence, the model is not even called."
pause 5

scene 7 "Another tenant sees nothing"
say "Mallory is an admin, but of a different tenant. She asks exactly the same question as Bob:"
JQ_FILTER='{answerFound, answer, citations}' \
  call MALLORY "$MALLORY" POST /api/v1/questions '{"question":"How many days in advance must flights be booked?"}'
pause 4
say "Her tenant has no documents, so there is no evidence and no answer."
say "Permissions are enforced in the API and the store, never by asking the model to behave."
pause 5

scene 8 "Recap"
printf '  %s✓%s Ingestion   pages → sections → chunks → embeddings → tenant-scoped store\n' "$GREEN" "$RESET"
printf '  %s✓%s Answers     grounded in retrieved chunks, cited by file, page and section\n' "$GREEN" "$RESET"
printf '  %s✓%s Unknown     a fixed "not enough information" answer instead of a guess\n' "$GREEN" "$RESET"
printf '  %s✓%s Security    JWT roles and tenant boundary enforced in code\n\n' "$GREEN" "$RESET"
say "Run it yourself: scripts/demo.sh    Verify everything: scripts/smoke-test.sh"
pause 6
