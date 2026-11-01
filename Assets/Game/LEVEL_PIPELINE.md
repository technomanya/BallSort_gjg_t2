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
| `Scripts/Gameplay/GameManager.cs` | `StartLevel` uses `ForAttempt`: attempt k plays seed k, then a random seed that is not the last one; a win resets the counter. |
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
4. Point `GameManager.levelPackResource` at `data/<name>`.

Hidden tubes in generated packs: set *Hidden tubes per level* (0-4), *Hidden tubes from level* and *every N levels*.
`LevelDesign.HideTubes(count, seed)` covers that many filled tubes at random, never two side by side in the same row. Hidden tubes make
boards much harder (most fills come out unsolvable or high-bucket), so when no fill reaches the target the level is
generated again with one hidden tube fewer; the log shows `/<n>h` and `(wanted <m>h)` when it had to drop some.
Scriptable via `PackSettings.HiddenTubes / HiddenFromLevel / HiddenEvery`.

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
| `Scripts/LevelEditor/LevelEditorManager.cs` | `EditorManager` + `EditorInputSystem`: attaches itself to any scene with a `GameManager`, shows the layout on the real board (`GameManager.Preview`) with its controls over it in the game's UI skin; undo, Test, Check, Save (generate + write to the pack). |
| `Scripts/LevelEditor/EditorUI.cs` | Runtime uGUI kit: the `Resources/UISkin` sprites when present, procedural sprites otherwise (also used by the mini games). |
| `Scripts/Gameplay/GameManager.cs` | `Preview(tubes, capacity)` shows a layout on the board without a game (grey `?` balls for luck slots; `MinCameraSize` keeps the editor controls clear of the tubes). `SetOverrideLevel(level)` plays an editor level (with its seeds) in place of the pack slot; `null` goes back. |

**Access.** Available only in the Unity Editor and in development builds (File > Build Profiles > your platform >
tick **Development Build**); a release build never attaches it. Open it with **F2** or **a tap on the level pill**;
it opens on the level being played. Esc / F2 closes it (or returns from a test).

**Editing.** The board is the game view: the real tubes, balls and background, hidden balls as grey `?` balls.
Pick a value in the palette at the bottom (the colours in use with `used/height` counters, `?` with
`placed/needed`, and the eraser), then tap a ball to recolour it or tap the empty glass of a tube to fill its free
slots (eraser: remove the ball / empty the tube). *TUBES*, *HEIGHT* and *COLOURS* steppers size the level: *TUBES +* adds a
full `?` tube of a new colour (so Save fills it and Reset keeps it; tap it with the eraser and lower *COLOURS* to make it a spare
tube instead), *TUBES −* removes the last tube and, if it had balls, a colour;
*Reset* makes every colour a full `?` tube again; *Undo* (Ctrl+Z; Ctrl+Y redoes) steps back. The pill at the top
shows the state: what is still wrong, or ready.

**One-way tubes.** The palette's *IN* and *OUT* tools (the arrow sprite from `Art/UI/NewUI`) make a tube one-way;
tapping it again with the same tool makes it normal. An **in-only** tube (arrow pointing down under the glass)
accepts balls but never gives one back, and starts empty: the tool empties it and balls cannot be painted into it.
An **out-only** tube (arrow pointing up) gives balls away but accepts none, and starts with balls: an empty tube
becomes a full `?` tube. `EditorLevel.Kinds` / `TubeSpec.Kind` / `BoardState.KindOf` carry the rule
(`TubeKind`: 0 normal, 1 in-only, 2 out-only), levels store it as the optional `tube_kinds` array, and the solvers,
the playout bot and the game's `CanImport` all respect it. The win rule is unchanged: every tube empty or full of one colour.

**Hidden tubes.** The palette's *HIDE* tool (the cover `Art/Tubes/Hidden tube.png`, `GameManager.hiddenTubeSprite`) covers a
tube; tapping it again uncovers it. A hidden tube keeps its balls (or starts empty) but nothing goes in or out and its balls cannot
be seen until a **tube physically next to it** (left or right in the same row; the last tube of a row and the first of the next are *not* neighbours, see `TubeRows`) is **completed**: then the cover fades and the
tube plays as a normal tube for good (undo does not close it again). A covered tube is never a finished tube, so a hidden tube
with balls must be opened before the level is won; an opened tube that is already complete opens its other neighbour in turn.
The editor preview draws the cover see-through so the balls under it can be painted. Levels store it as kind 4 in `tube_kinds`;
`TubeKind.Hidden`, `BoardState.OpenHiddenTubes` (run after every move that completes a tube, and when a board is built),
`Tube.Open` / `GameManager.OpenHiddenTubes`, the audit, the solvers and the rater all carry the rule.

