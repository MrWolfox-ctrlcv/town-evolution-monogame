/** Preserve authored assembly parts from local canonical TypeScript data; no Cocos runtime required. */
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),old=path.resolve(root,'../town-evolution');
const ts=require(path.join(old,'node_modules/typescript'));
require.extensions['.ts']=(module,filename)=>{
  const source=fs.readFileSync(filename,'utf8');
  const compiled=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020}}).outputText;
  module._compile(compiled,filename);
};
const {ASSETS}=require(path.join(old,'assets/scripts/art/AssetCatalog.ts'));
const modules=Object.fromEntries(Object.entries(ASSETS).filter(([id])=>id.startsWith('module-')).map(([id,a])=>[id,{footprint:a.footprint,parts:a.parts}]));
const ids=['house','warehouse-base','warehouse-expanded','warehouse-transport','mill','bakery','stone-oven','crate','order-board','stone-mill','shop','residence','handcart','boat','wooden-pier','compost-barrel','nursery-empty','nursery-seeded','nursery-growing','nursery-ready','farm-cell-bed','farm-cell-bed-water','farm-with-box','farm-with-box-water'];
const buildings=Object.fromEntries(ids.map(id=>{const a=ASSETS[id];return[id,{footprint:a.footprint,sockets:a.sockets??{},parts:a.parts.map((p,i)=>({partId:`${id}/${i}/${p.name}`,...p,sharedModules:Object.keys(modules).filter(key=>modules[key].parts.some(candidate=>JSON.stringify(candidate)===JSON.stringify(p)))}))}];}));
const sourceFiles=fs.readdirSync(path.join(old,'assets/scripts/art')).filter(name=>name.endsWith('.ts')).sort().map(name=>({path:'town-evolution/assets/scripts/art/'+name,sha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(old,'assets/scripts/art',name))).digest('hex')}));
fs.writeFileSync(path.join(root,'assets/composition.json'),JSON.stringify({schema:1,purpose:'Canonical material/part identity for subsequent entity assembly. Assembly rules remain separate from model geometry.',sourceFiles,modules,buildings},null,2)+'\n');
console.log(`Preserved ${ids.length} existing designs and ${Object.keys(modules).length} shared modules.`);
