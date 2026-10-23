# RA2 vanilla 1.006 rules research

This catalog extracts numeric and identity data only; no original executable implementation is copied. Runtime behavior is reconstructed independently.

## Sources

- Original retained rules: `Assets/Reference/Visual/Metadata/rules.ini`, SHA256 `fd1e95cea0306ea78049dc81c8cd816e18c28c496872a1ff02edd50bd082062f`.
- Original retained art: `Assets/Reference/Details/art.ini`, SHA256 `b477f861063a9509e87b7836b78190204c73bea5ea5595a842c456f5920ea223`.
- Original data mirror, fixed revision: https://github.com/toarrntnz/ts-redalert2-assets/tree/acfa5b834899e7fab4c8499ccb4b49c402927b44
- Vanilla version and nine subfactions: https://cncnz.com/games/red-alert-2/
- Country IDs and artwork mapping: https://modenc.renegadeprojects.com/Countries
- Vanilla Allied units: https://cncnz.com/games/red-alert-2/allied-units/
- Vanilla Soviet units: https://cncnz.com/games/red-alert-2/soviet-units/
- Vanilla Allied structures: https://cncnz.com/games/red-alert-2/allied-structures/
- Vanilla Soviet structures: https://cncnz.com/games/red-alert-2/soviet-structures/

## Country mapping

|Stable C# ordinal|Country|Original house|Vanilla exclusive|
|---|---|---|---|
|0|America|Americans|Paratroopers / AMRADR|
|1|Russia|Russians|Tesla Tank TTNK|
|2|France|French|Grand Cannon GTGCAN|
|3|Britain|British|Sniper SNIPE|
|4|Germany|Germans|Tank Destroyer TNKD|
|5|Korea|Alliance|Black Eagle BEAG; ORCA excluded|
|6|Iraq|Arabs|Desolator DESO|
|7|Cuba|Confederation|Terrorist TERROR|
|8|Libya|Africans|Demolition Truck DTRUCK|

Original INI house order is different; preserve existing project ordinals and map original IDs explicitly.

## INFANTRYTYPES ordinary/stolen-tech candidates (21)

|Rules ID : line|Name|Cost / HP / speed|Tech|Prerequisites|Country restriction|Primary / secondary|Art image / cameo|Foundation / producer|
|---|---|---|---|---|---|---|---|---|
|E1:3130|GI|200/125/4|1|GAPILE|required:-; forbidden:-|M60 / Para|GI / GIICON|- / -|
|E2:3221|Conscript|100/125/4|1|NAHAND|required:-; forbidden:-|M1Carbine / -|CONS / E2ICON|- / -|
|SHK:3301|Shock Trooper|500/130/4|5|NAHAND|required:-; forbidden:-|ElectricBolt / AssaultBolt|SHK / SHKICON|- / -|
|ENGINEER:3342|Engineer|500/75/4|1|Barracks|required:-; forbidden:Russians,Confederation,Africans,Arabs|DefuseKit / VirtualScanner|ENGINEER / ENGNICON|- / -|
|JUMPJET:3433|Rocketeer|600/125/9|3|GAPILE,RADAR|required:-; forbidden:-|20mm / -|ROCK / JJETICON|- / -|
|YURI:3590|Yuri|1200/100/4|10|NAHAND,NATECH|required:-; forbidden:-|MindControl / PsiWave|YURI / YURIICON|- / -|
|IVAN:3636|Crazy Ivan|600/125/4|5|NAHAND,NARADR|required:-; forbidden:-|IvanBomber / -|IVAN / IVANICON|- / -|
|DESO:3684|Desolater|600/150/4|8|NAHAND,RADAR|required:Arabs; forbidden:-|RadBeamWeapon / RadEruptionWeapon|DESO / DESOICON|- / -|
|DOG:3783|Attack Dog|200/100/8|2|Barracks|required:-; forbidden:British,French,Germans,Americans,Alliance|BadTeeth / VirtualScanner|DOG / DOGICON|- / -|
|CLEG:3733|Chrono Legionnaire|1500/125/5|10|GAPILE,TECH|required:-; forbidden:-|NeutronRifle / -|CLEG / CLEGICON|- / -|
|SPY:4719|Spy|1000/100/4|5|GAPILE,GATECH|required:-; forbidden:-|MakeupKit / -|SPY / SPYICON|- / -|
|CCOMAND:4808|Chrono Commando|2000/100/5|9|GAPILE; REQUIRESSTOLENALLIEDTECH=yes|required:-; forbidden:-|ChronoMP5 / FakeC4|CCOMAND / CCOMICON|- / -|
|PTROOP:4859|Psi-Corp Trooper|1000/100/5|9|GAPILE; REQUIRESSTOLENSOVIETTECH=yes|required:-; forbidden:-|MindControl / -|PTROOP / PSICICON|- / -|
|CIVAN:4908|Chrono Ivan|1000/100/6|9|NAHAND; REQUIRESSTOLENALLIEDTECH=yes|required:-; forbidden:-|IvanBomber / -|CIVAN / IVNCICON|- / -|
|YURIPR:4957|Yuri Prime|2000/200/6|9|NAHAND; REQUIRESSTOLENSOVIETTECH=yes|required:-; forbidden:-|SuperMindControl / PsiWave|YURIPR / YURPICON|- / -|
|SNIPE:3179|Sniper|600/125/4|1|GAPILE,RADAR|required:British; forbidden:-|AWP / -|SNIPE / SNIPICON|- / -|
|TANY:3540|Tanya|1000/125/5|9|GAPILE,GATECH|required:-; forbidden:-|DoublePistols / Sapper|TANY / TANYICON|- / -|
|FLAKT:3260|Flak Trooper|300/100/4|1|NAHAND,NARADR|required:-; forbidden:-|FlakGuyGun / FlakGuyAAGun|FLAKT / FLKTICON|- / -|
|TERROR:4761|Terrorist|200/75/6|5|NAHAND,RADAR|required:Confederation; forbidden:-|TerrorBomb / -|TRST / TRSTICON|- / -|
|SENGINEER:3387|Soviet Engineer|500/75/4|1|Barracks|required:-; forbidden:British,French,Germans,Americans,Alliance|DefuseKit / VirtualScanner|ENGINEER / ENGNICON|- / -|
|ADOG:3834|Allied Attack Dog|200/100/8|2|Barracks|required:-; forbidden:Russians,Confederation,Africans,Arabs|GoodTeeth / VirtualScanner|ADOG / ADOGICON|- / -|

<details><summary>Additional exact data and weapon facts</summary>

