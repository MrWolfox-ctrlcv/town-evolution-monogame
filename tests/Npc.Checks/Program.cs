using System.Text.Json.Nodes;
using Town.Core;
using Town.Core.Residents;

int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Test(string name, Action action) { action(); passed++; Console.WriteLine($"PASS {name}"); }

var world = NpcDemoIsland.Create();

Test("demo island is flat land with a two-level terrace", () =>
{
    Check(world.Height(-11, 0) == NpcDemoIsland.GroundHeight, "plain height");
    Check(world.Height(20, 0) == 0, "outside island should be water");
    Check(world.Height(6, 0) == 4, "terrace height");
    Check(world.Height(2, 0) == 3 && world.Height(1, 0) == 2, "stair steps");
    Check(world.Objects.Any(o => o.Kind == "residence"), "no residence");
    Check(world.IslandTag == NpcDemoIsland.Tag, "island tag");
});

Test("residents spawn beside a residence and respect two slots", () =>
{
    var w = NpcDemoIsland.Create();
    var residence = w.Objects.First(o => o.Kind == "residence");
    Check(w.Npcs.TryAdd(w, residence.Id, out var first), "first spawn");
    Check(NpcNavigation.Passable(w, first.CellX, first.CellZ), "spawned in a blocked cell");
    Check(Math.Abs(first.X - (first.CellX + .5f)) < 1e-3, "not centred");
    var (_, depth) = World.Footprint(residence.Kind, residence.Rotation);
    Check(first.CellZ == residence.Z + depth, "first resident should appear in front of the roof");
    Check(w.Npcs.TryAdd(w, residence.Id, out var second), "second spawn");
    Check(second.CellZ == first.CellZ && second.CellX != first.CellX, "second resident should appear beside the first");
    Check(!w.Npcs.TryAdd(w, residence.Id, out _), "third spawn should be refused");
});

Test("pathfinding walks the stairs but refuses a two-level cliff and water", () =>
{
    var w = NpcDemoIsland.Create();
    var residence = w.Objects.First(o => o.Kind == "residence");
    w.Npcs.TryAdd(w, residence.Id, out var npc);
    var stairPath = NpcNavigation.FindPath(w, npc.CellX, npc.CellZ, 1, 0);
    Check(stairPath.Found, "stairs unreachable");
    var climb = NpcNavigation.FindPath(w, 1, 0, 6, 0);
    Check(climb.Found && climb.Cells.Count > 0, "terrace unreachable via stairs");
    Check(NpcNavigation.FindPath(w, 1, 0, 20, 0) is { Found: false }, "water should not be walkable");
    Check(NpcNavigation.Passable(w, 6, 0), "terrace cell should be passable");
    Check(!NpcNavigation.Passable(w, residence.X, residence.Z), "residence footprint should block");
});

Test("a two-level cliff is not a shortcut", () =>
{
    var w = NpcDemoIsland.Create();
    // (3, -5) 是高台边缘 (4)，其南邻 (-4) 仍是平地 2：一次抬升 2 级，不能走。
    Check(w.Height(3, -5) == 4 && w.Height(3, -6) == NpcDemoIsland.GroundHeight, "cliff fixture");
    Check(NpcNavigation.StepCost(w, 3, -6, 3, -5) is null, "two-level step allowed");
    Check(NpcNavigation.StepCost(w, 1, 0, 2, 0) is not null, "one-level step blocked");
});

Test("resident walks at the configured speed and slows down when loaded", () =>
{
    var w = NpcDemoIsland.Create();
    var residence = w.Objects.First(o => o.Kind == "residence");
    w.Npcs.TryAdd(w, residence.Id, out var npc);
    NpcTasks.Assign(w, npc, NpcTask.WalkTo(-10, 6, "远处"), out _);
    double walked = 0;
    for (int i = 0; i < 20; i++)
    {
        float px = npc.X, pz = npc.Z;
        NpcMover.Tick(w, npc);
        walked += Math.Sqrt(Math.Pow(npc.X - px, 2) + Math.Pow(npc.Z - pz, 2));
    }
    // 每 tick 的预算正好用在步进上，格子边界处会有半格以内的余量。
    Check(walked > 1.85 && walked < 2.1, $"one second should cover about two cells, walked {walked:0.00}");
    Check(Math.Abs(npc.SpeedCellsPerSecond - 2f) < 1e-3, "empty speed");
    var loaded = JsonNode.Parse(SaveCodec.Encode(w))!;
    loaded["residents"]![0]!["cargo"] = new JsonObject { ["wood"] = 4 };
    npc = SaveCodec.Decode(loaded.ToJsonString()).Npcs.All.Single();
    Check(Math.Abs(npc.SpeedCellsPerSecond - 1.4f) < 1e-3, "loaded speed should be 1.4");
    Check(Math.Abs(NpcConfig.SpeedCellsPerSecond(6) - 1.7f) < 1e-3, "half load speed");
});

