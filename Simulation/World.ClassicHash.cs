using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace RedAlert.Simulation;

public sealed partial class World
{
    static readonly JsonSerializerOptions FingerprintJson=new(){IncludeFields=true};
    static readonly Lazy<string> CachedContentFingerprint=new(CreateContentFingerprint);
    static readonly ConditionalWeakTable<ClassicMapDefinition,Lazy<string>> ImmutableMapFingerprints=new();
    public static string ContentFingerprint()=>CachedContentFingerprint.Value;

    static object[] StaticRuleData(Type type,bool constantsOnly=false)=>type.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
        .Where(field=>!constantsOnly || field.IsLiteral).OrderBy(field=>field.Name,StringComparer.Ordinal)
        .Select(field=>(object)new{field.Name,Value=field.GetValue(null)}).ToArray();
    static string CreateContentFingerprint()=>DataFingerprint(new
    {
        HashSchema=4,
        Constants=StaticRuleData(typeof(Rules),true),
        Countries=Enum.GetValues<Country>().Select(country=>new
        {
            Country=country,Name=Rules.CountryName(country),Faction=Rules.Side(country),
            Units=Enum.GetValues<UnitType>().Select(type=>Rules.Unit(type,country)).ToArray(),
            Buildings=Enum.GetValues<BuildingType>().Select(type=>Rules.Building(type,country)).ToArray()
        }).ToArray(),
        Combat=StaticRuleData(typeof(ClassicCombatRules)),Economy=StaticRuleData(typeof(ClassicEconomyRules)),
        Ramps=StaticRuleData(typeof(ClassicMapRamps)),MapObjects=ClassicMapObjectRules.Definitions,
        TerrainCombat=StaticRuleData(typeof(ClassicTerrainCombatRules)),TerrainWeapons=StaticRuleData(typeof(ClassicTerrainWeaponRules))
    });
    static string DataFingerprint(object data,bool canonical=true)
    {
        if(!canonical)return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data,FingerprintJson))).ToLowerInvariant();
        var element=JsonSerializer.SerializeToElement(data,FingerprintJson);
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream))WriteCanonicalJson(writer,element);
        var bytes=stream.GetBuffer().AsSpan(0,checked((int)stream.Length));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    static void WriteCanonicalJson(Utf8JsonWriter writer,JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach(var property in element.EnumerateObject().OrderBy(property=>property.Name,StringComparer.Ordinal))
            {writer.WritePropertyName(property.Name);WriteCanonicalJson(writer,property.Value);}
            writer.WriteEndObject();
        }
        else if(element.ValueKind==JsonValueKind.Array)
        {writer.WriteStartArray();foreach(var item in element.EnumerateArray())WriteCanonicalJson(writer,item);writer.WriteEndArray();}
        else element.WriteTo(writer);
    }
    static string ImmutableMapFingerprint(ClassicMapDefinition definition)=>ImmutableMapFingerprints.GetValue(definition,
        static map=>new Lazy<string>(()=>DataFingerprint(map,canonical:false))).Value;

    public ulong StateHash()
    {
        var h=new StableHash();h.Add(3);h.Add(ContentFingerprint());
        HashSettings(ref h,Settings);
        h.Add(Tick);h.Add(nextId);h.Add(Finished);h.Add(WinningTeam);h.Add(AcceptedCommands);h.Add(RejectedCommands);h.Add(PathSearches);
        h.Add(Map.Width);h.Add(Map.Height);h.Add(Map.NavigationVersion);
        h.Add(Map.PlayableMinU);h.Add(Map.PlayableMaxU);h.Add(Map.PlayableMinV);h.Add(Map.PlayableMaxV);
        h.Add(Map.OriginalDefinition is not null);
        if(Map.OriginalDefinition is { } original)
        {h.Add(original.Id);h.Add(original.SourceSha256);h.Add(ImmutableMapFingerprint(original));}
        h.Add(Map.Spawns.Length);foreach(var spawn in Map.Spawns){h.Add(spawn.X);h.Add(spawn.Y);}
        h.Add(Players.Length);foreach(var player in Players)HashPlayer(ref h,player);
        h.Add(Map.Ground.Length);foreach(var value in Map.Ground)h.Add((int)value);
        HashIntList(ref h,Map.Ore);HashIntList(ref h,Map.Buildings);
        h.Add(Map.Elevation.Length);foreach(var value in Map.Elevation)h.Add((int)value);
        h.Add(Map.Bridges.Length);foreach(var value in Map.Bridges)h.Add(value);
        h.Add(Map.Ramps.Length);foreach(var value in Map.Ramps)h.Add((int)value);
        HashIntList(ref h,Map.BridgeIds);h.Add(Map.BridgeSpans.Length);
        foreach(var span in Map.BridgeSpans)
        {
            h.Add(span.Id);h.Add((int)span.Axis);h.Add(span.Origin.X);h.Add(span.Origin.Y);h.Add(span.Length);h.Add(span.Width);
            h.Add(span.DeckHeightFixed);h.Add(span.BankA.X);h.Add(span.BankA.Y);h.Add(span.BankB.X);h.Add(span.BankB.Y);
        }
        HashTerrainCombat(ref h);
        h.Add(Units.Count);foreach(var unit in Units)HashUnit(ref h,unit);
        h.Add(Buildings.Count);foreach(var building in Buildings)HashBuilding(ref h,building);
        h.Add(ClassicProjectiles.Count);foreach(var projectile in ClassicProjectiles)HashProjectile(ref h,projectile);
        h.Add(ClassicAreaEffects.Count);
        foreach(var effect in ClassicAreaEffects)
        {
            h.Add(effect.SourceId);h.Add(effect.Owner);h.Add(effect.X);h.Add(effect.Y);h.Add(effect.RadiusFixed);
            h.Add(effect.CreatedTick);h.Add(effect.ExpireTick);h.Add(effect.Strength);h.Add(effect.Kind);h.Add(effect.NextTick);
        }
        h.Add(ClassicCrateRandomState);h.Add(NextClassicCrateTick);h.Add(NextClassicCrateId);h.Add(ClassicCrates.Count);
        foreach(var crate in ClassicCrates)
        {h.Add(crate.Id);h.Add(crate.X);h.Add(crate.Y);h.Add((int)crate.Kind);h.Add(crate.SpawnTick);h.Add(crate.ExpiresTick);h.Add(crate.Value);}
        h.Add(handlingClassicDeaths);h.Add(classicDeathQueue.Count);
        foreach(var entity in classicDeathQueue)
        {
            switch(entity)
            {
                case Unit unit:h.Add(1);HashUnit(ref h,unit);break;
                case Building building:h.Add(2);HashBuilding(ref h,building);break;
                case ClassicProjectile projectile:h.Add(3);HashProjectile(ref h,projectile);break;
                default:h.Add(entity.GetType().FullName);HashEntity(ref h,entity);break;
            }
        }
        // occupied/pathsThisTick/sightRows are derived scratch rebuilt before use.
        // Events, weapon events and crate pickups only notify presentation; they
        // cannot affect later simulation and are deliberately outside this hash.
        return h.Value;
    }
    static void HashSettings(ref StableHash h,MatchSettings settings)
    {
        h.Add(settings.Seed);h.Add(settings.StartingCredits);h.Add(settings.Width);h.Add(settings.Height);h.Add(settings.MapId);
        h.Add(settings.ClassicStart);h.Add(settings.StartingUnits);h.Add(settings.ShortGame);h.Add(settings.MCVRepacks);
        h.Add(settings.Crates);h.Add(settings.SuperWeapons);h.Add(settings.BuildNearAlly);h.Add(settings.GameSpeed);
        h.Add(settings.Players.Length);
        foreach(var setup in settings.Players){h.Add(setup.Name);h.Add((int)setup.Country);h.Add(setup.Team);}
    }
    static void HashPlayer(ref StableHash h,PlayerState p)
    {
        h.Add(p.Id);h.Add(p.Setup.Name);h.Add((int)p.Setup.Country);h.Add(p.Setup.Team);h.Add(p.Credits);
        h.Add(p.GeneratedPower);h.Add(p.UsedPower);h.Add(p.SupportReadyTick);h.Add(p.Defeated);
        h.Add(p.BuildingProduction is { } production?(int)production:-1);h.Add(p.BuildingProductionProgress);
        h.Add(p.BuildingProductionPaid);h.Add(p.BuildingProductionPaused);
        h.Add(p.StolenAlliedTech);h.Add(p.StolenSovietTech);h.Add(p.CrateMapRevealed);
        h.Add(p.IronCurtainReadyTick);h.Add(p.ChronosphereReadyTick);h.Add(p.WeatherReadyTick);h.Add(p.NukeReadyTick);
        h.Add(p.ChronosphereSourceX);h.Add(p.ChronosphereSourceY);h.Add(p.ChronosphereSelectingDestination);
        h.Add(p.PowerBlackoutUntil);h.Add(p.RadarBlackoutUntil);h.Add(p.VeteranInfantry);h.Add(p.VeteranVehicles);
        h.Add(p.Visibility.Length);foreach(var value in p.Visibility)h.Add((int)value);
    }
    static void HashEntity(ref StableHash h,Entity entity)
    {
        h.Add(entity.Id);h.Add(entity.Owner);h.Add(entity.Health);h.Add(entity.X);h.Add(entity.Y);
        h.Add(entity.Reload);h.Add(entity.LastFiredTick);h.Add(entity.LastTarget);
    }
    static void HashIntList(ref StableHash h,IReadOnlyList<int> values)
    {h.Add(values.Count);for(var i=0;i<values.Count;i++)h.Add(values[i]);}
    static void HashUnit(ref StableHash h,Unit u)
    {
        HashEntity(ref h,u);h.Add((int)u.Type);h.Add((int)u.OriginCountry);h.Add((int)u.Order);
        h.Add(u.GoalX);h.Add(u.GoalY);h.Add(u.TargetId);h.Add(u.GroundTargetX);h.Add(u.GroundTargetY);h.Add(u.PathIndex);h.Add(u.PathVersion);h.Add(u.NextPathTick);
        h.Add(u.BlockedTicks);h.Add(u.Cargo);h.Add(u.CargoBales);h.Add(u.HarvestTimer);h.Add(u.Returning);
        h.Add(u.FacingX);h.Add(u.FacingY);h.Add(u.BodyFacingX);h.Add(u.BodyFacingY);h.Add(u.Kills);HashIntList(ref h,u.Path);
        h.Add(u.Ammo);h.Add(u.AircraftDockId);h.Add(u.TransportId);h.Add(u.Deployed);HashIntList(ref h,u.Passengers);
        h.Add(u.Altitude);h.Add(u.PendingTransportId);h.Add(u.AirReloadTick);
        h.Add(u.InvulnerableUntil);h.Add(u.ControllerId);h.Add(u.ControlledTargetId);h.Add(u.ErasedById);h.Add(u.EraseProgress);
        h.Add(u.ErasingTargetId);h.Add(u.ParasiteHostId);h.Add(u.ParasiteId);h.Add(u.NextParasiteTick);h.Add(u.OriginalOwner);
        h.Add(u.BombTick);h.Add(u.BombOwner);h.Add(u.BombSourceId);h.Add(u.DisguiseOwner);h.Add(u.RevealedUntil);
        h.Add(u.DisabledUntil);h.Add(u.RadiationUntil);h.Add(u.SpecialOrder);h.Add(u.BombIsC4);h.Add(u.ClassicDeathHandled);
    }
    static void HashBuilding(ref StableHash h,Building b)
    {
        HashEntity(ref h,b);h.Add((int)b.Type);h.Add((int)b.FoundationCountry);h.Add(b.CellLeft);h.Add(b.CellTop);
        h.Add(b.RemainingBuildTicks);h.Add(b.BuildDurationTicks);h.Add(b.ProductionProgress);h.Add(b.ProductionPaid);h.Add(b.ProductionPaused);h.Add(b.RallyX);h.Add(b.RallyY);h.Add(b.Repairing);
        h.Add(b.Queue.Count);foreach(var type in b.Queue)h.Add((int)type);
        h.Add(b.FreeMinerPending);h.Add(b.PendingClones.Count);
        foreach(var clone in b.PendingClones){h.Add((int)clone.Type);h.Add((int)clone.OriginCountry);h.Add(clone.Owner);}
        h.Add(b.InvulnerableUntil);h.Add(b.ErasedById);h.Add(b.EraseProgress);h.Add(b.BombTick);h.Add(b.BombOwner);
        h.Add(b.BombSourceId);h.Add(b.BombIsC4);h.Add(b.ClassicDeathHandled);
    }
    static void HashProjectile(ref StableHash h,ClassicProjectile p)
    {
        HashEntity(ref h,p);h.Add(p.SourceId);h.Add(p.TargetId);h.Add(p.ImpactX);h.Add(p.ImpactY);h.Add(p.LaunchTick);
        h.Add(p.ImpactTick);h.Add(p.Damage);h.Add(p.Warhead);h.Add(p.Kind);h.Add(p.Returning);h.Add(p.TerrainImpactLevelFixed);
    }
}
