using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Core.Residents;
using Town.Game.Interaction;

namespace Town.Game.Rendering;

public sealed class NpcRenderer : IDisposable
{
    private enum Joint { Body, Head, LeftArm, RightArm, Forearm, LeftLeg, RightLeg, LeftShin, RightShin }
    private sealed record Cuboid(float[] Min, float[] Max, string Color);
    private sealed record Part(string Name, string Joint, float[] Pivot, Cuboid[] Boxes,
        int? Outfit = null, bool LoadedOnly = false, string? Parent = null);
    private sealed record Model(int UnitsPerTile, Dictionary<string, string> Palette,
        Dictionary<string, string>[] Outfits, Part[] Parts);

    private sealed class Pose(NpcState npc, int partCount)
    {
        public Vector2 Position = new(npc.X, npc.Z);
        public float Facing = npc.Facing;
        public float Stride;
        public float WalkBlend;
        public readonly Matrix[] Parts = new Matrix[partCount];
    }

    private readonly record struct MeshPart(Joint Joint, Vector3 Pivot, BoundingBox[] Boxes,
        GpuSurface?[] Surfaces, bool LoadedOnly, int Parent);

    private readonly MeshPart[] parts;
    private readonly int head;
    private readonly int outfitCount;
    private readonly Dictionary<int, Pose> poses = [];
    private readonly GpuSurface shadow;
    private World? currentWorld;

    public NpcRenderer(GraphicsDevice device)
    {
        using var stream = typeof(NpcRenderer).Assembly.GetManifestResourceStream("Town.Game.Resident.json")
            ?? throw new InvalidOperationException("Resident model is missing.");
        var model = JsonSerializer.Deserialize<Model>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Resident model is empty.");
        Vector3 Position(float[] point) => new Vector3(point[0], point[1], point[2]) / model.UnitsPerTile;
        parts = new MeshPart[model.Parts.Length];
        head = Array.FindIndex(model.Parts, part => part.Name == "head");
        outfitCount = model.Outfits.Length;

        for (int i = 0; i < model.Parts.Length; i++)
        {
            var part = model.Parts[i];
            var pivot = Position(part.Pivot);
            var boxes = part.Boxes.Select(box => new BoundingBox(Position(box.Min) - pivot, Position(box.Max) - pivot)).ToArray();
            var surfaces = new GpuSurface?[outfitCount];
            for (int outfit = 0; outfit < surfaces.Length; outfit++)
            {
                if (part.Outfit is int only && outfit != only) continue;
                var mesh = new SurfaceMesh();
                for (int j = 0; j < boxes.Length; j++)
                {
                    var slot = part.Boxes[j].Color;
                    var hex = model.Outfits[outfit].GetValueOrDefault(slot) ?? model.Palette[slot];
                    var rgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    mesh.Box(boxes[j].Min, boxes[j].Max - boxes[j].Min, new Color(rgb >> 16, (rgb >> 8) & 255, rgb & 255));
                }
                surfaces[outfit] = new GpuSurface(device, mesh);
            }
            int parent = part.Parent is null ? -1 : Array.FindIndex(model.Parts, p => p.Name == part.Parent);
            if (part.Parent is not null && (parent < 0 || parent >= i))
                throw new InvalidOperationException($"Invalid parent for resident part {part.Name}.");
            parts[i] = new(Enum.Parse<Joint>(part.Joint), pivot, boxes, surfaces, part.LoadedOnly, parent);
        }

        var shadowMesh = new SurfaceMesh();
        shadowMesh.Quad(new(-.23f, 0, -.16f), new(-.23f, 0, .16f), new(.23f, 0, .16f),
            new(.23f, 0, -.16f), Vector3.Up, Color.Black * .22f);
        shadow = new GpuSurface(device, shadowMesh);
    }