Test("a simulation tick drives the resident all the way to the target", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var residence = sim.World.Objects.First(o => o.Kind == "residence");
    sim.World.Npcs.TryAdd(sim.World, residence.Id, out var npc);
    Check(NpcTasks.Assign(sim.World, npc, NpcTask.WalkTo(1, 0, "台阶"), out _), "assign failed");
    for (int i = 0; i < 20 * 30 && npc.Task is not null; i++) sim.Advance(.05);
    Check(npc.Task is null, "never arrived");
    Check(npc.CellX == 1 && npc.CellZ == 0, $"arrived at {npc.CellX},{npc.CellZ}");
    Check(sim.World.Clock > 0, "clock did not advance");
});

Test("an unreachable target reports instead of retrying forever", () =>
{
    var w = NpcDemoIsland.Create();
    var residence = w.Objects.First(o => o.Kind == "residence");
    w.Npcs.TryAdd(w, residence.Id, out var npc);
    var onWater = new Cell(30, 30);
    Check(!NpcTasks.Assign(w, npc, NpcTask.WalkTo(onWater.X, onWater.Z, "海面"), out string message), "water accepted");
    Check(message.Contains("无路"), message);
    Check(npc.Task is null, "failed task should be dropped");
});

Test("residents and their tasks survive a save round trip", () =>
{
    var w = NpcDemoIsland.Create();
    NpcDemoIsland.TrySpawn(w, out var npc);
    npc.Hunger = 61; npc.Stamina = 77;
    NpcTasks.Assign(w, npc, NpcTask.WalkTo(1, 0, "台阶"), out _);
    var legacy = JsonNode.Parse(SaveCodec.Encode(w))!;
    legacy["residents"]![0]!["carriedKg"] = 4.5;
    ((JsonObject)legacy["residents"]![0]!).Remove("item");
    var empty = SaveCodec.Decode(legacy.ToJsonString()).Npcs.All.Single();
    Check(empty.Cargo.Count == 0 && empty.CarriedKg == 0 && empty.SpeedCellsPerSecond == NpcConfig.WalkCellsPerSecond, "legacy phantom load changed the pose or speed");
    legacy["residents"]![0]!["cargo"] = new JsonObject { ["stone"] = 1, ["treeSeed"] = 3 };
    var back = SaveCodec.Decode(legacy.ToJsonString());
    Check(back.IslandTag == NpcDemoIsland.Tag, "island tag lost");
    Check(back.Npcs.Count == 1, "resident lost");
    var restored = back.Npcs.All[0];
    Check(restored.Hunger == 61 && restored.Stamina == 77, "stats lost");
    Check(Math.Abs(restored.CarriedKg - 5.3) < 1e-6 && restored.Cargo.Count == 2, "weight should equal cargo");
    Check(restored.Task?.TargetX == 1 && restored.Task.TargetZ == 0, "task lost");
    var canonical = SaveCodec.Encode(back);
    Check(SaveCodec.Encode(SaveCodec.Decode(canonical)) == canonical, "round trip not stable");
});

Test("walking residents keep their feet on the ground across a step", () =>
{
    var w = NpcDemoIsland.Create();
    for (int i = 0; i < 4; i++) w.Npcs.TryAdd(w, w.Objects.First(o => o.Kind == "house").Id, out _);
    var npc = w.Npcs.All[0];
    NpcTasks.Assign(w, npc, NpcTask.WalkTo(6, 0, "高台"), out _);
    float previous = npc.Y;
    bool sawStep = false;
    for (int i = 0; i < 20 * 40 && npc.Task is not null; i++)
    {
        NpcMover.Tick(w, npc);
        float ground = w.Height(npc.CellX, npc.CellZ) * World.Step;
        Check(Math.Abs(npc.Y - ground) <= World.Step + 1e-3, "feet left the ground");
        if (npc.Y > previous + .1f) sawStep = true;
        previous = npc.Y;
    }
    Check(sawStep, "height never rose while climbing");
    Check(npc.CellZ == 0 && npc.CellX == 6, $"ended at {npc.CellX},{npc.CellZ}");
});

