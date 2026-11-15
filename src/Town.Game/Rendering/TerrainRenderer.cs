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
    private readonly Dictionary<Cell, GpuSurface> chunks = [];
    public int Rebuilds { get; private set; }
    public int Count => chunks.Count;
    public int Triangles => chunks.Values.Sum(m => m.Triangles);
    public void Rebuild(World world, IEnumerable<Cell>? affected = null)
    {
        var cells = affected ?? Enumerable.Range(0, 25).Select(i => new Cell(i % 5, i / 5));
        foreach (var cell in cells)
        {
            if (chunks.Remove(cell, out var old)) old.Dispose();
            chunks[cell] = new(device, Build(world, cell)); Rebuilds++;
        }
    }
    private static SurfaceMesh Build(World world, Cell chunk)
    {
        var mesh = new SurfaceMesh();
        int startX = chunk.X * WorldQueries.ChunkSize - World.Half, startZ = chunk.Z * WorldQueries.ChunkSize - World.Half;
        for (int z = startZ; z < startZ + WorldQueries.ChunkSize; z++)
            for (int x = startX; x < startX + WorldQueries.ChunkSize; x++)
            {
                int h = world.Height(x, z); if (h == 0) continue;
                float y = h * World.Step; double r = World.Hash(x, z);
                var top = h == 1 ? new Color(216,199,157) : h >= 6 ? new Color(152,161,139) : r > .8 ? new Color(147,174,104) : r > .4 ? new Color(159,185,119) : new Color(168,193,127);
                mesh.Quad(new(x,y,z),new(x,y,z+1),new(x+1,y,z+1),new(x+1,y,z),Vector3.Up,top);
                foreach (var (dx,dz) in new[] {(-1,0),(1,0),(0,-1),(0,1)})
                    for (int layer = world.Height(x+dx,z+dz); layer < h; layer++)
                    {
                        float low = layer * World.Step, high = (layer+1)*World.Step;
                        var color = layer == h-1 && h > 1 ? new Color(126,152,90) : layer < 2 ? new Color(168,142,108) : new Color(182,157,121);
                        if(dx == -1)mesh.Quad(new(x,low,z+1),new(x,high,z+1),new(x,high,z),new(x,low,z),Vector3.Left,color);
                        if(dx == 1)mesh.Quad(new(x+1,low,z),new(x+1,high,z),new(x+1,high,z+1),new(x+1,low,z+1),Vector3.Right,color);
                        if(dz == -1)mesh.Quad(new(x,low,z),new(x,high,z),new(x+1,high,z),new(x+1,low,z),Vector3.Forward,color);
                        if(dz == 1)mesh.Quad(new(x+1,low,z+1),new(x+1,high,z+1),new(x,high,z+1),new(x,low,z+1),Vector3.Backward,color);
                    }
            }
        return mesh;
    }
    public void Draw(BasicEffect effect)
    {
        foreach(var pass in effect.CurrentTechnique.Passes) { pass.Apply(); foreach(var chunk in chunks.Values)chunk.Draw(device); }
    }
    public void Dispose() { foreach(var mesh in chunks.Values)mesh.Dispose(); chunks.Clear(); }
}
