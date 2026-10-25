# Android touch controls

The existing virtual pad feeds the native player's configured actions. The
left stick moves with analog strength; the right stick turns using an angular
velocity and physics delta. A activates, B opens the Pip-Boy, X draws/reloads
and holds to holster, Y jumps, RT attacks, LT aims and LB changes the view.
Start opens pause. Source control locks still apply during the opening.

Modal screens use A to confirm, B/Start to go back and the left stick for
repeating directional navigation. Direct taps and drags are forwarded as one
mouse pointer for the original projected menu surfaces; a second finger cannot
steal that pointer. The pad owns touch conversion while attached and restores
the previous engine setting when removed. Controls remain alive when the tree
is paused. Gameplay touches do not also generate mouse fire or menu clicks.

Each stick has one finger owner. Buttons retain their state until their final
finger releases. Focus loss, canceled gestures, viewport resizing and modal
transitions clear the pointer, sticks and held actions. Cancellation does not
complete a pending reload or keep automatic fire running after a menu closes.

V.A.T.S., waiting, player sneak, world flashlight, native prop grab and Xbox
hotkey/ammunition selection remain unimplemented gameplay integrations. RB and
Back are not reassigned to unrelated mechanics. Xbox menu repair/modification
and full camera orbit/hold behavior also remain incomplete. This feature is
not evidence of whole-game or Xbox parity.

Run the native synthetic audit with the built Godot Mono project:

```sh
godot --headless --path runtime res://tools/NativeTouchInputAudit/NativeTouchInputAudit.tscn
```

The audit is part of Test-GodotRuntime.ps1. It does not load owned game data or
replace Android hardware acceptance. Hardware checks must cover new game name
entry, character creation, ordinary movement/aim/fire/reload, opening and using
the Pip-Boy, pause/save/load, focus loss and continued input after a door load.
