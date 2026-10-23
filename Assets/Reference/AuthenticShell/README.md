# Authentic original RA2 title-screen movie

The complete original `ra2ts_l.bik` was extracted byte-for-byte from the pinned
`language.mix` at revision `acfa5b834899e7fab4c8499ccb4b49c402927b44` of
[toarrntnz/ts-redalert2-assets](https://github.com/toarrntnz/ts-redalert2-assets/tree/acfa5b834899e7fab4c8499ccb4b49c402927b44).
This is original UI media, not an executable game implementation.

| Property | Large title screen | Small title screen (metadata only) |
| --- | --- | --- |
| Original filename | `ra2ts_l.bik` | `ra2ts_s.bik` |
| MIX filename CRC32 | `33665128` | `c1e6e166` |
| Absolute MIX offset | 37,942,628 | 45,127,460 |
| Original file bytes | 7,184,828 | 7,184,988 |
| Exact complete range | `bytes=37942628-45127455` | `bytes=45127460-52312447` |
| Bink signature | `BIKi` | `BIKi` |
| Native dimensions | 632 × 570 | 472 × 450 |
| Frame count | 431 | 431 |
| Frame rate | 15/1 fps | 15/1 fps |
| Duration | 28.733333333333334 seconds | 28.733333333333334 seconds |
| Audio tracks | 0 | 0 |
| Keyframes flagged in original table | frame 0 only | frame 0 only |

Large original SHA-256:
`4c73d2b9c5dc1dccab156edf7a48bda9ae11928fa97907c4b20e304d2da7d2a6`.

The large original is retained at [Original/ra2ts_l.bik](Original/ra2ts_l.bik).
The two exact original header/frame-table prefixes and parsed offsets are in
[Metadata](Metadata/). A Bink table entry's low bit flags a keyframe; the remaining
bits are the movie-relative byte offset. Both files have a 44-byte video header,
no audio metadata, and 432 32-bit table entries (431 frames plus end-of-file).
Frame zero of the large file starts at movie byte 1,772 and uses 50,848 bytes.
For a strictly bounded first-keyframe acquisition, the complete original header,
frame table, and first frame occupy only 52,620 bytes:
`bytes=37942628-37995247` of the pinned MIX. The prefix retains the original
431-frame header, so it is evidence/extraction input rather than a standalone
short movie. The full original movie is modest, so a truncated/reconstructed
Bink was unnecessary.

The existing encrypted MIX index ranges, 0–91 and 84–163, are reused read-only
and SHA-verified using the previous menu manifest. Its original MIX reader
decrypts those bytes and confirms both exact audited movie offsets before any
movie acquisition. The 53,116,040-byte MIX archive was never downloaded whole.
Only the selected movie, two tiny header/table probes, and existing index ranges
are involved. Movie network requests are exact HTTP 206 ranges of at most 1 MiB;
incorrect status, Content-Range, length, or source hash stops acquisition.

Reproduce from the project root:

```sh
python3 Tools/fetch_authentic_shell.py --probe
python3 Tools/fetch_authentic_shell.py
python3 Tools/prepare_authentic_shell.py --ffmpeg /path/to/native/ffmpeg
```

`--variant small` or `--variant both` explicitly selects the alternative original
payload. The default acquires only the large movie and probes small metadata.
Repeated default acquisition validates and reuses the original with zero network
bytes. Acquisition needs Python 3 plus Node with its built-in OpenSSL legacy
provider for the existing read-only MIX index decoder; no Python image or crypto
packages are installed. Preparation uses an independent FFmpeg with Bink,
rawvideo/pipe, and RGB conversion support; Ogg/libtheora support produces the
Godot playback version. The companion manifest records original/range SHA-256, source URLs, exact
HTTP ranges, Content-Range, ETag, complete movie metadata, and cache provenance.
Range caches live beneath `$XDG_CACHE_HOME/redalert-authentic-shell`; transient
files live beneath `$TMPDIR`. No configuration/cache files are placed in the
project or an emulated `/tmp` directory.

The source Bink is retained for provenance and future lossless decoding. Godot
normally consumes the generated PNG stills or Ogg Theora movie, rather than Bink.
See the [generated inspection and playback guide](../../Generated/AuthenticShell/README.md).

## Independent native decoder on this host

There was no FFmpeg/ffprobe command or Bink-capable system converter on PATH.
The system FFmpeg media libraries returned no public `binkvideo` decoder or
`bink` input format. A stock, unpatched FFmpeg 7.1.2 was built and executed
natively in XDG cache with the existing signed HarmonyOS SDK compiler and GNU
Make. The source pins and toolchain are recorded in
[decoder-source-pins.json](decoder-source-pins.json) and
[decoder-build-environment.json](decoder-build-environment.json). The final
[decoder build and validation record](decoder-build.json) includes exact cached
rebuild recipes, both binary hashes, and the verified Ogg Theora artifact.

The retained minimal decoder is
`$XDG_CACHE_HOME/redalert-authentic-shell/ffmpeg-minimal/bin/ffmpeg`, with ffprobe
beside it. Its only shared runtime dependency is the platform `libc.so`; codec,
format, scaling and zlib code are static. The complete original independently
passed [ffprobe's 431-frame decode count](Metadata/ra2ts_l.bik.ffprobe.json).
Its one-frame RGB export matches the preparation tool's first frame byte for
byte: 1,080,720 RGB bytes, SHA-256
`ebf3d8fac1ff338e05f3845fbd524a95794da812a5ee1efc3aa57026347b5867`.

The full playback converter adds static libogg 1.3.6/libtheora 1.2.0 and is
located at `$XDG_CACHE_HOME/redalert-authentic-shell/ffmpeg/bin/ffmpeg` after its
build. Preparation checks that full path first, then the retained minimal
variant. The generated manifest records the actual binary hash, exact
configuration, conversion commands, output hashes, and Ogg Theora validation.
