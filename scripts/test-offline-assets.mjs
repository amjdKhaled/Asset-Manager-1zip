import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
async function scan(folder) {
  for (const entry of await readdir(folder, { withFileTypes: true })) {
    const path = join(folder, entry.name);
    if (entry.isDirectory()) await scan(path);
    else if (/\.(cshtml|css|js)$/.test(entry.name)) {
      const source = await readFile(path, 'utf8');
      assert.doesNotMatch(source, /<(?:script|link|iframe)\b[^>]*(?:src|href)\s*=\s*["'](?:https?:)?\/\//i, path);
      assert.doesNotMatch(source, /(?:@import\s*|url\(\s*)["']?(?:https?:)?\/\//i, path);
    }
  }
}
await scan('src/LFPortal.Web/Views');
await scan('src/LFPortal.Web/wwwroot/css');
assert.ok((await readFile('src/LFPortal.Web/wwwroot/lib/chartjs/chart.umd.min.js')).length > 100000);
console.log('Offline assets: no external script, stylesheet, iframe or font dependencies; Chart.js is bundled.');
