namespace RedAlert.Simulation;

// Existing protocol ordinals remain stable; vanilla additions are appended.
public enum Country { America, Russia, France, Britain, Germany, Korea, Iraq, Cuba, Libya }
public enum Faction { Allied, Soviet }
public enum MovementDomain { Land, Air, Water }
public enum ProductionCategory { Infantry, Vehicle, Aircraft, Naval }
public enum UnitType
{
    Gi, Conscript, Grizzly, Rhino, ChronoMiner, WarMiner, Engineer, Dog, Sniper, TeslaTank, PrismTank, Apocalypse, Ifv, FlakTrack,
    FlakTrooper, TeslaTrooper, Rocketeer, Tanya, Spy, Yuri, CrazyIvan, Desolator, Terrorist, ChronoLegionnaire,
    MirageTank, TankDestroyer, TerrorDrone, V3Launcher, DemolitionTruck, Mcv, Nighthawk, Kirov, Harrier, BlackEagle,
    AmphibiousTransport, Destroyer, AegisCruiser, AircraftCarrier, Dolphin, AttackSubmarine, SeaScorpion, Dreadnought, GiantSquid,
    ChronoCommando, PsiCommando, ChronoIvan, YuriPrime
}
public enum BuildingType
{
    ConstructionYard, PowerPlant, Refinery, Barracks, WarFactory, Radar, BattleLab, Pillbox, TeslaCoil, GrandCannon, Wall,
    NavalYard, ServiceDepot, OrePurifier, SpySatellite, GapGenerator, Chronosphere, WeatherControl, NuclearReactor,
    CloningVats, PsychicSensor, IronCurtain, NuclearMissile, SentryGun, PatriotMissile, PrismTower, FlakCannon
}
public enum CommandType { Move, Attack, AttackMove, Stop, Build, Train, CancelQueue, Rally, Sell, Repair, Support, Surrender, QueueBuilding, CancelBuilding, Deploy, EnterTransport, Unload, Special, SuperWeapon, AttackGround, PauseBuilding, ResumeBuilding, PauseQueue, ResumeQueue }
public enum UnitOrder { Idle, Move, Attack, AttackMove, Harvest, AttackGround }
public enum Terrain : byte { Grass, Dirt, Rock, Water }
public readonly record struct Cell(int X, int Y);

// Every entry is required (AND). A null faction accepts the original generic
// group, e.g. RADAR accepts either faction; an explicit faction means its INI ID.
public readonly record struct PrerequisiteDef(BuildingType Building,Faction? Faction=null);
public sealed record WeaponDef(string Id,int Damage,int RangeFixed,int Reload,int Burst=1,
    bool AntiAir=false,bool AntiGround=true,bool AntiSubmarine=false,bool Underwater=false,string Projectile="",string Warhead="");

public sealed record UnitDef(UnitType Type, string Name, int Cost, int Health, int Speed, int Damage,
    int Range, int Reload, int BuildTicks, int Sight, bool Infantry = false, bool Miner = false,
    Faction? Faction = null, Country? Country = null, bool Advanced = false, int Splash = 0,
    MovementDomain Domain=MovementDomain.Land,ProductionCategory Category=ProductionCategory.Vehicle,
    BuildingType? Producer=null,PrerequisiteDef[]? Prerequisites=null,string RulesId="",string ArtId="",string Cameo="",
    bool Buildable=true,bool Amphibious=false,int Ammo=0,int Passengers=0,int TechLevel=1,int OriginalSpeed=0,
    WeaponDef? PrimaryWeapon=null,WeaponDef? SecondaryWeapon=null,bool RequiresStolenAlliedTech=false,bool RequiresStolenSovietTech=false,
    int MaxInstances=0,Country[]? ForbiddenCountries=null,bool TargetsAir=false,bool TargetsGround=true,bool TargetsWater=true,
    bool AirportBound=false,bool Submersible=false,bool CanDeploy=false,bool AllowedToStart=true,string Armor="none");
public sealed record BuildingDef(BuildingType Type, string Name, int Cost, int Health, int Width,
    int Height, int BuildTicks, int Power, int Sight, bool Advanced = false,
    Faction? Faction = null, Country? Country = null, int Damage = 0, int Range = 0, int Reload = 0,
    ProductionCategory? Produces=null,bool IsDefense=false,bool WaterBound=false,PrerequisiteDef[]? Prerequisites=null,
    string RulesId="",string ArtId="",string Cameo="",bool Buildable=true,int TechLevel=1,int AirCapacity=0,
    WeaponDef? PrimaryWeapon=null,WeaponDef? SecondaryWeapon=null,int MaxInstances=0,string SuperWeapon="",string Armor="wood");

