// 居民底模构建器 · 逐块渲染（每块单独一图，最后拼接）
// 本地离线：无网络、无第三方依赖。
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import {fileURLToPath} from 'node:url';
import {UNIT, PALETTE, PARTS, PART_ORDER} from './resident-model.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const outDir = path.join(root, 'review/character');
fs.mkdirSync(outDir, {recursive:true});

/* ---------- 向量 ---------- */
const V = {
  sub:(a,b)=>[a[0]-b[0],a[1]-b[1],a[2]-b[2]],
  dot:(a,b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2],
  cross:(a,b)=>[a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]],
  norm:a=>{const l=Math.hypot(a[0],a[1],a[2])||1;return[a[0]/l,a[1]/l,a[2]/l];}
};
const hex2rgb = h => [parseInt(h.slice(1,3),16), parseInt(h.slice(3,5),16), parseInt(h.slice(5,7),16)];

/* ---------- 体素 → 表面四边形（同面同色贪心合并） ---------- */
const AXES = [
  {a:0, n:[1,0,0]}, {a:0, n:[-1,0,0]},
  {a:1, n:[0,1,0]}, {a:1, n:[0,-1,0]},
  {a:2, n:[0,0,1]}, {a:2, n:[0,0,-1]}
];
function surfaceQuads(boxes){
  const grid = new Map();
  const key = (x,y,z) => x+','+y+','+z;
  let mn=[1e9,1e9,1e9], mx=[-1e9,-1e9,-1e9];
  for(const r of boxes)
    for(let x=r.x0;x<r.x1;x++)for(let y=r.y0;y<r.y1;y++)for(let z=r.z0;z<r.z1;z++){
      grid.set(key(x,y,z), r.slot);          // 后画覆盖先画
      mn=[Math.min(mn[0],x),Math.min(mn[1],y),Math.min(mn[2],z)];
      mx=[Math.max(mx[0],x+1),Math.max(mx[1],y+1),Math.max(mx[2],z+1)];
    }

  const quads=[];
  for(const {a,n} of AXES){
    const ua = a===0?1:0, va = a===2?1:2;
    for(let slice=mn[a]; slice<mx[a]; slice++){
      // 收集本层暴露面
      const faces = new Map();
      const cell=[0,0,0];
      for(let u=mn[ua]; u<mx[ua]; u++)for(let v=mn[va]; v<mx[va]; v++){
        cell[a]=slice; cell[ua]=u; cell[va]=v;
        const slot = grid.get(key(cell[0],cell[1],cell[2]));
        if(!slot) continue;
        if(grid.has(key(cell[0]+n[0], cell[1]+n[1], cell[2]+n[2]))) continue;
        faces.set(u+','+v, slot);
      }
      if(!faces.size) continue;
      const used = new Set();
      for(let v=mn[va]; v<mx[va]; v++)for(let u=mn[ua]; u<mx[ua]; u++){
        const k0=u+','+v;
        const slot0=faces.get(k0);
        if(!slot0 || used.has(k0)) continue;
        let w=1; while(faces.get((u+w)+','+v)===slot0 && !used.has((u+w)+','+v)) w++;
        let h=1;
        outer: while(v+h<mx[va]){
          for(let i=0;i<w;i++){ const k=(u+i)+','+(v+h); if(faces.get(k)!==slot0||used.has(k)) break outer; }
          h++;
        }
        for(let i=0;i<w;i++)for(let j=0;j<h;j++) used.add((u+i)+','+(v+j));
        const plane = slice + (n[a] > 0 ? 1 : 0);
        const P=(du,dv)=>{const q=[0,0,0];q[a]=plane;q[ua]=u+du;q[va]=v+dv;return q;};
        let pts=[P(0,0),P(w,0),P(w,h),P(0,h)];
        const nn=V.cross(V.sub(pts[1],pts[0]), V.sub(pts[2],pts[0]));
        if(V.dot(nn,n)<0) pts=[pts[0],pts[3],pts[2],pts[1]];
        quads.push({pts, n, slot: slot0});
      }
    }
  }
  return {quads, mn, mx};
}