**Hidden balls.** A `?` cell is a hidden ball: the filler picks its colour (every colour still ends up with
`capacity` balls, so the `?` counter must reach `placed/needed`), and the game shows it as a grey `?` until it becomes
the top ball of its tube (a finished tube reveals everything). A run of same-coloured balls stops at a hidden ball, so
only the visible run moves. The level JSON carries the positions in the optional `hidden` array (top-first, parallel to
`tubes`, 1 = hidden); the solver and the rater see the real colours. The bot plays the shorter run and re-solves from
the live board when a hidden ball cut a solver move.

**Heavy balls.** The palette's *HEAVY* tool (the cannonball weight `Art/Balls/NewBalls/Cannonball.png`, drawn over the ball by
`GameManager.heavyBallSprite`) puts the weight on **any ball**, whatever its colour or place in the tube (tap the ball, or the
glass above the stack for the top ball; tap again to take it off). The weight only matters to the player: a heavy ball is a
normal coloured ball for every rule but the move rule - it moves **alone** and only into an **empty** tube, so once moved it
sits at the bottom, and a run of balls above a heavy ball moves without it (the run stops at the weight). Balls of its colour
stack on it as usual and a tube completed with a heavy ball in it is a finished tube. Levels store it as the optional per-tube
`heavy` array of **bit masks**, bit *s* = the *s*-th ball from the bottom is heavy (so `1` = the bottom ball, the old form);
`EditorLevel.HeavyFlags` / `SetHeavy(tube, slot, on)`, `TubeSpec.HeavyMask`, `BoardState.IsHeavyAt` / `IsTopHeavy` /
`CanImport` / `TopRun`, `Ball.SetHeavy`, `Tube.CanImport` / `TopBallSameCount`, the audit and the solvers all carry the rule
(the bit travels with the ball when it moves).

**Actions.** *TEST* plays the generated seeds (retry = next seed) or one random fill of the hidden balls, with a
`< EDITOR` button to return.
 *Check* rates one fill (solvable, moves, bot bucket). *SAVE* runs `LevelGenerator` off the main thread for
five seeds of the *DIFFICULTY* bucket (local stand-in for Magic Sort's `SolveLevelRequest` backend; a layout
without `?` is rated once) and writes them into the pack the game plays (`GameManager.levelPackResource`, e.g.
`Resources/data/levels.json` in the Unity Editor; on a device a copy under `persistentDataPath/LevelEditor/`).
Every layout belongs to a pack slot (`EditorLevel.Slot`, the unique level number: what the slot shows the first time
round, before the pack loops; level 106 of a 100-level pack is slot 6). Save replaces that slot; a layout without a slot
(a new level, a pasted one) is appended and becomes the next number. The loaded pack is updated in memory too, so the
game and the bot play the saved level at once.

**Level row.** Under the size steppers: the slot being edited (`LEVEL 6 / 100`, or `NEW LEVEL 101`), a number box and
*GO*. GO loads that slot into the editor (undoable); a number past the end of the pack starts a fresh layout with the
current sizes that Save appends as the next level. Closing the editor after GO plays the level that was edited.

**Play as bot.** The *PLAY AS BOT* button (pack row above the actions) hands the loaded pack to `Scripts/LevelEditor/LevelBot.cs`,
which plays it from level 1 on the real board: every level is audited and solved on a worker thread
(`Scripts/Core/LevelAudit.cs`: ball counts, gaps, sizes, one-way tube rules, solvability with an "Add Tube only" note,
stored `steps`, duplicate ids; the other seeds of a slot are solver-checked in the background), then the bot taps the
solution's tubes through `GameManager.OnTubeClick` and waits for the animations. Rule mismatches between solver and game
(a rejected move, a "no moves" popup on a solvable line, a solution that does not win) are reported too. The bar at the
bottom shows progress with a speed toggle (x1 / x4 / x16 via `Time.timeScale`) and *STOP*; the report that follows lists
every issue (tap one to load that fill into the editor) and *COPY* puts the full text on the clipboard (it is also logged).
`GameManager.IsBotPlaying` ignores taps and keeps wins from saving progress while the bot plays.

**Share.** The *SHARE* button (pack row, next to PLAY AS BOT; `Scripts/LevelEditor/LevelEditorManager.Share.cs`) writes the
whole pack the editor saves into, seeds included, as `levels.json` somewhere the designer can reach: on Android into the
public *Download* folder through MediaStore (no storage permission on Android 10+; older devices get a plain file in
Downloads) and then the system share sheet opens with it (WhatsApp, mail, Drive...); in the Unity Editor and on desktop it
lands on the Desktop and the JSON is also put on the clipboard. A copy is always kept at `persistentDataPath/LevelEditor/levels.json`.
The file is a normal pack (`stage_id` + `levels`), so it drops straight into `Resources/data/` of another checkout.
