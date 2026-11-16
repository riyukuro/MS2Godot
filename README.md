# MS2Godot

A MapleStory 2 client written in Godot 4 (C#). The parity target is the restored original client.

The project ships no game data. You bring your own MapleStory 2 client; MS2Godot reads its `.m2d` packs at
run time.

Current state: a field walk. The main scene loads one field (Tria, `02000001_tw_tria`, by default) and places
the female character at the field's player spawn point. She walks, runs and climbs steps over the field's
collision.

Controls: WASD or the arrow keys move (relative to the camera), Shift walks instead of running, right mouse drag
turns the camera, and the wheel zooms.

The field comes from these packs:

- `Resource/Exported.m2d`: the field entity set (`xblock/<field>.xblock`) and the model defaults (`flat/`).
- `Resource/asset-web-metadata.m2d`: maps an entity's `urn:llid:` asset id to a model path.
- `Resource/Model/Map.m2d`: the models, their textures and their PhysX collision meshes.
- `Xml.m2d`: `mapxblock/<field>.xml` (fog).

Cubes without a PhysX mesh get a generated box collider. Known gaps: no character customisation (white skin),
no swimming, some banner textures are white, and props without PhysX data (such as low fences) have no collision.

The earlier pilot viewer (female model, idle and walk on a ground plane; keys `1` idle, `2` walk) is still
`game/main.tscn`.

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

## Licence

AGPL-3.0, see [LICENSE](LICENSE). The m2d archive reader is the NuGet package `Maple2.File.Parser.Tadeucci`
(GPL-3.0-or-later).
