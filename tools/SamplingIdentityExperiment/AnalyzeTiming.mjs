// Offline analysis only. Reads existing captures; never sends input or changes runtime.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const dir = process.argv[2];
if (!dir) throw new Error('Usage: node AnalyzeTiming.mjs <capture-directory>');
const lines = fs.readFileSync(path.join(dir, 'receiver-trace', 'trace.csv'), 'utf8').trim().split(/\r?\n/);
const header = lines.shift().replace(/^\uFEFF/, '').split(',');
const rows = lines.map(line => {
  const fields = line.split(',');
  assert.equal(fields.length, header.length);
  return Object.fromEntries(header.map((key, i) => [key, ['kind', 'event', 'status', 'senderRun'].includes(key) ? fields[i] : Number(fields[i])]));
});
const text = fs.readFileSync(path.join(dir, 'receiver-trace', 'summary.txt'), 'utf8');
const freq = Number(text.match(/Stopwatch frequency: (\d+)/)[1]);
const ms = ticks => ticks * 1000 / freq;
const stats = values => {
  const sorted = [...values].sort((a, b) => a - b);
  if (!sorted.length) return { n: 0 };
  const at = p => sorted[Math.ceil(p * sorted.length) - 1];
  return { n: sorted.length, min: sorted[0], p01: at(.01), p05: at(.05), p50: at(.5), p95: at(.95), p99: at(.99), max: sorted.at(-1), over8: sorted.filter(x => x > 8).length, over20: sorted.filter(x => x > 20).length };
};
const intervals = (items, key, scale) => items.slice(1).map((r, i) => (r[key] - items[i][key]) * scale);
const samples = rows.filter(r => r.kind === 'Sample' && r.status === 'Accepted');
const identities = new Set(samples.map(r => `${r.runtimeRun}/${r.senderRun}/${r.session}`));
assert.equal(identities.size, 1, 'Use one complete contact per capture');
assert.equal(samples[0].event, 'Down');
assert.equal(samples.at(-1).event, 'Up');
assert.equal(samples.filter(r => r.event === 'Down').length, 1);
assert.equal(samples.filter(r => r.event === 'Up').length, 1);
const down = samples[0], up = samples.at(-1);
const same = r => r.runtimeRun === down.runtimeRun && r.senderRun === down.senderRun && r.session === down.session;
const move = samples.filter(r => r.event === 'Move');
let lastPlay = down.atQpc + .008 * freq;
let lastAndroid = down.androidTimestampNs;
const margins = samples.slice(1).map(r => {
  assert(r.androidTimestampNs > lastAndroid, 'Mapping requires strictly increasing timestamps');
  lastAndroid = r.androidTimestampNs;
  const mapped = Math.round(down.atQpc + .008 * freq + (r.androidTimestampNs - down.androidTimestampNs) * freq / 1e9);
  assert(mapped <= r.atQpc + freq, 'Unexpected timeline fallback');
  lastPlay = Math.max(lastPlay, mapped);
  return { ordinal: r.ordinal, androidTimestampNs: r.androidTimestampNs, receiveQpc: r.atQpc, mappedPlayQpc: lastPlay, marginMs: ms(lastPlay - r.atQpc), receiveDriftFromDownMs: ms(r.atQpc - down.atQpc) - (r.androidTimestampNs - down.androidTimestampNs) / 1e6 };
});
const playPoints = [{ receiveQpc: down.atQpc, mappedPlayQpc: down.atQpc + .008 * freq }, ...margins];
const coverageGaps = playPoints.slice(1).map((r, i) => ({
  sampleOrdinal: samples[i + 1].ordinal,
  gapMs: ms(r.receiveQpc - playPoints[i].mappedPlayQpc),
  previousPlayQpc: playPoints[i].mappedPlayQpc,
  nextReceiveQpc: r.receiveQpc
})).filter(r => r.gapMs > 0);
const ticks = rows.filter(r => r.kind === 'Tick' && same(r));
const outputs = rows.filter(r => r.kind === 'Output' && same(r));
const longTickGaps = ticks.slice(1).map((r, i) => ({ start: ticks[i].atQpc, end: r.atQpc, ms: ms(r.atQpc - ticks[i].atQpc) })).filter(r => r.ms > 20);
const longTickGapsWithMissingCoverage = longTickGaps.filter(t => coverageGaps.some(g => g.previousPlayQpc < t.end && g.nextReceiveQpc > t.start));
const nonzero = outputs.filter(r => r.dx !== 0 || r.dy !== 0);
const native = rows.filter(r => r.kind === 'NativeSubmit' && r.runtimeRun === down.runtimeRun && r.atQpc >= down.atQpc && r.atQpc <= outputs.at(-1).atQpc);
assert.equal(native.length, nonzero.length);
for (let i = 0; i < native.length; ++i) {
  assert.equal(native[i].dx, nonzero[i].dx); assert.equal(native[i].dy, nonzero[i].dy);
  assert(native[i].ordinal < nonzero[i].ordinal && native[i].endQpc <= nonzero[i].atQpc);
  assert(native[i].atQpc >= (i ? nonzero[i - 1].atQpc : down.atQpc));
}
const receives = rows.filter(r => r.kind === 'Receive' && same(r));
const acceptedMoves = receives.filter(r => r.status === 'Accepted' && r.event === 'Move');
const rawText = fs.readFileSync(path.join(dir, 'getevent.txt'), 'utf8');
const kernelTimes = [...rawText.matchAll(/\[\s*(\d+)\.(\d+)\]\s+EV_SYN\s+SYN_REPORT/g)].map(m => Number(m[1]) * 1e6 + Number(m[2].padEnd(6, '0')));
const kernelSet = new Set(kernelTimes);
const overlap = samples.filter(r => r.androidTimestampNs / 1000 >= kernelTimes[0] && r.androidTimestampNs / 1000 <= kernelTimes.at(-1));
const overlapMove = move.filter(r => r.androidTimestampNs / 1000 >= kernelTimes[0] && r.androidTimestampNs / 1000 <= kernelTimes.at(-1));
const missingKernel = overlap.filter(r => !kernelSet.has(r.androidTimestampNs / 1000));
const receivedAndroidSet = new Set(samples.map(r => r.androidTimestampNs / 1000));
const kernelMissingReceiver = kernelTimes.filter(t => !receivedAndroidSet.has(t));
const match = Object.fromEntries(acceptedMoves.map(r => [r.sequence, r.sampleCount]));
const result = {
  method: 'Single complete Receiver contact. DOWN-anchored fixed M8 map; clocks are not synchronized. Margin is relative playout headroom, not absolute one-way latency. Kernel capture covers only the first part; exact timestamp comparisons use that overlap only.',
  identity: { runtimeRun: down.runtimeRun, senderRun: down.senderRun, session: down.session },
  contact: { samples: samples.length, moveSamples: move.length, spanSeconds: (up.androidTimestampNs - down.androidTimestampNs) / 1e9, downQpc: down.atQpc, upReceiveQpc: up.atQpc, outputEndAfterUpReceiveMs: ms(outputs.at(-1).atQpc - up.atQpc) },
  androidMoveIntervalsMs: stats(intervals(move, 'androidTimestampNs', 1e-6)),
  receiveMoveIntervalsMs: stats(intervals(acceptedMoves, 'atQpc', 1000 / freq)),
  packets: { acceptedMovePackets: acceptedMoves.length, samplesPerPacket: [...new Set(Object.values(match))], statuses: Object.fromEntries([...new Set(receives.map(r => r.status))].map(s => [s, receives.filter(r => r.status === s).length])) },
  playout: { marginMs: stats(margins.map(r => r.marginMs)), lateSamples: margins.filter(r => r.marginMs < 0).length, below1ms: margins.filter(r => r.marginMs < 1).length, driftFromDownMs: stats(margins.map(r => r.receiveDriftFromDownMs)), nextSampleArrivesAfterPreviousPlayout: { definition: 'Positive interval from previous sample mapped playout to next sample receive. Indicates missing future sample coverage, not a scheduler or physical-finger fault diagnosis.', count: coverageGaps.length, totalMs: coverageGaps.reduce((s, r) => s + r.gapMs, 0), gapsMs: stats(coverageGaps.map(r => r.gapMs)) } },
  ticks: { count: ticks.length, missed: ticks.reduce((s, r) => s + r.missedTicks, 0), deadlineObservationLatenessMs: stats(ticks.map(r => ms(r.atQpc - r.deadlineQpc))), intervalMs: stats(intervals(ticks, 'atQpc', 1000 / freq)), gapsOver20ms: longTickGaps.length, gapsOver20msOverlappingMissingFutureCoverage: longTickGapsWithMissingCoverage.length },
  output: { opportunities: outputs.length, nonzero: nonzero.length, nonzeroIntervalMs: stats(intervals(nonzero, 'atQpc', 1000 / freq)) },
  native: { count: native.length, failures: native.filter(r => r.status !== 'Success').length, adapterDurationMs: stats(native.map(r => ms(r.endQpc - r.atQpc))) },
  kernelOverlap: { synReports: kernelTimes.length, spanSeconds: (kernelTimes.at(-1) - kernelTimes[0]) / 1e6, receiverSamplesInOverlap: overlap.length, exactTimestampMatches: overlap.length - missingKernel.length, receiverWithoutKernelTimestamp: missingKernel.length, kernelWithoutReceiverTimestamp: kernelMissingReceiver.length, synDropped: (rawText.match(/SYN_DROPPED/g) ?? []).length, androidMoveIntervalsMs: stats(intervals(overlapMove, 'androidTimestampNs', 1e-6)) }
};
assert.equal(missingKernel.length, 0, 'Unexpected unmatched Android timestamp in kernel overlap');
assert.equal(kernelMissingReceiver.length, 0, 'Unexpected raw report missing at Receiver');
fs.writeFileSync(path.join(dir, 'timing-analysis.json'), JSON.stringify(result, null, 2));
fs.writeFileSync(path.join(dir, 'sample-margins.csv'), Object.keys(margins[0]).join(',') + '\n' + margins.map(r => Object.values(r).join(',')).join('\n') + '\n');
fs.writeFileSync(path.join(dir, 'coverage-gaps.csv'), 'sampleOrdinal,gapMs,previousPlayQpc,nextReceiveQpc\n' + coverageGaps.map(r => Object.values(r).join(',')).join('\n') + '\n');
console.log(JSON.stringify(result, null, 2));
