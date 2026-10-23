# Original Red Alert 2 front-end references

Scope: the 2000 base game, with later **vanilla 1.006** evidence identified separately. No Yuri's Revenge, CnCNet launcher, OpenRA, remaster, or fan UI is used as a layout authority. Images below were directly inspected. Confidence applies to each row; a still proves visible layout, while manuals/procedural accounts support navigation. Exact patch identity is not invented when absent.

## Reference index

| ID | Local reference | Source / provenance | Version and confidence |
|---|---|---|---|
| S1 | [English retail manual](./ra2-2000-english-manual.pdf), [extracted text](./ra2-2000-english-manual-extracted.txt) | [Nyerguds C&C manual archive](http://nyerguds.arsaneus-design.com/manuals/Red%20Alert%202/Red%20Alert%202%20-%20English.pdf). Original printed pages 6–14 cover these menus; PDF pages 4–8 contain two printed pages each. | Base RA2 retail manual, 2000; **high** for documented operation. Not a screenshot of a running build. |
| S2 | [Main menu](./ra2-main-menu-mobygames-20001029.png) | [MobyGames image 10837](https://www.mobygames.com/game/2544/command-conquer-red-alert-2/screenshots/windows/10837/), contributed by emil, **2000-10-29**. Site image is 640×480. Local file is an element-only browser capture at 1.9× device scale. | **Version 1.0 visibly printed**; **high** for original layout, colors, order. |
| S3 | [Options root](./ra2-options-mobygames-20010410.png) | [MobyGames image 14417](https://www.mobygames.com/game/2544/command-conquer-red-alert-2/screenshots/windows/14417/), Andrew Hartnett, **2001-04-10**. Site image 800×600; local browser capture 1.9×. | Base RA2, captured before Yuri's release; patch unknown; **high** for visible layout. |
| S4 | [Skirmish setup](./ra2-skirmish-setup-mobygames-20040208.png) | [MobyGames image 61176](https://www.mobygames.com/game/2544/command-conquer-red-alert-2/screenshots/windows/61176/), Indra was here, **2004-02-08**. Site image 640×480; local capture 1.9×. | Base-game gallery, patch unknown; **high** for this skirmish screen, **medium** when used to illustrate the manual's shared network setup controls. This is **not** a LAN room screenshot. |
| S5 | [LAN lobby](./ra2-lan-lobby-tlukas-2013.png), [version crop](./ra2-version-1006-tlukas-2013.jpg) | [Tesyho weblog tutorial](https://www.tlukas.eu/uvod/red-alert-2-multiplayer). Available by **2013-04-04** (dated comments); exact publication date not exposed. Direct small image downloads. | Author specifies **RA2 1.006 plus wsock32 transport shim / Hamachi**. Native game UI, no CnCNet launcher; **high** for visible layout, **medium-high** for version attribution. Transport is modified; this does not prove stock IPX behavior. |
| S6 | [Chinese main menu](./ra2-main-menu-chinese-baidu-20121222.png), [IPX Options](./ra2-ipx-options-chinese-baidu-20121222.png), [IPX fields crop](./ra2-ipx-fields-crop-chinese-baidu-20121222.png) | [Baidu tutorial, updated 2012-12-22](https://jingyan.baidu.com/article/4dc40848f90995c8d946f19a.html). Main menu shows the original RA2 logo; IPX image was inspected independently of its filename. | Base-game front-end in Traditional Chinese, appears to show version 1.0; exact subbuild/distribution unverified. **Medium** as supplementary geometry evidence. Watermarked / 500-pixel images. |
| S7 | No additional image | [CNCNZ LAN/IPX discussion, 2004-06-09/10](https://forums.cncnz.com/topic/1470-ra2-lanipx-specific-help-please/) independently names `RA2 → Options → Network`, network adapter, Socket and Destination Network, lobby, player-name changes and chat. | Base RA2 as described by users, patch unknown; **medium** for corroborating field names and behavior. |

Complete original image URLs, hashes, saved dimensions, transformations, source excerpts, and exclusions are in [provenance.json](./provenance.json).

## Main menu: style and order

| Confirmed observation | Version / confidence / evidence |
|---|---|
| Top-to-bottom buttons: **Single Player → Internet → Network → Movies & Credits → Options → Exit Game**. “Main Menu” is the upper-right heading; “Version 1.0” is bottom right. | RA2 **1.0**, **high**, S2. |
| Dominant left area is a large rounded CRT/radar face, black and deep red with bright red radar circles/sweep, wrapped in thick curved silver/steel housing. The huge gold/red/silver RA2 logo fills its upper center. | RA2 **1.0**, **high**, S2. A still establishes the radar design; timing/animation behavior was not measured. |
| A narrow right rail has a black title plaque, a small amber/yellow warning monitor, metallic vents, then six stacked **rectangular red inset buttons** with black bevels, bright central red glow and thick silver surrounds. All visible labels are bright yellow. The geometry is mechanical and textured. | RA2 **1.0**, **high**, S2. Hover/pressed states are unverified. |
| At the native 640×480 source, the left housing occupies roughly **three quarters** of the width and the rail roughly **one quarter**. Button centers are evenly spaced below the monitor, with the version text beneath Exit Game. | RA2 **1.0**, **medium**, visual estimate from S2; not pixel measurements. |
| On Options/lobby/setup, the left area becomes a mostly flat dark burgundy/black map-texture panel with silver border; the right monitor/rail remains. Do not carry the giant main-menu logo into these pages. | Base RA2, patch unknown / S5 attributed **1.006**, **high**, S3–S5. |

## Options operation and real page structure

| Action or control | Version / confidence / evidence |
|---|---|
| **Main Menu → Options** opens one full Options screen. **Display Options, Game Options, UI Options and Audio Options are sections on this same page**, not four separate dialogs or tabs. Right rail: **Keyboard**, **Network**, bottom **Main Menu**. | Base RA2 pre-Yuri capture, patch unknown; **high**, S3 and S1 printed pp. 9–11. |
| Display: **Visual Details** horizontal slider (LOW/HIGH per manual), plus **Set Game Resolution** dropdown to its right. Capture shows **800 × 600 × 16**. | Base RA2, patch unknown; **high**, S3 + S1 p. 9. |
| Game: **Difficulty** horizontal slider, EASY/NORMAL/BRUTAL per manual. Capture shows Easy. The manual also discusses Game Speed and says single player is locked to default; **the front-end Options capture does not show a Game Speed slider here**. | Base retail manual / pre-Yuri capture, **high** for documented difficulty and visible omission; placement/availability of Game Speed outside setup is **unresolved**, S1 p. 9 + S3. |
| UI: left column checkboxes **Tooltips**, **Target Lines**, **See Hidden Objects**; right **Scroll Rate** slider. Checked boxes are red-edged dark squares with amber/yellow round lamps, not modern tick marks. Dropdowns/sliders have thin red rectangular borders and red thumbs; dropdown arrows are amber. | Base RA2, patch unknown; **high**, S3. |
| Audio: three side-by-side sliders **Music Volume**, **Sound Volume**, **Voice Volume**, each with numeric value at right. Manual range **0–10**; click and drag changes volume. No playlist or audio-device picker is shown on this Options page. | Base RA2, patch unknown; **high**, S3 + S1 p. 10. |
| Root has **Main Menu** as its visible return action; there is no root Apply/Save/Cancel button in S3. Exact persistence timing, Escape behavior and resolution confirmation are not established by a still. | Base RA2, patch unknown; **high** for visible buttons, **unverified** for persistence timing, S3. |
| **Options → Keyboard**: Category dropdown → choose command → Press New Hotkey → Assign; **Reset All** restores defaults. | Base retail RA2, **high** for operation from S1 pp. 10–11; exact geometry not captured. |
| **Options → Network** opens **IPX Options**. Small form in the center-left: adapter dropdown first, **Socket Number** field second, **Destination Network** field third. Chinese capture shows a top-right confirmation button. | Base RA2 Chinese, exact subbuild unverified; **medium**, S6; route/field names independently supported by S1 p. 11 and S7. Exact English confirm/cancel labels and return behavior were not independently captured. |

## LAN before and after creating a game

| State / operation | Version / confidence / evidence |
|---|---|
| **Main Menu → Network → Lobby**. Network is the LAN route; Internet is the separate Westwood Online route. No server-address entry or Internet account is shown in this native Lobby. | Retail RA2 manual, **high**, S1 p. 8; Lobby capture attributed **1.006**, **high** for what is visible, S5. |
| Before hosting/joining, **Your Name** is an editable-looking field near the top. Two boxed lists: **Players** left and **Games** right. Games contains **Lobby** and a room such as **Martasw's Game**; the selected room row is solid bright red. A wide chat history is below, with scrollbar on its right and a single-line input along its bottom. | RA2 **1.006** attribution, **high** for visible geometry, S5. |
| Right rail shows **New**, **Join**, and bottom **Main Menu**. An unlabeled checkbox and small crossed-circle icon appear near the name area; their exact meaning is not proven by this still. | RA2 **1.006** attribution, **high** for visible controls, **unverified** for icon semantics, S5. |
| Host route: **Network → Lobby → New → network game setup**. Other machines remain in the Lobby and can select the newly advertised room. The source account explicitly tells the host to choose New; the retail manual says starting a new network game brings up setup options. | Base retail manual + native **1.006** procedure, **high** for route/role distinction, S1 p. 11 + S5. **No direct frame of the transition or intermediate dialog was obtained**; do not assert a room-name/password modal. |
| Guest route: **Network → Lobby → select host's game → Join → host's game**. Host changes settings/map/mode; guests choose side/country and color. | Native **1.006** tutorial procedure, **medium-high**, S5. Guest readiness/Accept button and exact host/client exit behavior remain unverified. |
| After creating/joining, setup is a different state from the two-list discovery Lobby. The manual describes player/country/color rows, game options and battlefield selection. The saved setup still is skirmish and must only illustrate shared controls, not prove LAN room chat, Ready controls or player columns. | Retail RA2, **high** for separate setup/state and documented controls, S1 pp. 11–14; **medium** for transferring S4 geometry to LAN. |
| **Internet → Westwood Online → Create Game** has documented player-count/password/tournament choices. These belong to Internet play and are **not evidence for a LAN “New” dialog**. | Retail RA2, **high**, S1 printed pp. 52–53. |

## Network / multiplayer setup controls

| Confirmed behavior / layout | Version / confidence / evidence |
|---|---|
| Player rows use player identity, country/faction flag and dropdown, and color selector; the manual lists **nine countries** or Random. S4 visibly has four player rows. | Retail RA2 + base-game skirmish capture (patch unknown), **high**, S1 pp. 11–12 + S4. Do not infer a four-player LAN maximum from this map. |
| Shared settings: **Game Speed, Credits, Unit Count, Short Game, Super Weapons, Build Near Ally, MCV Repacks, Crates Appear**. S4's label is **Build Off Ally ConYards** (disabled there). The manual explains Short Game as structures-only defeat versus eliminating units too. | Retail RA2, **high**, S1 p. 12; captured wording/disabled style **high** for S4, patch unknown. |
| **Customize Battle → choose game mode and battlefield**. Choose mode first because it filters valid maps. **Start Game** starts with selected settings. S4 right rail shows map preview at top, mode/map information, **Start Game**, **Customize Battle**, bottom **Back**. | Retail RA2 operation **high**, S1 pp. 12–14; skirmish geometry **high** / LAN transfer **medium**, S4. |
| Original listed modes: **Battle, Free For All, Unholy Alliance, Megawealth, Land Rush, Meat Grinder, Naval War, Cooperative**. | Retail RA2 manual, **high**, S1 p. 13. Availability varies by map/mode; do not substitute a modern launcher mode list. |

Remaining evidence gaps are specific: a directly inspected vanilla **post-New LAN host screen**, guest Ready/Accept behavior, exact English IPX confirmation labels, and the resolution-change confirmation/persistence sequence. This reference set establishes the visible main menu, complete Options root and LAN discovery screen; it deliberately keeps these gaps separate from confirmed facts.
