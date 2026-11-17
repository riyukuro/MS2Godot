# Native UI (pilot)

UI screens drawn by Godot controls, with no Flash movie running at run time. The art is taken from the player's own
client files when a scene starts (the Gfx movies' atlases and shapes, read through `UiAssets` and baked by `NativeArt`).
Nothing from the client is stored in this folder. The layout lives in the scenes. Positions are the movies' numbers, in
stage units, and art is named by its symbol or export name. The behaviour is ported from the client's dialog code and
the movies' ActionScript.

Widgets:

- `NSkin`: static art; nine-patch when resized.
- `NButton` and `NCheckBox`: CLIK button states and toggle.
- `NTextInput`: a text field, with its default, focused and disabled frames.
- `NScrollBar` and `NScrollingList`.
- `NWindow`: alignment, OpenCloseTween and drag.
- `NTooltip`: TooltipLabel.
- `NMenuButton`: the game menu's SystemDropdown.

Screens:

| Scene | Shows | Args after `--` |
|---|---|---|
| `native_login.tscn` | the login dialog (uilogindialog) | `--native-shots DIR` |
| `native_hud.tscn` | the field with the quick slots and orb, EXP bar and game menu | `--native-hudshot DIR` |
| `native_widgets.tscn` | a test bench for the widgets | `--native-shots DIR` |

These scenes do not touch the Flash UI path (`LoginScene`, `HudScene`).
