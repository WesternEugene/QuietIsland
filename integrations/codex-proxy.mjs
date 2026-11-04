import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { islandPost } from './bridge-client.mjs';
import { codexApprovalReply, codexQuestionReply } from './protocol.mjs';
import fs from 'node:fs';
import path from 'node:path';

// JSON-RPC stdio proxy for a host that would otherwise spawn `codex app-server`.
// It does not attach to, scrape or alter an already running Codex window.
const npmVendor = path.join(process.env.APPDATA || '', 'npm', 'node_modules', '@openai', 'codex', 'node_modules', '@openai', 'codex-win32-x64', 'vendor', 'x86_64-pc-windows-msvc', 'bin', 'codex.exe');
const executable = process.env.QUIET_ISLAND_CODEX_EXE || (process.platform === 'win32' ? (fs.existsSync(npmVendor) ? npmVendor : 'codex.exe') : 'codex');
const child = spawn(executable, ['app-server', ...process.argv.slice(2)], { stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true, shell: false });
const send = value => child.stdin.write(JSON.stringify(value) + '\n');
const intercept = async message => {
  const p = message.params || {};
  if (['item/commandExecution/requestApproval', 'item/fileChange/requestApproval'].includes(message.method)) {
    let decision = 'deny';
    const canAccept = !Array.isArray(p.availableDecisions) || p.availableDecisions.includes('accept');
    try { decision = (await islandPost('/request', {
      source: 'Codex · ' + (p.threadId || '').slice(0, 16),
      title: message.method.includes('commandExecution') ? 'Разрешить команду?' : 'Разрешить изменения файлов?',
      message: JSON.stringify(p, null, 2).slice(0, 20000), options: canAccept ? ['allow', 'deny'] : ['deny'], timeoutSeconds: 150
    })).decision; } catch { }
    send(codexApprovalReply(message.id, decision, p.availableDecisions)); return true;
  }
  if (['item/tool/requestUserInput', 'tool/requestUserInput'].includes(message.method)) {
    const answers = {};
    for (const question of p.questions || []) {
      try {
        answers[question.id] = (await islandPost('/request', {
          source: 'Codex', title: question.header || 'Вопрос', message: question.question || '',
          options: (question.options || []).slice(0, 10).map(x => x.label).concat(['deny']), allowText: true, timeoutSeconds: 150
        })).decision;
      } catch { answers[question.id] = 'deny'; }
    }
    send(codexQuestionReply(message.id, answers)); return true;
  }
  return false;
};

createInterface({ input: process.stdin }).on('line', line => { if (child.stdin.writable) child.stdin.write(line + '\n'); });
createInterface({ input: child.stdout }).on('line', line => {
  let message;
  try { message = JSON.parse(line); } catch { process.stdout.write(line + '\n'); return; }
  if (message.id !== undefined && message.method) {
    if (['item/commandExecution/requestApproval', 'item/fileChange/requestApproval', 'item/tool/requestUserInput', 'tool/requestUserInput'].includes(message.method)) {
      void intercept(message).catch(() => send({ id: message.id, error: { code: -32603, message: 'Island request failed' } })); return;
    }
  }
  if (['turn/started', 'turn/completed'].includes(message.method)) void islandPost('/events', {
    source: 'Codex', title: message.method === 'turn/started' ? 'Работает' : 'Готово', message: pSummary(message.params)
  }, 1500).catch(() => {});
  process.stdout.write(line + '\n');
});
function pSummary(params) { return params?.threadId ? 'Поток ' + params.threadId : ''; }
child.on('error', () => { process.stderr.write('Не удалось запустить Codex. Укажите путь к codex.exe в QUIET_ISLAND_CODEX_EXE.\n'); process.exitCode = 1; });
child.on('exit', code => process.exit(code || 0));
process.on('SIGINT', () => child.kill()); process.on('SIGTERM', () => child.kill());