```text
E1 NAME=GI TECHLEVEL=1 COST=200 STRENGTH=125 SPEED=4 SIGHT=5 PREREQUISITE=GAPILE OWNER=British,French,Germans,Americans,Alliance PRIMARY=M60 SECONDARY=Para MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:M60 DAMAGE=15 ROF=20 RANGE=4 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleLow REPORT=GIAttack 
  SECONDARY:PARA DAMAGE=15 ROF=15 RANGE=5 SPEED=100 WARHEAD=SSA PROJECTILE=InvisibleLow REPORT=GIAttackDeployed 
E2 NAME=Conscript TECHLEVEL=1 COST=100 STRENGTH=125 SPEED=4 SIGHT=5 PREREQUISITE=NAHAND OWNER=Russians,Confederation,Africans,Arabs PRIMARY=M1Carbine MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:M1CARBINE DAMAGE=15 ROF=25 RANGE=4 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleLow REPORT=ConscriptAttack 
SHK NAME=Shock Trooper TECHLEVEL=5 COST=500 STRENGTH=130 SPEED=4 SIGHT=6 PREREQUISITE=NAHAND OWNER=Russians,Confederation,Africans,Arabs PRIMARY=ElectricBolt SECONDARY=AssaultBolt MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:ELECTRICBOLT DAMAGE=50 ROF=60 RANGE=3 SPEED=100 WARHEAD=Shock PROJECTILE=InvisibleLow REPORT=TeslaTroopAttack 
  SECONDARY:ASSAULTBOLT DAMAGE=10 ROF=25 RANGE=1.83 SPEED=100 WARHEAD=ElectricAssault PROJECTILE=InvisibleLow REPORT=TeslaTroopRechargeCoil 
ENGINEER NAME=Engineer TECHLEVEL=1 COST=500 STRENGTH=75 SPEED=4 SIGHT=4 PREREQUISITE=Barracks FORBIDDENHOUSES=Russians,Confederation,Africans,Arabs OWNER=Russians,Confederation,Africans,Arabs,British,French,Germans,Americans,Alliance PRIMARY=DefuseKit SECONDARY=VirtualScanner MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:DEFUSEKIT DAMAGE=1 ROF=20 RANGE=1.5 SPEED=100 WARHEAD=BombDisarm PROJECTILE=InvisibleAll REPORT=DefuseKit projectile.AA=yes projectile.AG=yes
  SECONDARY:VIRTUALSCANNER DAMAGE=1 RANGE=5 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleAll projectile.AA=yes projectile.AG=yes
JUMPJET NAME=Rocketeer TECHLEVEL=3 COST=600 STRENGTH=125 SPEED=9 SIGHT=8 PREREQUISITE=GAPILE,RADAR OWNER=British,French,Germans,Americans,Alliance PRIMARY=20mm MOVEMENTZONE=Fly LOCOMOTOR={92612C46-F71F-11d1-AC9F-006008055BB5}
  PRIMARY:20MM DAMAGE=25 ROF=30 RANGE=5 SPEED=100 WARHEAD=SSA PROJECTILE=Invisible3 REPORT=RocketeerAttack projectile.AA=yes projectile.AG=yes
YURI NAME=Yuri TECHLEVEL=10 COST=1200 STRENGTH=100 SPEED=4 SIGHT=12 PREREQUISITE=NAHAND,NATECH OWNER=Russians,Confederation,Africans,Arabs PRIMARY=MindControl SECONDARY=PsiWave MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:MINDCONTROL DAMAGE=10 ROF=200 RANGE=7 SPEED=100 WARHEAD=Controller PROJECTILE=PsychicControl 
  SECONDARY:PSIWAVE DAMAGE=250 ROF=50 RANGE=1 SPEED=1 WARHEAD=PsiPulse PROJECTILE=Psychic REPORT=YuriDeploy 
IVAN NAME=Crazy Ivan TECHLEVEL=5 COST=600 STRENGTH=125 SPEED=4 SIGHT=6 PREREQUISITE=NAHAND,NARADR OWNER=Russians,Confederation,Africans,Arabs PRIMARY=IvanBomber MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:IVANBOMBER DAMAGE=400 ROF=50 RANGE=1.5 WARHEAD=IvanBomb PROJECTILE=Invisible 
DESO NAME=Desolater TECHLEVEL=8 COST=600 STRENGTH=150 SPEED=4 SIGHT=6 PREREQUISITE=NAHAND,RADAR REQUIREDHOUSES=Arabs OWNER=Russians,Confederation,Africans,Arabs PRIMARY=RadBeamWeapon SECONDARY=RadEruptionWeapon MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:RADBEAMWEAPON DAMAGE=125 ROF=50 RANGE=6 SPEED=100 WARHEAD=RadBeamWarhead PROJECTILE=InvisibleLow REPORT=DesolatorAttack 
  SECONDARY:RADERUPTIONWEAPON DAMAGE=1 ROF=60 RANGE=4 SPEED=1 WARHEAD=RadEruptionWarhead PROJECTILE=InvisibleLow REPORT=DesolatorDeploy 
DOG NAME=Attack Dog TECHLEVEL=2 COST=200 STRENGTH=100 SPEED=8 SIGHT=9 PREREQUISITE=Barracks FORBIDDENHOUSES=British,French,Germans,Americans,Alliance OWNER=Russians,Confederation,Africans,Arabs,British,French,Germans,Americans,Alliance PRIMARY=BadTeeth SECONDARY=VirtualScanner MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:BADTEETH DAMAGE=30 ROF=30 RANGE=1.5 SPEED=30 WARHEAD=ParasiteDog PROJECTILE=DOGJUMP REPORT=DogAttack projectile.AA=no projectile.ROT=8
  SECONDARY:VIRTUALSCANNER DAMAGE=1 RANGE=5 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleAll projectile.AA=yes projectile.AG=yes
CLEG NAME=Chrono Legionnaire TECHLEVEL=10 COST=1500 STRENGTH=125 SPEED=5 SIGHT=8 PREREQUISITE=GAPILE,TECH OWNER=British,French,Germans,Americans,Alliance PRIMARY=NeutronRifle MOVEMENTZONE=Infantry LOCOMOTOR={4A582747-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:NEUTRONRIFLE DAMAGE=8 ROF=120 RANGE=5 SPEED=100 WARHEAD=ChronoBeam PROJECTILE=InvisibleLow REPORT=ChronoLegionAttack 
SPY NAME=Spy TECHLEVEL=5 COST=1000 STRENGTH=100 SPEED=4 SIGHT=9 PREREQUISITE=GAPILE,GATECH OWNER=British,French,Germans,Americans,Alliance PRIMARY=MakeupKit MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:MAKEUPKIT DAMAGE=1 ROF=100 RANGE=-2 SPEED=100 WARHEAD=Snapshot PROJECTILE=InvisibleAll REPORT=SpyAttack projectile.AA=yes projectile.AG=yes
CCOMAND NAME=Chrono Commando TECHLEVEL=9 COST=2000 STRENGTH=100 SPEED=5 SIGHT=8 PREREQUISITE=GAPILE OWNER=British,French,Germans,Americans,Alliance PRIMARY=ChronoMP5 SECONDARY=FakeC4 MOVEMENTZONE=Infantry LOCOMOTOR={4A582747-9839-11d1-B709-00A024DDAFD1} REQUIRESSTOLENALLIEDTECH=yes
  PRIMARY:CHRONOMP5 DAMAGE=125 ROF=10 RANGE=6 SPEED=100 WARHEAD=HollowPointNoBuilding PROJECTILE=InvisibleLow REPORT=SealAttack 
  SECONDARY:FAKEC4 DAMAGE=5000 ROF=10 RANGE=1.5 SPEED=100 WARHEAD=FakeC4WH PROJECTILE=InvisibleLow REPORT=SealPlaceBomb 
PTROOP NAME=Psi-Corp Trooper TECHLEVEL=9 COST=1000 STRENGTH=100 SPEED=5 SIGHT=8 PREREQUISITE=GAPILE OWNER=British,French,Germans,Americans,Alliance PRIMARY=MindControl MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1} REQUIRESSTOLENSOVIETTECH=yes
  PRIMARY:MINDCONTROL DAMAGE=10 ROF=200 RANGE=7 SPEED=100 WARHEAD=Controller PROJECTILE=PsychicControl 
CIVAN NAME=Chrono Ivan TECHLEVEL=9 COST=1000 STRENGTH=100 SPEED=6 SIGHT=8 PREREQUISITE=NAHAND OWNER=Russians,Confederation,Africans,Arabs PRIMARY=IvanBomber MOVEMENTZONE=Infantry LOCOMOTOR={4A582747-9839-11d1-B709-00A024DDAFD1} REQUIRESSTOLENALLIEDTECH=yes
  PRIMARY:IVANBOMBER DAMAGE=400 ROF=50 RANGE=1.5 WARHEAD=IvanBomb PROJECTILE=Invisible 
YURIPR NAME=Yuri Prime TECHLEVEL=9 COST=2000 STRENGTH=200 SPEED=6 SIGHT=8 PREREQUISITE=NAHAND OWNER=Russians,Confederation,Africans,Arabs PRIMARY=SuperMindControl SECONDARY=PsiWave MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1} BUILDLIMIT=1 REQUIRESSTOLENSOVIETTECH=yes
  PRIMARY:SUPERMINDCONTROL DAMAGE=10 ROF=200 RANGE=30 SPEED=100 WARHEAD=Controller PROJECTILE=PsychicControl 
  SECONDARY:PSIWAVE DAMAGE=250 ROF=50 RANGE=1 SPEED=1 WARHEAD=PsiPulse PROJECTILE=Psychic REPORT=YuriDeploy 
SNIPE NAME=Sniper TECHLEVEL=1 COST=600 STRENGTH=125 SPEED=4 SIGHT=8 PREREQUISITE=GAPILE,RADAR REQUIREDHOUSES=British OWNER=British,French,Germans,Americans,Alliance PRIMARY=AWP MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:AWP DAMAGE=125 ROF=150 RANGE=14 SPEED=100 WARHEAD=HollowPoint PROJECTILE=InvisibleLow REPORT=SniperAttack 
TANY NAME=Tanya TECHLEVEL=9 COST=1000 STRENGTH=125 SPEED=5 SIGHT=8 PREREQUISITE=GAPILE,GATECH OWNER=British,French,Germans,Americans,Alliance PRIMARY=DoublePistols SECONDARY=Sapper MOVEMENTZONE=AmphibiousDestroyer LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:DOUBLEPISTOLS DAMAGE=125 ROF=10 RANGE=6 SPEED=100 WARHEAD=HollowPoint PROJECTILE=InvisibleLow REPORT=TanyaAttack 
  SECONDARY:SAPPER DAMAGE=2500 ROF=100 RANGE=1.5 WARHEAD=Super PROJECTILE=Invisible5 REPORT=SealPlaceBomb projectile.AA=no projectile.AG=no
FLAKT NAME=Flak Trooper TECHLEVEL=1 COST=300 STRENGTH=100 SPEED=4 SIGHT=5 PREREQUISITE=NAHAND,NARADR OWNER=Russians,Confederation,Africans,Arabs PRIMARY=FlakGuyGun SECONDARY=FlakGuyAAGun MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:FLAKGUYGUN DAMAGE=20 ROF=20 RANGE=5 SPEED=50 WARHEAD=FlakTWH PROJECTILE=FlakTProj REPORT=FlakTrackAttackGround projectile.AA=no projectile.AG=yes projectile.INACCURATE=yes
  SECONDARY:FLAKGUYAAGUN DAMAGE=20 ROF=25 RANGE=8 SPEED=100 WARHEAD=FlakGuyWH PROJECTILE=FlakProj REPORT=FlakTrackAttackAir projectile.AA=yes projectile.AG=no projectile.INACCURATE=yes
TERROR NAME=Terrorist TECHLEVEL=5 COST=200 STRENGTH=75 SPEED=6 SIGHT=9 PREREQUISITE=NAHAND,RADAR REQUIREDHOUSES=Confederation OWNER=Russians,Confederation,Africans,Arabs PRIMARY=TerrorBomb MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:TERRORBOMB DAMAGE=225 ROF=10 RANGE=1.5 WARHEAD=TerrorBombWH PROJECTILE=Invisible 
SENGINEER NAME=Soviet Engineer TECHLEVEL=1 COST=500 STRENGTH=75 SPEED=4 SIGHT=4 PREREQUISITE=Barracks FORBIDDENHOUSES=British,French,Germans,Americans,Alliance OWNER=Russians,Confederation,Africans,Arabs,British,French,Germans,Americans,Alliance PRIMARY=DefuseKit SECONDARY=VirtualScanner MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:DEFUSEKIT DAMAGE=1 ROF=20 RANGE=1.5 SPEED=100 WARHEAD=BombDisarm PROJECTILE=InvisibleAll REPORT=DefuseKit projectile.AA=yes projectile.AG=yes
  SECONDARY:VIRTUALSCANNER DAMAGE=1 RANGE=5 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleAll projectile.AA=yes projectile.AG=yes
ADOG NAME=Allied Attack Dog TECHLEVEL=2 COST=200 STRENGTH=100 SPEED=8 SIGHT=9 PREREQUISITE=Barracks FORBIDDENHOUSES=Russians,Confederation,Africans,Arabs OWNER=Russians,Confederation,Africans,Arabs,British,French,Germans,Americans,Alliance PRIMARY=GoodTeeth SECONDARY=VirtualScanner MOVEMENTZONE=Infantry LOCOMOTOR={4A582744-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:GOODTEETH DAMAGE=30 ROF=30 RANGE=1.5 SPEED=30 WARHEAD=ParasiteDog PROJECTILE=ADOGJUMP REPORT=DogAttack projectile.AA=no projectile.ROT=8
  SECONDARY:VIRTUALSCANNER DAMAGE=1 RANGE=5 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleAll projectile.AA=yes projectile.AG=yes
```

