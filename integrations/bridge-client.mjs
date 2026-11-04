import fs from 'node:fs/promises';
import path from 'node:path';

export async function islandPost(route, payload, timeout = 170_000) {
  const directory = process.env.QUIET_ISLAND_DATA || path.join(process.env.LOCALAPPDATA || '', 'QuietIsland');
  let port = 47832;
  try {
    const state = JSON.parse(await fs.readFile(path.join(directory, 'state.json'), 'utf8'));
    port = state.Settings?.BridgePort || state.settings?.bridgePort || port;
  } catch { /* Default is also used before the first state save. */ }
  const url = new URL(process.env.QUIET_ISLAND_URL || `http://localhost:${port}`);
  if (url.protocol !== 'http:' || !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname) || url.username || url.password) {
    throw new Error('The Island bridge must be on loopback HTTP');
  }
  const token = (await fs.readFile(path.join(directory, 'bridge-token'), 'utf8')).trim();
  const response = await fetch(new URL(route, url), {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Island-Token': token },
    body: JSON.stringify(payload), signal: AbortSignal.timeout(timeout), redirect: 'error'
  });
  if (!response.ok) throw new Error(`Island bridge HTTP ${response.status}`);
  return response.json();
}
