#!/usr/bin/env node
// Minimal OpenAI-compatible fake provider for local development and smoke tests.
// No dependencies, no network, deterministic output. NOT a model: it only imitates the wire format.
//
//   POST /v1/chat/completions   (JSON reply, SSE when "stream": true, grounded JSON when response_format is set)
//   POST /v1/embeddings         (hashed bag-of-words vectors, so similar wording => similar vectors)
//
// Usage: node tools/mock-llm/server.mjs [port]      (default 5099, or MOCK_LLM_PORT)
//        MOCK_LLM_DELAY_MS=5000 simulates a slow provider (to exercise timeouts).

import { createServer } from 'node:http';
import { createHash } from 'node:crypto';

const port = Number(process.argv[2] ?? process.env.MOCK_LLM_PORT ?? 5099);
const delayMs = Number(process.env.MOCK_LLM_DELAY_MS ?? 0);
const DIMENSIONS = 256;
const STOP_WORDS = new Set(('a an and are as at be by do does for from how i in is it many may must of on or our ' +
  'that the their there this to was what when where which who will with you your can should').split(' '));

const tokenize = (text) => (text.toLowerCase().match(/[a-z0-9]+/g) ?? [])
  .filter((word) => word.length > 1 && !STOP_WORDS.has(word))
  // crude stemming so "days"/"day" and "booked"/"book" land in the same bucket
  .map((word) => word.replace(/(ing|ed|es|s)$/, ''));

// Feature hashing: each token increments one of DIMENSIONS buckets; the vector is L2-normalised.
function embed(text) {
  const vector = new Array(DIMENSIONS).fill(0);
  for (const token of tokenize(text)) {
    vector[createHash('md5').update(token).digest().readUInt16BE(0) % DIMENSIONS] += 1;
  }
  const norm = Math.hypot(...vector) || 1;
  return vector.map((value) => value / norm);
}

// Imitates a well-behaved grounded model: answer from the best-matching source or admit not knowing.
function groundedAnswer(userContent) {
  const question = (userContent.match(/<question>([\s\S]*?)<\/question>/) ?? [])[1] ?? '';
  const questionTokens = new Set(tokenize(question));
  let best = { id: 0, overlap: 0, text: '' };
  for (const [, id, text] of userContent.matchAll(/<source id="(\d+)"[^>]*>([\s\S]*?)<\/source>/g)) {
    const overlap = new Set(tokenize(text).filter((token) => questionTokens.has(token))).size;
    if (overlap > best.overlap) best = { id: Number(id), overlap, text: text.trim() };
  }
  const enoughEvidence = best.overlap >= Math.max(2, Math.ceil(questionTokens.size / 2));
  if (!enoughEvidence) return { answerFound: false, answer: '', citations: [] };
  const firstSentence = best.text.split(/(?<=[.!?])\s+/)[0];
  return { answerFound: true, answer: `${firstSentence} [${best.id}]`, citations: [best.id] };
}

function readJson(request) {
  return new Promise((resolve, reject) => {
    let body = '';
    request.on('data', (chunk) => { body += chunk; });
    request.on('end', () => { try { resolve(JSON.parse(body || '{}')); } catch (error) { reject(error); } });
    request.on('error', reject);
  });
}

const sendJson = (response, status, payload) => {
  response.writeHead(status, { 'content-type': 'application/json' });
  response.end(JSON.stringify(payload));
};

const estimateTokens = (text) => Math.ceil(text.length / 4);

async function chatCompletions(body, response) {
  const messages = body.messages ?? [];
  const lastUser = [...messages].reverse().find((m) => m.role === 'user')?.content ?? '';
  const content = body.response_format
    ? JSON.stringify(groundedAnswer(lastUser))
    : `Mock reply to: ${lastUser.slice(0, 200)}`;
  const model = body.model ?? 'mock-chat';

  if (!body.stream) {
    return sendJson(response, 200, {
      id: 'chatcmpl-mock', object: 'chat.completion', model,
      choices: [{ index: 0, message: { role: 'assistant', content }, finish_reason: 'stop' }],
      usage: {
        prompt_tokens: estimateTokens(messages.map((m) => m.content).join(' ')),
        completion_tokens: estimateTokens(content),
      },
    });
  }

  response.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
  for (const word of content.split(/(?<= )/)) {
    response.write(`data: ${JSON.stringify({ model, choices: [{ index: 0, delta: { content: word } }] })}\n\n`);
    await new Promise((resolve) => setTimeout(resolve, 15));
  }
  response.end('data: [DONE]\n\n');
}

function embeddings(body, response) {
  const inputs = Array.isArray(body.input) ? body.input : [body.input ?? ''];
  sendJson(response, 200, {
    object: 'list', model: body.model ?? 'mock-embedding',
    data: inputs.map((text, index) => ({ object: 'embedding', index, embedding: embed(String(text)) })),
    usage: { prompt_tokens: inputs.reduce((sum, text) => sum + estimateTokens(String(text)), 0), total_tokens: 0 },
  });
}

createServer(async (request, response) => {
  try {
    if (request.method !== 'POST') return sendJson(response, 404, { error: { message: 'not found' } });
    const body = await readJson(request);
    if (delayMs > 0) await new Promise((resolve) => setTimeout(resolve, delayMs));
    if (request.url.endsWith('/chat/completions')) return await chatCompletions(body, response);
    if (request.url.endsWith('/embeddings')) return embeddings(body, response);
    return sendJson(response, 404, { error: { message: 'not found' } });
  } catch (error) {
    return sendJson(response, 400, { error: { message: String(error) } });
  }
}).listen(port, '127.0.0.1', () => console.log(`mock-llm listening on http://127.0.0.1:${port}/v1`));
