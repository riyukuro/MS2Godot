# MS2Godot

A MapleStory 2 client written in Godot 4 (C#). The parity target is the restored original client.

The project ships no game data. You bring your own MapleStory 2 client; MS2Godot reads its `.m2d` packs at
run time.

Current state: a field walk. The main scene loads one field (Tria, `02000001_tw_tria`, by default) and places
the player character at the field's player spawn point, dressed in a new character's starter look. It walks,
runs and climbs steps over the field's collision.

Controls: WASD or the arrow keys move (relative to the camera), Shift walks instead of running, and the wheel
zooms.

The camera follows the client's default field camera: a fixed direction, a field of view, and a pitch and target
height that change with the zoom distance. The values come from the field's entry in `table/fielddata.xml`, or
the client's defaults when the entry has none.

The field comes from these packs:

- `Resource/Exported.m2d`: the field entity set (`xblock/<field>.xblock`) and the model defaults (`flat/`).
- `Resource/asset-web-metadata.m2d`: maps an entity's `urn:llid:` asset id to a model path.
- `Resource/Model/Map.m2d`: the models, their textures and their PhysX collision meshes.
- `Xml.m2d`: `mapxblock/<field>.xml` (fog) and `table/fielddata.xml` (camera).

Field meshes are lit with the field's ambient and directional light entities, the way Gamebryo's standard
material does it: in gamma space, with the entity's material colour, vertex colours and alpha blending from the
NIF (`game/shaders/field_lit.gdshader`).

Collision follows the client:

- The NIF's own PhysX meshes.
- For cubes without them, the box the client generates from `GeneratePhysXDimension`.
- Boxes for `PhysXWhitebox` entities.

Props without PhysX data, such as the low fences, have no collision in the client either, so the character can
step onto the water surface.

Sound comes from `Sound/MS2Sound.fev` and the `MS2Sound_*.fsb` banks next to it (FMOD Designer 4 project and FSB5 banks of
MPEG layer II). `src/Ms2.Audio` reads them without Godot: music cue id (the `<bgm id>` of `table/fielddata.xml`) to bank
sample, event path to sound definition to bank sample, volume and 3D range, and decodes the payloads to PCM. `game/audio` plays:

- the field's bgm cue, looped (an intro sample plays once first);
- the field's sound entities (flat mixin `MS2Sound`, `soundEventID`) as looped positional sounds with the event's min / max distance;
- footsteps: the ground cube's `MapAttribute` selects `groundeffect/<ground>.xml`, the action (walk, run0, run1, land) the sound
  events. The player's KF files carry no footstep text keys, so steps run at two per animation cycle;
- UI sounds by the client's key (`table/systemsound.xml`: `FieldAudio.Instance.PlayUi("button_click")`, `dialog_open`, ...).

`game/audio_probe.tscn` is a headless check of these paths. Not done yet: music parameters and bridge segments, voice banks per
language, event groups beyond the event category.

Known gaps:

- No swimming.
- Some banner textures are white.
- No bloom pass. The client's is small: a bright pass of max(c - 0.27, 0) / (that + 30) over a quarter-size
  copy, blurred and added twice to the scene, with no tone mapping. So it does not explain why lit floors come out
  about 10-20% darker than in the client.
- No cast shadows on field meshes.
- No texture animation (the water flipbook).
- The water shows the inner faces of its stacked cubes.

The pilot viewer (the player character on a ground plane; keys `1` idle, `2` walk) is still `game/main.tscn`.

### Character look

`game/scripts/character/` builds the local player:

- `StarterLook` picks the look a new character gets from `table/defaultitems.xml`. That is the first face, hair and
  ears for the gender, and the job's starter outfit (CL, PA, SH). An item fits a gender by its itemdata
  `genderLimit`.
- Colours come from `table/colorpalette.xml`. Each item uses its item model's `defaultColorIndex` row, and the skin
  uses row 0 of palette 10000.
- `net/EquipApplier` attaches the items. An item whose itemdata has `tool itemPreset` uses that item's model, as
  the female copies of the starter outfits do.

Characters are drawn with `game/shaders/character_lit.gdshader`, after the client's MS2 character materials:

- The `_C` control map blends the texture towards three dye colours. Skin parts take the skin colour pair; items
  take their dye.
- Lighting uses a squared half-lambert sun term, a Fresnel rim, the sun's ambient colour on the unlit side, and a
  red scatter band on skin.
- The light direction comes from the camera.
- The material values come from the NIF's extra data (`OverrideColor0..2`, `FresnelBoost`, `FresnelExponent`,
  `ColorBoost`).

Not done: normal and gloss maps, hair anisotropy, face decals (FD), weapons.

