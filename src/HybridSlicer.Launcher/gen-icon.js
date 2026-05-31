#!/usr/bin/env node
'use strict';
const { deflateSync } = require('zlib');
const fs = require('fs');

function clamp01(v) { return Math.max(0, Math.min(1, v)); }
function lerp(a, b, t) { return a + (b - a) * t; }
function lerpC(c1, c2, t) { return [lerp(c1[0],c2[0],t), lerp(c1[1],c2[1],t), lerp(c1[2],c2[2],t)]; }

function sdRRect(px, py, cx, cy, hw, hh, r) {
  const dx = Math.abs(px - cx) - hw + r;
  const dy = Math.abs(py - cy) - hh + r;
  return Math.sqrt(Math.max(dx,0)**2 + Math.max(dy,0)**2) - r + Math.min(Math.max(dx,dy), 0);
}

function sdBox(px, py, cx, cy, hw, hh) {
  const dx = Math.abs(px - cx) - hw;
  const dy = Math.abs(py - cy) - hh;
  return Math.sqrt(Math.max(dx,0)**2 + Math.max(dy,0)**2) + Math.min(Math.max(dx,dy), 0);
}

// ── Logo concept: stylised cube with visible layers + diagonal precision cut ──
// Represents: additive layers (3D printing) + subtractive cut (CNC) = hybrid mfg
// Colors: blue (#2563EB) → violet (#7C3AED) gradient matching the slicer theme

function renderPNG(W) {
  const buf = Buffer.alloc((1 + W * 4) * W, 0);
  const cx = W/2, cy = W/2;
  const hw = W*0.44, hh = W*0.44;
  const cr = W*0.20;
  const AA = 1.2;

  // Theme gradient: blue → violet
  const col1 = [37, 99, 235];     // #2563EB (blue-600)
  const col2 = [124, 58, 237];    // #7C3AED (violet-600)

  for (let y = 0; y < W; y++) {
    buf[y*(W*4+1)] = 0;
    for (let x = 0; x < W; x++) {
      const off = y*(W*4+1) + 1 + x*4;

      // Rounded rect background
      const rd = sdRRect(x, y, cx, cy, hw, hh, cr);
      const ra = clamp01(0.5 - rd/AA);
      if (ra <= 0) continue;

      // Diagonal gradient (top-left blue → bottom-right violet)
      const nx = (x - (cx-hw)) / (2*hw);
      const ny = (y - (cy-hh)) / (2*hh);
      const gy = clamp01((nx + ny) / 2);
      const bg = lerpC(col1, col2, gy);

      // Subtle top-left highlight
      const hi = clamp01(1 - (nx + ny) * 1.5) * 0.12;
      bg[0] = Math.min(255, bg[0] + hi*50);
      bg[1] = Math.min(255, bg[1] + hi*50);
      bg[2] = Math.min(255, bg[2] + hi*50);

      // ── Draw the symbol: 3 stacked horizontal layers + diagonal cut ──

      // Three layer bars (additive manufacturing / slicing layers)
      const layerW = 0.30;  // half-width of layer bars
      const layerH = 0.032; // half-height of each bar
      const layerCx = 0.46;
      const layer1 = sdBox(nx, ny, layerCx, 0.30, layerW, layerH);
      const layer2 = sdBox(nx, ny, layerCx, 0.46, layerW, layerH);
      const layer3 = sdBox(nx, ny, layerCx, 0.62, layerW, layerH);
      const layerD = Math.min(layer1, layer2, layer3) * (2*hw);
      const layerA = clamp01(0.5 - layerD/AA);

      // Vertical bar on the left connecting layers (like an 'F' spine)
      const spineD = sdBox(nx, ny, 0.20, 0.46, 0.04, 0.20) * (2*hw);
      const spineA = clamp01(0.5 - spineD/AA);

      // Diagonal cut line (subtractive/CNC precision) — goes from upper-right to lower area
      // Thin diagonal slash representing the cutting tool path
      const cutAngle = -0.65; // radians
      const cutCx = 0.62, cutCy = 0.46;
      const cosA = Math.cos(cutAngle), sinA = Math.sin(cutAngle);
      const rx = (nx - cutCx) * cosA - (ny - cutCy) * sinA;
      const ry = (nx - cutCx) * sinA + (ny - cutCy) * cosA;
      const cutD = (Math.max(Math.abs(rx) - 0.015, 0) + Math.max(Math.abs(ry) - 0.22, 0)) * (2*hw);
      const cutA = clamp01(0.5 - cutD/AA);

      // Small diamond/arrow at the end of the cut (tool head indicator)
      const tipX = cutCx + 0.22 * Math.cos(cutAngle + Math.PI/2);
      const tipY = cutCy + 0.22 * Math.sin(cutAngle + Math.PI/2);
      const tipD = (Math.abs(nx - tipX) + Math.abs(ny - tipY) - 0.04) * (2*hw);
      const tipA = clamp01(0.5 - tipD/AA);

      let r = bg[0], g = bg[1], b = bg[2];

      // Composite: layers (white, slight transparency for depth)
      const whiteA = Math.max(layerA, spineA);
      if (whiteA > 0) {
        // Layers get slightly different brightness per level for depth
        const layerBright = layerA > 0 ? (
          layer1 <= layer2 && layer1 <= layer3 ? 1.0 :
          layer2 <= layer3 ? 0.92 : 0.84
        ) : 0.95;
        const wb = 255 * (whiteA > layerA ? 0.95 : layerBright);
        r = lerp(r, wb, whiteA * 0.95);
        g = lerp(g, wb, whiteA * 0.95);
        b = lerp(b, wb, whiteA * 0.95);
      }

      // Diagonal cut line (bright cyan/white — precision)
      if (cutA > 0) {
        r = lerp(r, 200, cutA * 0.9);
        g = lerp(g, 240, cutA * 0.9);
        b = lerp(b, 255, cutA * 0.9);
      }

      // Tool tip diamond
      if (tipA > 0) {
        r = lerp(r, 255, tipA * 0.85);
        g = lerp(g, 255, tipA * 0.85);
        b = lerp(b, 255, tipA * 0.85);
      }

      buf[off]   = Math.round(clamp01(r/255)*255);
      buf[off+1] = Math.round(clamp01(g/255)*255);
      buf[off+2] = Math.round(clamp01(b/255)*255);
      buf[off+3] = Math.round(ra * 255);
    }
  }

  return encodePNG(W, buf);
}

