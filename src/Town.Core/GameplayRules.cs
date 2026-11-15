namespace Town.Core;
public sealed partial class World
{
    private sealed class RuleFailure(string message):Exception(message);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid,string reason){if(!valid)throw new RuleFailure(reason);}
    private void Discover(string recipe){if(RecipeBook.All.Any(r=>r.Id==recipe))play.Discoveries[recipe]=play.Discoveries.GetValueOrDefault(recipe)+1;}
    private void Take(string item,int amount){Require(ItemNames.ContainsKey(item)&&amount>0&&Stock(item)>=amount,"库存材料不足");play.Stock[item]=Stock(item)-amount;}
    private void Gain(string item,int amount){Require(ItemNames.ContainsKey(item)&&amount>0&&UsedStorage+amount<=Capacity,"仓库空间不足，物品仍保留原处");play.Stock[item]=Stock(item)+amount;}
    private PlacedObject Building(int id)=>byId.TryGetValue(id,out var item)?item:throw new RuleFailure("目标建筑已变化");
    private void RemoveBuilding(PlacedObject item)
    {
        objects.Remove(item);byId.Remove(item.Id);foreach(var p in Cells(item))occupancy[Index(p.X,p.Z)]=0;
        play.Farms.RemoveAll(f=>f.Id==item.Id);play.Jobs.RemoveAll(j=>j.Id==item.Id);play.Attachments.RemoveAll(a=>a.Id==item.Id);
    }
    private void ReplaceBuilding(PlacedObject original,string kind)
    {
        Require(Catalog.ContainsKey(kind),"建筑类型尚未开放");
        var farm=Farm(original.Id);var attachments=play.Attachments.Find(a=>a.Id==original.Id);RemoveBuilding(original);
        var valid=CanPlace(kind,original.X,original.Z,original.Rotation);Require(valid.Ok,valid.Message);
        AddObject(original with{Kind=kind});if(kind=="farm"&&farm is not null)play.Farms.Add(farm);if(attachments is not null)play.Attachments.Add(attachments);
    }
    private PlacedObject MakeBuilding(string kind,int x,int z,int rotation=0)
    {
        Require(nextId<1_000_000_000&&objects.Count<400,"已达物件上限");var valid=CanPlace(kind,x,z,rotation);Require(valid.Ok,valid.Message);
        var b=new PlacedObject(nextId++,kind,x,z,rotation);AddObject(b);if(kind=="farm")play.Farms.Add(new(b.Id,false,new CropCell?[9]));return b;
    }
    private void EditableGround(int x,int z)
    {
        Require(Inside(x,z),"超出地图");Require(Occupant(x,z)is null&&!HasNature(x,z),"地面被占用");Require(!play.Piles.Any(p=>p.X==x&&p.Z==z)&&!play.Drops.Any(d=>d.X==x&&d.Z==z),"先收集地面物品");
    }
    private void FreeGround(int x,int z,bool allowPile=false)
    {
        Require(LooseGround(x,z)&&!HasNature(x,z),"需要没有建筑或自然物件的陆地");
        Require(!play.Drops.Any(d=>d.X==x&&d.Z==z),"请先拾取地上的掉落物");
        Require(allowPile||!play.Piles.Any(p=>p.X==x&&p.Z==z),"地面已有材料堆");
    }
    private void DropNear(string item,int amount,int x,int z,bool floating=false,bool skipOrigin=false)
    {
        Require(play.Drops.Count+amount<=500&&nextId<1_000_000_000-amount,"掉落物过多，请先收集");
        for(int n=0;n<amount;n++)
        {
            bool placed=false;
            for(int radius=skipOrigin?1:0;radius<=5&&!placed;radius++)for(int dz=-radius;dz<=radius&&!placed;dz++)for(int dx=-radius;dx<=radius&&!placed;dx++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;int a=x+dx,b=z+dz;
                if(!(floating?DropGround(a,b):LooseGround(a,b))||HasNature(a,b)||play.Piles.Any(p=>p.X==a&&p.Z==b))continue;
                for(int slot=0;slot<9&&!placed;slot++)
                {
                    float ox=.2f+slot%3*.3f,oz=.2f+slot/3*.3f;
                    if(play.Drops.Any(d=>d.X==a&&d.Z==b&&d.OffsetX==ox&&d.OffsetZ==oz))continue;
                    play.Drops.Add(new(nextId++,item,1,a,b,ox,oz));placed=true;
                }
            }
            Require(placed,"附近没有足够掉落空间，操作未消耗材料");
        }
    }
    private void CheckSources(WorldCommand c)
    {
        if(c.ExpectedStock>=0)Require(Stock(c.Item)==c.ExpectedStock,"库存已变化，请重新拖动");
        if(c.Piles is not null){Require(c.Piles.Length<=20&&c.Piles.Select(p=>p.Id).Distinct().Count()==c.Piles.Length,"材料来源重复");foreach(var p in c.Piles)Require(play.Piles.Contains(p),"材料堆已变化，请重新拖动");}
        if(c.Entities is not null){Require(c.Entities.Length<=3&&c.Entities.Select(p=>p.Id).Distinct().Count()==c.Entities.Length,"部件重复");foreach(var p in c.Entities)Require(objects.Contains(p),"建筑部件已变化，请重新拖动");}
    }
    private void ConsumePile(GroundPile pile,int amount)
    {
        Require(amount>0&&pile.Amount>=amount,"地面材料不足");play.Piles.Remove(pile);if(pile.Amount>amount)play.Piles.Add(pile with{Amount=pile.Amount-amount});
    }
    private void PutPile(string item,int amount,int x,int z)
    {
        Require(item!="fertilizer","肥料用途尚待设计，保留在库存");
        FreeGround(x,z,true);Require(ItemNames.ContainsKey(item)&&amount>0,"物品无效");var target=play.Piles.Find(p=>p.X==x&&p.Z==z);
        Require(target is null||target.Item==item,"每格只能堆同一种物品");Require((target?.Amount??0)+amount<=StackLimit(item),"这格材料堆已满");
        if(target is not null){play.Piles.Remove(target);play.Piles.Add(target with{Amount=target.Amount+amount});}else{Require(nextId<1_000_000_000&&play.Piles.Count<400,"地面材料堆已达上限");play.Piles.Add(new(nextId++,item,amount,x,z));}
    }
    private void Attach(PlacedObject b,string item)
    {
        Require(ItemNames.ContainsKey(item),"物品无效");var attachment=play.Attachments.Find(a=>a.Id==b.Id)??new Attachment(b.Id,[]);
        Require(b.Kind!="compostBarrel"||IsCrop(item)||item=="treeSeed","木桶只接受作物或树种");
        Require(attachment.Stock.Values.Sum()<(b.Kind=="compostBarrel"?16:5),"建筑挂载材料已满");attachment.Stock[item]=attachment.Stock.GetValueOrDefault(item)+1;
        if(!play.Attachments.Any(a=>a.Id==b.Id))play.Attachments.Add(attachment);
        if(b.Kind=="compostBarrel"&&!play.Jobs.Any(j=>j.Id==b.Id))
        {
            var pool=attachment.Stock.Where(p=>IsCrop(p.Key)||p.Key=="treeSeed").Sum(p=>p.Value);
            if(pool>=2){int remaining=2;foreach(var key in attachment.Stock.Keys.ToArray()){if(!IsCrop(key)&&key!="treeSeed")continue;int n=Math.Min(remaining,attachment.Stock[key]);attachment.Stock[key]-=n;remaining-=n;if(remaining==0)break;}play.Jobs.Add(new(b.Id,"make-fertilizer",play.Clock+300));}
        }
    }
    private void PlantCell(PlacedObject b,int cell,string crop)
    {
        var farm=Farm(b.Id);Require(b.Kind=="farm"&&farm is not null&&cell is >=0 and <9,"请拖到田地的小格");
        Require(IsCrop(crop)&&(crop=="rice")==farm!.Irrigated,"旱田种胡萝卜、小麦、生菜；水田种稻谷");Require(farm.Cells[cell]is null,"这个小格已有作物");
        Take(crop,1);var cells=farm.Cells.ToArray();cells[cell]=new(crop,play.Clock+300);play.Farms[play.Farms.IndexOf(farm)]=farm with{Cells=cells};
    }
    private void HarvestCell(PlacedObject b,int cell)
    {
        var farm=Farm(b.Id);Require(farm is not null&&cell is >=0 and <9,"请把镰刀划过作物小格");var crop=farm!.Cells[cell];Require(crop is not null,"这个小格没有作物");Require(crop!.ReadyTick<=play.Clock,"此小格作物尚未成熟");
        if(farm.Box){var stock=farm.BoxStock is null?new Dictionary<string,int>():new(farm.BoxStock);Require(stock.Values.Sum()+crop.Yield<=16,"田边收获箱已满，作物保留");stock[crop.Crop]=stock.GetValueOrDefault(crop.Crop)+crop.Yield;farm=farm with{BoxStock=stock};}
        else DropNear(crop.Crop,crop.Yield,b.X,b.Z);
        var cells=farm.Cells.ToArray();cells[cell]=null;play.Farms[play.Farms.FindIndex(f=>f.Id==b.Id)]=farm with{Cells=cells};if(crop.Crop=="rice")Discover("grow-rice");
    }
    private void CraftPile(string tool,int x,int z)
    {
        var pile=play.Piles.Find(p=>p.X==x&&p.Z==z);Require(pile is not null,"未选中地面材料");
        var rule=(tool,pile!.Item,pile.Amount) switch{
            ("hammer","wood",1)=>("orderBoard","build-order-board"),("hammer","wood",2)=>("crate","build-crate"),("hammer","wood",3)=>("woodenPier","build-wooden-pier"),("hammer","wood",4)=>("house","build-house"),
            ("hammer","stone",2)=>("stoneMill","build-stone-mill"),("saw","wood",3)=>("handcart","build-handcart"),("saw","wood",4)=>("boat","build-boat"),_=> ("","")};
        Require(rule.Item1!="",tool=="saw"?"锯子需要恰好3或4份木材":"锤子：1木公告牌、2木箱子、3木栈桥、4木房屋、2石石磨");
        play.Piles.Remove(pile);var(w,d)=Footprint(rule.Item1,0);
        for(int dz=0;dz<d;dz++)for(int dx=0;dx<w;dx++)if(CanPlace(rule.Item1,x-dx,z-dz,0).Ok){MakeBuilding(rule.Item1,x-dx,z-dz);Discover(rule.Item2);return;}
        throw new RuleFailure("合成区域需要完整空地，材料已保留");
    }
    private void HammerBuilding(PlacedObject b)
    {
        Require(!play.Jobs.Any(j=>j.Id==b.Id),"请先领取加工产物");var attached=play.Attachments.Find(a=>a.Id==b.Id);int Amount(string item)=>attached?.Stock.GetValueOrDefault(item)??0;
        if(b.Kind=="crate"&&Amount("mud")==0){Require(attached is null||attached.Stock.Values.Sum()==0,"请先取回箱子上的材料");ReplaceBuilding(b,"compostBarrel");Discover("build-compost-barrel");return;}
        if(b.Kind is "house" or "crate"){Require(Amount("mud")>=1,"请先把1份泥拖到建筑上");attached!.Stock["mud"]--;ReplaceBuilding(b,b.Kind=="house"?"residence":"nursery");Discover(b.Kind=="house"?"build-residence":"build-nursery");return;}
        Require(b.Kind=="warehouse"&&Amount("wood")>=2,"基础仓房先挂载2份木材，再使用锤子");attached!.Stock["wood"]-=2;ReplaceBuilding(b,"expandedWarehouse");Discover("expand-warehouse");
    }
    private void MergeEntities(PlacedObject[] sources)
    {
        Require(sources.Length==2&&sources[0].Id!=sources[1].Id,"请选择两个不同的建筑部件");
        Require(sources.All(s=>!play.Jobs.Any(j=>j.Id==s.Id)),"部件还有加工任务，请先领取");
        var recipes=new[]{("house","crate","warehouse","build-warehouse"),("house","orderBoard","shop","build-shop"),("house","stoneMill","mill","build-mill"),("house","oven","bakery","build-bakery"),("warehouse","handcart","transportWarehouse","build-transport-warehouse"),("farm","crate","farm","attach-farm-box")};
        var rule=recipes.FirstOrDefault(r=>sources.Any(s=>s.Kind==r.Item1)&&sources.Any(s=>s.Kind==r.Item2));Require(rule.Item1 is not null,"这两个部件没有可用合成配方");
        var main=sources.First(s=>s.Kind==rule.Item1);var addition=sources.First(s=>s.Id!=main.Id);
        var attached=sources.SelectMany(s=>play.Attachments.Find(a=>a.Id==s.Id)?.Stock??new Dictionary<string,int>()).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Value));Require(attached.Values.Sum()<=5,"合并后的挂载材料超过5份");
        if(main.Kind=="farm"){var farm=Farm(main.Id)!;Require(!farm.Box,"这块田已有收获箱");play.Farms[play.Farms.IndexOf(farm)]=farm with{Box=true,BoxStock=[]};}
        RemoveBuilding(addition);ReplaceBuilding(main,rule.Item3);play.Attachments.RemoveAll(a=>a.Id==main.Id);if(attached.Count>0)play.Attachments.Add(new(main.Id,attached));Discover(rule.Item4);
    }
    private void UseTool(WorldCommand c)
    {
        switch(c.Kind)
        {
            case "axe":
                var tree=LiveTrees.FirstOrDefault(n=>n.X==c.X&&n.Z==c.Z);Require(tree is not null,"把斧子拖到一棵树上松手");Require(TreeMature(c.X,c.Z),"树苗尚未长成");
                if(play.Trees.RemoveAll(t=>t.X==c.X&&t.Z==c.Z)==0)play.ClearedTrees.Add(Index(c.X,c.Z));DropNear("wood",2,c.X,c.Z);DropNear("treeSeed",2,c.X,c.Z);Discover("gather-wood");break;
            case "diggingTool":
                MakeBuilding("farm",c.X,c.Z);Discover("dig-farm");break;
            case "sickle":HarvestCell(Building(c.ObjectId),c.FarmCell);break;
            case "wateringCan":
                if(c.ObjectId!=0){var b=Building(c.ObjectId);if(b.Kind=="nursery"){StartProcessing(b,"grow-sapling",true);break;}var farm=Farm(b.Id);Require(farm is not null&&!farm.Irrigated,"把水壶拖到空旱田，或划过地上的土料堆");Require(farm!.Cells.All(p=>p is null||p.Crop=="rice"),"先收获旱作物，再灌成水田");play.Farms[play.Farms.IndexOf(farm)]=farm with{Irrigated=true};Discover("irrigate-farm");}
                else{var piles=c.Piles??[];Require(piles.Length>=2&&piles.All(p=>p.Item=="soil")&&piles.Sum(p=>p.Amount)>=9,"水壶需划过至少两堆、合计9份土料");int left=9;foreach(var p in piles){int n=Math.Min(left,p.Amount);if(n>0)ConsumePile(p,n);left-=n;}PutPile("mud",1,piles[0].X,piles[0].Z);Discover("water-soil");}break;
            case "hammer":
                if(c.Entities?.Length>=2){MergeEntities(c.Entities);break;}
                if(c.Piles?.Length==2&&c.Piles.Any(p=>p.Item=="wood")&&c.Piles.Any(p=>p.Item=="stone")){foreach(var p in c.Piles)ConsumePile(p,1);MakeBuilding("oven",c.X,c.Z,c.Rotation);Discover("build-stone-oven");break;}
                if(c.ObjectId!=0)HammerBuilding(Building(c.ObjectId));else CraftPile("hammer",c.X,c.Z);break;
            case "saw":CraftPile("saw",c.X,c.Z);break;
            default:throw new RuleFailure("工具用途尚未开放");
        }
    }
    private void StartProcessing(PlacedObject b,string recipe,bool attached)
    {
        Require(Processing.TryGetValue(recipe,out var rule)&&rule.Building==b.Kind,"这座建筑不能加工该配方");Require(!play.Jobs.Any(j=>j.Id==b.Id),"已有加工任务，请先领取");
        if(attached){var a=play.Attachments.Find(a=>a.Id==b.Id);Require(a is not null&&rule!.Inputs.All(p=>a.Stock.GetValueOrDefault(p.Item)>=p.Amount),"建筑上投入的材料不足");foreach(var input in rule!.Inputs)a!.Stock[input.Item]-=input.Amount;}
        else foreach(var input in rule!.Inputs)Take(input.Item,input.Amount);
        play.Jobs.Add(new(b.Id,recipe,play.Clock+rule!.Ticks));
    }
    private Result ExecuteGameplay(WorldCommand c)
    {
        try
        {
            CheckSources(c);
            switch(c.Type)
            {
                case "tool":UseTool(c);break;
                case "terrainDig":
                    EditableGround(c.X,c.Z);Require(Height(c.X,c.Z)>0,"此处已是水面");
                    // The mountain is the stone supply and the land itself is the soil supply.
                    bool rock=Surface(c.X,c.Z)==GroundSurface.Stone;string excavated=rock?"stone":"soil";
                    heights[Index(c.X,c.Z)]--;int left=Height(c.X,c.Z);
                    surfaces[Index(c.X,c.Z)]=left>=RockLine?DefaultSurface(left):(byte)GroundSurface.Soil;
                    DropNear(excavated,rock?1:1,c.X,c.Z,true,true);Discover(rock?"gather-stone":"gather-soil");break;
                case "terrainFill":
                    EditableGround(c.X,c.Z);Require(Height(c.X,c.Z)<MaxHeight,$"最多{MaxHeight}层");Take("soil",1);heights[Index(c.X,c.Z)]++;surfaces[Index(c.X,c.Z)]=(byte)GroundSurface.Soil;break;
                case "stockDrop":
                    Require(c.Quantity>0&&c.Quantity<=StackLimit(c.Item),"物品数量无效");var target=Occupant(c.X,c.Z);
                    if(target is not null){if(target.Kind=="farm"&&IsCrop(c.Item))PlantCell(target,c.FarmCell,c.Item);else{Take(c.Item,1);Attach(target,c.Item);}}
                    else if(c.Item=="sapling"){Require(nextId<1_000_000_000,"物件已达上限");Take("sapling",1);FreeGround(c.X,c.Z);play.Trees.Add(new(nextId++,c.X,c.Z,play.Clock+600,Hash(c.X,c.Z)));Discover("plant-sapling");}
                    else{Take(c.Item,c.Quantity);PutPile(c.Item,c.Quantity,c.X,c.Z);}break;
                case "plant":PlantCell(Building(c.ObjectId),c.FarmCell,c.Item);break;
                case "pickup":
                    var drop=play.Drops.Find(d=>d.Id==c.SourceId);if(drop is not null){Gain(drop.Item,drop.Amount);play.Drops.Remove(drop);break;}
                    var pile=play.Piles.Find(p=>p.Id==c.SourceId);Require(pile is not null,"物品已被收集");Gain(pile!.Item,pile.Amount);play.Piles.Remove(pile);break;
                case "movePile":
                    var source=play.Piles.Find(p=>p.Id==c.SourceId);Require(source is not null,"材料堆已变化");
                    if(c.ObjectId!=0){var b=Building(c.ObjectId);if(b.Kind.Contains("Warehouse",StringComparison.Ordinal)||b.Kind=="warehouse"){Gain(source!.Item,source.Amount);play.Piles.Remove(source);}
                        else{ConsumePile(source!,1);Attach(b,source!.Item);}break;}
                    if(source!.X==c.X&&source.Z==c.Z)break;
                    var dest=play.Piles.Find(p=>p.X==c.X&&p.Z==c.Z);int amount=Math.Min(source.Amount,StackLimit(source.Item)-(dest?.Amount??0));Require(amount>0,"目标材料堆已满");ConsumePile(source,amount);PutPile(source.Item,amount,c.X,c.Z);break;
                case "moveDrop":
                    var loose=play.Drops.Find(d=>d.Id==c.SourceId);Require(loose is not null,"掉落物已变化");play.Drops.Remove(loose!);PutPile(loose!.Item,loose.Amount,c.X,c.Z);break;
                case "merge":MergeEntities(c.Entities??[]);break;
                case "detach":
                    var a=play.Attachments.Find(a=>a.Id==c.ObjectId);Require(a is not null&&a.Stock.GetValueOrDefault(c.Item)>0,"建筑上没有这份材料");Gain(c.Item,1);a!.Stock[c.Item]--;break;
                case "process":StartProcessing(Building(c.ObjectId),c.Item,c.Kind=="attached");break;
                case "collectJob":
                    var j=play.Jobs.Find(j=>j.Id==c.ObjectId);Require(j is not null&&j.ReadyTick<=play.Clock,"加工尚未完成");var outputs=Processing[j!.Recipe].Outputs;Require(UsedStorage+outputs.Sum(p=>p.Amount)<=Capacity,"仓库空间不足，成品保留");foreach(var output in outputs)Gain(output.Item,output.Amount);play.Jobs.Remove(j);Discover(j.Recipe);break;
                case "collectBox":
                    var f=Farm(c.ObjectId);Require(f?.BoxStock is not null&&f.BoxStock.Values.Sum()>0,"田箱里没有收获");Require(UsedStorage+f!.BoxStock!.Values.Sum()<=Capacity,"仓库空间不足，收获保留田箱");foreach(var p in f.BoxStock)if(p.Value>0)Gain(p.Key,p.Value);play.Farms[play.Farms.IndexOf(f)]=f with{BoxStock=[]};break;
                default:throw new RuleFailure("未知操作");
            }
            return new(true,"已完成");
        }
        catch(RuleFailure error){return new(false,error.Message);}
    }
}