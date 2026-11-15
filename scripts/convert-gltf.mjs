/** Offline conversion of our existing static glTF models into OBJ + one palette PNG for MGCB. */
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url));
const selection=JSON.parse(fs.readFileSync(path.join(root,'assets/selection.json'),'utf8').replace(/^\uFEFF/,''));
const enabled=new Set(selection.enabled);
const output=path.join(root,'src/Town.Content/Generated');fs.mkdirSync(output,{recursive:true});
const colors=[],colorIds=new Map(),assets=[];
function colorId(color){const key=color.map(v=>Math.max(0,Math.min(255,Math.round(v*255)))).join(',');if(!colorIds.has(key)){colorIds.set(key,colors.length);colors.push(key.split(',').map(Number));}return colorIds.get(key);}
function accessor(gltf,buffers,id){
 const a=gltf.accessors[id],v=gltf.bufferViews[a.bufferView],components={SCALAR:1,VEC2:2,VEC3:3,VEC4:4}[a.type];
 const bytes={5126:4,5125:4,5123:2,5121:1}[a.componentType],read={5126:'getFloat32',5125:'getUint32',5123:'getUint16',5121:'getUint8'}[a.componentType];
 if(!components||!bytes||a.normalized)throw Error('Unsupported accessor; use an explicit converter for this model.');
 const view=new DataView(buffers[v.buffer]);return Array.from({length:a.count},(_,i)=>Array.from({length:components},(_,j)=>view[read]((v.byteOffset??0)+(a.byteOffset??0)+i*(v.byteStride??components*bytes)+j*bytes,true)));
}
for(const file of fs.readdirSync(path.join(root,'assets/models')).filter(n=>n.endsWith('.gltf')).sort()){
 if(!enabled.has(file.slice(0,-5)))continue;
 const bytes=fs.readFileSync(path.join(root,'assets/models',file)),gltf=JSON.parse(bytes);
 if(gltf.meshes.length!==1||gltf.nodes.some(n=>n.matrix||n.translation||n.rotation||n.scale)||gltf.skins)throw Error(`Unsupported transform/skin in ${file}; conversion must be explicit.`);
 const buffers=gltf.buffers.map(b=>{if(!b.uri.startsWith('data:application/octet-stream;base64,'))throw Error('Only embedded project assets are accepted.');const b64=Buffer.from(b.uri.split(',')[1],'base64');return b64.buffer.slice(b64.byteOffset,b64.byteOffset+b64.byteLength);});
 const vertices=[],indices=[];
 for(const primitive of gltf.meshes[0].primitives){
  if((primitive.mode??4)!==4)throw Error('Only triangle primitives are supported.');
  const p=accessor(gltf,buffers,primitive.attributes.POSITION),n=accessor(gltf,buffers,primitive.attributes.NORMAL);
  const c=primitive.attributes.COLOR_0===undefined?null:accessor(gltf,buffers,primitive.attributes.COLOR_0);
  const pbr=gltf.materials?.[primitive.material]?.pbrMetallicRoughness;
  const factor=pbr?.baseColorFactor??[1,1,1,1],offset=vertices.length;
  const image=pbr?.baseColorTexture===undefined?null:gltf.images[gltf.textures[pbr.baseColorTexture.index].source].uri;
  const texture=image?path.basename(image):null;
  if(texture)fs.copyFileSync(path.join(root,'assets/textures',texture),path.join(output,texture));
  const uv=texture?accessor(gltf,buffers,primitive.attributes.TEXCOORD_0):null;
  for(let i=0;i<p.length;i++)vertices.push({p:p[i],n:n[i],color:colorId((c?.[i]??[1,1,1,1]).slice(0,3).map((v,j)=>v*factor[j])),texture,uv:uv?.[i]});
  for(const [i]of accessor(gltf,buffers,primitive.indices))indices.push(i+offset);
 }
 assets.push({id:file.slice(0,-5),vertices,indices,sourceBytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),footprint:gltf.nodes[0].extras?.footprint??[1,1]});
}
function crc32(buffer){let crc=0xffffffff;for(const byte of buffer){crc^=byte;for(let j=0;j<8;j++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return(crc^0xffffffff)>>>0;}
function chunk(type,data){const tag=Buffer.from(type),length=Buffer.alloc(4),crc=Buffer.alloc(4);length.writeUInt32BE(data.length);crc.writeUInt32BE(crc32(Buffer.concat([tag,data])));return Buffer.concat([length,tag,data,crc]);}
const width=256,height=Math.ceil(colors.length/width),raw=Buffer.alloc(height*(width*4+1));
for(let i=0;i<width*height;i++){const offset=Math.floor(i/width)*(width*4+1)+1+(i%width)*4;const c=colors[i]??[255,255,255];raw.set([...c,255],offset);}
const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(width,0);ihdr.writeUInt32BE(height,4);ihdr[8]=8;ihdr[9]=6;
fs.writeFileSync(path.join(output,'palette.png'),Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',ihdr),chunk('IDAT',zlib.deflateSync(raw)),chunk('IEND',Buffer.alloc(0))]));
let materialText='newmtl Palette\nKa 1 1 1\nKd 1 1 1\nKs 0 0 0\nd 1\nillum 1\nmap_Kd palette.png\n';
for(const texture of new Set(assets.flatMap(a=>a.vertices.map(v=>v.texture).filter(Boolean))))materialText+=`\nnewmtl ${texture}\nKd 1 1 1\nd 1\nillum 1\nmap_Kd ${texture}\n`;
fs.writeFileSync(path.join(output,'palette.mtl'),materialText);
const format=n=>Number(n.toFixed(7)).toString();
for(const a of assets){
 const lines=['# Converted offline from local Town Evolution glTF; Y up, +Z front.','mtllib palette.mtl',`o ${a.id}`];
 for(const v of a.vertices)lines.push(`v ${v.p.map(format).join(' ')}`);
 for(const v of a.vertices)lines.push(`vn ${v.n.map(format).join(' ')}`);
 // Assimp drops a constant UV channel as invalid data. Sub-texel variation retains it without changing palette sampling.
 for(const [i,v] of a.vertices.entries()){const epsilon=(i%3)*.000001;lines.push(v.texture?`vt ${(v.uv[0]+epsilon).toFixed(8)} ${(1-v.uv[1]+epsilon).toFixed(8)}`:`vt ${((v.color%width+.5)/width+epsilon).toFixed(8)} ${(1-(Math.floor(v.color/width)+.5)/height+epsilon).toFixed(8)}`);}
 let current=null;for(let i=0;i<a.indices.length;i+=3){const mat=a.vertices[a.indices[i]].texture??'Palette';if(mat!==current){lines.push('usemtl '+mat);current=mat;}lines.push('f '+a.indices.slice(i,i+3).map(v=>`${v+1}/${v+1}/${v+1}`).join(' '));}
 fs.writeFileSync(path.join(output,a.id+'.obj'),lines.join('\n')+'\n');
}
// Retain the same source triangles for precise interaction; no GPU readback is needed on Android.
const collision=[];
function u32(n){const b=Buffer.alloc(4);b.writeUInt32LE(n);return b;}
collision.push(u32(assets.length));
for(const a of assets){const id=Buffer.from(a.id);const data=Buffer.alloc(a.vertices.length*12+a.indices.length*4);let offset=0;for(const v of a.vertices)for(const f of v.p){data.writeFloatLE(f,offset);offset+=4;}for(const i of a.indices){data.writeUInt32LE(i,offset);offset+=4;}collision.push(u32(id.length),id,u32(a.vertices.length),u32(a.indices.length),data);}
fs.writeFileSync(path.join(output,'picking.bin'),Buffer.concat(collision));
const mgcb=['# Generated by scripts/convert-gltf.mjs; compile via official MonoGame Content Builder.','/outputDir:bin/$(Platform)','/intermediateDir:obj/$(Platform)','/platform:DesktopGL','/config:','/profile:HiDef','/compress:False','', '#begin Effects/Instanced.fx','/importer:EffectImporter','/processor:EffectProcessor','/build:Effects/Instanced.fx;Effects/Instanced.xnb','', '#begin Generated/palette.png','/importer:TextureImporter','/processor:TextureProcessor','/processorParam:ColorKeyEnabled=False','/processorParam:GenerateMipmaps=False','/processorParam:PremultiplyAlpha=False','/processorParam:TextureFormat=Color','/build:Generated/palette.png;palette.xnb'];
for(const a of assets)mgcb.push('',`#begin Generated/${a.id}.obj`,'/importer:FbxImporter','/processor:ModelProcessor','/processorParam:ColorKeyEnabled=False','/processorParam:GenerateMipmaps=False','/processorParam:GenerateTangentFrames=False','/processorParam:PremultiplyVertexColors=False','/processorParam:TextureFormat=Color',`/build:Generated/${a.id}.obj;Models/${a.id}.xnb`);
mgcb.push('','#begin Generated/ui-font.png','/importer:TextureImporter','/processor:TextureProcessor','/processorParam:ColorKeyEnabled=False','/processorParam:GenerateMipmaps=False','/processorParam:PremultiplyAlpha=True','/processorParam:TextureFormat=Color','/build:Generated/ui-font.png;ui-font.xnb');
for(const name of fs.readdirSync(path.join(root,'src/Town.Content/Ui')).filter(n=>n.endsWith('.png')).sort())mgcb.push('',`#begin Ui/${name}`,'/importer:TextureImporter','/processor:TextureProcessor','/processorParam:PremultiplyAlpha=True',`/build:Ui/${name};Ui/${name.slice(0,-4)}.xnb`);
fs.writeFileSync(path.join(root,'src/Town.Content/Content.mgcb'),mgcb.join('\n')+'\n');
fs.writeFileSync(path.join(root,'assets/manifest.json'),JSON.stringify({schema:2,source:'../town-evolution/art/exports',pipeline:'glTF → OBJ + palette PNG → official MGCB → Model/Texture2D XNB',paletteColors:colors.length,assets:assets.map(({vertices,indices,...rest})=>({...rest,vertices:vertices.length,triangles:indices.length/3}))},null,2)+'\n');
console.log(`Converted ${assets.length} local glTF models; ${colors.length} palette colors; no network.`);