</details>

## VEHICLETYPES ordinary/stolen-tech candidates (28)

|Rules ID : line|Name|Cost / HP / speed|Tech|Prerequisites|Country restriction|Primary / secondary|Art image / cameo|Foundation / producer|
|---|---|---|---|---|---|---|---|---|
|AMCV:6090|Allied Construction Vehicle|3000/1000/4|10|GAWEAP,GADEPT|required:-; forbidden:-|- / -|MCV / MCVICON|- / -|
|HARV:5924|War Miner|1400/1000/4|1|NAWEAP,PROC|required:-; forbidden:-|20mmRapid / -|HARV / HARVICON|- / -|
|APOC:5011|Apocalypse|1750/800/4|7|NAWEAP,NATECH|required:-; forbidden:-|120mmx / MammothTusk|MTNK / MTNKICON|- / -|
|HTNK:6475|Rhino Heavy Tank|900/400/6|2|NAWEAP|required:-; forbidden:-|120mm / -|HTNK / HTNKICON|- / -|
|SAPC:6946|Armored Transport|900/300/6|2|NAYARD|required:-; forbidden:-|- / -|TRS / SAPCICON|- / -|
|MTNK:6569|Grizzly Battle Tank|700/300/7|2|GAWEAP|required:-; forbidden:-|105mm / -|GTNK / GTNKICON|- / -|
|CARRIER:5347|Aircraft Carrier|2000/800/4|7|GAYARD,TECH|required:-; forbidden:-|HornetLauncher / -|CARRIER / CARRICON|- / -|
|V3:5711|V3 Launcher|800/150/4|3|NAWEAP,NARADR|required:-; forbidden:-|V3Launcher / -|V3 / V3ICON|- / -|
|ZEP:7981|Kirov Airship|2000/2000/5|10|NAWEAP,NATECH|required:-; forbidden:-|BlimpBomb / -|ZEP / ZEPICON|- / -|
|DRON:5762|Terror Drone|500/100/10|4|NAWEAP|required:-; forbidden:-|DroneJump / VirtualScanner|DRON / DRONICON|- / -|
|HTK:6231|Flak Track|500/180/8|3|NAWEAP|required:-; forbidden:-|FlakTrackGun / FlakTrackAAGun|HTK / HTKICON|- / -|
|DEST:5656|Destroyer|1000/600/6|4|GAYARD|required:-; forbidden:-|155mm / ASWLauncher|DEST / desticon|- / -|
|SUB:5182|Typhoon Attack Sub|1000/600/4|2|NAYARD|required:-; forbidden:-|SubTorpedo / -|SUB / SubICON|- / -|
|AEGIS:5285|Aegis Cruiser|1200/800/4|7|GAYARD,RADAR|required:-; forbidden:-|Medusa / -|AEGIS / AGISICON|- / -|
|LCRF:6167|Landing Craft|900/300/6|4|GAYARD|required:-; forbidden:-|- / -|LCRF / LANDICON|- / -|
|DRED:5450|Dreadnought|2000/800/4|6|NAYARD,NATECH|required:-; forbidden:-|DredLauncher / -|DRED / DREDICON|- / -|
|SHAD:7912|BlackHawk Transport|1000/175/14|7|GAWEAP|required:-; forbidden:-|BlackHawkCannon / -|SHAD / SHADICON|- / -|
|SQD:5818|Giant Squid|1000/200/8|9|NAYARD,NATECH|required:-; forbidden:-|SquidGrab / SquidPunch|SQD / SQDICON|- / -|
|DLPH:5230|Dolphin|500/200/8|5|GAYARD,GATECH|required:-; forbidden:-|SonicZap / -|DLPH / DLPHICON|- / -|
|SMCV:6129|Soviet Construction Vehicle|3000/1000/4|10|NAWEAP,NADEPT|required:-; forbidden:-|- / -|SMCV / SMCVICON|- / -|
|TNKD:6862|Tank Destroyer|900/400/5|2|GAWEAP,RADAR|required:Germans; forbidden:-|SABOT / -|TNKD / TNKDICON|- / -|
|TTNK:7015|Tesla Tank|1200/300/6|10|NAWEAP,NARADR|required:Russians; forbidden:-|TankBolt / -|TTNK / TTNKICON|- / -|
|CMIN:6028|Chrono Miner|1400/1000/4|1|GAWEAP,PROC|required:-; forbidden:-|none / -|CMIN / AHRVICON|- / -|
|SREF:7061|Prism Tank|1200/150/4|8|GAWEAP,GATECH|required:-; forbidden:-|- / -|SREF / SREFICON|- / -|
|HYD:5608|Sea Scorpion|600/400/8|6|NAYARD,NARADR|required:-; forbidden:-|FlakTrackGun / FlakWeapon|HYD / HOVRICON|- / -|
|MGTK:6614|Mirage Tank|1000/200/7|9|GAWEAP,GATECH|required:-; forbidden:-|MirageGun / -|RTNK / RTNKICON|- / -|
|FV:6666|IFV|600/200/10|3|GAWEAP|required:-; forbidden:-|HoverMissile / -|FV / FVICON|- / -|
|DTRUCK:5058|Demolitions Truck|1500/150/5|10|NAWEAP,RADAR|required:Africans; forbidden:-|Demobomb / none|TRUCKA / TRKAICON|- / -|

<details><summary>Additional exact data and weapon facts</summary>

