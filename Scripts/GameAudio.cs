using Godot;
using RedAlert.Simulation;
using SimWorld = RedAlert.Simulation.World;

namespace RedAlert;

/// <summary>
/// Client-only mixer. All clips and native players are allocated once, outside simulation ticks.
/// Main preserves the audio owner when rebuilding its UI; shutdown releases native ownership.
/// All methods must be called from Godot's main thread.
/// </summary>
public sealed partial class GameAudio : IDisposable
{
    const string AssetRoot = "res://Assets/Generated/Audio/";
    const int EffectsPoolSize = 12;
    const int VoicePoolSize = 3;
    /// <summary>Only these sourced originals are active; focused diagnostics can decode these paths without playback.</summary>
    public static IReadOnlyDictionary<string, string> ActiveFiles { get; } = WithClassicFiles(new Dictionary<string, string>
    {
        ["menu_music"] = AssetRoot + "Original/grinder.mp3",
        ["battle_music"] = AssetRoot + "Original/hm2.mp3",
        ["ui_click"] = AssetRoot + "Original/umenucl1.wav",
        ["select"] = AssetRoot + "Original/igisea.wav",
        ["order"] = AssetRoot + "Original/igimoa.wav",
        ["select_soviet"] = AssetRoot + "Original/iconsea.wav",
        ["order_soviet"] = AssetRoot + "Original/iconmoa.wav",
        ["select_vehicle"] = AssetRoot + "Original/vgrasea.wav",
        ["order_vehicle"] = AssetRoot + "Original/vgramoa.wav",
        ["select_vehicle_soviet"] = AssetRoot + "Original/vgrssea.wav",
        ["order_vehicle_soviet"] = AssetRoot + "Original/vgrsmoa.wav",
        ["select_sniper"] = AssetRoot + "Original/isnisea.wav",
        ["order_sniper"] = AssetRoot + "Original/isnimoa.wav",
        ["select_dog"] = AssetRoot + "Original/idogsela.wav",
        ["order_dog"] = AssetRoot + "Original/idogmova.wav",
        ["rifle"] = AssetRoot + "Original/igiat1a.wav",
        ["conscript"] = AssetRoot + "Original/iconatta.wav",
        ["sniper"] = AssetRoot + "Original/isniatta.wav",
        ["tank"] = AssetRoot + "Original/vgriatta.wav",
        ["rhino"] = AssetRoot + "Original/vrhiatta.wav",
        ["apocalypse"] = AssetRoot + "Original/vapoat1a.wav",
        ["tesla"] = AssetRoot + "Original/vtesatta.wav",
        ["tesla_coil"] = AssetRoot + "Original/btesat1a.wav",
        ["grand_cannon"] = AssetRoot + "Original/bgraatta.wav",
        ["prism"] = AssetRoot + "Original/vpriatta.wav",
        ["flak"] = AssetRoot + "Original/vflaat1a.wav",
        ["ifv"] = AssetRoot + "Original/vifvat2a.wav",
        ["dog"] = AssetRoot + "Original/idogatta.wav",
        ["impact"] = AssetRoot + "Original/bmetdama.wav",
        ["flesh"] = AssetRoot + "Original/igidia.wav",
        ["explosion"] = AssetRoot + "Original/gexp14a.wav",
        ["construction"] = AssetRoot + "Original/uqeue.wav",
        ["chat"] = AssetRoot + "Original/umessage.wav",
        ["building_ready"] = AssetRoot + "Original/ceva048.wav",
        ["production_ready"] = AssetRoot + "Original/ceva062.wav",
        ["victory"] = AssetRoot + "Original/ceva022.wav",
        ["defeat"] = AssetRoot + "Original/ceva023.wav",
        ["building_ready_soviet"] = AssetRoot + "Original/csof048.wav",
        ["production_ready_soviet"] = AssetRoot + "Original/csof062.wav",
        ["victory_soviet"] = AssetRoot + "Original/csof022.wav",
        ["defeat_soviet"] = AssetRoot + "Original/csof023.wav"
    });

