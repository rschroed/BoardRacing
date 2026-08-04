using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    internal enum PitPresentationState
    {
        Inactive,
        Active,
        Approaching,
        Occupied,
        Servicing,
        Ready,
        Releasing,
        Finished
    }

    internal readonly struct PitKitPlacement
    {
        public PitKitPlacement(Vector2 center, Vector2 heading, Vector2 outward)
        {
            Center = center;
            Heading = heading;
            Outward = outward;
            RotationDegrees = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
        }

        public Vector2 Center { get; }
        public Vector2 Heading { get; }
        public Vector2 Outward { get; }
        public float RotationDegrees { get; }
    }

    internal static class PitKitPresentation
    {
        public const float ReadyPunctuationSeconds = .8f;

        public static PitKitPlacement Placement(
            PitLanePresentationLayout layout, PlayerId playerId)
        {
            Vec2 authoredCenter = layout.Box(playerId);
            Vec2 authoredHeading = layout.ParkedHeading(playerId);
            Vec2 authoredLane = layout.LaneAnchor(playerId);
            var center = new Vector2(authoredCenter.X, authoredCenter.Y);
            var heading = new Vector2(authoredHeading.X, authoredHeading.Y);
            if (heading.sqrMagnitude < .0001f) heading = Vector2.right;
            else heading.Normalize();

            // Keep the kit orthogonal to the parked car, but choose the side
            // away from the shared lane. This makes the same prefab-like rig
            // valid for horizontal, angled, and curved authored pit complexes.
            var outward = new Vector2(-heading.y, heading.x);
            var awayFromLane = center -
                new Vector2(authoredLane.X, authoredLane.Y);
            if (Vector2.Dot(outward, awayFromLane) < 0f) outward = -outward;
            return new PitKitPlacement(center, heading, outward);
        }

        public static PitPresentationState Resolve(
            bool active, RacerSnapshot racer, bool readyPunctuation)
        {
            if (!active) return PitPresentationState.Inactive;
            if (racer.Finished || racer.Pit.Phase == PitPhase.Parked)
                return PitPresentationState.Finished;
            if (racer.Pit.Phase == PitPhase.Exiting)
                return PitPresentationState.Releasing;
            if (racer.Pit.Phase == PitPhase.Requested ||
                racer.Pit.Phase == PitPhase.Entering ||
                racer.Pit.Phase == PitPhase.Parking)
                return PitPresentationState.Approaching;
            if (racer.Pit.Phase == PitPhase.InService)
            {
                if (readyPunctuation) return PitPresentationState.Ready;
                if (racer.Pit.SelectedService != PitService.None &&
                    racer.Pit.ServiceProgress > .001f &&
                    racer.Pit.ServiceProgress < .999f)
                    return PitPresentationState.Servicing;
                return PitPresentationState.Occupied;
            }
            return PitPresentationState.Active;
        }
    }
}
