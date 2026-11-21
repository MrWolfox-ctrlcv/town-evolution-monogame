using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Town.Game;

public sealed partial class TownGame
{
    private const int CameraCheckFrames=126;
    private const int CameraCheckWarmup=24;
    private readonly List<double> cameraCheckTimes=[];
    private long cameraCheckPrevious;
    private int cameraCheckUpdate=-1;

    private void AdvanceCameraCheck()
    {
        if(cameraCheckUpdate==frames)return;
        cameraCheckUpdate=frames;
        long now=Stopwatch.GetTimestamp();
        if(frames>CameraCheckWarmup&&cameraCheckPrevious!=0)
            cameraCheckTimes.Add(Stopwatch.GetElapsedTime(cameraCheckPrevious,now).TotalMilliseconds);
        cameraCheckPrevious=now;
        var viewport=GraphicsDevice.Viewport;
        var start=new Vector2(viewport.Width*.41f,viewport.Height*.68f);
        int step=frames-CameraCheckWarmup;
        float sway=step<=0?0:MathF.Sin(step*.09f);
        if(step>=0)
        {
            var point=start+new Vector2(170*sway,84*MathF.Sin(step*.14f));
            GamePointer(point, step==0, step==CameraCheckFrames-CameraCheckWarmup-1);
            ZoomCameraForCheck(18+12*sway);
        }
        camera.Update(viewport);
    }

    private void ZoomCameraForCheck(float zoom)
    {
        var viewport=GraphicsDevice.Viewport;
        ZoomAround(zoom,viewport.Bounds.Center.ToVector2(),viewport);
    }

    private void FrameCanopyCheck()
    {
        var world=simulation.World;
        var trees=world.LiveTrees.ToArray();
        var views=from pine in trees
                  where pine.Variant>.5 && world.Height(pine.X,pine.Z)>=4
                  from broadleaf in trees
                  let x=broadleaf.X-pine.X
                  let z=broadleaf.Z-pine.Z
                  let distance=x*x+z*z
                  where broadleaf.Variant<=.5 && distance is >=4 and <=12 &&
                      Math.Abs(world.Height(broadleaf.X,broadleaf.Z)-world.Height(pine.X,pine.Z))<=2
                  orderby Math.Abs(pine.X+18)+Math.Abs(pine.Z-4),distance
                  select new Vector3((pine.X+broadleaf.X+1)*.5f,
                      world.Height(pine.X,pine.Z)*Town.Core.World.Step+1,(pine.Z+broadleaf.Z+1)*.5f);
        camera.Target=views.DefaultIfEmpty(camera.Target).First();
        camera.Zoom=7;
    }

    private void SaveCameraCheck(string directory)
    {
        var times=cameraCheckTimes.Order().ToArray();
        double Percentile(double quantile)=>times[Math.Clamp((int)Math.Ceiling(times.Length*quantile)-1,0,times.Length-1)];
        var metrics=new { samples=times.Length, averageMs=times.Average(), medianMs=Percentile(.5), p95Ms=Percentile(.95),
            worstMs=times[^1], width=GraphicsDevice.PresentationParameters.BackBufferWidth,
            height=GraphicsDevice.PresentationParameters.BackBufferHeight,
            visibleTerrainChunks=terrain.VisibleChunks,broadleafAsset=Rendering.InstancedModels.BroadleafAsset,
            sceneWidth=drawnSceneWidth,sceneHeight=drawnSceneHeight,antialiasSamples=drawnSceneSamples };
        File.WriteAllText(Path.Combine(directory,"camera-metrics.json"),JsonSerializer.Serialize(metrics));
    }
}
