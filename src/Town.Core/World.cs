using System.Collections.ObjectModel;

namespace Town.Core;

public enum GroundSurface : byte { Grass, Sand, Soil, Stone }
public sealed record BuildingSpec(string Name, string? Asset, int Width, int Depth, float Scale);
public sealed record PlacedObject(int Id, string Kind, int X, int Z, int Rotation);
public sealed record NaturalObject(int X, int Z, double Variant);
public readonly record struct Cell(int X, int Z);
public readonly record struct Result(bool Ok, string Message);
public sealed record WorldCommand(string Type, int X, int Z, string Kind = "", int Rotation = 0, int ObjectId = 0,
    string Item="",int Quantity=1,int SourceId=0,int FarmCell=-1,int ExpectedStock=-1,PlacedObject[]? Entities=null,GroundPile[]? Piles=null);
public sealed record CommandPreview(Result Result,PlacedObject? Ghost);

/// <summary>Integer column grid and deterministic occupancy. No engine or storage references.</summary>
public sealed partial class World
{
    public const int Size = 96, Half = Size / 2, Schema = 4;
    public const int MaxHeight = 72, SeaLevel = 0, RockLine = 12;
    /// <summary>Starter settlement sits on the south-east coastal plain.</summary>
    public const int TownX = 12, TownZ = 12;
    public const float Step = .45f;
    public static readonly ReadOnlyDictionary<string, BuildingSpec> Catalog = new(
        new Dictionary<string, BuildingSpec>(StringComparer.Ordinal)
        {
            ["house"] = new("基础房屋", "house", 2, 2, 1),
            ["warehouse"] = new("基础仓房", "warehouse-base", 2, 2, 1),
            ["farm"] = new("田地", "farm-cell-bed", 2, 2, 1),
            ["mill"] = new("磨坊", "mill", 2, 2, 1),
            ["bakery"] = new("烘焙坊", "bakery", 2, 2, 1),
            ["oven"] = new("石窑", "stone-oven", 1, 1, 1),
            ["crate"] = new("箱子", "crate", 1, 1, 1),
            ["orderBoard"] = new("公告牌", "order-board", 1, 1, 1),
            ["stoneMill"] = new("石磨", "stone-mill", 1, 1, 1),
            ["shop"] = new("商店", "shop", 2, 2, 1),
            ["expandedWarehouse"] = new("扩容仓房", "warehouse-expanded", 2, 2, 1),
            ["residence"] = new("住宅", "residence", 2, 2, 1),
            ["handcart"] = new("手推车", "handcart", 1, 1, 1),
            ["boat"] = new("木船", "boat", 1, 2, 1),
            ["woodenPier"] = new("木栈桥", "wooden-pier", 2, 2, 1),
            ["transportWarehouse"] = new("运输仓", "warehouse-transport", 2, 2, 1),
            ["compostBarrel"] = new("堆肥木桶", "compost-barrel", 1, 1, 1),
            ["nursery"] = new("育苗箱", "nursery-empty", 1, 1, 1),
            ["road"] = new("土路", null, 1, 1, 1f),
        });
    private readonly int[] heights;
    private readonly byte[] surfaces;
    public int Seed {get;}
    /// <summary>生成器标记：普通随机岛为空，演示岛为 "npc-demo"，读档时据此重建地形。</summary>
    public string? IslandTag {get;set;}
    public const float WaterLevel=.22f;
    public GroundSurface Surface(int x,int z)=>Inside(x,z)?(GroundSurface)surfaces[Index(x,z)]:GroundSurface.Grass;
    public IReadOnlyList<byte> Surfaces=>Array.AsReadOnly(surfaces);
    private readonly List<PlacedObject> objects = [];
    private readonly List<NaturalObject> nature = [];
    private readonly int[] occupancy = new int[Size * Size];
    private readonly Dictionary<int, PlacedObject> byId = [];
    private int nextId = 1;
    public IReadOnlyList<int> Heights { get; }
    public IReadOnlyList<PlacedObject> Objects { get; }
    public IReadOnlyList<NaturalObject> Nature { get; }
    public int NextId => nextId;
    /// <summary>Water at zero, beach at one, rock from ten; every other height is open grass.</summary>
    public static byte DefaultSurface(int height)=>(byte)(height==1?GroundSurface.Sand:height>=RockLine?GroundSurface.Stone:GroundSurface.Grass);
    private World(int[] heights,int seed=0,byte[]? surfaces=null)
    {
        this.heights = heights;this.surfaces=surfaces??heights.Select(DefaultSurface).ToArray();Seed=seed;
        Heights = Array.AsReadOnly(heights);
        Objects = objects.AsReadOnly();
        Nature = nature.AsReadOnly();
    }
    public static bool Inside(int x, int z) => x >= -Half && x < Half && z >= -Half && z < Half;
    public static int Index(int x, int z) => (z + Half) * Size + x + Half;
    public int Height(int x, int z) => Inside(x, z) ? heights[Index(x, z)] : 0;

