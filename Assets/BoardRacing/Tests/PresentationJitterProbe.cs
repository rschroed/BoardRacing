using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // Diagnostic harness (born from the 2026-07-23 owner report: jitter in
    // and out of Fishhook's corners; Wedge and Hourglass smooth — it found
    // the split-engage teleport and the formation churn). Replays a scripted race
    // through the exact drawn-car pipeline of RacePrototype at fixed steps and
    // reports the frames where drawn motion deviates hardest from true motion:
    // drawn speed vs sim speed (position channel) and heading acceleration
    // (pose channel). Writes a per-frame CSV for offline analysis.
    public sealed class PresentationJitterProbe
    {
        private const float Step = 1f / 60f;

        [Explicit("Diagnostic: prints drawn-motion deviation reports and writes per-frame CSVs.")]
        [TestCase("Fishhook", false)]
        [TestCase("Fishhook", true)]
        [TestCase("Wedge", false)]
        [TestCase("Hourglass", false)]
        public void ProbeDrawnMotion(string courseName, bool boostPulses)
        {
            CourseDefinition course = CourseCatalog.All()
                .Single(c => c.Name == courseName);
            TrackDefinition track = course.Track;
            RaceRules rules = RaceRules.Defaults;
            var simulation = new RaceSimulation(track, rules);

            var previous = new Dictionary<PlayerId, (Vec2 drawn, float heading, float dHeading, float speed)>();
            var slewedPads = new Dictionary<PlayerId, float>();
            var rows = new List<string>();
            var events = new List<(float severity, string line)>();
            float raceSeconds = 0f;
            int steps = (int)(150f / Step);
            for (int frame = 0; frame < steps; frame++)
            {
                var commands = new List<RacerCommand>
                {
                    new RacerCommand(PlayerId.Player1, ThrottleStep.Drive, true, false),
                    new RacerCommand(PlayerId.Player2,
                        boostPulses && raceSeconds > 8f && raceSeconds % 12f < 1f
                            ? ThrottleStep.Boost : ThrottleStep.Drive, true, false),
                };
                RaceSnapshot before = simulation.Snapshot;
                RaceSnapshot snapshot = simulation.Step(Step, commands);
                if (snapshot.Phase == RacePhase.Finished) break;
                if (snapshot.Phase != RacePhase.Racing) continue;
                raceSeconds = snapshot.ElapsedSeconds;

                // --- RefreshDrawnPads, replicated ---
                var racing = snapshot.Racers.Where(r => OnRacingLine(r) && !r.Finished).ToArray();
                var pads = new Dictionary<PlayerId, float>();
                if (racing.Length >= 2)
                {
                    float[] targets = CornerCharacter.CornerSpacingPads(track,
                        racing.Select(r => r.TotalDistance).ToArray(), rules.PassingDistance);
                    for (int i = 0; i < racing.Length; i++)
                    {
                        float pad = slewedPads.TryGetValue(racing[i].PlayerId, out float was)
                            ? was + Math.Max(-PresentationLife.PadSlideRate * Step,
                                Math.Min(PresentationLife.PadSlideRate * Step, targets[i] - was))
                            : targets[i];
                        float wrapped = ((racing[i].TotalDistance % track.Length) + track.Length) % track.Length;
                        float bound = Math.Min(wrapped, track.Length - wrapped) *
                            (CornerCharacter.NoseToTailSpacing / CornerCharacter.LineFadeSpan);
                        pads[racing[i].PlayerId] = Math.Max(-bound, Math.Min(bound, pad));
                    }
                    foreach (var kv in pads) slewedPads[kv.Key] = kv.Value;
                }
                else slewedPads.Clear();
                float DrawnOf(RacerSnapshot r) => r.TotalDistance +
                    (pads.TryGetValue(r.PlayerId, out float dp) ? dp : 0f);
                var clearance = new Dictionary<PlayerId, float>();
                foreach (var racer in racing)
                    clearance[racer.PlayerId] = PresentationLife.PassClearance(
                        racing.Where(other => other.PlayerId != racer.PlayerId)
                            .Select(other =>
                            {
                                float w = ((DrawnOf(racer) - DrawnOf(other)) % track.Length +
                                    track.Length) % track.Length;
                                return Math.Min(w, track.Length - w);
                            }).DefaultIfEmpty(float.MaxValue).Min());
                var engagement = new Dictionary<PlayerId, float>();
                foreach (var racer in racing)
                    engagement[racer.PlayerId] = PresentationLife.DuelEngagement(
                        racing.Where(other => other.PlayerId != racer.PlayerId)
                            .Select(other =>
                            {
                                float wrapped = ((racer.TotalDistance - other.TotalDistance) % track.Length +
                                    track.Length) % track.Length;
                                return Math.Min(wrapped, track.Length - wrapped);
                            }).DefaultIfEmpty(float.MaxValue).Min(),
                        rules.PassingDistance);
                float breathDistance = 0f, breathAmplitude = 0f;
                var duel = racing.Where(r => r.LateralOffset != 0f).ToArray();
                if (duel.Length >= 2)
                {
                    breathDistance = duel.Average(r => r.TotalDistance);
                    breathAmplitude = 1f;
                    foreach (var r in duel)
                        breathAmplitude *= CornerCharacter.LineTruthEnvelope(track, r.TotalDistance) *
                            (1f - CornerCharacter.FormationBlend(track, r.TotalDistance)) *
                            (engagement.TryGetValue(r.PlayerId, out float e) ? e : 1f);
                }

                foreach (var racer in snapshot.Racers)
                {
                    // --- DrawnDistance ---
                    var twitch = PresentationLife.Launch(snapshot.ElapsedSeconds,
                        PresentationLife.LaunchPhase((int)racer.PlayerId, track.Length));
                    float pad = pads.TryGetValue(racer.PlayerId, out float value) ? value : 0f;
                    float drawnDistance = racer.TotalDistance + pad -
                        Math.Min(twitch.Lag, racer.TotalDistance);

                    // --- CarPose + OffsetCenter (on-track path) ---
                    Vec2 center = track.Sample(drawnDistance).Position;
                    Vec2 tangent = TrackPresentation.SmoothHeading(track, drawnDistance);
                    var breath = breathAmplitude > 0f && racer.LateralOffset != 0f && !racer.Finished &&
                            OnRacingLine(racer)
                        ? PresentationLife.Breathe(breathDistance, racer.LateralOffset, breathAmplitude)
                        : DuelBreath.Still;
                    float lateral = OnRacingLine(racer)
                        ? racer.LateralOffset *
                            Math.Max(CornerCharacter.SplitScale(track, drawnDistance),
                                clearance.TryGetValue(racer.PlayerId, out float clear) ? clear : 0f) *
                            breath.FlareScale *
                            (engagement.TryGetValue(racer.PlayerId, out float engaged) ? engaged : 1f)
                        : 0f;
                    var drawn = new Vec2(center.X - tangent.Y * lateral, center.Y + tangent.X * lateral);

                    // --- UpdateWorldCars heading ---
                    float previousSpeed = previous.TryGetValue(racer.PlayerId, out var last) ? last.speed : 0f;
                    float deceleration = before.Phase == snapshot.Phase
                        ? Math.Max(0f, (previousSpeed - racer.Speed) / Step) : 0f;
                    var attitude = OnRacingLine(racer) && !racer.Finished
                        ? CornerCharacter.Attitude(track, drawnDistance, racer.Speed,
                            deceleration, rules.Braking)
                        : CarAttitude.Neutral;
                    float stance = -Math.Sign(racer.LateralOffset) * breath.StanceDegrees;
                    float heading = (float)(Math.Atan2(tangent.Y, tangent.X) * 180.0 / Math.PI) +
                        attitude.DriftDegrees + stance + twitch.YawDegrees;

                    if (previous.TryGetValue(racer.PlayerId, out var was))
                    {
                        float dx = drawn.X - was.drawn.X, dy = drawn.Y - was.drawn.Y;
                        float drawnSpeed = (float)Math.Sqrt(dx * dx + dy * dy) / Step;
                        float speedError = drawnSpeed - racer.Speed;
                        float dHeading = DeltaAngle(was.heading, heading);
                        float ddHeading = dHeading - was.dHeading;
                        rows.Add(string.Join(",", new[]
                        {
                            frame.ToString(), ((int)racer.PlayerId).ToString(),
                            (racer.TotalDistance % track.Length).ToString("0.0", CultureInfo.InvariantCulture),
                            racer.Speed.ToString("0.0", CultureInfo.InvariantCulture),
                            drawnSpeed.ToString("0.0", CultureInfo.InvariantCulture),
                            pad.ToString("0.00", CultureInfo.InvariantCulture),
                            CornerCharacter.FormationBlend(track, racer.TotalDistance)
                                .ToString("0.000", CultureInfo.InvariantCulture),
                            lateral.ToString("0.00", CultureInfo.InvariantCulture),
                            heading.ToString("0.00", CultureInfo.InvariantCulture),
                            attitude.DriftDegrees.ToString("0.00", CultureInfo.InvariantCulture),
                            racer.IncidentThisStep ? "1" : "0",
                            racer.RecoveryRemaining.ToString("0.00", CultureInfo.InvariantCulture),
                        }));
                        bool launchWindow = snapshot.ElapsedSeconds < 1.5f;
                        float severity = Math.Abs(speedError) / Pace.BasePace + Math.Abs(ddHeading) / 8f;
                        if (!launchWindow &&
                            (Math.Abs(speedError) > Pace.BasePace * .3f || Math.Abs(ddHeading) > 3f))
                            events.Add((severity,
                                $"f{frame} P{(int)racer.PlayerId} d={racer.TotalDistance % track.Length:0} " +
                                $"kind={track.Sample(racer.TotalDistance).Kind} " +
                                $"blend={CornerCharacter.FormationBlend(track, racer.TotalDistance):0.00} " +
                                $"pad={pad:0.0} lat={lateral:0.0} v={racer.Speed:0} drawnV={drawnSpeed:0} " +
                                $"vErr={speedError:0} dHead={dHeading:0.0} ddHead={ddHeading:0.0} " +
                                $"drift={attitude.DriftDegrees:0.0} inc={(racer.IncidentThisStep ? 1 : 0)} " +
                                $"rec={racer.RecoveryRemaining:0.00}"));
                    }
                    previous[racer.PlayerId] = (drawn, heading,
                        previous.TryGetValue(racer.PlayerId, out var w) ? DeltaAngle(w.heading, heading) : 0f,
                        racer.Speed);
                }
            }

            string csv = Path.Combine(Path.GetTempPath(),
                $"jitter-{courseName}-{(boostPulses ? "pulses" : "even")}.csv");
            File.WriteAllLines(csv, new[]
                { "frame,player,dist,speed,drawnSpeed,pad,blend,lateral,heading,drift,incident,recovery" }
                .Concat(rows));
            TestContext.Out.WriteLine(
                $"JITTER PROBE {courseName} {(boostPulses ? "pulses" : "even")}: " +
                $"{events.Count} flagged frames of {rows.Count}; csv={csv}");
            foreach (var e in events.OrderByDescending(x => x.severity).Take(25))
                TestContext.Out.WriteLine("JP| " + e.line);
            Assert.That(rows.Count, Is.GreaterThan(1000), "the race never got going");
        }

        private static bool OnRacingLine(RacerSnapshot racer) =>
            racer.Pit.Phase == PitPhase.OnTrack || racer.Pit.Phase == PitPhase.Requested;

        private static float DeltaAngle(float from, float to)
        {
            float delta = (to - from) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }
    }
}