/* ---------- 画布 ---------- */
function crc32(b){let c=0xffffffff;for(const x of b){c^=x;for(let k=0;k<8;k++)c=(c>>>1)^((c&1)?0xedb88320:0);}return(c^0xffffffff)>>>0;}
class Canvas{
  constructor(w,h,bg=[246,244,239]){
    this.w=w;this.h=h;this.buf=new Uint8Array(w*h*3);this.depth=new Float32Array(w*h).fill(1e9);
    for(let i=0;i<w*h;i++){this.buf[i*3]=bg[0];this.buf[i*3+1]=bg[1];this.buf[i*3+2]=bg[2];}
  }
  png(){
    const st=this.w*3+1, raw=Buffer.alloc(st*this.h);
    for(let y=0;y<this.h;y++){raw[y*st]=0;Buffer.from(this.buf.buffer, y*this.w*3, this.w*3).copy(raw, y*st+1);}
    const ck=(t,d)=>{const l=Buffer.alloc(4);l.writeUInt32BE(d.length);const b=Buffer.concat([Buffer.from(t),d]);
      const c=Buffer.alloc(4);c.writeUInt32BE(crc32(b));return Buffer.concat([l,b,c]);};
    const ih=Buffer.alloc(13);ih.writeUInt32BE(this.w,0);ih.writeUInt32BE(this.h,4);ih[8]=8;ih[9]=2;
    return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), ck('IHDR',ih), ck('IDAT',zlib.deflateSync(raw,{level:9})), ck('IEND',Buffer.alloc(0))]);
  }
}
const LIGHT=V.norm([-0.45,-0.82,0.35]), AMB=[0.66,0.69,0.62], DIF=[0.38,0.35,0.29];
const shade=(c,n)=>{const l=Math.max(0,-V.dot(n,LIGHT));return [0,1,2].map(i=>Math.min(255,Math.round(c[i]*(AMB[i]+l*DIF[i])*1.08)));};

function tri(cv,p0,p1,p2,col){
  const x0=Math.max(0,Math.floor(Math.min(p0[0],p1[0],p2[0]))), x1=Math.min(cv.w-1,Math.ceil(Math.max(p0[0],p1[0],p2[0])));
  const y0=Math.max(0,Math.floor(Math.min(p0[1],p1[1],p2[1]))), y1=Math.min(cv.h-1,Math.ceil(Math.max(p0[1],p1[1],p2[1])));
  const area=(p1[0]-p0[0])*(p2[1]-p0[1])-(p2[0]-p0[0])*(p1[1]-p0[1]);
  if(Math.abs(area)<1e-9) return;
  const inv=1/area;
  for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){
    const px=x+0.5, py=y+0.5;
    const w0=((p1[0]-px)*(p2[1]-py)-(p2[0]-px)*(p1[1]-py))*inv;
    const w1=((p2[0]-px)*(p0[1]-py)-(p0[0]-px)*(p2[1]-py))*inv;
    const w2=1-w0-w1;
    if(w0<0||w1<0||w2<0) continue;
    const z=w0*p0[2]+w1*p1[2]+w2*p2[2], di=y*cv.w+x;
    if(z>=cv.depth[di]) continue;
    cv.depth[di]=z;
    const o=di*3;
    cv.buf[o]=col[0]; cv.buf[o+1]=col[1]; cv.buf[o+2]=col[2];
  }
}
function dot(cv,p,col){
  const x=Math.round(p[0]), y=Math.round(p[1]);
  if(x<0||y<0||x>=cv.w||y>=cv.h) return;
  const o=(y*cv.w+x)*3; cv.buf[o]=col[0]; cv.buf[o+1]=col[1]; cv.buf[o+2]=col[2];
}
function line(cv,p0,p1,col){
  const n=Math.max(2,Math.ceil(Math.hypot(p1[0]-p0[0],p1[1]-p0[1])));
  for(let i=0;i<=n;i++) dot(cv,[p0[0]+(p1[0]-p0[0])*i/n, p0[1]+(p1[1]-p0[1])*i/n], col);
}

/* ---------- 相机 ---------- */
function camera(pitchDeg, target, dist){
  const p=pitchDeg*Math.PI/180;
  const eye=[target[0], target[1]+Math.sin(p)*dist, target[2]+Math.cos(p)*dist];
  const f=V.norm(V.sub(target,eye));
  const r=V.norm(V.cross(f,[0,1,0]));
  const u=V.cross(r,f);
  return {eye,f,r,u};
}
const projector=(cam,scale,cx,cy)=>p=>{
  const d=V.sub(p,cam.eye), z=V.dot(d,cam.f);
  return [cx + V.dot(d,cam.r)/z*scale, cy - V.dot(d,cam.u)/z*scale, z];
};
const yawRot=(deg,p)=>{const a=deg*Math.PI/180,c=Math.cos(a),s=Math.sin(a);return [p[0]*c+p[2]*s, p[1], -p[0]*s+p[2]*c];};