    /// <summary>演示岛用：直接写入一列高度，不做挖填规则校验。</summary>
    public void SetHeight(int x, int z, int height)
    {
        if (!Inside(x, z) || height < 0 || height > MaxHeight) return;
        heights[Index(x, z)] = height;
        surfaces[Index(x, z)] = DefaultSurface(height);
    }

    /// <summary>演示岛用：整体替换地形高度与地表。</summary>
    public void ReplaceTerrain(int[] newHeights, byte[] newSurfaces)
    {
        if (newHeights.Length != heights.Length || newSurfaces.Length != surfaces.Length) throw new ArgumentException("地形尺寸不符");
        newHeights.CopyTo(heights, 0);
        newSurfaces.CopyTo(surfaces, 0);
    }

    /// <summary>演示岛用：清空天然植被。</summary>
    public void ClearNature() => nature.Clear();

    /// <summary>演示岛用：直接放一棵天然树。</summary>
    public void AddNature(int x, int z, double variant)
    {
        if (Inside(x, z) && heights[Index(x, z)] > 0) nature.Add(new NaturalObject(x, z, Math.Clamp(variant, 0, 1)));
    }

    /// <summary>演示岛用：整张图都是水面、没有任何建筑的空白世界。</summary>
    public static World CreateEmpty(int seed) => new(new int[Size * Size], seed);