```text
AMCV NAME=Allied Construction Vehicle TECHLEVEL=10 COST=3000 STRENGTH=1000 SPEED=4 SIGHT=6 PREREQUISITE=GAWEAP,GADEPT OWNER=British,French,Germans,Americans,Alliance MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1} DEPLOYSINTO=GACNST
HARV NAME=War Miner TECHLEVEL=1 COST=1400 STRENGTH=1000 SPEED=4 SIGHT=4 PREREQUISITE=NAWEAP,PROC OWNER=Russians,Confederation,Africans,Arabs PRIMARY=20mmRapid MOVEMENTZONE=Crusher LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1} DOCK=NAREFN,GAREFN
  PRIMARY:20MMRAPID DAMAGE=30 ROF=20 RANGE=5.5 SPEED=100 WARHEAD=HARVWH PROJECTILE=InvisibleLow REPORT=WarMinerAttack 
APOC NAME=Apocalypse TECHLEVEL=7 COST=1750 STRENGTH=800 SPEED=4 SIGHT=6 PREREQUISITE=NAWEAP,NATECH OWNER=Russians,Confederation,Africans,Arabs PRIMARY=120mmx SECONDARY=MammothTusk MOVEMENTZONE=Destroyer LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:120MMX DAMAGE=100 ROF=80 RANGE=5.75 SPEED=40 WARHEAD=ApocAP PROJECTILE=Cannon BURST=2 REPORT=ApocalypseAttackGround 
  SECONDARY:MAMMOTHTUSK DAMAGE=50 ROF=80 RANGE=8 SPEED=20 WARHEAD=HE PROJECTILE=AAHeatSeeker BURST=2 REPORT=ApocalypseAttackAir projectile.AA=yes projectile.AG=no projectile.ROT=80
HTNK NAME=Rhino Heavy Tank TECHLEVEL=2 COST=900 STRENGTH=400 SPEED=6 SIGHT=8 PREREQUISITE=NAWEAP OWNER=Russians,Confederation,Africans,Arabs PRIMARY=120mm MOVEMENTZONE=Destroyer LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:120MM DAMAGE=90 ROF=65 RANGE=5.75 SPEED=40 WARHEAD=AP PROJECTILE=Cannon REPORT=RhinoTankAttack 
SAPC NAME=Armored Transport TECHLEVEL=2 COST=900 STRENGTH=300 SPEED=6 SIGHT=6 PREREQUISITE=NAYARD OWNER=Russians,Confederation,Africans,Arabs MOVEMENTZONE=Amphibious LOCOMOTOR={4A582742-9839-11d1-B709-00A024DDAFD1} NAVAL=yes PASSENGERS=12
MTNK NAME=Grizzly Battle Tank TECHLEVEL=2 COST=700 STRENGTH=300 SPEED=7 SIGHT=8 PREREQUISITE=GAWEAP OWNER=British,French,Germans,Americans,Alliance PRIMARY=105mm MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:105MM DAMAGE=65 ROF=60 RANGE=5 SPEED=40 WARHEAD=AP PROJECTILE=Cannon REPORT=GrizzlyTankAttack 
CARRIER NAME=Aircraft Carrier TECHLEVEL=7 COST=2000 STRENGTH=800 SPEED=4 SIGHT=7 PREREQUISITE=GAYARD,TECH OWNER=British,French,Germans,Americans,Alliance PRIMARY=HornetLauncher MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:HORNETLAUNCHER DAMAGE=1 ROF=150 RANGE=25 SPEED=10 WARHEAD=Special PROJECTILE=Invisible 
V3 NAME=V3 Launcher TECHLEVEL=3 COST=800 STRENGTH=150 SPEED=4 SIGHT=7 PREREQUISITE=NAWEAP,NARADR OWNER=Russians,Confederation,Africans,Arabs PRIMARY=V3Launcher MOVEMENTZONE=Destroyer LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:V3LAUNCHER DAMAGE=1 ROF=150 RANGE=18 SPEED=10 WARHEAD=Special PROJECTILE=InvisibleHigh 
ZEP NAME=Kirov Airship TECHLEVEL=10 COST=2000 STRENGTH=2000 SPEED=5 SIGHT=8 PREREQUISITE=NAWEAP,NATECH OWNER=Russians,Confederation,Africans,Arabs PRIMARY=BlimpBomb MOVEMENTZONE=Fly LOCOMOTOR={92612C46-F71F-11d1-AC9F-006008055BB5}
  PRIMARY:BLIMPBOMB DAMAGE=250 ROF=50 RANGE=1.5 SPEED=20 WARHEAD=BlimpHE PROJECTILE=BlimpBombP BURST=1 REPORT=KirovAttack 
DRON NAME=Terror Drone TECHLEVEL=4 COST=500 STRENGTH=100 SPEED=10 SIGHT=4 PREREQUISITE=NAWEAP OWNER=Russians,Confederation,Africans,Arabs PRIMARY=DroneJump SECONDARY=VirtualScanner MOVEMENTZONE=Destroyer LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:DRONEJUMP DAMAGE=50 ROF=60 RANGE=1.83 SPEED=30 WARHEAD=Parasite PROJECTILE=JUMP REPORT=TerrorDroneAttack projectile.AA=no projectile.ROT=8
  SECONDARY:VIRTUALSCANNER DAMAGE=1 RANGE=5 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleAll projectile.AA=yes projectile.AG=yes
HTK NAME=Flak Track TECHLEVEL=3 COST=500 STRENGTH=180 SPEED=8 SIGHT=8 PREREQUISITE=NAWEAP OWNER=Russians,Confederation,Africans,Arabs PRIMARY=FlakTrackGun SECONDARY=FlakTrackAAGun MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1} PASSENGERS=5
  PRIMARY:FLAKTRACKGUN DAMAGE=25 ROF=40 RANGE=5 SPEED=50 WARHEAD=FlakTWH PROJECTILE=FlakTProj REPORT=FlakTrackAttackGround projectile.AA=no projectile.AG=yes projectile.INACCURATE=yes
  SECONDARY:FLAKTRACKAAGUN DAMAGE=35 ROF=25 RANGE=10 SPEED=100 WARHEAD=FlakWH PROJECTILE=FlakProj REPORT=FlakTrackAttackAir projectile.AA=yes projectile.AG=no projectile.INACCURATE=yes
DEST NAME=Destroyer TECHLEVEL=4 COST=1000 STRENGTH=600 SPEED=6 SIGHT=7 PREREQUISITE=GAYARD OWNER=British,French,Germans,Americans,Alliance PRIMARY=155mm SECONDARY=ASWLauncher MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:155MM DAMAGE=60 ROF=110 RANGE=8 SPEED=10 WARHEAD=ARTYHE PROJECTILE=Ballistic REPORT=DestroyerAttack 
  SECONDARY:ASWLAUNCHER DAMAGE=1 ROF=150 RANGE=-2 SPEED=10 WARHEAD=Special PROJECTILE=ASWVirt projectile.AA=no projectile.AG=no
SUB NAME=Typhoon Attack Sub TECHLEVEL=2 COST=1000 STRENGTH=600 SPEED=4 SIGHT=4 PREREQUISITE=NAYARD OWNER=Russians,Confederation,Africans,Arabs PRIMARY=SubTorpedo MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:SUBTORPEDO DAMAGE=100 ROF=120 RANGE=7 SPEED=25 WARHEAD=APSplash PROJECTILE=Torpedo REPORT=SubAttack projectile.AA=no projectile.AG=yes projectile.ROT=12
AEGIS NAME=Aegis Cruiser TECHLEVEL=7 COST=1200 STRENGTH=800 SPEED=4 SIGHT=8 PREREQUISITE=GAYARD,RADAR OWNER=British,French,Germans,Americans,Alliance PRIMARY=Medusa MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:MEDUSA DAMAGE=100 ROF=15 RANGE=12 SPEED=120 WARHEAD=SAMWH PROJECTILE=MedusaProjectile REPORT=AegisAttack projectile.AA=yes projectile.AG=no projectile.ROT=20
LCRF NAME=Landing Craft TECHLEVEL=4 COST=900 STRENGTH=300 SPEED=6 SIGHT=6 PREREQUISITE=GAYARD OWNER=British,French,Germans,Americans,Alliance MOVEMENTZONE=Amphibious LOCOMOTOR={4A582742-9839-11d1-B709-00A024DDAFD1} NAVAL=yes PASSENGERS=12
DRED NAME=Dreadnought TECHLEVEL=6 COST=2000 STRENGTH=800 SPEED=4 SIGHT=7 PREREQUISITE=NAYARD,NATECH OWNER=Russians,Confederation,Africans,Arabs PRIMARY=DredLauncher MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:DREDLAUNCHER DAMAGE=50 ROF=50 RANGE=25 SPEED=15 WARHEAD=Special PROJECTILE=InvisibleHigh BURST=2 
SHAD NAME=BlackHawk Transport TECHLEVEL=7 COST=1000 STRENGTH=175 SPEED=14 SIGHT=7 PREREQUISITE=GAWEAP OWNER=British,French,Germans,Americans,Alliance PRIMARY=BlackHawkCannon MOVEMENTZONE=Fly LOCOMOTOR={92612C46-F71F-11d1-AC9F-006008055BB5} PASSENGERS=5
  PRIMARY:BLACKHAWKCANNON DAMAGE=35 ROF=40 RANGE=6 SPEED=100 WARHEAD=SA PROJECTILE=QuadShell REPORT=BlackOpsAttack projectile.AA=no projectile.AG=yes
SQD NAME=Giant Squid TECHLEVEL=9 COST=1000 STRENGTH=200 SPEED=8 SIGHT=5 PREREQUISITE=NAYARD,NATECH OWNER=Russians,Confederation,Africans,Arabs PRIMARY=SquidGrab SECONDARY=SquidPunch MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:SQUIDGRAB DAMAGE=15 ROF=99 RANGE=1.5 SPEED=30 WARHEAD=ParasitePlus PROJECTILE=SQDJUMP REPORT=SquidAttack projectile.AA=no projectile.ROT=8
  SECONDARY:SQUIDPUNCH DAMAGE=50 ROF=32 RANGE=1.83 SPEED=30 WARHEAD=HE PROJECTILE=InvisibleLow REPORT=SquidAttackNonShip 
DLPH NAME=Dolphin TECHLEVEL=5 COST=500 STRENGTH=200 SPEED=8 SIGHT=4 PREREQUISITE=GAYARD,GATECH OWNER=British,French,Germans,Americans,Alliance PRIMARY=SonicZap MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:SONICZAP DAMAGE=4 ROF=120 RANGE=6 SPEED=100 WARHEAD=SonicWarhead PROJECTILE=Sonic REPORT=DolphinAttack 
SMCV NAME=Soviet Construction Vehicle TECHLEVEL=10 COST=3000 STRENGTH=1000 SPEED=4 SIGHT=6 PREREQUISITE=NAWEAP,NADEPT OWNER=Russians,Confederation,Africans,Arabs MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1} DEPLOYSINTO=NACNST
TNKD NAME=Tank Destroyer TECHLEVEL=2 COST=900 STRENGTH=400 SPEED=5 SIGHT=8 PREREQUISITE=GAWEAP,RADAR REQUIREDHOUSES=Germans OWNER=British,French,Germans,Americans,Alliance PRIMARY=SABOT MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:SABOT DAMAGE=150 ROF=70 RANGE=5 SPEED=60 WARHEAD=UltraAP PROJECTILE=Cannon REPORT=TankDestroyerAttack 
TTNK NAME=Tesla Tank TECHLEVEL=10 COST=1200 STRENGTH=300 SPEED=6 SIGHT=8 PREREQUISITE=NAWEAP,NARADR REQUIREDHOUSES=Russians OWNER=Russians,Confederation,Africans,Arabs PRIMARY=TankBolt MOVEMENTZONE=Destroyer LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:TANKBOLT DAMAGE=135 ROF=75 RANGE=4 SPEED=100 WARHEAD=Electric PROJECTILE=InvisibleLow REPORT=TeslaTankAttack 
CMIN NAME=Chrono Miner TECHLEVEL=1 COST=1400 STRENGTH=1000 SPEED=4 SIGHT=4 PREREQUISITE=GAWEAP,PROC OWNER=British,French,Germans,Americans,Alliance PRIMARY=none MOVEMENTZONE=Crusher LOCOMOTOR={4A582747-9839-11d1-B709-00A024DDAFD1} DOCK=NAREFN,GAREFN
SREF NAME=Prism Tank TECHLEVEL=8 COST=1200 STRENGTH=150 SPEED=4 SIGHT=8 PREREQUISITE=GAWEAP,GATECH OWNER=British,French,Germans,Americans,Alliance MOVEMENTZONE=Destroyer LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
HYD NAME=Sea Scorpion TECHLEVEL=6 COST=600 STRENGTH=400 SPEED=8 SIGHT=8 PREREQUISITE=NAYARD,NARADR OWNER=Russians,Confederation,Africans,Arabs PRIMARY=FlakTrackGun SECONDARY=FlakWeapon MOVEMENTZONE=Water LOCOMOTOR={2BEA74E1-7CCA-11d3-BE14-00104B62A16C} NAVAL=yes
  PRIMARY:FLAKTRACKGUN DAMAGE=25 ROF=40 RANGE=5 SPEED=50 WARHEAD=FlakTWH PROJECTILE=FlakTProj REPORT=FlakTrackAttackGround projectile.AA=no projectile.AG=yes projectile.INACCURATE=yes
  SECONDARY:FLAKWEAPON DAMAGE=40 ROF=20 RANGE=12 SPEED=100 WARHEAD=FlakWH PROJECTILE=FlakProj REPORT=FlakCannonAttack projectile.AA=yes projectile.AG=no projectile.INACCURATE=yes
MGTK NAME=Mirage Tank TECHLEVEL=9 COST=1000 STRENGTH=200 SPEED=7 SIGHT=9 PREREQUISITE=GAWEAP,GATECH OWNER=British,French,Germans,Americans,Alliance PRIMARY=MirageGun MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:MIRAGEGUN DAMAGE=100 ROF=70 RANGE=7 SPEED=100 WARHEAD=MirageWH PROJECTILE=InvisibleLow REPORT=MirageTankAttack 
FV NAME=IFV TECHLEVEL=3 COST=600 STRENGTH=200 SPEED=10 SIGHT=8 PREREQUISITE=GAWEAP OWNER=British,French,Germans,Americans,Alliance PRIMARY=HoverMissile MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1} PASSENGERS=1
  PRIMARY:HOVERMISSILE DAMAGE=25 ROF=50 RANGE=6 SPEED=40 WARHEAD=HE PROJECTILE=AAHeatSeeker2 BURST=2 REPORT=SeawolfAttack projectile.AA=yes projectile.AG=yes projectile.ROT=60
DTRUCK NAME=Demolitions Truck TECHLEVEL=10 COST=1500 STRENGTH=150 SPEED=5 SIGHT=5 PREREQUISITE=NAWEAP,RADAR REQUIREDHOUSES=Africans OWNER=Russians,Confederation,Africans,Arabs PRIMARY=Demobomb SECONDARY=none MOVEMENTZONE=Normal LOCOMOTOR={4A582741-9839-11d1-B709-00A024DDAFD1}
  PRIMARY:DEMOBOMB DAMAGE=300 ROF=80 RANGE=1 SPEED=35 WARHEAD=DemobombWH PROJECTILE=InvisibleLow REPORT=DemoTruckDie 
```

