using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Game.Rendering;
namespace Town.Game.Interaction;
public sealed record WorldHit(string Type,int Id,int X,int Z,int FarmCell=-1);
public static class WorldPicking
{
    public static Vector3 FarmCenter(PlacedObject b,int cell)
    {
        float x=(cell%3+.5f)*2/3,z=(cell/3+.5f)*2/3;
        (x,z)=(b.Rotation%4) switch{1=>(z,2-x),2=>(2-x,2-z),3=>(2-z,x),_=>(x,z)};
        return new(b.X+x,0,b.Z+z);
    }
    public static string FarmAsset(FarmState f)=>f.Box?(f.Irrigated?"farm-with-box-water":"farm-with-box"):(f.Irrigated?"farm-cell-bed-water":"farm-cell-bed");
    public static string CropAsset(World w,CropCell c)=>$"crop-cell-{c.Crop}-"+(c.ReadyTick<=w.Clock?"ready":c.ReadyTick-w.Clock<=200?"young":"seedling");
    public static string NurseryAsset(World w,int id)=>w.Jobs.FirstOrDefault(j=>j.Id==id)is { } j?(j.ReadyTick<=w.Clock?"nursery-ready":"nursery-growing"):(w.Attachments.Any(a=>a.Id==id&&a.Stock.GetValueOrDefault("treeSeed")>0)?"nursery-seeded":"nursery-empty");
    public static WorldHit? Pick(World world,MapCamera camera,Viewport viewport,Vector2 point,NpcRenderer? residents=null)
    {
        var start=viewport.Unproject(new(point,0),camera.Projection,camera.View,Matrix.Identity);var end=viewport.Unproject(new(point,1),camera.Projection,camera.View,Matrix.Identity);
        var ray=new Ray(start,Vector3.Normalize(end-start));float nearest=float.MaxValue;WorldHit? found=null;
        if(residents is not null)foreach(var npc in world.Npcs.All)
        {
            var hit=residents.Intersect(ray,npc);
            if(hit is float distance&&distance<nearest){nearest=distance;found=new("resident",npc.Id,npc.CellX,npc.CellZ);}
        }
        Matrix Transform(Vector3 p,float rotation=0,float scale=1)=>Matrix.CreateScale(scale)*Matrix.CreateRotationY(rotation)*Matrix.CreateTranslation(p);
        void Mesh(string asset,Matrix transform,WorldHit hit){if(BakedGeometry.Intersect(asset,ray,transform,nearest)is float d){nearest=d;found=hit;}}
        foreach(var b in world.Objects.Where(o=>o.Kind!="road"))
        {
            var(w,d)=World.Footprint(b.Kind,b.Rotation);float y=world.Height(b.X,b.Z)*World.Step;var spec=World.Catalog[b.Kind];var farm=world.Farm(b.Id);float angle=b.Rotation*MathHelper.PiOver2;
            var transform=Transform(new(b.X+w*.5f,y,b.Z+d*.5f),angle,spec.Scale);
            if(farm is null)Mesh(b.Kind=="nursery"?NurseryAsset(world,b.Id):spec.Asset!,transform,new("building",b.Id,b.X,b.Z));
            else
            {
                if(BakedGeometry.Intersect(FarmAsset(farm),ray,transform)is float t&&t<nearest)
                {
                    var local=Vector3.Transform(ray.Position+ray.Direction*t,Matrix.Invert(transform));int cx=(int)MathF.Floor((local.X+1)*1.5f),cz=(int)MathF.Floor((local.Z+1)*1.5f);
                    nearest=t;found=new("farm",b.Id,b.X,b.Z,cx is >=0 and <3&&cz is >=0 and <3?cz*3+cx:-1);
                }
                for(int i=0;i<9;i++)if(farm.Cells[i]is { } c){var p=FarmCenter(b,i);Mesh(CropAsset(world,c),Transform(new(p.X,y,p.Z),angle),new("farm",b.Id,b.X,b.Z,i));}
            }
        }
        foreach(var t in world.LiveTrees)Mesh(t.Variant>.5?"approved-pine-8":InstancedModels.BroadleafAsset,Transform(new(t.X+.5f,world.Height(t.X,t.Z)*World.Step,t.Z+.5f),(float)t.Variant*MathHelper.TwoPi,world.TreeScale(t.X,t.Z)),new("tree",World.Index(t.X,t.Z),t.X,t.Z));
        foreach(var p in world.Piles)
        {
            float y=world.Height(p.X,p.Z)*World.Step;Mesh($"pile-{p.Item}-{p.Amount}",Transform(new(p.X+.5f,y,p.Z+.5f)),new("pile",p.Id,p.X,p.Z));
            // Material stacks have air gaps between authored pieces; retain a small touch target.
            var hit=ray.Intersects(new BoundingBox(new(p.X+.18f,y,p.Z+.18f),new(p.X+.82f,y+.35f,p.Z+.82f)));
            if(hit is float t&&t<nearest){nearest=t;found=new("pile",p.Id,p.X,p.Z);}
        }
        // Bounded touch aid for tiny dropped units; opaque scene geometry still blocks it.
        foreach(var p in world.Drops){float y=Math.Max(World.WaterLevel,world.Height(p.X,p.Z)*World.Step);var d=ray.Intersects(new BoundingBox(new(p.X+p.OffsetX-.11f,y,p.Z+p.OffsetZ-.11f),new(p.X+p.OffsetX+.11f,y+.3f,p.Z+p.OffsetZ+.11f)));if(d is float distance&&distance<nearest){nearest=distance;found=new("drop",p.Id,p.X,p.Z);}}
        var tile=camera.Pick(world,viewport,point,false);
        if(tile is Cell ground){var hit=world.Height(ground.X,ground.Z)==0?ray.Intersects(new Plane(Vector3.Up,-World.WaterLevel)):ray.Intersects(new BoundingBox(new(ground.X,0,ground.Z),new(ground.X+1,world.Height(ground.X,ground.Z)*World.Step,ground.Z+1)));if(hit is float d&&d<nearest-.001f)return new("ground",0,ground.X,ground.Z);}
        return found??(tile is Cell fallback?new("ground",0,fallback.X,fallback.Z):null);
    }
}
