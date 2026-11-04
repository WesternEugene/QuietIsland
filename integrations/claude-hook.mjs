import { islandPost } from './bridge-client.mjs';
import { claudeDecision } from './protocol.mjs';
import path from 'node:path';

let raw = '';
for await (const chunk of process.stdin) { raw += chunk; if (raw.length > 1_000_000) process.exit(1); }
let data;
try { data = JSON.parse(raw); } catch { process.exit(1); }
const event = data.hook_event_name || 'Notification';
const source = 'Claude Code' + (data.cwd ? ' · ' + path.basename(data.cwd) : '');
const tool = data.tool_name || '';
const message = data.tool_input?.command || JSON.stringify(data.tool_input || {}) || '';

if (event === 'PermissionRequest') {
  let decision = 'deny';
  try { decision = (await islandPost('/request', { source, title: `Разрешить ${tool}?`, message: message.slice(0, 20000), options: ['allow', 'deny'], timeoutSeconds: 150 })).decision; }
  catch { /* No answer never grants permission. */ }
  process.stdout.write(JSON.stringify(claudeDecision(decision)));
} else {
  const title = { SessionStart: 'Сессия началась', PreToolUse: 'Работает · ' + tool, PostToolUse: 'Завершено · ' + tool, Stop: 'Готово', Notification: 'Уведомление' }[event] || event;
  try { await islandPost('/events', { source, title, message: (event === 'Stop' ? data.last_assistant_message || '' : data.message || message).slice(0, 6000) }, 2000); } catch { /* Non-decision hooks must not interrupt work. */ }
}
