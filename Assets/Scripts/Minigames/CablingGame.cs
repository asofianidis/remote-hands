using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// Patch every connector on the back of a device to the port the work order calls for.
    /// In reseat mode everything is already patched except one loose cable that must be found and reseated.
    /// </summary>
    public class CablingGame : Minigame
    {
        public bool reseat;

        class Src { public string name, dest; public Vector2 p; public bool connected, loose; public Color color; }
        class Dst { public string id, label; public Vector2 p; }
        class Group { public string name; public Rect r; public int count; public string prefix; }

        readonly List<Src> srcs = new List<Src>();
        readonly List<Dst> dsts = new List<Dst>();
        readonly List<Group> groups = new List<Group>();
        Src dragging;
        DevDef def;
        float flash;

        protected override void Begin()
        {
            def = reseat ? Dev.Def : Catalog.Dev(task.def);
            var rnd = new System.Random(task.u * 31 + task.rack.GetHashCode());
            var p = P;
            bool isSwitch = def.kind == DevKind.Switch;
            groups.Add(new Group { name = isSwitch ? "CORE UPLINKS" : "ToR SWITCH", prefix = isSwitch ? "CORE" : "SW", count = 24, r = new Rect(p.x + 500, p.y + 110, 730, 150) });
            groups.Add(new Group { name = "MGMT / PATCH PANEL", prefix = "PP", count = 12, r = new Rect(p.x + 500, p.y + 290, 730, 90) });
            groups.Add(new Group { name = "PDU A (red feed)", prefix = "PDU-A", count = 12, r = new Rect(p.x + 500, p.y + 410, 730, 90) });
            groups.Add(new Group { name = "PDU B (blue feed)", prefix = "PDU-B", count = 12, r = new Rect(p.x + 500, p.y + 530, 730, 90) });
            foreach (var g in groups)
            {
                int perRow = Mathf.Min(12, g.count);
                int rows = Mathf.CeilToInt(g.count / (float)perRow);
                for (int i = 0; i < g.count; i++)
                {
                    int row = i / perRow, col = i % perRow;
                    float x = g.r.x + 40 + col * ((g.r.width - 80) / (perRow - 1));
                    float y = g.r.y + 52 + row * 50 + (rows == 1 ? 0 : -8);
                    dsts.Add(new Dst { id = $"{g.prefix}-{i + 1}", label = (i + 1).ToString(), p = new Vector2(x, y) });
                }
            }
            var usedNet = new HashSet<int>();
            var usedPp = new HashSet<int>();
            int outlet = 1 + rnd.Next(12);
            int Pick(HashSet<int> used, int max) { int v; do v = 1 + rnd.Next(max); while (used.Contains(v)); used.Add(v); return v; }
            for (int i = 0; i < def.ports.Length; i++)
            {
                string port = def.ports[i];
                string dest; Color c;
                if (port == "PSU A") { dest = $"PDU-A-{outlet}"; c = new Color(0.55f, 0.12f, 0.1f); }
                else if (port == "PSU B") { dest = $"PDU-B-{outlet}"; c = new Color(0.12f, 0.2f, 0.55f); }
                else if (port == "mgmt" || port == "ipmi" || port.StartsWith("SAS")) { dest = $"PP-{Pick(usedPp, 12)}"; c = new Color(0.95f, 0.8f, 0.2f); }
                else { dest = (isSwitch ? "CORE-" : "SW-") + Pick(usedNet, 24); c = new Color(0.25f, 0.6f, 1f); }
                srcs.Add(new Src { name = port, dest = dest, color = c, p = new Vector2(p.x + 330, p.y + 150 + i * (440f / Mathf.Max(1, def.ports.Length - 1))) });
            }
            if (reseat)
            {
                foreach (var s in srcs) s.connected = true;
                var data = srcs.Where(s => !s.name.StartsWith("PSU")).ToList();
                data[rnd.Next(data.Count)].loose = true;
                title = $"Reseat cable: {def.name} in {task.rack} U{task.u}";
                hint = "One link is down (blinking red). Click its connector to unplug it, then drag it back into the same port. Leave the healthy links alone!";
            }
            else
            {
                title = $"Cabling: {def.name} in {task.rack} U{task.u}";
                hint = "Drag from each connector on the device to the port listed in the work order. Wrong ports cost time.";
            }
        }

        Dst DstAt(Vector2 m) => dsts.FirstOrDefault(d => Vector2.Distance(d.p, m) < 20f);
        Dst DstById(string id) => dsts.FirstOrDefault(d => d.id == id);

        static string Pretty(string id)
        {
            int k = id.LastIndexOf('-');
            string g = id.Substring(0, k), n = id.Substring(k + 1);
            return g switch { "SW" => $"ToR port {n}", "CORE" => $"Core uplink {n}", "PP" => $"Patch panel {n}", "PDU-A" => $"PDU A outlet {n}", _ => $"PDU B outlet {n}" };
        }

        protected override void Update(float dt, Vector2 m)
        {
            flash += dt;
            if (Pressed)
            {
                var s = srcs.FirstOrDefault(x => Vector2.Distance(x.p, m) < 24f);
                if (s != null)
                {
                    if (!s.connected) dragging = s;
                    else if (reseat)
                    {
                        s.connected = false;
                        Audio.Play("plug");
                        if (!s.loose) Oops($"{s.name} was a healthy link. You just knocked {Dev.owner} offline on it", 2);
                        s.loose = false;
                        dragging = s;
                    }
                }
            }
            if (Released && dragging != null)
            {
                var d = DstAt(m);
                if (d != null && d.id == dragging.dest)
                {
                    dragging.connected = true;
                    Audio.Play("plug");
                }
                else if (d != null) Oops($"Wrong port: {dragging.name} goes to {Pretty(dragging.dest)}, not {Pretty(d.id)}", 0.5f);
                dragging = null;
            }
            if (srcs.All(s => s.connected && !s.loose)) Win(reseat ? "Link is back up. The customer is online again." : $"{def.name} cabled and powered on.");
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            // Work order
            var wo = new Rect(p.x + 24, p.y + 84, 250, 560);
            Ui.Panel(wo, Ui.Bg2);
            Ui.Label(new Rect(wo.x + 12, wo.y + 10, wo.width - 24, 26), "WORK ORDER", 16, Ui.Accent, TextAnchor.UpperLeft, true);
            float y = wo.y + 42;
            foreach (var s in srcs)
            {
                bool ok = s.connected && !s.loose;
                Ui.Label(new Rect(wo.x + 12, y, wo.width - 24, 22), s.name, 17, ok ? Ui.Good : Ui.Text, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(wo.x + 12, y + 22, wo.width - 24, 22), "-> " + Pretty(s.dest), 16, ok ? Ui.Good : Ui.Dim);
                y += 56;
            }
            // Device back panel
            var dev = new Rect(p.x + 290, p.y + 110, 110, 520);
            Ui.Box(dev, def.color * 1.8f);
            Ui.Border(dev, Ui.Line);
            Ui.Label(new Rect(dev.x, dev.y + 6, dev.width, 20), def.model, 14, Ui.Dim, TextAnchor.UpperCenter, true);
            foreach (var g in groups)
            {
                Ui.Panel(g.r, new Color(0.08f, 0.1f, 0.12f, 1f));
                Ui.Label(new Rect(g.r.x + 12, g.r.y + 6, 400, 22), g.name, 15, Ui.Dim, TextAnchor.UpperLeft, true);
            }
            bool tester = career.Has("tester");
            foreach (var d in dsts)
            {
                bool used = srcs.Any(s => s.connected && s.dest == d.id);
                bool highlight = tester && dragging != null && dragging.dest == d.id;
                Ui.Box(new Rect(d.p.x - 11, d.p.y - 9, 22, 18), used ? new Color(0.15f, 0.5f, 0.25f) : new Color(0.02f, 0.02f, 0.03f));
                Ui.Border(new Rect(d.p.x - 11, d.p.y - 9, 22, 18), highlight ? Ui.Good : Hover(d) ? Ui.Accent : new Color(0.35f, 0.38f, 0.42f), highlight ? 3f : 1.5f);
                Ui.Label(new Rect(d.p.x - 15, d.p.y - 30, 30, 18), d.label, 12, Ui.Dim, TextAnchor.MiddleCenter);
            }
            foreach (var s in srcs)
            {
                bool blink = s.loose && Mathf.Repeat(flash * 2.5f, 1f) < 0.5f;
                Ui.Circle(s.p, 15f, blink ? Ui.Bad : s.connected ? s.color : new Color(0.1f, 0.1f, 0.12f));
                Ui.Ring(s.p, 18f, s.loose ? Ui.Bad : Ui.Line);
                Ui.Label(new Rect(s.p.x - 140, s.p.y - 12, 118, 24), s.name, 16, s.loose ? Ui.Bad : Ui.Text, TextAnchor.MiddleRight, true);
                if (s.connected)
                {
                    var d = DstById(s.dest);
                    Ui.Cable(s.p, d.p, s.loose ? new Color(0.8f, 0.2f, 0.2f) : s.color, 5f);
                    Ui.Circle(d.p, 7f, s.color);
                }
            }
            if (dragging != null)
            {
                Ui.Cable(dragging.p, Ui.Mouse, dragging.color, 5f);
                Ui.Circle(Ui.Mouse, 8f, dragging.color);
            }
        }

        static bool Hover(Dst d) => Vector2.Distance(Ui.Mouse, d.p) < 20f;
    }
}