Command line, for the field and the viewer:

| argument | effect |
|---|---|
| `--gender male\|female` | default female |
| `--job <code>` | default 10, knight |
| `--skin-color <row>` | palette row for the skin |
| `--hair-color <row>` | palette row for the hair |
| `--eye-color <row>` | palette row for the eyes |

## Layout

| path | what |
|---|---|
| `src/Ms2.Formats` | Class library with no Godot dependency: m2d pack access, a `nif.xml`-driven reader for Gamebryo NIF/KF files (version 30.2.0.3), the MS2 model conventions (skeleton, skinned meshes, KF animation), NIF PhysX collision meshes, asset id lookup, and field entity sets (xblock + flat defaults). |
| `src/Ms2.Swf` | Class library with no Godot dependency: a Scaleform GFX / SWF reader (CFX, CWS, FWS, GFX) for UI layout: stage, shapes with bitmap fills, sprites and display lists, edit text, buttons, scale9 grids, symbol classes, imports, GFx external images and sub images. `LayoutJson` turns a movie into a JSON layout. |
| `tools/GfxLayout` | Command line tool that writes that layout JSON for a movie in `Resource/Gfx.m2d`. |
| `src/Ms2.Swf/Abc` | ABC (ActionScript 3 bytecode) reader: constant pool, multinames, classes, traits, method bodies, opcode decoder, and a rough decompiler to pseudo C#. |
| `tools/AbcProbe` | `stats` (opcode histogram, classes, external references, shared code over every movie), `dump <movie>` (pseudo C# of the classes), `decompile-all`. |
| `tests/Ms2.Swf.Tests` | NUnit tests. The ones that need game data read `MS2_DATA` and skip when it is unset. |
| `game/` | The Godot project. It builds `Skeleton3D`, skinned `MeshInstance3D` and `AnimationPlayer` nodes at run time from `Ms2.Formats` data, the field as MultiMeshes with static colliders, and the player as a `CharacterBody3D`. |

MS2 is Z-up and Godot is Y-up. Each model sits under a node rotated -90 degrees about X. File units are kept
1:1 (the female body is about 100 units tall).

## Requirements

- Godot 4.7 .NET edition (`godot-mono`).
- .NET SDK 10.
- Your own MapleStory 2 client: the `Data` folder that holds `Resource/Model/*.m2d`.
- niftools `nif.xml` (BSD), from https://github.com/niftools/nifxml. It is not included in this repository.
  The reader interprets it at run time.

## Settings

The project contains no local paths. Set them in one of these places (checked in this order):

1. The environment variables `MS2_DATA` (the client's `Data` folder) and `MS2_NIFXML` (the path to `nif.xml`).
2. `game/local.cfg` (gitignored). Copy it from `game/settings.example.cfg`.
3. `user://settings.cfg`, the Godot user data folder. It uses the same format.

## Build and run

```
cd game
dotnet build
godot-mono --path . --import --headless     # first run only: creates .godot/
MS2_DATA=/path/to/Data MS2_NIFXML=/path/to/nif.xml godot-mono --path .
```

Another field: `godot-mono --path . -- --field <xblock name>` (the file name without `.xblock`).

Screenshot mode runs a scripted walk, saves frames to a folder and then quits:

```
godot-mono --path . -- --shots /some/dir
```

The pilot viewer: `godot-mono --path . res://main.tscn` (it takes the same `--shots` argument).

## GfxLayout

```
MS2_DATA=/path/to/Data dotnet run --project tools/GfxLayout -- uilogindialog uicharacterlistdialog --out layouts/
dotnet run --project tools/GfxLayout -- --all --stats          # parse every movie in Gfx.m2d, print tag coverage
dotnet run --project tools/GfxLayout -- --file some.gfx        # a loose file
```

One JSON per movie. Lengths are pixels (twips / 20); matrices carry `x`,`y` (translation) and `a`,`b`,`c`,`d` (omitted when 1/0).

| key | content |
|---|---|
| `stage`, `fps`, `frames`, `background`, `documentClass`, `exporter` | movie header |
| `imports` | shared movies it imports (`gfxfontlib.swf`, `component_v1.swf`, ...) |
| `images` | external DDS files (GFx tag 1009) with their pixel size; `subImages` are the rectangles cut from them (tag 1008) |
| `symbols` | by character id: `kind` (shape, sprite, text, button, ...), AS3 `class` (SymbolClass), `export` name, `bounds`, `scale9`. Shapes list `bitmaps` (sub image id, atlas rect, file, fill matrix; scale 20 means 1:1). Text: font class or id, size, colour, align, margins, `variable`, initial text, flags. Buttons: state records. Sprites: `timeline` |
| `timeline`, `root` | frame labels and `children`: one entry per display-list lifespan (`depth`, `id`, `name`, `first`/`last` frame, `matrix`, `color`, `clipDepth`, `blend`, `filters`, `moves` for later changes) |
| `namedInstances` | flat list of `path` (`m_dialog.m_inputID`), symbol id and bounds in stage space, reached through named children (what the C++ side looks up by name) |
| `coverage` | tags parsed / skipped / failed, ABC bytes, problems |

Movies import shared symbols (`component_v1`, `commonResource`, ...) with an empty asset list and look the classes up by name at run time. When a movie's code names a class (or a string constant equal to a class name) that an imported movie exports, GfxLayout copies that symbol, everything it needs and its images into a `library` object, keyed by the imported movie. `--no-merge` skips it.

Not decoded: shape outlines (bounds and bitmap fills only), font glyphs, ActionScript bytecode (DoABC is skipped; its size is reported), sounds. Scene movies such as `uiscenelobbyselect` have no display list: they are a document class only, and the layout is in the dialog movies they load.

## UI runtime (game/ui)

`game/ui` builds Scaleform UI movies as Godot nodes from the player's own `Gfx.m2d`: `UiAssets` (layouts via Ms2.Swf, DDS atlases, the flash string table from `Xml.m2d`), `MovieClipNode` (display list, frame labels, frame state), `ShapeNode` (atlas sub-images, nine-sliced inside scale-9 sprites), `TextNode`, and CLIK-like widgets in `UiWidgets` (button states, toggle, checkbox, text input, label). Text uses the game's own UI fonts: `Ms2.Swf` reads the DefineFont3 outlines from `gfxfontlib` and writes a TrueType file at run time (`TtfWriter`), Godot loads it as a `FontFile`; a system font (Noto Sans CJK / Noto Sans) covers missing glyphs. Shapes with solid vector fills are drawn as polygons, state changes and popups play the timeline at the movie's frame rate. `charselect.tscn` is the character select dialog filled from Ms2.Net's `CharacterEntry` (stub data, no network). `UiBridge` carries what a dialog tells the game (the movies' `ExternalInterface.call`); nothing touches a network.

`tools/UiContract` writes the C# contract of a movie (callbacks both ways, named instances with their CLIK base, component inspector values) from its ABC, layout and, for the host-called methods, the C++ dialog source: `UiContract uilogindialog --cpp path/to/uilogindialog.cpp --out game/ui/contracts`. `LoginDialogLogic` is the login dialog ported by hand against that contract.

```
cd game && dotnet build
MS2_DATA=/path/to/Data godot-mono --path . res://ui/login.tscn                          # Tab, Enter, buttons, checkbox
MS2_DATA=/path/to/Data godot-mono --path . res://ui/login.tscn -- --shots DIR --clear "#e8c070"   # shots, then quit
```

## Licence

AGPL-3.0, see [LICENSE](LICENSE). The m2d archive reader is the NuGet package `Maple2.File.Parser.Tadeucci`
(GPL-3.0-or-later).

## Offline replay

`game/net/Replay.tscn` plays a recorded session (MapleShark2 `.msb`, your own local file) through the real receive path, with no socket:
`ReplayStream` (a synthesized handshake and each recorded server packet as an encrypted frame, released at its recorded time) into
`MapleChannel` and `ClientSession` into `FieldTracker`. The scene loads the field the recording enters, spawns every player from
`FieldAddUser` (a stand-in body, male or female by the packet), moves them by `UserSync`/`RideSync` (the local player by the recording's
own movement packets) and removes them on `FieldRemoveUser`.

```
godot-mono --path game res://net/Replay.tscn -- --recording FILE.msb [--speed 1] [--start SEC] [--follow self|NAME|center] \
    [--dist 1000] [--out DIR --shots "SEC[:FOLLOW],SEC,..."]
```

Combat in the replay: skill uses play the skill's motion on the body (skill xml sequence name -> the body's kf or the npc's kfm sequence) and its effect nif, damage numbers float over targets, npcs get name tags and an hp bar that follows the Stat packets, and an npc at 0 hp plays its death sequence until it is removed. Effects are drawn from NiMesh geometry only (additive, faded over the effect's duration); particle systems are not drawn.

With `--out` it saves a screenshot when the recording reaches each time, compares every player's position with the recorded coordinates
(exit code 3 on a mismatch) and quits at the end of the recording. `MS2_REPLAY=FILE.msb dotnet test tests/Ms2.Net.Tests` runs the same
pipeline without Godot and compares final positions, yaw and move counts with an independent walk over the recording.
