# Level pipeline (generator + rater)

Offline counterpart of the Magic Sort production pipeline (design → random fills → bot rating → seed lists),
rebuilt from the decompiled reference in `D:\Tools\decon\work\MagicSort_0.0.3420\LEVEL_SYSTEM_REPORT.md`.

## Pieces

| File | Role |
| --- | --- |
| `Scripts/Core/LevelDesign.cs` | `LevelDesign` / `TubeSpec`: the designed part of a level (tube count, capacity, colours, empties, fixed colours). Presets: `Deterministic`, `Luck`, `PartialLuck`, `Empty`. `ProgressionCurve`: board size and target difficulty per level index (Magic Sort curve: hard every x4, super-hard every x9 from level 14/19). |
| `Scripts/Core/LevelFiller.cs` | `LevelFiller.Fill(design, seed)`: deterministic random fill of the luck slots (every colour gets exactly `capacity` balls). |
| `Scripts/Core/LevelRater.cs` | `LevelRater.Rate(board, budget)`: greedy solver + A* (admissible heuristic, weighted fallback) for solvability and a short solution, then `PlayoutBot` (random player with a 70 % preference for stacking moves) whose **loss rate** is the difficulty score. `BucketUpperBounds` maps it to buckets 1..7. |
| `Scripts/Core/LevelGenerator.cs` | `LevelGenerator.Generate(design, options)`: fill → rate → keep fills of the wanted buckets (exact, then nearest within `FallbackTolerance`). Seeds are named `<design>_difficulty<bucket>-<variant>`. `SeedSelector`: Magic Sort's attempt → seed rule. |
| `Scripts/Core/LevelData.cs` | Pack format. Generated levels add optional fields: `difficulty`, `variants` (extra seeds), `dlb_variants` + `dlb_cap` (easier seeds after N failed attempts), `hard`, `super_hard`. `LevelData.ForAttempt` picks the fill to play. |
| `Scripts/Gameplay/GameMgr.cs` | `StartLevel` uses `ForAttempt`: attempt k plays seed k, then a random seed that is not the last one; a win resets the counter. |
| `Editor/LevelPackGeneratorWindow.cs` | Menu **BallSort > Level Pack Generator**: generate a pack for a level range (curve or fixed size), or rate an existing pack. Output: `Resources/data/<name>.json`. |

## Calibration

The rater was calibrated on 595 blocker-free Magic Sort seeds with known bot buckets. Of all measured
signals (optimal solution length, detour over the lower bound, A* effort, greedy overshoot, board size) only
the playout loss rate tracks the bot's bucket (+0.87 overall, +0.48 within one design). Bucket means of the
loss rate: 0.22 / 0.54 / 0.67 / 0.76 / 0.93 / 0.98 / 1.00. The thresholds `0.50, 0.60, 0.70, 0.87, 0.975, 0.995`
reproduce the bot bucket exactly for 73 % of the seeds and within one bucket for 94 %.
Buckets 2 and 3 overlap heavily in the reference data as well; treat them as "medium".

Throughput (Fast budget, one thread): levels 1-60 with 5 seeds each in ~35 s. Hard/super-hard slots need
more fills (bucket 5 is ~1 in 20 random fills of an 8-colour board).

## Usage

1. Open **BallSort > Level Pack Generator**.
2. Set the level range, seeds per level (5 like Magic Sort), keep *Use progression curve* on.
3. Generate. The window logs each level's seeds and buckets; the pack lands in `Resources/data/`.
4. Point `GameMgr.levelPackResource` at `data/<name>`.

Custom designs: build a `LevelDesign` in code (e.g. `TubeSpec.Deterministic(4, 1,1,2,2)` for hand-placed
colours, `TubeSpec.Partial(4, 3,0,0,0)` for a fixed bottom ball) and call `LevelGenerator.Generate`.

Calibration harness (console, rates Magic Sort seeds / runs the generator outside Unity):
`C:\Users\Yavuz\AppData\Local\Temp\claude\...\scratchpad\calib\` (build output `D:\Tools\decon\tmp\calib-build\`).

## In-app level editor

Ball-sort counterpart of Magic Sort's `Modules.LevelEditor.WaterSort` editor scene: it lives in the shipped
game, players never see it, and designers switch into it from the running build.

| File | Role |
| --- | --- |
| `Scripts/LevelEditor/EditorLevel.cs` | The edited layout: tubes as bottom-first cells (colour 1..N, `-1` = `?` luck slot). Presets follow from the cells (all colours = Deterministic, all `?` = Luck, mixed = PartialLuck, none = Empty). `ToDesign()` feeds `LevelFiller` / `LevelGenerator`; `Validate()` is the `IsLevelValid` check (every colour exactly `capacity` balls, free space, not already solved). |
| `Scripts/LevelEditor/LevelEditorManager.cs` | `EditorManager` + `EditorInputSystem`: builds its own UI at runtime, attaches itself to any scene with a `GameMgr`, undo/redo, Test, Generate, Save/Open, Copy/Paste, To pack. |
| `Scripts/LevelEditor/EditorUI.cs` | Runtime uGUI kit with procedural sprites (no prefabs or Resources needed). |
| `Scripts/Gameplay/GameMgr.cs` | `SetOverrideLevel(level)` plays an editor level (with its seeds) in place of the pack slot; `null` goes back. |

**Access.** Available in the Unity Editor and development builds, or in a release build after
`PlayerPrefs.SetInt("ballsort.level_editor", 1)`. Open it with **F2** or **five quick taps on the level pill**.
Esc / F2 closes it (or returns from a test).

**Tools.** Palette: colours (with `used/height` counters), `?` (luck slot, counter = placed/needed) and Erase.
*Paint* taps a slot (taps above the stack append; Erase removes a ball), *Fill tube* fills a whole tube
(`?` = luck tube, Erase = empty tube), *Move* swaps two tubes, *Delete* removes a tube, *+ Tube* adds one.
*Height* and *Colours* steppers set capacity and colour count; *Pack lv + Load* imports a level of the current pack.

**Actions.** *Test* plays the generated seeds (retry = next seed) or one random fill, with a `< Editor` button to
return. *Check* rates one fill (solvable, moves, bot bucket). *Generate* runs `LevelGenerator` off the main thread
for a wanted difficulty and seed count (local stand-in for Magic Sort's `SolveLevelRequest` backend); each seed can
be played from the list. *To pack* writes the seeds into `Resources/data/editor_pack.json` (in the Unity Editor;
on a device into `persistentDataPath/LevelEditor/`), replacing the entry with the same name; point
`GameMgr.levelPackResource` at `data/editor_pack` to play them. *Save/Open* keep layouts as JSON in
`persistentDataPath/LevelEditor/`; *Copy/Paste* use the clipboard and also accept pack levels and whole packs.
