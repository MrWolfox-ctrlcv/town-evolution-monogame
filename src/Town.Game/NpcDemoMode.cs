using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Core.Residents;
using Town.Game.Interaction;
using Town.Game.Rendering;

namespace Town.Game;

/// <summary>在功能测试岛或正式岛试玩中显示并指派居民。</summary>
public sealed partial class TownGame
{
    private NpcRenderer? npcRenderer;
    private GpuSurface? residentMarkers;
    private int selectedNpc;
    private enum ResidentOrder { Context, Dig, Workplace }
    private ResidentOrder residentOrder;
    private long haulCheckStart;
    private int haulCheckExpectedStock;

    private void LoadNpcIsland()
    {
        if (options.NpcDemo) simulation.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
        else if (options.NpcIslandSnapshot is string snapshot) simulation.Load(File.ReadAllText(snapshot));
        Rebuild();
        InitializeNpcDemo();
    }

    private void InitializeNpcDemo()
    {
        npcRenderer = new NpcRenderer(GraphicsDevice);
        var world = simulation.World;
        SeedResidents(world);
        npcRenderer.Update(world, 0, 0);
        message = "左键选择居民，右键移动或工作。";
    }

    private void SeedResidents(World world)
    {
        if (world.Npcs.Count == 0)
        {
            NpcDemoIsland.TrySpawn(world, out _);
            NpcDemoIsland.TrySpawn(world, out _);
        }
        selectedNpc = 0;
    }

    private void FrameResidentTown()
    {
        if (!options.NpcTown || simulation.World.Npcs.All.FirstOrDefault() is not { } npc) return;
        camera.Target = new Vector3(npc.X, .8f, npc.Z);
        camera.Zoom = 14;
    }

    private NpcState SelectResidentForCheck()
    {
        var world = simulation.World;
        var npc = world.Npcs.All.First();
        var viewport = GraphicsDevice.Viewport;
        var point = camera.Project(viewport, npcRenderer!.HeadCenter(npc));
        var hit = WorldPicking.Pick(world, camera, viewport, point, npcRenderer);
        if (hit?.Type != "resident" || hit.Id != npc.Id)
            throw new InvalidOperationException("Resident head was not selectable at its rendered position.");
        TapWorld(point);
        return npc;
    }

    private void StartNpcWalkCheck(Cell goal)
    {
        var world = simulation.World;
        var npc = SelectResidentForCheck();
        var y = world.Height(goal.X, goal.Z) * World.Step;
        OrderResidentAt(camera.Project(GraphicsDevice.Viewport, new(goal.X + .5f, y, goal.Z + .5f)));
        if (!npc.HasRemainingPath || npc.Task is not { Kind: NpcTaskKind.GoTo } task ||
            task.TargetX != goal.X || task.TargetZ != goal.Z)
            throw new InvalidOperationException("The projected ground point did not order movement to its own tile.");
    }

    private void StartNpcHaulCheck()
    {
        var world = simulation.World;
        var npc = SelectResidentForCheck();
        var farm = world.Objects.First(b => world.Farm(b.Id)?.Cells[0] is { Crop: "wheat" });
        var farmPoint = WorldPicking.FarmCenter(farm, 0);
        farmPoint.Y = world.Height(farm.X, farm.Z) * World.Step + .6f;
        OrderResidentAt(camera.Project(GraphicsDevice.Viewport, farmPoint));
        if (npc.Task is not { Kind: NpcTaskKind.Harvest } task || task.SourceId != farm.Id)
            throw new InvalidOperationException("The contextual order did not start a wheat harvest.");
        var crop = world.Farm(farm.Id)!.Cells[task.FarmCell]!;
        haulCheckExpectedStock = world.Stock("wheat") + crop.Yield;
        haulCheckStart = world.Clock;
    }

    private bool NpcHaulCheckFinished()
    {
        if (!options.NpcHaulCheck) return true;
        var world = simulation.World;
        if (world.Stock("wheat") == haulCheckExpectedStock && world.LastResidentDeposit is not null &&
            world.Npcs.All.All(npc => npc.Cargo.Count == 0)) return true;
        if (world.Clock - haulCheckStart > 800)
            throw new InvalidOperationException("The harvested wheat did not reach a warehouse.");
        return false;
    }

    private void DrawNpcDemo(BasicEffect effect)
    {
        var world = simulation.World;
        DrawResidentMarkers(world, selected);
        effect.World = Matrix.Identity;
        effect.LightingEnabled = true;
        npcRenderer?.Draw(GraphicsDevice, effect, world);
        effect.World = Matrix.Identity;
        // 站位标记贴地画一层，不写深度，避免被地形抢掉。
        GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        GraphicsDevice.BlendState = BlendState.Opaque;
        effect.LightingEnabled = false;
        foreach (var pass in effect.CurrentTechnique.Passes) pass.Apply();
        residentMarkers?.Draw(GraphicsDevice);
        effect.LightingEnabled = true;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
    }