/* ---------- 位图字 ---------- */
const FONT={
 A:'01110 10001 10001 11111 10001 10001 10001',B:'11110 10001 10001 11110 10001 10001 11110',
 C:'01110 10001 10000 10000 10000 10001 01110',D:'11110 10001 10001 10001 10001 10001 11110',
 E:'11111 10000 10000 11110 10000 10000 11111',F:'11111 10000 10000 11110 10000 10000 10000',
 G:'01110 10001 10000 10111 10001 10001 01110',H:'10001 10001 10001 11111 10001 10001 10001',
 I:'01110 00100 00100 00100 00100 00100 01110',J:'00111 00010 00010 00010 00010 10010 01100',
 K:'10001 10010 10100 11000 10100 10010 10001',L:'10000 10000 10000 10000 10000 10000 11111',
 M:'10001 11011 10101 10101 10001 10001 10001',N:'10001 11001 10101 10011 10001 10001 10001',
 O:'01110 10001 10001 10001 10001 10001 01110',P:'11110 10001 10001 11110 10000 10000 10000',
 Q:'01110 10001 10001 10001 10101 10010 01101',R:'11110 10001 10001 11110 10100 10010 10001',
 S:'01111 10000 10000 01110 00001 00001 11110',T:'11111 00100 00100 00100 00100 00100 00100',
 U:'10001 10001 10001 10001 10001 10001 01110',V:'10001 10001 10001 10001 10001 01010 00100',
 W:'10001 10001 10001 10101 10101 11011 10001',X:'10001 10001 01010 00100 01010 10001 10001',
 Y:'10001 10001 01010 00100 00100 00100 00100',Z:'11111 00001 00010 00100 01000 10000 11111',
 0:'01110 10001 10011 10101 11001 10001 01110',1:'00100 01100 00100 00100 00100 00100 01110',
 2:'01110 10001 00001 00010 00100 01000 11111',3:'11111 00010 00100 00010 00001 10001 01110',
 4:'00010 00110 01010 10010 11111 00010 00010',5:'11111 10000 11110 00001 00001 10001 01110',
 6:'00110 01000 10000 11110 10001 10001 01110',7:'11111 00001 00010 00100 01000 01000 01000',
 8:'01110 10001 10001 01110 10001 10001 01110',9:'01110 10001 10001 01111 00001 00010 01100',
 '.':'00000 00000 00000 00000 00000 01100 01100','-':'00000 00000 00000 11111 00000 00000 00000',
 '/':'00001 00010 00010 00100 01000 01000 10000',':':'00000 01100 01100 00000 01100 01100 00000',
 ' ':'00000 00000 00000 00000 00000 00000 00000'
};
function text(cv,x,y,s,sc,col=[70,70,70]){
  let cx=x;
  for(const ch of s.toUpperCase()){
    const rows=(FONT[ch] ?? FONT[' ']).split(' ');
    for(let r=0;r<7;r++)for(let c=0;c<5;c++){
      if(rows[r][c] !== '1') continue;
      for(let dy=0;dy<sc;dy++)for(let dx=0;dx<sc;dx++) dot(cv,[cx+c*sc+dx, y+r*sc+dy], col);
    }
    cx += 6*sc;
  }
}
const textW=(s,sc)=>s.length*6*sc;

