using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>Follow one fiber jumper through the tangle at the back of the rack to its patch panel port.</summary>
    public class TraceGame : Minigame
    {
        class Cab { public string label; public int port; public Vector2 a, c1, c2, b; public Color color; public bool target; }
        readonly List<Cab> cabs = new List<Cab>();
        readonly Vector2[] ports = new Vector2[24];

        protected override void Begin()
        {
            var p = P;
            var rnd = new System.Random(task.deviceId * 7 + task.slot);
            for (int i = 0; i < 24; i++)
                ports[i] = new Vector2(p.x + 1100 + (i / 12) * 90, p.y + 130 + (i % 12) * 42);
            // The customer's cable plus seven others from the same rack.
            var rack = shift.Rack(task.rack);
            var labels = new List<string> { $"U{task.u} {task.component}" };
            foreach (var d in rack.devices.OrderBy(_ => rnd.Next()))
                foreach (var port in d.Def.ports.Where(x => !x.StartsWith("PSU")))
                {
                    string l = $"U{d.u} {port}";
                    if (!labels.Contains(l) && labels.Count < 8) labels.Add(l);
                }
            var free = Enumerable.Range(1, 24).Where(x => x != task.slot).OrderBy(_ => rnd.Next()).ToList();
            var order = labels.OrderBy(_ => rnd.Next()).ToList();
            for (int i = 0; i < order.Count; i++)
            {
                bool target = order[i] == labels[0];
                int port = target ? task.slot : free[i];
                var a = new Vector2(p.x + 240, p.y + 130 + i * (500f / Mathf.Max(1, order.Count - 1)));
                var b = ports[port - 1];
                float shade = 0.75f + (float)rnd.NextDouble() * 0.25f;
                cabs.Add(new Cab
                {
                    label = order[i], port = port, target = target, a = a, b = b,
                    c1 = new Vector2(a.x + 220 + (float)rnd.NextDouble() * 320, p.y + 110 + (float)rnd.NextDouble() * 520),
                    c2 = new Vector2(b.x - 220 - (float)rnd.NextDouble() * 320, p.y + 110 + (float)rnd.NextDouble() * 520),
                    color = new Color(0.95f * shade, 0.78f * shade, 0.15f, 1f),
                });
            }
            title = $"Trace cable: U{task.u} {task.component} in {task.rack} (rear)";
            hint = $"Follow the jumper labelled U{task.u} {task.component} through the tangle and click the patch panel port it lands on.";
        }

        static Vector2 Bez(Cab c, float t)
        {
            float u = 1 - t;
            return u * u * u * c.a + 3 * u * u * t * c.c1 + 3 * u * t * t * c.c2 + t * t * t * c.b;
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (!Pressed) return;
            for (int i = 0; i < ports.Length; i++)
            {
                if (Vector2.Distance(m, ports[i]) > 16f) continue;
                if (i + 1 == task.slot) Win($"Found it: U{task.u} {task.component} lands on patch panel port {task.slot}.");
                else if (cabs.Any(c => c.port == i + 1)) Oops($"Port {i + 1} is a different cable. You'd have patched the wrong circuit", 1f);
                else Oops($"Nothing is plugged into port {i + 1}", 0.5f);
                return;
            }
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            bool toner = career.Has("toner");
            Ui.Label(new Rect(p.x + 30, p.y + 84, 200, 24), "DEVICE PORTS", 15, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(p.x + 1060, p.y + 84, 200, 24), "PATCH PANEL", 15, Ui.Dim, TextAnchor.UpperLeft, true);
            foreach (var c in cabs)
            {
                var col = c.color;
                if (toner && c.target) col = Color.Lerp(c.color, Color.white, 0.5f + 0.5f * Mathf.Sin(time * 8f));
                var prev = c.a;
                for (int k = 1; k <= 40; k++)
                {
                    var pt = Bez(c, k / 40f);
                    Ui.Segment(prev, pt, col, 4f);
                    prev = pt;
                }
            }
            foreach (var c in cabs)
            {
                Ui.Circle(c.a, 9f, c.color);
                Ui.Label(new Rect(c.a.x - 210, c.a.y - 12, 195, 24), c.label, 16, c.target ? Ui.Accent : Ui.Text, TextAnchor.MiddleRight, c.target);
            }
            for (int i = 0; i < ports.Length; i++)
            {
                var r = new Rect(ports[i].x - 12, ports[i].y - 10, 24, 20);
                bool hover = Vector2.Distance(Ui.Mouse, ports[i]) < 16f;
                Ui.Box(r, new Color(0.03f, 0.03f, 0.04f));
                Ui.Border(r, hover ? Ui.Accent : new Color(0.35f, 0.38f, 0.42f), hover ? 3f : 1.5f);
                Ui.Label(new Rect(r.xMax + 4, r.y, 40, 20), (i + 1).ToString(), 13, Ui.Dim, TextAnchor.MiddleLeft);
            }
        }
    }

    /// <summary>In the meet-me room: patch the customer's circuit through to the carrier's port. Only that circuit.</summary>
    public class PatchGame : Minigame
    {
        class Jumper { public string label; public bool right; public Vector2 p; public bool plugged; }
        class Port { public string carrier; public int n; public Vector2 p; }
        readonly List<Jumper> jumpers = new List<Jumper>();
        readonly List<Port> ports = new List<Port>();
        Jumper dragging;

        protected override void Begin()
        {
            var p = P;
            var rnd = new System.Random(task.u * 17 + task.slot);
            var racks = shift.racks.Where(r => r.label != task.rack).OrderBy(_ => rnd.Next()).Take(3).Select(r => r.label).ToList();
            var labels = new List<(string, bool)> { ($"{task.rack} PP-{task.u}", true) };
            foreach (var other in racks) labels.Add(($"{other} PP-{task.u}", false));
            int k = 0;
            while (labels.Count < 6)
            {
                int pp = 1 + rnd.Next(24);
                if (pp != task.u && labels.All(l => l.Item1 != $"{task.rack} PP-{pp}")) labels.Add(($"{task.rack} PP-{pp}", false));
                if (++k > 50) break;
            }
            labels = labels.OrderBy(_ => rnd.Next()).ToList();
            for (int i = 0; i < labels.Count; i++)
                jumpers.Add(new Jumper { label = labels[i].Item1, right = labels[i].Item2, p = new Vector2(p.x + 300, p.y + 140 + i * 82) });
            for (int c = 0; c < Catalog.Carriers.Length; c++)
                for (int i = 0; i < 12; i++)
                    ports.Add(new Port { carrier = Catalog.Carriers[c], n = i + 1, p = new Vector2(p.x + 560 + i * 54, p.y + 160 + c * 170) });
            title = $"Meet-me room: cross-connect {task.rack} to {task.component}";
            hint = $"Drag the jumper labelled {task.rack} PP-{task.u} onto {task.component} port {task.slot}. The other jumpers are other customers' live circuits.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (Pressed) dragging = jumpers.FirstOrDefault(j => !j.plugged && Vector2.Distance(j.p, m) < 22f);
            if (!Released || dragging == null) return;
            var port = ports.FirstOrDefault(x => Vector2.Distance(x.p, m) < 20f);
            var jp = dragging;
            dragging = null;
            if (port == null) return;
            Audio.Play("plug");
            if (!jp.right) Oops($"{jp.label} is another customer's circuit. You unpatched it and patched it into the wrong carrier", 2f);
            else if (port.carrier != task.component || port.n != task.slot) Oops($"Wrong port: the order says {task.component} port {task.slot}, not {port.carrier} port {port.n}", 0.5f);
            else { jp.plugged = true; Win($"Cross-connect live: {task.rack} PP-{task.u} to {task.component} port {task.slot}."); }
        }

        Port Target => ports.First(x => x.carrier == task.component && x.n == task.slot);

        public override void Draw()
        {
            Frame();
            var p = P;
            Ui.Label(new Rect(p.x + 120, p.y + 90, 300, 24), "CUSTOMER SIDE", 15, Ui.Dim, TextAnchor.UpperLeft, true);
            foreach (var j in jumpers)
            {
                Ui.Label(new Rect(j.p.x - 240, j.p.y - 12, 215, 24), j.label, 17, Ui.Text, TextAnchor.MiddleRight, true);
                Ui.Circle(j.p, 14f, new Color(0.95f, 0.78f, 0.15f));
                Ui.Ring(j.p, 17f, Ui.Line);
                if (j.plugged) Ui.Cable(j.p, Target.p, new Color(0.95f, 0.78f, 0.15f), 4f);
            }
            for (int c = 0; c < Catalog.Carriers.Length; c++)
            {
                var r = new Rect(p.x + 520, p.y + 110 + c * 170, 690, 100);
                Ui.Panel(r, new Color(0.08f, 0.1f, 0.12f, 1f));
                Ui.Label(new Rect(r.x + 12, r.y + 6, 400, 22), Catalog.Carriers[c].ToUpperInvariant(), 15, Ui.Dim, TextAnchor.UpperLeft, true);
            }
            foreach (var port in ports)
            {
                var r = new Rect(port.p.x - 11, port.p.y - 9, 22, 18);
                bool hover = Vector2.Distance(Ui.Mouse, port.p) < 20f;
                bool tester = career.Has("tester") && dragging != null && dragging.right && port == Target;
                Ui.Box(r, new Color(0.02f, 0.02f, 0.03f));
                Ui.Border(r, tester ? Ui.Good : hover ? Ui.Accent : new Color(0.35f, 0.38f, 0.42f), tester ? 3f : 1.5f);
                Ui.Label(new Rect(port.p.x - 15, port.p.y + 12, 30, 18), port.n.ToString(), 12, Ui.Dim, TextAnchor.MiddleCenter);
            }
            if (dragging != null) Ui.Cable(dragging.p, Ui.Mouse, new Color(0.95f, 0.78f, 0.15f), 4f);
        }
    }

    /// <summary>
    /// PDU rebalancing at the rear of the rack: give every unit one cord on feed A and one on feed B,
    /// keeping both feeds under 80% of the breaker. Moving a unit's only live cord drops its power.
    /// </summary>
    public class PowerGame : Minigame
    {
        List<Shift.PowerRow> rows;
        float breaker, rowH, verify;
        bool clamp;

        protected override void Begin()
        {
            rows = Shift.PowerLayout(shift.Rack(task.rack), task.slot, out breaker);
            rowH = Mathf.Min(36f, 470f / Mathf.Max(1, rows.Count));
            clamp = career.Has("clamp");
            title = $"PDU rebalancing: {task.rack} (rear)";
            hint = "Click A, B or OFF to move a cord. Every unit needs one cord on each feed. Never move a unit's ONLY live cord: it drops the unit.";
        }

        bool Solved => rows.All(r => r.dead || r.Redundant) && Shift.FeedLoad(rows, 1) <= breaker * 0.8f && Shift.FeedLoad(rows, 2) <= breaker * 0.8f;

        void SetCord(Shift.PowerRow r, int which, int feed)
        {
            int cur = which == 1 ? r.c1 : r.c2;
            if (cur == feed || r.dead) return;
            int other = which == 1 ? r.c2 : r.c1;
            Audio.Play("plug");
            if (cur != 0 && other == 0)
            {
                // Pulling the only live cord, even to move it, drops the unit.
                r.dead = true;
                Oops($"U{r.dev.u} {r.dev.Def.name} lost power: that was its only live cord", 5f);
                shift.FollowUp(task.rack, r.dev, TaskKind.PowerOn, Fault.NoPower,
                    $"{r.dev.Name} lost power ({task.rack} U{r.dev.u})",
                    $"You pulled the only live power cord on {r.dev.Name} during PDU rebalancing. Power it back on and boot it from its OS disk.");
            }
            if (which == 1) r.c1 = feed; else r.c2 = feed;
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (!Solved) { verify = 0; return; }
            verify += dt;
            if (verify > 0.6f) Win("Both feeds balanced and every unit is dual-corded. Monitoring is green.");
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            float a = Shift.FeedLoad(rows, 1), b = Shift.FeedLoad(rows, 2);
            void Meter(Rect r, string name, float amps, Color c)
            {
                float f = amps / breaker;
                Ui.Label(new Rect(r.x, r.y, r.width, 24), $"{name}: {amps:0.0}A of {breaker:0}A ({f * 100:0}%)", 18, f > 0.8f ? Ui.Bad : Ui.Text, TextAnchor.UpperLeft, true);
                var bar = new Rect(r.x, r.y + 30, r.width, 22);
                Ui.Bar(bar, f, f > 0.8f ? Ui.Bad : c);
                Ui.Box(new Rect(bar.x + bar.width * 0.8f - 1, bar.y - 4, 3, bar.height + 8), Ui.Warn);
            }
            Meter(new Rect(p.x + 40, p.y + 80, 560, 60), "Feed A (red)", a, new Color(0.85f, 0.3f, 0.25f));
            Meter(new Rect(p.x + 660, p.y + 80, 560, 60), "Feed B (blue)", b, new Color(0.3f, 0.5f, 0.95f));
            float y = p.y + 150;
            Ui.Label(new Rect(p.x + 40, y, 400, 22), "UNIT", 14, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(p.x + 560, y, 260, 22), "PSU 1", 14, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(p.x + 880, y, 260, 22), "PSU 2", 14, Ui.Dim, TextAnchor.UpperLeft, true);
            y += 24;
            foreach (var r in rows)
            {
                var row = new Rect(p.x + 40, y, p.width - 80, rowH - 3);
                Ui.Box(row, r.dead ? new Color(0.3f, 0.05f, 0.05f, 0.8f) : r.Redundant ? new Color(0.05f, 0.15f, 0.08f, 0.8f) : new Color(0.08f, 0.1f, 0.12f, 0.9f));
                string amps = clamp ? $"   {r.amps:0.0}A" : "";
                Ui.Label(new Rect(row.x + 10, row.y, 500, row.height), $"U{r.dev.u}  {r.dev.Def.name}{amps}" + (r.dead ? "  <color=#ff7a5c>NO POWER</color>" : ""), Mathf.Min(16, (int)rowH - 14 + 10), Ui.Text, TextAnchor.MiddleLeft, false, false);
                for (int which = 1; which <= 2; which++)
                {
                    int cur = which == 1 ? r.c1 : r.c2;
                    float x0 = which == 1 ? p.x + 560 : p.x + 880;
                    string[] names = { "OFF", "A", "B" };
                    for (int f = 1; f <= 3; f++)
                    {
                        int feed = f % 3; // A, B, then OFF last
                        var br = new Rect(x0 + (f - 1) * 86, row.y + 2, 80, row.height - 4);
                        bool on = cur == feed;
                        Color tint = feed == 1 ? new Color(0.85f, 0.3f, 0.25f) : feed == 2 ? new Color(0.3f, 0.5f, 0.95f) : Ui.Dim;
                        Ui.Box(br, on ? tint * new Color(1, 1, 1, 0.85f) : Ui.Hover(br) && !r.dead ? new Color(0.2f, 0.24f, 0.28f) : new Color(0.12f, 0.14f, 0.16f));
                        Ui.Label(br, names[feed], 14, on ? Color.white : Ui.Dim, TextAnchor.MiddleCenter, true);
                        if (!r.dead && Event.current.type == EventType.MouseDown && br.Contains(Event.current.mousePosition))
                        {
                            Event.current.Use();
                            SetCord(r, which, feed);
                        }
                    }
                }
                y += rowH;
            }
        }
    }
}
