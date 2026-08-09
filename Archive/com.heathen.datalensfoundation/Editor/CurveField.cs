using UnityEditor;
using UnityEngine;

namespace Heathen.DataLens.Editor
{
    /// <summary>
    /// IMGUI response-curve control for a <see cref="Curve"/> (A3.11): a live preview over the normalised
    /// [0,1] domain (drawn from <see cref="Curve.ShapeNormalised"/> — the canonical managed math, never
    /// re-implemented here) plus draggable handles for the parameters that map cleanly to a point
    /// (Linear endpoints → slope/intercept; Threshold edge → P0). Shared across the Heathen authoring tools
    /// (HATE considerations today; Wyrd subjective AI next). House convention: IMGUI, no UI Toolkit.
    /// </summary>
    public static class CurveField
    {
        private const int Samples = 64;
        private const float HandleR = 5f;

        private static readonly Color BackCol   = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color GridCol   = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color CurveCol  = new Color(0.45f, 0.85f, 1f);
        private static readonly Color HandleCol = new Color(1f, 0.8f, 0.25f);

        /// <summary>Reserve a row and draw the preview; returns true if a handle drag mutated
        /// <paramref name="curve"/> this frame (caller should mark its document dirty).</summary>
        public static bool DrawLayout(ref Curve curve, float height = 84f)
        {
            Rect r = GUILayoutUtility.GetRect(10, 10000, height, height, GUILayout.ExpandWidth(true));
            return Draw(r, ref curve);
        }

        /// <summary>Draw the preview into <paramref name="area"/>; returns true if a handle drag mutated
        /// <paramref name="curve"/> this frame.</summary>
        public static bool Draw(Rect area, ref Curve curve)
        {
            Rect plot = new Rect(area.x + 4, area.y + 4, area.width - 8, area.height - 8);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, BackCol);
                DrawGrid(plot);
                DrawCurve(plot, curve);
            }
            return DrawHandles(plot, ref curve);
        }

        private static void DrawGrid(Rect plot)
        {
            for (int i = 1; i < 4; i++)
            {
                float fx = plot.x + plot.width * i / 4f;
                float fy = plot.y + plot.height * i / 4f;
                EditorGUI.DrawRect(new Rect(fx, plot.y, 1f, plot.height), GridCol);
                EditorGUI.DrawRect(new Rect(plot.x, fy, plot.width, 1f), GridCol);
            }
        }

        private static void DrawCurve(Rect plot, Curve curve)
        {
            var pts = new Vector3[Samples];
            for (int i = 0; i < Samples; i++)
            {
                float x = i / (float)(Samples - 1);
                float y = curve.ShapeNormalised(x);
                pts[i] = new Vector3(plot.x + x * plot.width, plot.yMax - y * plot.height, 0f);
            }
            Handles.color = CurveCol;
            Handles.DrawAAPolyLine(2f, pts);
        }

        // Draggable handles per curve type; each is a point in normalised [0,1]^2 plot space.
        private static bool DrawHandles(Rect plot, ref Curve curve)
        {
            switch (curve.Type)
            {
                case CurveType.Threshold: // drag the vertical edge → P0 (x only)
                {
                    var p = new Vector2(curve.P0, 0.5f);
                    if (DragPoint(plot, ref p, GetId(plot, 0)))
                    {
                        curve.P0 = Clamp01(p.x);
                        return true;
                    }
                    return false;
                }
                case CurveType.Linear: // left endpoint (0,P1) sets intercept; right endpoint sets slope
                {
                    bool changed = false;
                    var left = new Vector2(0f, curve.P1);
                    if (DragPoint(plot, ref left, GetId(plot, 0)))
                    {
                        curve.P1 = Clamp01(left.y);
                        changed = true;
                    }
                    float yRight = curve.P0 * 1f + curve.P1; // y at x=1
                    var right = new Vector2(1f, yRight);
                    if (DragPoint(plot, ref right, GetId(plot, 1)))
                    {
                        curve.P0 = Clamp01(right.y) - curve.P1; // slope = y(1) - intercept
                        changed = true;
                    }
                    return changed;
                }
                default: // Power / Smoothstep: preview only (exponent edited via a field)
                    return false;
            }
        }

        private static bool DragPoint(Rect plot, ref Vector2 norm, int id)
        {
            Vector2 px = ToPixel(plot, norm);
            var e = Event.current;
            bool changed = false;
            switch (e.GetTypeForControl(id))
            {
                case EventType.Repaint:
                    EditorGUI.DrawRect(new Rect(px.x - HandleR, px.y - HandleR, HandleR * 2, HandleR * 2), HandleCol);
                    break;
                case EventType.MouseDown:
                    if (e.button == 0 && Vector2.Distance(e.mousePosition, px) <= HandleR * 2f)
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        norm = FromPixel(plot, e.mousePosition);
                        changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
            return changed;
        }

        private static Vector2 ToPixel(Rect plot, Vector2 norm) =>
            new Vector2(plot.x + Clamp01(norm.x) * plot.width, plot.yMax - Clamp01(norm.y) * plot.height);

        private static Vector2 FromPixel(Rect plot, Vector2 px) => new Vector2(
            Clamp01((px.x - plot.x) / plot.width),
            Clamp01((plot.yMax - px.y) / plot.height));

        // A stable per-control id seeded by the plot position so multiple curves in one window don't collide.
        private static int GetId(Rect plot, int slot) =>
            GUIUtility.GetControlID(unchecked((int)plot.x * 73856093 ^ (int)plot.y * 19349663 ^ (slot + 1)), FocusType.Passive);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
