using System.Collections.ObjectModel;
namespace Town.Core;

public sealed record GroundPile(int Id,string Item,int Amount,int X,int Z);
public sealed record LooseDrop(int Id,string Item,int Amount,int X,int Z,float OffsetX,float OffsetZ);
public sealed record CropCell(string Crop,long ReadyTick,int Yield=2);
public sealed record FarmState(int Id,bool Irrigated,CropCell?[] Cells,bool Box=false,Dictionary<string,int>? BoxStock=null);
public sealed record WorkJob(int Id,string Recipe,long ReadyTick);
public sealed record Attachment(int Id,Dictionary<string,int> Stock);
public sealed record PlantedTree(int Id,int X,int Z,long MatureTick,double Variant);
public sealed record PlaySave(long Clock,Dictionary<string,int> Stock,GroundPile[] Piles,LooseDrop[] Drops,int[] ClearedTrees,
    FarmState[] Farms,Dictionary<string,int> Discoveries,Attachment[] Attachments,WorkJob[] Jobs,PlantedTree[] Trees);

internal sealed class PlayState
{
    internal long Clock;
    internal Dictionary<string,int> Stock=[];
    internal List<GroundPile> Piles=[];
    internal List<LooseDrop> Drops=[];
    internal HashSet<int> ClearedTrees=[];
    internal List<FarmState> Farms=[];
    internal Dictionary<string,int> Discoveries=[];
    internal List<Attachment> Attachments=[];
    internal List<WorkJob> Jobs=[];
    internal List<PlantedTree> Trees=[];
    internal PlaySave Snapshot()=>new(Clock,new(Stock),Piles.ToArray(),Drops.ToArray(),ClearedTrees.Order().ToArray(),
        Farms.Select(f=>f with{Cells=f.Cells.ToArray(),BoxStock=f.BoxStock is null?null:new(f.BoxStock)}).ToArray(),new(Discoveries),
        Attachments.Select(a=>a with{Stock=new(a.Stock)}).ToArray(),Jobs.ToArray(),Trees.ToArray());
    internal static PlayState Restore(PlaySave data)
    {
        if(data.Stock is null||data.Piles is null||data.Drops is null||data.ClearedTrees is null||data.Farms is null||data.Discoveries is null||data.Attachments is null||data.Jobs is null||data.Trees is null
            ||data.Piles.Any(p=>p is null||p.Item is null)||data.Drops.Any(p=>p is null||p.Item is null)||data.Trees.Any(t=>t is null)||data.Jobs.Any(j=>j is null||j.Recipe is null)||data.Farms.Any(f=>f is null||f.Cells is null||f.Cells.Any(c=>c is not null&&c.Crop is null))||data.Attachments.Any(a=>a is null||a.Stock is null))throw new FormatException("物品存档结构不完整");
        return new(){Clock=data.Clock,Stock=new(data.Stock),Piles=data.Piles.ToList(),Drops=data.Drops.ToList(),ClearedTrees=data.ClearedTrees.ToHashSet(),
            Farms=data.Farms.Select(f=>f with{Cells=f.Cells.ToArray(),BoxStock=f.BoxStock is null?null:new(f.BoxStock)}).ToList(),Discoveries=new(data.Discoveries),
            Attachments=data.Attachments.Select(a=>a with{Stock=new(a.Stock)}).ToList(),Jobs=data.Jobs.ToList(),Trees=data.Trees.ToList()};
    }
}

