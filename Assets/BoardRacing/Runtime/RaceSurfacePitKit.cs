using System;
using System.Collections.Generic;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    internal sealed partial class RaceSurfaceRenderer
    {
        private const float PitKitDepth = -.5f;
        private readonly Dictionary<PlayerId, PitKitRig> pitKits =
            new Dictionary<PlayerId, PitKitRig>();

        public void AttachPitComplex(PitLanePresentationLayout layout,
            IReadOnlyDictionary<PlayerId, PieceIdentity> activeIdentities = null)
        {
            if (layout.Stalls.Count > PhysicalPieceCatalog.All.Length)
                throw new ArgumentException("The pit kit supports at most four authored stalls.",
                    nameof(layout));
            if (pitKits.Count != 0)
                throw new InvalidOperationException("The pit complex is already attached.");

            for (int i = 0; i < layout.Stalls.Count; i++)
            {
                PlayerId playerId = (PlayerId)(i + 1);
                PieceIdentity identity = activeIdentities != null &&
                    activeIdentities.TryGetValue(playerId, out PieceIdentity activeIdentity)
                    ? activeIdentity
                    : PhysicalPieceCatalog.All[i];
                PitKitPlacement placement = PitKitPresentation.Placement(layout, playerId);
                pitKits.Add(playerId, CreatePitKit(playerId, identity, placement));
            }
        }

        public void SetPitPresentation(IReadOnlyList<RacerSnapshot> racers,
            float elapsedSeconds, float deltaSeconds)
        {
            foreach (KeyValuePair<PlayerId, PitKitRig> entry in pitKits)
            {
                bool active = TryFindRacer(racers, entry.Key, out RacerSnapshot racer);
                entry.Value.Apply(active, racer, elapsedSeconds,
                    Mathf.Max(0f, deltaSeconds));
            }
        }

        internal int PitKitCount => pitKits.Count;

        internal int PitKitRendererCount
        {
            get
            {
                int total = 0;
                foreach (PitKitRig rig in pitKits.Values) total += rig.RendererCount;
                return total;
            }
        }

        internal PitPresentationState PitKitState(PlayerId playerId) =>
            pitKits.TryGetValue(playerId, out PitKitRig rig)
                ? rig.State : PitPresentationState.Inactive;

        internal Transform PitKitRoot(PlayerId playerId) =>
            pitKits.TryGetValue(playerId, out PitKitRig rig) ? rig.Root : null;

        private PitKitRig CreatePitKit(PlayerId playerId, PieceIdentity identity,
            PitKitPlacement placement)
        {
            var rootObject = new GameObject("Pit Kit " + playerId);
            Transform root = rootObject.transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(
                placement.Center.x, placement.Center.y, PitKitDepth);
            root.localRotation = Quaternion.Euler(0f, 0f, placement.RotationDegrees);

            var renderers = new List<SpriteRenderer>(
                PitKitVisual.RetainedRenderersPerStall);
            SpriteRenderer wheelStop = AddPitSprite(root, "Wheel Stop",
                PitKitVisual.LoadWheelStop(), 6.25f, 1);
            wheelStop.transform.localPosition = new Vector3(17f, 0f, 0f);
            wheelStop.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // The authored stop sits beneath the front axle. Stretch its long
            // axis just beyond the 26 px car body while keeping the crossbar
            // shallow enough to read as a stop rather than a second lane mark.
            wheelStop.transform.localScale = new Vector3(1.25f, .55f, 1f);
            renderers.Add(wheelStop);

            float side = placement.OutwardSign;
            SpriteRenderer bench = AddPitSprite(root, "Full Length Service Bench",
                PitKitVisual.LoadServiceBench(), 8f, 1);
            bench.transform.localPosition = new Vector3(0f, 42f * side, 0f);
            renderers.Add(bench);

            SpriteRenderer tongue = AddPitSprite(root, "Service Connector",
                PitKitVisual.LoadServiceTongue(), 15f, 2);
            tongue.transform.localPosition = new Vector3(0f, 22f * side, 0f);
            tongue.transform.localRotation = Quaternion.Euler(
                0f, 0f, side < 0f ? 180f : 0f);
            renderers.Add(tongue);

            SpriteRenderer marker = AddPitSprite(root, "Bench Player Marker",
                PitKitVisual.LoadMarker(identity), 30.5f, 2);
            marker.transform.localPosition = new Vector3(-22f, 42f * side, 0f);
            renderers.Add(marker);

            SpriteRenderer toolArc = AddPitSprite(root, "Tool Arc",
                PitKitVisual.LoadToolArc(), 16.8f, 4);
            toolArc.transform.localPosition = new Vector3(-11f, 4f * side, 0f);
            toolArc.transform.localRotation = Quaternion.Euler(0f, 0f, 18f * side);
            renderers.Add(toolArc);

            SpriteRenderer sparks = AddPitSprite(root, "Four Spark Burst",
                PitKitVisual.LoadSparks(), 12.8f, 4);
            sparks.transform.localPosition = new Vector3(13f, 3f * side, 0f);
            renderers.Add(sparks);

            SpriteRenderer lampHalo = AddPitSprite(root, "Activity Lamp Halo",
                PitKitVisual.LoadLampHalo(), 16.5f, 3);
            lampHalo.transform.localPosition = new Vector3(3f, 42f * side, 0f);
            renderers.Add(lampHalo);

            SpriteRenderer readyRings = AddPitSprite(root, "Ready Rings",
                PitKitVisual.LoadReadyRings(), 6.4f, 4);
            renderers.Add(readyRings);

            SpriteRenderer releaseStreak = AddPitSprite(root, "Release Streak",
                PitKitVisual.LoadReleaseStreak(), 7.7f, 3);
            releaseStreak.transform.localPosition = new Vector3(-38f, 0f, 0f);
            renderers.Add(releaseStreak);

            if (renderers.Count != PitKitVisual.RetainedRenderersPerStall)
                throw new InvalidOperationException(
                    "The pit kit's retained renderer budget changed.");

            return new PitKitRig(root, identity, renderers, side, wheelStop,
                bench, tongue, marker,
                toolArc, sparks, lampHalo, readyRings, releaseStreak);
        }

        private SpriteRenderer AddPitSprite(Transform parent, string layerName,
            Texture2D texture, float pixelsPerUnit, int sortingOrder,
            Vector2? pivot = null)
        {
            Sprite sprite = Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height),
                pivot ?? new Vector2(.5f, .5f), pixelsPerUnit, 0,
                SpriteMeshType.FullRect, Vector4.zero, false);
            sprite.name = layerName;
            sprites.Add(sprite);

            var layer = new GameObject(layerName);
            layer.transform.SetParent(parent, false);
            var spriteRenderer = layer.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            ConfigureSpriteRenderer(spriteRenderer, sortingOrder);
            spriteRenderer.enabled = false;
            return spriteRenderer;
        }

        private static bool TryFindRacer(IReadOnlyList<RacerSnapshot> racers,
            PlayerId playerId, out RacerSnapshot racer)
        {
            if (racers != null)
                for (int i = 0; i < racers.Count; i++)
                    if (racers[i].PlayerId == playerId)
                    {
                        racer = racers[i];
                        return true;
                    }
            racer = default;
            return false;
        }

        private sealed class PitKitRig
        {
            private readonly PieceIdentity identity;
            private readonly IReadOnlyList<SpriteRenderer> renderers;
            private readonly float outwardSign;
            private readonly SpriteRenderer wheelStop, bench, tongue, marker;
            private readonly SpriteRenderer toolArc, sparks, lampHalo;
            private readonly SpriteRenderer readyRings, releaseStreak;
            private int completedServices;
            private float readyRemaining;
            private float serviceReach;

            public PitKitRig(Transform root, PieceIdentity identity,
                IReadOnlyList<SpriteRenderer> renderers,
                float outwardSign, SpriteRenderer wheelStop,
                SpriteRenderer bench, SpriteRenderer tongue, SpriteRenderer marker,
                SpriteRenderer toolArc, SpriteRenderer sparks,
                SpriteRenderer lampHalo, SpriteRenderer readyRings,
                SpriteRenderer releaseStreak)
            {
                Root = root;
                this.identity = identity;
                this.renderers = renderers;
                this.outwardSign = outwardSign;
                this.wheelStop = wheelStop;
                this.bench = bench;
                this.tongue = tongue;
                this.marker = marker;
                this.toolArc = toolArc;
                this.sparks = sparks;
                this.lampHalo = lampHalo;
                this.readyRings = readyRings;
                this.releaseStreak = releaseStreak;
                State = PitPresentationState.Inactive;
                Apply(false, default, 0f, 0f);
            }

            public Transform Root { get; }
            public int RendererCount => renderers.Count;
            public PitPresentationState State { get; private set; }

            public void Apply(bool active, RacerSnapshot racer,
                float elapsedSeconds, float deltaSeconds)
            {
                if (active && racer.Pit.CompletedServices > completedServices)
                    readyRemaining = PitKitPresentation.ReadyPunctuationSeconds;
                if (active) completedServices = racer.Pit.CompletedServices;
                if (!active || racer.Finished) readyRemaining = 0f;

                bool ready = readyRemaining > 0f;
                State = PitKitPresentation.Resolve(active, racer, ready);
                readyRemaining = Mathf.Max(0f, readyRemaining - deltaSeconds);

                float phase = elapsedSeconds * 7f + (int)racer.PlayerId * .9f;
                float pulse = .5f + .5f * Mathf.Sin(phase);
                float targetReach = State == PitPresentationState.Servicing ? 1f :
                    State == PitPresentationState.Ready ? .32f : 0f;
                serviceReach = Mathf.MoveTowards(serviceReach, targetReach,
                    deltaSeconds * (targetReach > serviceReach ? 4.5f : 5.5f));
                tongue.transform.localPosition = new Vector3(0f,
                    Mathf.Lerp(22f, 17f, serviceReach) * outwardSign, 0f);
                float tonguePulse = State == PitPresentationState.Servicing
                    ? 1f + .035f * pulse : 1f;
                tongue.transform.localScale = new Vector3(
                    1f, tonguePulse, 1f);

                // These are physical course pieces, so semantic inactivity
                // quiets only their lights and effects. It never fades the
                // bench, connector, stop, or identity marker out of existence.
                SetNormal(wheelStop, 1f);
                wheelStop.color = new Color(1f, .9f, .58f, wheelStop.color.a);
                SetNormal(bench, 1f);
                SetNormal(tongue, 1f);
                SetNormal(marker, 1f);
                marker.transform.localScale = Vector3.one *
                    (State == PitPresentationState.Ready ? 1.06f + .03f * pulse :
                     State == PitPresentationState.Approaching ? 1f + .025f * pulse : 1f);
                float service = State == PitPresentationState.Servicing ? 1f : 0f;
                SetEffect(toolArc, service * (.48f + .32f * pulse),
                    .88f + .12f * pulse, Color.white);
                SetEffect(sparks, service * Mathf.Pow(pulse, 3f),
                    .82f + .24f * pulse, Color.white);

                float lamp = State == PitPresentationState.Servicing
                    ? .56f + .36f * pulse
                    : State == PitPresentationState.Ready ? .95f
                    : State == PitPresentationState.Occupied ? .18f
                    : State == PitPresentationState.Approaching ? .16f + .12f * pulse
                    : State == PitPresentationState.Releasing ? .24f + .12f * pulse
                    : 0f;
                SetEffect(lampHalo, lamp, .9f + .12f * pulse, Color.white);

                float readyProgress = PitKitPresentation.ReadyPunctuationSeconds <= 0f
                    ? 0f : readyRemaining / PitKitPresentation.ReadyPunctuationSeconds;
                Color accent = PlayerColors.For(identity);
                SetEffect(readyRings,
                    State == PitPresentationState.Ready ? readyProgress : 0f,
                    1.18f - .35f * readyProgress, accent);

                float release = State == PitPresentationState.Releasing
                    ? .52f + .28f * pulse : 0f;
                SetEffect(releaseStreak, release,
                    .92f + .18f * pulse, accent);
            }

            private static void SetNormal(SpriteRenderer renderer, float opacity)
            {
                if (renderer == null || !renderer.gameObject.activeSelf) return;
                float alpha = Mathf.Clamp01(opacity);
                renderer.enabled = alpha > .002f;
                renderer.color = new Color(1f, 1f, 1f, alpha);
            }

            private static void SetEffect(SpriteRenderer renderer, float opacity,
                float scale, Color tint)
            {
                float alpha = Mathf.Clamp01(opacity);
                renderer.enabled = alpha > .002f;
                if (!renderer.enabled) return;
                tint.a = alpha;
                renderer.color = tint;
                renderer.transform.localScale = Vector3.one * scale;
            }
        }
    }
}