public static partial class Rules
{
    public const int Protocol = 3;
    public const string Revision = "command-alpha-13";
    public const int TicksPerSecond = 20;
    public const int BuildingPlacementTicks = 20;
    public const int Scale = 1024;
    public const int MaxPlayers = 8;
    public const int MaxEntities = 4096;
    public static readonly UnitDef[] Units=ClassicCatalog.CreateUnits(Country.America);
    static readonly UnitDef[] SovietUnits=ClassicCatalog.CreateUnits(Country.Russia);
    public static readonly BuildingDef[] Buildings=ClassicCatalog.CreateBuildings(Country.America);
    static readonly BuildingDef[] AlliedBuildings=ClassicCatalog.CreateBuildings(Country.Britain);
    static readonly BuildingDef[] SovietBuildings=ClassicCatalog.CreateBuildings(Country.Russia);
    public static Faction Side(Country country) => country is Country.Russia or Country.Iraq or Country.Cuba or Country.Libya ? Faction.Soviet : Faction.Allied;
    public static string CountryName(Country country) => country switch
    {
        Country.America=>"美国",Country.Russia=>"俄罗斯",Country.France=>"法国",Country.Britain=>"英国",Country.Germany=>"德国",
        Country.Korea=>"韩国",Country.Iraq=>"伊拉克",Country.Cuba=>"古巴",Country.Libya=>"利比亚",_=>throw new ArgumentOutOfRangeException(nameof(country))
    };
    public static UnitDef Unit(UnitType type) => Units[(int)type];
    public static UnitDef Unit(UnitType type,Country originCountry)=>(Side(originCountry)==Faction.Soviet?SovietUnits:Units)[(int)type];
    public static BuildingDef Building(BuildingType type) => Buildings[(int)type];
    public static BuildingDef Building(BuildingType type,Country foundationCountry) =>
        (Side(foundationCountry)==Faction.Soviet?SovietBuildings:foundationCountry==Country.America?Buildings:AlliedBuildings)[(int)type];
    public static bool NationallyAllowed(UnitDef d,Country c)=>(d.Country is null || d.Country==c) && !(d.ForbiddenCountries?.Contains(c)??false);
    public static bool Allowed(UnitDef d, Country c) => NationallyAllowed(d,c) && (d.Faction is null || d.Faction == Side(c));
    public static bool Allowed(BuildingDef d, Country c) => (d.Country is null || d.Country == c) && (d.Faction is null || d.Faction == Side(c));
    public static ProductionCategory Production(UnitType type)=>Unit(type).Category;
    public static BuildingType ProducerForUnit(UnitType type,Country country)=>Unit(type,country).Producer??throw new ArgumentException("单位没有生产建筑",nameof(type));
    public static BuildingType Producer(UnitType type,Country country)=>ProducerForUnit(type,country);
    public static IReadOnlyList<PrerequisiteDef> UnitRequirements(UnitType type,Country country)=>Unit(type,country).Prerequisites??[];
    public static IReadOnlyList<PrerequisiteDef> BuildingRequirements(BuildingType type,Country country)=>Building(type,country).Prerequisites??[];
    public static IReadOnlyList<PrerequisiteDef> Prerequisites(UnitDef d,Country country)=>UnitRequirements(d.Type,country);
    public static IReadOnlyList<PrerequisiteDef> Prerequisites(BuildingDef d,Country country)=>BuildingRequirements(d.Type,country);
    public static bool IsStolenUnit(UnitType type)=>Unit(type).RequiresStolenAlliedTech || Unit(type).RequiresStolenSovietTech;
    public static UnitType BasicInfantry(Country c) => Side(c) == Faction.Allied ? UnitType.Gi : UnitType.Conscript;
    public static UnitType BasicTank(Country c) => Side(c) == Faction.Allied ? UnitType.Grizzly : UnitType.Rhino;
    public static UnitType Miner(Country c) => Side(c) == Faction.Allied ? UnitType.ChronoMiner : UnitType.WarMiner;
}

public sealed record PlayerSetup(string Name, Country Country, int Team);
public sealed record MatchSettings(uint Seed, PlayerSetup[] Players, int StartingCredits = 10000, int Width = 96, int Height = 96,
    string MapId="",bool ClassicStart=true,int StartingUnits=10,bool ShortGame=true,bool MCVRepacks=true,bool Crates=true,bool SuperWeapons=true,bool BuildNearAlly=true,int GameSpeed=0)
{
    public void Validate()
    {
        if (Players is null || Players.Length is < 2 or > Rules.MaxPlayers) throw new ArgumentException("需要 2—8 位玩家");
        if (Width is <32 or >512 || Height is <32 or >512) throw new ArgumentException("地图尺寸必须为 32—512");
        if (StartingCredits is < 0 or > 1_000_000) throw new ArgumentException("初始资金越界");
        if(MapId is null || MapId.Length>128 || StartingUnits is <0 or >10 || GameSpeed!=0)throw new ArgumentException("遭遇战设置无效");
        if (Players.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 32 || !Enum.IsDefined(p.Country) || p.Team is < 0 or > 7))
            throw new ArgumentException("玩家设置无效");
    }
    public static MatchSettings Practice(int count = 4, uint seed = 20261018) => new(seed,
        Enumerable.Range(0, count).Select(i => new PlayerSetup(i == 0 ? "指挥官" : $"练习对手 {i}", (Country)(i % 9), i)).ToArray());
    // Existing focused checks intentionally retain their small, fully built bases.
    public static MatchSettings LegacyFixture(int count=4,uint seed=20261018)=>new(seed,
        Enumerable.Range(0,count).Select(i=>new PlayerSetup(i==0?"指挥官":$"练习对手 {i}",(Country)(i%4),i)).ToArray(),ClassicStart:false,Crates:false);
}

