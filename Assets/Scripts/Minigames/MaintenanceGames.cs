using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// A switch booted with an empty config. On its console: pick the right backup off the TFTP server,
    /// load it, check the ports came back, and save it so it survives the next reboot.
    /// </summary>
    public class ConfigGame : Minigame
    {
        public override bool OnScreen => true;
        class Backup { public string file, when, size; public bool good; public string why; }
        List<Backup> backups;
        Backup loaded;
        bool saved, verified;
        float winIn = -1f;
        readonly List<string> log = new List<string>();

        protected override void Begin()
        {
            var d = Dev;
            var rnd = new System.Random(task.deviceId * 11 + 7);
            int day = career.day;
            string host = d.Host;
            var other = shift.racks.SelectMany(r => r.devices).FirstOrDefault(x => x.Def.kind == DevKind.Switch && x.Host != host)?.Host ?? "qr-tor-17";
            backups = new List<Backup>
            {
                new Backup { file = $"{host}_d{day - 1:000}.cfg", when = $"Day {day - 1}, 23:00 (nightly)", size = "48.2 KB", good = true },
                day > 8
                    ? new Backup { file = $"{host}_d{day - 8:000}.cfg", when = $"Day {day - 8}, 23:00 (nightly)", size = "47.9 KB", why = "that's a week old: it's missing the last week of changes" }
                    : new Backup { file = $"{host}_build.cfg", when = "Initial build (manual)", size = "31.4 KB", why = "that's the config it was built with: every change since is missing" },
                new Backup { file = $"{host}_d{day:000}.cfg", when = $"Day {day}, 06:12 (after the crash)", size = "2.1 KB", why = "that one was taken after the switch lost its config: it's the empty default" },
                new Backup { file = $"{other}_d{day - 1:000}.cfg", when = $"Day {day - 1}, 23:00 (nightly)", size = "51.0 KB", why = $"that's {other}'s config, a different switch" },
                new Backup { file = $"{host}_d{day - 1:000}.cfg.labtest", when = $"Day {day - 1}, 15:40 (manual)", size = "48.6 KB", why = "that's a lab test copy, not the production config" },
            }.OrderBy(_ => rnd.Next()).ToList();
            log.Add($"switch# show running-config | count");
            log.Add("  14 lines (factory default)");
            title = $"Restore config: {d.Def.name} {host} in {task.rack} U{task.u}";
            hint = $"Load the newest good backup of {host} taken BEFORE it lost its config, check the ports, then save it to startup-config so it survives a reboot.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (winIn < 0) return;
            winIn -= dt;
            if (winIn <= 0) Win(verified ? "Config restored and saved. Every server in the rack is back online." : "Config restored and saved. The rack is back online (you could have checked the ports first).");
        }

        void Run(string cmd)
        {
            switch (cmd)
            {
                case "show":
                    log.Add("switch# show interface status");
                    if (loaded != null) { log.Add("  Eth1/1-48  connected   vlan 110,120"); log.Add("  Eth1/49-50 connected   trunk (uplinks)"); verified = true; }
                    else log.Add("  Eth1/1-50  disabled    (admin down)");
                    break;
                case "save":
                    log.Add("switch# copy running-config startup-config");
                    if (loaded == null) { Oops("You saved the empty default config over startup-config. Now it's gone for good, until you load a backup", 1.5f); log.Add("  [########################] 100%  Copy complete."); }
                    else { log.Add("  [########################] 100%  Copy complete."); saved = true; winIn = 1.2f; Audio.Play("beep"); }
                    break;
                case "reload":
                    log.Add("switch# reload");
                    if (loaded != null && !saved) { Oops("You reloaded before saving: the restored config is gone again and the rack dropped a second time", 2f); loaded = null; verified = false; log.Add("  ...rebooting... startup-config is empty, using defaults"); }
                    else { Oops("A pointless reboot: every server in the rack dropped again for two minutes", 1f); log.Add("  ...rebooting..."); }
                    break;
                case "erase":
                    log.Add("switch# write erase");
                    Oops("write erase wipes startup-config. That's the opposite of what you want", 2f);
                    break;
            }
            while (log.Count > 11) log.RemoveAt(0);
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            var list = new Rect(p.x + 30, p.y + 84, 700, 330);
            Ui.Box(list, new Color(0, 0, 0, 0.75f));
            Ui.Label(new Rect(list.x + 12, list.y + 8, 680, 22), "tftp://10.0.0.5/backups/switches/", 16, Ui.Good, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(list.x + 18, list.y + 34, 200, 20), "FILE", 13, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(list.x + 358, list.y + 34, 200, 20), "TAKEN", 13, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(list.xMax - 108, list.y + 34, 90, 20), "SIZE", 13, Ui.Dim, TextAnchor.UpperRight, true);
            for (int i = 0; i < backups.Count; i++)
            {
                var b = backups[i];
                var r = new Rect(list.x + 8, list.y + 58 + i * 52, list.width - 16, 46);
                bool on = loaded == b;
                if (Ui.Button(r, "", saved == false, 15, on ? Ui.Good : (Color?)null))
                {
                    if (b.good)
                    {
                        loaded = b;
                        log.Add($"switch# copy tftp://10.0.0.5/backups/switches/{b.file} running-config");
                        log.Add("  Trying to connect to tftp server...... Copy complete, now saving to disk.");
                        log.Add("  Running config loaded (612 lines).");
                        Audio.Play("beep");
                    }
                    else Oops($"Wrong backup: {b.why}", 1f);
                }
                Ui.Label(new Rect(r.x + 10, r.y, 330, r.height), b.file, 15, on ? Ui.Good : Ui.Text, TextAnchor.MiddleLeft, true, false);
                Ui.Label(new Rect(r.x + 350, r.y, 240, r.height), b.when, 14, Ui.Dim, TextAnchor.MiddleLeft, false, false);
                Ui.Label(new Rect(r.xMax - 90, r.y, 80, r.height), b.size, 14, Ui.Dim, TextAnchor.MiddleRight);
            }
            var cmds = new Rect(p.x + 760, p.y + 84, 490, 330);
            Ui.Label(new Rect(cmds.x, cmds.y, 480, 22), loaded == null ? "1. CLICK A BACKUP TO LOAD IT" : $"LOADED: {loaded.file}", 15, loaded == null ? Ui.Dim : Ui.Good, TextAnchor.UpperLeft, true);
            string[] ids = { "show", "save", "reload", "erase" };
            string[] names = { "show interface status", "copy running-config startup-config", "reload", "write erase" };
            for (int i = 0; i < ids.Length; i++)
                if (Ui.Button(new Rect(cmds.x, cmds.y + 34 + i * 58, 480, 48), names[i], !saved, 18, i >= 2 ? Ui.Bad : (Color?)null)) Run(ids[i]);
            var con = new Rect(p.x + 30, p.y + 430, p.width - 60, 210);
            Ui.Box(con, new Color(0, 0, 0, 0.7f));
            for (int i = 0; i < log.Count; i++)
                Ui.Label(new Rect(con.x + 12, con.y + 6 + i * 18, con.width - 24, 18), log[i], 13, Ui.Good);
        }
    }

    /// <summary>
    /// Preventive maintenance: read a server's BMC sensor table and flag every reading outside its
    /// limits. Some readings sit close to a limit without crossing it.
    /// </summary>
    public class HealthCheckGame : Minigame
    {
        public override bool OnScreen => true;
        class Sensor { public string name, unit; public float value, lo, hi; public bool bad, flagged, missed; }
        readonly List<Sensor> sensors = new List<Sensor>();

        protected override void Begin()
        {
            var d = Dev;
            var rnd = new System.Random(task.slot);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            void Add(string n, string u, float v, float lo, float hi) => sensors.Add(new Sensor { name = n, unit = u, value = v, lo = lo, hi = hi });
            Add("CPU0 Temp", "C", Mathf.Round(R(41, 62)), 0, 90);
            Add("CPU1 Temp", "C", Mathf.Round(R(41, 62)), 0, 90);
            Add("Inlet Temp", "C", Mathf.Round(R(21, 26.9f) * 10) / 10, 10, 27);
            Add("Exhaust Temp", "C", Mathf.Round(R(34, 48)), 0, 60);
            for (int i = 1; i <= 4; i++) Add($"Fan{i}", "RPM", Mathf.Round(R(6800, 9200) / 10) * 10, 3000, 16000);
            Add("PSU1 Vout", "V", Mathf.Round(R(11.95f, 12.1f) * 100) / 100, 11.4f, 12.6f);
            Add("PSU2 Vout", "V", Mathf.Round(R(11.95f, 12.1f) * 100) / 100, 11.4f, 12.6f);
            Add("Board 3.3V", "V", Mathf.Round(R(3.27f, 3.34f) * 100) / 100, 3.14f, 3.47f);
            Add("DIMM ECC (24h)", "errors", rnd.Next(0, 4), 0, 100);
            // Out of spec: the parts that are about to fail.
            int n = Shift.Anomalies(task.slot);
            foreach (var s in sensors.OrderBy(_ => rnd.Next()).Take(n))
            {
                s.bad = true;
                s.value = s.name switch
                {
                    var x when x.StartsWith("Fan") => Mathf.Round(R(1200, 2800) / 10) * 10,
                    var x when x.StartsWith("PSU") => Mathf.Round(R(11.0f, 11.35f) * 100) / 100,
                    "Board 3.3V" => Mathf.Round(R(3.05f, 3.12f) * 100) / 100,
                    "DIMM ECC (24h)" => rnd.Next(180, 900),
                    "Inlet Temp" => Mathf.Round(R(28, 31) * 10) / 10,
                    "Exhaust Temp" => Mathf.Round(R(62, 70)),
                    _ => Mathf.Round(R(91, 97)),
                };
            }
            title = $"Health check: {d.Def.name} in {task.rack} U{task.u}";
            hint = "Click every reading outside its lower/upper limit to flag it, then submit the report. Close to a limit is fine; over it isn't.";
        }

        protected override void Update(float dt, Vector2 m) { }

        void Submit()
        {
            bool clean = true;
            foreach (var s in sensors)
            {
                if (s.flagged && !s.bad) { Oops($"{s.name} at {s.value:0.##} {s.unit} is within its limits ({s.lo:0.##} to {s.hi:0.##})", 0.5f); s.flagged = false; clean = false; }
                else if (!s.flagged && s.bad) { Oops($"You missed {s.name}: {s.value:0.##} {s.unit} is outside {s.lo:0.##} to {s.hi:0.##}", 1f); s.missed = true; s.flagged = true; clean = false; }
            }
            if (clean) Win($"Report filed: {sensors.Count(s => s.bad)} part{(sensors.Count(s => s.bad) > 1 ? "s" : "")} caught before {(sensors.Count(s => s.bad) > 1 ? "they" : "it")} failed.");
            else hint = "Corrections are marked. Check the table again and resubmit.";
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            var tbl = new Rect(p.x + 30, p.y + 84, 820, 560);
            Ui.Box(tbl, new Color(0, 0, 0, 0.75f));
            Ui.Label(new Rect(tbl.x + 14, tbl.y + 8, 790, 22), $"ipmitool -H {Dev.Host}-bmc sdr list full", 15, Ui.Good, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(tbl.x + 14, tbl.y + 36, 790, 20), "SENSOR                 READING          LOWER LIMIT      UPPER LIMIT       FLAG", 13, Ui.Dim, TextAnchor.UpperLeft, true);
            for (int i = 0; i < sensors.Count; i++)
            {
                var s = sensors[i];
                var r = new Rect(tbl.x + 8, tbl.y + 60 + i * 38, tbl.width - 16, 34);
                if (s.flagged) Ui.Box(r, s.missed ? new Color(1f, 0.5f, 0.1f, 0.3f) : new Color(1f, 0.3f, 0.25f, 0.25f));
                else if (Ui.Hover(r)) Ui.Box(r, new Color(1, 1, 1, 0.05f));
                string F(float v) => s.unit == "V" ? v.ToString("0.00") : v.ToString("0.#");
                Ui.Label(new Rect(r.x + 8, r.y, 200, r.height), s.name, 16, Ui.Text, TextAnchor.MiddleLeft);
                Ui.Label(new Rect(r.x + 190, r.y, 160, r.height), $"{F(s.value)} {s.unit}", 16, Ui.Text, TextAnchor.MiddleLeft, true);
                Ui.Label(new Rect(r.x + 360, r.y, 150, r.height), F(s.lo), 16, Ui.Dim, TextAnchor.MiddleLeft);
                Ui.Label(new Rect(r.x + 520, r.y, 150, r.height), F(s.hi), 16, Ui.Dim, TextAnchor.MiddleLeft);
                if (s.flagged) Ui.Label(new Rect(r.xMax - 120, r.y, 110, r.height), s.missed ? "MISSED" : "FLAGGED", 15, s.missed ? Ui.Warn : Ui.Bad, TextAnchor.MiddleRight, true);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition) && !s.missed)
                {
                    s.flagged = !s.flagged;
                    Event.current.Use();
                    Audio.Play("click");
                }
            }
            var side = new Rect(p.x + 880, p.y + 84, 370, 560);
            Ui.Label(new Rect(side.x, side.y, side.width, 200),
                "<b>Reading the table</b>\nA reading counts as out of spec when it's below the LOWER limit or above the UPPER one.\n\nFans spin down before they die. PSU rails sag before a supply fails. A pile of corrected memory errors means a DIMM is on its way out.",
                16, Ui.Dim);
            int flagged = sensors.Count(s => s.flagged);
            if (Ui.Button(new Rect(side.x, side.y + 400, 360, 60), $"Submit report ({flagged} flagged)", true, 20, Ui.Good)) Submit();
        }
    }
}
