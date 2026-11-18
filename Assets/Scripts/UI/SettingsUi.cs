using UnityEngine;

namespace RH
{
    public partial class Game
    {
        /// <summary>The control being rebound (waiting for a key press), or -1.</summary>
        int rebinding = -1;

        void DrawSettings()
        {
            var r = Ui.Center(1100, 820);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 40, r.y + 30, 600, 50), "Settings", 40, Ui.Text, TextAnchor.UpperLeft, true);

            // Left column: feel and look.
            float x = r.x + 40, y = r.y + 110, w = 460;
            Ui.Label(new Rect(x, y, w, 30), $"Mouse sensitivity: {Player.Sensitivity:0.0}", 19, Ui.Text);
            Player.Sensitivity = GUI.HorizontalSlider(new Rect(x, y + 34, w, 20), Player.Sensitivity, 0.5f, 6f);
            y += 80;
            Ui.Label(new Rect(x, y, w, 30), $"Volume: {Audio.Volume * 100:0}%", 19, Ui.Text);
            Audio.Volume = GUI.HorizontalSlider(new Rect(x, y + 34, w, 20), Audio.Volume, 0f, 1f);
            y += 80;
            Ui.Label(new Rect(x, y, w, 30), $"Field of view: {Player.Fov:0}", 19, Ui.Text);
            Player.Fov = Mathf.Round(GUI.HorizontalSlider(new Rect(x, y + 34, w, 20), Player.Fov, 60f, 100f));
            y += 80;
            var names = QualitySettings.names;
            int q = QualitySettings.GetQualityLevel();
            Ui.Label(new Rect(x, y, w, 30), "Graphics quality", 19, Ui.Text);
            if (Ui.Button(new Rect(x, y + 34, 60, 44), "<", q > 0, 22)) SetQuality(q - 1);
            Ui.Label(new Rect(x + 70, y + 34, w - 140, 44), names.Length > 0 ? names[q] : "Default", 21, Ui.Accent, TextAnchor.MiddleCenter, true);
            if (Ui.Button(new Rect(x + w - 60, y + 34, 60, 44), ">", q < names.Length - 1, 22)) SetQuality(q + 1);
            y += 90;
            Ui.Label(new Rect(x, y, w, 30), "Display mode", 19, Ui.Text);
            int m = System.Array.IndexOf(DisplayModes, Screen.fullScreenMode);
            if (m < 0) m = 0;
            if (Ui.Button(new Rect(x, y + 34, 60, 44), "<", m > 0, 22)) SetDisplay(DisplayModes[m - 1], ResolutionIndex());
            Ui.Label(new Rect(x + 70, y + 34, w - 140, 44), DisplayModeNames[m], 21, Ui.Accent, TextAnchor.MiddleCenter, true);
            if (Ui.Button(new Rect(x + w - 60, y + 34, 60, 44), ">", m < DisplayModes.Length - 1, 22)) SetDisplay(DisplayModes[m + 1], ResolutionIndex());
            y += 90;
            var res = Resolutions();
            int ri = ResolutionIndex();
            Ui.Label(new Rect(x, y, w, 30), "Resolution", 19, Ui.Text);
            if (Ui.Button(new Rect(x, y + 34, 60, 44), "<", ri > 0, 22)) SetDisplay(Screen.fullScreenMode, ri - 1);
            Ui.Label(new Rect(x + 70, y + 34, w - 140, 44), $"{res[ri].x} x {res[ri].y}", 21, Ui.Accent, TextAnchor.MiddleCenter, true);
            if (Ui.Button(new Rect(x + w - 60, y + 34, 60, 44), ">", ri < res.Length - 1, 22)) SetDisplay(Screen.fullScreenMode, ri + 1);
            y += 100;
            Ui.Label(new Rect(x, y, w, 80), "Lower quality turns off shadows and anti-aliasing for older machines. Alt+Enter toggles full screen.", 16, Ui.Dim);

            // Right column: controls.
            x = r.x + 560;
            y = r.y + 110;
            w = 500;
            Ui.Label(new Rect(x, y, w, 30), "CONTROLS (click to rebind)", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            y += 36;
            for (int i = 0; i < Keys.Names.Length; i++)
            {
                var a = (Keys.Action)i;
                Ui.Label(new Rect(x, y, 260, 44), Keys.Names[i], 19, Ui.Text, TextAnchor.MiddleLeft);
                string label = rebinding == i ? "press a key..." : Keys.Label(a);
                if (Ui.Button(new Rect(x + 280, y + 2, 220, 40), label, true, 18, rebinding == i ? Ui.Warn : (Color?)null)) rebinding = i;
                y += 48;
            }
            if (Ui.Button(new Rect(x, y + 10, 220, 44), "Reset controls", true, 18)) { Keys.Reset(); rebinding = -1; }
            Ui.Label(new Rect(x, y + 60, w, 30), "Esc always pauses and steps back.", 15, Ui.Dim);

            if (rebinding >= 0)
            {
                var e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
                {
                    if (e.keyCode != KeyCode.Escape) Keys.Set((Keys.Action)rebinding, e.keyCode);
                    rebinding = -1;
                    e.Use();
                }
                else if (e.type == EventType.MouseDown && e.button > 0)
                {
                    Keys.Set((Keys.Action)rebinding, e.button == 1 ? KeyCode.Mouse1 : KeyCode.Mouse2);
                    rebinding = -1;
                    e.Use();
                }
            }
            if (Ui.Button(new Rect(r.x + 40, r.yMax - 90, 300, 60), "Done", rebinding < 0, 24, Ui.Good)) CloseSettings();
        }

        static readonly FullScreenMode[] DisplayModes = { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };
        static readonly string[] DisplayModeNames = { "Windowed", "Borderless full screen", "Exclusive full screen" };

        /// <summary>Resolution picked in this session; Screen.width lags a frame behind SetResolution.</summary>
        Vector2Int? pickedResolution;
        Vector2Int[] resolutionCache;

        /// <summary>The monitor's supported resolutions, one entry per size (refresh rates folded), smallest first.</summary>
        Vector2Int[] Resolutions()
        {
            if (resolutionCache != null) return resolutionCache;
            var list = new System.Collections.Generic.List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 800 && v.y >= 600 && !list.Contains(v)) list.Add(v);
            }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return resolutionCache = list.ToArray();
        }

        /// <summary>Index of the current (or just picked) resolution, or the closest match by pixel count.</summary>
        int ResolutionIndex()
        {
            var res = Resolutions();
            var actual = new Vector2Int(Screen.width, Screen.height);
            if (pickedResolution == actual) pickedResolution = null; // applied; follow the real window again
            var cur = pickedResolution ?? actual;
            int best = 0;
            long bestDiff = long.MaxValue;
            for (int i = 0; i < res.Length; i++)
            {
                long diff = System.Math.Abs((long)res[i].x * res[i].y - (long)cur.x * cur.y) + System.Math.Abs(res[i].x - cur.x);
                if (diff < bestDiff) { bestDiff = diff; best = i; }
            }
            return best;
        }

        /// <summary>Apply a display mode and resolution. Unity remembers both between launches.</summary>
        void SetDisplay(FullScreenMode mode, int resIndex)
        {
            var res = Resolutions();
            resIndex = Mathf.Clamp(resIndex, 0, res.Length - 1);
            pickedResolution = res[resIndex];
            Screen.SetResolution(res[resIndex].x, res[resIndex].y, mode);
        }

        void SetQuality(int q)
        {
            QualitySettings.SetQualityLevel(q, true);
            PlayerPrefs.SetInt("quality", q);
        }
    }
}
