using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;

namespace Town.Game.Interaction;

public sealed class MapCamera
{
    public float Yaw=.68f,Zoom=20;
    public Vector3 Target=new(0,.8f,0);
    public Matrix View {get;private set;}
    public Matrix Projection {get;private set;}
    public void Update(Viewport viewport)
    {
        var eye=Target+new Vector3(MathF.Sin(Yaw)*45,34,MathF.Cos(Yaw)*45);
        View=Matrix.CreateLookAt(eye,Target,Vector3.Up);
        Projection=Matrix.CreateOrthographic(Zoom*2*viewport.AspectRatio,Zoom*2,.1f,160);
    }
    public void Pan(Vector2 delta,int screenHeight)
    {
        float u=Zoom*2/screenHeight,s=MathF.Sin(Yaw),c=MathF.Cos(Yaw),pitch=34/MathF.Sqrt(34*34+45*45);
        Target+=new Vector3(-delta.X*u*c-delta.Y*u*s/pitch,0,delta.X*u*s-delta.Y*u*c/pitch);
        Target=new(MathHelper.Clamp(Target.X,-25,25),.8f,MathHelper.Clamp(Target.Z,-25,25));
    }
    public Cell? Pick(World world,Viewport viewport,Vector2 point,bool objects)
    {
        var start=viewport.Unproject(new(point,0),Projection,View,Matrix.Identity);
        var end=viewport.Unproject(new(point,1),Projection,View,Matrix.Identity);
        var ray=new Ray(start,Vector3.Normalize(end-start));float distance=float.MaxValue;Cell? selected=null;
        if(ray.Intersects(new Plane(Vector3.Up,-World.WaterLevel))is float water){var pointOnWater=ray.Position+ray.Direction*water;int wx=(int)MathF.Floor(pointOnWater.X),wz=(int)MathF.Floor(pointOnWater.Z);if(World.Inside(wx,wz)){distance=water;selected=new(wx,wz);}}
        for(int z=-World.Half;z<World.Half;z++)for(int x=-World.Half;x<World.Half;x++)
        {
            int h=world.Height(x,z);if(h==0)continue;
            var hit=ray.Intersects(new BoundingBox(new(x,0,z),new(x+1,h*World.Step,z+1)));
            if(hit.HasValue&&hit.Value<distance){distance=hit.Value;selected=new(x,z);}
        }
        if(objects)foreach(var item in world.Objects)
        {
            if(item.Kind=="road")continue;var (w,d)=World.Footprint(item.Kind,item.Rotation);float y=world.Height(item.X,item.Z)*World.Step;
            var hit=ray.Intersects(new BoundingBox(new(item.X,y,item.Z),new(item.X+w,y+2.8f,item.Z+d)));
            if(hit.HasValue&&hit.Value<distance){distance=hit.Value;selected=new(item.X,item.Z);}
        }
        return selected;
    }
    public Vector2 Project(Viewport viewport,Vector3 world){var p=viewport.Project(world,Projection,View,Matrix.Identity);return new(p.X,p.Y);}
}
