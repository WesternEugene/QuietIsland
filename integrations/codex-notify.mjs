import { islandPost } from './bridge-client.mjs';
let data;
try { data = JSON.parse(process.argv.at(-1)); } catch { process.exit(0); }
try { await islandPost('/events', { source: 'Codex', title: data.type === 'agent-turn-complete' ? 'Готово' : data.type || 'Событие', message: (data['last-assistant-message'] || '').slice(0, 6000) }, 2000); } catch { }
