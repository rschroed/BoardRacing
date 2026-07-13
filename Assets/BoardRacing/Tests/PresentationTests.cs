using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    public sealed class PresentationTests
    {
        [Test]
        public void DisabledConditionsAlwaysMapToNormalVisuals()
        {
            var state = CarConditionVisualMapper.From(Condition(.9f, .9f), ConditionRules.Disabled);
            Assert.That(state.HeatLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(state.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));
        }

        [Test]
        public void HeatAndTireLevelsMapIndependentlyAtStableThresholds()
        {
            var rules = ConditionRules.Defaults;
            var normal = CarConditionVisualMapper.From(Condition(.1f, .1f), rules);
            Assert.That(normal.HeatLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(normal.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));

            var heatWarning = CarConditionVisualMapper.From(
                Condition(rules.HeatPenaltyThreshold * .65f, .1f), rules);
            Assert.That(heatWarning.HeatLevel, Is.EqualTo(ConditionVisualLevel.Warning));
            Assert.That(heatWarning.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));

            var tireCritical = CarConditionVisualMapper.From(
                Condition(.1f, rules.TirePenaltyThreshold), rules);
            Assert.That(tireCritical.HeatLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(tireCritical.TireLevel, Is.EqualTo(ConditionVisualLevel.Critical));
        }

        [Test]
        public void VisualMappingPreservesNormalizedConditionValues()
        {
            var state = CarConditionVisualMapper.From(Condition(.42f, .73f), ConditionRules.Defaults);
            Assert.That(state.Heat, Is.EqualTo(.42f));
            Assert.That(state.TireWear, Is.EqualTo(.73f));
        }

        [Test]
        public void SimultaneousRacersKeepIndependentConditionVisuals()
        {
            var rules = ConditionRules.Defaults;
            var playerOne = CarConditionVisualMapper.From(
                Racer(PlayerId.Player1, rules.HeatPenaltyThreshold, .1f), rules);
            var playerTwo = CarConditionVisualMapper.From(
                Racer(PlayerId.Player2, .1f, rules.TirePenaltyThreshold * .65f), rules);

            Assert.That(playerOne.HeatLevel, Is.EqualTo(ConditionVisualLevel.Critical));
            Assert.That(playerOne.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(playerTwo.HeatLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(playerTwo.TireLevel, Is.EqualTo(ConditionVisualLevel.Warning));
        }

        private static RacerConditionSnapshot Condition(float heat, float wear) =>
            new RacerConditionSnapshot(heat, wear, false, false);

        private static RacerSnapshot Racer(PlayerId id, float heat, float wear) =>
            new RacerSnapshot(id, 0f, 0f, 0, 1, false, -1f, default, 0f, false, 0f, 0,
                Condition(heat, wear), default);
    }
}