    sealed class Slot(AudioStreamPlayer player)
    {
        public AudioStreamPlayer Player { get; } = player;
        public ulong Started;
        public int Priority;
    }

    readonly Node owner;
    readonly AudioStreamPlayer music;
    readonly Slot[] effects;
    readonly Slot[] voices;
    readonly Dictionary<string, AudioStream> clips = [];
    readonly Dictionary<string, ulong> lastCue = [];
    readonly HashSet<int> previousUnits = [];
    BuildingType? previousBuildingProduction;
    bool previousBuildingReady;
    readonly HashSet<int> currentUnits = [];
    SimWorld? observedWorld;
    int observedPlayer = -2;
    int lastTick = -1;
    bool resultPlayed;
    bool sovietVoice;
    bool disposed;
    string musicName = "";
    ulong serial;

    public int LoadedClipCount => clips.Count;
    public int EffectsCapacity => effects.Length;
    public int VoiceCapacity => voices.Length;
    public string MusicTrack => musicName;
    public bool MusicPlaying => !disposed && GodotObject.IsInstanceValid(music) && music.Playing;
    public int ActiveEffects => disposed ? 0 : effects.Count(s => s.Player.Playing);
    public int ActiveVoices => disposed ? 0 : voices.Count(s => s.Player.Playing);
    public long ObservedCombatEvents { get; private set; }
    public long HiddenShotsSuppressed { get; private set; }
    public long PlayedCues { get; private set; }
    public bool OwnsNode(Node node) => ReferenceEquals(owner, node);

    public GameAudio(Node parent)
    {
        // Defensive second guard: default headless callers never construct this manager.
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GameAudio playback is disabled in headless mode.");
        owner = new Node { Name = "GameAudioPlayers", ProcessMode = Node.ProcessModeEnum.Always };
        parent.AddChild(owner);
        music = NewPlayer("MusicLoop", "Music");
        effects = CreatePool("Effect", "Effects", EffectsPoolSize);
        voices = CreatePool("Radio", "Voice", VoicePoolSize);
        foreach (var (name, path) in ActiveFiles)
        {
            var stream = LoadActiveStream(path);
            if (stream is null) { GD.PushWarning($"Could not load original audio clip: {path}"); continue; }
            if (name is "menu_music" or "battle_music")
            {
                if (stream is AudioStreamMP3 mp3) { mp3.Loop = true; mp3.LoopOffset = 0; }
            }
            clips.Add(name, stream);
        }
    }

    /// <summary>Decode a mapped original stream without creating players or enabling headless playback.</summary>
    public static AudioStream? LoadActiveStream(string path)
    {
        if (!ActiveFiles.Values.Contains(path)) return null;
        var imported = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
        if (imported is not null) return imported;
        if (!Godot.FileAccess.FileExists(path)) return null;
        var source = ProjectSettings.GlobalizePath(path);
        return path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? AudioStreamMP3.LoadFromFile(source) : AudioStreamWav.LoadFromFile(source);
    }

    AudioStreamPlayer NewPlayer(string name, string bus)
    {
        var player = new AudioStreamPlayer { Name = name, Bus = bus, MaxPolyphony = 1 };
        owner.AddChild(player);
        return player;
    }

    Slot[] CreatePool(string prefix, string bus, int count)
    {
        var result = new Slot[count];
        for (var i = 0; i < count; i++) result[i] = new Slot(NewPlayer(prefix + i, bus));
        return result;
    }

    public void Menu()
    {
        if (disposed || musicName == "menu_music") return;
        ResetObservation();
        StopOneShots();
        ChangeMusic("menu_music", -4);
    }

    public void Battle(SimWorld world, int player)
    {
        if (disposed) return;
        var changed = !ReferenceEquals(observedWorld, world) || observedPlayer != player;
        if (changed) { StopOneShots(); Snapshot(world, player); }
        ChangeMusic("battle_music", -7);
    }

