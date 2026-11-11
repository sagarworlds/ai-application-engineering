#!/usr/bin/env node
// Runs scripts/demo.sh and records what it prints, with timing, to a JSON file.
// The recording is a faithful capture of one real run: nothing in it is typed in by hand.
//
// Usage: node tools/demo-recorder/record.mjs <output.json>

import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const output = process.argv[2];
if (!output) {
  console.error('Usage: record.mjs <output.json>');
  process.exit(2);
}

const started = Date.now();
const events = [];
const demo = spawn(resolve(root, 'scripts/demo.sh'), { cwd: root, env: process.env });

demo.stdout.on('data', (chunk) => {
  events.push({ t: Date.now() - started, data: chunk.toString('utf8') });
  process.stdout.write(chunk); // show progress while recording
});
demo.stderr.pipe(process.stderr);

demo.on('close', (code) => {
  if (code !== 0) {
    console.error(`demo.sh exited with code ${code}; nothing was written.`);
    process.exit(code ?? 1);
  }
  writeFileSync(output, JSON.stringify({ recordedAt: new Date(started).toISOString(), events }));
  console.error(`\nRecorded ${events.length} output events over ${((Date.now() - started) / 1000).toFixed(0)} s -> ${output}`);
});
