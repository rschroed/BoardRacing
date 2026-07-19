using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    [CreateAssetMenu(menuName = "Board Racing/Tranche One Settings")]
    public sealed class TrancheOneSettings : ScriptableObject
    {
        [Header("Input")]
        [Range(0f, 30f)] public float throttleHysteresisDegrees = 8f;
        public bool preferBoardInputOnDevice = true;
        public float playerRegionBoundaryY = 540f;
        [Header("Pit action")]
        // Call Pit circle centers against each seat's short board edge, measured from
        // frame 40:23 component 44:124 (wireframe-ui.md, issue #77 Round 2).
        public Vector2 playerOneServiceCenter = new Vector2(1832f, 398f);
        public Vector2 playerTwoServiceCenter = new Vector2(88f, 682f);
        public Vector2 serviceHalfSize = new Vector2(180f, 150f);
        [Range(0f, 360f)] public float targetAngleDegrees = 0f;
        [Range(1f, 90f)] public float alignmentToleranceDegrees = 15f;
        [Min(0.1f)] public float holdDurationSeconds = 1.5f;

        public static TrancheOneSettings Defaults()
        {
            var result = CreateInstance<TrancheOneSettings>();
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }
    }
}
