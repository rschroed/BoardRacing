using System;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// The deliberately small authoring seam for the first approved car family.
    /// A second real family is the trigger for a generalized catalog.
    /// </summary>
    internal static class DirectionECarVisual
    {
        public const int BodySourceWidth = 432;
        public const int BodySourceHeight = 208;
        public const float BodyPixelsPerUnit = 8f;
        public const int ShadowSourceWidth = 54;
        public const int ShadowSourceHeight = 26;
        public const float ShadowPixelsPerUnit = 1f;

        private const string ResourceRoot = "Cars/";
        private const string ShadowPath = ResourceRoot + "DirectionE_ContactShadow";

        public static string BodyResourcePath(PieceIdentity identity)
        {
            if (identity.ColorName == "Orange" && identity.Symbol == "▲")
                return ResourceRoot + "DirectionE_OrangeTriangle";
            if (identity.ColorName == "Purple" && identity.Symbol == "●")
                return ResourceRoot + "DirectionE_PurpleCircle";
            if (identity.ColorName == "Pink" && identity.Symbol == "◆")
                return ResourceRoot + "DirectionE_PinkDiamond";
            if (identity.ColorName == "Yellow" && identity.Symbol == "■")
                return ResourceRoot + "DirectionE_YellowSquare";
            throw new ArgumentException(
                "Direction E has no approved asset for " + identity.VisualIdentity + ".");
        }

        public static Texture2D LoadBody(PieceIdentity identity) =>
            LoadRequired(BodyResourcePath(identity));

        public static Texture2D LoadContactShadow() => LoadRequired(ShadowPath);

        private static Texture2D LoadRequired(string path)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException(
                    "Missing Direction E car texture at Resources/" + path + ".");
            return texture;
        }
    }
}
