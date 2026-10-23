# Original Red Alert 2 music and sound

The active audio now uses **actual original RA2 soundtrack recordings, game sound effects, unit voices, and EVA announcements**. It does not use the earlier synthesized score/effects. There are **41 active original recordings: 2 unchanged MP3s and 39 decoded PCM WAVs**, totaling **7,579,924 bytes** (about 7.23 MiB).

## Primary sources and provenance

### Original music from the composer

Both active music files were downloaded directly from [Frank Klepacki’s official C&C: Red Alert 2 ORIGINAL SOUNDTRACK page](https://www.frankklepacki.com/ost/vg/cnc-ra2). That page identifies the original Westwood Studios / Electronic Arts game and provides the following recordings directly:

| Use | Original recording | Exact source | Retained bytes |
| --- | --- | --- | ---: |
| Intro/menu loop | **Grinder**, approximately 2:27 | [04+Grinder.mp3](https://static1.squarespace.com/static/5cb8811d5239585cb782f0e0/5ce600b4e4966b3368e7ec34/5ce9ccaeb208fc0bb153a6a2/1558826397956/04+Grinder.mp3) | 2,353,946 |
| Battle loop | **Hell March 2**, approximately 3:44 | [01+Hell+March+2.mp3](https://static1.squarespace.com/static/5cb8811d5239585cb782f0e0/5ce600b4e4966b3368e7ec34/5ce9cca9ec212dc60a434c65/1558826387658/01+Hell+March+2.mp3) | 3,596,664 |

The retained [original theme configuration](../Assets/Reference/Audio/Metadata/theme.ini) independently confirms `[INTRO] Sound=Grinder, Normal=no, Repeat=yes` and `[HM2] Sound=HM2, Normal=yes`. Thus the menu uses the actual game’s intro-theme mapping. The options/credits track is distinct and is not mislabeled as Grinder.

These are composer-provided original soundtrack MP3 recordings, copied **byte-for-byte** into the runtime assets. They are not claimed to be byte-identical to retail IMA ADPCM theme-container payloads, and are not fan remakes or synthesized arrangements. The source and runtime SHA256 hashes are identical for both MP3s. Hell March 2 SHA256 is `96fe852a7cb70da253ca6a69d1f1f85e907144b338b716996a42272fc2fa4790`; all hashes, including Grinder, are in the [active original manifest](../Assets/Generated/Audio/original-manifest.json).

### Original effects and voices from the game archive

Game recordings come from the same fixed asset repository already used for the project’s original visual references:

- [Pinned XWIS asset provenance](https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/acfa5b834899e7fab4c8499ccb4b49c402927b44/game-res/manifest.json) explicitly identifies the source as the **Red-Alert-2-Multiplayer (XWIS)** copy processed through the import pipeline.
- [Pinned original language archive](https://raw.githubusercontent.com/toarrntnz/ts-redalert2-assets/acfa5b834899e7fab4c8499ccb4b49c402927b44/game-res/language.mix) contains the actual game sound bank and loose EVA recordings. Only selected HTTP 206 byte ranges were fetched; no complete game archive or executable was downloaded.
- [XCC’s original format guide](http://xhp.xwis.net/documents/ra2/guide_file_types.html) identifies BAG/IDX and IMA ADPCM WAV audio under `language.mix → audio.mix`. [CnCNet’s extraction discussion](https://forums.cncnet.org/topic/2057-extracting-ra2-sounds/#findComment-16896) corroborates that structure.
- The retained [sound configuration](../Assets/Reference/Audio/Metadata/sound.ini), [theme configuration](../Assets/Reference/Audio/Metadata/theme.ini), and [selection metadata](../Assets/Reference/Audio/Metadata/selection.json) record original sample-name semantics. [OpenRA’s notification mapping](https://raw.githubusercontent.com/OpenRA/ra2/61e24e3c1d7b586aa55a86096d29e1559aa9b994/mods/ra2/audio/notifications.yaml), [voice mapping](https://raw.githubusercontent.com/OpenRA/ra2/61e24e3c1d7b586aa55a86096d29e1559aa9b994/mods/ra2/audio/voices.yaml), and [weapon mapping](https://github.com/OpenRA/ra2/tree/61e24e3c1d7b586aa55a86096d29e1559aa9b994/mods/ra2/weapons) were used only as corroborating name references, not as replacement audio or copied game implementation.

The 53,116,040-byte language archive has its nested audio container at offset 164. The original sound bank begins at absolute offset **19,772,134**, length **17,643,176**. Its [retained index](../Assets/Reference/Audio/Metadata/audio.idx) begins at absolute offset **37,415,318**, length **41,520**, with `GABA` signature, version 2, and 1,153 indexed original samples. EVA announcements are loose RIFF WAV entries in the nested audio archive, separate from the BAG. The [source manifest](../Assets/Reference/Audio/manifest.json) records exact URLs, validated HTTP ranges, content ranges, original names, offsets, codec metadata, byte lengths, and SHA256 hashes for every acquired original.

Original audio remains the property of EA/Westwood, Frank Klepacki, and the respective rights holders. The mirror’s documentation license does not relicense the game audio. The active original assets carry **no CC0 or open-source audio claim**.

## Active cue mapping

All runtime originals are under the [original audio directory](../Assets/Generated/Audio/Original/). Logical cue names map to original sample names as follows:

| Event | Original sample(s) | Bus |
| --- | --- | --- |
| Menu intro / battle music | `Grinder` / `HM2` | Music |
| UI activation / chat arrival | `umenucl1` / `umessage` | Effects |
| Allied GI selection / order | `igisea` / `igimoa` | Voice |
| Soviet Conscript selection / order | `iconsea` / `iconmoa` | Voice |
| Allied vehicle selection / order | `vgrasea` / `vgramoa` | Voice |
| Soviet vehicle selection / order | `vgrssea` / `vgrsmoa` | Voice |
| Sniper selection / order | `isnisea` / `isnimoa` | Voice |
| Dog selection / order | `idogsela` / `idogmova` | Voice |
| GI rifle / Conscript rifle | `igiat1a` / `iconatta` | Effects |
| Sniper rifle | `isniatta` | Effects |
| Grizzly / Rhino cannon | `vgriatta` / `vrhiatta` | Effects |
| Apocalypse cannon | `vapoat1a` | Effects |
| Tesla tank / Tesla Coil | `vtesatta` / `btesat1a` | Effects |
| Prism tank | `vpriatta` | Effects |
| Flak Track / IFV ground fire | `vflaat1a` / `vifvat2a` | Effects |
| Grand Cannon | `bgraatta` | Effects |
| Dog attack | `idogatta` | Effects |
| Metal damage / explosion | `bmetdama` / `gexp14a` | Effects |
| Infantry death | `igidia` | Effects |
| Construction queued | `uqeue` (the original MenuBuild queue signal) | Effects |
| Construction complete | Allied `ceva048`, Soviet `csof048` | Voice |
| Unit ready | Allied `ceva062`, Soviet `csof062` | Voice |
| Victory / defeat | Allied `ceva022` / `ceva023`, Soviet `csof022` / `csof023` | Voice |

Selection voices choose the selected unit’s faction and vehicle/sniper/dog role. Mixed groups emit one acknowledgement. EVA uses the local player’s faction. Construction observes the player’s queued building type and ready-state transition: the original completion announcement occurs when the sidebar becomes ready, before placement. Later placement and its visual construction animation do not repeat EVA. This uses actual recorded unit responses and announcements, replacing the former non-verbal synthesized signals.

Two mappings are explicit adaptations, not claims of exact original engine behavior: the original building metal-damage sample also covers generic armor impacts, and the original GI death cry covers generic infantry death because the presentation combat event only exposes whether the target is infantry. **No separate original flesh-hit recording is asserted. Nonfatal infantry hits stay silent**; a death cry is never substituted for a flesh hit. No generated replacement fills that gap.

## Conversion and runtime contract

[fetch_original_audio.py](../Tools/fetch_original_audio.py) acquires pinned container indexes, selected raw compressed bank samples, loose original EVA WAVs, primary INI data, and the two composer MP3s. It records exact source evidence in the [source manifest](../Assets/Reference/Audio/manifest.json). Acquisition transferred **7,082,895 bytes** in the successful full run, including metadata/index requests and the two full music files.

[prepare_original_audio.py](../Tools/prepare_original_audio.py) independently decodes standard mono IMA ADPCM blocks to 16-bit PCM WAV. It checks predictor/index headers and RIFF bounds, preserves the original 22,050 Hz sample rate and amplitude, and honors a WAV fact sample count when present. There is no synthesis, resampling, normalization, new melody, external codec install, or runtime conversion. The composer MP3s remain unchanged. The [active original manifest](../Assets/Generated/Audio/original-manifest.json) records each conversion, source/runtime hash, byte size, and exact active resource path.

[GameAudio.cs](../Scripts/GameAudio.cs) exposes `public static IReadOnlyDictionary<string, string> ActiveFiles`, containing **only the 41 active originals**. `public static AudioStream? LoadActiveStream(string path)` loads those exact paths without creating players or playing audio, so a tiny headless diagnostic can check native MP3/WAV decoding. Runtime loading first uses imported resource remapping and then a native MP3/WAV file-loader fallback for local files awaiting editor import. Native music looping uses the full original MP3 recording; no seamless-loop claim or edited musical splice is made.

The existing [Main audio hooks](../Scripts/Main.Audio.cs) are unchanged:

- `InitGameAudio()`, `AudioMenu()`, `AudioBattle()`.
- `AudioUiClick()`, `AudioSelection(bool order = false)`, `AudioCaptureTick()`, `AudioChat()`.
- `StopGameAudio()`, `IsGameAudioNode(Node node)`, `GetAudioAudit()`.

Normal initialization is suppressed headlessly before any asset/player creation. The explicit static native-decode helper does not override that playback guard. Graphical clients cache clips once and retain fixed pools of **1 Music, 12 Effects, and 3 Voice** players. Existing Music/Effects/Voice preferences remain authoritative. UI rebuilding preserves the audio owner. Returning to menus stops battle one-shots; exit releases player stream references before native resources. Combat retains independent source/impact visibility checks, source deduplication, bounded budgets, cooldowns, and camera-distance attenuation. Hidden enemy firing does not grant audio information merely because its impact is visible.

The old synthesized recordings and their historical generator remain inactive; they are not in `ActiveFiles` and are never a fallback for missing originals. The [active original manifest](../Assets/Generated/Audio/original-manifest.json), rather than the historical generated manifest, is the current source of truth.

## Editor import isolation fix

The original EVA source WAVs retain retail IMA ADPCM compression. They are conversion inputs, while Godot's WAV importer requires PCM or IEEE float input. The earlier native runtime decode check only exercised converted files and missed the editor automatically trying to import these source copies.

[Assets/Reference/Audio/.gdignore](../Assets/Reference/Audio/.gdignore) now excludes the raw recordings and provenance directory from Godot scanning and export. Both acquisition and conversion create this marker before retaining or processing audio; conversion validation requires it. Only the converted **39 PCM WAVs and 2 original MP3s** under the generated runtime directory are imported. Raw bytes, source hashes, active cue mappings, and recordings are preserved.

## Focused checks performed

The full fetch and conversion each completed successfully with exit code 0. The small conversion validator confirmed:

- **41 active originals**, comprising 39 PCM WAVs and 2 unchanged original MP3s.
- Original source hashes and converted runtime hashes.
- Mono IMA state validity, RIFF PCM headers, frame counts, original sample rates, and nonzero signal.
- Exact MP3 byte identity between download and runtime copy.
- All 41 C# `ActiveFiles` entries exactly match the active original manifest, and every mapped path exists.

The audio teammate did not build the project, run old regression suites, start Godot, or claim human listening verification. The Lead’s integrated build subsequently passed with **0 warnings and 0 errors**, and its single focused `--presentation-check` passed all four groups, including actual native `LoadActiveStream` decoding of **all 41 active original files**. Native decoded durations were **147.043 seconds** for Grinder and **224.758 seconds** for Hell March 2; results are recorded in the Lead’s [presentation alignment report](presentation-alignment.json). This confirms native decoder readiness without a human listening claim. Existing original recordings may reach full-scale samples; conversion preserves their original levels rather than applying an undocumented limiter.

Follow-up import isolation validation: after adding the raw-source ignore marker, conversion and validation again passed for all **41** active recordings, including **39 PCM WAVs** and **2 unchanged MP3s**. A read-only audit of the already-running graphical match confirmed `loadedClips=41`, `musicPlaying=true`, and `musicTrack=battle_music`. The isolated editor-import probe exceeded 45 seconds and was stopped; it is **not** recorded as a passing import test. No user match was interrupted. See the later [battlefield refinement report](BATTLEFIELD_REFINEMENT.md) for the complete follow-up and focused checks.

Reproduce acquisition and conversion, or validate the existing originals without regeneration:

```sh
python3 Tools/fetch_original_audio.py
python3 Tools/prepare_original_audio.py
python3 Tools/prepare_original_audio.py --validate
```
