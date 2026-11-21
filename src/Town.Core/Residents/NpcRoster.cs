namespace Town.Core.Residents;

/// <summary>居民集合、生成与每 tick 推进。挂在 World 上，但全部规则都在 Town.Core 内。</summary>
public sealed class NpcRoster
{
    /// <summary>每座住宅提供的居住名额。</summary>
    public const int SlotsPerResidence = 2;

    private readonly List<NpcState> list = [];
    private int nextId = 1;

    public IReadOnlyList<NpcState> All => list;
    public int NextId => nextId;
    public int Count => list.Count;

    public bool TryAdd(World world, int residenceId, out NpcState npc)
    {
        npc = null!;
        if (list.Count >= NpcDemoIsland.MaxResidents || nextId == int.MaxValue) return false;
        var residence = world.Objects.FirstOrDefault(o => o.Id == residenceId);
        if (residence is null || (residence.Kind != "residence" && residence.Kind != "house")) return false;
        if (list.Count(n => n.Home == residenceId) >= SlotsPerResidence) return false;
        if (SpawnCell(world, residence, list) is not Cell spawn) return false;

        npc = new NpcState { Id = nextId++, Home = residenceId, X = spawn.X + .5f, Z = spawn.Z + .5f };
        npc.Y = world.Height(spawn.X, spawn.Z) * World.Step;
        npc.Facing = residence.Rotation * MathF.PI / 2;
        list.Add(npc);
        return true;
    }

    /// <summary>住宅旁边第一块可站立、且没有别人的陆地。优先门前(+Z/+X)一侧，免得被屋顶挡住。</summary>
    private static Cell? SpawnCell(World world, PlacedObject residence, List<NpcState> taken)
    {
        var (w, d) = World.Footprint(residence.Kind, residence.Rotation);
        for (int radius = 1; radius <= 5; radius++)
        {
            var ring = new List<Cell>();
            for (int dz = -radius; dz < d + radius; dz++)
                for (int dx = -radius; dx < w + radius; dx++)
                {
                    if (dx != -radius && dx != w + radius - 1 && dz != -radius && dz != d + radius - 1) continue;
                    int x = residence.X + dx, z = residence.Z + dz;
                    if (!NpcNavigation.Passable(world, x, z)) continue;
                    if (taken.Any(n => n.CellX == x && n.CellZ == z)) continue;
                    ring.Add(new Cell(x, z));
                }
            if (ring.Count > 0) return ring.OrderByDescending(c => c.Z).ThenByDescending(c => c.X).First();
        }
        return null;
    }

    internal bool Tick(World world)
    {
        bool changed = false;
        foreach (var npc in list)
        {
            changed |= NpcTasks.Tick(world, npc);
            NpcMover.Tick(world, npc);
        }
        return changed;
    }

    internal void Clear() => list.Clear();

    internal void Restore(IEnumerable<NpcSave> saves)
    {
        list.Clear();
        nextId = 1;
        foreach (var save in saves)
        {
            var npc = new NpcState
            {
                Id = save.Id, Home = save.Home, X = save.X, Z = save.Z, Facing = save.Facing,
                Hunger = save.Hunger, Stamina = save.Stamina, AutoHaul = save.AutoHaul, Workplace = save.Workplace, CartId = save.CartId,
            };
            if (save.Cargo is not null)
                foreach (var (item, amount) in save.Cargo) npc.CargoItems.Add(item, amount);
            if (save.TaskKind != (int)NpcTaskKind.None)
                npc.Task = new((NpcTaskKind)save.TaskKind, save.TargetX, save.TargetZ, save.Label,
                    save.SourceId, save.FarmCell, save.Automatic, save.Item, save.DestinationId);
            list.Add(npc);
            nextId = Math.Max(nextId, save.Id + 1);
        }
    }

    internal NpcSave[] Snapshot() => list.Select(n => new NpcSave(n.Id, n.Home, n.X, n.Z, n.Facing, n.Hunger, n.Stamina, n.CarriedKg,
        (int)(n.Task?.Kind ?? NpcTaskKind.None), n.Task?.TargetX ?? 0, n.Task?.TargetZ ?? 0, n.Task?.Label ?? "",
        n.Task?.SourceId ?? 0, n.Task?.FarmCell ?? -1, n.Task?.Automatic ?? false, n.AutoHaul,
        n.Cargo.Count == 0 ? null : new Dictionary<string, int>(n.CargoItems), n.Task?.Item ?? "", n.Task?.DestinationId ?? 0, n.Workplace, n.CartId)).ToArray();

    /// <summary>读档后重新贴上地面高度，并把已失效的路径清掉。</summary>
    internal void Refresh(World world)
    {
        foreach (var npc in list)
        {
            npc.Y = world.Height(npc.CellX, npc.CellZ) * World.Step;
            npc.Path.Clear();
            npc.PathIndex = 0;
            npc.PathFailed = false;
            if (npc.Task is not null) npc.Activity = NpcActivity.Walking;
        }
    }
}
