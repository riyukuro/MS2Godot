# MS2Godot

A MapleStory 2 client written in Godot 4 (C#). The parity target is the restored original client.

The project ships no game data. You bring your own MapleStory 2 client; MS2Godot reads its `.m2d` packs at
run time.

Current state: a pilot. It loads the female character model and its idle and walk animations from
`Resource/Model/Character.m2d` and plays them on a ground plane. Keys: `1` idle, `2` walk.

## Layout

| path | what |
|---|---|
| `src/Ms2.Formats` | Class library with no Godot dependency: m2d pack access, a `nif.xml`-driven reader for Gamebryo NIF/KF files (version 30.2.0.3), and the MS2 model conventions (skeleton, skinned meshes, KF animation). |
| `game/` | The Godot project. It builds `Skeleton3D`, skinned `MeshInstance3D` and `AnimationPlayer` nodes at run time from `Ms2.Formats` data. |

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

Screenshot mode saves idle and walk frames to a folder and then quits:

```
godot-mono --path . -- --shots /some/dir
```

## Licence

AGPL-3.0, see [LICENSE](LICENSE). The m2d archive reader is the NuGet package `Maple2.File.Parser.Tadeucci`
(GPL-3.0-or-later).