/* ---------- 渲染单块：四视图 ---------- */
function renderPart(name, stepTag){
  const part = PARTS[name];
  if(!part || part.boxes.length === 0) return null;
  const {quads, mn, mx} = surfaceQuads(part.boxes);
  const cells = [mx[0]-mn[0], mx[1]-mn[1], mx[2]-mn[2]];

  const W=400, H=460;
  const cv = new Canvas(W*4, H, [246,244,239]);
  const labels = ['FRONT','3/4','SIDE','BACK'];
  const yaws = [0,38,90,180];
  const ctr = [(mn[0]+mx[0])/2*UNIT, (mn[1]+mx[1])/2*UNIT, (mn[2]+mx[2])/2*UNIT];
  const halfW = Math.max(mx[0]-mn[0], mx[2]-mn[2]) * UNIT / 2;
  const halfH = (mx[1]-mn[1]) * UNIT / 2;

  labels.forEach((lab, i)=>{
    const cam = camera(6, ctr, 3.0);
    const probe = projector(cam, 1, 0, 0);
    // 画面里这个物体的像素半高 / 半宽（透视已计入）
    const topPx = Math.abs(probe([ctr[0], ctr[1]+halfH, ctr[2]])[1] - probe([ctr[0], ctr[1]-halfH, ctr[2]])[1]);
    const sidePx = Math.abs(probe([ctr[0]+halfW, ctr[1], ctr[2]])[0] - probe([ctr[0]-halfW, ctr[1], ctr[2]])[0]);
    const s = Math.min((H-190)/topPx, (W-90)/sidePx);
    const anchor = probe(ctr);
    const ox = W*i + W/2 - anchor[0]*s;
    const oy = H*0.46 - anchor[1]*s;
    const proj = projector(cam, s, ox, oy);

    for(const q of quads){
      const pts = q.pts.map(p=>proj(yawRot(yaws[i], [p[0]*UNIT, p[1]*UNIT, p[2]*UNIT])));
      const n = yawRot(yaws[i], q.n);
      const col = shade(hex2rgb(PALETTE[q.slot] ?? PALETTE.skin), n);
      tri(cv, pts[0], pts[1], pts[2], col);
      tri(cv, pts[0], pts[2], pts[3], col);
    }
    text(cv, W*i + W/2 - textW(lab,3)/2, H-54, lab, 3, [60,60,60]);
  });

  text(cv, 18, 16, 'BASE MODEL  /  '+stepTag+'  /  '+part.label.toUpperCase(), 3, [40,40,40]);
  text(cv, 18, 50, 'VOXEL ONLY  -  NO CLOTHES, NO HAT, NO FACE  -  '+cells.join(' X ')+' VOXELS AT 1/32  -  '+quads.length+' QUADS', 2, [125,120,112]);

  const file = 'step-'+name+'.png';
  fs.writeFileSync(path.join(outDir, file), cv.png());
  return {file, cells, quads:quads.length, mn, mx};
}


