using System.Collections.ObjectModel;

namespace Town.Core;

public enum GroundSurface : byte { Grass, Sand, Soil, Stone }
public sealed record BuildingSpec(string Name, string? Asset, int Width, int Depth, float Scale);
public sealed record PlacedObject(int Id, string Kind, int X, int Z, int Rotation);
public sealed record NaturalObject(int X, int Z, bool Rock, double Variant);
public readonly record struct Cell(int X, int Z);
public readonly record struct Result(bool Ok, string Message);
public sealed record WorldCommand(string Type, int X, int Z, string Kind = "", int Rotation = 0, int ObjectId = 0,
    string Item="",int Quantity=1,int SourceId=0,int FarmCell=-1,int ExpectedStock=-1,PlacedObject[]? Entities=null,GroundPile[]? Piles=null);
public sealed record CommandPreview(Result Result,PlacedObject? Ghost);

/// <summary>Integer column grid and deterministic occupancy. No engine or storage references.</summary>
public sealed partial class World
{
    public const int Size = 40, Half = Size / 2, Schema = 3;
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
    public int Generator {get;}
    public const float WaterLevel=.22f;
    public GroundSurface Surface(int x,int z)=>Inside(x,z)?(GroundSurface)surfaces[Index(x,z)]:GroundSurface.Sand;
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
    private World(int[] heights,int seed=0,int generator=0,byte[]? surfaces=null)
    {
        this.heights = heights;this.surfaces=surfaces??heights.Select(h=>(byte)(h==1?GroundSurface.Sand:h>=6?GroundSurface.Stone:GroundSurface.Grass)).ToArray();Seed=seed;Generator=generator;
        Heights = Array.AsReadOnly(heights);
        Objects = objects.AsReadOnly();
        Nature = nature.AsReadOnly();
    }
    public static bool Inside(int x, int z) => x >= -Half && x < Half && z >= -Half && z < Half;
    public static int Index(int x, int z) => (z + Half) * Size + x + Half;
    public int Height(int x, int z) => Inside(x, z) ? heights[Index(x, z)] : 0;
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
    public bool HasNature(int x, int z) => nature.Any(n => n.X == x && n.Z == z&&!play.ClearedTrees.Contains(Index(x,z)))||play.Resources.Any(r=>r.X==x&&r.Z==z)||play.Trees.Any(t=>t.X==x&&t.Z==z);
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
            if (h == 0 || value is < 1 or > 9) return new(false, "仅调整现有陆地，高度为一至九层");
            if (Occupant(x, z) is not null || HasNature(x, z)||play.Piles.Any(p=>p.X==x&&p.Z==z)||play.Drops.Any(p=>p.X==x&&p.Z==z)) return new(false, "地面被占用，无法挖填");
            heights[Index(x, z)] = value;
        }
        else return new(false, "未知操作");
        return new(true, "已完成");
    }
    public static World Create()
    {
        var world = new World(new int[Size * Size]);
        for (int z = -Half; z < Half; z++)
            for (int x = -Half; x < Half; x++)
            {
                double radial = Math.Pow(x / 17.0, 2) + Math.Pow(z / 15.5, 2);
                double edge = 1 + .06 * Math.Sin(x * .8) + .045 * Math.Cos(z * .9);
                int h = radial > edge ? 0 : radial > edge - .14 ? 1 : 2;
                if (h == 2)
                {
                    double hill = Math.Max(0, 1 - Math.Sqrt(Math.Pow((x - 5) / 9.0, 2) + Math.Pow((z + 9) / 6.0, 2)));
                    h += (int)Math.Floor(hill * 6);
                    if (x is >= -1 and <= 13 && z is >= -6 and <= 10) h = 2;
                }
                if (Math.Abs(x - (-5 + 1.4 * Math.Sin(z * .23))) < 1.1 && z < 13) h = 0;
                world.heights[Index(x, z)] = h;
            }
        void Place(string kind, int x, int z, int rotation = 0)
        {
            var result = world.Execute(new("place", x, z, kind, rotation));
            if (!result.Ok) throw new InvalidOperationException($"Starter {kind} at {x},{z}: {result.Message}");
        }
        Place("house", 0, 0); Place("house", 5, 0); Place("warehouse", 10, 0, 1);
        Place("mill", 3, -5); Place("bakery", 8, -5); Place("farm", 0, 6); Place("farm", 5, 6, 1); Place("oven", 10, 6);
        for (int x = -2; x <= 13; x++) Place("road", x, 4);
        for (int z = -5; z <= 9; z++) if (z != 4) Place("road", -2, z);
        for (int z = 5; z <= 10; z++) Place("road", 8, z);
        Place("crate",9,3);Place("crate",12,3);Place("crate",7,-1);
        for (int z = -14; z < 14; z++)
            for (int x = -17; x < 17; x++)
            {
                int h = world.Height(x, z); double r = Hash(x, z);
                if (h < 2 || world.Occupant(x, z) is not null || x is >= -3 and <= 14 && z is >= -7 and <= 11) continue;
                if (r > .91 && h <= 4) world.nature.Add(new(x, z, false, Hash(z, x)));
            }
        for(int i=0;i<world.heights.Length;i++)world.surfaces[i]=(byte)(world.heights[i]==1?GroundSurface.Sand:world.heights[i]>=6?GroundSurface.Stone:GroundSurface.Grass);
        world.InitializeGameplay();
        return world;
    }
    internal static World Restore(SaveData data)
    {
        var world = Create();
        world.RestoreGameplay(data.Gameplay);
        if (data.Schema<3&&data.Heights.Where((h, i) => (h == 0) != (world.heights[i] == 0)).Any()) throw new FormatException("海陆边界不匹配");
        if (world.nature.Any(n => !world.play.ClearedTrees.Contains(Index(n.X,n.Z))&&data.Heights[Index(n.X, n.Z)] != world.Height(n.X, n.Z))) throw new FormatException("自然资源地面被改动");
        data.Heights.CopyTo(world.heights, 0);
        for(int i=0;i<world.surfaces.Length;i++)world.surfaces[i]=data.Surfaces is not null?(byte)data.Surfaces[i]:(byte)(data.Heights[i]==1?GroundSurface.Sand:data.Heights[i]>=6?GroundSurface.Stone:GroundSurface.Grass);
        world.objects.Clear(); world.byId.Clear(); Array.Clear(world.occupancy);
        var ids = new HashSet<int>();
        foreach (var item in data.Objects)
        {
            if (item is null || item.Kind is null || item.Id < 1 || item.Id >= data.NextId || !ids.Add(item.Id) || !world.CanPlace(item.Kind, item.X, item.Z, item.Rotation).Ok) throw new FormatException("建筑占用或身份不合法");
            world.AddObject(item);
        }
        world.nextId = data.NextId;if(data.Gameplay is null)world.MigrateLegacyGameplay();world.ValidateGameplay();return world;
    }
}