void Advance(Simulation sim, int seconds)
{
    for (int i = 0; i < seconds * 4; i++) sim.Advance(.25);
}

Test("the main island keeps a connected river and flat starter footprints across seeds", () =>
{
    foreach (int seed in new[] { 1, 424242, 50231, 1918020111, int.MaxValue })
    {
        var island = World.Create(seed);
        foreach (var building in island.Objects)
            Check(World.Cells(building).All(c => island.Height(c.X,c.Z) == 2), "starter footprint changed height");
        var water = new HashSet<Cell>();
        var border = new Queue<Cell>();
        border.Enqueue(new(-World.Half, World.Half - 1));
        while (border.TryDequeue(out var cell))
        {
            if (!World.Inside(cell.X,cell.Z) || island.Height(cell.X,cell.Z) != 0 || !water.Add(cell)) continue;
            border.Enqueue(new(cell.X - 1,cell.Z)); border.Enqueue(new(cell.X + 1,cell.Z));
            border.Enqueue(new(cell.X,cell.Z - 1)); border.Enqueue(new(cell.X,cell.Z + 1));
        }
        for (int z = -7; z <= 25; z++)
            Check(Enumerable.Range(-21, 22).Any(x => water.Contains(new(x,z))), $"river disconnected on row {z}, seed {seed}");
        Check(SaveCodec.Encode(SaveCodec.Decode(SaveCodec.Encode(island))) == SaveCodec.Encode(island), "island changed on restore");
    }
});

Test("chopping, competing haulers and storage conserve loose items and leave crafting piles alone", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var w = sim.World;
    Check(NpcDemoIsland.TrySpawn(w, out var first) && NpcDemoIsland.TrySpawn(w, out _), "spawn");
    Check(w.Execute(new("stockDrop", -10, 7, Item: "wheat")).Ok, "reserved crafting pile");
    var tree = w.LiveTrees.First(t => t.X < 3);
    Check(NpcTasks.Assign(w, first, new(NpcTaskKind.Chop, tree.X, tree.Z, "伐木"), out _), "chop not assigned");
    Advance(sim, 70);
    Check(!w.LiveTrees.Contains(tree), "worker did not cut the tree");
    Check(w.Stock("wood") == 2 && w.Stock("treeSeed") == 2 && w.Drops.Count == 0, "wood or seeds lost or duplicated");
    Check(w.Piles.Count == 1 && w.Piles[0].Item == "wheat", "auto hauling took a crafting pile");
    Check(w.Npcs.All.All(n => n.Cargo.Count == 0), "cargo did not reach storage");
    Check(w.Npcs.All.Select(n => n.Cell).Distinct().Count() == 2, "neighbors stopped on the same warehouse tile");
});

Test("manual harvest consumes one mature farm cell and produces two loose units", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var w = sim.World;
    Check(NpcDemoIsland.TrySpawn(w, out var npc), "spawn");
    npc.AutoHaul = false;
    var farm = w.Objects.First(b => b.Kind == "farm");
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.Harvest, farm.X, farm.Z, "收割", farm.Id, 0), out _), "harvest not assigned");
    Advance(sim, 25);
    Check(w.Farm(farm.Id)!.Cells[0] is null && w.Farm(farm.Id)!.Cells[1] is not null, "wrong crop changed");
    Check(w.Drops.Sum(d => d.Amount) == 2, "wrong harvest amount");
});

Test("cargo, in-flight work and auto preference survive reload and a full warehouse", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var w = sim.World;
    Check(NpcDemoIsland.TrySpawn(w, out var npc), "spawn");
    npc.AutoHaul = false;
    Check(w.Execute(new("stockDrop", -10, 7, Item: "wheat")).Ok, "prepare pile");
    var pile = w.Piles.Single();
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.PickupPile, pile.X, pile.Z, "搬运", pile.Id), out _), "pick not assigned");
    sim.Load(SaveCodec.Encode(w));
    w = sim.World;
    npc = w.Npcs.All.Single();
    Check(npc.Task?.SourceId == pile.Id && !npc.AutoHaul, "assignment or preference lost");
    Advance(sim, 25);
    Check(npc.Cargo.GetValueOrDefault("wheat") == 1 && w.Piles.Count == 0, "failed to load");
    var data = System.Text.Json.Nodes.JsonNode.Parse(SaveCodec.Encode(w))!;
    data["gameplay"]!["stock"]!["wood"] = w.Capacity - w.UsedStorage;
    sim.Load(data.ToJsonString());
    w = sim.World;
    npc = w.Npcs.All.Single();
    var store = w.Objects.First(b => World.StorageCapacity(b.Kind) > 0);
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.Store, store.X, store.Z, "存放物品", store.Id), out _), "store not assigned");
    Advance(sim, 25);
    Check(npc.Cargo.GetValueOrDefault("wheat") == 1 && w.Stock("wheat") == 0, "full warehouse lost or duplicated inventory");
    Check(w.Execute(new("stockDrop", -10, 7, Item: "wood")).Ok, "free a slot");
    npc.AutoHaul = true;
    Advance(sim, 10);
    Check(w.Stock("wheat") == 1 && npc.Cargo.Count == 0 && npc.CarriedKg == 0, "auto deposit did not resume");
});

