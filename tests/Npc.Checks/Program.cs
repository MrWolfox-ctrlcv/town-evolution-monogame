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
    Check(w.Npcs.TryAdd(w, residence.Id, out _), "second spawn");
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
    npc.CarriedKg = NpcConfig.MaxCarryKg;
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
    npc.Hunger = 61; npc.Stamina = 77; npc.CarriedKg = 5.25;
    NpcTasks.Assign(w, npc, NpcTask.WalkTo(1, 0, "台阶"), out _);
    var json = SaveCodec.Encode(w);
    var back = SaveCodec.Decode(json);
    Check(back.IslandTag == NpcDemoIsland.Tag, "island tag lost");
    Check(back.Npcs.Count == 1, "resident lost");
    var restored = back.Npcs.All[0];
    Check(restored.Hunger == 61 && restored.Stamina == 77, "stats lost");
    Check(Math.Abs(restored.CarriedKg - 5.25) < 1e-6, "carry lost");
    Check(restored.Task?.TargetX == 1 && restored.Task.TargetZ == 0, "task lost");
    Check(SaveCodec.Encode(back) == json, "round trip not stable");
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

Console.WriteLine($"{passed} npc checks passed.");
