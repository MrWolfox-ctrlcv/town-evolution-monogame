using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.InteropServices;
using Town.Core;
using Town.Game.Interaction;

namespace Town.Game.Rendering;

[StructLayout(LayoutKind.Sequential)]
public readonly struct InstanceVertex(Matrix matrix) : IVertexType
{
    public readonly Matrix Transform = matrix;
    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0,VertexElementFormat.Vector4,VertexElementUsage.TextureCoordinate,4),
        new VertexElement(16,VertexElementFormat.Vector4,VertexElementUsage.TextureCoordinate,5),
        new VertexElement(32,VertexElementFormat.Vector4,VertexElementUsage.TextureCoordinate,6),
        new VertexElement(48,VertexElementFormat.Vector4,VertexElementUsage.TextureCoordinate,7));
    VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}
internal readonly record struct LeafMaterial(Vector3 Color,Vector2 CrownProfile);

internal sealed class InstanceBatch : IDisposable
{
    private readonly GraphicsDevice device;
    private DynamicVertexBuffer? buffer;
    public Model Model { get; }
    public int Count { get; private set; }
    public int Uploads { get; private set; }
    public InstanceBatch(GraphicsDevice device, Model model) { this.device=device;Model=model; }
    public void Update(IReadOnlyList<Matrix> matrices)
    {
        Count=matrices.Count;
        if(Count==0)return;
        if(buffer is null||buffer.VertexCount<Count){buffer?.Dispose();buffer=new(device,InstanceVertex.Declaration,Math.Max(4,Count*2),BufferUsage.WriteOnly);}
        var data=matrices.Select(m=>new InstanceVertex(m)).ToArray();
        buffer.SetData(data,0,data.Length,SetDataOptions.Discard);Uploads++;
    }
    public int Draw(Effect effect,Vector3 tint,LeafMaterial? leaves=null)
    {
        if(Count==0||buffer is null)return 0;
        int calls=0;effect.Parameters["Tint"].SetValue(tint);
        foreach(var mesh in Model.Meshes)foreach(var part in mesh.MeshParts)
        {
            var texture=(part.Effect as BasicEffect)?.Texture;
            if(texture is null)throw new InvalidOperationException("Content model is missing the baked palette/texture.");
            // The leaf card supplies its alpha silhouette. Mipmapped transparent texels must not darken its paint.
            bool leafSurface=leaves.HasValue&&texture.Name?.Contains("Tree_Leaves",StringComparison.Ordinal)==true;
            var leaf=leaves.GetValueOrDefault();
            effect.Parameters["LeafColor"].SetValue(leafSurface?new Vector4(leaf.Color,1):Vector4.Zero);
            if(leafSurface)effect.Parameters["CrownProfile"].SetValue(leaf.CrownProfile);
            effect.Parameters["Palette"].SetValue(texture);
            device.SetVertexBuffers(new VertexBufferBinding(part.VertexBuffer,part.VertexOffset,0),new VertexBufferBinding(buffer,0,1));
            device.Indices=part.IndexBuffer;
            foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();device.DrawInstancedPrimitives(PrimitiveType.TriangleList,0,part.StartIndex,part.PrimitiveCount,Count);calls++;}
        }
        return calls;
    }
    public void Dispose()=>buffer?.Dispose();
}
public sealed class InstancedModels(GraphicsDevice device,ContentManager content) : IDisposable
{
    public const string BroadleafAsset="approved-tree-7-lod";
    private readonly Dictionary<string,InstanceBatch> batches=[];
    private readonly Effect effect=content.Load<Effect>("Effects/Instanced");
    private readonly Dictionary<string,Matrix[]> previous=[];
    private InstanceBatch? preview;
    private string? previewAsset;
    private Matrix previewMatrix;
    private InstanceBatch? movingCarts;
    private Matrix[] previousCarts=[];
    public int DrawCalls {get;private set;}
    public int GeometryLoads {get;private set;}
    public int InstanceUploads=>batches.Values.Sum(b=>b.Uploads);
    public int InstanceCount=>batches.Values.Sum(b=>b.Count);
    public IReadOnlyDictionary<string,int> Counts=>batches.ToDictionary(p=>p.Key,p=>p.Value.Count);
    public void SetPreview(PlacedObject? item,World world)
    {
        if(item is null){if(preview is not null)preview.Update([]);return;}
        var spec=World.Catalog[item.Kind];if(spec.Asset is null)return;
        if(previewAsset!=spec.Asset){preview?.Dispose();preview=new(device,content.Load<Model>("Models/"+spec.Asset));previewAsset=spec.Asset;}
        var(w,d)=World.Footprint(item.Kind,item.Rotation);var transform=Matrix.CreateScale(spec.Scale)*Matrix.CreateRotationY(item.Rotation*MathHelper.PiOver2)*Matrix.CreateTranslation(item.X+w/2f,world.Height(item.X,item.Z)*World.Step+.05f,item.Z+d/2f);
        if(preview is not null&&(preview.Count==0||previewMatrix!=transform)){preview.Update([transform]);previewMatrix=transform;}
    }
    public void Sync(World world)
    {
        var groups=new Dictionary<string,List<Matrix>>();
        void Add(string asset,Vector3 pos,float rotation=0,float scale=1){if(!groups.TryGetValue(asset,out var list))groups[asset]=list=[];list.Add(Matrix.CreateScale(scale)*Matrix.CreateRotationY(rotation)*Matrix.CreateTranslation(pos));}
        foreach(var item in world.Objects)
        {
            var spec=World.Catalog[item.Kind];if(spec.Asset is null)continue;
            var (w,d)=World.Footprint(item.Kind,item.Rotation);float y=world.Height(item.X,item.Z)*World.Step;
            var farm=world.Farm(item.Id);string asset=spec.Asset;
            if(farm is not null)asset=WorldPicking.FarmAsset(farm);
            if(item.Kind=="nursery")asset=WorldPicking.NurseryAsset(world,item.Id);
            Add(asset,new(item.X+w/2f,y,item.Z+d/2f),item.Rotation*MathHelper.PiOver2,spec.Scale);
            if(farm is not null)for(int i=0;i<9;i++)if(farm.Cells[i]is { } crop){var p=WorldPicking.FarmCenter(item,i);Add(WorldPicking.CropAsset(world,crop),new(p.X,y,p.Z),item.Rotation*MathHelper.PiOver2);}
        }
        foreach(var tree in world.LiveTrees)
        {
            Add(tree.Variant>.5?"approved-pine-8":BroadleafAsset,new(tree.X+.5f,world.Height(tree.X,tree.Z)*World.Step,tree.Z+.5f),(float)tree.Variant*MathHelper.TwoPi,world.TreeScale(tree.X,tree.Z));
        }
        foreach(var p in world.Piles)Add($"pile-{p.Item}-{p.Amount}",new(p.X+.5f,world.Height(p.X,p.Z)*World.Step,p.Z+.5f));
        foreach(var p in world.Drops)Add($"pile-{p.Item}-1",new(p.X+p.OffsetX,Math.Max(World.WaterLevel,world.Height(p.X,p.Z)*World.Step)+.04f,p.Z+p.OffsetZ),0,.3f);
        foreach(var key in batches.Keys.Union(groups.Keys).ToArray())
        {
            Matrix[] data=groups.TryGetValue(key,out var group)?group.ToArray():[];
            if(previous.TryGetValue(key,out var old)&&old.SequenceEqual(data))continue;
            if(!batches.TryGetValue(key,out var batch)){batches[key]=batch=new(device,content.Load<Model>($"Models/{key}"));GeometryLoads++;}
            batch.Update(data);previous[key]=data;
        }
    }
    public void SetResidentCarts(Matrix[] transforms)
    {
        if (previousCarts.AsSpan().SequenceEqual(transforms)) return;
        movingCarts ??= new(device,content.Load<Model>("Models/handcart"));
        movingCarts.Update(transforms);
        previousCarts=transforms;
    }
    public void Draw(Matrix viewProjection)
    {
        effect.Parameters["ViewProjection"].SetValue(viewProjection);
        effect.Parameters["AmbientColor"].SetValue(new Vector3(.66f,.69f,.62f));
        effect.Parameters["LightDirection"].SetValue(Vector3.Normalize(new(-.45f,-.82f,.35f)));DrawCalls=0;
        effect.Parameters["Opacity"].SetValue(1f);
        foreach(var (asset,batch) in batches)
        {
            LeafMaterial? leaves=asset switch
            {
                BroadleafAsset => new(new(.46f,.61f,.25f),new(.78f,1.35f)),
                "approved-pine-8" => new(new(.36f,.49f,.30f),new(.58f,1.24f)),
                _ => null
            };
            DrawCalls+=batch.Draw(effect,Vector3.One,leaves);
        }
        if (movingCarts is { Count: > 0 }) DrawCalls+=movingCarts.Draw(effect,Vector3.One);
        if(preview is {Count:>0}){device.BlendState=BlendState.NonPremultiplied;device.DepthStencilState=DepthStencilState.DepthRead;effect.Parameters["Opacity"].SetValue(.55f);preview.Draw(effect,new(.85f,1,.65f));device.BlendState=BlendState.Opaque;device.DepthStencilState=DepthStencilState.Default;}
    }
    public void Dispose(){preview?.Dispose();movingCarts?.Dispose();foreach(var b in batches.Values)b.Dispose();batches.Clear();}
}