    private void DisposeNpcDemo()
    {
        npcRenderer?.Dispose();
        residentMarkers?.Dispose();
        npcRenderer = null;
        residentMarkers = null;
    }

    /// <summary>演示岛右下角的居民面板：数值、状态与两个按钮。</summary>
    private void DrawResidentPanel(int w, int h)
    {
        if (npcRenderer is null) return;
        var world = simulation.World;
        var npc = world.Npcs.All.FirstOrDefault(n => n.Id == selectedNpc);
        if (npc is null) return;
        bool showWorkplaceRow = npc.Workplace is not null || residentOrder == ResidentOrder.Workplace;
        int parkRow = showWorkplaceRow ? 260 : 220;
        int height = npc.HasCart ? parkRow + 42 : showWorkplaceRow ? 262 : 225;
        int x = Math.Max(8, w - HudMetrics.TrayWidth - 296), y = h - height - 14;
        var panel = new Rectangle(x, y, 286, height);
        ui.Panel(panel); ui.Block(panel);
        string workplace = npc.Workplace is { } work ? World.ItemNames[World.Processing[work.Recipe].Outputs[0].Item] : "无";
        ui.Text($"居民 {npc.Id} · 岗位 {workplace}", new(x + 14, y + 9), 16);
        ui.Text(npc.Status, new(x + 14, y + 37), 14);
        ui.Text($"饱食 {npc.Hunger}  体力 {npc.Stamina}  负重 {npc.CarriedKg:0.#}/{npc.CarryCapacityKg}kg", new(x + 14, y + 61), 14);
        int extra = Math.Max(0, npc.Cargo.Count - 2);
        string cargo = npc.Cargo.Count == 0 ? "无" : string.Join("、", npc.Cargo.Take(2).Select(p => $"{World.ItemNames[p.Key]} ×{p.Value}"));
        if (extra > 0) cargo += $" 等 {npc.Cargo.Count} 种";
        ui.Text($"随身 {cargo}", new(x + 14, y + 86), 14);
        ui.Text($"任务 {npc.Task?.Label ?? "无"}", new(x + 14, y + 111), 14);
        ui.Button(new(x + 14, y + 137, 128, 34), npc.AutoHaul ? "自动搬运 开" : "自动搬运 关", () => ToggleHauling(npc), npc.AutoHaul, 14);
        ui.Button(new(x + 152, y + 137, 120, 34), "取消任务", CancelSelectedTask, false, 14);
        bool digging = residentOrder == ResidentOrder.Dig, staffing = residentOrder == ResidentOrder.Workplace;
        ui.Button(new(x + 14, y + 179, 128, 34), digging ? "右键选择土地" : "指派挖掘",
            () => residentOrder = digging ? ResidentOrder.Context : ResidentOrder.Dig, digging, 14);
        ui.Button(new(x + 152, y + 179, 120, 34), staffing ? "右键选择工坊" : "指派岗位",
            () => residentOrder = staffing ? ResidentOrder.Context : ResidentOrder.Workplace, staffing, 14);
        if (npc.Workplace is not null)
            ui.Button(new(x + 14, y + 220, 258, 30), "解除岗位", () =>
            {
                npc.Workplace = null;
                if (npc.Task is { Automatic: true, Kind: NpcTaskKind.Process or NpcTaskKind.FetchSupplies or NpcTaskKind.PickupJob }) npc.ClearTask();
                residentOrder = ResidentOrder.Context;
            }, false, 14);
        else if (staffing) ui.Text("石磨按住 Shift 可选碾米", new(x + 14, y + 225), 13);
        if (npc.HasCart)
            ui.Button(new(x + 14, y + parkRow, 258, 30), "停放手推车", () =>
                AssignResidentOrder(world, npc, new(NpcTaskKind.ParkCart, npc.CellX, npc.CellZ, "停放手推车")), false, 14);
    }

    private void SpawnOneResident(int homeId)
    {
        var world = simulation.World;
        if (world.Npcs.TryAdd(world, homeId, out var npc))
        {
            selectedNpc = npc.Id;focusedBuilding = 0;residentOrder = ResidentOrder.Context;
            Notice($"居民 {npc.Id} 已入住");
        }
        else Notice("住宅旁没有空位了");
    }

    private void CancelSelectedTask()
    {
        var npc = simulation.World.Npcs.All.FirstOrDefault(n => n.Id == selectedNpc);
        if (npc is null) return;
        npc.ClearTask();
        npc.AutoHaul = false;
        npc.Workplace = null;
        residentOrder = ResidentOrder.Context;
        Notice($"居民 {npc.Id} 已停下");
    }

    private static void ToggleHauling(NpcState npc)
    {
        npc.AutoHaul = !npc.AutoHaul;
        if (!npc.AutoHaul && npc.Task is { Automatic: true } task &&
            (task.Kind is NpcTaskKind.PickupDrop or NpcTaskKind.PickupFarmBox
                || npc.Workplace is null && task.Kind is NpcTaskKind.Store or NpcTaskKind.PickupJob)) npc.ClearTask();
    }
}
