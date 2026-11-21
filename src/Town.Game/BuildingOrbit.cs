using Microsoft.Xna.Framework;
using Town.Core;
using Town.Core.Residents;
using Town.Game.Interaction;

namespace Town.Game;

public sealed partial class TownGame
{
    private int buildingActionPage;

    private sealed record BuildingAction(string Label, string? Icon, Action Execute, string Face = "", string? Detail = null);

    private void DrawBuildingOrbit(int width, int height, PlacedObject building)
    {
        if (BuildingScreenBounds(building) is not { } model || !model.Intersects(new(0, 0, width, height))) return;
        var actions = GetBuildingActions(building);
        const int maxButtons = 8;
        for (int pageSize = Math.Min(actions.Count, maxButtons); pageSize >= 2; pageSize--)
        {
            bool paged = actions.Count > pageSize;
            int perPage = paged ? pageSize - 1 : pageSize;
            int pages = (actions.Count + perPage - 1) / perPage;
            int page = buildingActionPage % pages;
            var visible = actions.Skip(page * perPage).Take(perPage).ToList();
            if (paged) visible.Add(new($"下一页 {page + 1}/{pages}", null, () => buildingActionPage = (page + 1) % pages, "›"));
            if (PlaceBuildingActions(width, height, model, visible.Count) is not { } buttons) continue;

            string? detail = null;
            var pointer = MousePoint() / ui.Scale;
            for (int i = 0; i < visible.Count; i++)
            {
                var action = visible[i];
                var rect = buttons[i];
                ui.ActionDisc(rect, action.Icon, action.Label, $"building:{building.Id}:{action.Label}", () =>
                {
                    if (focusedBuilding == building.Id && simulation.World.Objects.Contains(building)) action.Execute();
                }, action.Face);
                if (Vector2.DistanceSquared(pointer, rect.Center.ToVector2()) < rect.Width*rect.Width/4f)
                    detail = action.Detail ?? action.Label;
            }

            string description = detail ?? World.Catalog[building.Kind].Name;
            int labelWidth = Math.Min(width - 12, description.Sum(c => c > 127 ? 13 : 7) + 18);
            int labelX = Math.Clamp(model.Center.X - labelWidth / 2, 6, width - labelWidth - 6);
            foreach (int y in new[] { model.Bottom + 8, model.Top - 29, buttons.Max(r => r.Bottom) + 5 })
            {
                var label = new Rectangle(labelX, y, labelWidth, 22);
                if (y < 0 || label.Bottom > height || ui.Occupies(label)) continue;
                ui.Panel(label);
                ui.Text(description, new(label.X + 7, label.Y + 1), 13);
                break;
            }
            return;
        }
    }

    private Rectangle? BuildingScreenBounds(PlacedObject building)
    {
        var world = simulation.World;
        var spec = World.Catalog[building.Kind];
        var farm = world.Farm(building.Id);
        string? asset = farm is not null ? WorldPicking.FarmAsset(farm) :
            building.Kind == "nursery" ? WorldPicking.NurseryAsset(world, building.Id) : spec.Asset;
        if (asset is null) return null;
        var (w, d) = World.Footprint(building.Kind, building.Rotation);
        var rotation = Matrix.CreateRotationY(building.Rotation * MathHelper.PiOver2);
        float height = world.Height(building.X, building.Z) * World.Step;
        var viewport = GraphicsDevice.Viewport;
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);

