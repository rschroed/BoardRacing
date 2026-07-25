using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // The experiment issue #147 turns on: give lateral position a real path
    // cost and cars a real separation constraint, then ask the two questions
    // that decide whether the idea is worth a tranche.
    //
    // 1. Does anyone ever take the outside line, or does a real distance
    //    penalty just produce single file for an honest reason?
    // 2. Does a field held up in traffic still resolve, or does throttle-only
    //    control plus a real speed cap deadlock behind a slow leader?
    //
    // Reports rather than asserts, except where an answer would be
    // disqualifying: bodies must not overlap, and a race must finish.
    public sealed class LateralCostExperiment
    {
        private const float Step = 1f / 60f;
        private const float BodyLength = 54f;
        private const float BodyWidth = 26f;

        private static RaceRules Rules => new RaceRules(3, 3f, Pace.BasePace, Pace.Acceleration,
            Pace.Drag, Pace.Braking, .55f, 1f, .35f, 180f, 16f, 1f,
            slipstreamBonus: Pace.SlipstreamBonus,
            slipstreamWindow: RaceRules.DefaultSlipstreamWindow,
            lateralRules: LateralRules.Defaults);

        [Explicit("Experiment: reports whether a real lateral cost produces racing or a train.")]
        [TestCase("Wedge")]
        [TestCase("Fishhook")]
        [TestCase("Hourglass")]
        public void RealLateralCostProducesRacing(string courseName)
        {
            CourseDefinition course = CourseCatalog.All().Single(c => c.Name == courseName);
            Trace trace = Run(course, new[]
            {
                PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4
            }, slowLeader: false);

            TestContext.Out.WriteLine($"===== {courseName} =====");
            ReportGridGeometry(course);
            TestContext.Out.WriteLine(
                $"  finished={trace.Finished} in {trace.Seconds:0.0}s  " +
                $"lead changes={trace.LeadChanges}  overtakes={trace.Overtakes}");
            TestContext.Out.WriteLine(
                $"  outside-line use: {100f * trace.OutsideFrames / Math.Max(1, trace.CornerFrames):0.0}% " +
                $"of car-frames in corners ({trace.OutsideFrames} of {trace.CornerFrames})");
            TestContext.Out.WriteLine(
                $"  two-abreast in corners: {100f * trace.AbreastFrames / Math.Max(1, trace.CornerFrames):0.0}%");
            TestContext.Out.WriteLine(
                $"  held up (capped behind a rival): {100f * trace.CappedFrames / Math.Max(1, trace.CarFrames):0.0}% " +
                "of car-frames");
            TestContext.Out.WriteLine(
                $"  longest single stint held up: {trace.LongestHeldStreak * Step:0.0}s");
            TestContext.Out.WriteLine(
                $"  finish spread {trace.FinishSpread:0.00}s  worst body clearance {trace.WorstClearance:0.0}px");
            TestContext.Out.WriteLine($"    worst at: {trace.WorstWhere}");

            // A stalemate would show as a race that never finishes.
            Assert.That(trace.Finished, Is.True, courseName + " never finished");
            // The whole point: separation is now real, so nothing may overlap.
            Assert.That(trace.WorstClearance, Is.GreaterThanOrEqualTo(0f),
                courseName + " bodies overlapped despite a real separation constraint");
        }

        [Explicit("Experiment: can the field get past a deliberately slow leader?")]
        [TestCase("Wedge")]
        [TestCase("Fishhook")]
        public void TheFieldCanPassASlowLeader(string courseName)
        {
            CourseDefinition course = CourseCatalog.All().Single(c => c.Name == courseName);
            Trace trace = Run(course, new[]
            {
                PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4
            }, slowLeader: true);
            TestContext.Out.WriteLine($"===== {courseName} (Player1 holds Drive, rest Boost) =====");
            TestContext.Out.WriteLine(
                $"  finished={trace.Finished}  overtakes={trace.Overtakes}  " +
                $"Player1 final place={trace.FinalPlace[PlayerId.Player1]}");
            TestContext.Out.WriteLine(
                $"  held up: {100f * trace.CappedFrames / Math.Max(1, trace.CarFrames):0.0}% of car-frames");
            Assert.That(trace.Finished, Is.True, courseName + " deadlocked behind the slow leader");
        }

        [Explicit("Experiment: does a real lateral cost create a seat advantage?")]
        [TestCase("Wedge")]
        public void LineChoiceDoesNotFavourASeat(string courseName)
        {
            CourseDefinition course = CourseCatalog.All().Single(c => c.Name == courseName);
            PlayerId[] roster =
            {
                PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4
            };
            var times = new Dictionary<PlayerId, List<float>>();
            foreach (PlayerId[] order in Permutations(roster))
            {
                Trace trace = Run(course, order, slowLeader: false);
                foreach (var kv in trace.FinishTime)
                {
                    if (!times.TryGetValue(kv.Key, out var list))
                        times[kv.Key] = list = new List<float>();
                    list.Add(kv.Value);
                }
            }
            TestContext.Out.WriteLine($"===== {courseName} seat fairness over roster orders =====");
            foreach (var kv in times.OrderBy(x => x.Key))
                TestContext.Out.WriteLine(
                    $"  {kv.Key}: mean {kv.Value.Average():0.00}s  " +
                    $"spread {kv.Value.Max() - kv.Value.Min():0.00}s");
            float[] means = times.Values.Select(x => x.Average()).ToArray();
            TestContext.Out.WriteLine($"  worst mean gap between seats: {means.Max() - means.Min():0.00}s");
        }

        // The grid is laid out in ARC LENGTH behind the line. If the run-up to
        // the line is a corner, slots that are 80px apart along the ribbon are
        // much closer than that in world space, and the grid bunches however
        // well it is spaced. Relevant to where the start line sits.
        private static void ReportGridGeometry(CourseDefinition course)
        {
            TrackDefinition track = course.Track;
            var slots = new List<(float along, Vec2 at, TrackSectionKind kind)>();
            for (int i = 0; i < 4; i++)
            {
                float d = -(i / 2) * RaceSimulation.GridRowSpacing;
                TrackSample sample = track.Sample(d);
                slots.Add((d, sample.Position, sample.Kind));
            }
            string kinds = string.Join(" ", slots.Select(s => s.kind.ToString().Substring(0, 1)));
            float frontToBack = (float)Math.Sqrt(
                Math.Pow(slots[0].at.X - slots[3].at.X, 2) +
                Math.Pow(slots[0].at.Y - slots[3].at.Y, 2));
            float worstAdjacent = float.MaxValue;
            for (int i = 1; i < slots.Count; i++)
                worstAdjacent = Math.Min(worstAdjacent, (float)Math.Sqrt(
                    Math.Pow(slots[i - 1].at.X - slots[i].at.X, 2) +
                    Math.Pow(slots[i - 1].at.Y - slots[i].at.Y, 2)));
            TestContext.Out.WriteLine(
                $"  grid run-up sections (front to back): {kinds}   " +
                $"pole-to-last world gap {frontToBack:0}px of " +
                $"{RaceSimulation.GridRowSpacing:0}px along-track (two rows of two), " +
                $"closest rows {worstAdjacent:0}px");
        }

        private sealed class Trace
        {
            public bool Finished;
            public float Seconds, FinishSpread, WorstClearance = float.MaxValue;
            public string WorstWhere = "(none)";
            public int LeadChanges, Overtakes, OutsideFrames, AbreastFrames, CornerFrames,
                CappedFrames, CarFrames;
            // The owner's report: stuck behind two cars running abreast. How
            // long does one stint of being held up actually last?
            public int LongestHeldStreak;
            public readonly Dictionary<PlayerId, int> HeldStreak = new Dictionary<PlayerId, int>();
            public readonly Dictionary<PlayerId, float> FinishTime = new Dictionary<PlayerId, float>();
            public readonly Dictionary<PlayerId, int> FinalPlace = new Dictionary<PlayerId, int>();
        }

        private static Trace Run(CourseDefinition course, PlayerId[] roster, bool slowLeader)
        {
            TrackDefinition track = course.Track;
            var simulation = new RaceSimulation(track, Rules, roster);
            var trace = new Trace();
            PlayerId leader = roster[0];
            var priorOrder = new List<PlayerId>(roster);

            for (int frame = 0; frame < (int)(600f / Step); frame++)
            {
                RacerCommand[] commands = simulation.Snapshot.Racers.Select(r =>
                    new RacerCommand(r.PlayerId,
                        slowLeader && r.PlayerId == PlayerId.Player1
                            ? ThrottleStep.Drive
                            : slowLeader ? ThrottleStep.Boost : ThrottleStep.Drive,
                        true, false)).ToArray();
                RaceSnapshot snapshot = simulation.Step(Step, commands);
                trace.Seconds = snapshot.ElapsedSeconds;

                if (snapshot.Phase == RacePhase.Racing)
                {
                    var racing = snapshot.Racers.Where(r => !r.Finished).ToArray();
                    foreach (RacerSnapshot racer in racing)
                    {
                        trace.CarFrames++;
                        bool corner = track.Sample(racer.TotalDistance).Kind == TrackSectionKind.Corner;
                        if (!corner) continue;
                        trace.CornerFrames++;
                        // Outside = the offset sits away from the turn; sample
                        // the same curvature sign the model steers on.
                        float curvature = Curvature(track, racer.TotalDistance);
                        if (curvature != 0f && racer.LateralOffset * Math.Sign(curvature) < -1f)
                            trace.OutsideFrames++;
                        if (racing.Any(o => o.PlayerId != racer.PlayerId &&
                                Math.Abs(o.TotalDistance - racer.TotalDistance) < BodyLength &&
                                Math.Abs(o.LateralOffset - racer.LateralOffset) >= BodyWidth))
                            trace.AbreastFrames++;
                        if (racer.Speed < Pace.BasePace * .98f &&
                            racing.Any(o => o.PlayerId != racer.PlayerId &&
                                o.TotalDistance > racer.TotalDistance &&
                                o.TotalDistance - racer.TotalDistance < 150f &&
                                Math.Abs(o.LateralOffset - racer.LateralOffset) < BodyWidth))
                        {
                            trace.CappedFrames++;
                            trace.HeldStreak.TryGetValue(racer.PlayerId, out int streak);
                            trace.HeldStreak[racer.PlayerId] = ++streak;
                            if (streak > trace.LongestHeldStreak) trace.LongestHeldStreak = streak;
                        }
                        else trace.HeldStreak[racer.PlayerId] = 0;
                    }
                    // Body clearance in the sim's own terms: along-track gap
                    // and lateral gap, both real now. The standing start is no
                    // longer excused — the grid is a real staggered grid, so
                    // this covers every frame from the lights.
                    for (int i = 0; i < racing.Length; i++)
                        for (int j = i + 1; j < racing.Length; j++)
                        {
                            float along = Math.Abs(racing[i].TotalDistance - racing[j].TotalDistance);
                            float across = Math.Abs(racing[i].LateralOffset - racing[j].LateralOffset);
                            if (along >= BodyLength || across >= BodyWidth) continue;
                            float clearance = Math.Max(along - BodyLength, across - BodyWidth);
                            if (clearance >= trace.WorstClearance) continue;
                            trace.WorstClearance = clearance;
                            trace.WorstWhere =
                                $"f{frame} t={snapshot.ElapsedSeconds:0.00}s " +
                                $"{racing[i].PlayerId}/{racing[j].PlayerId} along={along:0.0} across={across:0.0} " +
                                $"d={racing[i].TotalDistance:0}/{racing[j].TotalDistance:0} " +
                                $"lat={racing[i].LateralOffset:0.0}/{racing[j].LateralOffset:0.0}";
                        }

                    List<PlayerId> order = racing.OrderByDescending(r => r.TotalDistance)
                        .Select(r => r.PlayerId).ToList();
                    if (order.Count > 0 && order[0] != leader) { leader = order[0]; trace.LeadChanges++; }
                    for (int i = 0; i < order.Count; i++)
                    {
                        int was = priorOrder.IndexOf(order[i]);
                        if (was > i) trace.Overtakes++;
                    }
                    priorOrder = order;
                }

                if (snapshot.Phase == RacePhase.Finished)
                {
                    trace.Finished = true;
                    foreach (RacerSnapshot racer in snapshot.Racers)
                    {
                        trace.FinishTime[racer.PlayerId] = racer.FinishTime;
                        trace.FinalPlace[racer.PlayerId] = racer.Place;
                    }
                    float[] finishes = trace.FinishTime.Values.ToArray();
                    trace.FinishSpread = finishes.Max() - finishes.Min();
                    break;
                }
            }
            if (trace.WorstClearance == float.MaxValue) trace.WorstClearance = 0f;
            return trace;
        }

        private static float Curvature(TrackDefinition track, float distance)
        {
            const float halfSpan = 40f;
            Vec2 behind = track.Sample(distance - halfSpan).Position;
            Vec2 at = track.Sample(distance).Position;
            Vec2 ahead = track.Sample(distance + halfSpan).Position;
            float aX = at.X - behind.X, aY = at.Y - behind.Y;
            float bX = ahead.X - at.X, bY = ahead.Y - at.Y;
            float cross = aX * bY - aY * bX, dot = aX * bX + aY * bY;
            if (cross == 0f && dot == 0f) return 0f;
            return (float)Math.Atan2(cross, dot) / halfSpan;
        }

        private static IEnumerable<PlayerId[]> Permutations(PlayerId[] roster)
        {
            yield return new[] { roster[0], roster[1], roster[2], roster[3] };
            yield return new[] { roster[3], roster[2], roster[1], roster[0] };
            yield return new[] { roster[1], roster[3], roster[0], roster[2] };
            yield return new[] { roster[2], roster[0], roster[3], roster[1] };
        }
    }
}
