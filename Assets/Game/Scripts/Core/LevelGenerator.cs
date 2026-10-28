using System;
using System.Collections.Generic;

namespace BallSortGame.Core
{
    /// <summary>One concrete, rated fill of a design.</summary>
    public sealed class GeneratedLevel
    {
        /// <summary>"&lt;design&gt;_difficulty&lt;bucket&gt;-&lt;variant&gt;", the Magic Sort seed naming.</summary>
        public string Id;
        public string DesignName;
        public int Seed;
        public int Bucket;
        public int Variant;
        public BoardState Board;
        public LevelRating Rating;

        public List<List<int>> TubesTopFirst => LevelFiller.ToTopFirst(Board);

        /// <summary>Solution as [from, to] pairs (the level pack "steps" convention: the whole run moves).</summary>
        public List<List<int>> Steps
        {
            get
            {
                var steps = new List<List<int>>();
                if (Rating?.Solution != null)
                    foreach (var m in Rating.Solution) steps.Add(new List<int> { m.From, m.To });
                return steps;
            }
        }
    }

    public sealed class GeneratorOptions
    {
        /// <summary>Buckets wanted for the design, e.g. {2, 2, 2, 3, 3} = five seeds, three of bucket 2 and two of bucket 3.</summary>
        public List<int> WantedBuckets = new List<int> { 1, 1, 1, 1, 1 };
        /// <summary>Accept a fill when its bucket is within this distance of a wanted bucket (0 = exact).</summary>
        public int Tolerance = 0;
        /// <summary>After <see cref="MaxFills"/>, still-missing buckets are filled from the nearest rejected fills within this distance (0 = never).</summary>
        public int FallbackTolerance = 1;
        /// <summary>Random fills tried before giving up on the remaining wanted buckets.</summary>
        public int MaxFills = 300;
        public RaterBudget Budget = RaterBudget.Default;
        /// <summary>First seed number; seed k of the design is <c>BaseSeed + k</c>.</summary>
        public int BaseSeed = 1;
        public bool RejectSolvedTubes = true;
    }

    /// <summary>
    /// Builds rated seeds for a design: random-fill, rate, keep the fills whose bucket is wanted.
    /// This is the offline counterpart of the Magic Sort pipeline (editor layout -> server fills -> bot buckets).
    /// </summary>
    public sealed class LevelGenerator
    {
        readonly LevelRater _rater = new LevelRater();

        /// <summary>Progress callback: (fillsTried, accepted, wanted).</summary>
        public Action<int, int, int> Progress;

        public List<GeneratedLevel> Generate(LevelDesign design, GeneratorOptions options, out string error)
        {
            var result = new List<GeneratedLevel>();
            if (!design.Validate(out error)) return result;

            var remaining = new List<int>(options.WantedBuckets);
            remaining.Sort();
            var variantPerBucket = new Dictionary<int, int>();
            var rejectedByBucket = new Dictionary<int, int>();
            var rejected = new List<(int seed, BoardState board, LevelRating rating)>();
            var seen = new HashSet<ulong>();   // identical fills (tiny designs) are generated once
            int unsolvable = 0;

            void Accept(int seed, BoardState board, LevelRating rating)
            {
                variantPerBucket.TryGetValue(rating.Bucket, out int v);
                variantPerBucket[rating.Bucket] = ++v;
                result.Add(new GeneratedLevel
                {
                    DesignName = design.Name,
                    Seed = seed,
                    Bucket = rating.Bucket,
                    Variant = v,
                    Board = board,
                    Rating = rating,
                    Id = $"{design.Name}_difficulty{rating.Bucket}-{v}",
                });
            }

            int NearestWanted(int bucket, int tolerance)
            {
                int wantedIdx = -1, bestDist = int.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int d = Math.Abs(remaining[i] - bucket);
                    if (d <= tolerance && d < bestDist) { bestDist = d; wantedIdx = i; }
                }
                return wantedIdx;
            }

