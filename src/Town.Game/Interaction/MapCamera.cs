using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;

namespace Town.Game.Interaction;

public sealed class MapCamera
{
    /// <summary>Yaw 0 is north-up. A positive yaw swings the eye east, so the map turns 30 degrees anticlockwise.</summary>
    public float Yaw=MathHelper.Pi/6,Zoom=20;
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
        float limit=World.Half;Target=new(MathHelper.Clamp(Target.X,-limit,limit),.8f,MathHelper.Clamp(Target.Z,-limit,limit));
    }
    public Cell? Pick(World world,Viewport viewport,Vector2 point,bool objects)
    {
        var start=viewport.Unproject(new(point,0),Projection,View,Matrix.Identity);
        var end=viewport.Unproject(new(point,1),Projection,View,Matrix.Identity);
        var ray=new Ray(start,Vector3.Normalize(end-start));float distance=float.MaxValue;Cell? selected=null;
        if(ray.Intersects(new Plane(Vector3.Up,-World.WaterLevel))is float water){var pointOnWater=ray.Position+ray.Direction*water;int wx=(int)MathF.Floor(pointOnWater.X),wz=(int)MathF.Floor(pointOnWater.Z);if(World.Inside(wx,wz)){distance=water;selected=new(wx,wz);}}
        // Column walk: a 96x96 map has 9216 cells, so test only the cells the ray actually crosses.
        foreach(var (x,z) in Walk(world,ray,distance))
        {
            int h=world.Height(x,z);
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
    /// <summary>2D DDA: visit only the columns the picking ray crosses instead of all 9216 cells.</summary>
    private static IEnumerable<Cell> Walk(World world,Ray ray,float limit)
    {
        if(MathF.Abs(ray.Direction.Y)<1e-6f)yield break;
        float entry=Math.Max(0,-ray.Position.Y/ray.Direction.Y);
        var start=ray.Position+ray.Direction*entry;
        float dx=ray.Direction.X,dz=ray.Direction.Z;
        int x=(int)MathF.Floor(start.X),z=(int)MathF.Floor(start.Z);
        int stepX=dx>=0?1:-1,stepZ=dz>=0?1:-1;
        float tMaxX=dx==0?float.MaxValue:(stepX>0?x+1-start.X:start.X-x)/MathF.Abs(dx);
        float tMaxZ=dz==0?float.MaxValue:(stepZ>0?z+1-start.Z:start.Z-z)/MathF.Abs(dz);
        float tDeltaX=dx==0?float.MaxValue:MathF.Abs(1/dx),tDeltaZ=dz==0?float.MaxValue:MathF.Abs(1/dz);
        float t=entry;
        for(int guard=0;guard<World.Size*3;guard++)
        {
            if(World.Inside(x,z))yield return new(x,z);
            if(t>limit&&World.Inside(x,z))yield break;
            if(t>limit&&(x<-World.Half||x>=World.Half||z<-World.Half||z>=World.Half))yield break;
            if(tMaxX<tMaxZ){x+=stepX;t=entry+tMaxX;tMaxX+=tDeltaX;}else{z+=stepZ;t=entry+tMaxZ;tMaxZ+=tDeltaZ;}
        }
    }
    public Vector2 Project(Viewport viewport,Vector3 world){var p=viewport.Project(world,Projection,View,Matrix.Identity);return new(p.X,p.Y);}
}