using Microsoft.Xna.Framework;
using System.Text;
namespace Town.Game.Interaction;

/// <summary>Source glTF triangles retained offline; Android never reads GPU buffers for picking.</summary>
internal sealed class BakedGeometry(Vector3[] positions,int[] elements)
{
    private readonly Vector3[] vertices=positions;
    private readonly int[] indices=elements;
    private readonly BoundingBox bounds=BoundingBox.CreateFromPoints(positions);
    private static readonly Dictionary<string,BakedGeometry> models=Load();
    private static Dictionary<string,BakedGeometry> Load()
    {
        using var stream=typeof(BakedGeometry).Assembly.GetManifestResourceStream("Town.Game.Picking.bin")??throw new InvalidOperationException("Picking geometry is missing.");
        using var reader=new BinaryReader(stream);int count=reader.ReadInt32();var result=new Dictionary<string,BakedGeometry>();
        for(int n=0;n<count;n++)
        {
            string id=Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));int vc=reader.ReadInt32(),ic=reader.ReadInt32();
            var v=new Vector3[vc];var i=new int[ic];for(int k=0;k<vc;k++)v[k]=new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());for(int k=0;k<ic;k++)i[k]=reader.ReadInt32();result[id]=new(v,i);
        }
        return result;
    }
    internal static BoundingBox? ModelBounds(string asset)
        => models.TryGetValue(asset,out var model)?model.bounds:null;
    public static float? Intersect(string asset,Ray worldRay,Matrix transform,float limit=float.MaxValue)
    {
        var model=models[asset];var inverse=Matrix.Invert(transform);
        // Leave direction unnormalised: triangle t stays in world-distance units.
        var ray=new Ray(Vector3.Transform(worldRay.Position,inverse),Vector3.TransformNormal(worldRay.Direction,inverse));
        if(ray.Intersects(model.bounds)is not float boundsHit||boundsHit>limit)return null;
        float nearest=limit;
        for(int n=0;n<model.indices.Length;n+=3)
        {
            Vector3 a=model.vertices[model.indices[n]],e1=model.vertices[model.indices[n+1]]-a,e2=model.vertices[model.indices[n+2]]-a;
            var p=Vector3.Cross(ray.Direction,e2);float determinant=Vector3.Dot(e1,p);if(MathF.Abs(determinant)<1e-8f)continue;
            float inv=1/determinant;var s=ray.Position-a;float u=Vector3.Dot(s,p)*inv;if(u<0||u>1)continue;
            var q=Vector3.Cross(s,e1);float v=Vector3.Dot(ray.Direction,q)*inv;if(v<0||u+v>1)continue;
            float t=Vector3.Dot(e2,q)*inv;if(t>=0&&t<nearest)nearest=t;
        }
        return nearest<limit?nearest:null;
    }
}
