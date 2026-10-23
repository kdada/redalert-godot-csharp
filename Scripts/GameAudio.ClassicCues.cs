using Godot;
using RedAlert.Simulation;
using System;
using System.Collections.Generic;
using SimWorld=RedAlert.Simulation.World;

namespace RedAlert;

public sealed partial class GameAudio
{
    sealed record ClassicUnitCue(string Select,string Order,string Created);
    static IReadOnlyDictionary<string,string> WithClassicFiles(Dictionary<string,string> files)
    {AddClassicFiles(files);return files;}

    public long ObservedOriginalWeaponEvents {get;private set;}
    public long MissingOriginalWeaponCues {get;private set;}
    public long IntentionalSilentWeaponEvents {get;private set;}
    public long ObservedCratePickups {get;private set;}
    public long PlayedCrateCues {get;private set;}
    public long PlayedOriginalCreatedCues {get;private set;}

    static ClassicUnitCue? OriginalUnitCue(UnitType type,bool soviet)
    {
        if(soviet && ClassicSovietUnitCues.TryGetValue(type,out var specific))return specific;
        return ClassicUnitCues.GetValueOrDefault(type);
    }
    bool OriginalSelection(UnitType type,bool order,bool soviet)
    {
        var source=OriginalUnitCue(type,soviet);var cue=order?source?.Order:source?.Select;
        if(string.IsNullOrEmpty(cue))return false;
        Cue(cue,true,-5,4,250);return true;
    }
    // Optional full-unit hook for captured factories: caller may supply the unit's
    // original country rather than the current owner's faction. Presentation only.
    public void SelectionFor(Unit selected,bool order=false)
        =>Selection(order,selected.Type,Rules.Side(selected.OriginCountry)==Faction.Soviet);

    void OriginalCreated(Unit unit)
    {
        var cue=OriginalUnitCue(unit.Type,Rules.Side(unit.OriginCountry)==Faction.Soviet)?.Created;
        if(!string.IsNullOrEmpty(cue) && Cue(cue,true,-5,6,450))PlayedOriginalCreatedCues++;
    }

    void ObserveOriginalWeapons(SimWorld world,int player,bool validPlayer,bool spectating,Vector2 listenerCell)
    {
        var shots=0;
        foreach(var fired in world.ClassicWeaponEvents)
        {
            // World.Step increments Tick after simulation. The list is transient,
            // but a finished world can retain its last list; never replay that tail.
            if(fired.Tick!=world.Tick-1)continue;
            ObservedOriginalWeaponEvents++;
            var audible=spectating || validPlayer && (fired.Owner==player || world.Visible(player,fired.X/Rules.Scale,fired.Y/Rules.Scale));
            if(!audible){HiddenShotsSuppressed++;continue;}
            if(!ClassicWeaponCues.TryGetValue(fired.WeaponId,out var cue)){MissingOriginalWeaponCues++;continue;}
            if(cue.Length==0){IntentionalSilentWeaponEvents++;continue;}
            var distanceDb=Attenuation(fired.X,fired.Y,listenerCell);
            if(shots>=5 || distanceDb<=-32)continue;
            // No simulation RNG or unit fields are changed by audio variation.
            if(Cue(cue,false,-8+distanceDb,2,42,.96f+((fired.Source*17+fired.Tick)&7)*.011f))shots++;
        }
    }

    void ObserveOriginalCrates(SimWorld world,int player,bool validPlayer,bool spectating)
    {
        foreach(var pickup in world.ClassicCratePickups)
        {
            if(pickup.Tick!=world.Tick-1)continue;
            ObservedCratePickups++;
            // These global reward cues belong to the collecting player; viewing
            // another player's hidden reward must not reveal it through audio.
            if(!validPlayer || spectating || pickup.Player!=player)continue;
            var cue=ClassicCrateCue(pickup.Kind);
            if(cue.Length>0 && Cue(cue,false,-2,8,0))PlayedCrateCues++;
        }
    }
}
