const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const [page, out] = process.argv.slice(2);
  const browser = await chromium.launch();
  const tab = await browser.newPage({ viewport: { width: 1280, height: 640 } });
  await tab.goto('file://' + path.resolve(page));
  await tab.screenshot({ path: out });
  await browser.close();
})();