</details>

## AIRCRAFTTYPES ordinary/stolen-tech candidates (2)

|Rules ID : line|Name|Cost / HP / speed|Tech|Prerequisites|Country restriction|Primary / secondary|Art image / cameo|Foundation / producer|
|---|---|---|---|---|---|---|---|---|
|ORCA:7785|Intruder|1200/150/14|3|RADAR|required:-; forbidden:Alliance|Maverick / -|FALC / FALCICON|- / -|
|BEAG:8328|Black Eagle|1200/200/14|3|RADAR|required:Alliance; forbidden:-|Maverick2 / -|BEAG / BEAGICON|- / -|

<details><summary>Additional exact data and weapon facts</summary>

```text
ORCA NAME=Intruder TECHLEVEL=3 COST=1200 STRENGTH=150 SPEED=14 SIGHT=8 PREREQUISITE=RADAR FORBIDDENHOUSES=Alliance OWNER=British,French,Germans,Americans PRIMARY=Maverick MOVEMENTZONE=Fly LOCOMOTOR={4A582746-9839-11d1-B709-00A024DDAFD1} AMMO=1 AIRPORTBOUND=yes DOCK=GAAIRC,AMRADR
  PRIMARY:MAVERICK DAMAGE=150 ROF=10 RANGE=6 SPEED=70 WARHEAD=ORCAAP PROJECTILE=AirToGroundMissile BURST=2 REPORT=IntruderAttack projectile.AA=no projectile.AG=yes projectile.ROT=100
BEAG NAME=Black Eagle TECHLEVEL=3 COST=1200 STRENGTH=200 SPEED=14 SIGHT=8 PREREQUISITE=RADAR REQUIREDHOUSES=Alliance OWNER=British,French,Germans,Americans,Alliance PRIMARY=Maverick2 MOVEMENTZONE=Fly LOCOMOTOR={4A582746-9839-11d1-B709-00A024DDAFD1} AMMO=1 AIRPORTBOUND=yes DOCK=GAAIRC,AMRADR
  PRIMARY:MAVERICK2 DAMAGE=200 ROF=10 RANGE=6 SPEED=70 WARHEAD=ORCAAP PROJECTILE=AirToGroundMissile BURST=2 REPORT=BlackEagleAttack projectile.AA=no projectile.AG=yes projectile.ROT=100
```

</details>

## BUILDINGTYPES ordinary/stolen-tech candidates (36)

|Rules ID : line|Name|Cost / HP / speed|Tech|Prerequisites|Country restriction|Primary / secondary|Art image / cameo|Foundation / producer|
|---|---|---|---|---|---|---|---|---|
|GAPOWR:8892|Power Plant|800/750/-|1|GACNST|required:-; forbidden:-|- / -|GAPOWR / POWRICON|2x2 / -|
|GAREFN:8554|Allied Ore Refinery|2000/1000/-|1|POWER,GACNST|required:-; forbidden:-|- / -|GAREFN / REFICON|4x3 / -|
|GAPILE:9046|Allied Barracks|500/500/-|2|POWER,GACNST|required:-; forbidden:-|- / -|GAPILE / BRRKICON|3x2 / InfantryType|
|GADEPT:9079|Allied Service Depot|800/1200/-|6|GAWEAP,GACNST|required:-; forbidden:-|- / -|GADEPT / FIXICON|3x3 / -|
|GATECH:8388|Allied Battle Lab|2000/500/-|8|GAWEAP,RADAR,GACNST|required:-; forbidden:-|- / -|GATECH / TECHICON|3x2 / -|
|GAWEAP:8418|Allied War Factory|2000/1000/-|2|PROC,GAPILE,GACNST|required:-; forbidden:-|- / -|GAWEAP / gwepicon|5x3 / UnitType|
|NAPOWR:8922|Tesla Reactor|600/750/-|1|NACNST|required:-; forbidden:-|- / -|NAPOWR / NPWRICON|3x2 / -|
|NATECH:8951|Soviet Battle Lab|2000/500/-|7|NAWEAP,RADAR,NACNST|required:-; forbidden:-|- / -|NATECH / NTCHICON|3x3 / -|
|NAHAND:8981|Soviet Barracks|500/500/-|2|POWER,NACNST|required:-; forbidden:-|- / -|NAHAND / HANDICON|2x2 / InfantryType|
|GAWALL:9341|Allied Wall|100/300/-|1|GAPILE|required:-; forbidden:-|- / -|GAWALL / WALLICON|1x1 / -|
|NARADR:10180|Radar Tower|1000/1000/-|3|NAREFN,NACNST|required:-; forbidden:-|- / -|NARADR / NRADICON|2x2 / -|
|NAWEAP:8454|Soviet War Factory|2000/1000/-|2|PROC,NAHAND,NACNST|required:-; forbidden:-|- / -|NAWEAP / NWEPICON|5x3 / UnitType|
|NAREFN:8597|Soviet Ore Refinery|2000/1000/-|1|POWER,NACNST|required:-; forbidden:-|- / -|NAREFN / NREFICON|4x3 / -|
|NAWALL:9422|Soviet Wall|100/300/-|1|NAHAND|required:-; forbidden:-|- / -|NAWALL / NWALICON|1x1 / -|
|NAPSIS:10210|Psychic Sensor|1000/750/-|10|NATECH,NACNST|required:-; forbidden:-|- / -|NAPSIS / PSISICON|2x2 / -|
|NALASR:10367|Sentry Gun|500/400/-|1|BARRACKS,NACNST|required:-; forbidden:-|Vulcan / -|NALASR / PLTICON|1x1 / -|
|NASAM:10443|Patriot Missile|1000/900/-|4|BARRACKS,GACNST|required:-; forbidden:-|RedEye2 / -|NASAM / SAMICON|1x1 / -|
|GAYARD:8676|Allied Shipyard|1000/1500/-|4|PROC,POWER,GACNST|required:-; forbidden:-|- / -|GAYARD / ayaricon|4x4 / UnitType|
|NAIRON:10049|Iron Curtain Device|2500/750/-|10|NATECH,NACNST|required:-; forbidden:-|- / -|NAIRON / IRONICON|3x3 / -|
|NADEPT:10517|Soviet Service Depot|800/1200/-|6|NAWEAP,NACNST|required:-; forbidden:-|- / -|NADEPT / RFIXICON|4x3 / -|
|GACSPH:10118|Chrono Sphere|2500/750/-|10|GATECH,GACNST|required:-; forbidden:-|- / -|GACSPH / csphicon|4x3 / -|
|GAWEAT:10083|Weather Controller|5000/1000/-|10|GATECH,GACNST|required:-; forbidden:-|- / -|GAWETH / wethicon|3x3 / -|
|TESLA:10246|Tesla Coil|1500/600/-|5|POWER,RADAR,NACNST|required:-; forbidden:-|CoilBolt / OPCoilBolt|NATSLA / TSLAICON|1x1 / -|
|NAMISL:10327|Nuclear Missile Silo|5000/1000/-|10|NATECH,NACNST|required:-; forbidden:-|- / -|NAMISL / MSSLICON|3x3 / -|
|ATESLA:10287|Prism Cannon|1500/600/-|6|POWER,RADAR,GACNST|required:-; forbidden:-|PrismShot / PrismSupport|GAPRIS / prisicon|1x1 / -|
|NAYARD:8715|Soviet Shipyard|1000/1500/-|2|PROC,POWER,NACNST|required:-; forbidden:-|- / -|NAYARD / YARDICON|4x4 / UnitType|
|GASPYSAT:8825|SpySat Uplink|1500/1000/-|9|GATECH,GACNST|required:-; forbidden:-|- / -|GASPST / asaticon|2x2 / -|
|GAGAP:8859|Gap Generator|1000/600/-|7|GATECH,GACNST|required:-; forbidden:-|- / -|GAGAP / Gapicon|1x1 / -|
|GTGCAN:15945|Grand Cannon|2000/900/-|7|RADAR,GACNST|required:French; forbidden:-|GrandCannonWeapon / -|GTGCAN / GCANICON|2x2 / -|
|NANRCT:15990|Nuclear Reactor|1000/1000/-|9|NATECH,NACNST|required:-; forbidden:-|- / -|NANRCT / NRCTICON|4x4 / -|
|GAPILL:10406|Pill Box|500/400/-|1|BARRACKS,GACNST|required:-; forbidden:-|Vulcan2 / -|GAPILL / pillicon|1x1 / -|
|NAFLAK:16019|Flak Cannon|1000/900/-|4|BARRACKS,NACNST|required:-; forbidden:-|FlakWeapon / -|NAFLAK / FLAKICON|1x1 / -|
|NACLON:9013|Cloning Vats|2500/1000/-|9|NATECH,NACNST|required:-; forbidden:-|- / -|NACLON / CLONICON|2x2 / -|
|GAOREP:8638|Allied Ore Processor|2500/900/-|10|GATECH,PROC,GACNST|required:-; forbidden:-|- / -|GAOREP / gorep|3x3 / -|
|GAAIRC:8754|Airforce Command Headquarters|1000/600/-|3|GAREFN,GACNST|required:-; forbidden:Americans|- / -|GAAIRC / HELIICON|3x2 / AircraftType|
|AMRADR:8789|American Airforce Command Headquarters|1000/600/-|3|GAREFN,GACNST|required:Americans; forbidden:-|- / -|GAAIRC / HELIICON|3x2 / AircraftType|

