using System.Collections.Generic;
using UnityEngine;

namespace RH
{
    /// <summary>Cached materials and primitive builders for the code-generated world.</summary>
    public static class Mats
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Font font;
        static Material textMat;

        static Material Get(string shader, Color c, float smooth, float metal)
        {
            string key = $"{shader}|{ColorUtility.ToHtmlStringRGBA(c)}|{smooth:0.00}|{metal:0.00}";
            if (cache.TryGetValue(key, out var m)) return m;
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("Missing shader " + shader); sh = Shader.Find("Hidden/InternalErrorShader"); }
            m = new Material(sh) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            cache[key] = m;
            return m;
        }

        public static Material Lit(Color c, float smooth = 0.25f, float metal = 0f) => Get("RH/Lit", c, smooth, metal);
        public static Material Unlit(Color c) => Get("RH/Unlit", c, 0, 0);
        public static Material Glass(Color c) => Get("RH/UnlitTransparent", c, 0, 0);

        public static Material Glow(Color c, float strength = 1f)
        {
            var m = Get("RH/Lit", c * 0.2f, 0.4f, 0f);
            string key = "glow|" + ColorUtility.ToHtmlStringRGBA(c) + strength;
            if (cache.TryGetValue(key, out var g)) return g;
            g = new Material(m);
            if (g.HasProperty("_Emission")) g.SetColor("_Emission", c * strength);
            cache[key] = g;
            return g;
        }

        /// <summary>A box with no collider unless asked for.</summary>
        public static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider = false, bool shadows = true, string name = "box")
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            if (!collider) Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        /// <summary>A world-space line (cables, cords, jumpers). Colour comes from the vertex colour.</summary>
        public static LineRenderer Line(Transform parent, Color c, float width)
        {
            var g = new GameObject("line");
            g.transform.SetParent(parent, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = Unlit(Color.white);
            lr.startColor = lr.endColor = c;
            lr.startWidth = lr.endWidth = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>A cable hanging between two points.</summary>
        public static void Sag(LineRenderer lr, Vector3 a, Vector3 b, float sag, int n = 18)
        {
            lr.positionCount = n + 1;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                lr.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * sag);
            }
        }

        /// <summary>A cubic Bezier curve through four control points.</summary>
        public static void Bezier(LineRenderer lr, Vector3 a, Vector3 c1, Vector3 c2, Vector3 b, int n = 40)
        {
            lr.positionCount = n + 1;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, u = 1 - t;
                lr.SetPosition(i, u * u * u * a + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * b);
            }
        }

        /// <summary>World-space text, readable when looking along the parent's +Z.</summary>
        public static TextMesh Text(Transform parent, Vector3 pos, string text, float height, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, float yaw = 0f)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var g = new GameObject("text");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var tm = g.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = height / 6.4f * 1.0f;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = c;
            tm.text = text;
            if (textMat == null)
            {
                textMat = new Material(Shader.Find("RH/Text"));
                Font.textureRebuilt += f => { if (f == font && textMat != null) textMat.mainTexture = f.material.mainTexture; };
            }
            textMat.mainTexture = font.material.mainTexture;
            var mr = g.GetComponent<MeshRenderer>();
            mr.sharedMaterial = textMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tm;
        }
    }
}
