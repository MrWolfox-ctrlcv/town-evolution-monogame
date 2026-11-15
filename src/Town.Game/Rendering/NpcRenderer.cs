using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Core.Residents;

namespace Town.Game.Rendering;

/// <summary>居民占位表现：一个黑色方块加地面阴影，选中时加一圈高亮。</summary>
public sealed class NpcRenderer : IDisposable
{
    private readonly VertexBuffer vertices;
    private readonly IndexBuffer indices;
    private readonly int triangleCount;

    public NpcRenderer(GraphicsDevice device)
    {
        var mesh = new SurfaceMesh();
        // 以"脚下中心"为原点：宽 .52、深 .52、高 .86 的方块。
        mesh.Box(new Vector3(-.26f, 0, -.26f), new Vector3(.52f, .86f, .52f), new Color(38, 40, 44));
        // 脚下阴影，避免方块像浮在地面上。
        mesh.Quad(new(-.34f, .012f, -.34f), new(-.34f, .012f, .34f), new(.34f, .012f, .34f), new(.34f, .012f, -.34f), Vector3.Up, new Color(30, 34, 30));
        triangleCount = mesh.Indices.Count / 3;
        vertices = new(device, WorldVertex.Declaration, mesh.Vertices.Count, BufferUsage.WriteOnly);
        vertices.SetData(mesh.Vertices.ToArray());
        indices = new(device, IndexElementSize.ThirtyTwoBits, mesh.Indices.Count, BufferUsage.WriteOnly);
        indices.SetData(mesh.Indices.ToArray());
    }

    /// <summary>方块本身与选中环一起画，调用方负责设置相机矩阵。</summary>
    public void Draw(GraphicsDevice device, BasicEffect effect, World world, int selectedId)
    {
        foreach (var npc in world.Npcs.All)
        {
            float y = npc.Y;
            effect.World = Matrix.CreateRotationY(npc.Facing) * Matrix.CreateTranslation(npc.X, y, npc.Z);
            device.SetVertexBuffer(vertices);
            device.Indices = indices;
            foreach (var pass in effect.CurrentTechnique.Passes) { pass.Apply(); device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triangleCount); }
        }
        effect.World = Matrix.Identity;
    }

    public void Dispose() { vertices.Dispose(); indices.Dispose(); }
}