<details><summary>Additional exact data and weapon facts</summary>

```text
GAPOWR NAME=Power Plant TECHLEVEL=1 COST=800 STRENGTH=750 SIGHT=4 PREREQUISITE=GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=200
GAREFN NAME=Allied Ore Refinery TECHLEVEL=1 COST=2000 STRENGTH=1000 SIGHT=6 PREREQUISITE=POWER,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-50 FREEUNIT=CMIN
GAPILE NAME=Allied Barracks TECHLEVEL=2 COST=500 STRENGTH=500 SIGHT=5 PREREQUISITE=POWER,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs FACTORY=InfantryType POWER=-10
GADEPT NAME=Allied Service Depot TECHLEVEL=6 COST=800 STRENGTH=1200 SIGHT=5 PREREQUISITE=GAWEAP,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-25
GATECH NAME=Allied Battle Lab TECHLEVEL=8 COST=2000 STRENGTH=500 SIGHT=6 PREREQUISITE=GAWEAP,RADAR,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-100
GAWEAP NAME=Allied War Factory TECHLEVEL=2 COST=2000 STRENGTH=1000 SIGHT=4 PREREQUISITE=PROC,GAPILE,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs FACTORY=UnitType POWER=-25
NAPOWR NAME=Tesla Reactor TECHLEVEL=1 COST=600 STRENGTH=750 SIGHT=4 PREREQUISITE=NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=150
NATECH NAME=Soviet Battle Lab TECHLEVEL=7 COST=2000 STRENGTH=500 SIGHT=6 PREREQUISITE=NAWEAP,RADAR,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-100
NAHAND NAME=Soviet Barracks TECHLEVEL=2 COST=500 STRENGTH=500 SIGHT=6 PREREQUISITE=POWER,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs FACTORY=InfantryType POWER=-10
GAWALL NAME=Allied Wall TECHLEVEL=1 COST=100 STRENGTH=300 SIGHT=1 PREREQUISITE=GAPILE OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs
NARADR NAME=Radar Tower TECHLEVEL=3 COST=1000 STRENGTH=1000 SIGHT=10 PREREQUISITE=NAREFN,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-50
NAWEAP NAME=Soviet War Factory TECHLEVEL=2 COST=2000 STRENGTH=1000 SIGHT=4 PREREQUISITE=PROC,NAHAND,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs FACTORY=UnitType POWER=-25
NAREFN NAME=Soviet Ore Refinery TECHLEVEL=1 COST=2000 STRENGTH=1000 SIGHT=6 PREREQUISITE=POWER,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-50 FREEUNIT=HARV
NAWALL NAME=Soviet Wall TECHLEVEL=1 COST=100 STRENGTH=300 SIGHT=1 PREREQUISITE=NAHAND OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs
NAPSIS NAME=Psychic Sensor TECHLEVEL=10 COST=1000 STRENGTH=750 SIGHT=10 PREREQUISITE=NATECH,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-50
NALASR NAME=Sentry Gun TECHLEVEL=1 COST=500 STRENGTH=400 SIGHT=7 PREREQUISITE=BARRACKS,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs PRIMARY=Vulcan POWER=0 ISBASEDEFENSE=yes
  PRIMARY:VULCAN DAMAGE=50 ROF=26 RANGE=5.5 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleLow REPORT=SentryGunAttack 
NASAM NAME=Patriot Missile TECHLEVEL=4 COST=1000 STRENGTH=900 SIGHT=10 PREREQUISITE=BARRACKS,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs PRIMARY=RedEye2 POWER=-50 ISBASEDEFENSE=yes
  PRIMARY:REDEYE2 DAMAGE=75 ROF=55 RANGE=12 SPEED=100 WARHEAD=SAMWH PROJECTILE=AAHeatSeeker REPORT=PatriotAttack projectile.AA=yes projectile.AG=no projectile.ROT=80
GAYARD NAME=Allied Shipyard TECHLEVEL=4 COST=1000 STRENGTH=1500 SIGHT=10 PREREQUISITE=PROC,POWER,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs NAVAL=yes FACTORY=UnitType POWER=-25 WATERBOUND=yes
NAIRON NAME=Iron Curtain Device TECHLEVEL=10 COST=2500 STRENGTH=750 SIGHT=4 PREREQUISITE=NATECH,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-200 BUILDLIMIT=1 SUPERWEAPON=IronCurtainSpecial
NADEPT NAME=Soviet Service Depot TECHLEVEL=6 COST=800 STRENGTH=1200 SIGHT=5 PREREQUISITE=NAWEAP,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-20
GACSPH NAME=Chrono Sphere TECHLEVEL=10 COST=2500 STRENGTH=750 SIGHT=5 PREREQUISITE=GATECH,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-200 BUILDLIMIT=1 SUPERWEAPON=ChronoSphereSpecial
GAWEAT NAME=Weather Controller TECHLEVEL=10 COST=5000 STRENGTH=1000 SIGHT=5 PREREQUISITE=GATECH,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-200 BUILDLIMIT=1 SUPERWEAPON=LightningStormSpecial
TESLA NAME=Tesla Coil TECHLEVEL=5 COST=1500 STRENGTH=600 SIGHT=8 PREREQUISITE=POWER,RADAR,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs PRIMARY=CoilBolt SECONDARY=OPCoilBolt POWER=-75 ISBASEDEFENSE=yes
  PRIMARY:COILBOLT DAMAGE=200 ROF=120 RANGE=7 SPEED=100 WARHEAD=Electric PROJECTILE=Invisible REPORT=TeslaCoilAttack 
  SECONDARY:OPCOILBOLT DAMAGE=300 ROF=100 RANGE=8 SPEED=100 WARHEAD=Electric PROJECTILE=InvisibleHigh REPORT=TeslaCoilSuper 
NAMISL NAME=Nuclear Missile Silo TECHLEVEL=10 COST=5000 STRENGTH=1000 SIGHT=4 PREREQUISITE=NATECH,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-200 BUILDLIMIT=1 SUPERWEAPON=MultiSpecial
ATESLA NAME=Prism Cannon TECHLEVEL=6 COST=1500 STRENGTH=600 SIGHT=8 PREREQUISITE=POWER,RADAR,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs PRIMARY=PrismShot SECONDARY=PrismSupport POWER=-75 ISBASEDEFENSE=yes
  PRIMARY:PRISMSHOT DAMAGE=120 ROF=60 RANGE=8 SPEED=100 WARHEAD=PrismWarhead PROJECTILE=InvisibleHigh REPORT=PrismTowerAttack 
  SECONDARY:PRISMSUPPORT DAMAGE=200 ROF=60 RANGE=8 SPEED=100 WARHEAD=DummyWarhead PROJECTILE=InvisibleHigh 
NAYARD NAME=Soviet Shipyard TECHLEVEL=2 COST=1000 STRENGTH=1500 SIGHT=10 PREREQUISITE=PROC,POWER,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs NAVAL=yes FACTORY=UnitType POWER=-20 WATERBOUND=yes
GASPYSAT NAME=SpySat Uplink TECHLEVEL=9 COST=1500 STRENGTH=1000 SIGHT=5 PREREQUISITE=GATECH,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-100
GAGAP NAME=Gap Generator TECHLEVEL=7 COST=1000 STRENGTH=600 SIGHT=5 PREREQUISITE=GATECH,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-100
GTGCAN NAME=Grand Cannon TECHLEVEL=7 COST=2000 STRENGTH=900 SIGHT=10 PREREQUISITE=RADAR,GACNST REQUIREDHOUSES=French OWNER=British,French,Germans,Americans,Alliance PRIMARY=GrandCannonWeapon POWER=-100
  PRIMARY:GRANDCANNONWEAPON DAMAGE=150 ROF=120 RANGE=15 SPEED=1 WARHEAD=GrandCannonWH PROJECTILE=GrandCannonBall REPORT=GrandCannonAttack 
NANRCT NAME=Nuclear Reactor TECHLEVEL=9 COST=1000 STRENGTH=1000 SIGHT=5 PREREQUISITE=NATECH,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=2000
GAPILL NAME=Pill Box TECHLEVEL=1 COST=500 STRENGTH=400 SIGHT=7 PREREQUISITE=BARRACKS,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs PRIMARY=Vulcan2 POWER=0 ISBASEDEFENSE=yes
  PRIMARY:VULCAN2 DAMAGE=50 ROF=26 RANGE=5.5 SPEED=100 WARHEAD=SA PROJECTILE=InvisibleLow REPORT=PillboxAttack 
NAFLAK NAME=Flak Cannon TECHLEVEL=4 COST=1000 STRENGTH=900 SIGHT=5 PREREQUISITE=BARRACKS,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs PRIMARY=FlakWeapon POWER=-50 ISBASEDEFENSE=yes
  PRIMARY:FLAKWEAPON DAMAGE=40 ROF=20 RANGE=12 SPEED=100 WARHEAD=FlakWH PROJECTILE=FlakProj REPORT=FlakCannonAttack projectile.AA=yes projectile.AG=no projectile.INACCURATE=yes
NACLON NAME=Cloning Vats TECHLEVEL=9 COST=2500 STRENGTH=1000 SIGHT=5 PREREQUISITE=NATECH,NACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-200 BUILDLIMIT=1
GAOREP NAME=Allied Ore Processor TECHLEVEL=10 COST=2500 STRENGTH=900 SIGHT=5 PREREQUISITE=GATECH,PROC,GACNST OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs POWER=-200 BUILDLIMIT=1
GAAIRC NAME=Airforce Command Headquarters TECHLEVEL=3 COST=1000 STRENGTH=600 SIGHT=5 PREREQUISITE=GAREFN,GACNST FORBIDDENHOUSES=Americans OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs FACTORY=AircraftType POWER=-50
AMRADR NAME=American Airforce Command Headquarters TECHLEVEL=3 COST=1000 STRENGTH=600 SIGHT=5 PREREQUISITE=GAREFN,GACNST REQUIREDHOUSES=Americans OWNER=British,French,Germans,Americans,Alliance,Russians,Confederation,Africans,Arabs FACTORY=AircraftType POWER=-50 SUPERWEAPON=AmericanParaDropSpecial
```