Test("a farm box feeds the same walking warehouse route", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var w = sim.World;
    Check(NpcDemoIsland.TrySpawn(w, out _), "spawn");
    var plot = w.Objects.First(o => o.Kind == "farm");
    var crate = w.Objects.First(o => o.Kind == "crate");
    Check(w.Execute(new("merge",0,0,Entities:[plot,crate])).Ok, "add harvest box");
    Check(w.Execute(new("tool", plot.X, plot.Z, "sickle", ObjectId:plot.Id, FarmCell:0)).Ok, "harvest into box");
    Check(w.Farm(plot.Id)?.BoxStock?.GetValueOrDefault("wheat") == 2 && w.Drops.Count == 0, "box yield");
    int before = w.Stock("wheat");
    Advance(sim, 60);
    Check(w.Stock("wheat") == before+2 && w.Farm(plot.Id)?.BoxStock?.Count == 0, "box was not delivered exactly once");
    Check(w.Npcs.All.All(npc => npc.Cargo.Count == 0), "box cargo was not unloaded");
});

Test("a hungry resident eats actual warehouse food before resuming chores", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var w = sim.World;
    Check(NpcDemoIsland.TrySpawn(w, out var npc), "spawn");
    npc.Hunger = 25;
    npc.AutoHaul = false;
    Check(NpcTasks.Assign(w, npc, NpcTask.WalkTo(1, 0, "路口"), out _), "manual walk");
    sim.Advance(.25);
    Check(npc.Task?.Kind == NpcTaskKind.GoTo, "needs interrupted player command");
    for (int i = 0; i < 220 && w.Stock("vegetable") == 1; i++) sim.Advance(.25);
    Check(w.Stock("vegetable") == 0 && w.Stock("lettuce") == 1 && npc.Hunger > 40, "meal was not taken from stock exactly once");
    var warehouse = w.Objects.First(b => World.StorageCapacity(b.Kind) > 0);
    var (width, depth) = World.Footprint(warehouse.Kind, warehouse.Rotation);
    Check(NpcNavigation.ApproachCells(w, warehouse.X, warehouse.Z, Math.Max(width, depth)).Contains(npc.Cell), "meal was eaten away from the warehouse");
    int fed = npc.Hunger;
    Advance(sim, 4);
    Check(npc.Hunger < fed, "hunger stopped advancing");
});

Test("an exhausted resident returns home, rests, and recovers", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var w = sim.World;
    Check(NpcDemoIsland.TrySpawn(w, out var npc), "spawn");
    Check(NpcTasks.Assign(w, npc, NpcTask.WalkTo(1, 0, "路口"), out _), "move away from home");
    Advance(sim, 11);
    var previous = npc.Cell;
    Check(npc.Task is null, "walk did not finish");
    npc.Stamina = 18;
    Advance(sim, 1);
    Check(npc.Task is { Kind: NpcTaskKind.Rest, SourceId: > 0 }, "rest not assigned");
    Advance(sim, 28);
    Check(npc.Stamina >= NpcConfig.StaminaRestTarget && npc.Task is null && npc.Cell != previous, "never recovered at home");
});

