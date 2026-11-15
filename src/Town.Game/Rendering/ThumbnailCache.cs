using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
namespace Town.Game.Rendering;

/// <summary>Handbook portraits use the live cached model geometry, with no substitute artwork.</summary>
public sealed class ThumbnailCache : IDisposable
{
    private readonly Dictionary<string,RenderTarget2D> images=[];
    public Texture2D? Get(string kind)=>images.GetValueOrDefault(kind);
    public ThumbnailCache(GraphicsDevice device,ContentManager content)
    {
        var mapping=World.Catalog.Where(p=>p.Value.Asset is not null).ToDictionary(p=>p.Key,p=>p.Value.Asset!);
        foreach(string item in World.ItemNames.Keys.Where(i=>i!="fertilizer"))mapping[item]=$"pile-{item}-1";
        mapping["tree"]="approved-tree-7";mapping["stoneOven"]="stone-oven";mapping["expandedWarehouse"]="warehouse-expanded";
        mapping["flatLand"]="farm-cell-bed";mapping["waterFarm"]="farm-cell-bed-water";mapping["farmWithBox"]="farm-with-box";
        foreach(var(kind,asset)in mapping)
        {
            var model=content.Load<Model>("Models/"+asset);var bounds=model.Meshes.Select(m=>m.BoundingSphere).Aggregate(BoundingSphere.CreateMerged);
            float radius=Math.Max(.3f,bounds.Radius);var view=Matrix.CreateLookAt(bounds.Center+new Vector3(3,2.4f,4)*radius,bounds.Center,Vector3.Up);
            var projection=Matrix.CreateOrthographic(radius*2.5f,radius*2.5f,.01f,radius*20+10);
            var target=new RenderTarget2D(device,128,128,false,SurfaceFormat.Color,DepthFormat.Depth24);
            device.SetRenderTarget(target);device.Clear(Color.Transparent);device.BlendState=BlendState.Opaque;device.DepthStencilState=DepthStencilState.Default;device.RasterizerState=RasterizerState.CullNone;
            var bones=new Matrix[model.Bones.Count];model.CopyAbsoluteBoneTransformsTo(bones);
            foreach(var mesh in model.Meshes){foreach(var effect in mesh.Effects.OfType<BasicEffect>()){effect.World=bones[mesh.ParentBone.Index];effect.View=view;effect.Projection=projection;effect.EnableDefaultLighting();effect.AmbientLightColor=new(.65f,.68f,.61f);}mesh.Draw();}
            device.SetRenderTarget(null);images[kind]=target;
        }
        device.SetVertexBuffers(Array.Empty<VertexBufferBinding>());device.Indices=null;device.Textures[0]=null;
    }
    public void Dispose(){foreach(var image in images.Values)image.Dispose();images.Clear();}
}