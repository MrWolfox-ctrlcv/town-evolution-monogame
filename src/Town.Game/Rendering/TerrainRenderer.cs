using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using System.Runtime.InteropServices;

namespace Town.Game.Rendering;

[StructLayout(LayoutKind.Sequential)]
public readonly struct WorldVertex(Vector3 position, Vector3 normal, Color color) : IVertexType
{
    public readonly Vector3 Position = position;
    public readonly Vector3 Normal = normal;
    public readonly Color Color = color;
    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0));
    VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}
public sealed class SurfaceMesh
{
    public readonly List<WorldVertex> Vertices = [];
    public readonly List<int> Indices = [];
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color)
    {
        int offset = Vertices.Count;
        foreach (var p in new[] { a, b, c, d }) Vertices.Add(new(p, normal, color));
        Indices.AddRange([offset, offset + 1, offset + 2, offset, offset + 2, offset + 3]);
    }
    public void Box(Vector3 origin, Vector3 size, Color color)
    {
        float x = origin.X, y = origin.Y, z = origin.Z, a = x + size.X, b = y + size.Y, c = z + size.Z;
        Quad(new(x,b,z),new(x,b,c),new(a,b,c),new(a,b,z),Vector3.Up,color);
        Quad(new(x,y,c),new(x,b,c),new(x,b,z),new(x,y,z),Vector3.Left,color);
        Quad(new(a,y,z),new(a,b,z),new(a,b,c),new(a,y,c),Vector3.Right,color);
        Quad(new(x,y,z),new(x,b,z),new(a,b,z),new(a,y,z),Vector3.Forward,color);
        Quad(new(a,y,c),new(a,b,c),new(x,b,c),new(x,y,c),Vector3.Backward,color);
    }
}
public sealed class GpuSurface : IDisposable
{
    private readonly VertexBuffer? vertices;
    private readonly IndexBuffer? indices;
    public int Triangles { get; }
    public GpuSurface(GraphicsDevice device, SurfaceMesh mesh)
    {
        Triangles = mesh.Indices.Count / 3;
        if (Triangles == 0) return;
        vertices = new(device, WorldVertex.Declaration, mesh.Vertices.Count, BufferUsage.WriteOnly);
        vertices.SetData(mesh.Vertices.ToArray());
        indices = new(device, IndexElementSize.ThirtyTwoBits, mesh.Indices.Count, BufferUsage.WriteOnly);
        indices.SetData(mesh.Indices.ToArray());
    }
    public void Draw(GraphicsDevice device)
    {
        if (vertices is null || indices is null) return;
        device.SetVertexBuffer(vertices); device.Indices = indices;
        device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, Triangles);
    }
    public void Dispose() { vertices?.Dispose(); indices?.Dispose(); }
}
public sealed class TerrainRenderer(GraphicsDevice device) : IDisposable
{
    private sealed record Chunk(GpuSurface Surface, BoundingBox Bounds);
    private readonly Dictionary<Cell, Chunk> chunks = [];
    public int Rebuilds { get; private set; }
    public int Count => chunks.Count;
    public int Triangles => chunks.Values.Sum(m => m.Surface.Triangles);
    public int VisibleChunks { get; private set; }
    public void Rebuild(World world, IEnumerable<Cell>? affected = null)
    {
        var cells = affected ?? WorldQueries.AllChunks();
        foreach (var cell in cells)
        {
            if (chunks.Remove(cell, out var old)) old.Surface.Dispose();
            var mesh=Build(world,cell);
            var start=new Vector3(cell.X*WorldQueries.ChunkSize-World.Half,0,cell.Z*WorldQueries.ChunkSize-World.Half);
            float top=mesh.Vertices.Count==0?0:mesh.Vertices.Max(v=>v.Position.Y);
            chunks[cell]=new(new(device,mesh),new(start,start+new Vector3(WorldQueries.ChunkSize,top,WorldQueries.ChunkSize)));
            Rebuilds++;
        }
    }
    private static Color TopColor(World world, int x, int z, int h)
    {
        float patch = .5f + .22f * MathF.Sin(x * .24f + z * .16f)
            + .18f * MathF.Cos(z * .31f - x * .09f) + .07f * ((float)World.Hash(x, z) - .5f);
        float exposure = Math.Clamp((h - 12) / 42f, 0, 1);
        return world.Surface(x, z) switch
        {
            GroundSurface.Sand => Color.Lerp(new(205, 182, 135), new(229, 206, 159), patch),
            GroundSurface.Stone => Color.Lerp(Color.Lerp(new(141, 143, 119), new(159, 158, 141), patch),
                new(216, 214, 198), exposure),
            GroundSurface.Soil => Color.Lerp(new(151, 120, 81), new(177, 147, 103), patch),
            _ => Color.Lerp(new(119, 149, 84), new(161, 179, 104), patch * (1 - Math.Min(.4f, (h - 1) * .03f)))
        };
    }
    /// <summary>Cliff faces keep material identity: grass overhang, exposed soil, and grey mountain rock.</summary>
    private static Color SideColor(World world, int x, int z, int h, int layer)
    {
        var surface = world.Surface(x, z);
        if (surface == GroundSurface.Stone)
            return Color.Lerp(new(119, 113, 97), new(183, 181, 166), Math.Clamp((layer - 9) / 42f, 0, 1));
        if (surface == GroundSurface.Sand) return new Color(189, 161, 112);
        if (layer == h - 1 && surface == GroundSurface.Grass) return Color.Lerp(TopColor(world,x,z,h),new(109,124,74),.35f);
        return layer >= h - 2 ? new Color(150, 116, 77) : new Color(130, 101, 71);
    }
    private static SurfaceMesh Build(World world, Cell chunk)
    {
        var mesh = new SurfaceMesh();
        int startX = chunk.X * WorldQueries.ChunkSize - World.Half, startZ = chunk.Z * WorldQueries.ChunkSize - World.Half;
        for (int z = startZ; z < startZ + WorldQueries.ChunkSize; z++)
            for (int x = startX; x < startX + WorldQueries.ChunkSize; x++)
            {
                int h = world.Height(x, z); if (h == 0) continue;
                float y = h * World.Step;
                mesh.Quad(new(x,y,z),new(x,y,z+1),new(x+1,y,z+1),new(x+1,y,z),Vector3.Up,TopColor(world,x,z,h));
                foreach (var (dx,dz) in new[] {(-1,0),(1,0),(0,-1),(0,1)})
                    for (int layer = world.Height(x+dx,z+dz); layer < h; layer++)
                    {
                        float low = layer * World.Step, high = (layer+1)*World.Step;
                        var color = SideColor(world,x,z,h,layer);
                        if(dx == -1)mesh.Quad(new(x,low,z+1),new(x,high,z+1),new(x,high,z),new(x,low,z),Vector3.Left,color);
                        if(dx == 1)mesh.Quad(new(x+1,low,z),new(x+1,high,z),new(x+1,high,z+1),new(x+1,low,z+1),Vector3.Right,color);
                        if(dz == -1)mesh.Quad(new(x,low,z),new(x,high,z),new(x+1,high,z),new(x+1,low,z),Vector3.Forward,color);
                        if(dz == 1)mesh.Quad(new(x+1,low,z+1),new(x+1,high,z+1),new(x,high,z+1),new(x,low,z+1),Vector3.Backward,color);
                    }
            }
        return mesh;
    }
    public void Draw(BasicEffect effect,BoundingFrustum frustum)
    {
        VisibleChunks=0;
        foreach(var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            foreach(var chunk in chunks.Values)
            {
                if(chunk.Surface.Triangles==0||!frustum.Intersects(chunk.Bounds))continue;
                chunk.Surface.Draw(device);
                VisibleChunks++;
            }
        }
    }
    public void Dispose() { foreach(var chunk in chunks.Values)chunk.Surface.Dispose(); chunks.Clear(); }
}
