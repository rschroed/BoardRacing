using System;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// Resource seam for the approved issue #183 modular pit kit. The kit stays
    /// deliberately concrete until a second pit-art family creates a real need
    /// for a generalized catalog.
    /// </summary>
    internal static class PitKitVisual
    {
        public const int RetainedRenderersPerStall = 18;

        private const string Root = "Pits/";

        public static Texture2D LoadWheelStop() => LoadRequired(Root + "PitKit_WheelStop");
        public static Texture2D LoadRail() => LoadRequired(Root + "PitKit_Rail");
        public static Texture2D LoadArmPivot() => LoadRequired(Root + "PitKit_ArmPivot");
        public static Texture2D LoadArmUpper() => LoadRequired(Root + "PitKit_ArmUpper");
        public static Texture2D LoadArmForearm() => LoadRequired(Root + "PitKit_ArmForearm");
        public static Texture2D LoadToolHead() => LoadRequired(Root + "PitKit_ToolHead");
        public static Texture2D LoadToolArc() => LoadRequired(Root + "PitKit_FxToolArc");
        public static Texture2D LoadSparks() => LoadRequired(Root + "PitKit_FxSparks");
        public static Texture2D LoadLampHalo() => LoadRequired(Root + "PitKit_FxLampHalo");
        public static Texture2D LoadReadyRings() => LoadRequired(Root + "PitKit_FxReadyRings");
        public static Texture2D LoadReleaseStreak() =>
            LoadRequired(Root + "PitKit_FxReleaseStreak");

        public static string MarkerResourcePath(PieceIdentity identity)
        {
            if (identity.ColorName == "Orange" && identity.Symbol == "▲")
                return Root + "PitKit_MarkerOrangeTriangle";
            if (identity.ColorName == "Purple" && identity.Symbol == "●")
                return Root + "PitKit_MarkerPurpleCircle";
            if (identity.ColorName == "Pink" && identity.Symbol == "◆")
                return Root + "PitKit_MarkerPinkDiamond";
            if (identity.ColorName == "Yellow" && identity.Symbol == "■")
                return Root + "PitKit_MarkerYellowSquare";
            throw new ArgumentException(
                "The approved pit kit has no marker for " + identity.VisualIdentity + ".");
        }

        public static Texture2D LoadMarker(PieceIdentity identity) =>
            LoadRequired(MarkerResourcePath(identity));

        public static Texture2D LoadTirePile() =>
            LoadRequired(Root + "PitKit_PropTirePile");

        public static Texture2D LoadToolCart() =>
            LoadRequired(Root + "PitKit_PropToolCart");

        public static Texture2D LoadJackAndTire() =>
            LoadRequired(Root + "PitKit_PropJackTire");

        private static Texture2D LoadRequired(string path)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException(
                    "Missing approved pit-kit texture at Resources/" + path + ".");
            return texture;
        }
    }
}