Test("resident excavation changes the rendered terrain event and delivers soil", () =>
{
    var sim = new Simulation();
    sim.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
    var w = sim.World;
    Check(NpcDemoIsland.TrySpawn(w, out var npc), "spawn");
    var target = new Cell(1, 0);
    w.SetHeight(npc.CellX, npc.CellZ, 1);
    var flooded = new WorldCommand("terrainDig", npc.CellX, npc.CellZ);
    Check(!w.PreviewPlan(flooded).Result.Ok && !w.Execute(flooded).Ok, "resident allowed to be flooded");
    w.SetHeight(npc.CellX, npc.CellZ, 2);
    Check(w.PreviewPlan(new("terrainDig", target.X, target.Z)).Result.Ok, "dig fixture");
    int originalHeight = w.Height(target.X, target.Z);
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.Dig, target.X, target.Z, "挖掘"), out _), "dig not assigned");
    Advance(sim, 1);
    Check(w.Height(target.X, target.Z) == originalHeight, "dug from a distance");
    Advance(sim, 40);
    bool terrainChanged = false;
    while (sim.TryChange(out var change)) terrainChanged |= change.Type == "terrainDig" && change.Cells.Contains(target);
    Check(terrainChanged && w.Height(target.X, target.Z) == originalHeight - 1, "resident dig did not invalidate terrain");
    Check(w.Stock("soil") == 1 && npc.Cargo.Count == 0, "soil was not delivered to storage");
});

Test("a saved resident milling order starts at the workshop and ships flour via cargo", () =>
{
    var sim = new Simulation();
    var w = NpcDemoIsland.Create();
    Check(NpcDemoIsland.TrySpawn(w, out var npc), "spawn");
    var mill = w.Objects.First(o => o.Kind == "mill");
    var doc = JsonNode.Parse(SaveCodec.Encode(w))!;
    doc["gameplay"]!["stock"]!["wheat"] = 2;
    sim.Load(doc.ToJsonString());
    w = sim.World;
    npc = w.Npcs.All.Single();
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.Process, mill.X, mill.Z, "磨面粉", mill.Id, Item: "mill-wheat"), out _), "process not assigned");
    Check(npc.Task is { Kind: NpcTaskKind.FetchSupplies }, "did not route to ingredients");
    sim.Load(SaveCodec.Encode(w));
    w = sim.World;
    npc = w.Npcs.All.Single();
    Check(npc.Task?.Item == "mill-wheat" && npc.Task?.DestinationId == mill.Id, "employee recipe was lost");
    Advance(sim, 1);
    Check(w.Jobs.Count == 0 && w.Stock("wheat") == 2, "inputs taken before reaching warehouse");
    bool walkedWithInput = false, walkedWithOutput = false;
    for (int i = 0; i < 200 && w.Stock("flour") == 0; i++)
    {
        sim.Advance(.25);
        walkedWithInput |= npc.Cargo.GetValueOrDefault("wheat") == 2 && npc.Task?.Kind == NpcTaskKind.Process && w.Jobs.Count == 0;
        walkedWithOutput |= npc.Cargo.GetValueOrDefault("flour") == 1;
    }
    Check(walkedWithInput && walkedWithOutput && w.Stock("wheat") == 0 && w.Stock("flour") == 1 && npc.Cargo.Count == 0 && w.Jobs.Count == 0,
        "flour was lost, duplicated, or auto-credited without a delivery");
});

Test("two staffed workshops repeat milling and carry flour and wood to the bakery", () =>
{
    var sim = new Simulation();
    var w = NpcDemoIsland.Create();
    Check(w.Execute(new("place", 5, -8, "bakery")).Ok, "bakery fixture");
    Check(NpcDemoIsland.TrySpawn(w, out var miller), "miller");
    Check(NpcDemoIsland.TrySpawn(w, out var baker), "baker");
    miller.AutoHaul = false;
    baker.AutoHaul = false;
    var mill = w.Objects.First(o => o.Kind == "mill");
    var bakery = w.Objects.First(o => o.Kind == "bakery");
    Check(NpcTasks.SetWorkplace(w, miller, mill.Id, "mill-wheat", out _), "assign mill");
    Check(!NpcTasks.SetWorkplace(w, baker, mill.Id, "mill-rice", out _), "assigned a second worker to the same mill");
    Check(NpcTasks.SetWorkplace(w, baker, bakery.Id, "bake-bread", out _), "assign bakery");
    var save = JsonNode.Parse(SaveCodec.Encode(w))!;
    save["gameplay"]!["stock"]!["wheat"] = 4;
    save["gameplay"]!["stock"]!["wood"] = 1;
    sim.Load(save.ToJsonString());
    w = sim.World;
    baker = w.Npcs.All.Single(n => n.Id == baker.Id);
    bool bakerCarriedFlourAndWood = false;
    for (int i = 0; i < 620 && w.Stock("bread") != 2; i++)
    {
        sim.Advance(.25);
        bakerCarriedFlourAndWood |= baker.Cargo.GetValueOrDefault("flour") == 2 && baker.Cargo.GetValueOrDefault("wood") == 1;
    }
    Check(bakerCarriedFlourAndWood && w.Stock("bread") == 2, "bread chain did not transport all inputs");
    Check(w.Stock("wheat") == 0 && w.Stock("flour") == 0 && w.Stock("wood") == 0 && w.Jobs.Count == 0
        && w.Discoveries.GetValueOrDefault("mill-wheat") == 2 && w.Discoveries.GetValueOrDefault("bake-bread") == 1,
        "repeat work did not conserve ingredients and outputs");
    Check(w.Npcs.All.All(n => n.Cargo.Count == 0 && n.Workplace is not null), "assignment or cargo state lost");
});

