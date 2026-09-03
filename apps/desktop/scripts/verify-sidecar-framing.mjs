// SPDX-License-Identifier: GPL-3.0-only

import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { resolve } from 'node:path';

const executableArgument = process.argv[2];
if (!executableArgument) {
  throw new Error('Pass the published KM.Tools bridge executable path to verify its framing.');
}

const executablePath = resolve(executableArgument);
const child = spawn(executablePath, ['bridge'], {
  stdio: ['pipe', 'pipe', 'pipe'],
  windowsHide: true
});
const pendingFrames = [];
const stderrChunks = [];
let bufferedStdout = Buffer.alloc(0);
let childExit = null;

child.stdout.on('data', (chunk) => {
  bufferedStdout = Buffer.concat([bufferedStdout, chunk]);
  while (true) {
    const newline = bufferedStdout.indexOf(0x0a);
    if (newline < 0) {
      return;
    }

    const frame = bufferedStdout.subarray(0, newline);
    bufferedStdout = bufferedStdout.subarray(newline + 1);
    const pending = pendingFrames.shift();
    if (!pending) {
      childExit = new Error('The bridge returned an unexpected response frame.');
      child.kill();
      return;
    }

    pending.resolve(frame);
  }
});
child.stderr.on('data', (chunk) => stderrChunks.push(chunk));
child.on('exit', (code, signal) => {
  childExit ??= new Error(
    `The bridge exited before framing verification completed (code ${String(code)}, signal ${String(signal)}).${formatStderr()}`
  );
  for (const pending of pendingFrames.splice(0)) {
    pending.reject(childExit);
  }
});

try {
  await once(child, 'spawn');
  await sendProbe('KM-SIDECAR-FRAMING-SHORT', '__km_framing_short__');

  // The previous TextReader implementation deterministically stalled on this shape while stdin
  // remained open: its eighth 8 KiB destination already contained the complete tail and LF, but
  // StreamReader tried to fill the rest of that destination before returning it to the scanner.
  await sendProbe(
    'KM-SIDECAR-FRAMING-LONG',
    '__km_framing_long_éééééééé__',
    59_169
  );

  await sendProbe(
    'KM-SIDECAR-FRAMING-UTF8',
    '__km_framing_Pokémon_测试__',
    null,
    { assertCommandEcho: true }
  );

  await sendInvalidUtf8Probe();
  await sendProbe('KM-SIDECAR-FRAMING-RECOVERY', '__km_framing_recovery__');
  await sendProbe(
    'KM-SIDECAR-FRAMING-CR',
    '__km_framing_split_crlf__',
    null,
    { terminator: '\r' }
  );
  await sendProbe(
    'KM-SIDECAR-FRAMING-AFTER-CRLF',
    '__km_framing_after_split_crlf__',
    null,
    { prefix: Buffer.from('\n', 'utf8') }
  );
} finally {
  child.stdin.end();
  if (child.exitCode === null && child.signalCode === null) {
    const exited = once(child, 'exit').then(() => true);
    const gracePeriod = new Promise((resolveGracePeriod) => {
      const timer = setTimeout(() => resolveGracePeriod(false), 1_000);
      timer.unref();
    });
    if (!await Promise.race([exited, gracePeriod])) {
      child.kill();
      await once(child, 'exit');
    }
  }
}

console.log('Published KM.Tools bridge passed persistent input framing verification.');

async function sendProbe(
  requestId,
  command,
  targetLineCharacters = null,
  { assertCommandEcho = false, prefix = Buffer.alloc(0), terminator = '\n' } = {}
) {
  const requestJson = JSON.stringify({ command, payload: {}, requestId });
  if (targetLineCharacters !== null && requestJson.length > targetLineCharacters) {
    throw new Error('The framing probe envelope exceeds its target line length.');
  }

  const padding = targetLineCharacters === null
    ? ''
    : ' '.repeat(targetLineCharacters - requestJson.length);
  const requestFrame = Buffer.concat([
    prefix,
    Buffer.from(`${requestJson}${padding}${terminator}`, 'utf8')
  ]);
  const response = await sendFrame(requestFrame, requestId);
  const expectedMessage = `Bridge command '${command}' is not supported.`;
  if (assertCommandEcho
      && (response.error?.code !== 'KM-BRIDGE-UNSUPPORTED-COMMAND'
        || response.error?.message !== expectedMessage)) {
    throw new Error(
      `The bridge did not preserve probe command ${JSON.stringify(command)} through UTF-8 framing: ${JSON.stringify(response)}.`
    );
  }
}

async function sendInvalidUtf8Probe() {
  const requestFrame = Buffer.concat([
    Buffer.from('{"command":"__km_framing_invalid_utf8__","value":"', 'utf8'),
    Buffer.from([0xc3, 0x28]),
    Buffer.from('"}\n', 'utf8')
  ]);
  const response = await sendFrame(requestFrame, null, 'KM-SIDECAR-FRAMING-INVALID-UTF8');
  if (response.error?.code !== 'KM-BRIDGE-INVALID-JSON'
      || response.error?.message !== 'Bridge request JSON must use valid UTF-8 encoding.') {
    throw new Error('The bridge did not reject invalid UTF-8 with its stable transport error.');
  }
}

async function sendFrame(requestFrame, expectedRequestId, probeLabel = expectedRequestId) {
  const responsePromise = new Promise((resolveFrame, rejectFrame) => {
    pendingFrames.push({ reject: rejectFrame, resolve: resolveFrame });
  });
  const writePromise = new Promise((resolveWrite, rejectWrite) => {
    child.stdin.write(requestFrame, (error) => {
      if (error) {
        rejectWrite(error);
      } else {
        resolveWrite();
      }
    });
  });

  await withTimeout(writePromise, 10_000, `writing probe ${probeLabel}`);
  const responseFrame = await withTimeout(
    responsePromise,
    10_000,
    `waiting for probe ${probeLabel}`
  );
  const responseText = responseFrame.toString('utf8').replace(/\r$/, '');
  const response = JSON.parse(responseText);
  if ((response.requestId ?? null) !== expectedRequestId) {
    throw new Error(
      `The bridge crossed response ownership: expected ${String(expectedRequestId)}, received ${String(response.requestId)}.`
    );
  }

  return response;
}

async function withTimeout(promise, timeoutMilliseconds, operation) {
  let timer;
  const timeout = new Promise((_, rejectTimeout) => {
    timer = setTimeout(() => {
      rejectTimeout(new Error(
        `Timed out ${operation}.${formatStderr()}`
      ));
    }, timeoutMilliseconds);
    timer.unref();
  });

  try {
    return await Promise.race([promise, timeout]);
  } finally {
    clearTimeout(timer);
  }
}

function formatStderr() {
  const stderr = Buffer.concat(stderrChunks).toString('utf8').trim();
  return stderr ? ` Bridge stderr: ${stderr}` : '';
}