/* ---------- 正交格线图：直接标注本体素模型的格数 ---------- */
function renderPartGrid(name){
  const part = PARTS[name];
  if(!part || part.boxes.length === 0) return null;
  const {quads, mn, mx} = surfaceQuads(part.boxes);

  const CELL = 26;
  const ROWS = 34, COLS = 34;
  const PAD_L = 76, PAD_T = 96, PAD_B = 92, PAD_R = 40;
  const PW = COLS*CELL, PH = ROWS*CELL;
  const views = [
    {label:'正 视  FRONT', yaw:0,   axis:'x', sign: 1},
    {label:'侧 视  SIDE',  yaw:90,  axis:'z', sign:-1},
    {label:'背 视  BACK',  yaw:180, axis:'x', sign:-1}
  ];
  const W = PAD_L + views.length*(PW+30) + PAD_R;
  const H = PAD_T + PH + PAD_B;
  const cv = new Canvas(W, H, [252,251,248]);
  const GRID=(40,80,155), GRIDF=(120,145,200), RED=(214,86,64), INK=(35,35,35), MUTE=(140,140,138);

  text(cv, 20, 16, 'MY VOXEL HEAD - GRID ANNOTATION  /  1 CELL = 1/32 TILE', 3, [30,30,30]);
  text(cv, 20, 50, 'LEFT NUMBERS = HEIGHT (Y) CELLS   /   BOTTOM NUMBERS = SIDE CELLS (X OR Z)   /   CENTER RED LINE = 0', 2, [120,118,112]);

  views.forEach((vw,i)=>{
    const ox = PAD_L + i*(PW+30);
    const oy = PAD_T;
    const cx = ox + PW/2;
    const cy = oy + PH;

    // 网格
    for(let g=0; g<=ROWS; g++){
      const y = cy - g*CELL;
      const major = g%4===0;
      for(let x=ox; x<ox+PW; x++) dot(cv,[x,y], major?GRID:GRIDF);
      if(g%4===0 && g<=32){ const s=String(g); text(cv, ox-8-5*s.length*2, y-7, s, 2, GRID); }
    }
    for(let k=0; k<=COLS; k++){
      const x = cx + (k-COLS/2)*CELL;
      if(x<ox||x>ox+PW) continue;
      const kk = k-COLS/2;
      const major = kk%4===0;
      for(let y=oy; y<oy+PH; y++) dot(cv,[x,y], major?GRID:GRIDF);
      if(major && kk!==0){ const s=String(kk); text(cv, x-3*s.length*2, oy+PH+14, s, 2, GRID); }
    }
    for(let y=oy; y<oy+PH; y++) dot(cv,[cx,y], RED);
    text(cv, cx-3, oy+PH+14, '0', 2, RED);

    // 模型（正交投影，格线严格对齐）
    const scale = CELL/UNIT;   // 一格 = CELL 像素
    const camYaw = vw.yaw*Math.PI/180;
    const cy2 = Math.cos(camYaw), sy2 = Math.sin(camYaw);
    const depth = new Float32Array(PW*PH).fill(1e9);
    const projP = q=>{
      const rx = q[0]*cy2 - q[2]*sy2;
      const rz = q[0]*sy2 + q[2]*cy2;
      const X = cx + rx*scale;
      const Y = cy - q[1]*scale;
      return [X - ox, Y - oy, rz];
    };
    for(const q of quads){
      const pts = q.pts.map(pp=>projP([pp[0]*UNIT, pp[1]*UNIT, pp[2]*UNIT]));
      const n = [q.n[0]*cy2 - q.n[2]*sy2, q.n[1], q.n[0]*sy2 + q.n[2]*cy2];
      const col = shade(hex2rgb(PALETTE[q.slot] ?? PALETTE.skin), n);
      for(const triIdx of [[0,1,2],[0,2,3]]){
        const a=pts[triIdx[0]], b=pts[triIdx[1]], c=pts[triIdx[2]];
        const x0=Math.max(0,Math.floor(Math.min(a[0],b[0],c[0]))), x1=Math.min(PW-1,Math.ceil(Math.max(a[0],b[0],c[0])));
        const y0=Math.max(0,Math.floor(Math.min(a[1],b[1],c[1]))), y1=Math.min(PH-1,Math.ceil(Math.max(a[1],b[1],c[1])));
        const area=(b[0]-a[0])*(c[1]-a[1])-(c[0]-a[0])*(b[1]-a[1]);
        if(Math.abs(area)<1e-9) continue;
        const inv=1/area;
        for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){
          const px=x+0.5, py=y+0.5;
          const w0=((b[0]-px)*(c[1]-py)-(c[0]-px)*(b[1]-py))*inv;
          const w1=((c[0]-px)*(a[1]-py)-(a[0]-px)*(c[1]-py))*inv;
          const w2=1-w0-w1;
          if(w0<-1e-9||w1<-1e-9||w2<-1e-9) continue;
          const z=w0*a[2]+w1*b[2]+w2*c[2];
          const di=y*PW+x;
          if(-z < depth[di]){
            depth[di]=-z;
            const gx=ox+x, gy=oy+y, o=(gy*cv.w+gx)*3;
            cv.buf[o]=col[0]; cv.buf[o+1]=col[1]; cv.buf[o+2]=col[2];
          }
        }
      }
    }
    const lab = vw.label;
    text(cv, ox + PW/2 - lab.length*6*2/2, oy+PH+34, lab, 2, INK);
  });

  text(cv, 20, H-34, 'CELLS: '+[mx[0]-mn[0],mx[1]-mn[1],mx[2]-mn[2]].join(' X ')+'   /   Y RANGE '+mn[1]+'-'+mx[1]+'   /   X '+mn[0]+'..'+mx[0]+'   /   Z '+mn[2]+'..'+mx[2], 2, MUTE);

  fs.writeFileSync(path.join(outDir,'grid-'+name+'.png'), cv.png());
  return {mn,mx};
}

const results=[];
for(const name of PART_ORDER){
  const r = renderPart(name, 'STEP 1  BASE');
  if(r) results.push([name, r]);
}
for(const [name, r] of results) console.log(name+': '+r.cells.join(' x ')+' vox  |  '+r.quads+' quads  |  y '+r.mn[1]+'-'+r.mx[1]);
if(!results.length) console.log('no parts defined yet');
const g = renderPartGrid('head');
if(g) console.log('grid sheet -> review/character/grid-head.png');