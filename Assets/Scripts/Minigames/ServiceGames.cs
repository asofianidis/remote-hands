using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>Smart hands: find the right unit by counting U on the rail, then read its label to the customer.</summary>
    public class InspectGame : Minigame
    {
        const int Span = 12;
        const float UH = 40f;
        int lo;
        bool serial;
        string answer;
        List<string> options;

        static readonly Dictionary<char, char> LookAlike = new Dictionary<char, char>
        {
            { '8', 'B' }, { 'B', '8' }, { '5', 'S' }, { 'S', '5' }, { '2', 'Z' }, { 'Z', '2' }, { 'G', '6' }, { '6', 'G' },
            { 'U', 'V' }, { 'V', 'U' }, { 'M', 'N' }, { 'N', 'M' }, { 'K', 'X' }, { 'X', 'K' }, { 'E', 'F' }, { 'F', 'E' },
        };

        protected override void Begin()
        {
            var d = Dev;
            var rnd = new System.Random(task.deviceId * 13 + 5);
            serial = task.component == "serial";
            lo = Mathf.Clamp(d.u - 2 - rnd.Next(5), 1, Rack.Units - Span + 1);
            answer = serial ? d.serial : d.Host;
            var rack = shift.Rack(task.rack);
            var others = rack.devices.Where(x => x != d && x.Top >= lo && x.u < lo + Span).Select(x => serial ? x.serial : x.Host).Distinct().ToList();
            var opts = others.OrderBy(_ => rnd.Next()).Take(2).ToList();
            // One near-miss that only a careful reader catches.
            opts.Add(NearMiss(answer, rnd));
            while (opts.Count < 3) opts.Add(NearMiss(opts[opts.Count - 1], rnd));
            opts.Add(answer);
            options = opts.Distinct().OrderBy(_ => rnd.Next()).ToList();
            title = $"Smart hands: {task.rack} for {rack.owner}";
            hint = "Count the U numbers on the rail to find the right unit, then read its label. Telling the customer the wrong thing costs reputation.";
        }

        string NearMiss(string s, System.Random rnd)
        {
            if (!serial)
            {
                // Same customer prefix and role, neighbouring number.
                int dash = s.LastIndexOf('-');
                int n = int.Parse(s.Substring(dash + 1));
                int near = (n - 10 + (rnd.Next(2) == 0 ? 1 : -1) + 90) % 90 + 10;
                return s.Substring(0, dash + 1) + near.ToString("00");
            }
            var c = s.ToCharArray();
            var swappable = Enumerable.Range(0, c.Length).Where(i => LookAlike.ContainsKey(c[i])).ToList();
            if (swappable.Count > 0) { int i = swappable[rnd.Next(swappable.Count)]; c[i] = LookAlike[c[i]]; }
            else { int i = rnd.Next(c.Length - 1); (c[i], c[i + 1]) = (c[i + 1], c[i]); }
            return new string(c);
        }

        protected override void Update(float dt, Vector2 m) { }

        public override void Draw()
        {
            Frame();
            var p = P;
            var d = Dev;
            Ui.Label(new Rect(p.x + 30, p.y + 80, p.width - 60, 30),
                $"<b>{shift.Rack(task.rack).owner}:</b> \"" + (serial ? $"What's the serial number of the {d.Def.name} at U{task.u}?\"" : $"Which hostname is on the asset tag of the {d.Def.name} at U{task.u}?\""), 20, Ui.Text);
            // Close-up of the rack front: the rail shows U numbers, the units show their labels.
            var el = new Rect(p.x + 80, p.y + 130, 640, Span * UH);
            Ui.Box(el, new Color(0.02f, 0.025f, 0.03f));
            Ui.Box(new Rect(el.x - 40, el.y, 34, el.height), new Color(0.5f, 0.52f, 0.56f));
            for (int u = lo; u < lo + Span; u++)
            {
                float y = el.yMax - (u - lo + 1) * UH;
                Ui.Label(new Rect(el.x - 40, y, 34, UH), u.ToString(), 13, new Color(0.1f, 0.1f, 0.12f), TextAnchor.MiddleCenter, true);
                Ui.Box(new Rect(el.x, y, el.width, 1), new Color(0.2f, 0.22f, 0.25f));
            }
            foreach (var x in shift.Rack(task.rack).devices)
            {
                if (x.Top < lo || x.u >= lo + Span) continue;
                int bottom = Mathf.Max(x.u, lo), top = Mathf.Min(x.Top, lo + Span - 1);
                var r = new Rect(el.x + 4, el.yMax - (top - lo + 1) * UH + 2, el.width - 8, (top - bottom + 1) * UH - 4);
                Ui.Box(r, x.Def.color * 1.8f);
                Ui.Box(new Rect(r.x, r.y, 5, r.height), Catalog.CustColor(x.owner));
                Ui.Label(new Rect(r.x + 14, r.y, 160, Mathf.Min(r.height, UH)), x.Def.model, 14, Ui.Dim, TextAnchor.MiddleLeft, true);
                // Serial sticker and the customer's asset tag.
                var sn = new Rect(r.x + 180, r.y + Mathf.Min(r.height, UH) / 2 - 11, 190, 22);
                Ui.Box(sn, new Color(0.92f, 0.92f, 0.88f));
                Ui.Label(sn, "S/N " + x.serial, 13, new Color(0.1f, 0.1f, 0.1f), TextAnchor.MiddleCenter, true);
                var tag = new Rect(r.x + 390, sn.y, 150, 22);
                Ui.Box(tag, new Color(1f, 0.85f, 0.3f));
                Ui.Label(tag, x.Host, 13, new Color(0.1f, 0.1f, 0.1f), TextAnchor.MiddleCenter, true);
                Ui.Circle(new Vector2(r.xMax - 20, r.y + Mathf.Min(r.height, UH) / 2), 5f, new Color(0.3f, 0.6f, 1f));
            }
            Ui.Label(new Rect(p.x + 800, p.y + 150, 440, 26), "YOUR ANSWER", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            for (int i = 0; i < options.Count; i++)
                if (Ui.Button(new Rect(p.x + 800, p.y + 186 + i * 66, 420, 54), options[i], true, 21))
                {
                    if (options[i] == answer) Win("The customer confirmed it. Thanks!");
                    else Oops($"\"{options[i]}\" was wrong. The customer is now chasing the wrong box", 1.5f);
                }
        }
    }

    /// <summary>
    /// RAID controller CLI, after the technician pulled a healthy drive: clear the reinserted drive's
    /// stale foreign config, then add it back as a hot spare so the array rebuilds.
    /// </summary>
    public class RaidGame : Minigame
    {
        enum PD { Online, Failed, Foreign, Good, Rebuild }
        PD[] drives;
        int selected = -1, slot;
        float rebuild;
        readonly List<string> log = new List<string>();

        protected override void Begin()
        {
            var d = Dev;
            int n = Mathf.Clamp(d.Def.bays, 4, 12);
            drives = new PD[n];
            slot = Mathf.Clamp(task.slot, 0, n - 1);
            for (int i = 0; i < n; i++) drives[i] = i == slot ? PD.Foreign : i == d.failedBay ? PD.Failed : PD.Online;
            log.Add($"$ storcli /c0/eall/sall show");
            log.Add($"Controller 0: VD0 RAID6 state: {(d.failedBay >= 0 ? "Partially Degraded" : "Degraded")}");
            title = $"RAID rebuild: {d.Def.name} in {task.rack} U{task.u}";
            hint = "Select the drive you pulled (it shows a foreign config), clear that stale config, then make it a hot spare so the array rebuilds onto it.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (drives[slot] != PD.Rebuild) return;
            rebuild += dt / 4f;
            if (rebuild >= 1f) { drives[slot] = PD.Online; Win("Rebuild complete. The array is optimal again."); }
        }

        void Act(string action)
        {
            if (selected < 0) { log.Add("Select a drive first."); return; }
            var st = drives[selected];
            string where = $"/c0/e252/s{selected}";
            switch (action)
            {
                case "clear":
                    if (st == PD.Foreign) { drives[selected] = PD.Good; log.Add($"$ storcli {where} foreign delete"); log.Add("Foreign configuration cleared. Drive is Unconfigured Good."); }
                    else log.Add($"Error: {where} has no foreign configuration.");
                    break;
                case "import":
                    if (st == PD.Foreign) Oops("Importing a stale foreign config fights the live array: the controller rolled it back and flagged the VD", 1.5f);
                    else log.Add($"Error: {where} has no foreign configuration.");
                    break;
                case "spare":
                    if (st == PD.Good) { drives[selected] = PD.Rebuild; log.Add($"$ storcli {where} add hotsparedrive"); log.Add("Hot spare added. Rebuild started."); Audio.Play("beep"); }
                    else if (st == PD.Foreign) log.Add("Error: drive has a foreign configuration. Clear it first.");
                    else log.Add($"Error: {where} is not Unconfigured Good.");
                    break;
                case "offline":
                    if (st == PD.Online) { drives[selected] = PD.Failed; Oops("You took a healthy member of the degraded array offline. The customer lost the volume for a minute", 6f); }
                    else log.Add("Error: drive is not online.");
                    break;
                case "init":
                    Oops("Initializing the virtual disk erases it. The customer's data just got wiped", 8f);
                    break;
            }
            while (log.Count > 9) log.RemoveAt(0);
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            var list = new Rect(p.x + 30, p.y + 84, 640, 560);
            Ui.Box(list, new Color(0, 0, 0, 0.75f));
            Ui.Label(new Rect(list.x + 14, list.y + 10, 600, 24), "EID:Slt   State        Size       Model", 16, Ui.Good, TextAnchor.UpperLeft, true);
            for (int i = 0; i < drives.Length; i++)
            {
                var r = new Rect(list.x + 8, list.y + 40 + i * 40, list.width - 16, 36);
                if (i == selected) Ui.Box(r, new Color(0.2f, 0.4f, 0.6f, 0.5f));
                else if (Ui.Hover(r)) Ui.Box(r, new Color(1, 1, 1, 0.05f));
                string st = drives[i] switch { PD.Online => "Onln", PD.Failed => "Failed", PD.Foreign => "UGood (F)", PD.Good => "UGood", _ => $"Rbld {rebuild * 100:0}%" };
                Color c = drives[i] switch { PD.Online => new Color(0.7f, 0.85f, 0.75f), PD.Failed => Ui.Bad, PD.Rebuild => Ui.Accent, _ => Ui.Warn };
                Ui.Label(new Rect(r.x + 6, r.y, r.width, r.height), $"252:{i,-4}  {st,-12} 3.492 TB   ST4000NM0035", 17, c, TextAnchor.MiddleLeft);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { selected = i; Event.current.Use(); Audio.Play("click"); }
            }
            var act = new Rect(p.x + 700, p.y + 84, 550, 300);
            Ui.Label(new Rect(act.x, act.y, 500, 24), selected < 0 ? "SELECT A DRIVE" : $"DRIVE 252:{selected}", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            string[] ids = { "clear", "import", "spare", "offline", "init" };
            string[] names = { "Clear foreign config", "Import foreign config", "Make global hot spare", "Set drive offline", "Initialize virtual disk (erase)" };
            for (int i = 0; i < ids.Length; i++)
                if (Ui.Button(new Rect(act.x, act.y + 32 + i * 52, 520, 44), names[i], drives[slot] != PD.Rebuild, 18, i == 4 ? Ui.Bad : (Color?)null)) Act(ids[i]);
            var con = new Rect(p.x + 700, p.y + 400, 550, 244);
            Ui.Box(con, new Color(0, 0, 0, 0.6f));
            for (int i = 0; i < log.Count; i++)
                Ui.Label(new Rect(con.x + 10, con.y + 8 + i * 25, con.width - 20, 24), log[i], 14, log[i].StartsWith("Error") ? Ui.Warn : Ui.Good);
        }
    }

    /// <summary>
    /// Decommissioning at the bench: pull every drive, scan its barcode into the destruction log, then
    /// shred it. A drive shredded without a scan breaks the chain of custody.
    /// </summary>
    public class WipeGame : Minigame
    {
        enum St { InUnit, Pulled, Scanned, Shredded }
        class Drv { public string serial; public St st; public Rect home; }
        readonly List<Drv> drives = new List<Drv>();
        Rect chassis, bench, shredder;
        Drv dragging;
        Vector2 dragStart, dragPos;
        float shredAnim;

        protected override void Begin()
        {
            var def = Catalog.Dev(task.def);
            int n = Mathf.Clamp(def.bays, 1, 8);
            var rnd = new System.Random(task.tag.GetHashCode());
            var p = P;
            chassis = new Rect(p.x + 40, p.y + 100, 760, 130);
            bench = new Rect(p.x + 40, p.y + 290, 760, 240);
            shredder = new Rect(p.x + 860, p.y + 290, 380, 240);
            for (int i = 0; i < n; i++)
            {
                string s = "WD" + rnd.Next(100000, 999999) + (char)('A' + rnd.Next(26));
                float w = (chassis.width - 40) / n;
                drives.Add(new Drv { serial = s, home = new Rect(chassis.x + 20 + i * w + 4, chassis.y + 30, w - 8, 80) });
            }
            title = $"Wipe station: {def.name} ({task.tag})";
            hint = "Click each drive sled to pull it onto the bench. Click a pulled drive to scan its barcode into the log, then drag it into the shredder.";
        }

        Rect BenchSpot(int i)
        {
            int cols = 4;
            float w = (bench.width - 40) / cols;
            return new Rect(bench.x + 20 + (i % cols) * w + 6, bench.y + 40 + (i / cols) * 96, w - 12, 80);
        }

        Rect Where(Drv d) => d.st == St.InUnit ? d.home : BenchSpot(drives.IndexOf(d));

        protected override void Update(float dt, Vector2 m)
        {
            shredAnim = Mathf.Max(0, shredAnim - dt);
            if (Pressed)
            {
                foreach (var d in drives)
                {
                    if (d.st == St.Shredded || !Where(d).Contains(m)) continue;
                    if (d.st == St.InUnit) { d.st = St.Pulled; Audio.Play("clunk"); }
                    else { dragging = d; dragStart = m; }
                    break;
                }
            }
            if (dragging != null) dragPos = m;
            if (Released && dragging != null)
            {
                var d = dragging;
                dragging = null;
                if (shredder.Contains(m))
                {
                    if (d.st != St.Scanned) Oops($"Drive {d.serial} went into the shredder without being scanned: the chain of custody is broken", 1.5f);
                    d.st = St.Shredded;
                    shredAnim = 0.6f;
                    Audio.Play("screw");
                }
                else if (Vector2.Distance(m, dragStart) < 10f && d.st == St.Pulled) { d.st = St.Scanned; Audio.Play("beep"); }
            }
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            Ui.Box(chassis, Catalog.Dev(task.def).color * 1.6f);
            Ui.Label(new Rect(chassis.x + 10, chassis.y + 4, 400, 20), $"{Catalog.Dev(task.def).model}  ({task.tag})", 14, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Panel(bench, new Color(0.25f, 0.2f, 0.15f, 0.9f));
            Ui.Label(new Rect(bench.x + 12, bench.y + 8, 300, 22), "BENCH", 15, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Panel(shredder, shredAnim > 0 ? new Color(0.5f, 0.15f, 0.1f, 0.95f) : new Color(0.25f, 0.08f, 0.06f, 0.95f));
            Ui.Label(shredder, "DRIVE SHREDDER\n\ndrop drives here", 22, Ui.Text, TextAnchor.MiddleCenter, true);
            foreach (var d in drives)
            {
                if (d.st == St.InUnit) { Ui.Box(d.home, new Color(0.15f, 0.16f, 0.18f)); Ui.Border(d.home, Ui.Hover(d.home) ? Ui.Accent : Ui.Line, 2f); Ui.Circle(new Vector2(d.home.xMax - 10, d.home.y + 12), 5f, Ui.Good); continue; }
                if (d.st == St.Shredded) { Ui.Box(d.home, new Color(0.01f, 0.01f, 0.01f)); continue; }
                Ui.Box(d.home, new Color(0.01f, 0.01f, 0.01f));
                var r = d == dragging ? new Rect(dragPos.x - 60, dragPos.y - 40, 120, 80) : Where(d);
                Ui.Box(r, new Color(0.55f, 0.57f, 0.6f));
                Ui.Border(r, d.st == St.Scanned ? Ui.Good : Ui.Warn, 2f);
                Ui.Label(new Rect(r.x, r.y + 6, r.width, 20), d.serial, 13, new Color(0.1f, 0.1f, 0.12f), TextAnchor.UpperCenter, true);
                Ui.Label(new Rect(r.x, r.yMax - 26, r.width, 20), d.st == St.Scanned ? "SCANNED" : "click to scan", 12, d.st == St.Scanned ? new Color(0.05f, 0.4f, 0.1f) : new Color(0.5f, 0.3f, 0f), TextAnchor.UpperCenter, true);
            }
            // Destruction log.
            var logR = new Rect(p.x + 860, p.y + 84, 380, 190);
            Ui.Panel(logR, Ui.Bg2);
            Ui.Label(new Rect(logR.x + 12, logR.y + 8, 300, 22), "DESTRUCTION LOG", 15, Ui.Accent, TextAnchor.UpperLeft, true);
            int k = 0;
            foreach (var d in drives.Where(x => x.st == St.Scanned || x.st == St.Shredded))
                Ui.Label(new Rect(logR.x + 12, logR.y + 34 + k++ * 18, 360, 18), $"{d.serial}  {(d.st == St.Shredded ? "DESTROYED" : "logged")}", 13, d.st == St.Shredded ? Ui.Good : Ui.Text);
            bool all = drives.All(d => d.st == St.Shredded);
            if (all && Ui.Button(new Rect(p.x + 40, p.y + 560, 420, 56), "Print certificate of destruction", true, 20, Ui.Good)) Win("Certificate printed. The unit is ready to ship.");
        }
    }
}