Test("a handcart is claimed once, carries heavy loads, survives a reload, and parks intact", () =>
{
    var sim = new Simulation();
    var w = NpcDemoIsland.Create();
    Check(w.Execute(new("place", 1, 0, "handcart")).Ok, "cart fixture");
    var cart = w.Objects.Single(o => o.Kind == "handcart");
    Check(NpcDemoIsland.TrySpawn(w, out var npc), "cart driver");
    Check(NpcDemoIsland.TrySpawn(w, out var other), "competing driver");
    npc.AutoHaul = other.AutoHaul = false;
    var save = JsonNode.Parse(SaveCodec.Encode(w))!;
    save["gameplay"]!["stock"]!["wood"] = 6;
    sim.Load(save.ToJsonString());
    w = sim.World;
    npc = w.Npcs.All[0];
    other = w.Npcs.All[1];
    var use = new NpcTask(NpcTaskKind.EquipCart, cart.X, cart.Z, "取用手推车", cart.Id);
    Check(NpcTasks.Assign(w, npc, use, out _), "cart could not be reached");
    Check(NpcTasks.Assign(w, other, use, out _), "alternate cart route");
    Advance(sim, 14);
    Check(w.Npcs.All.Count(n => n.CartId == cart.Id) == 1 && w.Objects.All(o => o.Id != cart.Id),
        "handcart was duplicated or disappeared");
    npc = w.Npcs.All.Single(n => n.HasCart);
    Check(npc.CarryCapacityKg == NpcConfig.CartCapacityKg, "cart load limit");
    Check(w.Execute(new("stockDrop", -10, 7, Item: "wood", Quantity: 6)).Ok, "create heavy load");
    var pile = w.Piles.Single();
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.PickupPile, pile.X, pile.Z, "搬运", pile.Id), out _), "load pickup");
    Advance(sim, 25);
    Check(npc.CarriedKg == 18 && w.Piles.Count == 0, "cart did not load over the resident's limit");
    Check(!NpcTasks.Assign(w, npc, new(NpcTaskKind.ParkCart, npc.CellX, npc.CellZ, "停放"), out _), "cart parked while too heavy");
    var valid = SaveCodec.Encode(w);
    var malformed = JsonNode.Parse(valid)!;
    malformed["residents"]![npc.Id == w.Npcs.All[0].Id ? 1 : 0]!["cartId"] = cart.Id;
    bool rejectedDuplicate = false;
    try { SaveCodec.Decode(malformed.ToJsonString()); }
    catch (FormatException) { rejectedDuplicate = true; }
    Check(rejectedDuplicate, "duplicate cart accepted in save");
    sim.Load(valid);
    w = sim.World;
    npc = w.Npcs.All.Single(n => n.HasCart);
    Check(npc.CartId == cart.Id && npc.CarriedKg == 18, "cart or cargo lost on reload");
    var warehouse = w.Objects.First(o => World.StorageCapacity(o.Kind) > 0);
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.Store, warehouse.X, warehouse.Z, "存放", warehouse.Id), out _), "drive to storage");
    Advance(sim, 27);
    Check(npc.Cargo.Count == 0 && w.Stock("wood") == 6, "cart payload lost");
    Check(NpcTasks.Assign(w, npc, new(NpcTaskKind.ParkCart, npc.CellX, npc.CellZ, "停放"), out _), "park empty cart");
    Advance(sim, 2);
    Check(!npc.HasCart && w.Objects.Single(b => b.Id == cart.Id).Kind == "handcart", "parked cart did not keep its identity");
});

Console.WriteLine($"{passed} npc and island checks passed.");
