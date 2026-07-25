using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // Diagnostic harness (issue #143, from the owner report 2026-07-25: cars
    // overlapping in four-player races). Where PresentationJitterProbe asks how
    // honest drawn MOTION is, this asks whether two drawn BODIES ever occupy the
    // same pixels: it replays a scripted four-car race through RacePrototype's
    // drawn-car pipeline — allocator placement, corner pads and their slide, the
    // line-truth clamp, launch twitch, split taper, pass clearance, body floor,
    // and the pit-lane pose mapper — and buckets every interpenetrating pair by
    // what the pair was doing, so a report of "cars overlap" resolves to a
    // mechanism instead of a guess.
    //
    // The buckets it found on first run, none of which the #143 floor covers:
    // a FINISHED car parked on the line with the field driving through it
    // (issue #144), and a SAME-side pair whose in-lane stagger teleports when
    // the two exchange order. Measured in world space, so it also sees what the
    // flat along-track model cannot: on a tight corner the two bodies' headings
    // diverge, and the pair clears by less than the flat rule believes.
    public sealed class FourCarOverlapProbe
    {
        private const float Step = 1f / 60f;

        [Explicit("Diagnostic: reports where drawn car bodies interpenetrate, by cause.")]
        [TestCase("Wedge")]
        [TestCase("Fishhook")]
        [TestCase("Hourglass")]
        public void ProbeDrawnOverlap(string courseName)
        {
            CourseDefinition course = CourseCatalog.All().Single(c => c.Name == courseName);
            TrackDefinition track = course.Track;
            RaceRules rules = RaceRules.Defaults;
            PlayerId[] roster =
            {
                PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4
            };
            var simulation = new RaceSimulation(track, rules, roster);
            PitLanePresentationLayout layout = PitLanePresentationLayout.ForCourse(course);

            var slewedPads = new Dictionary<PlayerId, float>();
            var buckets = new Dictionary<string, (int frames, float worst, string sample)>();
            var requested = roster.ToDictionary(x => x, _ => false);
            int frames = 0, overlapFrames = 0;

            for (int frame = 0; frame < (int)(400f / Step); frame++)
            {
                // Staggered throttle so the field closes and re-opens instead of
                // running in lockstep: the pack has to actually form and break.
                RacerCommand[] commands = simulation.Snapshot.Racers.Select(r =>
                {
                    bool request = false;
                    if (!requested[r.PlayerId] && r.TotalDistance >= track.Length * .25f &&
                        r.Pit.Phase == PitPhase.OnTrack)
                    { requested[r.PlayerId] = true; request = true; }
                    ThrottleStep throttle = ((int)r.PlayerId + frame / 220) % 3 == 0
                        ? ThrottleStep.Boost : ThrottleStep.Drive;
                    return new RacerCommand(r.PlayerId, throttle, true, request);
                }).ToArray();

                RaceSnapshot snapshot = simulation.Step(Step, commands);
                if (snapshot.Phase == RacePhase.Finished) break;
                frames++;

                // --- RefreshDrawnPads ---
                var racing = snapshot.Racers.Where(r => OnRacingLine(r) && !r.Finished).ToArray();
                var pads = new Dictionary<PlayerId, float>();
                if (racing.Length >= 2 && snapshot.Phase == RacePhase.Racing)
                {
                    float[] targets = CornerCharacter.CornerSpacingPads(track,
                        racing.Select(r => r.TotalDistance + r.LongitudinalOffset).ToArray(),
                        rules.PassingDistance);
                    for (int i = 0; i < racing.Length; i++)
                    {
                        float pad = slewedPads.TryGetValue(racing[i].PlayerId, out float was)
                            ? was + Math.Max(-PresentationLife.PadSlideRate * Step,
                                Math.Min(PresentationLife.PadSlideRate * Step, targets[i] - was))
                            : targets[i];
                        float wrapped = Wrap(racing[i].TotalDistance, track.Length);
                        float bound = Math.Min(wrapped, track.Length - wrapped) *
                            (CornerCharacter.NoseToTailSpacing / CornerCharacter.LineFadeSpan);
                        pads[racing[i].PlayerId] = Math.Max(-bound, Math.Min(bound, pad));
                    }
                    foreach (var kv in pads) slewedPads[kv.Key] = kv.Value;
                }
                else slewedPads.Clear();

                float Drawn(RacerSnapshot r) => r.TotalDistance + r.LongitudinalOffset +
                    (pads.TryGetValue(r.PlayerId, out float p) ? p : 0f) -
                    Math.Min(PresentationLife.Launch(snapshot.ElapsedSeconds,
                        PresentationLife.LaunchPhase((int)r.PlayerId, track.Length)).Lag,
                        Math.Max(0f, r.TotalDistance));
                float Gap(float a, float b)
                {
                    float w = Wrap(a - b, track.Length);
                    return Math.Min(w, track.Length - w);
                }

                var clearance = new Dictionary<PlayerId, float>();
                var engagement = new Dictionary<PlayerId, float>();
                var floors = new Dictionary<PlayerId, float>();
                foreach (var racer in racing)
                {
                    var others = racing.Where(o => o.PlayerId != racer.PlayerId).ToArray();
                    clearance[racer.PlayerId] = PresentationLife.PassClearance(
                        others.Select(o => Gap(Drawn(racer), Drawn(o)))
                            .DefaultIfEmpty(float.MaxValue).Min());
                    engagement[racer.PlayerId] = PresentationLife.DuelEngagement(
                        others.Select(o => Gap(racer.TotalDistance, o.TotalDistance))
                            .DefaultIfEmpty(float.MaxValue).Min(), rules.PassingDistance);
                    float[] across = racing
                        .Where(o => o.LateralOffset * racer.LateralOffset < 0f)
                        .Select(o => RaceSurfaceGeometry.SplitForBodyClearance(
                            Gap(Drawn(racer), Drawn(o)))).ToArray();
                    floors[racer.PlayerId] = across.Length == 0 ? 0f
                        : Math.Min(1f, across.Max() / (2f * Math.Abs(racer.LateralOffset)));
                }

                float breathDistance = 0f, breathAmplitude = 0f;
                var duel = racing.Where(r => r.LateralOffset != 0f).ToArray();
                if (duel.Length >= 2)
                {
                    breathDistance = duel.Average(r => r.TotalDistance);
                    breathAmplitude = 1f;
                    foreach (var r in duel)
                        breathAmplitude *= CornerCharacter.LineTruthEnvelope(track, r.TotalDistance) *
                            (1f - CornerCharacter.FormationBlend(track, r.TotalDistance)) *
                            engagement[r.PlayerId];
                }

                // --- CarPose + OffsetCenter, every drawn car ---
                var poses = new Dictionary<PlayerId, (Vec2 center, Vec2 tangent)>();
                foreach (var racer in snapshot.Racers)
                {
                    float drawn = Drawn(racer);
                    CarPresentationPose pose = PitLanePresentationMapper.From(racer,
                        track.Sample(drawn).Position,
                        TrackPresentation.SmoothHeading(track, drawn), layout);
                    var breath = breathAmplitude > 0f && racer.LateralOffset != 0f &&
                            !racer.Finished && OnRacingLine(racer)
                        ? PresentationLife.Breathe(breathDistance, racer.LateralOffset, breathAmplitude)
                        : DuelBreath.Still;
                    float lateral = OnRacingLine(racer)
                        ? racer.LateralOffset * PresentationLife.DrawnSplitScale(
                            CornerCharacter.SplitScale(track, drawn),
                            clearance.TryGetValue(racer.PlayerId, out float c) ? c : 0f,
                            breath.FlareScale,
                            engagement.TryGetValue(racer.PlayerId, out float e) ? e : 1f,
                            floors.TryGetValue(racer.PlayerId, out float f) ? f : 0f)
                        : 0f;
                    poses[racer.PlayerId] = (new Vec2(
                        pose.Position.X - pose.Tangent.Y * lateral,
                        pose.Position.Y + pose.Tangent.X * lateral), pose.Tangent);
                }

                bool anyOverlap = false;
                var racers = snapshot.Racers.ToArray();
                for (int i = 0; i < racers.Length; i++)
                    for (int j = i + 1; j < racers.Length; j++)
                    {
                        // World separation resolved onto one body's heading: on a
                        // tight corner the pair's headings differ, and this is
                        // what the flat along-track model cannot see.
                        Vec2 a = poses[racers[i].PlayerId].center, b = poses[racers[j].PlayerId].center;
                        Vec2 t = poses[racers[i].PlayerId].tangent;
                        float dx = b.X - a.X, dy = b.Y - a.Y;
                        float along = dx * t.X + dy * t.Y;
                        float across = -dx * t.Y + dy * t.X;
                        float clear = RaceSurfaceGeometry.BodyClearance(along, across);
                        if (clear >= 0f) continue;
                        anyOverlap = true;
                        string key = Classify(racers[i], racers[j]);
                        string sample =
                            $"f{frame} {racers[i].PlayerId}/{racers[j].PlayerId} " +
                            $"phase={snapshot.Phase} clear={clear:0.0} along={along:0} across={across:0} " +
                            $"pit={racers[i].Pit.Phase}/{racers[j].Pit.Phase} " +
                            $"fin={racers[i].Finished}/{racers[j].Finished} " +
                            $"lat={racers[i].LateralOffset:0}/{racers[j].LateralOffset:0} " +
                            $"lon={racers[i].LongitudinalOffset:0}/{racers[j].LongitudinalOffset:0} " +
                            $"d={racers[i].TotalDistance % track.Length:0}/" +
                            $"{racers[j].TotalDistance % track.Length:0} " +
                            $"blend={CornerCharacter.FormationBlend(track, racers[i].TotalDistance):0.00}";
                        buckets[key] = buckets.TryGetValue(key, out var bucket)
                            ? (bucket.frames + 1,
                                clear < bucket.worst ? clear : bucket.worst,
                                clear < bucket.worst ? sample : bucket.sample)
                            : (1, clear, sample);
                    }
                if (anyOverlap) overlapFrames++;
            }

            TestContext.Out.WriteLine(
                $"=== {courseName}: {overlapFrames} overlapping frames of {frames} ===");
            foreach (var kv in buckets.OrderByDescending(x => x.Value.frames))
                TestContext.Out.WriteLine(
                    $"  [{kv.Value.frames,5} pair-frames] {kv.Key}  worst={kv.Value.worst:0.0}px\n" +
                    $"          {kv.Value.sample}");
            Assert.Pass();
        }

        private static string Classify(RacerSnapshot a, RacerSnapshot b)
        {
            if (a.Finished || b.Finished) return "FINISHED car vs the field (issue #144)";
            if (!OnRacingLine(a) || !OnRacingLine(b)) return "PIT-lane pose vs track pose";
            if (a.LateralOffset == 0f || b.LateralOffset == 0f)
                return "one car CENTERED (unsplit) vs a split car";
            return a.LateralOffset * b.LateralOffset > 0f
                ? "SAME-side pair (in-lane file only)"
                : "opposite-side pair (split)";
        }

        private static bool OnRacingLine(RacerSnapshot r) =>
            r.Pit.Phase == PitPhase.OnTrack || r.Pit.Phase == PitPhase.Requested;

        private static float Wrap(float v, float length)
        {
            v %= length;
            return v < 0f ? v + length : v;
        }
    }
}
