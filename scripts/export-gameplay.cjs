/** Copy adopted assets and the active original recipe text; no engine and no download. */
const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..'),old=path.resolve(root,'../town-evolution');
const ts=require(path.join(old,'node_modules/typescript'));
require.extensions['.ts']=(m,f)=>m._compile(ts.transpileModule(fs.readFileSync(f,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020}}).outputText,f);
const supported=new Set(['gather-wood','build-order-board','build-crate','build-house','build-warehouse','build-shop','expand-warehouse','dig-farm','irrigate-farm','gather-soil','gather-stone','water-soil','build-residence','build-compost-barrel','make-fertilizer','roast-carrot','attach-farm-box','build-nursery','grow-sapling','plant-sapling','grow-rice','build-stone-mill','build-mill','mill-rice','mill-wheat','build-stone-oven','build-bakery','bake-bread','build-handcart','build-boat','build-wooden-pier','build-transport-warehouse']);
const {ACTIVE_RECIPE_DEFINITIONS}=require(path.join(old,'assets/scripts/domain/recipes.ts'));
const recipes=ACTIVE_RECIPE_DEFINITIONS.filter(r=>supported.has(r.id));
fs.mkdirSync(path.join(root,'src/Town.Core/Data'),{recursive:true});fs.writeFileSync(path.join(root,'src/Town.Core/Data/recipes.json'),JSON.stringify(recipes,null,2)+'\n');
const {ASSETS}=require(path.join(old,'assets/scripts/art/AssetCatalog.ts'));
const ids=['order-board','stone-mill','shop','warehouse-expanded','residence','handcart','boat','wooden-pier','warehouse-transport','compost-barrel','nursery-empty','nursery-seeded','nursery-growing','nursery-ready','farm-cell-bed-water','farm-with-box','farm-with-box-water'];
for(const crop of ['vegetable','wheat','lettuce','rice'])for(const stage of ['seedling','young','ready'])ids.push(`crop-cell-${crop}-${stage}`);
for(const item of ['vegetable','wheat','lettuce','rice','wood','stone','soil','mud','treeSeed','sapling','flour','milledRice','bread','roastedCarrot'])for(let i=1;i<=(['wood','stone'].includes(item)?6:item==='mud'?9:['vegetable','wheat','lettuce','rice'].includes(item)?10:5);i++)ids.push(`pile-${item}-${i}`);
const selectionPath=path.join(root,'assets/selection.json'),selection=JSON.parse(fs.readFileSync(selectionPath,'utf8'));
for(const id of ids){if(!ASSETS[id])throw Error('Asset is not canonical: '+id);const source=path.join(old,'art/exports',id+'.gltf');if(!fs.existsSync(source))throw Error('Missing existing export: '+id);fs.copyFileSync(source,path.join(root,'assets/models',id+'.gltf'));if(!selection.enabled.includes(id))selection.enabled.push(id);}
fs.writeFileSync(selectionPath,JSON.stringify(selection,null,2)+'\n');
console.log(`Copied ${ids.length} canonical existing assets; exported ${recipes.length} supported recipe descriptions.`);