    void ChangeMusic(string name, float volumeDb)
    {
        if (disposed || musicName == name || !clips.TryGetValue(name, out var clip)) return;
        music.Stop();
        music.Stream = clip;
        music.VolumeDb = volumeDb;
        music.PitchScale = 1;
        musicName = name;
        music.Play();
    }

    public void UiClick() => Cue("ui_click", false, -7, 5, 45);
    public void Selection(bool order = false, UnitType? selected = null, bool soviet = false)
    {
        if (selected is { } original && OriginalSelection(original, order, soviet)) return;
        var name = order ? "order" : "select";
        if (selected == UnitType.Sniper) name += "_sniper";
        else if (selected == UnitType.Dog) name += "_dog";
        else if (selected is { } type && !Rules.Unit(type).Infantry) name += soviet ? "_vehicle_soviet" : "_vehicle";
        else if (soviet) name += "_soviet";
        Cue(name, true, -5, 4, 250);
    }
    public void Chat() => Cue("chat", false, -8, 6, 200);
    string FactionCue(string name) => sovietVoice ? name + "_soviet" : name;

    /// <summary>
    /// Call once after each authoritative/local simulation tick. It never changes world state.
    /// A visible impact never grants permission to hear an unseen source's weapon identity.
    /// </summary>
    public void Observe(SimWorld world, int player, bool spectating, Vector2 listenerCell)
    {
        if (disposed) return;
        if (!ReferenceEquals(observedWorld, world) || observedPlayer != player || world.Tick < lastTick)
            Snapshot(world, player);
        if (world.Tick == lastTick) return;
        lastTick = world.Tick;
        var validPlayer = player >= 0 && player < world.Players.Length;
        if (!validPlayer && !spectating) return;

        ObserveOriginalWeapons(world, player, validPlayer, spectating, listenerCell);
        var impacts = 0;
        var explosions = 0;
        foreach (var e in world.Events)
        {
            ObservedCombatEvents++;
            if (e.Damage <= 0 && !e.Explosion) continue;
            // Damage only drives hit/death sounds. Weapon identity and timing
            // come from actual Fire events, including secondary/IFV/launchers.
            var targetAudible = spectating || validPlayer && world.Visible(player, e.X / Rules.Scale, e.Y / Rules.Scale);
            if (!targetAudible) continue;
            var impactDb = Attenuation(e.X, e.Y, listenerCell);
            if (impactDb <= -32) continue;
            if (e.Explosion && explosions < 2)
            {
                if (Cue(e.TargetInfantry ? "flesh" : "explosion", false, -7 + impactDb, 3, 85)) explosions++;
            }
            else if (!e.Explosion && !e.TargetInfantry && impacts < 2)
            {
                // The source set has no dedicated flesh-hit recording; do not play a death cry on a nonfatal hit.
                if (Cue("impact", false, -16 + impactDb, 1, 65)) impacts++;
            }
        }
        ObserveOriginalCrates(world, player, validPlayer, spectating);
        ObserveEconomy(world, player, validPlayer);
        if (!resultPlayed && (world.Finished || validPlayer && world.Players[player].Defeated))
        {
            resultPlayed = true;
            var won = world.Finished && (!validPlayer || world.WinningTeam == world.Players[player].Setup.Team)
                && (!validPlayer || !world.Players[player].Defeated);
            Cue(FactionCue(won ? "victory" : "defeat"), true, -3, 10, 0);
            music.VolumeDb = -15;
        }
    }

    static float Attenuation(int x, int y, Vector2 listener)
    {
        if (x < 0 || y < 0) return -33;
        var dx = x / (float)Rules.Scale - listener.X;
        var dy = y / (float)Rules.Scale - listener.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        return -Math.Clamp((distance - 12) * .8f, 0, 33);
    }