    /// <summary>演示岛用：地形清空后重新建立初始玩法状态（库存、农田）。</summary>
    public void StartGameplay() => InitializeGameplay();
    public static double Hash(int x, int z)
    {
        unchecked
        {
            uint n = (uint)((x + 137) * 374761393) ^ (uint)((z + 53) * 668265263);
            n = (n ^ (n >> 13)) * 1274126177;
            return (n ^ (n >> 16)) / 4294967296.0;
        }
    }
    public static (int Width, int Depth) Footprint(string kind, int rotation)
    {
        var spec = Catalog[kind];
        return rotation % 2 == 0 ? (spec.Width, spec.Depth) : (spec.Depth, spec.Width);
    }
    public static IEnumerable<Cell> Cells(PlacedObject item)
    {
        var (width, depth) = Footprint(item.Kind, item.Rotation);
        for (int dz = 0; dz < depth; dz++)
            for (int dx = 0; dx < width; dx++) yield return new(item.X + dx, item.Z + dz);
    }
    public PlacedObject? Occupant(int x, int z) => Inside(x,z) && byId.TryGetValue(occupancy[Index(x,z)],out var item) ? item : null;
    private void AddObject(PlacedObject item)
    {
        objects.Add(item); byId.Add(item.Id,item);
        foreach(var cell in Cells(item))occupancy[Index(cell.X,cell.Z)]=item.Id;
    }
    public bool HasNature(int x, int z) => nature.Any(n => n.X == x && n.Z == z&&!play.ClearedTrees.Contains(Index(x,z)))||play.Trees.Any(t=>t.X==x&&t.Z==z);
    public Result CanPlace(string kind, int x, int z, int rotation, int ignoreObjectId = 0)
    {
        if (!Catalog.ContainsKey(kind) || rotation is < 0 or > 3) return new(false, "未知建造项目");
        var (width, depth) = Footprint(kind, rotation);
        // Check the origin before adding footprint offsets: imported integer coordinates may overflow.
        if (!Inside(x, z) || x > Half - width || z > Half - depth) return new(false, "超出地图边界");
        int h = Height(x, z);
        for (int dz = 0; dz < depth; dz++)
            for (int dx = 0; dx < width; dx++)
            {
                int a = x + dx, b = z + dz;
                if (Height(a, b) == 0) return new(false, "需要完整陆地，不能建在水面");
                if (Height(a, b) != h) return new(false, "地面高低不同，请先整理土地");
                if (Occupant(a, b) is { } occupied && occupied.Id != ignoreObjectId || HasNature(a, b)||play.Piles.Any(p=>p.X==a&&p.Z==b)||play.Drops.Any(p=>p.X==a&&p.Z==b)) return new(false, "地块已被建筑、道路、物品或资源占用");
            }
        return new(true, "可以建造");
    }
    public Result Execute(WorldCommand command)
    {
        var candidate=Fork();
        Result result;try{candidate.CheckSources(command);result=candidate.ExecuteUnchecked(command);}catch(RuleFailure error){result=new(false,error.Message);}
        if(result.Ok)Adopt(candidate);
        return result;
    }
    public Result Preview(WorldCommand command){var candidate=Fork();try{candidate.CheckSources(command);return candidate.ExecuteUnchecked(command);}catch(RuleFailure error){return new(false,error.Message);}}
    public CommandPreview PreviewPlan(WorldCommand command)
    {
        var candidate=Fork();Result result;try{candidate.CheckSources(command);result=candidate.ExecuteUnchecked(command);}catch(RuleFailure error){result=new(false,error.Message);}
        return new(result,result.Ok?candidate.objects.FirstOrDefault(o=>!objects.Contains(o)):null);
    }
    private Result ExecuteUnchecked(WorldCommand command)
    {
        if(command.Type is not ("place" or "move" or "remove" or "raise" or "lower"))return ExecuteGameplay(command);
        int x = command.X, z = command.Z;
        if (command.Type == "place")
        {
            if (objects.Count >= 400 || nextId >= 1_000_000_000) return new(false, "已达检查版物件上限");
            var result = CanPlace(command.Kind, x, z, command.Rotation);
            if (!result.Ok) return result;
            AddObject(new(nextId++, command.Kind, x, z, command.Rotation));
            if(command.Kind=="farm")play.Farms.Add(new(objects[^1].Id,false,new CropCell?[9]));
        }
        else if (command.Type == "move")
        {
            if(!byId.TryGetValue(command.ObjectId,out var original))return new(false,"请先选择要移动的建筑");
            var valid=CanPlace(original.Kind,x,z,command.Rotation,original.Id);if(!valid.Ok)return valid;
            foreach(var cell in Cells(original))occupancy[Index(cell.X,cell.Z)]=0;
            var moved=original with {X=x,Z=z,Rotation=command.Rotation};
            objects[objects.IndexOf(original)]=moved;byId[moved.Id]=moved;
            foreach(var cell in Cells(moved))occupancy[Index(cell.X,cell.Z)]=moved.Id;
        }
        else if (command.Type == "remove")
        {
            var item = Occupant(x, z);
            if (item is null) return new(false, "这里没有可拆除的建筑或道路");
            if(play.Attachments.Any(a=>a.Id==item.Id&&a.Stock.Values.Sum()>0)||play.Jobs.Any(j=>j.Id==item.Id)||Farm(item.Id)is { } f&&(f.Cells.Any(c=>c is not null)||f.BoxStock?.Values.Sum()>0))return new(false,"先取回材料、完成加工并清空农田");
            int storage=item.Kind switch{"warehouse" or "transportWarehouse"=>16,"expandedWarehouse"=>32,_=>0};if(UsedStorage>Capacity-storage)return new(false,"拆除后仓容不足，请先整理库存");
            RemoveBuilding(item);
        }
        else if (command.Type is "raise" or "lower")
        {
            if (!Inside(x, z)) return new(false, "超出地图边界");
            int h = Height(x, z), value = h + (command.Type == "raise" ? 1 : -1);
            if (h == 0 || value is < 1 or > MaxHeight) return new(false, $"仅调整现有陆地，高度为一至{MaxHeight}层");
            if (Occupant(x, z) is not null || HasNature(x, z)||play.Piles.Any(p=>p.X==x&&p.Z==z)||play.Drops.Any(p=>p.X==x&&p.Z==z)) return new(false, "地面被占用，无法挖填");
            heights[Index(x, z)] = value;
            surfaces[Index(x, z)] = DefaultSurface(value);
        }
        else return new(false, "未知操作");
        return new(true, "已完成");
    }
    private void PopulateStarter()
    {
        var world=this;
        void Place(string kind, int x, int z, int rotation = 0)
        {
            var result = world.Execute(new("place", x, z, kind, rotation));
            if (!result.Ok) throw new InvalidOperationException($"Starter {kind} at {x},{z}: {result.Message}");
        }
        const int ox = TownX, oz = TownZ;
        Place("house", ox+0, oz+0); Place("house", ox+5, oz+0); Place("warehouse", ox+10, oz+0, 1);
        Place("mill", ox+3, oz-5); Place("bakery", ox+8, oz-5); Place("farm", ox+0, oz+6); Place("farm", ox+5, oz+6, 1); Place("oven", ox+10, oz+6);
        for (int x = ox-2; x <= ox+13; x++) Place("road", x, oz+4);
        for (int z = oz-5; z <= oz+9; z++) if (z != oz+4) Place("road", ox-2, z);
        for (int z = oz+5; z <= oz+10; z++) Place("road", ox+8, z);
        Place("crate",ox+9,oz+3);Place("crate",ox+12,oz+3);Place("crate",ox+7,oz-1);
    }
    internal static World Restore(SaveData data)
    {
        var world = data.Island == NpcDemoIsland.Tag ? NpcDemoIsland.CreateForRestore() : Create(data.Seed);
        world.IslandTag = data.Island;
        world.RestoreGameplay(data.Gameplay);
        if(data.Heights.Length!=world.heights.Length)throw new FormatException("地形尺寸不符");
        data.Heights.CopyTo(world.heights, 0);
        for(int i=0;i<world.surfaces.Length;i++)world.surfaces[i]=data.Surfaces is not null?(byte)data.Surfaces[i]:DefaultSurface(data.Heights[i]);
        world.nature.Clear();world.nature.AddRange(data.Nature!);
        world.objects.Clear(); world.byId.Clear(); Array.Clear(world.occupancy);
        var ids = new HashSet<int>();
        foreach (var item in data.Objects)
        {
            if (item is null || item.Kind is null || item.Id < 1 || item.Id >= data.NextId || !ids.Add(item.Id) || !world.CanPlace(item.Kind, item.X, item.Z, item.Rotation).Ok) throw new FormatException("建筑占用或身份不合法");
            world.AddObject(item);
        }
        world.nextId = data.NextId;if(data.Gameplay is null)world.MigrateLegacyGameplay();
        world.npcs.Restore(data.Residents ?? []);
        world.npcs.Refresh(world);
        world.ValidateGameplay();return world;
    }
}