</details>

## Exclusions and reconstruction rules

- GHOST (SEAL) has TechLevel=-1 and is campaign-only in vanilla. It must not appear as ordinary skirmish production. Tanya TANY is ordinary Allied high-tier infantry.
- CCOMAND / PTROOP / CIVAN / YURIPR need stolen-tech flags; do not make them ordinary buildables merely because TechLevel is positive.
- Production and movement are independent: JUMPJET is Air + Infantry/Barracks; SHAD and ZEP are Air + Vehicle/WarFactory; ORCA and BEAG are Air + Aircraft/Allied Airforce Command.
- Shared logical building enum values retain exact faction/country variants, including AMRADR for US Airforce Command. Generic prerequisite groups are OR groups, while the comma-separated unit/building prerequisite list is AND.
- GRND and transport amphibious locomotion needs explicit capability flags; Water domain alone must not strand a landing craft on a beach.
- Rules costs, strengths, weapon numeric data and art foundations are source data. Simulation conversion of original speed/ROF/build-time clocks must be documented; original time units are not equal to this project's 20 Hz ticks.

## Implemented catalog and API contract

The implementation contains 47 logical unit types, 27 logical building types and nine countries. Country variants preserve all 89 relevant original INI entries: 51 unit prototypes and 38 building prototypes, including both deployed Construction Yards. Existing UnitType ordinals 0–13, BuildingType 0–10, Country 0–3 and CommandType 0–13 are preserved. New commands are Deploy=14, EnterTransport=15, Unload=16, Special=17 and SuperWeapon=18.

The supplied original-data snapshot is the numeric authority. Its retained header, hashes and external vanilla roster checks establish the data used here; this report does not claim independent cryptographic verification against a retail 1.006 installation. Vanilla 1.006 remains the intended target; no YR faction or ordinary YR-only units are added.

- `UnitDef` exposes Domain, Category, Producer, Prerequisites, RulesId, ArtId, Cameo, Buildable, Amphibious, Ammo, Passengers, TechLevel, OriginalSpeed, PrimaryWeapon, SecondaryWeapon, stolen-tech requirements, MaxInstances, ForbiddenCountries, TargetsAir/Ground/Water, AirportBound, Submersible, CanDeploy, AllowedToStart and raw Armor. Shared Engineer, Dog, MCV and amphibious transport variants use `Rules.Unit(type, originCountry)`.
- `BuildingDef` preserves faction-specific cost, strength, power and original art foundation. It adds Produces, IsDefense (original BuildCat=Combat), WaterBound, Prerequisites, RulesId/ArtId/Cameo, Buildable, TechLevel, AirCapacity, PrimaryWeapon/SecondaryWeapon, MaxInstances, SuperWeapon and raw Armor. Only AircraftType-producing airfields expose AirCapacity; refinery, naval and service docks are not aircraft slots.
- `WeaponDef` has Id, Damage, RangeFixed, Reload, Burst, AntiAir, AntiGround, AntiSubmarine, Underwater, Projectile and Warhead. Prism Tank SREF uses `Weapon1=Comet`, not a Primary key. Scanner/disguise/defusal utility weapons do not grant a unit offensive anti-air capability. LandTargeting=1 and NavalTargeting=6 restrict unit target domains; original underwater units remain submersible. Flag references: https://modenc.renegadeprojects.com/LandTargeting and https://modenc.renegadeprojects.com/NavalTargeting .
- `PrerequisiteDef(BuildingType Building, Faction? Faction=null)` arrays are AND lists. A null faction denotes the original generic group; a concrete faction denotes that original faction prototype. The POWER group also accepts a Nuclear Reactor.
- `Rules.Production`, `ProducerForUnit` (also `Producer`), `UnitRequirements`, `BuildingRequirements`, `Prerequisites` overloads and `IsStolenUnit` are shared by UI and simulation. `World.CanSeeUnit` checks national/stolen-tech visibility. `CanProduceUnit` checks technology and a completed compatible factory, including captured foreign factories. It excludes funds, queue capacity and total entity limit; `CanTrainUnit(player,type,producer)` adds queue/entity/airport admission limits. Task-42 removed the full-cost credit gate so an affordable first production installment can start a job. Production processing must not reapply a queue-admission limit to a job already counted in that queue.
- Building preparation retains one ready item and rapid 20-tick physical erection. Task-42 changed payment to progressive spending; ready requires the full cost to have been paid, and cancellation returns only actual payment. Preparation/ready/refund use the player's country variant, including Soviet Power Plant 600 versus Allied 800. `CanUseBuildingFootprint` is the shared integration hook for the Lead's land/water foundation implementation. BuildNearAlly controls allied expansion access.
- MatchSettings adds MapId, ClassicStart, StartingUnits, ShortGame, MCVRepacks, Crates, SuperWeapons, BuildNearAlly and GameSpeed. StartingUnits is 0–10 (original default 10); original credits are 5000–10000, default 10000, increment 100. Source defaults ShortGame/MCVRedeploys/Crates are yes. Practice uses classic MCV starts; LegacyFixture explicitly preserves the old prepared-base checks. GameSpeed currently accepts only 0 and simulation remains 20 Hz.
- Rules, PlayerState, Unit and Building are partial. Unit adds OriginCountry, Ammo, AircraftDockId, Passengers, TransportId and Deployed; combat ability state is supplied by the combat owner. PlayerState provides StolenAlliedTech/StolenSovietTech.

## Clock conversion and verification limits

The first reconstruction uses a documented fastest-speed assumption: original ROF frames map to `ceil(ROF * 20 / 60)` ticks. Preparation time maps original BuildSpeed=.7 minutes per 1000 credits at a 15-frame nominal clock to a 60-frame fastest clock, producing `ceil(cost * .21)` ticks. The physical erection remains the existing 20-tick phase. These are explicit reconstruction conversions, rather than claims of recovered original engine timing.

