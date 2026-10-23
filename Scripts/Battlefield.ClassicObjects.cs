using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    void DrawClassicCrates(World world)
    {
        foreach(var crate in world.ClassicCrates)
        {
            if(!Controller.ViewVisible(crate.X,crate.Y))continue;
            var point=ProjectGround(new Vector2(crate.X+.5f,crate.Y+.5f));
            if(!new Rect2(Vector2.Zero,Size).Grow(65*Zoom).HasPoint(point))continue;
            var water=world.Map.Ground[world.Map.Index(crate.X,crate.Y)]==Terrain.Water && !world.Map.HasBridge(crate.X,crate.Y);
            Controller.Graphics.DrawOriginalCrate(this,water,point,Zoom,Colors.White,world.Tick);
        }
    }
    void DrawClassicProjectiles(World world)
    {
        foreach(var projectile in world.ClassicProjectiles)
        {
            if(!projectile.Alive || !Controller.ViewVisible(projectile.CellX,projectile.CellY))continue;
            var fraction=Math.Clamp((world.Tick-projectile.LaunchTick)/(float)Math.Max(1,projectile.ImpactTick-projectile.LaunchTick),0,1);
            var height=projectile.Kind.Equals("HORNET",StringComparison.OrdinalIgnoreCase) || projectile.Kind.Equals("ASW",StringComparison.OrdinalIgnoreCase)?65:35+MathF.Sin(fraction*MathF.PI)*105;
            var point=ProjectGround(new Vector2(projectile.X/(float)Rules.Scale,projectile.Y/(float)Rules.Scale))-new Vector2(0,height*Zoom);
            Controller.Graphics.DrawExpandedProjectile(this,projectile,point,Zoom,world);
        }
    }
}
