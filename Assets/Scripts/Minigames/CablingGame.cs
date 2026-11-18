using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    public enum CableMode { Install, Reseat, Unplug }

    /// <summary>
    /// At the rear of the real rack: patch every connector on the unit to the port the work order calls
    /// for, with 3D cables to the switch, the patch panel and the A/B PDU strips.
    /// Reseat: everything is patched except one loose cable that must be found and reseated.
    /// Unplug (decommissioning): pull every cable of this unit, and only this unit's: the neighbours'
    /// cables run through the same space.
    /// </summary>
    public class CablingGame : Minigame
    {
        public override bool InWorld => true;
        public CableMode mode;
        bool reseat => mode == CableMode.Reseat;
        bool unplug => mode == CableMode.Unplug;

        class Src { public string name, dest; public Vector3 w; public bool connected, loose, neighbour; public Color color; public LineRenderer line; }
        class Dst { public string id, label; public Vector3 w; }
        class Group { public string name; public Vector3 w; }

        readonly List<Src> srcs = new List<Src>();
        readonly List<Dst> dsts = new List<Dst>();
        readonly List<Group> groups = new List<Group>();
        Src dragging;
        DevDef def;
        float flash;
        Plane work;
        LineRenderer dragLine;

        const float Back = -RackView.Depth / 2f + 0.05f + 0.9f + 0.018f;
        const float Field = 0.47f;
        const float Pdu = RackView.Depth / 2f - 0.07f + 0.028f;

        protected override void Begin()
        {
            var rack = shift.Rack(task.rack);
            Device unitDev = mode == CableMode.Install ? rack.At(task.u) : Dev;
            def = unitDev != null ? unitDev.Def : Catalog.Dev(task.def);
            var rnd = new System.Random(task.u * 31 + task.rack.GetHashCode());
            var rv = RackV;
            MakeRig(rv.transform);
            // Hide the rack's own copy of the unit and everyone's power cords, so only this job's cables show.
            if (unitDev != null) HideOnRack(unitDev.id, true);
            float h = def.heightU * RackView.UnitH;
            float bottom = RackView.UY(task.u), top = bottom + h, dy = bottom + h / 2f;
            bool isSwitch = def.kind == DevKind.Switch;

            // The unit's rear, rebuilt in the rig so its connectors can be worked on.
            Models.Spawn(Models.For(def), rig, new Vector3(0, bottom, -RackView.Depth / 2f + 0.05f), palette: Models.Unit(def));

            // A patch field around the unit: switch and patch panel above it (below it near the top of the
            // rack), and outlets on the PDU strips at its height.
            bool above = top + 0.19f < RackView.Height - 0.08f;
            float Row(float off) => above ? top + off : bottom - off;
            var plate = Mats.Lit(new Color(0.07f, 0.08f, 0.1f), 0.3f, 0.3f);
            var hole = Mats.Unlit(new Color(0.24f, 0.26f, 0.3f));
            void Ports(string prefix, int count, int perRow, float y0, float dyRow, float x0, float x1, string name)
            {
                int rows = Mathf.CeilToInt(count / (float)perRow);
                float yMid = y0 + (rows - 1) * dyRow * (above ? 1 : -1) / 2f;
                Box(rig, new Vector3((x0 + x1) / 2f, yMid, Field + 0.004f), new Vector3(x1 - x0 + 0.04f, rows * Mathf.Abs(dyRow) + 0.018f, 0.006f), plate, "panel");
                groups.Add(new Group { name = name, w = rv.transform.TransformPoint(new Vector3(x0 - 0.02f, yMid, Field)) });
                for (int i = 0; i < count; i++)
                {
                    int r = i / perRow, c = i % perRow;
                    var lp = new Vector3(Mathf.Lerp(x0, x1, perRow == 1 ? 0.5f : c / (float)(perRow - 1)), y0 + r * dyRow * (above ? 1 : -1), Field + 0.009f);
                    Box(rig, lp, new Vector3(0.016f, 0.012f, 0.004f), hole, "port");
                    dsts.Add(new Dst { id = $"{prefix}-{i + 1}", label = (i + 1).ToString(), w = rv.transform.TransformPoint(lp) });
                }
            }
            Ports(isSwitch ? "CORE" : "SW", 24, 12, Row(0.115f), 0.03f, -0.17f, 0.17f, isSwitch ? "CORE UPLINKS" : "ToR SWITCH");
            Ports("PP", 12, 12, Row(0.05f), 0f, -0.17f, 0.17f, "PATCH PANEL");
            // PDU outlets run vertically down the real strips, centred on the unit.
            float span = 0.3f, oy0 = Mathf.Clamp(dy - span / 2f, 0.18f, RackView.Height - 0.2f - span);
            for (int i = 0; i < 12; i++)
            {
                float oy = oy0 + span - i * span / 11f;
                foreach (var (x, prefix) in new[] { (-RackView.PduX, "PDU-A"), (RackView.PduX, "PDU-B") })
                {
                    var lp = new Vector3(x, oy, Pdu);
                    Box(rig, lp, new Vector3(0.026f, 0.016f, 0.004f), hole, "outlet");
                    dsts.Add(new Dst { id = $"{prefix}-{i + 1}", label = (i + 1).ToString(), w = rv.transform.TransformPoint(lp) });
                }
            }
            groups.Add(new Group { name = "PDU A", w = rv.transform.TransformPoint(new Vector3(-RackView.PduX, oy0 + span + 0.03f, Pdu)) });
            groups.Add(new Group { name = "PDU B", w = rv.transform.TransformPoint(new Vector3(RackView.PduX, oy0 + span + 0.03f, Pdu)) });

            // Who plugs into what.
            var usedNet = new HashSet<int>();
            var usedPp = new HashSet<int>();
            var usedPdu = new HashSet<int>();
            int outlet = 1 + rnd.Next(12);
            usedPdu.Add(outlet);
            int Pick(HashSet<int> used, int max) { int v; do v = 1 + rnd.Next(max); while (used.Contains(v)); used.Add(v); return v; }
            var ports = def.ports.Select(x => (name: x, neighbour: false, u: task.u)).ToList();
            if (unplug)
            {
                // A few of the neighbours' cables run right past this unit's connectors.
                var near = rack.devices.Where(d => d.id != task.deviceId && Mathf.Abs(d.u - task.u) <= 4).OrderBy(_ => rnd.Next()).Take(3).ToList();
                foreach (var n in near)
                {
                    string np = n.Def.ports[rnd.Next(n.Def.ports.Length)];
                    ports.Insert(rnd.Next(ports.Count + 1), ($"U{n.u} {np}", true, n.u));
                }
            }
            int own = ports.Count(x => !x.neighbour), k = 0;
            for (int i = 0; i < ports.Count; i++)
            {
                var (port, neighbour, pu) = ports[i];
                string key = neighbour ? port.Substring(port.IndexOf(' ') + 1) : port;
                if (unplug && !neighbour) port = $"U{task.u} {port}";
                string dest; Color c;
                if (key == "PSU A") { dest = neighbour ? $"PDU-A-{Pick(usedPdu, 12)}" : $"PDU-A-{outlet}"; c = new Color(0.75f, 0.15f, 0.12f); }
                else if (key == "PSU B") { dest = neighbour ? $"PDU-B-{Pick(usedPdu, 12)}" : $"PDU-B-{outlet}"; c = new Color(0.15f, 0.3f, 0.8f); }
                else if (key == "mgmt" || key == "ipmi" || key.StartsWith("SAS")) { dest = $"PP-{Pick(usedPp, 12)}"; c = new Color(0.95f, 0.8f, 0.2f); }
                else { dest = (isSwitch ? "CORE-" : "SW-") + Pick(usedNet, 24); c = new Color(0.25f, 0.6f, 1f); }
                Vector3 lp;
                if (neighbour)
                {
                    var nd = rack.devices.First(d => d.u == pu);
                    lp = new Vector3(-0.12f + (float)rnd.NextDouble() * 0.24f, RackView.UY(pu) + nd.Def.heightU * RackView.UnitH / 2f, Back);
                }
                else lp = new Vector3(Mathf.Lerp(-0.17f, 0.17f, own == 1 ? 0.5f : k++ / (float)(own - 1)), dy, Back);
                Box(rig, lp, new Vector3(0.022f, Mathf.Min(0.018f, h * 0.5f), 0.01f), Mats.Lit(c * 0.7f, 0.4f, 0.2f), "connector");
                var src = new Src { name = port, dest = dest, color = c, neighbour = neighbour, connected = unplug, w = rv.transform.TransformPoint(lp) };
                src.line = Mats.Line(rig, c, 0.006f);
                srcs.Add(src);
            }
            dragLine = Mats.Line(rig, Color.white, 0.006f);
            work = new Plane(rv.transform.forward, rv.transform.TransformPoint(new Vector3(0, 0, Field)));

            // Frame everything from the hot aisle.
            float minY = Mathf.Min(Row(0.15f), oy0, bottom) - 0.04f, maxY = Mathf.Max(Row(0.15f), oy0 + span, top) + 0.06f;
            float cy = (minY + maxY) / 2f;
            float dist = FitDistance(0.62f, maxY - minY, 0.86f);
            // Leave room on the left of the screen for the work order (from behind the rack, +X is screen-left).
            Look(new Vector3(0.09f, cy, Pdu + dist), new Vector3(0.09f, cy, Back));

            if (reseat)
            {
                foreach (var s in srcs) s.connected = true;
                var data = srcs.Where(s => !s.name.StartsWith("PSU")).ToList();
                data[rnd.Next(data.Count)].loose = true;
                title = $"Reseat cable: {def.name} in {task.rack} U{task.u} (rear)";
                hint = "One link is down (blinking red). Click its connector to unplug it, then drag it back into the same port. Leave the healthy links alone!";
            }
            else if (unplug)
            {
                title = $"Unplug: {def.name} in {task.rack} U{task.u} (rear)";
                hint = $"Click each connector labelled U{task.u} to unplug it. Read the labels: the other cables belong to the neighbours and are live!";
            }
            else
            {
                title = $"Cabling: {def.name} in {task.rack} U{task.u} (rear)";
                hint = "Drag from each connector on the unit to the port in the work order. Wrong ports cost reputation and the clean-work bonus.";
            }
        }

        Dst DstAt(Vector2 m) => dsts.Where(d => Vector2.Distance(ToUi(d.w), m) < 14f).OrderBy(d => Vector2.Distance(ToUi(d.w), m)).FirstOrDefault();
        Dst DstById(string id) => dsts.FirstOrDefault(d => d.id == id);
        Src SrcAt(Vector2 m, bool connected) => srcs.Where(s => s.connected == connected && Vector2.Distance(ToUi(s.w), m) < 18f).OrderBy(s => Vector2.Distance(ToUi(s.w), m)).FirstOrDefault();

        static string Pretty(string id)
        {
            int k = id.LastIndexOf('-');
            string g = id.Substring(0, k), n = id.Substring(k + 1);
            return g switch { "SW" => $"ToR port {n}", "CORE" => $"Core uplink {n}", "PP" => $"Patch panel {n}", "PDU-A" => $"PDU A outlet {n}", _ => $"PDU B outlet {n}" };
        }

        protected override void Update(float dt, Vector2 m)
        {
            flash += dt;
            if (unplug) UpdateUnplug(m);
            else
            {
                if (Pressed)
                {
                    var s = SrcAt(m, false) ?? (reseat ? SrcAt(m, true) : null);
                    if (s != null && !s.connected) dragging = s;
                    else if (s != null && reseat)
                    {
                        s.connected = false;
                        Audio.Play("plug");
                        if (!s.loose) Oops($"{s.name} was a healthy link. You just knocked {Dev.owner} offline on it", 2);
                        s.loose = false;
                        dragging = s;
                    }
                }
                if (Released && dragging != null)
                {
                    var d = DstAt(m);
                    if (d != null && d.id == dragging.dest) { dragging.connected = true; Audio.Play("plug"); }
                    else if (d != null) Oops($"Wrong port: {dragging.name} goes to {Pretty(dragging.dest)}, not {Pretty(d.id)}", 0.5f);
                    dragging = null;
                }
                if (srcs.All(s => s.connected && !s.loose)) Win(reseat ? "Link is back up. The customer is online again." : $"{def.name} cabled and powered on.");
            }
            UpdateLines();
        }

        void UpdateUnplug(Vector2 m)
        {
            if (Pressed)
            {
                var s = SrcAt(m, true);
                if (s != null)
                {
                    Audio.Play("plug");
                    // A neighbour's cable: you spot the label and push it straight back in, but the link flapped.
                    if (s.neighbour) Oops($"{s.name} isn't this unit's cable. You knocked a neighbour's link down for a moment", 2f);
                    else s.connected = false;
                }
            }
            if (srcs.Where(s => !s.neighbour).All(s => !s.connected)) Win($"{def.name} is fully unplugged.");
        }

        void UpdateLines()
        {
            foreach (var s in srcs)
            {
                if (s == dragging || !s.connected) { s.line.positionCount = 0; continue; }
                var d = DstById(s.dest);
                bool blink = s.loose && Mathf.Repeat(flash * 2.5f, 1f) < 0.5f;
                s.line.startColor = s.line.endColor = blink ? new Color(1f, 0.2f, 0.15f) : s.color;
                // A loose cable hangs a little out of its port.
                Mats.Sag(s.line, s.w, s.loose ? Vector3.Lerp(d.w, s.w, 0.08f) : d.w, 0.035f);
            }
            if (dragging != null)
            {
                dragLine.startColor = dragLine.endColor = dragging.color;
                Mats.Sag(dragLine, dragging.w, MouseOn(work), 0.02f, 10);
            }
            else dragLine.positionCount = 0;
        }

        public override void Draw()
        {
            WorldHud();
            // Work order clipboard.
            var wo = new Rect(20, 20, 300, 64 + srcs.Count(s => !s.neighbour) * 52);
            Ui.Box(wo, new Color(0.03f, 0.05f, 0.07f, 0.88f));
            Ui.Label(new Rect(wo.x + 14, wo.y + 10, wo.width - 28, 26), unplug ? $"UNPLUG EVERYTHING ON U{task.u}" : "WORK ORDER", 16, Ui.Accent, TextAnchor.UpperLeft, true);
            float y = wo.y + 42;
            foreach (var s in srcs)
            {
                if (s.neighbour) continue;
                bool ok = unplug ? !s.connected : s.connected && !s.loose;
                Ui.Label(new Rect(wo.x + 14, y, wo.width - 28, 22), s.name, 17, ok ? Ui.Good : Ui.Text, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(wo.x + 14, y + 22, wo.width - 28, 22), unplug ? (ok ? "unplugged" : "from " + Pretty(s.dest)) : "-> " + Pretty(s.dest), 15, ok ? Ui.Good : Ui.Dim);
                y += 52;
            }
            foreach (var g in groups) Tag(g.w, g.name, 13, Ui.Dim, -22, 160, TextAnchor.MiddleLeft);
            bool tester = career.Has("tester");
            var hover = DstAt(Ui.Mouse);
            foreach (var d in dsts)
            {
                var p = ToUi(d.w);
                bool highlight = tester && dragging != null && dragging.dest == d.id;
                if (highlight) Ui.Ring(p, 13f, Ui.Good);
                else if (d == hover) Ui.Ring(p, 11f, Ui.Accent);
                if (!d.id.StartsWith("PDU")) Ui.Label(new Rect(p.x - 12, p.y - 24, 24, 14), d.label, 10, Ui.Dim, TextAnchor.MiddleCenter);
                else Ui.Label(new Rect(d.id.StartsWith("PDU-A") ? p.x - 34 : p.x + 12, p.y - 7, 22, 14), d.label, 10, Ui.Dim, d.id.StartsWith("PDU-A") ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            }
            foreach (var s in srcs)
            {
                var p = ToUi(s.w);
                bool blink = s.loose && Mathf.Repeat(flash * 2.5f, 1f) < 0.5f;
                Ui.Circle(p, 7f, blink ? Ui.Bad : s.connected ? s.color : new Color(0.1f, 0.1f, 0.12f));
                Ui.Ring(p, 10f, s.loose ? Ui.Bad : Vector2.Distance(Ui.Mouse, p) < 18f ? Ui.Accent : new Color(1, 1, 1, 0.4f));
                Chip(s.w, s.name, 12, s.loose ? Ui.Bad : Ui.Text, 22);
            }
        }
    }
}
