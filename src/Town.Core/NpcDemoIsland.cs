namespace Town.Core;

/// <summary>
/// NPC 演示岛：一块 24×18 的平地，一段两级台阶和一座高台，用来验收居民能否上下一级、绕开建筑与水面。
/// 地形与建筑都走现有规则，不新增建筑类型，也不改正式随机岛。
/// </summary>
public static class NpcDemoIsland
{
    public const int Seed = 20261115;
    public const string Tag = "npc-demo";
    /// <summary>演示岛最多居民数，用作存档上限校验。</summary>
    public const int MaxResidents = 64;
    public const int MinX = -12, MaxX = 11, MinZ = -9, MaxZ = 8;
    public const int GroundHeight = 2;

    public static World Create() => Build(World.CreateEmpty(Seed));

    public static World CreateForRestore() => Create();

    private static World Build(World world)
    {
        Flatten(world);
        Terrace(world);
        Place(world, "house", -9, -6);
        Place(world, "residence", 6, 5);
        Place(world, "warehouse", -3, -6);
        Place(world, "mill", -1, -6, 1);
        Place(world, "crate", 2, -5);
        Place(world, "oven", 8, -6);
        Place(world, "farm", -9, 2);
        Place(world, "farm", -6, 2, 1);
        // 土路铺在平地上，台阶口 (0,0)(1,0) 留空；高台那一侧不铺路。
        for (int x = -11; x <= 10; x++) if (x is not (0 or 1) && world.Height(x, 0) == GroundHeight) TryPlace(world, "road", x, 0);
        PlantTrees(world);
        world.StartGameplay();
        world.IslandTag = Tag;
        return world;
    }

    /// <summary>在演示岛住宅旁生成一名居民。</summary>
    public static bool TrySpawn(World world, out Residents.NpcState npc)
    {
        // 先住正式住宅(residence)，住满了再退回基础房屋(house)。
        foreach (var residence in world.Objects.Where(o => o.Kind == "residence").Concat(world.Objects.Where(o => o.Kind == "house")))
            if (world.Npcs.TryAdd(world, residence.Id, out npc)) return true;
        npc = null!;
        return false;
    }

    private static void Flatten(World world)
    {
        var heights = new int[World.Size * World.Size];
        var surfaces = new byte[World.Size * World.Size];
        for (int z = -World.Half; z < World.Half; z++)
            for (int x = -World.Half; x < World.Half; x++)
            {
                int index = World.Index(x, z);
                bool land = x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
                heights[index] = land ? GroundHeight : 0;
                surfaces[index] = land ? (byte)GroundSurface.Grass : (byte)GroundSurface.Sand;
            }
        world.ReplaceTerrain(heights, surfaces);
        world.ClearNature();
    }

    /// <summary>两级台地：主体高 4（比平地高两级），西侧留一段 3 高、2 高的台阶可以走上去。</summary>
    private static void Terrace(World world)
    {
        for (int z = -5; z <= 3; z++)
            for (int x = 3; x <= 9; x++) world.SetHeight(x, z, 4);
        // 台阶：从 (1,0) 侧上到台地边缘 (3,0)
        for (int z = -1; z <= 1; z++) world.SetHeight(2, z, 3);
        for (int z = -1; z <= 1; z++) world.SetHeight(1, z, 2);
    }

    private static void PlantTrees(World world)
    {
        for (int i = 0; i < 24; i++)
        {
            double v = World.Hash(i * 7 + 3, i * 13 + 5);
            int x = -11 + (int)(v * 22), z = -8 + (int)(World.Hash(i * 5 + 1, i * 3 + 2) * 16);
            if (!World.Inside(x, z) || world.Height(x, z) != GroundHeight) continue;
            if (world.Occupant(x, z) is not null || world.Objects.Any(o => Near(o, x, z))) continue;
            if (world.Nature.Any(n => n.X == x && n.Z == z)) continue;
            world.AddNature(x, z, v);
        }
    }

    private static bool Near(PlacedObject item, int x, int z)
    {
        var (w, d) = World.Footprint(item.Kind, item.Rotation);
        return x >= item.X - 1 && x <= item.X + w && z >= item.Z - 1 && z <= item.Z + d;
    }

    private static void Place(World world, string kind, int x, int z, int rotation = 0)
    {
        var result = world.Execute(new WorldCommand("place", x, z, kind, rotation));
        if (!result.Ok) throw new InvalidOperationException($"演示岛 {kind}@{x},{z}: {result.Message}");
    }

    private static void TryPlace(World world, string kind, int x, int z, int rotation = 0)
        => world.Execute(new WorldCommand("place", x, z, kind, rotation));
}
