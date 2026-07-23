using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BoardRacing.Tests
{
    // Pins the framework side of RingGraphic: a Graphic whose GameObject lacks
    // a CanvasRenderer renders NOTHING, silently — RequireComponent is not
    // inherited from Graphic, so dropping the attribute from RingGraphic
    // reproduces the invisible seat HUD from the round 3 capture review (#86).
    // This drives the real rebuild (Canvas.ForceUpdateCanvases) rather than
    // calling Populate directly the way RaceHudTests does.
    public sealed class RaceHudProbeTests
    {
        private sealed class MeshSpy : MonoBehaviour, IMeshModifier
        {
            public int VertexCount = -1;
            public void ModifyMesh(Mesh mesh) { VertexCount = mesh.vertexCount; }
            public void ModifyMesh(VertexHelper vh) { VertexCount = vh.currentVertCount; }
        }

        [Test]
        public void RingGraphicPushesGeometryThroughTheRealRebuild()
        {
            var canvasGo = new GameObject("Probe Canvas", typeof(Canvas));
            try
            {
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var go = new GameObject("Ring", typeof(RectTransform));
                go.transform.SetParent(canvasGo.transform, false);
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(120f, 120f);
                var ring = go.AddComponent<RingGraphic>();
                var spy = go.AddComponent<MeshSpy>();
                ring.Radius = 50f;
                ring.Thickness = 10f;
                Canvas.ForceUpdateCanvases();
                Debug.Log("PROBE ring: spyVerts=" + spy.VertexCount +
                    " cull=" + ring.canvasRenderer.cull +
                    " materials=" + ring.canvasRenderer.materialCount +
                    " material=" + (ring.canvasRenderer.materialCount > 0
                        ? ring.canvasRenderer.GetMaterial(0).shader.name : "none") +
                    " mainTexture=" + (ring.mainTexture == null ? "null" : ring.mainTexture.name) +
                    " enabled=" + ring.isActiveAndEnabled +
                    " rectSize=" + rect.rect.size);
                Assert.That(spy.VertexCount, Is.EqualTo(122),
                    "framework rebuild did not deliver the ring mesh");
                Assert.That(ring.canvasRenderer.cull, Is.False);
                Assert.That(ring.canvasRenderer.materialCount, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }
    }
}
