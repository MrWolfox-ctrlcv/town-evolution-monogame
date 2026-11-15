namespace Town.Game;
public static class HudMetrics
{
    public const int TrayWidth=100,StartY=82,ButtonSize=84;
    public static float Scale(int height)=>Math.Clamp(height/760f,1,2.5f);
    public static int Slot(int height)=>Math.Clamp((height-204)/6,54,88);
}