    void ObserveEconomy(SimWorld world, int player, bool validPlayer)
    {
        if (!validPlayer) return;
        currentUnits.Clear();
        var produced = false;
        foreach (var unit in world.Units)
        {
            if (!unit.Alive || unit.Owner != player) continue;
            currentUnits.Add(unit.Id);
            if (!previousUnits.Contains(unit.Id)) { produced = true; OriginalCreated(unit); }
        }
        previousUnits.Clear();
        previousUnits.UnionWith(currentUnits);
        var state = world.Players[player];
        var changedProduction = state.BuildingProduction != previousBuildingProduction;
        var started = state.BuildingProduction is not null && changedProduction;
        var complete = state.BuildingProduction is not null && state.BuildingReady && (changedProduction || !previousBuildingReady);
        previousBuildingProduction = state.BuildingProduction;
        previousBuildingReady = state.BuildingReady;
        // Announce sidebar readiness, not the later placement/visual construction animation.
        if (complete) Cue(FactionCue("building_ready"), true, -5, 7, 450);
        else if (started) Cue("construction", false, -7, 3, 300);
        if (produced) Cue(FactionCue("production_ready"), true, -5, 6, 450);
    }

    void Snapshot(SimWorld world, int player)
    {
        ResetObservation();
        observedWorld = world;
        observedPlayer = player;
        sovietVoice = player >= 0 && player < world.Players.Length && Rules.Side(world.Players[player].Setup.Country) == Faction.Soviet;
        lastTick = world.Tick;
        foreach (var unit in world.Units)
            if (unit.Alive && unit.Owner == player) previousUnits.Add(unit.Id);
        if (player >= 0 && player < world.Players.Length)
        {
            previousBuildingProduction = world.Players[player].BuildingProduction;
            previousBuildingReady = world.Players[player].BuildingReady;
        }
    }

    void ResetObservation()
    {
        observedWorld = null;
        observedPlayer = -2;
        lastTick = -1;
        resultPlayed = false;
        sovietVoice = false;
        previousUnits.Clear();
        previousBuildingProduction = null;
        previousBuildingReady = false;
        currentUnits.Clear();
        lastCue.Clear();
    }

    bool Cue(string name, bool voice, float volumeDb, int priority, ulong intervalMs, float pitch = 1)
    {
        if (disposed || !clips.TryGetValue(name, out var clip)) return false;
        var now = Time.GetTicksMsec();
        if (intervalMs > 0 && lastCue.TryGetValue(name, out var previous) && now - previous < intervalMs) return false;
        var pool = voice ? voices : effects;
        Slot? choice = null;
        foreach (var slot in pool)
        {
            if (!slot.Player.Playing) { choice = slot; break; }
            if (slot.Priority > priority) continue;
            if (choice is null || slot.Priority < choice.Priority || slot.Priority == choice.Priority && slot.Started < choice.Started)
                choice = slot;
        }
        if (choice is null) return false;
        choice.Player.Stop();
        choice.Player.Stream = clip;
        choice.Player.VolumeDb = volumeDb;
        choice.Player.PitchScale = pitch;
        choice.Priority = priority;
        choice.Started = ++serial;
        lastCue[name] = now;
        choice.Player.Play();
        PlayedCues++;
        return true;
    }

    void StopOneShots()
    {
        foreach (var slot in effects) { slot.Player.Stop(); slot.Player.Stream = null; }
        foreach (var slot in voices) { slot.Player.Stop(); slot.Player.Stream = null; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (GodotObject.IsInstanceValid(owner))
        {
            // Release player references before disposing native RefCounted audio resources.
            if (GodotObject.IsInstanceValid(music)) { music.Stop(); music.Stream = null; }
            StopOneShots();
            owner.Free();
        }
        foreach (var clip in clips.Values) clip.Dispose();
        clips.Clear();
        musicName = "";
        ResetObservation();
    }
}
