using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    GameAudio? gameAudio;
    bool gameAudioInBattle;

    /// <summary>Call after loading preferences and setting headless. No audio work occurs headlessly.</summary>
    public void InitGameAudio()
    {
        if (gameAudio is not null || headless || DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase)) return;
        clientPreferences.ApplyAudio();
        gameAudio = new GameAudio(this);
    }

    /// <summary>ClearUi must preserve this one native audio owner while rebuilding controls.</summary>
    public bool IsGameAudioNode(Node node) => gameAudio?.OwnsNode(node) == true;

    /// <summary>Call on menu entry/return; repeated front-end navigation preserves the loop position.</summary>
    public void AudioMenu()
    {
        InitGameAudio();
        gameAudioInBattle = false;
        gameAudio?.Menu();
    }

    /// <summary>Call once when entering a battle after assigning Battle/localWorld/network.World.</summary>
    public void AudioBattle()
    {
        if (Battle is not { } world) return;
        InitGameAudio();
        gameAudioInBattle = true;
        gameAudio?.Battle(world, LocalPlayer);
    }

    public void AudioUiClick() => gameAudio?.UiClick();
    public void AudioSelection(bool order = false)
    {
        if (gameAudio is null) return;
        var world = Battle;
        Unit? selected = null;
        if (world is not null && battlefield is not null)
            foreach (var id in battlefield.Selection)
                if (world.Find(id) is Unit unit) { selected = unit; break; }
        var owner = selected?.Owner ?? LocalPlayer;
        var soviet = world is not null && owner >= 0 && owner < world.Players.Length && Rules.Side(world.Players[owner].Setup.Country) == Faction.Soviet;
        if(selected is not null)gameAudio.SelectionFor(selected,order);
        else gameAudio.Selection(order,null,soviet);
    }
    public void AudioChat() => gameAudio?.Chat();

    /// <summary>Call after every simulation step, separately from graphical effect/detail capture.</summary>
    public void AudioCaptureTick()
    {
        if (!gameAudioInBattle || gameAudio is null || Battle is not { } world) return;
        var player = LocalPlayer;
        var spawn = world.Map.Spawns[Math.Clamp(player, 0, world.Players.Length - 1)];
        var listener = battlefield?.CameraCell ?? new Vector2(spawn.X, spawn.Y);
        gameAudio.Observe(world, player, IsSpectating, listener);
    }

    /// <summary>Stop before Main leaves the tree. This is safe to call repeatedly.</summary>
    public void StopGameAudio()
    {
        gameAudioInBattle = false;
        gameAudio?.Dispose();
        gameAudio = null;
    }

    /// <summary>Read-only focused diagnostic; it does not enable audio in a headless session.</summary>
    public Godot.Collections.Dictionary GetAudioAudit() => new()
    {
        ["enabled"] = gameAudio is not null,
        ["inBattle"] = gameAudioInBattle,
        ["loadedClips"] = gameAudio?.LoadedClipCount ?? 0,
        ["musicTrack"] = gameAudio?.MusicTrack ?? "",
        ["musicPlaying"] = gameAudio?.MusicPlaying ?? false,
        ["effectsCapacity"] = gameAudio?.EffectsCapacity ?? 0,
        ["voiceCapacity"] = gameAudio?.VoiceCapacity ?? 0,
        ["activeEffects"] = gameAudio?.ActiveEffects ?? 0,
        ["activeVoices"] = gameAudio?.ActiveVoices ?? 0,
        ["playedCues"] = gameAudio?.PlayedCues ?? 0,
        ["observedCombatEvents"] = gameAudio?.ObservedCombatEvents ?? 0,
        ["observedOriginalWeaponEvents"] = gameAudio?.ObservedOriginalWeaponEvents ?? 0,
        ["missingOriginalWeaponCues"] = gameAudio?.MissingOriginalWeaponCues ?? 0,
        ["intentionalSilentWeaponEvents"] = gameAudio?.IntentionalSilentWeaponEvents ?? 0,
        ["observedCratePickups"] = gameAudio?.ObservedCratePickups ?? 0,
        ["playedCrateCues"] = gameAudio?.PlayedCrateCues ?? 0,
        ["playedOriginalCreatedCues"] = gameAudio?.PlayedOriginalCreatedCues ?? 0,
        ["hiddenShotsSuppressed"] = gameAudio?.HiddenShotsSuppressed ?? 0
    };
}
