using global::Android.App;
using global::Android.Content.PM;
using global::Android.OS;
using global::Android.Views;
using Microsoft.Xna.Framework;
using Town.Game;
namespace Town.Android;
[Activity(Name="local.townevolution.foundation.MainActivity",Label="小镇进化",MainLauncher=true,Exported=true,Icon="@drawable/icon",ScreenOrientation=ScreenOrientation.SensorLandscape,ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.Keyboard|ConfigChanges.KeyboardHidden|ConfigChanges.ScreenSize)]
public sealed class Activity1 : AndroidGameActivity
{
    private TownGame? game;
    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if(hasFocus&&OperatingSystem.IsAndroidVersionAtLeast(30)&&Window?.InsetsController is { } controller)
        {
            controller.SystemBarsBehavior=(int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            controller.Hide(WindowInsets.Type.SystemBars());
        }
    }
    private void UpdateGestureAreas(View view)
    {
        if(!OperatingSystem.IsAndroidVersionAtLeast(29)||view.Width<=0||view.Height<=0)return;
        float scale=HudMetrics.Scale(view.Height),density=Resources?.DisplayMetrics?.Density??1;
        int height=(int)(view.Height/scale),slot=HudMetrics.Slot(height);
        // Android's edge-back gesture otherwise cancels drags starting on the trays.
        // Divide its per-edge allowance among the six actual control centres.
        float band=Math.Min(slot-6,200*density/(6*scale)-2);
        var areas=new List<global::Android.Graphics.Rect>();
        for(int i=0;i<6;i++)
        {
            float leftY=HudMetrics.StartY+(i+.5f)*slot-1,rightY=leftY;
            areas.Add(new(0,(int)((leftY-band/2)*scale),(int)(HudMetrics.TrayWidth*scale),(int)((leftY+band/2)*scale)));
            areas.Add(new(view.Width-(int)(HudMetrics.TrayWidth*scale),(int)((rightY-band/2)*scale),view.Width,(int)((rightY+band/2)*scale)));
        }
        view.SystemGestureExclusionRects=areas;
    }
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.Fullscreen);
        string root=FilesDir?.AbsolutePath??throw new InvalidOperationException("Application files directory is unavailable.");
        bool review=Intent?.GetBooleanExtra("review",false)??false;
        var session=Intent?.GetStringExtra("reviewSession")??"default";
        if(!System.Text.RegularExpressions.Regex.IsMatch(session,"^[A-Za-z0-9-]{1,40}$"))session="default";
        game=new TownGame(new GameOptions(Path.Combine(root,review?"review-save-"+session:"save"),review?Path.Combine(root,"review",session):null,false));
        var view=game.Services.GetService(typeof(View)) as View??throw new InvalidOperationException("MonoGame view is unavailable.");
        view.LayoutChange+=(_,_)=>UpdateGestureAreas(view);
        SetContentView(view);view.Post(()=>UpdateGestureAreas(view));game.Run();
    }
}