            for (int k = 0; k < options.MaxFills && remaining.Count > 0; k++)
            {
                int seed = options.BaseSeed + k;
                var board = LevelFiller.Fill(design, seed, out error, options.RejectSolvedTubes);
                if (board == null) return result;
                if (!seen.Add(board.ComputeHash())) continue;
                var rating = _rater.Rate(board, options.Budget);
                if (rating.Verdict != SolveVerdict.Solvable) { unsolvable++; continue; }

                int wantedIdx = NearestWanted(rating.Bucket, options.Tolerance);
                if (wantedIdx < 0)
                {
                    rejectedByBucket.TryGetValue(rating.Bucket, out int n);
                    rejectedByBucket[rating.Bucket] = n + 1;
                    if (options.FallbackTolerance > 0 && NearestWanted(rating.Bucket, options.FallbackTolerance) >= 0)
                        rejected.Add((seed, board, rating));
                    Progress?.Invoke(k + 1, result.Count, options.WantedBuckets.Count);
                    continue;
                }
                remaining.RemoveAt(wantedIdx);
                Accept(seed, board, rating);
                Progress?.Invoke(k + 1, result.Count, options.WantedBuckets.Count);
            }

            // fall back to the nearest near-misses for the buckets still missing
            int fallbacks = 0;
            while (remaining.Count > 0 && rejected.Count > 0)
            {
                int bestIdx = -1, bestDist = int.MaxValue, bestWanted = -1;
                for (int i = 0; i < rejected.Count; i++)
                {
                    int w = NearestWanted(rejected[i].rating.Bucket, options.FallbackTolerance);
                    if (w < 0) continue;
                    int d = Math.Abs(remaining[w] - rejected[i].rating.Bucket);
                    if (d < bestDist) { bestDist = d; bestIdx = i; bestWanted = w; }
                }
                if (bestIdx < 0) break;
                var r = rejected[bestIdx];
                rejected.RemoveAt(bestIdx);
                remaining.RemoveAt(bestWanted);
                Accept(r.seed, r.board, r.rating);
                fallbacks++;
            }

            if (remaining.Count > 0 || fallbacks > 0)
            {
                var sb = new System.Text.StringBuilder();
                if (remaining.Count > 0) sb.Append("missing buckets [").Append(string.Join(",", remaining)).Append("] after ").Append(options.MaxFills).Append(" fills");
                if (fallbacks > 0) sb.Append(remaining.Count > 0 ? "; " : "").Append(fallbacks).Append(" seed(s) from a neighbouring bucket");
                sb.Append("; solvable fills seen: ");
                foreach (var kv in rejectedByBucket) sb.Append('b').Append(kv.Key).Append('x').Append(kv.Value).Append(' ');
                if (unsolvable > 0) sb.Append("unsolvable x").Append(unsolvable);
                error = sb.ToString();
            }
            return result;
        }

        /// <summary>Rates one existing board (e.g. a level from a pack).</summary>
        public LevelRating Rate(BoardState board, RaterBudget budget) => _rater.Rate(board, budget);
    }

    /// <summary>
    /// Magic Sort's seed selection (LevelModelService.GetSeedIdFromConfig): attempt N plays seed N while the list
    /// lasts, then a random seed that is not the one played last; after <paramref name="dlbCap"/> failed attempts
    /// the easier <paramref name="dlbSeeds"/> list takes over the same way.
    /// </summary>
    public static class SeedSelector
    {
        public static int Pick(int seedCount, int attempt, int lastPlayed, Random rng)
        {
            if (seedCount <= 0) return -1;
            if (attempt < seedCount) return attempt;
            if (seedCount == 1) return 0;
            int pick;
            do pick = rng.Next(seedCount); while (pick == lastPlayed);
            return pick;
        }

        /// <summary>Returns (useDlb, index).</summary>
        public static (bool useDlb, int index) Pick(int seedCount, int dlbSeedCount, int dlbCap, int attempt, int lastPlayed, Random rng)
        {
            if (dlbSeedCount > 0 && dlbCap > 0 && attempt >= dlbCap)
                return (true, Pick(dlbSeedCount, attempt - dlbCap, lastPlayed, rng));
            return (false, Pick(seedCount, attempt, lastPlayed, rng));
        }
    }
}
