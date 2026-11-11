# Demo

`demo.mp4` (about 4 minutes, silent) is a capture of one real run of `scripts/demo.sh`: every response on
screen came from the running API. The model behind it is `tools/mock-llm`, a deterministic stand-in, so
the answer wording is simpler than a real model's.

## Talk track

Use this if you record a narrated version. Run `scripts/demo.sh` and speak over each scene; the script
pauses long enough for roughly these lines. `DEMO_SPEED=1.3` slows it down, `DEMO_SPEED=0.8` speeds it up.

| # | Scene | Say |
|---|---|---|
| 1 | Introduction | What the assistant does. Three users: Alice can ingest, Bob can only ask, Mallory belongs to another company. The model here is a local stand-in. |
| 2 | Ingestion | Alice uploads the extracted text of a three-page PDF. The API splits it by page and heading, embeds each chunk and stores it under her tenant. Point at `chunkCount`. |
| 3 | Second document | A plain-text file works too. Bob tries the same upload and gets 403: roles are enforced by the API. |
| 4 | Cited answer | Bob asks about booking flights. Point at `answerFound: true` and the citation: file, page 2, section "Booking", snippet. |
| 5 | Follow-up | Same conversation, different document. The citation now points at the remote-work file. |
| 6 | Unanswerable | Nothing covers parental leave. `answerFound: false`, no citations, a fixed sentence. When retrieval finds nothing, the model is not called at all. |
| 7 | Tenant isolation | Mallory is an admin of another tenant and asks Bob's exact question. She gets nothing, because nothing is retrieved for her. |
| 8 | Recap | Ingestion, cited answers, safe unknown, security in code. |

## Sample documents

`sample-documents/` holds the two ingestion request bodies used in the demo. They are invented text.
