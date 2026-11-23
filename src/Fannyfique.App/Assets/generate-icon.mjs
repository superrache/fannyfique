// Régénère app.ico depuis icon.svg (≥ 32 px) et icon-small.svg (≤ 24 px).
// cd <dossier temporaire> && npm i @resvg/resvg-js@2.6.2 png-to-ico@2.1.8
// node generate-icon.mjs <chemin>/Assets/icon.svg <dossier de sortie>
import { Resvg } from '@resvg/resvg-js';
import pngToIco from 'png-to-ico';
import fs from 'fs';
const [svgPath, outDir] = process.argv.slice(2);
const svg = fs.readFileSync(svgPath, 'utf8');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const small = fs.readFileSync(svgPath.replace('icon.svg','icon-small.svg'),'utf8');
const bufs = sizes.map(s => {
  const src = s <= 24 ? small : svg;
  const png = new Resvg(src, { fitTo: { mode: 'width', value: s } }).render().asPng();
  fs.writeFileSync(`${outDir}/icon-${s}.png`, png);
  return png;
});
fs.writeFileSync(`${outDir}/app.ico`, await pngToIco(bufs));