public sealed record GameCommand
{
    public int Tick { get; init; }
    public int Player { get; init; }
    public int Sequence { get; init; }
    public CommandType Type { get; init; }
    public int[] Actors { get; init; } = [];
    public int X { get; init; }
    public int Y { get; init; }
    public int Target { get; init; }
    public UnitType Unit { get; init; }
    public BuildingType Building { get; init; }
}

public sealed partial class PlayerState(int id, PlayerSetup setup, int credits, int cells)
{
    public int Id { get; } = id;
    public PlayerSetup Setup { get; } = setup;
    public int Credits = credits;
    public int GeneratedPower;
    public int UsedPower;
    public int SupportReadyTick = -1;
    public BuildingType? BuildingProduction;
    public int BuildingProductionProgress;
    public int BuildingProductionPaid;
    public bool BuildingProductionPaused;
    public bool BuildingReady => BuildingProduction is { } type && BuildingProductionProgress >= Rules.Building(type,Setup.Country).BuildTicks
        && BuildingProductionPaid >= Rules.Building(type,Setup.Country).Cost;
    public bool StolenAlliedTech;
    public bool StolenSovietTech;
    public bool Defeated;
    public readonly byte[] Visibility = new byte[cells];
    public bool Powered => GeneratedPower >= UsedPower;
}

public abstract class Entity
{
    public int Id;
    public int Owner;
    public int Health;
    public int X;
    public int Y;
    public int Reload;
    public int LastFiredTick = -100;
    public int LastTarget;
    public bool Alive => Health > 0;
    public int CellX => X / Rules.Scale;
    public int CellY => Y / Rules.Scale;
    public abstract int MaxHealth { get; }
    public abstract int Sight { get; }
}

public sealed partial class Unit : Entity
{
    public UnitType Type;
    public Country OriginCountry;
    public UnitDef Definition => Rules.Unit(Type,OriginCountry);
    public override int MaxHealth => Definition.Health;
    public override int Sight => Definition.Sight;
    public UnitOrder Order;
    public int GoalX;
    public int GoalY;
    public int TargetId;
    // Fixed-point aim survives a separate movement goal while force-firing terrain.
    public int GroundTargetX;
    public int GroundTargetY;
    public List<int> Path = [];
    public int PathIndex;
    public int NextPathTick;
    public int PathVersion = -1;
    public int BlockedTicks;
    public int Cargo;
    public int HarvestTimer;
    public bool Returning;
    public int FacingX = 1;
    public int FacingY;
    public int BodyFacingX = 1;
    public int BodyFacingY;
    public int Kills;
    public int Ammo;
    public int AircraftDockId;
    public List<int> Passengers=[];
    public int TransportId;
    public bool Deployed;
}

public sealed partial class Building : Entity
{
    public BuildingType Type;
    // Capturing changes ownership, never the original foundation or artwork.
    public Country FoundationCountry;
    public BuildingDef Definition => Rules.Building(Type,FoundationCountry);
    public override int MaxHealth => Definition.Health;
    public override int Sight => Definition.Sight;
    public int CellLeft;
    public int CellTop;
    public int RemainingBuildTicks;
    public int BuildDurationTicks;
    public int TotalBuildTicks => BuildDurationTicks > 0 ? BuildDurationTicks : Definition.BuildTicks;
    public float ConstructionProgress => Math.Clamp(1f - (float)RemainingBuildTicks / TotalBuildTicks, 0f, 1f);
    public bool Complete => RemainingBuildTicks == 0;
    public List<UnitType> Queue = [];
    public int ProductionProgress;
    // Only the active head has consumed money; queued followers are unpaid.
    public int ProductionPaid;
    public bool ProductionPaused;
    public int RallyX;
    public int RallyY;
    public bool Repairing;
}

public readonly record struct CombatEvent(int Tick, int Source, int Target, int X, int Y, int Damage, bool Explosion,
    int SourceX = 0, int SourceY = 0, UnitType? SourceUnit = null, BuildingType? SourceBuilding = null,
    bool TargetInfantry = false, int SourceOwner = -1, bool Secondary = false);