Movement currently retains the project calibration `24 * OriginalSpeed`; exact original movement timing has not been established. OriginalSpeed and full fixed-point weapon ranges are retained for later calibration. Movement, combat, air reload/transport/MCV mechanics, map loading, renderer assets and replay integration are owned by the corresponding Lead/team components; this catalog alone does not claim those behaviors are already complete.

Static data validation passed: all 89 eligible original prototypes are represented, enum/canonical array ordering matches, old ordinals are preserved, all records have cameos, the two shipyards are WaterBound, the two Allied airfield prototypes each have four aircraft slots, and submarine/dolphin/squid land-target restrictions are present. No simulation build, runtime or native launch was performed by the catalog member.

## Economy integration acceptance (task-25)

After the economy source was frozen, the Lead centrally rebuilt the simulation and actually executed `--classic-economy`. The Lead reported **8/8 checks passed, 321 World steps**, within the check suite's 512-step budget. The implementation member did not start a separate build or native process. The tested checks are defined in [ClassicEconomyChecks.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Tests/ClassicEconomyChecks.cs).

| Executed check | Verified behavior |
| --- | --- |
| Aircraft reservations and completion | Four queued aircraft reserve all four airport slots; public admission rejects a fifth. Existing paid jobs finish without counting themselves as new admissions; releasing a slot resumes the ready queue. Home airport, ammunition and origin remain attached. |
| Infantry, vehicle and naval production | Rocketeers use Barracks production, Nighthawks use War Factory production, ships use water exits, and a captured Soviet factory preserves the Soviet unit prototype. |
| Paused production and BuildLimit | Blackout, temporal erasure and lost radar prerequisites preserve ready jobs; restoring operation resumes them. A queued Yuri Prime is not blocked by reapplying its own admission limit. |
| Refinery and Cloning Vats output | Infantry production delivers one free clone. A captured Soviet refinery grants a War Miner with its original Soviet prototype to its new owner. |
| Ore, gems and Ore Purifier income | Chrono Miner and War Miner holds contain 20/40 bales. Ore pays 25 per bale and gems 50: full loads pay 500/1000 for ore and 1000/2000 for gems. Deposits clear both cargo fields and apply the powered purifier's 25% bonus; blackout disables the bonus. |
| Service Depot | A service step restores 8 health, charges the vehicle owner, stops during blackout and removes an attached Terror Drone even from a full-health vehicle. |
| Crate rewards | Credits, repair, map reveal and veterancy apply to eligible recipients; a crate pays once. Aircraft and transport passengers cannot collect exterior crates. |
| Crate scheduling | Equal seeds produce equal crate state. Generation stays bounded, and disabling crates prevents rewards and random-state generation. |

The final run retained the original strict airport and deposit assertions. Airport capacity is checked through the public `CanTrainUnit` admission API. Returning miners now use a stable unloading dock; choosing a vacant production exit no longer displaces the dock when the arriving miner occupies it. Chrono return destinations are checked separately for a free landing cell. The original deposit check requires the expected credits and zero remaining cargo in the same simulation step.

The economy implementation is in [World.Economy.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Economy.cs) and [World.ClassicEconomy.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicEconomy.cs). The accepted task-25 source was frozen at that point; task-42 later replaced its production payment/refund behavior as documented below. The service area currently uses the clear perimeter within one cell of the solid depot foundation; drive-on depot foundations remain a separate map integration detail. The crate scheduler implements the four tested rewards with their original relative shares, a bounded reconstructed money range and a conservative population cap; this result does not establish every original crate type or bit-exact original timing.

## Connected naval route repair (task-35)

The Lead's actual naval full-playthrough failure had a Destroyer at `(120,140)` unable to reach `(92,100)` on original `mp01t4`, with shipyards at `(61,76)` and `(116,137)`. A short map-only CPU probe loaded that original map and captured building foundations. Independent cardinal connectivity put both endpoints in the same 7,267-cell water component. The old production pathfinder returned no route: its 5,000 heap-pop limit included 1,678 stale entries and only 3,322 valid expansions. Completing the same A* search required 5,840 valid expansions / 8,446 pops and found 222 cells with cost 2,588. Excluding stale pops alone would still fail at a fixed 5,000 valid expansions.

The repaired [GridPathfinder](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/BattleMap.cs#L132) finalizes each cell once and bounds valid expansions by map cell count. Its octile heuristic is consistent with 10/14 movement costs plus nonnegative occupancy penalties. Neighbor ordering and score/cell tie breaks remain deterministic. At most eight outgoing edges per finalized cell can enqueue work, so an unreachable query also terminates. The actual repaired route matched the complete-reference route cell by cell. Reverse, repeated deterministic, occupancy-penalty, disconnected-water and land checks passed in [NavalNavigationChecks](/storage/Users/currentUser/dev/redalert-godot-csharp/Tools/NavalNavigationChecks/README.md). This CPU evidence is separate from the Lead's native gameplay rerun.

## Progressive production payment and manual pause (task-42)

The original manual printed page 17, BUILDING UNITS AND STRUCTURES / BUILD QUEUING, says credits are consumed while an item is built; insufficient money suspends progress until more money arrives. Right-clicking an in-progress portrait suspends it, left-clicking resumes it, and a second right click cancels and returns money already spent. Source: [original manual text](https://archive.org/download/Command_Conquer_Red_Alert_2/Command__Conquer_Red_Alert_2_djvu.txt), as read and cross-checked by the audio/research member. The manual's queue example explicitly permits simultaneous factory tank and shipyard ship production. It does not specify a separate active-plus-pending portrait priority or a ready-building right-click rule.

Task-42 replaces upfront full-price queue admission with an integer payment ledger. Every eligible production tick computes cumulative required payment as `ceil(Cost * nextProgress / BuildTicks)`, consumes only the unpaid difference, then grants progress. Insufficient cash changes neither ledger nor progress and is separate from a manual pause flag. This per-tick rounding is a documented reconstruction policy; the manual does not establish that exact deduction cadence. The prepared-building job uses the player's country; a producer uses its fixed foundation country. Each producer head pays independently; followers and free cloning jobs are unpaid.

`PlayerState.BuildingProductionPaid/BuildingProductionPaused` and `Building.ProductionPaid/ProductionPaused` are included in the deterministic state hash. Cancellation, selling and engineer capture return only the head's actual payment. Capture refunds the old owner before changing ownership, preserves foundation country, and clears queued work; surrender clears production state without refund. A completed building remains available for legal placement only after full payment. The existing 20-tick physical erection stage is unchanged.

The terrain member appended four command types without changing old ordinals: `PauseBuilding`, `ResumeBuilding`, `PauseQueue`, `ResumeQueue`. Existing cancel commands remain explicit cancellation. `CancelQueue.Target=0` retains the old tail behavior; a positive target selects the one-based queue slot. The sidebar pauses the active head on its first right click, resumes it on a left click, and cancels/refunds it on a second right click. A pending follower can be removed without returning unspent credits; a ready building retains its existing right-click cancellation behavior. These priority choices are implementation policy, not additional claims from the manual.

The existing NAHAND structural-exit classification is retained. A fully paid ready infantry job may refund before entity/clone/output capacity gates because refund creates neither an original nor a clone. Unit congestion continues waiting with its paid ledger. Successful original infantry can still generate the existing free clone, without charging twice.

Core implementation is in [World.Construction.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Construction.cs), [World.Economy.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Economy.cs), and [World.Commands.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Commands.cs). The actual portrait input code is [Main.ClassicProduction.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.ClassicProduction.cs), with the prepared-building left-click resume branch in [Main.Ui.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.Ui.cs#L272).

[ProductionFundingChecks](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.ProductionFundingChecks.cs) supplies two separate validation paths. `RunProductionFundingChecks()` creates bounded isolated simulation fixtures for payment, prepared miner unloading and cash auto-resume, manual pause/cancel, unpaid followers, independent producers, Soviet 600-price ready jobs, capture, cloning, ready structural refunds under a full clone backlog, state hash and surrender. `RunProductionFundingUiChecks()` starts an original-map MCV-only match with 12 credits and sends real viewport sidebar clicks through ordinary commands and Main stepping; it contains no direct entity spawn or production/credit shortcut. It checks the enabled low-credit portrait, cash waiting, right/left/right/right behavior, paid-only refund back to 12 credits, command acceptance and an actual viewport screenshot. The implementation member froze source for the Lead's central alpha12 build and native execution. That complete build passed with zero warnings and zero errors. The Lead then ran the real compiled helpers in Native: **funding 8/8 checks passed, 244 World steps, errors empty**, and **production exits 5/5 checks passed, 11 World steps, errors empty**. The result is saved in [production-core-alpha12.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/production-core-alpha12.json). Both results explicitly identify themselves as isolated fixtures; they do not establish an ordinary match or real UI input result. The Lead also completed the separate real Native sidebar input/GPU run: **PASS in 1.44 seconds, 6 accepted commands, 0 rejected commands, 10 credits paid before cancellation, final credits restored to 12, and no direct entity spawns**. The check sent actual low-credit portrait clicks for queue, pause, resume and cancellation through the ordinary command/step path. Its [actual viewport capture](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/production-funding-20261022-162835/sidebar-funding.png) was visually inspected by both the Lead and implementation member; it shows the Soviet construction yard, the 12-credit balance and the enabled 600-credit power-plant portrait. Task-42 is accepted for this bounded payment/control scope. This result does not replace the separate normal-start land/air/naval full-playthrough acceptance.

