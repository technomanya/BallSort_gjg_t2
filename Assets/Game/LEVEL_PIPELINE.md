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