public sealed partial class World
{
    private PlayState play=new();
    private readonly Residents.NpcRoster npcs=new();
    public Residents.NpcRoster Npcs=>npcs;
    public static readonly IReadOnlyDictionary<string,string> ItemNames=new ReadOnlyDictionary<string,string>(new Dictionary<string,string>{
        ["vegetable"]="胡萝卜",["wheat"]="小麦",["lettuce"]="生菜",["rice"]="稻谷",["wood"]="木材",["stone"]="石料",["soil"]="土料",["mud"]="泥",
        ["treeSeed"]="树种",["sapling"]="树苗",["flour"]="面粉",["milledRice"]="大米",["bread"]="面包",["roastedCarrot"]="烤胡萝卜",["fertilizer"]="肥料"});
    public IReadOnlyDictionary<string,int> Inventory=>new ReadOnlyDictionary<string,int>(play.Stock);
    public IReadOnlyList<GroundPile> Piles=>play.Piles.AsReadOnly();
    public IReadOnlyList<LooseDrop> Drops=>play.Drops.AsReadOnly();
    public IReadOnlyList<FarmState> Farms=>play.Farms.AsReadOnly();
    public IReadOnlyList<WorkJob> Jobs=>play.Jobs.AsReadOnly();
    public IReadOnlyList<Attachment> Attachments=>play.Attachments.AsReadOnly();
    public IReadOnlyDictionary<string,int> Discoveries=>new ReadOnlyDictionary<string,int>(play.Discoveries);
    public long Clock=>play.Clock;
    /// <summary>演示与检查用：直接推进玩法时钟，让作物和加工到期。</summary>
    public void SetClock(long clock)=>play.Clock=Math.Max(0,clock);
    /// <summary>Deterministic growth cursor; saved so grass and saplings keep spreading across a reload.</summary>
    public long Grown {get;private set;}
    public IEnumerable<NaturalObject> LiveTrees=>nature.Where(TreeAlive).Concat(play.Trees.Select(t=>new NaturalObject(t.X,t.Z,t.Variant)));
    public bool TreeMature(int x,int z)=>play.Trees.Find(t=>t.X==x&&t.Z==z)is not { } planted||planted.MatureTick<=play.Clock;
    public float TreeScale(int x,int z)=>TreeMature(x,z)?1.15f:.38f;
    public int UsedStorage=>play.Stock.Values.Sum();
    public static int StorageCapacity(string building)=>building switch{"warehouse" or "transportWarehouse"=>16,"expandedWarehouse"=>32,_=>0};
    public int Capacity=>objects.Sum(o=>StorageCapacity(o.Kind));
    public int Stock(string item)=>play.Stock.GetValueOrDefault(item);
    public bool TreeAlive(NaturalObject tree)=>!play.ClearedTrees.Contains(Index(tree.X,tree.Z));
    public FarmState? Farm(int id)=>play.Farms.Find(f=>f.Id==id);
    public static bool IsCrop(string item)=>item is "vegetable" or "wheat" or "lettuce" or "rice";
    public static int StackLimit(string item)=>item is "wood" or "stone"?6:item=="mud"?9:IsCrop(item)?10:5;
    public PlaySave SnapshotGameplay()=>play.Snapshot();
    private World Fork()
    {
        var copy=new World(heights.ToArray(),Seed,surfaces.ToArray()){nextId=nextId,Grown=Grown,play=PlayState.Restore(play.Snapshot())};
        copy.nature.AddRange(nature);foreach(var item in objects)copy.AddObject(item);return copy;
    }
    private void Adopt(World other)
    {
        other.heights.CopyTo(heights,0);other.surfaces.CopyTo(surfaces,0);nature.Clear();nature.AddRange(other.nature);
        objects.Clear();byId.Clear();Array.Clear(occupancy);
        foreach(var item in other.objects)AddObject(item);play=other.play;nextId=other.nextId;Grown=other.Grown;
    }
    private void InitializeGameplay()
    {
        play.Farms.Clear();npcs.Clear();
        foreach(string item in ItemNames.Keys)play.Stock[item]=IsCrop(item)?1:0;
        foreach(var o in objects.Where(o=>o.Kind=="farm"))play.Farms.Add(new(o.Id,false,Enumerable.Range(0,9).Select(_=>(CropCell?)new CropCell(o.Rotation%2==0?"wheat":"lettuce",0)).ToArray()));
    }
    private void RestoreGameplay(PlaySave? data){play=data is null?new():PlayState.Restore(data);}
    private void MigrateLegacyGameplay()
    {
        play=new();InitializeGameplay();if(Capacity==0)foreach(var key in play.Stock.Keys.ToArray())play.Stock[key]=0;
    }
    /// <summary>Runs every tick; returns true when the renderer must refresh growth-driven art.</summary>
    internal bool AdvancePlay()
    {
        ResidentDigs.Clear();
        play.Clock++;
        bool changed=play.Farms.Any(f=>f.Cells.Any(c=>c is not null&&(c.ReadyTick-play.Clock is 0 or 100 or 200)))||play.Jobs.Any(j=>j.ReadyTick==play.Clock)||play.Trees.Any(t=>t.MatureTick==play.Clock);
        changed|=npcs.Tick(this);
        if(play.Clock%(SpreadTicks/2)==0)changed|=Spread();
        return changed;
    }
    public const int SpreadTicks=40;
    private const int TreeCap=1200;
    /// <summary>Overgrowth: grass reclaims bare soil, and dense mature woodland seeds one neighbouring cell.</summary>
    private bool Spread()
    {
        for(int attempt=0;attempt<8;attempt++)
        {
            long cursor=Grown++;
            int index=(int)((cursor*2654435761u)%(uint)(Size*Size));
            int x=index%Size-Half,z=index/Size-Half;
            if(Height(x,z)==0||Occupant(x,z)is not null||play.Piles.Any(p=>p.X==x&&p.Z==z)||play.Drops.Any(d=>d.X==x&&d.Z==z))continue;
            if(nature.Any(n=>n.X==x&&n.Z==z&&TreeAlive(n))||play.Trees.Any(t=>t.X==x&&t.Z==z))continue;
            if(Surface(x,z)==GroundSurface.Soil){surfaces[index]=(byte)GroundSurface.Grass;return true;}
            if(Surface(x,z)!=GroundSurface.Grass||Height(x,z)>=RockLine||play.Trees.Count+nature.Count>=TreeCap)continue;
            int seeds=0;
            foreach(var (dx,dz) in new[]{(-1,0),(1,0),(0,-1),(0,1)})
            {
                int a=x+dx,b=z+dz;
                if(!Inside(a,b))continue;
                if(play.Trees.Any(t=>t.X==a&&t.Z==b))seeds++;
                else if(nature.Any(n=>n.X==a&&n.Z==b&&TreeAlive(n)))seeds++;
            }
            if(seeds<2&&play.Trees.Count+nature.Count>=TreeCap/2)continue;
            if(seeds<1)continue;
            int near=0;
            for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
            {
                int a=x+dx,b=z+dz;if(!Inside(a,b))continue;
                if(nature.Any(n=>n.X==a&&n.Z==b&&TreeAlive(n))||play.Trees.Any(t=>t.X==a&&t.Z==b))near++;
            }
            if(near>=3)continue;
            if(Hash(x,z)>(seeds>.1?.16:.06))continue;
            nature.Add(new(x,z,Hash(z,x)));
            return true;
        }
        return false;
    }
    private void ValidateGameplay()
    {
        bool StockValid(Dictionary<string,int> stock)=>stock.All(p=>ItemNames.ContainsKey(p.Key)&&p.Value is >=0 and <=10000);
        if(play.Clock is <0 or >1_000_000_000_000||Grown<0||!StockValid(play.Stock)||UsedStorage>Capacity||play.Piles.Count>400||play.Drops.Count>500||play.Farms.Count>400||play.Jobs.Count>400
            ||play.ClearedTrees.Any(i=>i<0||i>=Size*Size||!nature.Any(n=>Index(n.X,n.Z)==i))||play.Discoveries.Any(p=>!RecipeBook.All.Any(r=>r.Id==p.Key)||p.Value is <1 or >1000000))throw new FormatException("物品或配方记录无效");
        if(objects.Count(o=>o.Kind=="farm")!=play.Farms.Count)throw new FormatException("农田状态缺失");
        var ids=objects.Select(o=>o.Id).ToHashSet();var cells=new HashSet<Cell>();var dropSlots=new HashSet<(int,int,float,float)>();
        bool Id(int id)=>id>0&&id<nextId&&ids.Add(id);
        foreach(var p in play.Piles){if(p is null||!Id(p.Id)||!ItemNames.ContainsKey(p.Item)||p.Amount<1||p.Amount>StackLimit(p.Item)||!cells.Add(new(p.X,p.Z))||HasNature(p.X,p.Z)||!LooseGround(p.X,p.Z))throw new FormatException("地面材料堆无效");}
        foreach(var p in play.Drops){if(p is null||!Id(p.Id)||!ItemNames.ContainsKey(p.Item)||p.Amount is <1 or >10||!float.IsFinite(p.OffsetX)||!float.IsFinite(p.OffsetZ)||p.OffsetX is <.1f or >.9f||p.OffsetZ is <.1f or >.9f||HasNature(p.X,p.Z)||cells.Contains(new(p.X,p.Z))||!dropSlots.Add((p.X,p.Z,p.OffsetX,p.OffsetZ))||!DropGround(p.X,p.Z))throw new FormatException("掉落物记录无效");}
        foreach(var npc in Npcs.All)if(npc.HasCart&&!Id(npc.CartId))throw new FormatException("居民手推车身份重复");
        foreach(var t in play.Trees){if(t is null||!Id(t.Id)||!LooseGround(t.X,t.Z,true)||!cells.Add(new(t.X,t.Z))||!double.IsFinite(t.Variant)||t.Variant is <0 or >1||t.MatureTick<0||t.MatureTick>play.Clock+600)throw new FormatException("树苗记录无效");}
        if(play.Farms.Select(f=>f.Id).Distinct().Count()!=play.Farms.Count||play.Jobs.Select(f=>f.Id).Distinct().Count()!=play.Jobs.Count||play.Attachments.Select(f=>f.Id).Distinct().Count()!=play.Attachments.Count)throw new FormatException("建筑状态重复");
        foreach(var f in play.Farms){if(!byId.TryGetValue(f.Id,out var o)||o.Kind!="farm"||f.Cells.Length!=9||f.Cells.Any(c=>c is not null&&(!IsCrop(c.Crop)||(c.Crop=="rice")!=f.Irrigated||c.ReadyTick<0||c.ReadyTick>play.Clock+300||c.Yield is <1 or >10))||!f.Box&&f.BoxStock?.Values.Sum()>0||f.BoxStock is not null&&(!StockValid(f.BoxStock)||f.BoxStock.Values.Sum()>16))throw new FormatException("农田状态无效");}
        foreach(var a in play.Attachments)if(!byId.TryGetValue(a.Id,out var b)||!StockValid(a.Stock)||a.Stock.Values.Sum()>(b.Kind=="compostBarrel"?16:5))throw new FormatException("建筑挂载材料无效");
        foreach(var j in play.Jobs)if(!byId.TryGetValue(j.Id,out var o)||!Processing.TryGetValue(j.Recipe,out var rule)||o.Kind!=rule.Building||j.ReadyTick<0||j.ReadyTick>play.Clock+600)throw new FormatException("加工任务无效");
    }
    private bool DropGround(int x,int z)=>Inside(x,z)&&Occupant(x,z)is null&&!nature.Any(n=>n.X==x&&n.Z==z&&TreeAlive(n))&&!play.Trees.Any(t=>t.X==x&&t.Z==z);
    private bool LooseGround(int x,int z,bool ignorePlanted=false)=>Inside(x,z)&&Height(x,z)>0&&Occupant(x,z)is null&&!nature.Any(n=>n.X==x&&n.Z==z&&TreeAlive(n))&&(ignorePlanted||!play.Trees.Any(t=>t.X==x&&t.Z==z));
}
