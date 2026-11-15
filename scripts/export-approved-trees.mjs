import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url)),old=path.resolve(root,'../town-evolution');
const catalog=JSON.parse(fs.readFileSync(path.join(old,'assets/resources/asset-lab/models.json'),'utf8'));
const textures=path.join(root,'assets/textures');fs.mkdirSync(textures,{recursive:true});
function rotate(v,angles){let [x,y,z]=v;const [rx,ry,rz]=angles.map(a=>a*Math.PI/180);[x,y]=[x*Math.cos(rz)-y*Math.sin(rz),x*Math.sin(rz)+y*Math.cos(rz)];[y,z]=[y*Math.cos(rx)-z*Math.sin(rx),y*Math.sin(rx)+z*Math.cos(rx)];return [x*Math.cos(ry)+z*Math.sin(ry),y,-x*Math.sin(ry)+z*Math.cos(ry)];}
for(const [source,seed,id]of [['NormalTree_1',7,'approved-tree-7'],['PineTree_1',8,'approved-pine-8']]){
 const gltf={asset:{version:'2.0',generator:'Offline export of explicitly adopted tree variant'},scene:0,scenes:[{nodes:[0]}],nodes:[{mesh:0,extras:{source,leafSeed:seed}}],meshes:[{primitives:[]}],buffers:[],bufferViews:[],accessors:[],materials:[],textures:[],images:[]},chunks=[];let offset=0;
 const add=(values,components,type=5126)=>{const array=type===5125?new Uint32Array(values):new Float32Array(values),bytes=Buffer.from(array.buffer);const view=gltf.bufferViews.push({buffer:0,byteOffset:offset,byteLength:bytes.length})-1;chunks.push(bytes);offset+=bytes.length;return gltf.accessors.push({bufferView:view,componentType:type,count:values.length/components,type:{1:'SCALAR',2:'VEC2',3:'VEC3',4:'VEC4'}[components]})-1;};
 for(const [partIndex,part]of catalog.models[source].parts.entries()){
  const mesh={positions:[...part.mesh.positions],normals:[...part.mesh.normals],colors:[...part.mesh.colors],uvs:[...(part.mesh.uvs??[])],indices:[...part.mesh.indices]};
  if(part.texture?.includes('Leaves')){
   let state=(seed+partIndex*101)>>>0;const random=()=>{state=(Math.imul(state,1664525)+1013904223)>>>0;return state/4294967296;};
   for(let copy=0;copy<2;copy++){
    const angles=[(random()-.5)*8,70+copy*135+random()*40,(random()-.5)*8],scale=.9+random()*.13,sy=.96+random()*.06,dx=(random()-.5)*.1,dz=(random()-.5)*.1,base=mesh.positions.length/3;
    for(let i=0;i<part.mesh.positions.length;i+=3){const p=rotate([part.mesh.positions[i]*scale,part.mesh.positions[i+1]*sy,part.mesh.positions[i+2]*scale],angles);mesh.positions.push(p[0]+dx,p[1],p[2]+dz);const n=rotate([part.mesh.normals[i]/scale,part.mesh.normals[i+1]/sy,part.mesh.normals[i+2]/scale],angles),length=Math.hypot(...n);mesh.normals.push(...n.map(v=>v/length));}
    mesh.colors.push(...part.mesh.colors);mesh.uvs.push(...part.mesh.uvs);mesh.indices.push(...part.mesh.indices.map(i=>i+base));
   }
  }
  let material=gltf.materials.length;const pbr={baseColorFactor:[1,1,1,1],metallicFactor:0,roughnessFactor:1};
  if(part.texture){const texture=gltf.textures.length;gltf.images.push({uri:'../textures/'+part.texture});gltf.textures.push({source:gltf.images.length-1});pbr.baseColorTexture={index:texture};fs.copyFileSync(path.join(old,'assets/resources/asset-lab',part.texture),path.join(textures,part.texture));}
  gltf.materials.push({name:part.texture??'Trunk',pbrMetallicRoughness:pbr,doubleSided:true,alphaMode:part.texture?'MASK':'OPAQUE',alphaCutoff:.45});
  const attributes={POSITION:add(mesh.positions,3),NORMAL:add(mesh.normals,3),COLOR_0:add(mesh.colors,4)};if(mesh.uvs.length)attributes.TEXCOORD_0=add(mesh.uvs,2);
  gltf.meshes[0].primitives.push({attributes,indices:add(mesh.indices,1,5125),material});
 }
 gltf.buffers=[{byteLength:offset,uri:'data:application/octet-stream;base64,'+Buffer.concat(chunks).toString('base64')}];fs.writeFileSync(path.join(root,'assets/models',id+'.gltf'),JSON.stringify(gltf));
}
const selection=JSON.parse(fs.readFileSync(path.join(root,'assets/selection.json'),'utf8').replace(/^\uFEFF/,''));selection.enabled=[...new Set([...selection.enabled,'approved-tree-7','approved-pine-8'])];selection.pendingTrees=[];fs.writeFileSync(path.join(root,'assets/selection.json'),JSON.stringify(selection,null,2)+'\n');
fs.copyFileSync(path.join(old,'art/third-party/quaternius/nature/License.txt'),path.join(textures,'QUATERNIUS-LICENSE.txt'));
console.log('Exported only NormalTree_1 seed 7 and PineTree_1 seed 8, using existing local textures.');
