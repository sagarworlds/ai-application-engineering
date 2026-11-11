#!/usr/bin/env node
// Turns a recording made by record.mjs into a video and per-scene screenshots.
//
// Usage: node tools/demo-recorder/render.mjs <recording.json> <video.mp4> <screenshot-dir>
// Requires: playwright (with Chromium) and ffmpeg.

import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const { chromium } = createRequire(import.meta.url)('playwright');

const [recordingPath, videoPath, screenshotDir] = process.argv.slice(2);
if (!recordingPath || !videoPath || !screenshotDir) {
  console.error('Usage: render.mjs <recording.json> <video.mp4> <screenshot-dir>');
  process.exit(2);
}

const CLEAR = '\u001b[2J\u001b[H';
const VIEWPORT = { width: 1280, height: 720 };
const SCENE_NAMES = ['intro', 'ingestion', 'second-document', 'cited-answer', 'follow-up', 'unanswerable', 'tenant-isolation', 'recap'];
const replayUrl = pathToFileURL(resolve(dirname(fileURLToPath(import.meta.url)), 'replay.html')).href;

const { events } = JSON.parse(readFileSync(recordingPath, 'utf8'));

// Skip the build/startup wait: the film starts at the first scene (the second screen clear).
const clears = events.filter((event) => event.data.includes(CLEAR));
const firstScene = events.indexOf(clears[1] ?? clears[0]);
const filmed = events.slice(firstScene);

const browser = await chromium.launch();
const videoDir = mkdtempSync(join(tmpdir(), 'eka-demo-video-'));
try {
  const filming = await browser.newContext({ viewport: VIEWPORT, recordVideo: { dir: videoDir, size: VIEWPORT } });
  const film = await filming.newPage();
  await film.goto(replayUrl);
  await film.evaluate(([replayed]) => window.play(replayed, 1500), [filmed]);
  await filming.close(); // finalises the .webm

  const webm = join(videoDir, readdirSync(videoDir).find((name) => name.endsWith('.webm')));
  mkdirSync(dirname(resolve(videoPath)), { recursive: true });
  // H.264 + yuv420p + faststart: plays in browsers, on GitHub and in common players.
  execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-i', webm, '-c:v', 'libx264', '-preset', 'slow', '-crf', '28',
    '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-an', videoPath]);

  mkdirSync(screenshotDir, { recursive: true });
  const stills = await browser.newContext({ viewport: VIEWPORT, deviceScaleFactor: 2 });
  const still = await stills.newPage();
  await still.goto(replayUrl);
  const sceneCount = await still.evaluate(([all]) => window.showScene(all, 0), [filmed]);
  for (let scene = 1; scene <= sceneCount; scene++) {
    await still.evaluate(([all, number]) => window.showScene(all, number), [filmed, scene]);
    const name = `demo-${String(scene).padStart(2, '0')}-${SCENE_NAMES[scene - 1] ?? 'scene'}.png`;
    await still.screenshot({ path: join(screenshotDir, name) });
  }
  await stills.close();
  console.log(`Rendered ${videoPath} and ${sceneCount} screenshots in ${screenshotDir}`);
} finally {
  await browser.close();
  rmSync(videoDir, { recursive: true, force: true });
}
