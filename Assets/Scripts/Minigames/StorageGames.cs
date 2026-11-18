using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// At the rear of a disk shelf with one degraded SAS path: unplug only that cable, fit a new one
    /// from the shelf's IOM to the storage head's matching HBA port, and let the link train.
    /// </summary>
    public class SasGame : Minigame
    {
        public override bool InWorld => true;
        class Port { public string name; public Vector3 w; }
        Port[] ioms, hbas;
        LineRenderer[] paths;
        LineRenderer fresh;
        Renderer[] leds;
        int bad, step;
        bool carrying;
        float train;
        Material green, amber, off;
        Plane work;

        const float Back = -RackView.Depth / 2f + 0.05f + 0.9f + 0.018f;
        string Path(int i) => i == 0 ? "A" : "B";
        Rect Tray => new Rect(Ui.W - 320, Ui.H - 290, 300, 150);

        protected override void Begin()
        {
            var d = Dev;
            var def = d.Def;
            bad = Mathf.Clamp(task.slot, 0, 1);
            var rv = RackV;
            var t = rv.transform;
            MakeRig(t);
            HideOnRack(d.id, true);
            float h = def.heightU * RackView.UnitH, bottom = RackView.UY(d.u), top = bottom + h;
            Models.Spawn(Models.For(def), rig, new Vector3(0, bottom, -RackView.Depth / 2f + 0.05f), palette: Models.Unit(def));
            green = Mats.Unlit(new Color(0.3f, 1f, 0.4f));
            amber = Mats.Unlit(new Color(1f, 0.6f, 0.05f));
            off = Mats.Unlit(new Color(0.05f, 0.05f, 0.05f));
            var dark = Mats.Lit(new Color(0.06f, 0.07f, 0.08f), 0.3f, 0.3f);
            var hole = Mats.Unlit(new Color(0.02f, 0.02f, 0.025f));
            // The shelf's two I/O modules, and the storage head's HBA panel above (or below near the top).
            ioms = new Port[2];
            leds = new Renderer[2];
            float iy = bottom + h * 0.62f;
            for (int i = 0; i < 2; i++)
            {
                var lp = new Vector3(i == 0 ? 0.11f : -0.11f, iy, Back);
                Box(rig, lp + new Vector3(0, 0, 0.004f), new Vector3(0.03f, 0.014f, 0.006f), hole, "iom port");
                leds[i] = Box(rig, lp + new Vector3(0.026f, 0.012f, 0.004f), new Vector3(0.006f, 0.006f, 0.003f), green, "link led").GetComponent<Renderer>();
                ioms[i] = new Port { name = $"IOM {Path(i)}", w = t.TransformPoint(lp) };
            }
            bool above = top + 0.32f < RackView.Height - 0.05f;
            float hy = above ? top + 0.24f : bottom - 0.2f;
            Box(rig, new Vector3(0, hy, Back - 0.02f), new Vector3(0.44f, 0.06f, 0.03f), dark, "storage head");
            string[] hn = { "HBA A", "HBA B", "TAPE" };
            float[] hx = { 0.12f, 0f, -0.12f };
            hbas = new Port[3];
            for (int i = 0; i < 3; i++)
            {
                var lp = new Vector3(hx[i], hy, Back + 0.002f);
                Box(rig, lp, new Vector3(0.03f, 0.014f, 0.006f), hole, "hba port");
                hbas[i] = new Port { name = hn[i], w = t.TransformPoint(lp) };
            }
            paths = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                paths[i] = Mats.Line(rig, new Color(0.15f, 0.15f, 0.18f), 0.009f);
                Mats.Sag(paths[i], ioms[i].w, hbas[i].w, 0.06f);
            }
            fresh = Mats.Line(rig, new Color(0.2f, 0.22f, 0.3f), 0.009f);
            fresh.positionCount = 0;
            work = new Plane(t.forward, t.TransformPoint(new Vector3(0, 0, Back)));
            float minY = Mathf.Min(bottom, hy) - 0.08f, maxY = Mathf.Max(top, hy) + 0.1f;
            float cy = (minY + maxY) / 2f;
            float dist = FitDistance(0.62f, maxY - minY, 0.7f);
            Look(new Vector3(0, cy, RackView.Depth / 2f + dist), new Vector3(0, cy, Back));
            title = $"SAS path {Path(bad)}: {def.name} in {task.rack} U{task.u} (rear)";
            hint = $"Click the DEGRADED cable's connector (amber light) to unplug it. Path {Path(1 - bad)} is carrying everything: leave it alone.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            bool blink = Mathf.Repeat(time * 2.5f, 1f) < 0.5f;
            switch (step)
            {
                case 0:
                    if (!Pressed) break;
                    for (int i = 0; i < 2; i++)
                    {
                        if (Vector2.Distance(ToUi(ioms[i].w), m) > 22f) continue;
                        Audio.Play("plug");
                        if (i != bad) { Oops($"You pulled path {Path(i)}, the only healthy one. The shelf vanished from the storage head until you pushed it back in", 4f); break; }
                        paths[i].positionCount = 0;
                        step = 1;
                        hint = "Drag a new SAS cable from your kit into the empty IOM port.";
                        break;
                    }
                    break;
                case 1:
                    if (Pressed && Tray.Contains(m)) carrying = true;
                    if (Released && carrying)
                    {
                        carrying = false;
                        if (Vector2.Distance(ToUi(ioms[bad].w), m) < 26f) { step = 2; Audio.Play("plug"); hint = $"Now take the other end up to the storage head and plug it into HBA {Path(bad)}."; }
                    }
                    break;
                case 2:
                    if (!Pressed) break;
                    for (int i = 0; i < 3; i++)
                    {
                        if (Vector2.Distance(ToUi(hbas[i].w), m) > 22f) continue;
                        Audio.Play("plug");
                        if (i == bad) { step = 3; train = 0; hint = "Link training..."; }
                        else if (i == 2) Oops("That's the tape library's port, not the shelf's controller", 0.5f);
                        else Oops($"HBA {Path(i)} already carries path {Path(i)}. Both paths on one controller isn't redundancy", 0.5f);
                        break;
                    }
                    break;
                default:
                    train += dt;
                    if (train > 2f) Win($"Path {Path(bad)} is up at 12 Gb/s with no errors. The shelf is redundant again.");
                    break;
            }
            for (int i = 0; i < 2; i++)
            {
                bool degraded = i == bad && step < 3;
                bool gone = i == bad && step >= 1 && step < 3;
                leds[i].sharedMaterial = gone ? off : degraded ? (blink ? amber : off) : i == bad && train < 1.5f ? (blink ? green : off) : green;
            }
            if (step == 2) Mats.Sag(fresh, ioms[bad].w, MouseOn(work), 0.04f, 12);
            else if (step == 3) Mats.Sag(fresh, ioms[bad].w, hbas[bad].w, 0.06f);
            else if (step == 1 && carrying) { fresh.positionCount = 0; }
        }

        public override void Draw()
        {
            WorldHud();
            var m = Ui.Mouse;
            for (int i = 0; i < 2; i++)
            {
                var p = ToUi(ioms[i].w);
                if (step == 0 && Vector2.Distance(p, m) < 22f) Ui.Ring(p, 16f, Ui.Accent);
                if (step == 1 && i == bad) Ui.Ring(p, 16f, carrying ? Ui.Good : Ui.Warn);
                Chip(ioms[i].w, ioms[i].name + (i == bad && step == 0 ? "  degraded" : ""), 12, i == bad && step == 0 ? Ui.Warn : Ui.Text, 24);
            }
            foreach (var hb in hbas)
            {
                var p = ToUi(hb.w);
                if (step == 2 && Vector2.Distance(p, m) < 22f) Ui.Ring(p, 16f, Ui.Accent);
                Chip(hb.w, hb.name, 12, Ui.Text, -22);
            }
            if (step == 1)
            {
                Ui.Box(Tray, new Color(0.06f, 0.2f, 0.1f, 0.9f));
                Ui.Border(Tray, Ui.Good, 2f);
                Ui.Label(Tray, "NEW SAS CABLE\n(drag to the IOM)", 19, Ui.Text, TextAnchor.MiddleCenter, true);
                if (carrying)
                {
                    var r = new Rect(m.x - 50, m.y - 22, 100, 44);
                    Ui.Box(r, new Color(0.2f, 0.4f, 0.25f));
                    Ui.Label(r, "SAS", 15, Color.white, TextAnchor.MiddleCenter, true);
                }
            }
            var q = new Rect(20, 20, 380, 90);
            Ui.Box(q, new Color(0.03f, 0.05f, 0.07f, 0.88f));
            Ui.Label(new Rect(q.x + 16, q.y + 10, q.width - 32, 74),
                $"Path A: {(bad == 0 && step < 3 ? (step == 0 ? "DEGRADED (CRC errors)" : "down") : "OK")}\nPath B: {(bad == 1 && step < 3 ? (step == 0 ? "DEGRADED (CRC errors)" : "down") : "OK")}\nI/O: {(step == 3 && train > 1.5f ? "balanced on both paths" : $"all on path {Path(1 - bad)}")}", 16, Ui.Text);
        }
    }

    /// <summary>
    /// At the rear of a messy rack: dress every patch cable into the vertical cable manager on its own
    /// side (crossing the rack blocks the exhaust), then label each with the port it comes from.
    /// </summary>
    public class CableTidyGame : Minigame
    {
        public override bool InWorld => true;
        class Cab { public string label, port; public int u; public Vector3 src, grab, c1, c2, top; public bool left, dressed, labelled; public LineRenderer line; public Color color; }
        readonly List<Cab> cabs = new List<Cab>();
        List<string> sheet;
        Cab dragging, selected;
        Plane work;
        bool labeler;
        int lowU, highU;
        Transform rack;

        const float Back = -RackView.Depth / 2f + 0.05f + 0.9f + 0.016f;
        const float Mgr = 0.236f;

        protected override void Begin()
        {
            var r = shift.Rack(task.rack);
            rack = RackV.transform;
            MakeRig(rack);
            HideOnRack(0, true);
            labeler = career.Has("labeler");
            var rnd = new System.Random(task.slot);
            var picks = new List<(Device d, string port, int idx, int n)>();
            foreach (var d in r.devices.Where(x => x.Def.kind != DevKind.Switch))
            {
                var ports = d.Def.ports.Where(p => !p.StartsWith("PSU")).ToList();
                for (int i = 0; i < ports.Count; i++) picks.Add((d, ports[i], i, ports.Count));
            }
            picks = picks.OrderBy(_ => rnd.Next()).Take(8).OrderBy(p => p.d.u).ThenBy(p => p.idx).ToList();
            var colors = new[] { new Color(0.25f, 0.6f, 1f), new Color(0.95f, 0.8f, 0.2f), new Color(0.3f, 0.85f, 0.45f), new Color(0.85f, 0.4f, 0.9f) };
            foreach (var (d, port, idx, n) in picks)
            {
                float h = d.Def.heightU * RackView.UnitH;
                float x = Mathf.Lerp(-0.17f, 0.17f, n == 1 ? 0.5f : idx / (float)(n - 1));
                var lp = new Vector3(x, RackView.UY(d.u) + h / 2f, Back);
                Box(rig, lp, new Vector3(0.02f, Mathf.Min(0.016f, h * 0.5f), 0.01f), Mats.Lit(new Color(0.2f, 0.2f, 0.22f)), "connector");
                var c = new Cab { label = $"U{d.u} {port}", port = port, u = d.u, src = rack.TransformPoint(lp), left = x > 0, color = colors[rnd.Next(colors.Length)] };
                // A slack loop hanging below the connector, then off across the rack to the tray.
                float loopY = lp.y - 0.12f - (float)rnd.NextDouble() * 0.12f;
                c.c1 = rack.TransformPoint(new Vector3(x + (float)(rnd.NextDouble() - 0.5) * 0.2f, loopY, Back + 0.04f));
                c.c2 = rack.TransformPoint(new Vector3(-x * 1.2f + (float)(rnd.NextDouble() - 0.5) * 0.1f, lp.y + 0.25f, Back + 0.06f));
                c.top = rack.TransformPoint(new Vector3((float)(rnd.NextDouble() - 0.5) * 0.3f, RackView.Height + 0.05f, Back + 0.02f));
                c.grab = Bez(c.src, c.c1, c.c2, c.top, 0.3f);
                c.line = Mats.Line(rig, c.color, 0.005f);
                cabs.Add(c);
            }
            sheet = cabs.Select(c => c.label).OrderBy(_ => rnd.Next()).ToList();
            lowU = cabs.Count > 0 ? cabs.Min(c => c.u) : 1;
            highU = cabs.Count > 0 ? cabs.Max(c => c.u) : 1;
            work = new Plane(rack.forward, rack.TransformPoint(new Vector3(0, 0, Back)));
            float minY = RackView.UY(lowU) - 0.28f, maxY = RackView.Height + 0.1f;
            float cy = (minY + maxY) / 2f;
            float dist = FitDistance(0.8f, maxY - minY, 0.92f);
            Look(new Vector3(-0.1f, cy, RackView.Depth / 2f + dist), new Vector3(-0.1f, cy, Back));
            title = $"Cable audit: {task.rack} (rear)";
            hint = labeler
                ? "Drag each cable's slack loop onto the cable manager on ITS side of the rack. Your label printer labels each one as you dress it."
                : "Drag each cable's slack loop onto the cable manager on ITS side of the rack. Then click a dressed cable's connector and pick its label from the sheet.";
        }

        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        /// <summary>The manager channel (screen-space x and vertical span) on the left (+X, from behind) or right of the rack.</summary>
        Rect Channel(bool left)
        {
            float x = left ? Mgr : -Mgr;
            var a = ToUi(rack.TransformPoint(new Vector3(x, RackView.Height, Back)));
            var b = ToUi(rack.TransformPoint(new Vector3(x, RackView.UY(lowU) - 0.25f, Back)));
            return Rect.MinMaxRect(a.x - 30, Mathf.Min(a.y, b.y), a.x + 30, Mathf.Max(a.y, b.y));
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (Pressed)
            {
                dragging = cabs.Where(c => !c.dressed && Vector2.Distance(ToUi(c.grab), m) < 26f).OrderBy(c => Vector2.Distance(ToUi(c.grab), m)).FirstOrDefault();
                if (dragging == null && !labeler)
                {
                    var c = cabs.Where(x => x.dressed && !x.labelled && Vector2.Distance(ToUi(x.src), m) < 20f).OrderBy(x => Vector2.Distance(ToUi(x.src), m)).FirstOrDefault();
                    if (c != null) { selected = c; Audio.Play("click"); }
                }
            }
            if (Released && dragging != null)
            {
                var c = dragging;
                dragging = null;
                bool onLeft = Channel(true).Contains(m), onRight = Channel(false).Contains(m);
                if (onLeft || onRight)
                {
                    if (onLeft != c.left) Oops($"{c.label} would cross the whole rack to get there, right through the exhaust. Keep it on its own side", 0.5f);
                    else
                    {
                        c.dressed = true;
                        if (labeler) c.labelled = true;
                        Audio.Play("plug");
                    }
                }
            }
            foreach (var c in cabs)
            {
                if (c.dressed)
                {
                    var mx = rack.InverseTransformPoint(c.src);
                    float x = c.left ? Mgr : -Mgr;
                    c.line.positionCount = 4;
                    c.line.SetPosition(0, c.src);
                    c.line.SetPosition(1, rack.TransformPoint(new Vector3(x, mx.y, Back + 0.02f)));
                    c.line.SetPosition(2, rack.TransformPoint(new Vector3(x, RackView.Height, Back + 0.02f)));
                    c.line.SetPosition(3, rack.TransformPoint(new Vector3(x * 0.5f, RackView.Height + 0.05f, Back + 0.02f)));
                }
                else if (c == dragging)
                {
                    var p = MouseOn(work);
                    Mats.Bezier(c.line, c.src, Vector3.Lerp(c.src, p, 0.5f) + Vector3.down * 0.03f, p, c.top, 24);
                }
                else Mats.Bezier(c.line, c.src, c.c1, c.c2, c.top, 30);
            }
            if (cabs.All(c => c.dressed && c.labelled)) Win($"{task.rack} is dressed and labelled. The next tech to work here will thank you.");
        }

        public override void Draw()
        {
            WorldHud();
            var m = Ui.Mouse;
            foreach (bool left in new[] { true, false })
            {
                var ch = Channel(left);
                bool hot = dragging != null && ch.Contains(m);
                Ui.Border(ch, hot ? (left == dragging.left ? Ui.Good : Ui.Bad) : new Color(1, 1, 1, 0.25f), 2f);
            }
            for (int u = lowU; u <= highU; u++)
                if (cabs.Any(c => c.u == u))
                    Tag(rack.TransformPoint(new Vector3(0.29f, RackView.UY(u) + RackView.UnitH / 2f, Back)), $"U{u}", 13, new Color(0.85f, 0.88f, 0.92f), 0, 50, TextAnchor.MiddleRight);
            foreach (var c in cabs)
            {
                if (c.labelled) Chip(c.src, c.label, 11, Ui.Good, -18);
                else
                {
                    Chip(c.src, c.port, 11, c == selected ? Ui.Accent : Ui.Text, -18);
                    if (c == selected) Ui.Ring(ToUi(c.src), 14f, Ui.Accent);
                }
                if (!c.dressed && Vector2.Distance(ToUi(c.grab), m) < 26f && dragging == null) Ui.Ring(ToUi(c.grab), 18f, Ui.Accent);
            }
            if (labeler) return;
            var sh = new Rect(20, 20, 300, 60 + sheet.Count * 46);
            Ui.Box(sh, new Color(0.03f, 0.05f, 0.07f, 0.9f));
            Ui.Label(new Rect(sh.x + 14, sh.y + 10, sh.width - 28, 24), "LABEL SHEET", 16, Ui.Accent, TextAnchor.UpperLeft, true);
            for (int i = 0; i < sheet.Count; i++)
            {
                string l = sheet[i];
                bool used = cabs.Any(c => c.labelled && c.label == l);
                if (Ui.Button(new Rect(sh.x + 14, sh.y + 42 + i * 46, sh.width - 28, 40), l, !used && selected != null, 17))
                {
                    if (selected.label == l) { selected.labelled = true; selected = null; Audio.Play("plug"); }
                    else { Oops($"Wrong label: that cable comes from {selected.label}, not {l}", 0.5f); }
                }
            }
        }
    }
}