    public void Update(World world, float elapsed, float time)
    {
        if (!ReferenceEquals(world, currentWorld))
        {
            poses.Clear();
            currentWorld = world;
        }
        foreach (var npc in world.Npcs.All)
        {
            if (!poses.TryGetValue(npc.Id, out var pose)) poses.Add(npc.Id, pose = new(npc, parts.Length));
            var position = new Vector2(npc.X, npc.Z);
            var travelled = Vector2.Distance(position, pose.Position);
            pose.Position = position;
            pose.Stride += travelled;
            var walk = npc.HasRemainingPath || travelled > .001f ? 1f : 0f;
            pose.WalkBlend = MathHelper.Lerp(pose.WalkBlend, walk, Math.Min(1, elapsed * 12));
            pose.Facing += MathHelper.WrapAngle(npc.Facing - pose.Facing) * Math.Min(1, elapsed * 14);
            var swing = MathF.Sin(pose.Stride * MathHelper.TwoPi / .60f) * pose.WalkBlend;
            var idle = MathF.Sin(time * MathHelper.TwoPi / 2.4f + npc.Id) * (1 - pose.WalkBlend);
            var carrying = CarriesVisibly(npc);
            var working = npc.Activity == NpcActivity.Working;
            var stroke = working ? .5f + .5f * MathF.Sin(time * MathHelper.TwoPi * 1.7f) : 0;
            bool digging = working && npc.Task?.Kind == NpcTaskKind.Dig;
            bool resting = npc.Activity == NpcActivity.Resting;
            var root = Matrix.CreateRotationY(pose.Facing) * Matrix.CreateTranslation(npc.X, npc.Y, npc.Z);
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var angle = part.Joint switch
                {
                    Joint.Head when resting => .20f + idle * .02f,
                    Joint.Head when digging => .16f + stroke * .18f,
                    Joint.Head => idle * .035f,
                    Joint.LeftArm or Joint.RightArm when carrying => -.78f,
                    Joint.LeftArm or Joint.RightArm when digging => -.30f - stroke * 1.05f,
                    Joint.RightArm when working => -.45f - stroke * .85f,
                    Joint.LeftArm when working => -.24f - stroke * .30f,
                    Joint.LeftArm or Joint.RightArm when npc.HasCart => -.72f + idle * .02f,
                    Joint.LeftArm => -swing * .43f + idle * .025f,
                    Joint.RightArm => swing * .43f - idle * .025f,
                    Joint.Forearm when carrying => -.68f,
                    Joint.Forearm when working => -.20f - stroke * .46f,
                    Joint.Forearm when npc.HasCart => -.25f,
                    Joint.Forearm => -.13f - Math.Abs(swing) * .14f,
                    Joint.LeftLeg => swing * .5f,
                    Joint.RightLeg => -swing * .5f,
                    Joint.LeftShin => Math.Max(0, swing) * .64f,
                    Joint.RightShin => Math.Max(0, -swing) * .64f,
                    _ => 0
                };
                if(part.Parent >= 0)
                    pose.Parts[i] = Matrix.CreateRotationX(angle) * Matrix.CreateTranslation(part.Pivot - parts[part.Parent].Pivot)
                        * pose.Parts[part.Parent];
                else pose.Parts[i] = Matrix.CreateRotationX(angle) * Matrix.CreateTranslation(part.Pivot);
            }

            // Keep the lower boot above ground throughout the hip and knee swing.
            float bottom = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Joint is not (Joint.LeftShin or Joint.RightShin)) continue;
                var transform = pose.Parts[i];
                foreach (var box in parts[i].Boxes)
                {
                    var edge = transform.M32 < 0 ? box.Max.Z : box.Min.Z;
                    bottom = Math.Min(bottom, box.Min.Y * transform.M22 + edge * transform.M32 + transform.M42);
                }
            }
            root.Translation -= new Vector3(0, bottom, 0);
            for (int i = 0; i < parts.Length; i++) pose.Parts[i] *= root;
        }
    }

    public Vector3 HeadCenter(NpcState npc)
    {
        var box = parts[head].Boxes[0];
        return Vector3.Transform((box.Min + box.Max) * .5f, poses[npc.Id].Parts[head]);
    }

    public object Motion(NpcState npc)
    {
        var pose = poses[npc.Id];
        return new { travelled = pose.Stride, walkBlend = pose.WalkBlend };
    }

    public float? Intersect(Ray ray, NpcState npc)
    {
        if (!poses.TryGetValue(npc.Id, out var pose)) return null;
        int outfit = Outfit(npc);
        float? nearest = npc.HasCart ? BakedGeometry.Intersect("handcart", ray, CartMatrix(npc, pose)) : null;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Surfaces[outfit] is null || part.LoadedOnly && !CarriesVisibly(npc)) continue;
            var inverse = Matrix.Invert(pose.Parts[i]);
            var local = new Ray(Vector3.Transform(ray.Position, inverse), Vector3.TransformNormal(ray.Direction, inverse));
            foreach (var box in part.Boxes)
                if (local.Intersects(box) is float distance && (nearest is null || distance < nearest))
                    nearest = distance;
        }
        return nearest;
    }

    internal Matrix[] CartInstances(World world)
    {
        if (!world.Npcs.All.Any(n => n.HasCart)) return [];
        return world.Npcs.All.Where(n => n.HasCart && poses.ContainsKey(n.Id)).Select(n => CartMatrix(n, poses[n.Id])).ToArray();
    }

    private static Matrix CartMatrix(NpcState npc, Pose pose) =>
        Matrix.CreateScale(.82f) * Matrix.CreateRotationY(pose.Facing + MathHelper.Pi) *
        Matrix.CreateTranslation(npc.X + MathF.Sin(pose.Facing) * .63f, npc.Y, npc.Z + MathF.Cos(pose.Facing) * .63f);

    public void Draw(GraphicsDevice device, BasicEffect effect, World world)
    {
        device.DepthStencilState = DepthStencilState.DepthRead;
        device.BlendState = BlendState.AlphaBlend;
        effect.LightingEnabled = false;
        foreach (var npc in world.Npcs.All)
        {
            var height = world.Height(npc.CellX, npc.CellZ) * World.Step;
            effect.World = Matrix.CreateTranslation(npc.X, height + .012f, npc.Z);
            effect.CurrentTechnique.Passes[0].Apply();
            shadow.Draw(device);
        }
        effect.LightingEnabled = true;
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        foreach (var npc in world.Npcs.All)
        {
            var pose = poses[npc.Id];
            int outfit = Outfit(npc);
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Surfaces[outfit] is not { } surface || part.LoadedOnly && !CarriesVisibly(npc)) continue;
                effect.World = pose.Parts[i];
                effect.CurrentTechnique.Passes[0].Apply();
                surface.Draw(device);
            }
        }
        effect.World = Matrix.Identity;
    }

    private int Outfit(NpcState npc) => (npc.Id - 1) % outfitCount;
    private static bool CarriesVisibly(NpcState npc) => !npc.HasCart && npc.Cargo.Count > 0 && npc.Activity != NpcActivity.Working;

    public void Dispose()
    {
        shadow.Dispose();
        foreach (var part in parts)
            foreach (var surface in part.Surfaces) surface?.Dispose();
    }
}