        void Include(string kind, Matrix transform)
        {
            if (BakedGeometry.ModelBounds(kind) is not { } bounds) return;
            foreach (var corner in bounds.GetCorners())
            {
                var point = camera.Project(viewport, Vector3.Transform(corner, transform)) / ui.Scale;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        Include(asset, Matrix.CreateScale(spec.Scale) * rotation *
            Matrix.CreateTranslation(building.X + w * .5f, height, building.Z + d * .5f));
        if (farm is not null)
            for (int i = 0; i < farm.Cells.Length; i++)
            {
                if (farm.Cells[i] is not { } crop) continue;
                var center = WorldPicking.FarmCenter(building, i);
                Include(WorldPicking.CropAsset(world, crop), rotation * Matrix.CreateTranslation(center.X, height, center.Z));
            }

        if (!float.IsFinite(min.X)) return null;
        int left = (int)MathF.Floor(min.X), top = (int)MathF.Floor(min.Y);
        return new(left, top, (int)MathF.Ceiling(max.X) - left, (int)MathF.Ceiling(max.Y) - top);
    }

    private Rectangle[]? PlaceBuildingActions(int width, int height, Rectangle model, int count)
    {
        const int size = 44, gap = 12;
        var viewport = new Rectangle(4, 4, width - 8, height - 8);
        var protectedModel = model;
        protectedModel.Inflate(gap, gap);
        var center = model.Center.ToVector2();
        foreach (int pad in new[] { 0, 35 })
        {
            float rx = Math.Max(model.Width * .5f + size * .75f + gap + pad, size * 1.38f);
            float ry = Math.Max(model.Height * .5f + size * .75f + gap + pad, size * 1.38f);
            foreach (float direction in new[] { -MathHelper.PiOver2, 0, MathHelper.Pi, MathHelper.PiOver2,
                         -MathHelper.PiOver4, -3 * MathHelper.PiOver4, MathHelper.PiOver4, 3 * MathHelper.PiOver4 })
            {
                var layout = new List<Rectangle>(count);
                bool orbit = count > 3;
                for (int i = 0; i < count; i++)
                {
                    float angle = direction + (orbit ? i * MathHelper.TwoPi / count : (i - (count - 1) * .5f) * .88f);
                    var p = center + new Vector2(MathF.Cos(angle) * rx, MathF.Sin(angle) * ry);
                    var rect = new Rectangle((int)p.X - size / 2, (int)p.Y - size / 2, size, size);
                    if (!viewport.Contains(rect) || protectedModel.Intersects(rect) || ui.Occupies(rect) || layout.Any(r => r.Intersects(rect))) break;
                    layout.Add(rect);
                }
                if (layout.Count == count) return layout.ToArray();
            }
        }
        return null;
    }

    private List<BuildingAction> GetBuildingActions(PlacedObject b)
    {
        var world = simulation.World;
        var actions = new List<BuildingAction>();
        void Add(string label, string? icon, WorldCommand command, string face = "", string? detail = null)
            => actions.Add(new(label, icon, () => Commit(command), face, detail));
        actions.Add(new("关闭建筑操作", null, () => focusedBuilding = 0, "×"));
        Add("旋转", "rotate", new("move", b.X, b.Z, Rotation: (b.Rotation + 1) % 4, ObjectId: b.Id, Entities: [b]));
        if (world.Jobs.FirstOrDefault(j => j.Id == b.Id) is { } job)
        {
            var output = World.Processing[job.Recipe].Outputs[0];
            string label = $"{World.ItemNames[output.Item]} ×{output.Amount}";
            if (job.ReadyTick <= world.Clock)
                Add("领取成品", IconName(output.Item), new("collectJob", b.X, b.Z, ObjectId: b.Id), detail: "领取 " + label);
            else
                actions.Add(new("加工进度", IconName(output.Item), () => Notice("加工中"),
                    Detail: $"{label} · 剩余 {(job.ReadyTick - world.Clock + 19) / 20} 秒"));
        }
        else
        {
            foreach (var (recipe, rule) in World.Processing.Where(p => p.Value.Building == b.Kind && p.Key != "make-fertilizer"))
            {
                bool attached = rule.Inputs.All(p => (world.Attachments.FirstOrDefault(a => a.Id == b.Id)?.Stock.GetValueOrDefault(p.Item) ?? 0) >= p.Amount);
                var output = rule.Outputs[0];
                string supplies = string.Join(" + ", rule.Inputs.Select(p => $"{World.ItemNames[p.Item]} ×{p.Amount}"));
                string label = $"加工 {World.ItemNames[output.Item]}";
                string detail = $"{supplies} → {World.ItemNames[output.Item]} ×{output.Amount}";
                if (recipe == "grow-sapling")
                    actions.Add(new(label, "watering-can", () => Notice("挂上树种后把水壶拖到育苗箱"), Detail: "树种 ×1 + 水壶 → 树苗"));
                else Add(label, IconName(output.Item), new("process", b.X, b.Z, Kind: attached ? "attached" : "", ObjectId: b.Id, Item: recipe), detail: detail);
            }
        }
        if (world.Farm(b.Id) is { Box: true } farm)
            Add("领取田箱", "manage", new("collectBox", b.X, b.Z, ObjectId: b.Id), detail: $"领取田箱 {farm.BoxStock?.Values.Sum() ?? 0}/16");
        if (npcRenderer is not null && b.Kind is "house" or "residence")
            actions.Add(new("入住居民", "town", () => SpawnOneResident(b.Id), Detail: $"入住居民 {world.Npcs.All.Count(n => n.Home == b.Id)}/{NpcRoster.SlotsPerResidence}"));
        if (world.Attachments.FirstOrDefault(a => a.Id == b.Id) is { } attachment)
            foreach (var (item, amount) in attachment.Stock.Where(p => p.Value > 0))
                Add("取回 " + World.ItemNames[item], IconName(item), new("detach", b.X, b.Z, ObjectId: b.Id, Item: item),
                    detail: $"取回 {World.ItemNames[item]} · 已挂载 {amount}");
        Add("拆除", null, new("remove", b.X, b.Z), "−");
        return actions;
    }
}
