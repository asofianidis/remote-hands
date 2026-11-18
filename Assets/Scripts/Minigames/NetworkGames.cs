using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>At the rear of the rack: follow one fiber through the tangle up to the patch panel port it lands on.</summary>
    public class TraceGame : Minigame
    {
        public override bool InWorld => true;
        class Cab { public string label; public int port; public Vector3 a; public bool target; public LineRenderer line; public Color color; }
        readonly List<Cab> cabs = new List<Cab>();
        readonly Vector3[] ports = new Vector3[24];

        const float Back = -RackView.Depth / 2f + 0.05f + 0.9f + 0.014f;
        const float Panel = 0.475f;

        protected override void Begin()
        {
            var rack = shift.Rack(task.rack);
            var rv = RackV;
            MakeRig(rv.transform);
            var rnd = new System.Random(task.deviceId * 7 + task.slot);
            // The customer's cable plus up to seven others from nearby units, so the tangle fills the view.
            var labels = new List<(string label, int u, int portIdx)> { ($"U{task.u} {task.component}", task.u, 1) };
            foreach (var d in rack.devices.Where(d => Mathf.Abs(d.u - task.u) <= 8).OrderBy(_ => rnd.Next()))
            {
                var dp = d.Def.ports.Select((p, i) => (p, i)).Where(x => !x.p.StartsWith("PSU")).ToList();
                foreach (var (p, i) in dp)
                {
                    string l = $"U{d.u} {p}";
                    if (labels.Count < 8 && labels.All(x => x.label != l)) labels.Add((l, d.u, i));
                }
            }
            float topY = labels.Max(x => RackView.UY(x.u + 1));
            // A 24-port patch panel just above the highest of those units.
            float py = Mathf.Clamp(topY + 0.12f, 0.4f, 1.86f);
            Box(rig, new Vector3(0, py + 0.016f, Panel + 0.004f), new Vector3(0.44f, 0.07f, 0.006f), Mats.Lit(new Color(0.07f, 0.08f, 0.1f), 0.3f, 0.3f), "patch panel");
            var hole = Mats.Unlit(new Color(0.01f, 0.01f, 0.012f));
            for (int i = 0; i < 24; i++)
            {
                var lp = new Vector3(Mathf.Lerp(0.18f, -0.18f, (i % 12) / 11f), py + (i < 12 ? 0.032f : 0f), Panel + 0.009f);
                Box(rig, lp, new Vector3(0.016f, 0.012f, 0.004f), hole, "port");
                ports[i] = rv.transform.TransformPoint(lp);
            }
            var free = Enumerable.Range(1, 24).Where(x => x != task.slot).OrderBy(_ => rnd.Next()).ToList();
            float minY = py;
            for (int i = 0; i < labels.Count; i++)
            {
                var (label, u, pi) = labels[i];
                var dev = rack.devices.FirstOrDefault(d => d.u == u);
                float y = RackView.UY(u) + (dev != null ? dev.Def.heightU * RackView.UnitH / 2f : RackView.UnitH / 2f);
                var a = new Vector3(-0.16f + pi * 0.07f + (float)(rnd.NextDouble() - 0.5) * 0.02f, y, Back);
                minY = Mathf.Min(minY, y);
                bool target = i == 0;
                int port = target ? task.slot : free[i];
                var b = ports[port - 1];
                var aw = rv.transform.TransformPoint(a);
                var lb = rv.transform.InverseTransformPoint(b);
                // Control points wander across the rack and in depth, so the jumpers cross each other.
                var c1 = rv.transform.TransformPoint(new Vector3((float)(rnd.NextDouble() - 0.5) * 0.5f, Mathf.Lerp(a.y, lb.y, 0.2f + (float)rnd.NextDouble() * 0.4f), Back + 0.02f + (float)rnd.NextDouble() * 0.05f));
                var c2 = rv.transform.TransformPoint(new Vector3((float)(rnd.NextDouble() - 0.5) * 0.5f, Mathf.Lerp(a.y, lb.y, 0.5f + (float)rnd.NextDouble() * 0.4f), Panel - 0.01f + (float)rnd.NextDouble() * 0.03f));
                float shade = 0.75f + (float)rnd.NextDouble() * 0.25f;
                var col = new Color(0.95f * shade, 0.78f * shade, 0.15f);
                var line = Mats.Line(rig, col, 0.005f);
                Mats.Bezier(line, aw, c1, c2, b);
                Box(rig, a, new Vector3(0.014f, 0.012f, 0.012f), Mats.Lit(col * 0.8f), "plug");
                cabs.Add(new Cab { label = label, port = port, a = aw, target = target, line = line, color = col });
            }
            float cy = (minY - 0.05f + py + 0.08f) / 2f;
            float dist = FitDistance(0.62f, py + 0.08f - minY + 0.12f, 0.88f);
            Look(new Vector3(0, cy, RackView.Depth / 2f + dist), new Vector3(0, cy, Back));
            title = $"Trace cable: U{task.u} {task.component} in {task.rack} (rear)";
            hint = rack.tidy
                ? $"This rack is labelled at both ends. Find U{task.u} {task.component}'s label on the patch panel and click its port."
                : $"Follow the yellow jumper from U{task.u} {task.component} up through the tangle and click the patch panel port it lands on.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (career.Has("toner"))
                foreach (var c in cabs.Where(x => x.target))
                    c.line.startColor = c.line.endColor = Color.Lerp(c.color, Color.white, 0.5f + 0.5f * Mathf.Sin(time * 8f));
            if (!Pressed) return;
            int best = -1;
            float bd = 13f;
            for (int i = 0; i < ports.Length; i++)
            {
                float d = Vector2.Distance(m, ToUi(ports[i]));
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return;
            if (best + 1 == task.slot) Win($"Found it: U{task.u} {task.component} lands on patch panel port {task.slot}.");
            else if (cabs.Any(c => c.port == best + 1)) Oops($"Port {best + 1} is a different cable. You'd have patched the wrong circuit", 1f);
            else Oops($"Nothing is plugged into port {best + 1}", 0.5f);
        }

        public override void Draw()
        {
            WorldHud();
            var q = new Rect(20, 110, 440, 70);
            Ui.Box(q, new Color(0.03f, 0.05f, 0.07f, 0.88f));
            Ui.Label(new Rect(q.x + 16, q.y + 10, q.width - 32, 50), $"Which patch panel port does <b>U{task.u} {task.component}</b> land on?", 18, Ui.Text);
            for (int i = 0; i < cabs.Count; i++) Chip(cabs[i].a, cabs[i].label, 12, cabs[i].target ? Ui.Accent : Ui.Text, i % 2 == 0 ? 16 : 36);
            // A rack that's had a cable audit has every jumper labelled at both ends.
            if (shift.Rack(task.rack).tidy)
                for (int i = 0; i < cabs.Count; i++) Chip(ports[cabs[i].port - 1], cabs[i].label, 10, Ui.Good, cabs[i].port <= 12 ? -40 : 40);
            var m = Ui.Mouse;
            for (int i = 0; i < ports.Length; i++)
            {
                var p = ToUi(ports[i]);
                if (Vector2.Distance(m, p) < 13f) Ui.Ring(p, 11f, Ui.Accent);
                Ui.Label(new Rect(p.x - 12, i < 12 ? p.y - 24 : p.y + 8, 24, 14), (i + 1).ToString(), 10, Ui.Dim, TextAnchor.MiddleCenter);
            }
        }
    }

    /// <summary>On the meet-me room frame: patch the customer's circuit through to the carrier's port. Only that circuit.</summary>
    public class PatchGame : Minigame
    {
        public override bool InWorld => true;
        class Jumper { public string label; public bool right; public Vector3 anchor, home, pos; public bool plugged; public Transform plug; public LineRenderer line; }
        class Port { public string carrier; public int n; public Vector3 w; }
        readonly List<Jumper> jumpers = new List<Jumper>();
        readonly List<Port> ports = new List<Port>();
        readonly List<(string name, Vector3 w)> rows = new List<(string, Vector3)>();
        Jumper dragging;
        Plane work;

        const float Face = 0.495f;

        protected override void Begin()
        {
            MakeRig(game.facility.transform);
            var rnd = new System.Random(task.u * 17 + task.slot);
            // Carrier panels across the frame (it faces +X, into the hall; +Z is screen-right).
            var plate = Mats.Lit(new Color(0.07f, 0.08f, 0.1f), 0.3f, 0.3f);
            var hole = Mats.Unlit(new Color(0.01f, 0.01f, 0.012f));
            for (int c = 0; c < Catalog.Carriers.Length; c++)
            {
                float y = 1.78f - c * 0.26f;
                Box(rig, new Vector3(Face - 0.004f, y, 12.5f), new Vector3(0.006f, 0.16f, 2.45f), plate, "carrier panel");
                rows.Add((Catalog.Carriers[c], new Vector3(Face, y + 0.075f, 11.66f)));
                for (int i = 0; i < 12; i++)
                {
                    var w = new Vector3(Face, y, 11.75f + i * 0.136f);
                    Box(rig, w, new Vector3(0.004f, 0.03f, 0.04f), hole, "port");
                    ports.Add(new Port { carrier = Catalog.Carriers[c], n = i + 1, w = w });
                }
            }
            // Customer-side jumpers hang down from the cage feeds above.
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
            var fiber = new Color(0.95f, 0.78f, 0.15f);
            for (int i = 0; i < labels.Count; i++)
            {
                float z = 11.8f + i * 0.28f;
                var j = new Jumper { label = labels[i].Item1, right = labels[i].Item2, anchor = new Vector3(Face + 0.03f, 2.25f, z), home = new Vector3(Face + 0.08f, 0.82f, z) };
                j.pos = j.home;
                j.plug = Box(rig, j.home, new Vector3(0.02f, 0.035f, 0.02f), Mats.Lit(new Color(0.85f, 0.85f, 0.88f)), "jumper").transform;
                j.line = Mats.Line(rig, fiber, 0.006f);
                jumpers.Add(j);
            }
            work = new Plane(Vector3.right, new Vector3(Face + 0.06f, 0, 0));
            float dist = FitDistance(1.95f, 1.55f, 0.92f);
            Look(new Vector3(Face + dist, 1.32f, 12.5f), new Vector3(Face, 1.32f, 12.5f));
            title = $"Meet-me room: cross-connect {task.rack} to {task.component}";
            hint = $"Drag the jumper tagged {task.rack} PP-{task.u} up onto {task.component} port {task.slot}. The other jumpers are other customers' live circuits.";
        }

        Port Target => ports.First(x => x.carrier == task.component && x.n == task.slot);

        protected override void Update(float dt, Vector2 m)
        {
            if (Pressed) dragging = jumpers.Where(j => !j.plugged && Vector2.Distance(ToUi(j.pos), m) < 22f).OrderBy(j => Vector2.Distance(ToUi(j.pos), m)).FirstOrDefault();
            if (dragging != null) dragging.pos = MouseOn(work);
            if (Released && dragging != null)
            {
                var jp = dragging;
                dragging = null;
                var port = ports.Where(x => Vector2.Distance(ToUi(x.w), m) < 18f).OrderBy(x => Vector2.Distance(ToUi(x.w), m)).FirstOrDefault();
                if (port != null)
                {
                    Audio.Play("plug");
                    if (!jp.right) Oops($"{jp.label} is another customer's circuit. You unpatched it and patched it into the wrong carrier", 2f);
                    else if (port.carrier != task.component || port.n != task.slot) Oops($"Wrong port: the order says {task.component} port {task.slot}, not {port.carrier} port {port.n}", 0.5f);
                    else { jp.plugged = true; jp.pos = port.w + Vector3.right * 0.012f; Win($"Cross-connect live: {task.rack} PP-{task.u} to {task.component} port {task.slot}."); }
                }
                if (!jp.plugged) jp.pos = jp.home;
            }
            foreach (var j in jumpers)
            {
                j.plug.position = j.pos;
                Mats.Sag(j.line, j.anchor, j.pos + Vector3.up * 0.018f, j.plugged || j == dragging ? 0.08f : 0.02f, 14);
            }
        }

        public override void Draw()
        {
            WorldHud();
            foreach (var (name, w) in rows) Tag(w, name.ToUpperInvariant(), 14, Ui.Dim, 0, 260, TextAnchor.MiddleLeft);
            var m = Ui.Mouse;
            bool tester = career.Has("tester") && dragging != null && dragging.right;
            foreach (var port in ports)
            {
                var p = ToUi(port.w);
                if (tester && port == Target) Ui.Ring(p, 14f, Ui.Good);
                else if (dragging != null && Vector2.Distance(m, p) < 18f) Ui.Ring(p, 12f, Ui.Accent);
                Ui.Label(new Rect(p.x - 12, p.y + 12, 24, 14), port.n.ToString(), 10, Ui.Dim, TextAnchor.MiddleCenter);
            }
            foreach (var j in jumpers) Chip(j.pos, j.label, 13, j.right && tester ? Ui.Good : Ui.Text, 28);
        }
    }

    /// <summary>
    /// PDU rebalancing at the rear of the real rack: drag power cords between the red A strip and the
    /// blue B strip so every unit has one cord on each, keeping both feeds under 80% of the breaker.
    /// Moving a unit's only live cord drops it.
    /// </summary>
    public class PowerGame : Minigame
    {
        public override bool InWorld => true;
        List<Shift.PowerRow> rows;
        float breaker, verify;
        bool clamp;
        class Cord { public Shift.PowerRow row; public int which; public Vector3 psu; public LineRenderer line; }
        readonly List<Cord> cords = new List<Cord>();
        Cord dragging;
        Vector2 dragStart;
        Plane work;
        Transform rack;
        /// <summary>The camera frames part of the rack at a time; scroll or W/S moves along it.</summary>
        float viewY;
        const float ViewH = 1.05f;

        const float Back = -RackView.Depth / 2f + 0.05f + 0.9f + 0.014f;
        const float Pdu = RackView.Depth / 2f - 0.07f + 0.026f;

        protected override void Begin()
        {
            rows = Shift.PowerLayout(shift.Rack(task.rack), task.slot, out breaker);
            clamp = career.Has("clamp");
            var rv = RackV;
            rack = rv.transform;
            MakeRig(rack);
            HideOnRack(0, true);
            foreach (var r in rows)
            {
                float y = RackView.UY(r.dev.u) + r.dev.Def.heightU * RackView.UnitH / 2f;
                for (int which = 1; which <= 2; which++)
                {
                    var lp = new Vector3(0.1f + (which - 1) * 0.09f, y, Back);
                    var line = Mats.Line(rig, which == 1 ? new Color(0.75f, 0.15f, 0.12f) : new Color(0.15f, 0.3f, 0.8f), 0.007f);
                    cords.Add(new Cord { row = r, which = which, psu = rack.TransformPoint(lp), line = line });
                }
            }
            work = new Plane(rack.forward, rack.TransformPoint(new Vector3(0, 0, Pdu)));
            // Start on the part of the rack with the most work.
            float broken = rows.Where(r => !r.Redundant).Select(r => RackView.UY(r.dev.u)).DefaultIfEmpty(1f).Average();
            viewY = Mathf.Clamp(broken, 0.12f + ViewH / 2f, 2.08f - ViewH / 2f);
            FrameView();
            title = $"PDU rebalancing: {task.rack} (rear)";
            hint = $"Drag a cord from its PSU onto the red A strip or the blue B strip (drop it elsewhere to unplug). Every unit needs one cord on each feed; never move a unit's ONLY live cord. Scroll or {Keys.Label(Keys.Action.Forward)}/{Keys.Label(Keys.Action.Back)} to look up and down the rack.";
        }

        void FrameView()
        {
            float dist = FitDistance(0.8f, ViewH, 0.95f);
            // From behind the rack +X is screen-left: shift right to clear the meters.
            Look(new Vector3(0.12f, viewY, RackView.Depth / 2f + dist), new Vector3(0.12f, viewY, Back));
        }

        bool Solved => rows.All(r => r.dead || r.Redundant) && Shift.FeedLoad(rows, 1) <= breaker * 0.8f && Shift.FeedLoad(rows, 2) <= breaker * 0.8f;

        int Feed(Cord c) => c.which == 1 ? c.row.c1 : c.row.c2;

        /// <summary>Where a cord plugs into a strip (or dangles when unplugged), staggered so cords don't overlap.</summary>
        Vector3 Plug(Cord c, int feed)
        {
            var lp = rack.InverseTransformPoint(c.psu);
            if (feed == 0) return rack.TransformPoint(lp + new Vector3(0, -0.06f, 0.05f));
            return rack.TransformPoint(new Vector3(feed == 1 ? -RackView.PduX : RackView.PduX, lp.y + (c.which == 1 ? 0.008f : -0.008f), Pdu));
        }

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

        /// <summary>Which strip (1 A, 2 B) the mouse is over, or 0.</summary>
        int StripAt(Vector2 m)
        {
            for (int f = 1; f <= 2; f++)
            {
                float x = f == 1 ? -RackView.PduX : RackView.PduX;
                var top = ToUi(rack.TransformPoint(new Vector3(x, 1.95f, Pdu)));
                var bot = ToUi(rack.TransformPoint(new Vector3(x, 0.15f, Pdu)));
                if (Mathf.Abs(m.x - top.x) < 22f && m.y > top.y - 10f && m.y < bot.y + 10f) return f;
            }
            return 0;
        }

        Cord CordAt(Vector2 m) => cords.Where(c => !c.row.dead && Vector2.Distance(ToUi(c.psu), m) < 12f).OrderBy(c => Vector2.Distance(ToUi(c.psu), m)).FirstOrDefault();

        protected override void Update(float dt, Vector2 m)
        {
            float scroll = Input.mouseScrollDelta.y * 0.12f + ((Keys.Held(Keys.Action.Forward) ? 1 : 0) - (Keys.Held(Keys.Action.Back) ? 1 : 0)) * dt * 0.9f;
            if (scroll != 0)
            {
                viewY = Mathf.Clamp(viewY + scroll, 0.12f + ViewH / 2f, 2.08f - ViewH / 2f);
                FrameView();
            }
            if (Pressed) { dragging = CordAt(m); dragStart = m; }
            if (Released && dragging != null)
            {
                int strip = StripAt(m);
                if (strip > 0) SetCord(dragging.row, dragging.which, strip);
                else if (Vector2.Distance(m, dragStart) > 30f) SetCord(dragging.row, dragging.which, 0);
                dragging = null;
            }
            foreach (var c in cords)
            {
                var end = c == dragging ? MouseOn(work) : Plug(c, Feed(c));
                Mats.Sag(c.line, c.psu, end, Feed(c) == 0 && c != dragging ? 0.01f : 0.025f, 10);
                c.line.startColor = c.line.endColor = c.row.dead ? new Color(0.3f, 0.3f, 0.3f) : c.which == 1 ? new Color(0.75f, 0.15f, 0.12f) : new Color(0.15f, 0.3f, 0.8f);
            }
            if (!Solved) { verify = 0; return; }
            verify += dt;
            if (verify > 0.6f) Win("Both feeds balanced and every unit is dual-corded. Monitoring is green.");
        }

        public override void Draw()
        {
            WorldHud();
            float a = Shift.FeedLoad(rows, 1), b = Shift.FeedLoad(rows, 2);
            var panel = new Rect(20, 20, 420, 170);
            Ui.Box(panel, new Color(0.03f, 0.05f, 0.07f, 0.88f));
            void Meter(float y, string name, float amps, Color c)
            {
                float f = amps / breaker;
                Ui.Label(new Rect(panel.x + 16, y, 390, 24), $"{name}: {amps:0.0}A of {breaker:0}A ({f * 100:0}%)", 17, f > 0.8f ? Ui.Bad : Ui.Text, TextAnchor.UpperLeft, true);
                var bar = new Rect(panel.x + 16, y + 28, 388, 18);
                Ui.Bar(bar, f, f > 0.8f ? Ui.Bad : c);
                Ui.Box(new Rect(bar.x + bar.width * 0.8f - 1, bar.y - 4, 3, bar.height + 8), Ui.Warn);
            }
            Meter(panel.y + 14, "Feed A (red)", a, new Color(0.85f, 0.3f, 0.25f));
            Meter(panel.y + 90, "Feed B (blue)", b, new Color(0.3f, 0.5f, 0.95f));
            Tag(rack.TransformPoint(new Vector3(-RackView.PduX, 2.0f, Pdu)), "A", 22, new Color(1f, 0.45f, 0.4f));
            Tag(rack.TransformPoint(new Vector3(RackView.PduX, 2.0f, Pdu)), "B", 22, new Color(0.5f, 0.65f, 1f));
            var m = Ui.Mouse;
            foreach (var r in rows)
            {
                var mid = rack.TransformPoint(new Vector3(0, RackView.UY(r.dev.u) + r.dev.Def.heightU * RackView.UnitH / 2f, Back));
                if (r.dead) Chip(mid, "NO POWER", 11, Ui.Bad);
                else if (!r.Redundant) Tag(rack.TransformPoint(new Vector3(0.36f, RackView.UY(r.dev.u) + r.dev.Def.heightU * RackView.UnitH / 2f, Back)), "!", 16, Ui.Warn, 0, 20, TextAnchor.MiddleRight);
                if (clamp) Tag(rack.TransformPoint(new Vector3(-0.33f, RackView.UY(r.dev.u) + r.dev.Def.heightU * RackView.UnitH / 2f, Back)), $"{r.amps:0.0}A", 11, Ui.Dim, 0, 60, TextAnchor.MiddleLeft, false);
            }
            foreach (var c in cords)
            {
                var p = ToUi(c.psu);
                bool hover = Vector2.Distance(m, p) < 12f && !c.row.dead;
                Ui.Circle(p, 4f, Feed(c) == 0 ? new Color(0.3f, 0.3f, 0.3f) : c.which == 1 ? new Color(0.9f, 0.3f, 0.25f) : new Color(0.3f, 0.5f, 1f));
                if (hover || c == dragging)
                {
                    Ui.Ring(p, 9f, Ui.Accent);
                    string[] feeds = { "unplugged", "on A", "on B" };
                    Chip(c.psu, $"U{c.row.dev.u} {c.row.dev.Def.name}  PSU{c.which} {feeds[Feed(c)]}", 13, Ui.Text, -24);
                }
            }
            if (dragging != null)
            {
                int strip = StripAt(m);
                if (strip > 0) Tag(MouseOn(work), strip == 1 ? "plug into A" : "plug into B", 14, Ui.Good, -22);
                else if (Vector2.Distance(m, dragStart) > 30f) Tag(MouseOn(work), "unplug", 14, Ui.Warn, -22);
            }
        }
    }
}
