using System.Collections.Generic;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BoardRacing.Tests
{
    // The uGUI seat HUD (issue #86, round 3) replaces the IMGUI arc/dial
    // drawing; these tests pin the two things the migration must not change:
    // RingGraphic meshes must honor the IMGUI screen-angle convention every
    // seat measurement is expressed in, and Apply must bind RaceUiModel to the
    // same lit/ghosted/progress reads the old DrawCrewRegions and
    // DrawCornerController code paths produced.
    public sealed class RaceHudTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void RingMeshFollowsImguiScreenAngles()
        {
            RingGraphic ring = CreateRing();
            ring.Radius = 100f;
            ring.Thickness = 10f;
            ring.StartAngle = -90f;
            ring.SweepAngle = 90f;
            List<UIVertex> vertices = Vertices(ring);
            Assert.That(vertices.Count, Is.GreaterThan(0));
            // IMGUI -90° points up the screen; in the canvas's y-up local space
            // that is +y. The sweep ends at 0°, straight along +x.
            Assert.That((Vector2)vertices[1].position,
                Is.EqualTo(new Vector2(0f, 105f)).Using(Vector2Comparer));
            Assert.That((Vector2)vertices[vertices.Count - 1].position,
                Is.EqualTo(new Vector2(105f, 0f)).Using(Vector2Comparer));
            foreach (UIVertex vertex in vertices)
            {
                float distance = ((Vector2)vertex.position).magnitude;
                Assert.That(distance, Is.InRange(94.9f, 105.1f), "vertex off the annulus");
                Assert.That(vertex.position.x, Is.GreaterThanOrEqualTo(-.01f));
                Assert.That(vertex.position.y, Is.GreaterThanOrEqualTo(-.01f),
                    "quarter arc left its quadrant");
            }
        }

        [Test]
        public void FullRingClosesOnItself()
        {
            RingGraphic ring = CreateRing();
            ring.Radius = 46f;
            ring.Thickness = 10f;
            ring.StartAngle = 0f;
            ring.SweepAngle = 360f;
            List<UIVertex> vertices = Vertices(ring);
            Assert.That((Vector2)vertices[0].position,
                Is.EqualTo((Vector2)vertices[vertices.Count - 2].position).Using(Vector2Comparer));
            Assert.That((Vector2)vertices[1].position,
                Is.EqualTo((Vector2)vertices[vertices.Count - 1].position).Using(Vector2Comparer));
        }

        [Test]
        public void RacingSeatLightsTheHeldSectorAndCallPit()
        {
            RaceHud hud = CreateHud();
            hud.Apply(Ui(RacePhase.Racing,
                Player(PlayerId.Player1, throttle: ThrottleStep.Drive, tireWear: .37f),
                Player(PlayerId.Player2, throttle: ThrottleStep.Brake, shipPresent: false)));

            SeatHud seat = hud.PlayerOne;
            Assert.That(seat.Drive.ActiveFill.enabled, Is.True);
            Assert.That(seat.Drive.Band.enabled, Is.False);
            Assert.That(seat.Brake.ActiveFill.enabled, Is.False);
            Assert.That(seat.Boost.ActiveFill.enabled, Is.False);
            Assert.That(seat.Brake.Band.enabled, Is.True);
            Assert.That(seat.CallPitRing.color, Is.EqualTo(seat.Accent));
            Assert.That(seat.CallPitLabel.text, Is.EqualTo("CALL PIT"));
            Assert.That(seat.CallPitHold.enabled, Is.False);
            Assert.That(seat.Tires.Fill.enabled, Is.True);
            Assert.That(seat.Tires.Fill.SweepAngle, Is.EqualTo(360f * .37f).Within(.01f));
            Assert.That(seat.Tires.Value.text, Is.EqualTo("37"));
            Assert.That(seat.Tires.ServiceRing.enabled, Is.False,
                "service targets only surround the dials while parked");
            Assert.That(seat.ShipWell.color.a, Is.EqualTo(.8f).Within(.001f));
            Assert.That(hud.PlayerTwo.ShipWell.color, Is.EqualTo(RaceHud.GhostColor),
                "an absent Ship ghosts the well ring");
        }

        [Test]
        public void ParkedSeatMarksServiceTargetsAndProgress()
        {
            RaceHud hud = CreateHud();
            hud.Apply(Ui(RacePhase.Racing,
                Player(PlayerId.Player1, pitPhase: PitPhase.InService,
                    selected: PitService.Tires, serviceProgress: .5f,
                    serviceAction: new PitActionResult(PitActionState.Stirring, .5f, false)),
                Player(PlayerId.Player2)));

            SeatHud seat = hud.PlayerOne;
            Assert.That(seat.Tires.ServiceRing.enabled, Is.True);
            Assert.That(seat.Tires.ServiceRing.color, Is.EqualTo(Color.white),
                "the selected service ring emphasizes in white");
            Assert.That(seat.Fuel.ServiceRing.enabled, Is.True);
            Assert.That(seat.Fuel.ServiceRing.color, Is.EqualTo(seat.Accent));
            Assert.That(seat.Tires.ProgressRing.enabled, Is.True);
            Assert.That(seat.Tires.ProgressRing.SweepAngle, Is.EqualTo(180f).Within(.01f));
            Assert.That(seat.Fuel.ProgressRing.enabled, Is.False);
            Assert.That(seat.CallPitLabel.text, Is.EqualTo("LEAVE PIT"));
            // A parked car's throttle is locked: no lit sector, neutral bands.
            Assert.That(seat.Drive.ActiveFill.enabled, Is.False);
            Assert.That(seat.Drive.Band.enabled, Is.True);
            Assert.That(seat.Drive.Band.color, Is.EqualTo(new Color(.16f, .19f, .24f)));
        }

        [Test]
        public void HoldingThePitCallShowsTheProgressRing()
        {
            RaceHud hud = CreateHud();
            hud.Apply(Ui(RacePhase.Racing,
                Player(PlayerId.Player1, callState: PitCallState.Holding,
                    callAction: new PitActionResult(PitActionState.Holding, .25f, false)),
                Player(PlayerId.Player2)));

            SeatHud seat = hud.PlayerOne;
            Assert.That(seat.CallPitRing.color, Is.EqualTo(Color.white),
                "an active hold emphasizes the Call Pit circle");
            Assert.That(seat.CallPitHold.enabled, Is.True);
            Assert.That(seat.CallPitHold.SweepAngle, Is.EqualTo(90f).Within(.01f));
        }

        [Test]
        public void OppositeSeatBuildsAtTheMirroredGeometry()
        {
            RaceHud hud = CreateHud();
            // Mirrored ship well measured in WireframePresentationTests: (133, 142)
            // in reference screen space, which anchors at (x, -y) on the canvas.
            RectTransform well = hud.PlayerTwo.ShipWell.rectTransform;
            Assert.That(well.anchoredPosition,
                Is.EqualTo(new Vector2(133f, -142f)).Using(Vector2Comparer));
            // The opposite seat's readouts turn 180° to read from its edge.
            float z = hud.PlayerTwo.Tires.Value.rectTransform.localEulerAngles.z;
            Assert.That(Mathf.DeltaAngle(z, 180f), Is.EqualTo(0f).Within(.01f));
            Assert.That(hud.PlayerOne.Tires.Value.rectTransform.localEulerAngles.z,
                Is.EqualTo(0f).Within(.01f));
        }

        private static readonly IEqualityComparer<Vector2> Vector2Comparer =
            new Vector2EqualityComparer();

        private sealed class Vector2EqualityComparer : IEqualityComparer<Vector2>
        {
            public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < .01f;
            public int GetHashCode(Vector2 v) => v.GetHashCode();
        }

        private RingGraphic CreateRing()
        {
            var go = new GameObject("Ring", typeof(RectTransform));
            spawned.Add(go);
            return go.AddComponent<RingGraphic>();
        }

        private static List<UIVertex> Vertices(RingGraphic ring)
        {
            var helper = new VertexHelper();
            ring.Populate(helper);
            var vertices = new List<UIVertex>();
            for (int i = 0; i < helper.currentVertCount; i++)
            {
                UIVertex vertex = default;
                helper.PopulateUIVertex(ref vertex, i);
                vertices.Add(vertex);
            }
            helper.Dispose();
            return vertices;
        }

        private RaceHud CreateHud()
        {
            RaceHud hud = RaceHud.Create(Layout(), new Color(.92f, .39f, .12f),
                new Color(.48f, .28f, .72f));
            spawned.Add(hud.gameObject);
            return hud;
        }

        private static RaceLayout Layout() => RaceLayout.Create(
            new ServiceTargets(new Vector2(1832f, 398f), new Vector2(1692f, 321f),
                new Vector2(1590f, 212f)),
            new ServiceTargets(new Vector2(88f, 682f), new Vector2(228f, 759f),
                new Vector2(330f, 868f)),
            new Vector2(50f, 50f));

        private static RaceUiModel Ui(RacePhase phase, PlayerUiModel playerOne,
            PlayerUiModel playerTwo) =>
            new RaceUiModel(phase, playerOne, playerTwo, CenterMessageKind.None, null);

        private static PlayerUiModel Player(PlayerId id,
            ThrottleStep throttle = ThrottleStep.Drive, PitPhase pitPhase = PitPhase.OnTrack,
            PitService selected = PitService.None, float serviceProgress = 0f,
            PitCallState callState = PitCallState.Unavailable,
            PitActionResult callAction = default, PitActionResult serviceAction = default,
            bool shipPresent = true, float tireWear = .2f, float fuelUsed = .2f) =>
            new PlayerUiModel(id, "IDENTITY", "STATUS", PlayerUiInstructionKind.DriveAndPit,
                "DRIVE WITH SHIP · ROBOT CAN CALL PIT", throttle,
                new CarConditionVisualState(fuelUsed, tireWear, ConditionVisualLevel.Normal,
                    ConditionVisualLevel.Normal),
                pitPhase, selected, serviceProgress, callState, callAction, serviceAction,
                shipPresent, true, InputWarning.None, false, false);
    }
}
