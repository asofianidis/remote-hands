using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>Smart hands at the real rack: count U on the rail to find the unit, then read its label to the customer.</summary>
    public class InspectGame : Minigame
    {
        public override bool InWorld => true;
        const int Span = 12;
        int lo;
        bool serial;
        string answer;
        List<string> options;
        class Sticker { public Transform t; public string text; public bool tag; }
        readonly List<Sticker> stickers = new List<Sticker>();
        readonly List<(Vector3 w, int u)> rail = new List<(Vector3, int)>();

        const float Front = -RackView.Depth / 2f + 0.05f;

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
            var inView = rack.devices.Where(x => x.Top >= lo && x.u < lo + Span).ToList();
            var others = inView.Where(x => x != d).Select(x => serial ? x.serial : x.Host).Distinct().ToList();
            var opts = others.OrderBy(_ => rnd.Next()).Take(2).ToList();
            // One near-miss that only a careful reader catches.
            opts.Add(NearMiss(answer, rnd));
            while (opts.Count < 3) opts.Add(NearMiss(opts[opts.Count - 1], rnd));
            opts.Add(answer);
            options = opts.Distinct().OrderBy(_ => rnd.Next()).ToList();

            // Serial stickers and the customer's asset tags on the real units, U numbers on the rail.
            var rv = RackV;
            MakeRig(rv.transform);
            var white = Mats.Unlit(new Color(0.92f, 0.92f, 0.88f));
            var yellow = Mats.Unlit(new Color(1f, 0.85f, 0.3f));
            foreach (var x in inView)
            {
                float h = x.Def.heightU * RackView.UnitH;
                float y = RackView.UY(x.u) + Mathf.Min(h, RackView.UnitH) / 2f;
                float sh = Mathf.Min(h * 0.6f, 0.017f);
                stickers.Add(new Sticker { t = Box(rig, new Vector3(-0.07f, y, Front - 0.013f), new Vector3(0.12f, sh, 0.002f), white, "serial").transform, text = "S/N " + x.serial });
                stickers.Add(new Sticker { t = Box(rig, new Vector3(0.1f, y, Front - 0.013f), new Vector3(0.09f, sh, 0.002f), yellow, "tag").transform, text = x.Host, tag = true });
            }
            for (int u = lo; u < lo + Span; u++)
                rail.Add((rv.transform.TransformPoint(new Vector3(-0.282f, RackView.UY(u) + RackView.UnitH / 2f, Front - 0.012f)), u));
            float cy = RackView.UY(lo) + Span * RackView.UnitH / 2f;
            float dist = FitDistance(0.75f, Span * RackView.UnitH + 0.08f, 0.85f);
            // The answers sit on the right of the screen, so frame the rack left of centre.
            Look(new Vector3(0.22f, cy, Front - dist), new Vector3(0.22f, cy, Front));
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
            WorldHud();
            foreach (var s in stickers)
            {
                var r = UiRect(s.t);
                int size = Mathf.Clamp((int)(r.height * 0.75f), 8, 16);
                Ui.Label(r, s.text, size, new Color(0.08f, 0.08f, 0.1f), TextAnchor.MiddleCenter, true, false);
            }
            foreach (var (w, u) in rail) Tag(w, u.ToString(), 13, new Color(0.85f, 0.88f, 0.92f), 0, 40, TextAnchor.MiddleRight);
            var d = Dev;
            var panel = new Rect(Ui.W - 520, 110, 500, 140 + options.Count * 66);
            Ui.Box(panel, new Color(0.03f, 0.05f, 0.07f, 0.97f));
            Ui.Label(new Rect(panel.x + 20, panel.y + 16, panel.width - 40, 100),
                $"<b>{shift.Rack(task.rack).owner} (on the phone):</b>\n\"" + (serial ? $"What's the serial number of the {d.Def.name} at U{task.u}?\"" : $"Which hostname is on the asset tag of the {d.Def.name} at U{task.u}?\""), 19, Ui.Text);
            for (int i = 0; i < options.Count; i++)
                if (Ui.Button(new Rect(panel.x + 20, panel.y + 124 + i * 66, panel.width - 40, 54), options[i], true, 21))
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
        public override bool OnScreen => true;
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
    /// Decommissioning at the real bench: pull every drive onto the bench, scan its barcode into the
    /// destruction log, then drop it in the shredder. A drive shredded without a scan breaks the chain of custody.
    /// </summary>
    public class WipeGame : Minigame
    {
        public override bool InWorld => true;
        enum St { InUnit, Pulled, Scanned, Shredded }
        class Drv { public string serial; public St st; public Transform t; public Vector3 slot, spot, pos; }
        readonly List<Drv> drives = new List<Drv>();
        Drv dragging;
        Vector2 dragStart;
        Plane bench;
        float shredAnim;
        Transform shredder;

        // World positions: the bench top is at y 0.88, the shredder sits on its right end.
        const float Top = 0.88f;
        static readonly Vector3 Shredder = new Vector3(15.65f, 1.1f, 4.15f);

        protected override void Begin()
        {
            var def = Catalog.Dev(task.def);
            int n = Mathf.Clamp(def.bays, 1, 12);
            var rnd = new System.Random(task.tag.GetHashCode());
            MakeRig(game.facility.transform);
            float h = Mathf.Max(0.045f, def.heightU * RackView.UnitH);
            // The unit lies on the bench with its drive bays facing you.
            // (Turned to face the hall, and shortened so it fits on the bench; its drives are the rig's own.)
            var shell = Models.Spawn(Models.For(def), rig, new Vector3(14.55f, Top, 4.2f), Quaternion.Euler(0, 180, 0), Models.Unit(def), scale: new Vector3(1f, 1f, 0.55f));
            Models.ShowPart(shell, "Bays", false);
            shredder = Box(rig, Shredder + new Vector3(0, 0.235f, 0), new Vector3(0.5f, 0.02f, 0.12f), Mats.Unlit(new Color(0.02f, 0.02f, 0.02f)), "shredder slot").transform;
            var sled = Mats.Glow(new Color(0.7f, 0.72f, 0.76f), 0.9f);
            var pal = new Models.Palette().Set("sled", sled).Set("trim", sled);
            for (int i = 0; i < n; i++)
            {
                string s = "WD" + rnd.Next(100000, 999999) + (char)('A' + rnd.Next(26));
                // The bay grid, mirrored because the unit is turned round.
                Catalog.Bay(def, i, out var c, out var sz);
                var slot = new Vector3(14.55f - c.x, Top + c.y, 4.2f - 0.047f);
                var spot = new Vector3(14.08f + i * (n > 8 ? 0.088f : 0.105f), Top + 0.012f, 4.38f);
                var t = new GameObject("drive").transform;
                t.SetParent(rig, false);
                t.localPosition = slot;
                t.localRotation = Quaternion.Euler(0, 180, 0);
                Models.Spawn(Models.Sled(def), t, Vector3.zero, palette: pal, shadows: false);
                Box(t, new Vector3(sz.x * 0.35f, sz.y * 0.28f, -0.052f), new Vector3(0.004f, 0.004f, 0.002f), Mats.Unlit(new Color(0.3f, 1f, 0.4f)), "led");
                drives.Add(new Drv { serial = s, t = t, slot = slot, spot = spot, pos = slot });
            }
            bench = new Plane(Vector3.up, new Vector3(0, Top + 0.02f, 0));
            // From the hall side of the bench, looking down at the unit, the bench and the shredder.
            var target = new Vector3(14.95f, Top + 0.08f, 4.2f);
            var dir = new Vector3(0f, 0.62f, 0.78f).normalized;
            Look(target + dir * FitDistance(2.1f, 0.9f, 0.9f), target);
            title = $"Wipe station: {def.name} ({task.tag})";
            hint = "Click each drive to pull it onto the bench. Click a pulled drive to scan its barcode into the log, then drag it into the shredder.";
        }

        Rect RectOf(Drv d) { var r = UiRect(d.t); return new Rect(r.x - 6, r.y - 6, r.width + 12, r.height + 12); }

        protected override void Update(float dt, Vector2 m)
        {
            shredAnim = Mathf.Max(0, shredAnim - dt);
            if (Pressed)
            {
                foreach (var d in drives)
                {
                    if (d.st == St.Shredded || !RectOf(d).Contains(m)) continue;
                    if (d.st == St.InUnit) { d.st = St.Pulled; Audio.Play("clunk"); }
                    else { dragging = d; dragStart = m; }
                    break;
                }
            }
            if (dragging != null && Vector2.Distance(m, dragStart) > 8f) dragging.pos = MouseOn(bench);
            if (Released && dragging != null)
            {
                var d = dragging;
                dragging = null;
                var slotR = UiRect(shredder);
                slotR = new Rect(slotR.x - 30, slotR.y - 40, slotR.width + 60, slotR.height + 80);
                if (slotR.Contains(m))
                {
                    if (d.st != St.Scanned) Oops($"Drive {d.serial} went into the shredder without being scanned: the chain of custody is broken", 1.5f);
                    d.st = St.Shredded;
                    shredAnim = 0.6f;
                    Audio.Play("screw");
                }
                else if (Vector2.Distance(m, dragStart) < 8f && d.st == St.Pulled) { d.st = St.Scanned; Audio.Play("beep"); }
            }
            foreach (var d in drives)
            {
                d.t.gameObject.SetActive(d.st != St.Shredded);
                if (d == dragging) { d.t.position = Vector3.Lerp(d.t.position, d.pos + Vector3.up * 0.04f, dt * 20f); continue; }
                d.pos = d.st == St.InUnit ? d.slot : d.spot;
                d.t.position = Vector3.Lerp(d.t.position, d.pos, dt * 14f);
                d.t.rotation = Quaternion.Slerp(d.t.rotation, d.st == St.InUnit ? Quaternion.Euler(0, 180, 0) : Quaternion.Euler(-90, 180, 0), dt * 14f);
            }
            if (shredAnim > 0) shredder.localPosition = Shredder + new Vector3(Mathf.Sin(time * 90f) * 0.004f, 0.235f, 0);
        }

        public override void Draw()
        {
            WorldHud();
            var logR = new Rect(Ui.W - 420, 110, 400, 60 + drives.Count * 22);
            Ui.Box(logR, new Color(0.03f, 0.05f, 0.07f, 0.9f));
            Ui.Label(new Rect(logR.x + 14, logR.y + 10, 360, 22), "DESTRUCTION LOG", 15, Ui.Accent, TextAnchor.UpperLeft, true);
            int k = 0;
            foreach (var d in drives)
            {
                string st = d.st == St.Shredded ? "DESTROYED" : d.st == St.Scanned ? "logged" : d.st == St.Pulled ? "not scanned" : "in unit";
                Ui.Label(new Rect(logR.x + 14, logR.y + 40 + k++ * 22, 370, 20), $"{d.serial}   {st}", 14, d.st == St.Shredded ? Ui.Good : d.st == St.Scanned ? Ui.Text : Ui.Dim);
            }
            Tag(Shredder + new Vector3(0, 0.36f, 0), "SHREDDER", 16, shredAnim > 0 ? Ui.Bad : Ui.Text);
            var m = Ui.Mouse;
            foreach (var d in drives)
            {
                if (d.st == St.Shredded) continue;
                var r = RectOf(d);
                if (r.Contains(m) && dragging == null) Ui.Border(r, Ui.Accent, 2f);
                if (d.st != St.InUnit) Chip(d.t.position, d.st == St.Scanned ? "SCANNED" : "click to scan", 11, d.st == St.Scanned ? Ui.Good : Ui.Warn, -22);
            }
            if (drives.All(d => d.st == St.Shredded) && Ui.Button(new Rect(Ui.W / 2 - 220, Ui.H - 200, 440, 60), "Print certificate of destruction", true, 20, Ui.Good))
                Win("Certificate printed. The unit is ready to ship.");
        }
    }
}
