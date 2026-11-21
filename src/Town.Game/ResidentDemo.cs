using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Core.Residents;
using Town.Game.Interaction;
using Town.Game.Rendering;

namespace Town.Game;

/// <summary>演示岛上的居民选择、指派与站位标记。只改居民任务，不改世界规则。</summary>
public sealed partial class TownGame
{
    private static readonly Color MarkerIdle = new(96, 148, 210);
    private static readonly Color MarkerMove = new(228, 190, 92);

    private readonly record struct ResidentMarker(Cell Cell, int Height, Color Color);
    private ResidentMarker[] residentMarkerLayout = [];

    private void OrderResidentAt(Vector2 point)
    {
        if (npcRenderer is null || selectedNpc == 0 || ui.Hit(point, false)) return;
        var world = simulation.World;
        var hit = WorldPicking.Pick(world, camera, GraphicsDevice.Viewport, point, npcRenderer);
        if (hit is null || hit.Type == "resident") return;
        var npc = world.Npcs.All.FirstOrDefault(n => n.Id == selectedNpc);
        if (npc is null) return;
        if (residentOrder == ResidentOrder.Workplace)
        {
            var building = hit.Type == "building" ? world.Occupant(hit.X, hit.Z) : null;
            string? recipe = building is null ? null : SelectWorkRecipe(world, npc, building, ongoing: true);
            if (building is null || recipe is null) { Notice("请右键选择石磨、石窑或烘焙坊"); return; }
            if (NpcTasks.SetWorkplace(world, npc, building.Id, recipe, out var reason)) residentOrder = ResidentOrder.Context;
            Notice(reason);
            return;
        }
        if (residentOrder == ResidentOrder.Dig || ShiftDown() && hit.Type is ("ground" or "resource"))
        {
            if (hit.Type is not ("ground" or "resource")) { Notice("请右键选择可挖掘的地块"); return; }
            if (AssignResidentOrder(world, npc, new(NpcTaskKind.Dig, hit.X, hit.Z, "挖掘"))) residentOrder = ResidentOrder.Context;
            return;
        }
        if (hit is { Type: "farm", FarmCell: >= 0 })
        {
            var crop = world.Farm(hit.Id)?.Cells[hit.FarmCell];
            if (crop is null || crop.ReadyTick > world.Clock) { Notice("这里没有成熟作物"); return; }
        }

        NpcTask task = hit.Type switch
        {
            "tree" => new(NpcTaskKind.Chop, hit.X, hit.Z, "伐木"),
            "farm" when hit.FarmCell >= 0 => new(NpcTaskKind.Harvest, hit.X, hit.Z, "收割", hit.Id, hit.FarmCell),
            "drop" => new(NpcTaskKind.PickupDrop, hit.X, hit.Z, "拾取掉落物", hit.Id),
            "pile" => new(NpcTaskKind.PickupPile, hit.X, hit.Z, "搬运材料", hit.Id),
            "building" when world.Occupant(hit.X, hit.Z) is { } building && World.StorageCapacity(building.Kind) > 0
                && npc.Cargo.Count > 0 => new(NpcTaskKind.Store, hit.X, hit.Z, "存放物品", hit.Id),
            "building" when world.Occupant(hit.X, hit.Z)?.Kind == "handcart" =>
                new(NpcTaskKind.EquipCart, hit.X, hit.Z, "取用手推车", hit.Id),
            "building" when WorkstationOrder(world, npc, hit) is { } work => work,
            _ => NpcTask.WalkTo(hit.X, hit.Z, $"{hit.X},{hit.Z}")
        };
        AssignResidentOrder(world, npc, task);
    }

    private bool AssignResidentOrder(World world, NpcState npc, NpcTask task)
    {
        bool assigned = NpcTasks.Assign(world, npc, task, out string message);
        if (!assigned) Notice(message);
        reviewCapturePending = options.ReviewDirectory is not null;
        return assigned;
    }

    private static NpcTask? WorkstationOrder(World world, NpcState npc, WorldHit hit)
    {
        var building = world.Occupant(hit.X, hit.Z);
        if (building is null) return null;
        if (world.Jobs.FirstOrDefault(job => job.Id == building.Id) is { } job)
            return job.ReadyTick <= world.Clock ? new(NpcTaskKind.PickupJob, hit.X, hit.Z, "领取成品", building.Id) : null;
        string? recipe = SelectWorkRecipe(world, npc, building, ongoing: false);
        return recipe is null ? null : new(NpcTaskKind.Process, hit.X, hit.Z,
            "加工 " + World.ItemNames[World.Processing[recipe].Outputs[0].Item], building.Id, Item: recipe);
    }

    private static string? SelectWorkRecipe(World world, NpcState npc, PlacedObject building, bool ongoing)
    {
        var attachment = world.Attachments.FirstOrDefault(a => a.Id == building.Id)?.Stock;
        var recipes = World.Processing.Where(p => p.Value.Building == building.Kind && NpcTasks.CanStaff(p.Key)).ToArray();
        if (recipes.Length == 0) return null;
        if (ShiftDown()) return recipes[^1].Key;
        if (ongoing) return recipes[0].Key;
        return recipes.FirstOrDefault(r => world.CanSupplyResident(npc, r.Key)
            || attachment is not null && r.Value.Inputs.All(i => attachment.GetValueOrDefault(i.Item) >= i.Amount)).Key
            ?? recipes[0].Key;
    }

    /// <summary>地面标记：选中圈和目的地旗帜。</summary>
    private void DrawResidentMarkers(World world, Cell? hovered)
    {
        var layout = new List<ResidentMarker>();
        void Ring(Cell cell, Color color) => layout.Add(new(cell, world.Height(cell.X, cell.Z), color));
        foreach (var npc in world.Npcs.All)
            if (npc.Id == selectedNpc) Ring(npc.Cell, MarkerMove);
        foreach (var npc in world.Npcs.All)
            if (npc.Id == selectedNpc && npc.Goal is Cell goal) Ring(goal, MarkerMove);
        if (hovered is Cell h && world.Npcs.All.Any(n => n.Cell == h)) Ring(h, MarkerIdle);
        if (layout.SequenceEqual(residentMarkerLayout)) return;
        residentMarkerLayout = layout.ToArray();
        var markers = new SurfaceMesh();
        foreach (var marker in residentMarkerLayout)
        {
            float x = marker.Cell.X, z = marker.Cell.Z, y = marker.Height * World.Step + .03f;
            var color = marker.Color;
            markers.Box(new(x, y, z), new(1, .02f, .07f), color);
            markers.Box(new(x, y, z + .93f), new(1, .02f, .07f), color);
            markers.Box(new(x, y, z), new(.07f, .02f, 1), color);
            markers.Box(new(x + .93f, y, z), new(.07f, .02f, 1), color);
        }
        residentMarkers?.Dispose();
        residentMarkers = new(GraphicsDevice, markers);
    }
}