function encodePNG(W, buf) {
  const comp = deflateSync(buf, { level: 9 });
  const crcT = Array.from({length:256},(_,n)=>{let c=n;for(let k=0;k<8;k++)c=(c&1)?(0xEDB88320^(c>>>1)):(c>>>1);return c>>>0});
  const crc32 = b=>{let c=0xFFFFFFFF;for(const x of b)c=(c>>>8)^crcT[(c^x)&0xFF];return(~c)>>>0};
  const chunk = (t,d)=>{
    const tb=Buffer.from(t);const lb=Buffer.alloc(4);lb.writeUInt32BE(d.length);
    const cb=Buffer.alloc(4);cb.writeUInt32BE(crc32(Buffer.concat([tb,d])));
    return Buffer.concat([lb,tb,d,cb]);
  };
  const ihdr=Buffer.alloc(13);
  ihdr.writeUInt32BE(W,0);ihdr.writeUInt32BE(W,4);ihdr[8]=8;ihdr[9]=6;
  return Buffer.concat([
    Buffer.from([0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A]),
    chunk('IHDR',ihdr), chunk('IDAT',comp), chunk('IEND',Buffer.alloc(0))
  ]);
}

const sizes = [256, 64, 48, 32, 16];
const pngs = sizes.map(s => renderPNG(s));
fs.writeFileSync('icon-preview.png', pngs[0]);

const hSz = 6 + sizes.length*16; let o = hSz;
const h = Buffer.alloc(6); h.writeUInt16LE(0,0); h.writeUInt16LE(1,2); h.writeUInt16LE(sizes.length,4);
const ent = sizes.map((s,i)=>{
  const e=Buffer.alloc(16); e[0]=s===256?0:s; e[1]=e[0];
  e.writeUInt16LE(1,4); e.writeUInt16LE(32,6);
  e.writeUInt32LE(pngs[i].length,8); e.writeUInt32LE(o,12);
  o+=pngs[i].length; return e;
});
fs.writeFileSync('icon.ico', Buffer.concat([h,...ent,...pngs]));
console.log('Done: icon.ico + icon-preview.png');
