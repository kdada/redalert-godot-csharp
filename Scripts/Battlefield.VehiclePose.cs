using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    // Support plane only. Vertical voxel geometry is rotated/rasterized in
    // ArtLibrary.VehiclePose, never multiplied by this plane's 2D Jacobian.
    readonly record struct VehicleSurfacePose(Vector2 BasisX,Vector2 BasisY,Vector2 Lift,float Pitch,float Roll)
    {public Vector2 Gradient {get;init;}}
    public int VehiclesWithSlopePose {get;private set;}
    public float MaximumVehiclePitchDegrees {get;private set;}
    public float MaximumVehicleRollDegrees {get;private set;}
    void BeginVehiclePoseFrame()
    {VehiclesWithSlopePose=0;MaximumVehiclePitchDegrees=0;MaximumVehicleRollDegrees=0;}
    static float SampleVehicleContact(BattleMap map,Vector2 center,Vector2 contact,float centerHeight,Cell slope)
    {
        var from=new Cell((int)MathF.Floor(center.X),(int)MathF.Floor(center.Y));
        var to=new Cell((int)MathF.Floor(contact.X),(int)MathF.Floor(contact.Y));
        if(from==to || map.CanTraverseTerrain(from,to))return (float)map.SurfaceHeightAt(contact.X,contact.Y);
        // A contact beyond an impassable cliff is not support. Extend only
        // this cell's own surface plane; do not tilt toward the lower neighbor.
        return centerHeight+(contact.X-center.X)*slope.X/Rules.Scale+(contact.Y-center.Y)*slope.Y/Rules.Scale;
    }
    VehicleSurfacePose SampleVehiclePose(Unit unit)
    {
        var map=Controller.Battle!.Map;var center=new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale);
        var forward=new Vector2(unit.BodyFacingX,unit.BodyFacingY).Normalized();if(forward==Vector2.Zero)forward=Vector2.Right;
        var right=new Vector2(-forward.Y,forward.X);
        var frontExtent=unit.Type==UnitType.Apocalypse ? .48f : unit.Definition.Miner ? .46f : .4f;
        var sideExtent=unit.Type==UnitType.Apocalypse ? .3f : unit.Definition.Miner ? .28f : .24f;
        var centerHeight=(float)map.SurfaceHeightAt(center.X,center.Y);var slope=map.SurfaceSlopeFixedAt(unit.CellX,unit.CellY);
        var front=SampleVehicleContact(map,center,center+forward*frontExtent,centerHeight,slope);
        var rear=SampleVehicleContact(map,center,center-forward*frontExtent,centerHeight,slope);
        var left=SampleVehicleContact(map,center,center-right*sideExtent,centerHeight,slope);
        var rightHeight=SampleVehicleContact(map,center,center+right*sideExtent,centerHeight,slope);
        var along=(front-rear)/(2*frontExtent);var across=(rightHeight-left)/(2*sideExtent);
        var gradient=forward*along+right*across;
        var lift=new Vector2(0,-((front+rear+left+rightHeight)*.25f-centerHeight)*LandscapeLevelRise*Zoom);
        // Same 30deg camera and (30,15) ground vectors as the VXL projection.
        var groundUnit=30*MathF.Sqrt(2)*MathF.Cos(MathF.PI/6);
        var pitch=MathF.Atan(along*LandscapeLevelRise/groundUnit)*180/MathF.PI;
        var roll=MathF.Atan(across*LandscapeLevelRise/groundUnit)*180/MathF.PI;
        if(MathF.Abs(pitch)>.05f || MathF.Abs(roll)>.05f)
        {VehiclesWithSlopePose++;MaximumVehiclePitchDegrees=MathF.Max(MaximumVehiclePitchDegrees,MathF.Abs(pitch));MaximumVehicleRollDegrees=MathF.Max(MaximumVehicleRollDegrees,MathF.Abs(roll));}
        return new VehicleSurfacePose(Vector2.Right,Vector2.Down,lift,pitch,roll){Gradient=gradient};
    }
    void DrawVehicleGroundShadow(Unit unit,Vector2 bodyPoint,VehicleSurfacePose pose)
    {
        var groundPoint=bodyPoint-pose.Lift+new Vector2(1,2+unit.Altitude/(float)Rules.Scale*LandscapeLevelRise)*Zoom;
        var forward=new Vector2(unit.BodyFacingX,unit.BodyFacingY).Normalized();if(forward==Vector2.Zero)forward=Vector2.Right;
        var right=new Vector2(-forward.Y,forward.X);var points=new Vector2[20];
        for(var i=0;i<points.Length;i++)
        {
            var angle=i*MathF.Tau/points.Length;
            var offset=forward*(MathF.Cos(angle)*.42f)+right*(MathF.Sin(angle)*.25f);
            points[i]=groundPoint+new Vector2((offset.X-offset.Y)*30,(offset.X+offset.Y)*15-pose.Gradient.Dot(offset)*LandscapeLevelRise)*Zoom;
        }
        var area=0f;for(var i=0;i<points.Length;i++)area+=points[i].Cross(points[(i+1)%points.Length]);
        if(MathF.Abs(area)>.1f)DrawColoredPolygon(points,new Color(0,0,0,.35f));
        else
        {
            // Edge-on ground can have a line shadow; the 3D hull keeps volume.
            var lo=points[0];var hi=points[0];foreach(var p in points){if(p.X<lo.X)lo=p;if(p.X>hi.X)hi=p;}
            DrawLine(lo,hi,new Color(0,0,0,.35f),MathF.Max(1,Zoom));
        }
    }
}
