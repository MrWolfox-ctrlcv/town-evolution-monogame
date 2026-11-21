using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Town.Game;

public sealed partial class TownGame
{
    private double cameraMotionUntil;
    private float? wheelZoomTarget;
    private Vector2 wheelAnchor;
    private bool MotionActive => (press is not null && dragged) || multiTouchGesture ||
        wheelZoomTarget is not null || renderClock.Elapsed.TotalMilliseconds < cameraMotionUntil;

    private void PaceFrame()
    {
        double now=renderClock.Elapsed.TotalMilliseconds;
        double remaining=nextFrameMs-now;
        if(remaining>2)Thread.Sleep((int)(remaining-1));
        nextFrameMs=renderClock.Elapsed.TotalMilliseconds+(MotionActive?1000.0/60:1000.0/30);
    }

    private void MarkCameraMotion() => cameraMotionUntil=renderClock.Elapsed.TotalMilliseconds+220;

    private void PanCamera(Vector2 delta,int height)
    {
        if(delta==Vector2.Zero)return;
        wheelZoomTarget=null;
        camera.Pan(delta,height);
        MarkCameraMotion();
    }

    private void QueueWheelZoom(int wheel,Vector2 point)
    {
        wheelAnchor=point;
        wheelZoomTarget=MathHelper.Clamp((wheelZoomTarget??camera.Zoom)*MathF.Exp(-wheel*.001f),7,52);
        MarkCameraMotion();
    }

    private void AdvanceWheelZoom(double elapsed,Viewport viewport)
    {
        if(wheelZoomTarget is not float targetZoom)return;
        float next=MathHelper.Lerp(camera.Zoom,targetZoom,1-MathF.Exp(-23f*Math.Clamp((float)elapsed,0,.1f)));
        if(Math.Abs(next-targetZoom)<.025f){next=targetZoom;wheelZoomTarget=null;}
        ZoomAround(next,wheelAnchor,viewport);
    }

    private void ZoomAround(float zoom,Vector2 anchor,Viewport viewport)
    {
        if(zoom==camera.Zoom)return;
        var offset=anchor-viewport.Bounds.Center.ToVector2();
        float previous=camera.Zoom;
        camera.Zoom=zoom;
        camera.Pan(offset*(1-previous/zoom),viewport.Height);
        MarkCameraMotion();
    }
}
