using System;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    // The slipstream tow (issue #118): trailing within the window of any car
    // ahead on a straight adds a bonus to the throttle target — the only way
    // past a leader running the same throttle, and self-balancing because
    // the passer becomes the passee.
    public sealed class SlipstreamTests
    {
        private const float MaxSpeed = 100f;
        private const float Bonus = 20f;
        private const float Window = 150f;

        [Test]
        public void ATrailingCarInsideTheWindowGainsTheTow()
        {
            var simulation = StraightLoopSimulation();
            SeparateBy(simulation, 100f);
            for (int i = 0; i < 100; i++) simulation.Step(.01f, BothBoost());
            Assert.That(Racer(simulation, PlayerId.Player2).Speed, Is.GreaterThan(MaxSpeed + 10f),
                "the trailing car runs beyond top speed in the tow");
            Assert.That(Racer(simulation, PlayerId.Player1).Speed, Is.EqualTo(MaxSpeed).Within(.1f),
                "the leader gains nothing");
            float gap = Racer(simulation, PlayerId.Player1).TotalDistance -
                Racer(simulation, PlayerId.Player2).TotalDistance;
            Assert.That(gap, Is.LessThan(100f), "the tow actually reels the leader in");
        }

        [Test]
        public void NoTowAtDeadHeatOrBeyondTheWindow()
        {
            var simulation = StraightLoopSimulation();
            for (int i = 0; i < 200; i++) simulation.Step(.01f, BothBoost());
            Assert.That(Racer(simulation, PlayerId.Player1).Speed, Is.LessThanOrEqualTo(MaxSpeed + .001f),
                "side-by-side is racing, not drafting");
            Assert.That(Racer(simulation, PlayerId.Player2).Speed, Is.LessThanOrEqualTo(MaxSpeed + .001f));

            var gapped = StraightLoopSimulation();
            SeparateBy(gapped, 400f);
            for (int i = 0; i < 100; i++) gapped.Step(.01f, BothBoost());
            Assert.That(Racer(gapped, PlayerId.Player2).Speed, Is.LessThanOrEqualTo(MaxSpeed + .001f),
                "out of the window, out of the tow");
        }

        [Test]
        public void TheTowEndsInCorners()
        {
            // Straight then a wide-open corner (safe speed far above the tow,
            // so no scrub muddies the read): the trailing car's tow drops the
            // moment it is cornering — corners belong to #117's character.
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(500f, 0f), TrackSectionKind.Straight,
                    float.PositiveInfinity),
                new TrackSegment(new Vec2(500f, 0f), new Vec2(0f, 0f), TrackSectionKind.Corner, 500f)
            });
            var simulation = new RaceSimulation(track, SlipstreamRules());
            Start(simulation);
            SeparateBy(simulation, 100f);
            int cornering = 0, guard = 0;
            while (cornering < 60 && guard++ < 10000)
            {
                simulation.Step(.01f, BothBoost());
                cornering = Racer(simulation, PlayerId.Player2).Track.Kind == TrackSectionKind.Corner
                    ? cornering + 1 : 0;
            }
            Assert.That(guard, Is.LessThan(10000));
            Assert.That(Racer(simulation, PlayerId.Player2).Speed, Is.LessThanOrEqualTo(MaxSpeed + 1f));
        }

        [Test]
        public void CarsInThePitComplexGiveNoTow()
        {
            // The leader parked in its box is not punching a hole in the air:
            // a car bearing down on the pit line gets no tow from it.
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(500f, 0f), TrackSectionKind.Straight,
                    float.PositiveInfinity),
                new TrackSegment(new Vec2(500f, 0f), new Vec2(0f, 0f), TrackSectionKind.Straight,
                    float.PositiveInfinity)
            });
            var rules = new RaceRules(3, 0f, MaxSpeed, 1000f, 100f, 100f, .5f, .2f, .5f, 180f, 16f, 1f,
                1, new ConditionRules(.1f, .2f, .8f, .8f, .8f, .2f, .2f, .2f, .8f),
                new PitRules(30f, 15f, 15f, 15f, 15f), 2f, Bonus, Window);
            var simulation = new RaceSimulation(track, rules);
            Start(simulation);
            // P1 calls the pit and dives in at the line; P2 lags behind it.
            int guard = 0;
            while (Racer(simulation, PlayerId.Player1).Pit.Phase != PitPhase.InService && guard++ < 20000)
                simulation.Step(.01f, Commands(ThrottleStep.Boost, ThrottleStep.Drive, pitOne: true));
            Assert.That(guard, Is.LessThan(20000));
            // P2 now runs the straight toward the line P1 sits frozen at.
            float towed = 0f;
            for (int i = 0; i < 300; i++)
            {
                simulation.Step(.01f, Commands(ThrottleStep.Brake, ThrottleStep.Boost));
                towed = Math.Max(towed, Racer(simulation, PlayerId.Player2).Speed);
            }
            Assert.That(towed, Is.LessThanOrEqualTo(MaxSpeed + .001f));
        }

        [Test]
        public void JockeyingIsSelfBalancing()
        {
            // The passer becomes the passee: from one small nudge apart, two
            // equal cars trade the lead all race instead of freezing in file.
            var simulation = StraightLoopSimulation();
            SeparateBy(simulation, 40f);
            int leadChanges = 0;
            float previousSign = Math.Sign(
                Racer(simulation, PlayerId.Player1).TotalDistance -
                Racer(simulation, PlayerId.Player2).TotalDistance);
            for (int i = 0; i < 3000; i++)
            {
                simulation.Step(.01f, BothBoost());
                float sign = Math.Sign(
                    Racer(simulation, PlayerId.Player1).TotalDistance -
                    Racer(simulation, PlayerId.Player2).TotalDistance);
                if (sign != 0f && sign != previousSign) { leadChanges++; previousSign = sign; }
            }
            Assert.That(leadChanges, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void TheTowRidesThePaceDial()
        {
            var settings = TrancheTwoSettings.Defaults();
            RaceRules reference = settings.ToRules(5);
            Assert.That(reference.SlipstreamBonus,
                Is.EqualTo(Pace.BasePace * Pace.SlipstreamBonusRatio).Within(.001f));
            Assert.That(reference.SlipstreamWindow, Is.EqualTo(RaceRules.DefaultSlipstreamWindow));
            settings.basePace = Pace.BasePace * 2f;
            Assert.That(settings.ToRules(5).SlipstreamBonus,
                Is.EqualTo(reference.SlipstreamBonus * 2f).Within(.001f),
                "the tow scales with the one dial");
            Assert.That(settings.ToRules(5).SlipstreamWindow, Is.EqualTo(reference.SlipstreamWindow),
                "the window is geometry and stays put");
        }

        [Test]
        public void ShippedSettingsAssetMirrorsTheDomainReference()
        {
            // The device build reads the serialized asset, not the domain
            // defaults — and they CAN drift: the #117 split retune landed in
            // RaceRules.Defaults while the asset kept the old 38 px offset,
            // so hardware reviews ran different numbers than the tests. This
            // pin makes the shipped asset follow the domain reference.
            var settings = Resources.Load<TrancheTwoSettings>("TrancheTwoSettings");
            Assert.That(settings, Is.Not.Null);
            RaceRules reference = RaceRules.Defaults;
            Assert.That(settings.basePace, Is.EqualTo(Pace.BasePace));
            Assert.That(settings.passingDistance, Is.EqualTo(reference.PassingDistance));
            Assert.That(settings.passingOffset, Is.EqualTo(reference.PassingOffset));
            Assert.That(settings.accelerationRatio, Is.EqualTo(Pace.AccelerationRatio).Within(1e-5f));
            Assert.That(settings.dragRatio, Is.EqualTo(Pace.DragRatio).Within(1e-5f));
            Assert.That(settings.brakingRatio, Is.EqualTo(Pace.BrakingRatio).Within(1e-5f));
            Assert.That(settings.cornerSafeSpeedRatio, Is.EqualTo(Pace.CornerSafeSpeedRatio).Within(1e-5f));
            Assert.That(settings.slipstreamBonusRatio, Is.EqualTo(Pace.SlipstreamBonusRatio).Within(1e-5f));
            Assert.That(settings.slipstreamWindow, Is.EqualTo(RaceRules.DefaultSlipstreamWindow));
        }

        // A 2000 px all-straight loop: room for a leader's wake to never wrap
        // around and tow the leader itself.
        private static RaceSimulation StraightLoopSimulation()
        {
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(1000f, 0f), TrackSectionKind.Straight,
                    float.PositiveInfinity),
                new TrackSegment(new Vec2(1000f, 0f), new Vec2(0f, 0f), TrackSectionKind.Straight,
                    float.PositiveInfinity)
            });
            var simulation = new RaceSimulation(track, SlipstreamRules());
            Start(simulation);
            return simulation;
        }

        private static RaceRules SlipstreamRules() =>
            new RaceRules(100, 0f, MaxSpeed, 1000f, 100f, 100f, .5f, .2f, .5f, 180f, 16f, 1f,
                0, default, default, 2f, Bonus, Window);

        private static void Start(RaceSimulation simulation)
        {
            simulation.Step(.01f, Commands(ThrottleStep.Brake, ThrottleStep.Brake));
            simulation.Step(.01f, Commands(ThrottleStep.Brake, ThrottleStep.Brake));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Racing));
        }

        // Opens a gap with Player 1 ahead by the requested px, then lets the
        // speeds settle back to parity before the caller measures.
        private static void SeparateBy(RaceSimulation simulation, float gap)
        {
            int guard = 0;
            while (Racer(simulation, PlayerId.Player1).TotalDistance -
                Racer(simulation, PlayerId.Player2).TotalDistance < gap && guard++ < 20000)
                simulation.Step(.01f, Commands(ThrottleStep.Boost, ThrottleStep.Brake));
            Assert.That(guard, Is.LessThan(20000));
        }

        private static RacerSnapshot Racer(RaceSimulation simulation, PlayerId id) =>
            simulation.Snapshot.Racers.Single(x => x.PlayerId == id);

        private static RacerCommand[] BothBoost() => Commands(ThrottleStep.Boost, ThrottleStep.Boost);

        private static RacerCommand[] Commands(ThrottleStep one, ThrottleStep two, bool pitOne = false) => new[]
        {
            new RacerCommand(PlayerId.Player1, one, true, false, PitService.Tires, pitOne, 1f, false),
            new RacerCommand(PlayerId.Player2, two, true, false, PitService.None, false, 0f, false)
        };
    }
}
