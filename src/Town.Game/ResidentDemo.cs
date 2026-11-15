using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Core.Residents;
using Town.Game.Rendering;

namespace Town.Game;

/// <summary>演示岛上的居民选择、指派与站位标记。只改居民任务，不改世界规则。</summary>
public sealed partial class TownGame
{
    private static readonly Color MarkerIdle = new(96, 148, 210);
    private static readonly Color MarkerMove = new(228, 190, 92);

    /// <summary>点到的格子若有居民，选中他；没有则清空选择。</summary>
    private void SelectResidentAt(Cell cell)
    {
        var hit = simulation.World.Npcs.All.FirstOrDefault(n => n.CellX == cell.X && n.CellZ == cell.Z);
        selectedNpc = hit?.Id ?? 0;
        if (hit is not null) Notice($"选中居民 {hit.Id}：{hit.Status}");
    }

    /// <summary>Shift+点击：让选中的居民走到那一格。目标不可站时退到最近的可站立邻格。</summary>
    private void AssignSelectedResident(Cell cell)
    {
        var world = simulation.World;
        var npc = world.Npcs.All.FirstOrDefault(n => n.Id == selectedNpc);
        if (npc is null) { Notice("先点一下居民，再按住 Shift 点目标格"); return; }

        var target = cell;
        if (!NpcNavigation.Passable(world, target.X, target.Z))
        {
            var approach = NpcNavigation.ApproachCells(world, cell.X, cell.Z).FirstOrDefault();
            if (!World.Inside(approach.X, approach.Z)) { Notice("那附近没有能站的地方"); return; }
            target = approach;
        }

        if (NpcTasks.Assign(world, npc, NpcTask.WalkTo(target.X, target.Z, $"{target.X},{target.Z}"), out string message))
            Notice($"居民 {npc.Id} {message}");
        else Notice(message);
    }

    /// <summary>地面标记：选中圈和目的地旗帜。</summary>
    private void DrawResidentMarkers(World world, Cell? hovered)
    {
        var markers = new SurfaceMesh();
        void Ring(int x, int z, Color color)
        {
            float y = world.Height(x, z) * World.Step + .03f;
            foreach (var v in new[] { new Vector4(0, 0, 1, .07f), new(0, .93f, 1, .07f), new(0, 0, .07f, 1), new(.93f, 0, .07f, 1) })
                markers.Box(new(x + v.X, y, z + v.Y), new(v.Z, .02f, v.W), color);
        }
        foreach (var npc in world.Npcs.All)
            if (npc.Id == selectedNpc) Ring(npc.CellX, npc.CellZ, MarkerMove);
        foreach (var npc in world.Npcs.All)
            if (npc.Task is { Kind: NpcTaskKind.GoTo } task) Ring(task.TargetX, task.TargetZ, MarkerMove);
        if (hovered is Cell h && world.Npcs.All.Any(n => n.CellX == h.X && n.CellZ == h.Z)) Ring(h.X, h.Z, MarkerIdle);
        residentMarkers?.Dispose();
        residentMarkers = new(GraphicsDevice, markers);
    }
}
