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
                PitKitVisual.LoadWheelStop(), 8.1f, 1);
            wheelStop.transform.localPosition = new Vector3(34f, 0f, 0f);
            renderers.Add(wheelStop);

            SpriteRenderer rail = AddPitSprite(root, "Service Rail",
                PitKitVisual.LoadRail(), 13.79f, 1);
            rail.transform.localPosition = new Vector3(0f, 31f, 0f);
            renderers.Add(rail);

            ArmRig leftArm = CreatePitArm(root, -25f, false, renderers);
            ArmRig rightArm = CreatePitArm(root, 25f, true, renderers);

            SpriteRenderer marker = AddPitSprite(root, "Detached Player Marker",
                PitKitVisual.LoadMarker(identity), 12.95f, 2);
            marker.transform.localPosition = new Vector3(
                MarkerOffset(playerId), 57f, 0f);
            renderers.Add(marker);

            CreateDecor(root, playerId, renderers,
                out SpriteRenderer decorOne, out SpriteRenderer decorTwo);

            SpriteRenderer toolArc = AddPitSprite(root, "Tool Arc",
                PitKitVisual.LoadToolArc(), 16.8f, 4);
            toolArc.transform.localPosition = new Vector3(-13f, 3f, 0f);
            toolArc.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            renderers.Add(toolArc);

            SpriteRenderer sparks = AddPitSprite(root, "Four Spark Burst",
                PitKitVisual.LoadSparks(), 12.8f, 4);
            sparks.transform.localPosition = new Vector3(15f, 2f, 0f);
            renderers.Add(sparks);

            SpriteRenderer lampHalo = AddPitSprite(root, "Activity Lamp Halo",
                PitKitVisual.LoadLampHalo(), 12.5f, 4);
            lampHalo.transform.localPosition = new Vector3(0f, 31f, 0f);
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

            return new PitKitRig(root, identity, renderers, wheelStop,
                rail, marker, decorOne, decorTwo, leftArm, rightArm,
                toolArc, sparks, lampHalo, readyRings, releaseStreak);
        }

        private ArmRig CreatePitArm(Transform root, float x, bool mirror,
            ICollection<SpriteRenderer> renderers)
        {
            var mirrorObject = new GameObject(mirror ? "Right Arm Rig" : "Left Arm Rig");
            Transform mirrorRoot = mirrorObject.transform;
            mirrorRoot.SetParent(root, false);
            mirrorRoot.localPosition = new Vector3(x, 29f, 0f);
            mirrorRoot.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);

            SpriteRenderer pivot = AddPitSprite(mirrorRoot, "Pivot Housing",
                PitKitVisual.LoadArmPivot(), 16.25f, 2);
            renderers.Add(pivot);

            SpriteRenderer upper = AddPitSprite(mirrorRoot, "Upper Arm",
                PitKitVisual.LoadArmUpper(), 11.86f, 3,
                new Vector2(.142f, .5f));
            renderers.Add(upper);

            SpriteRenderer forearm = AddPitSprite(upper.transform, "Forearm",
                PitKitVisual.LoadArmForearm(), 12.08f, 3,
                new Vector2(.145f, .5f));
            forearm.transform.localPosition = new Vector3(24f, 0f, 0f);
            renderers.Add(forearm);

            SpriteRenderer tool = AddPitSprite(forearm.transform, "Tool Head",
                PitKitVisual.LoadToolHead(), 13.2f, 3,
                new Vector2(.18f, .5f));
            tool.transform.localPosition = new Vector3(22f, 0f, 0f);
            renderers.Add(tool);
            return new ArmRig(upper.transform, forearm.transform, tool.transform);
        }

        private void CreateDecor(Transform root, PlayerId playerId,
            ICollection<SpriteRenderer> renderers, out SpriteRenderer first,
            out SpriteRenderer second)
        {
            int index = (int)playerId - 1;
            Texture2D firstTexture = index % 2 == 0
                ? PitKitVisual.LoadTirePile() : PitKitVisual.LoadToolCart();
            Texture2D secondTexture = index < 2
                ? PitKitVisual.LoadJackAndTire() : PitKitVisual.LoadToolCart();
            float firstPpu = index % 2 == 0 ? 15.9f : 13.4f;
            float secondPpu = index < 2 ? 14.8f : 13.4f;

            first = AddPitSprite(root, "Pit Decoration A", firstTexture, firstPpu, 1);
            first.transform.localPosition = new Vector3(
                index % 2 == 0 ? 36f : -36f, 48f, 0f);
            first.transform.localRotation = Quaternion.Euler(
                0f, 0f, index % 2 == 0 ? 7f : -6f);
            renderers.Add(first);

            second = AddPitSprite(root, "Pit Decoration B", secondTexture, secondPpu, 1);
            second.transform.localPosition = new Vector3(
                index % 2 == 0 ? -37f : 37f, 49f, 0f);
            second.transform.localRotation = Quaternion.Euler(
                0f, 0f, index % 2 == 0 ? -5f : 8f);
            // Alternating one- and two-prop bays keep the complex from reading
            // as a repeated stamp while the retained object budget stays fixed.
            second.gameObject.SetActive(index % 2 == 0);
            renderers.Add(second);
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

        private static float MarkerOffset(PlayerId playerId)
        {
            switch (playerId)
            {
                case PlayerId.Player1: return -15f;
                case PlayerId.Player2: return 18f;
                case PlayerId.Player3: return -10f;
                default: return 14f;
            }
        }

        private sealed class ArmRig
        {
            public ArmRig(Transform upper, Transform forearm, Transform tool)
            {
                Upper = upper;
                Forearm = forearm;
                Tool = tool;
            }

            public Transform Upper { get; }
            public Transform Forearm { get; }
            public Transform Tool { get; }

            public void Apply(float reach, float beat)
            {
                Upper.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(5f, -54f + beat * 6f, reach));
                Forearm.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(166f, 72f - beat * 9f, reach));
                Tool.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(-160f, -22f + beat * 5f, reach));
            }
        }

        private sealed class PitKitRig
        {
            private readonly PieceIdentity identity;
            private readonly IReadOnlyList<SpriteRenderer> renderers;
            private readonly SpriteRenderer wheelStop, rail, marker;
            private readonly SpriteRenderer decorOne, decorTwo;
            private readonly ArmRig leftArm, rightArm;
            private readonly SpriteRenderer toolArc, sparks, lampHalo;
            private readonly SpriteRenderer readyRings, releaseStreak;
            private int completedServices;
            private float readyRemaining;
            private float armReach;

            public PitKitRig(Transform root, PieceIdentity identity,
                IReadOnlyList<SpriteRenderer> renderers,
                SpriteRenderer wheelStop, SpriteRenderer rail,
                SpriteRenderer marker,
                SpriteRenderer decorOne, SpriteRenderer decorTwo,
                ArmRig leftArm, ArmRig rightArm,
                SpriteRenderer toolArc, SpriteRenderer sparks,
                SpriteRenderer lampHalo, SpriteRenderer readyRings,
                SpriteRenderer releaseStreak)
            {
                Root = root;
                this.identity = identity;
                this.renderers = renderers;
                this.wheelStop = wheelStop;
                this.rail = rail;
                this.marker = marker;
                this.decorOne = decorOne;
                this.decorTwo = decorTwo;
                this.leftArm = leftArm;
                this.rightArm = rightArm;
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
                armReach = Mathf.MoveTowards(armReach, targetReach,
                    deltaSeconds * (targetReach > armReach ? 4.5f : 5.5f));
                float beat = Mathf.Sin(phase * 1.18f);
                leftArm.Apply(armReach, beat);
                rightArm.Apply(armReach, -beat);

                float activeAlpha = State == PitPresentationState.Inactive ? .34f : 1f;
                SetNormal(wheelStop, .78f * activeAlpha);
                SetNormal(rail, .84f * activeAlpha);
                SetNormal(marker, State == PitPresentationState.Inactive ? .28f :
                    State == PitPresentationState.Approaching ? .84f + .16f * pulse : 1f);
                marker.transform.localScale = Vector3.one *
                    (State == PitPresentationState.Ready ? 1.06f + .03f * pulse :
                     State == PitPresentationState.Approaching ? 1f + .025f * pulse : 1f);
                SetNormal(decorOne, .82f * activeAlpha);
                if (decorTwo.gameObject.activeSelf)
                    SetNormal(decorTwo, .78f * activeAlpha);

                for (int i = 0; i < renderers.Count; i++)
                {
                    SpriteRenderer renderer = renderers[i];
                    if (renderer == wheelStop ||
                        renderer == rail || renderer == marker ||
                        renderer == decorOne || renderer == decorTwo ||
                        renderer == toolArc || renderer == sparks ||
                        renderer == lampHalo || renderer == readyRings ||
                        renderer == releaseStreak)
                        continue;
                    SetNormal(renderer, (.78f + .22f * armReach) * activeAlpha);
                }

                float service = State == PitPresentationState.Servicing ? 1f : 0f;
                SetEffect(toolArc, service * (.48f + .32f * pulse),
                    .88f + .12f * pulse, Color.white);
                SetEffect(sparks, service * Mathf.Pow(pulse, 3f),
                    .82f + .24f * pulse, Color.white);

                float lamp = State == PitPresentationState.Servicing
                    ? .5f + .3f * pulse
                    : State == PitPresentationState.Ready ? .95f
                    : State == PitPresentationState.Approaching ? .16f + .12f * pulse
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
