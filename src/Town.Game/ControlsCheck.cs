using Microsoft.Xna.Framework;
using Town.Core;
using Town.Game.Interaction;

namespace Town.Game;

/// <summary>Uses rendered UI controls and the in-game pointer path on an isolated copy of the town.</summary>
public sealed partial class TownGame
{
    private bool controlsCheckPassed;

    private void AdvanceControlsCheck()
    {
        if (controlsCheckPassed || frames < 4) return;
        var world = simulation.World;
        var viewport = GraphicsDevice.Viewport;
        void Require(bool ok, string reason)
        {
            if (!ok) throw new InvalidOperationException(reason);
        }
        void Click(Vector2 p)
        {
            GamePointer(p, true, false);
            GamePointer(p, false, true);
        }

        if (frames >= 5 && focusedBuilding != 0)
        {
            Require(ui.ButtonCenter("田地") is null && ui.ButtonCenter("挖掘") is null &&
                ui.ButtonCenter("填土") is null, "Tapping the excavation tool opened a mode picker.");
            Require(ui.ButtonCenter("加工 烤胡萝卜") is Vector2, "The kiln's recipe action was missing.");
            var close = ui.ButtonCenter("关闭建筑操作")!.Value;
            Require(ui.ControlAt(close + new Vector2(20, 20) * ui.Scale) is null, "The dead space outside the disc accepted UI input.");
            Click(close);
            Require(focusedBuilding == 0, "The close disc did not dismiss the building.");
            var select = FindBuildingPixel(world);
            Click(select);
            controlsCheckPassed = true;
            return;
        }

        Vector2 ground = FindGroundPixel(world);
        var npc = SelectResidentForCheck();
        Click(ground);
        Require(selectedNpc == 0, "Left-clicking ground did not deselect the resident.");
        OrderResidentAt(ground);
        Require(npc.Task is null, "Right-click sent an order when no resident was selected.");

        SelectResidentForCheck();
        ClearWorldSelection();
        Require(selectedNpc == 0 && press is null && dragMode is null, "Clearing selection left a resident or drag active.");

        var axe = ui.ButtonCenter("斧子")!.Value;
        var saw = ui.ButtonCenter("锯子")!.Value;
        Require(axe.Y < saw.Y, "The axe should precede the saw.");
        var tool = ui.ButtonCenter("挖掘工具")!.Value;
        Click(tool);
        Require(simulation.PendingCount == 0 && dragMode is null, "Tapping the excavation tool changed the world.");
        var farm = FindToolGround(world, c => world.Height(c.X, c.Z)>0 && !world.IsExcavationTarget(c)
            && world.CanPlace("farm", c.X, c.Z, 0).Ok);
        GamePointer(tool, true, false);
        GamePointer(farm, false, false);
        Require(commandPreview is { Result.Ok: true, Ghost.Kind: "farm" }, "The shovel did not preview a farm on empty level ground.");
        GamePointer(tool, false, true);
        Require(simulation.PendingCount == 0, "Returning the shovel planted a farm.");
        GamePointer(axe, true, false);
        GamePointer(ground, false, false);
        Require(dragMode == "tool" && commandPreview?.Result.Ok == false, "The axe did not reject plain ground.");
        var tree = FindTreePixel(world);
        GamePointer(tree, false, false);
        Require(commandPreview?.Result.Ok == true, "A valid tree could not be targeted after leaving invalid ground.");
        GamePointer(ground, false, true);
        Require(simulation.PendingCount == 0 && dragMode is null && commandPreview is null, "A rejected drop was committed or kept its preview.");

        var mountain = GroundCells().Where(c => world.Surface(c.X, c.Z) == GroundSurface.Stone)
            .OrderByDescending(c => world.Height(c.X, c.Z)).First();
        camera.Target = new(mountain.X + .5f, world.Height(mountain.X, mountain.Z) * World.Step, mountain.Z + .5f);
        camera.Update(viewport);
        var rock = FindToolGround(world, c => world.Surface(c.X, c.Z) == GroundSurface.Stone);
        GamePointer(tool, true, false);
        GamePointer(rock, false, false);
        Require(proposed?.Type == "terrainDig" && commandPreview?.Result.Ok == true,
            "The same shovel did not switch from farming to mining over stone.");
        GamePointer(rock, false, true);
        Require(simulation.PendingCount == 1, "Releasing over stone did not queue exactly one excavation.");
        AdvanceSimulation(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.05)));
        Require(world.Drops.Any(d => d.Item == "stone") && simulation.LastResult.Ok, "Stone was not excavated.");

        var oven = world.Objects.First(o => o.Kind == "oven");
        var (w, d) = World.Footprint(oven.Kind, oven.Rotation);
        camera.Target = new(oven.X + w * .5f, world.Height(oven.X, oven.Z) * World.Step + .7f, oven.Z + d * .5f);
        camera.Zoom = 10;
        camera.Update(viewport);
        Click(FindBuildingPixel(world));
        Require(focusedBuilding == oven.Id && selectedNpc == 0, "Selecting the oven did not replace the resident selection.");
    }

    private static IEnumerable<Cell> GroundCells()
    {
        for (int z = -World.Half; z < World.Half; z++)
            for (int x = -World.Half; x < World.Half; x++)
                yield return new(x, z);
    }

    private Vector2 FindToolGround(World world, Func<Cell, bool> predicate)
    {
        var viewport = GraphicsDevice.Viewport;
        foreach (var cell in GroundCells().Where(predicate))
        {
            var point = camera.Project(viewport, new(cell.X + .5f, world.Height(cell.X, cell.Z)*World.Step, cell.Z + .5f));
            if (!viewport.Bounds.Contains(point) || ui.Hit(point, false)) continue;
            var hit = WorldPicking.Pick(world, camera, viewport, point);
            if (hit?.Type == "ground" && hit.X == cell.X && hit.Z == cell.Z) return point;
        }
        throw new InvalidOperationException("No visible terrain matched the excavation fixture.");
    }

    private Vector2 FindGroundPixel(World world)
    {
        var viewport = GraphicsDevice.Viewport;
        for (int y = viewport.Height / 3; y < viewport.Height * .8f; y += 16)
            for (int x = viewport.Width / 3; x < viewport.Width * .65f; x += 16)
            {
                var point = new Vector2(x, y);
                if (!ui.Hit(point, false) && WorldPicking.Pick(world, camera, viewport, point, npcRenderer)?.Type == "ground")
                    return point;
            }
        throw new InvalidOperationException("No unobstructed land under the viewport.");
    }

    private Vector2 FindTreePixel(World world)
    {
        var viewport = GraphicsDevice.Viewport;
        foreach (var tree in world.LiveTrees)
        {
            float y = world.Height(tree.X, tree.Z) * World.Step;
            var point = camera.Project(viewport, new(tree.X + .5f, y + 1.2f, tree.Z + .5f));
            if (!viewport.Bounds.Contains(point) || ui.Hit(point, false)) continue;
            if (WorldPicking.Pick(world, camera, viewport, point)?.Type == "tree") return point;
        }
        throw new InvalidOperationException("No visible tree was available for the axe check.");
    }

    private Vector2 FindBuildingPixel(World world)
    {
        var viewport = GraphicsDevice.Viewport;
        int id = world.Objects.First(o => o.Kind == "oven").Id;
        var middle = viewport.Bounds.Center;
        for (int y = middle.Y - 30; y <= middle.Y + 30; y += 4)
            for (int x = middle.X - 30; x <= middle.X + 30; x += 4)
            {
                var point = new Vector2(x, y);
                if (!ui.Hit(point, false) && WorldPicking.Pick(world, camera, viewport, point, npcRenderer) is { Type: "building" } hit && hit.Id == id)
                    return point;
            }
        throw new InvalidOperationException("The oven mesh could not be selected.");
    }
}
