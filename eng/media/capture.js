// node capture.js <page.html> <query> <outdir>: writes frame PNGs from the page's #frames
const { chromium } = require('playwright');
const fs = require('fs'), path = require('path');
(async () => {
  const [page, query, outDir] = process.argv.slice(2);
  const browser = await chromium.launch();
  const tab = await browser.newPage();
  await tab.goto('file://' + path.resolve(page) + (query ? '?' + query : ''));
  await tab.waitForSelector('#frames', { state: 'attached', timeout: 120000 });
  const lines = (await tab.$eval('#frames', e => e.textContent)).split('\n');
  fs.mkdirSync(outDir, { recursive: true });
  lines.forEach((l, i) => fs.writeFileSync(path.join(outDir, String(i).padStart(4, '0') + '.png'), Buffer.from(l.split(',')[1], 'base64')));
  console.log(lines.length + ' frames');
  await browser.close();
})();
