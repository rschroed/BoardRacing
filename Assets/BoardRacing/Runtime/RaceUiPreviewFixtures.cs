#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BoardRacing.Domain;

namespace BoardRacing.Runtime
{
    internal enum RaceUiPreviewScenario
    {
        Grid,
        Countdown,
        StableRacing,
        Warning,
        Critical,
        PitCallHolding,
        InService,
        SplitFinish,
        Results,
        RematchRelease,
        Paused
    }

    internal readonly struct RaceUiPreviewFrame
    {
        public RaceUiPreviewFrame(RaceSnapshot race, IReadOnlyList<PlayerControlSnapshot> controls,
            IReadOnlyDictionary<PlayerId, CrewStrategyOutput> crewOutputs, RaceUiModel ui)
        {
            Race = race;
            Controls = controls;
            CrewOutputs = crewOutputs;
            Ui = ui;
        }

        public RaceSnapshot Race { get; }
        public IReadOnlyList<PlayerControlSnapshot> Controls { get; }
        public IReadOnlyDictionary<PlayerId, CrewStrategyOutput> CrewOutputs { get; }
        public RaceUiModel Ui { get; }
    }

    internal static class RaceUiPreviewFixtures
    {
        public static RaceUiPreviewFrame Create(RaceUiPreviewScenario scenario, TrackDefinition track,
            ConditionRules conditionRules, int laps)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));

            RacePhase phase = RacePhase.Racing;
            float countdown = 0f;
            float elapsed = 12f;
            bool awaitingRelease = false;
            bool shipsPresent = true;
            RacerSnapshot playerOne = Racer(PlayerId.Player1, track.Sample(540f), place: 1);
            RacerSnapshot playerTwo = Racer(PlayerId.Player2, track.Sample(1080f), place: 2);
            var crew = new Dictionary<PlayerId, CrewStrategyOutput>
            {
                [PlayerId.Player1] = new CrewStrategyOutput(PitService.None, false,
                    PitCallState.NeedsPlacement, default, default),
                [PlayerId.Player2] = new CrewStrategyOutput(PitService.None, false,
                    PitCallState.NeedsPlacement, default, default)
            };

            switch (scenario)
            {
                case RaceUiPreviewScenario.Grid:
                    phase = RacePhase.Grid;
                    elapsed = 0f;
                    break;
                case RaceUiPreviewScenario.Countdown:
                    phase = RacePhase.Countdown;
                    countdown = 2f;
                    elapsed = 0f;
                    break;
                case RaceUiPreviewScenario.Warning:
                    playerOne = Racer(PlayerId.Player1, track.Sample(540f), place: 1,
                        fuelUsed: Math.Min(.99f, conditionRules.FuelWarningThreshold + .1f));
                    break;
                case RaceUiPreviewScenario.Critical:
                    playerOne = Racer(PlayerId.Player1, track.Sample(540f), place: 1,
                        fuelUsed: 1f, fuelEmpty: true);
                    break;
                case RaceUiPreviewScenario.PitCallHolding:
                    crew[PlayerId.Player1] = new CrewStrategyOutput(PitService.None, false,
                        PitCallState.Holding, new PitActionResult(PitActionState.Holding, .55f, false), default);
                    break;
                case RaceUiPreviewScenario.InService:
                    playerOne = Racer(PlayerId.Player1, track.Sample(0f), place: 1,
                        pitPhase: PitPhase.InService, selectedService: PitService.Tires,
                        serviceProgress: .55f, tireWear: .45f);
                    crew[PlayerId.Player1] = new CrewStrategyOutput(PitService.Tires, false,
                        PitCallState.NeedsPlacement, default,
                        new PitActionResult(PitActionState.Stirring, 0f, false), .002f);
                    break;
                case RaceUiPreviewScenario.SplitFinish:
                    playerOne = Racer(PlayerId.Player1, track.Sample(0f), place: 1,
                        finished: true, finishEligible: true);
                    break;
                case RaceUiPreviewScenario.Results:
                    phase = RacePhase.Finished;
                    playerOne = Racer(PlayerId.Player1, track.Sample(0f), place: 1,
                        finished: true, finishEligible: true);
                    playerTwo = Racer(PlayerId.Player2, track.Sample(0f), place: 2,
                        finished: true, finishEligible: true);
                    break;
                case RaceUiPreviewScenario.RematchRelease:
                    phase = RacePhase.Finished;
                    awaitingRelease = true;
                    playerOne = Racer(PlayerId.Player1, track.Sample(0f), place: 1,
                        finished: true, finishEligible: true);
                    playerTwo = Racer(PlayerId.Player2, track.Sample(0f), place: 2,
                        finished: true, finishEligible: true);
                    break;
                case RaceUiPreviewScenario.Paused:
                    // A mixed pause: the finished racer's Ship may stay off the table
                    // (pause choices), the unfinished racer is asked to replace theirs.
                    phase = RacePhase.Paused;
                    playerOne = Racer(PlayerId.Player1, track.Sample(540f), place: 2);
                    playerTwo = Racer(PlayerId.Player2, track.Sample(0f), place: 1,
                        finished: true, finishEligible: true);
                    shipsPresent = false;
                    break;
            }

            IReadOnlyList<PlayerControlSnapshot> controls = Controls(shipsPresent);
            var race = new RaceSnapshot(phase, countdown, elapsed, new[] { playerOne, playerTwo },
                awaitingRelease ? 1f : 0f, awaitingRelease);
            RaceUiModel ui = RaceUiModelBuilder.Build(race, controls, crew, conditionRules, laps);
            return new RaceUiPreviewFrame(race, controls, crew, ui);
        }

        private static IReadOnlyList<PlayerControlSnapshot> Controls(bool shipsPresent = true)
        {
            PieceState Present(int contact) => new PieceState(true, false, contact, new Vec2(), 0f);
            PieceState Ship(int contact) => shipsPresent ? Present(contact) : PieceState.Missing;
            return new[]
            {
                new PlayerControlSnapshot(PlayerId.Player1, ThrottleStep.Drive,
                    Ship(1), Present(2), InputWarning.None),
                new PlayerControlSnapshot(PlayerId.Player2, ThrottleStep.Boost,
                    Ship(3), Present(4), InputWarning.None)
            };
        }

        private static RacerSnapshot Racer(PlayerId id, TrackSample track, int place,
            PitPhase pitPhase = PitPhase.OnTrack, PitService selectedService = PitService.None,
            float serviceProgress = 0f, float fuelUsed = .2f, float tireWear = .2f,
            bool fuelEmpty = false, bool tireCritical = false, bool finished = false,
            bool finishEligible = false)
        {
            var condition = new RacerConditionSnapshot(fuelUsed, tireWear, fuelEmpty, tireCritical);
            var pit = new RacerPitSnapshot(selectedService, pitPhase, serviceProgress,
                finishEligible ? 1 : 0, finishEligible, 0f);
            return new RacerSnapshot(id, finished ? 0f : 100f, 100f, 2, place, finished,
                finished ? 20f + place : -1f, track, 0f, false, 0f, 0, condition, pit);
        }
    }
}
#endif
