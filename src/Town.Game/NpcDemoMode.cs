using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Core.Residents;
using Town.Game.Rendering;

namespace Town.Game;

/// <summary>NPC 演示岛：居民占位方块、点选与直接指派。正式随机岛不受影响。</summary>
public sealed partial class TownGame
{
    private NpcRenderer? npcRenderer;
    private GpuSurface? residentMarkers;
    private int selectedNpc;
    private bool npcDemo;

    /// <summary>把演示岛装上并重建全部渲染资源；正式岛不会走这里。</summary>
    private void UseNpcDemoIsland()
    {
        simulation.Load(SaveCodec.Encode(NpcDemoIsland.Create()));
        Rebuild();
        InitializeNpcDemo();
        Home();
        // 演示岛只有 24×18，镜头上贴近一点才看得清方块居民。
        camera.Target = new Vector3(0, .8f, 0);
        camera.Zoom = 13;
    }

    private void InitializeNpcDemo()
    {
        npcDemo = true;
        npcRenderer = new NpcRenderer(GraphicsDevice);
        var world = simulation.World;
        if (world.Npcs.Count == 0)
        {
            NpcDemoIsland.TrySpawn(world, out _);
            NpcDemoIsland.TrySpawn(world, out _);
        }
        selectedNpc = world.Npcs.All.FirstOrDefault()?.Id ?? 0;
        message = "演示岛：点居民选中，按住 Shift 点地面指派他走过去。";
    }

    private void DrawNpcDemo(BasicEffect effect)
    {
        var world = simulation.World;
        DrawResidentMarkers(world, selected);
        effect.World = Matrix.Identity;
        effect.LightingEnabled = true;
        npcRenderer?.Draw(GraphicsDevice, effect, world, selectedNpc);
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
        if (!npcDemo) return;
        var world = simulation.World;
        var npc = world.Npcs.All.FirstOrDefault(n => n.Id == selectedNpc) ?? world.Npcs.All.FirstOrDefault();
        int x = w - 300, y = h - 214;
        ui.Panel(new(x, y, 286, 200)); ui.Block(new(x, y, 286, 200));
        if (npc is null)
        {
            ui.Text("还没有居民", new(x + 14, y + 14), 18);
            ui.Button(new(x + 14, y + 56, 258, 40), "在住宅旁放一名", SpawnOneResident, false, 16);
            return;
        }
        ui.Text($"居民 {npc.Id}  {npc.Status}", new(x + 14, y + 10), 16);
        ui.Text($"饱食 {npc.Hunger}  体力 {npc.Stamina}  负重 {npc.CarriedKg:0.#}/{NpcConfig.MaxCarryKg}kg", new(x + 14, y + 42), 14);
        ui.Text($"当前速度 {npc.SpeedCellsPerSecond:0.00} 格/秒   所在格 {npc.CellX},{npc.CellZ}", new(x + 14, y + 68), 14);
        ui.Text($"任务 {(npc.Task is null ? "无" : npc.Task.Label)}", new(x + 14, y + 94), 14);
        ui.Button(new(x + 14, y + 132, 128, 40), "放一名居民", SpawnOneResident, false, 15);
        ui.Button(new(x + 152, y + 132, 120, 40), "取消任务", CancelSelectedTask, false, 15);
    }

    private void SpawnOneResident()
    {
        if (NpcDemoIsland.TrySpawn(simulation.World, out var npc))
        {
            selectedNpc = npc.Id;
            Notice($"居民 {npc.Id} 已入住");
        }
        else Notice("住宅旁没有空位了");
    }

    private void CancelSelectedTask()
    {
        var npc = simulation.World.Npcs.All.FirstOrDefault(n => n.Id == selectedNpc);
        if (npc is null) return;
        npc.ClearTask();
        Notice($"居民 {npc.Id} 已停下");
    }
}
