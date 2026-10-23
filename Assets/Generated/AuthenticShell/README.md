# Authentic RA2 shell media for Godot

Source: the exact original large title-screen Bink movie from the pinned RA2
asset repository. [Original movie](../../Reference/AuthenticShell/Original/ra2ts_l.bik)
and [source provenance](../../Reference/manifest-authentic-shell.json) are retained
separately. Original SHA-256:
`4c73d2b9c5dc1dccab156edf7a48bda9ae11928fa97907c4b20e304d2da7d2a6`.

Visual inspection confirms the **original gold/red RA2 title logo**, **red
radar/globe/map and sweeping beam**, occasional footage behind the radar sweep,
**black curved CRT surround**, and **silver bezel/panel** are all baked into the
movie. **Main-menu command buttons are not baked in.** The logo is already
present; faithful use does not need a second logo overlay.

Native movie geometry is **632 × 570 pixels**, **431 frames at 15 fps**, lasting
**28.733333333333334 seconds**, with **no audio**. All 431 frames decoded without
errors and have distinct RGB hashes. The entire `[0, 0, 632, 570]` movie rectangle
is decorative: the logo, globe, radar display, glass, and bezel are **not click
targets**. Use independent Godot buttons for menu commands and set the media
node's mouse filter to Ignore.

The Bink header contains no screen origin, full-screen canvas dimensions, or
click rectangles. Exact original button placement cannot be recovered from
this background movie alone. Preserve the movie aspect ratio when integrating
it into the surrounding menu layout.

Exported native resources:

| Resource | Content |
| --- | --- |
| [firstframe.png](ra2ts_l/firstframe.png) | Original frame 0, native 632 × 570, lossless PNG of decoded RGB |
| [frame-0107.png](ra2ts_l/frame-0107.png) | Original frame 107 at 7.133333 seconds |
| [frame-0215.png](ra2ts_l/frame-0215.png) | Original frame 215 at 14.333333 seconds |
| [frame-0323.png](ra2ts_l/frame-0323.png) | Original frame 323 at 21.533333 seconds |
| [frame-0430.png](ra2ts_l/frame-0430.png) | Original frame 430 at 28.666667 seconds |
| [contact-sheet.png](ra2ts_l/contact-sheet.png) | Five native frames, left-to-right then top-to-bottom; sixth cell black |
| [menu-loop.ogv](ra2ts_l/menu-loop.ogv) | 5,490,952-byte Ogg Theora loop, quality 9, all 431 original frames at native size/rate |
| [decoded-frame-hashes.json](ra2ts_l/decoded-frame-hashes.json) | SHA-256 of every decoded RGB frame, in original order |

[Generated manifest](manifest.json) records the decoder version, binary SHA,
commands, original source SHA, dimensions/timing, all exported file SHA values,
and the independent Ogg Theora probe. The loop passed a complete independent
decode: 431 frames, Theora, 632 × 570, 15/1 fps, duration 28.733333 seconds,
one video stream and no audio. Its SHA-256 is
`694e6daec92dbbea27ae0c827c9c26e70a033d9e348f910a9f9fd2d86ed9a2c6`.
PNG stills preserve the native decoded RGB samples. The Theora loop is a lossy playback derivative; the
complete original Bink remains the archival source. No frame is cropped,
repainted, interpolated, or replaced. No seam smoothing is applied when repeating
the complete original movie.

[Visual inspection](inspection.json) records what the actual decoded images show
and distinguishes visible art from input hit targets.

## Use in Godot

Godot's standard [VideoStreamPlayer](https://docs.godotengine.org/en/stable/classes/class_videostreamplayer.html)
supports Ogg Theora and its `loop` property restarts the video at EOF. No Bink
plugin is needed for the generated OGV.

For a still background, load the PNG as a normal texture. A `TextureRect` can use
`KeepAspectCentered` and `MouseFilterEnum.Ignore`:

```csharp
var image = GD.Load<Texture2D>(
    "res://Assets/Generated/AuthenticShell/ra2ts_l/firstframe.png");
var background = new TextureRect {
    Texture = image,
    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
    MouseFilter = Control.MouseFilterEnum.Ignore
};
```

For animation, assign the generated movie to a `VideoStreamPlayer`. Keep the
player at the native size, or put it in a layout that preserves 632:570:

```csharp
var background = new VideoStreamPlayer {
    Stream = GD.Load<VideoStreamTheora>(
        "res://Assets/Generated/AuthenticShell/ra2ts_l/menu-loop.ogv"),
    Loop = true,
    Autoplay = true,
    Expand = false,
    MouseFilter = Control.MouseFilterEnum.Ignore
};
```

These are integration examples; acquisition/preparation does not edit the C#
application, build the game, or control Godot. Playback in the project must be
verified when the application integrates the resource.

## Reproduce

From the project root:

```sh
python3 Tools/fetch_authentic_shell.py
python3 Tools/prepare_authentic_shell.py
```

Preparation finds FFmpeg on PATH, via `$FFMPEG`, via `--ffmpeg`, or at
`$XDG_CACHE_HOME/redalert-authentic-shell/ffmpeg/bin/ffmpeg`. Use `--no-loop` to
export PNG stills when FFmpeg lacks the libtheora encoder. All image generation
uses Python's standard library; no Python image library is installed.
