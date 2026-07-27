using UnityEngine;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// The committed course-surface treatment (issue #161): the serialized half
    /// of the <see cref="RaceSurfaceStyle"/> seam, holding semantic colour and
    /// texture slots rather than references to a particular generator, vendor
    /// pack, or filename. Swapping a texture reference here changes the course
    /// without touching mesh generation, the shader, or gameplay code.
    ///
    /// Deliberately only the course child of a future composition — player
    /// accents, HUD colours, and condition-state semantics are not course-owned
    /// and are not defined here. See Presentation/PROVENANCE.md.
    ///
    /// The Visual Lab tunes a runtime copy of <see cref="ToStyle"/>; it never
    /// writes back into this asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Board Racing/Course Surface Theme",
        fileName = "CourseSurfaceTheme")]
    internal sealed class CourseSurfaceTheme : ScriptableObject
    {
        public const string ResourcePath = "CourseSurfaceTheme";

        // The committed material instance source. Referencing it here is
        // what includes the shader in a player build.
        [Header("Material")]
        public Material surfaceMaterial;

        [Header("Ground")]
        public Color groundColor = RaceSurfaceGeometry.BackgroundColor;
        public Texture2D groundDetail;
        [Min(1f)] public float groundDetailTile = 128f;

        [Header("Road")]
        public Color straightRoadColor = RaceSurfaceGeometry.StraightColor;
        public Color cornerRoadColor = RaceSurfaceGeometry.CornerColor;
        public Texture2D roadDetail;
        [Min(1f)] public float roadDetailTile = 128f;

        [Header("Shoulder")]
        public Color shoulderColor = new Color(.28f, .24f, .18f, 1f);
        [Range(0f, 1f)] public float shoulderOpacity;
        [Min(0f)] public float shoulderSolidWidth = 12f;
        [Min(0f)] public float shoulderFeatherWidth = 24f;
        public Texture2D shoulderDetail;
        [Min(1f)] public float shoulderDetailTile = 128f;

        // The pit surface samples the road detail texture and is separated by
        // tint alone — a recorded mapping compromise (#161) rather than a
        // fourth sampler for a surface that is asphalt either way.
        [Header("Pit surface")]
        public Color pitSurfaceColor = RaceSurfaceGeometry.PitLaneColor;
        public Color pitStripeColor = RaceSurfaceGeometry.PitStripeColor;
        public Color inactivePitBoxAccent = RaceSurfaceGeometry.InactivePitBoxAccent;

        [Header("Markings and dressing")]
        public Color stripeColor = RaceSurfaceGeometry.StripeColor;
        public Color crossingShadowColor = RaceSurfaceGeometry.CrossingShadowColor;

        // Grades on top of the authored tile colors. White is the baseline —
        // pit lane and corners share the road tile ungraded for now, so the raw
        // assets can be judged before any grading is layered on.
        [Header("Detail")]
        public Color groundDetailTint = Color.white;
        public Color roadDetailTint = Color.white;
        public Color shoulderDetailTint = Color.white;
        [Range(0f, 1f)] public float detailStrength = 1f;

        public static CourseSurfaceTheme Load() =>
            Resources.Load<CourseSurfaceTheme>(ResourcePath);

        /// <summary>
        /// A fresh runtime value. Callers own the result: the Visual Lab tunes
        /// its copy freely without mutating the committed asset.
        /// </summary>
        public RaceSurfaceStyle ToStyle()
        {
            RaceSurfaceStyle style = RaceSurfaceStyle.Default;
            style.GroundColor = groundColor;
            style.StraightRoadColor = straightRoadColor;
            style.CornerRoadColor = cornerRoadColor;
            style.StripeColor = stripeColor;
            style.PitSurfaceColor = pitSurfaceColor;
            style.PitStripeColor = pitStripeColor;
            style.InactivePitBoxAccent = inactivePitBoxAccent;
            style.CrossingShadowColor = crossingShadowColor;
            style.ShoulderColor = shoulderColor;
            style.ShoulderOpacity = shoulderOpacity;
            style.ShoulderSolidWidth = shoulderSolidWidth;
            style.ShoulderFeatherWidth = shoulderFeatherWidth;
            style.SurfaceMaterial = surfaceMaterial;
            style.GroundDetail = groundDetail;
            style.RoadDetail = roadDetail;
            style.ShoulderDetail = shoulderDetail;
            style.GroundDetailTile = groundDetailTile;
            style.RoadDetailTile = roadDetailTile;
            style.ShoulderDetailTile = shoulderDetailTile;
            style.GroundDetailTint = groundDetailTint;
            style.RoadDetailTint = roadDetailTint;
            style.ShoulderDetailTint = shoulderDetailTint;
            style.DetailStrength = detailStrength;
            return style;
        }

        /// <summary>
        /// The committed theme if one is present, otherwise the flat default —
        /// so a project with no theme asset still renders the pre-#161 look
        /// rather than failing.
        /// </summary>
        public static RaceSurfaceStyle LoadStyleOrDefault()
        {
            CourseSurfaceTheme theme = Load();
            return theme != null ? theme.ToStyle() : RaceSurfaceStyle.Default;
        }
    }
}
