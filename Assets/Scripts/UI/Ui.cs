using System.Collections.Generic;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// Immediate-mode drawing kit. Everything is laid out on a virtual canvas 1080 units tall
    /// (width follows the aspect ratio) and scaled to the real screen.
    /// </summary>
    public static class Ui
    {
        public const float H = 1080f;
        public static float Scale => Mathf.Max(0.01f, Screen.height / H);
        public static float W => Screen.width / Scale;
        static Matrix4x4? mouseToCanvas;

        /// <summary>The mouse in canvas units (or in an in-world screen's canvas while one is active).</summary>
        public static Vector2 Mouse
        {
            get
            {
                var px = new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y, 0f);
                return mouseToCanvas.HasValue ? (Vector2)mouseToCanvas.Value.MultiplyPoint3x4(px) : (Vector2)px / Scale;
            }
        }

        /// <summary>Route Ui.Mouse through a canvas-to-pixels matrix (an in-world monitor), or back to normal with null.</summary>
        public static void SetMouseCanvas(Matrix4x4? canvasToPixels) => mouseToCanvas = canvasToPixels?.inverse;

        /// <summary>Draw subsequent GUI through a canvas-to-pixels matrix, e.g. onto an in-world monitor.</summary>
        public static void PushCanvas(Matrix4x4 canvasToPixels)
        {
            baseMatrix = canvasToPixels;
            GUI.matrix = baseMatrix;
        }

        public static void PopCanvas()
        {
            baseMatrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            GUI.matrix = baseMatrix;
        }

        /// <summary>A world point in canvas units, and whether it's in front of the camera.</summary>
        public static Vector2 FromWorld(Camera cam, Vector3 world, out bool inFront)
        {
            var s = cam.WorldToScreenPoint(world);
            inFront = s.z > 0;
            return new Vector2(s.x, Screen.height - s.y) / Scale;
        }

        public static readonly Color Bg = new Color(0.05f, 0.07f, 0.09f, 0.97f);
        public static readonly Color Bg2 = new Color(0.1f, 0.13f, 0.16f, 0.95f);
        public static readonly Color Line = new Color(0.25f, 0.32f, 0.38f, 1f);
        public static readonly Color Accent = new Color(0.25f, 0.78f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.9f, 0.45f);
        public static readonly Color Warn = new Color(1f, 0.72f, 0.2f);
        public static readonly Color Bad = new Color(1f, 0.32f, 0.28f);
        public static readonly Color Dim = new Color(0.6f, 0.67f, 0.72f);
        public static readonly Color Text = new Color(0.9f, 0.94f, 0.96f);

        static Texture2D white, circle, ring;
        static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        static Matrix4x4 baseMatrix;

        public static Texture2D White { get { Init(); return white; } }

        static void Init()
        {
            if (white != null) return;
            white = Tex(4, (x, y, n) => Color.white);
            circle = Tex(128, (x, y, n) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                return new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d));
            });
            ring = Tex(128, (x, y, n) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                float r = n / 2f - 1f;
                return new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Abs(d - (r - 4f)) / 3.5f));
            });
        }

        static Texture2D Tex(int n, System.Func<int, int, int, Color> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f(x, y, n);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Call at the start of OnGUI.</summary>
        public static void Begin()
        {
            Init();
            baseMatrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            GUI.matrix = baseMatrix;
        }

        /// <summary>Darken the whole screen behind a modal panel.</summary>
        public static void Shade(float a = 0.65f) => Box(new Rect(0, 0, W, H), new Color(0.01f, 0.015f, 0.02f, a));

        public static Rect Center(float w, float h) => new Rect((W - w) * 0.5f, (H - h) * 0.5f, w, h);

        public static void Box(Rect r, Color c)
        {
            var o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = o;
        }

        public static void Border(Rect r, Color c, float t = 2f)
        {
            Box(new Rect(r.x, r.y, r.width, t), c);
            Box(new Rect(r.x, r.yMax - t, r.width, t), c);
            Box(new Rect(r.x, r.y, t, r.height), c);
            Box(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        public static void Panel(Rect r, Color? bg = null)
        {
            Box(r, bg ?? Bg);
            Border(r, Line, 1.5f);
        }

        static GUIStyle Style(int size, bool bold, TextAnchor anchor, bool wrap)
        {
            int key = size * 1000 + (bold ? 500 : 0) + (int)anchor * 10 + (wrap ? 1 : 0);
            if (!styles.TryGetValue(key, out var s))
            {
                s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = anchor, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
                s.padding = new RectOffset(0, 0, 0, 0);
                s.margin = new RectOffset(0, 0, 0, 0);
                styles[key] = s;
            }
            return s;
        }

        public static void Label(Rect r, string text, int size = 20, Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft, bool bold = false, bool wrap = true)
        {
            var s = Style(size, bold, anchor, wrap);
            s.normal.textColor = color ?? Text;
            GUI.Label(r, text, s);
        }

        public static float TextHeight(string text, float width, int size = 20, bool bold = false) =>
            Style(size, bold, TextAnchor.UpperLeft, true).CalcHeight(new GUIContent(text), width);

        public static bool Hover(Rect r) => r.Contains(Event.current.mousePosition);

        /// <summary>A clickable button; returns true on the click.</summary>
        public static bool Button(Rect r, string text, bool enabled = true, int size = 20, Color? tint = null)
        {
            bool hover = enabled && Hover(r);
            var c = tint ?? Accent;
            Box(r, enabled ? (hover ? Color.Lerp(Bg2, c, 0.35f) : Bg2) : new Color(0.08f, 0.1f, 0.12f, 0.9f));
            Border(r, enabled ? (hover ? c : Color.Lerp(Line, c, 0.35f)) : Line, 1.5f);
            Label(r, text, size, enabled ? Text : Dim, TextAnchor.MiddleCenter, true);
            var e = Event.current;
            if (enabled && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                Audio.Play("click");
                return true;
            }
            return false;
        }

        public static void Bar(Rect r, float t, Color c, Color? bg = null)
        {
            Box(r, bg ?? new Color(0, 0, 0, 0.5f));
            Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), c);
        }

        public static void Circle(Vector2 center, float radius, Color c)
        {
            var o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), circle);
            GUI.color = o;
        }

        public static void Ring(Vector2 center, float radius, Color c)
        {
            var o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), ring);
            GUI.color = o;
        }

        public static void Segment(Vector2 a, Vector2 b, Color c, float width = 3f)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUI.matrix = baseMatrix * Matrix4x4.TRS(new Vector3(a.x, a.y, 0), Quaternion.Euler(0, 0, ang), Vector3.one);
            var o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, -width * 0.5f, len, width), white);
            GUI.color = o;
            GUI.matrix = m;
        }

        /// <summary>A sagging cable between two points.</summary>
        public static void Cable(Vector2 a, Vector2 b, Color c, float width = 5f)
        {
            float sag = Mathf.Min(120f, Vector2.Distance(a, b) * 0.25f);
            Vector2 prev = a;
            const int n = 18;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector2 p = Vector2.Lerp(a, b, t) + new Vector2(0, Mathf.Sin(t * Mathf.PI) * sag);
                Segment(prev, p, c, width);
                prev = p;
            }
        }

        public static string Money(int v) => "$" + v.ToString("N0");
    }
}
