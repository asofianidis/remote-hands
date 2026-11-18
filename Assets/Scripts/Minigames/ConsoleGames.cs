using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>Pick the right firmware image, then keep the flash stable until it finishes.</summary>
    public class FirmwareGame : Minigame
    {
        public override bool OnScreen => true;
        List<string> images;
        string correct, current;
        int phase;
        float progress, instability, signal, control;
        readonly List<string> log = new List<string>();
        float nextLog;

        protected override void Begin()
        {
            var d = Dev;
            string model = d.Def.model, comp = task.component, ver = task.version;
            current = d.Firmware(comp);
            correct = $"{model}_{comp}_{ver}.bin";
            var rnd = new System.Random(task.deviceId * 17 + time.GetHashCode());
            string other = Catalog.Devices.Where(x => x.kind == DevKind.Server && x.model != model).Select(x => x.model).FirstOrDefault() ?? "RX-999";
            string otherComp = Catalog.Components.First(c => c != comp);
            var vs = Shift.FirmwareVersions[comp];
            string prev = vs[Mathf.Max(0, vs.Length - 2)];
            var all = new List<string>
            {
                correct,
                $"{model}_{comp}_{current}.bin",
                $"{model}_{comp}_{ver}-rc2.bin",
                $"{model}_{otherComp}_{Shift.Latest(otherComp)}.bin",
                $"{other}_{comp}_{ver}.bin",
                $"{other}_{comp}_{prev}.bin",
                $"{model.Replace("-", "")}_{comp}_{ver}_signed.bin",
            };
            if (career.Has("usb")) all = all.Where(x => x.StartsWith(model + "_")).ToList();
            images = all.Distinct().OrderBy(_ => rnd.Next()).ToList();
            title = $"Firmware: {comp} {ver} on {d.Def.name} ({task.rack} U{task.u})";
            hint = "Select the image that matches this device's MODEL, the COMPONENT and the TARGET version exactly.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (phase != 1) return;
            var g = Gauge();
            control = Mathf.Lerp(control, Mathf.Clamp((m.x - g.center.x) / (g.width / 2f), -1.5f, 1.5f), dt * 12f);
            float drift = (Mathf.PerlinNoise(time * 0.32f, 1.7f) - 0.5f) * 3.2f + (Mathf.PerlinNoise(time * 1.9f, 8.3f) - 0.5f) * 0.7f;
            signal = drift + control;
            bool ok = Mathf.Abs(signal) < 0.24f;
            if (ok) { progress += dt / 13f; instability = Mathf.Max(0, instability - dt * 0.6f); }
            else instability += dt / 1.5f;
            if (instability >= 1f)
            {
                progress = 0;
                instability = 0;
                log.Add("!! write verify failed, rolling back to backup image");
                Oops("Voltage glitch: the flash failed and rolled back. Starting over", 1);
            }
            nextLog -= dt;
            if (nextLog <= 0 && ok)
            {
                nextLog = 0.6f;
                log.Add($"writing block 0x{(int)(progress * 65535):X4} ... ok");
                if (log.Count > 9) log.RemoveAt(0);
            }
            if (progress >= 1f) Win($"{task.component} updated to {task.version}.");
        }

        Rect Gauge() { var p = P; return new Rect(p.x + 120, p.y + 330, p.width - 240, 70); }

        public override void Draw()
        {
            Frame();
            var p = P;
            var d = Dev;
            var info = new Rect(p.x + 40, p.y + 90, p.width - 80, 70);
            Ui.Panel(info, Ui.Bg2);
            Ui.Label(new Rect(info.x + 20, info.y + 10, info.width - 40, 50),
                $"MODEL  <b>{d.Def.model}</b>        COMPONENT  <b>{task.component}</b>        INSTALLED  {current}        TARGET  <b><color=#7fe08a>{task.version}</color></b>", 20, Ui.Text, TextAnchor.MiddleLeft);
            if (phase == 0)
            {
                Ui.Label(new Rect(p.x + 40, p.y + 180, 600, 28), "$ ls /mnt/usb/firmware/", 18, Ui.Good);
                for (int i = 0; i < images.Count; i++)
                {
                    var r = new Rect(p.x + 40, p.y + 214 + i * 58, 640, 48);
                    if (Ui.Button(r, images[i], true, 19))
                    {
                        string img = images[i];
                        if (img == correct)
                        {
                            phase = 1;
                            hint = "Flashing! Keep the voltage needle inside the green band by moving the mouse against the drift. Leave it too long and the flash fails.";
                            log.Add($"$ fwupdate --component {task.component.ToLowerInvariant()} {img}");
                            log.Add("image signature ok, model match ok");
                        }
                        else Oops($"Image rejected: {Why(img)}", 0.75f);
                    }
                }
                Ui.Label(new Rect(p.x + 720, p.y + 214, 500, 300),
                    "Tips\n- The model is printed on the device and shown above.\n- Release candidates (-rc) are not approved for production.\n- Make sure the component matches too: BIOS, BMC and NIC images are different.", 18, Ui.Dim);
            }
            else
            {
                Ui.Label(new Rect(p.x + 120, p.y + 180, 400, 26), "FLASH PROGRESS", 16, Ui.Dim, TextAnchor.UpperLeft, true);
                Ui.Bar(new Rect(p.x + 120, p.y + 208, p.width - 240, 30), progress, Ui.Accent);
                Ui.Label(new Rect(p.x + 120, p.y + 208, p.width - 240, 30), $"{progress * 100:0}%", 18, Color.white, TextAnchor.MiddleCenter, true);
                Ui.Label(new Rect(p.x + 120, p.y + 300, 400, 26), "SUPPLY VOLTAGE", 16, Ui.Dim, TextAnchor.UpperLeft, true);
                var g = Gauge();
                Ui.Box(g, new Color(0.04f, 0.05f, 0.06f));
                float bandW = g.width * 0.24f / 1.5f;
                Ui.Box(new Rect(g.center.x - bandW, g.y, bandW * 2, g.height), new Color(0.2f, 0.8f, 0.35f, 0.35f));
                float nx = g.center.x + Mathf.Clamp(signal / 1.5f, -1f, 1f) * g.width / 2f;
                Ui.Box(new Rect(nx - 3, g.y - 8, 6, g.height + 16), Mathf.Abs(signal) < 0.24f ? Ui.Good : Ui.Bad);
                Ui.Label(new Rect(p.x + 120, p.y + 410, 400, 26), "INSTABILITY", 16, Ui.Dim, TextAnchor.UpperLeft, true);
                Ui.Bar(new Rect(p.x + 260, p.y + 412, 300, 18), instability, Ui.Bad);
                var con = new Rect(p.x + 120, p.y + 450, p.width - 240, 200);
                Ui.Box(con, new Color(0, 0, 0, 0.6f));
                for (int i = 0; i < log.Count; i++)
                    Ui.Label(new Rect(con.x + 12, con.y + 8 + i * 21, con.width - 24, 21), log[i], 15, log[i].StartsWith("!!") ? Ui.Bad : Ui.Good);
            }
        }

        string Why(string img)
        {
            var parts = img.Replace(".bin", "").Split('_');
            if (parts[0] != Dev.Def.model) return $"built for model {parts[0]}, this is a {Dev.Def.model}";
            if (parts.Length > 1 && parts[1] != task.component) return $"that's a {parts[1]} image, the ticket asks for {task.component}";
            if (img.Contains("-rc")) return "release candidates are not approved for production";
            return parts.Length > 2 && parts[2] == current ? "that version is already installed" : "image is not the approved target build";
        }
    }

    /// <summary>Read the console and BMC to work out why a server dropped off the network.</summary>
    public class DiagnoseGame : Minigame
    {
        public override bool OnScreen => true;
        class LogLine { public string text; public bool key; }
        readonly List<LogLine> lines = new List<LogLine>();
        List<(string label, Fault fault)> options;
        Fault fault;

        static readonly string[] Noise =
        {
            "systemd[1]: Started Daily apt download activities.",
            "CRON[2231]: (root) CMD (/usr/local/bin/backup-rotate)",
            "kernel: EXT4-fs (md0): mounted filesystem with ordered data mode",
            "sshd[1802]: Accepted publickey for deploy from 10.20.4.11",
            "kernel: mlx5_core 0000:5e:00.0: firmware version 16.35.2000",
            "chronyd[612]: Selected source 10.0.0.1 (ntp.dc.local)",
            "smartd[740]: Device /dev/sda, SMART Usage Attribute 194 Temperature 34",
            "kernel: perf: interrupt took too long (2507 > 2500), lowering rate",
            "dockerd[1190]: container 7f3a health check passed",
            "systemd-logind[701]: New session 412 of user deploy.",
            "kernel: nvme nvme0: 32/0/0 default/read/poll queues",
            "node_exporter[988]: msg=\"Listening on\" address=:9100",
            // Scary-looking but harmless: real logs are full of these.
            "ACPI Error: AE_NOT_FOUND, While resolving a named reference package element (20230628/dspkginit-438)",
            "kernel: ipmi_si dmi-ipmi-si.0: The BMC does not support clearing the recv irq bit, compensating",
            "kernel: pcieport 0000:00:1c.0: AER: Corrected error received: 0000:00:1c.0",
            "systemd[1]: Failed to start Load Kernel Module drm.",
        };

        static readonly string[] SwitchNoise =
        {
            "%NTP-5-SYSTEM_CLOCK_SET: System clock set by NTP server 10.0.0.1",
            "%AUTHPRIV-6-SYSTEM_MSG: pam_unix(dcos_sshd:session): session opened for user admin",
            "%PLATFORM-5-FAN_STATUS: Fan module 1 (sys_fan1) speed 41%",
            "%USER-2-SYSTEM_MSG: CLIS: loading cmd files begin - clis",
            "%ACLLOG-5-ACLLOG_FLOW_INTERVAL: Src IP: 10.20.0.4, Dst IP: 10.20.0.1, Hit-count: 12",
            "%LLDP-5-SERVER_ADDED: Server with Chassis ID 7c1e.52aa.0c10 Port ID eth0 discovered on local port Ethernet1/7",
            "%DAEMON-6-SYSTEM_MSG: Accepted publickey for netops from 10.0.4.11",
            // Harmless noise that looks bad.
            "%SYSMGR-3-BASIC_TRACE: core_copy: PID 4021 with message Core not generated by system for clis(0).",
            "%ETHPORT-5-IF_DOWN_ADMIN_DOWN: Interface Ethernet1/47 is down (Administratively down)",
            "%PLATFORM-2-MOD_PWRDN: Module 2 powered down (not inserted)",
        };

        bool isSwitch;

        protected override void Begin()
        {
            var d = Dev;
            fault = d.fault;
            isSwitch = d.Def.kind == DevKind.Switch;
            var rnd = new System.Random(task.deviceId * 7 + 3);
            if (isSwitch) { BeginSwitch(d, rnd); return; }
            string[] keys = fault switch
            {
                Fault.LinkDown => new[] { "ixgbe 0000:3b:00.0 eth0: NIC Link is Down", "bond0: link status definitely down for interface eth0, disabling it", "lldpd[877]: removal request for neighbor on eth0 (no carrier)" },
                Fault.BadSfp => new[] { "ixgbe 0000:3b:00.1 eth1: SFP+ module TX fault detected", "ixgbe eth1: failed to read SFP+ module EEPROM", "bond0: link status definitely down for interface eth1, disabling it" },
                Fault.BadDimm => new[] { $"EDAC MC0: 1 UE memory read error on CPU_SrcID#0_Ha#0_Chan#{d.badDimm % 8 / 2}_DIMM#{d.badDimm % 2} (page:0x3f2a1)", "mce: [Hardware Error]: Machine check events logged", "Kernel panic - not syncing: Fatal machine check" },
                Fault.FanFail => new[] { "CPU12: Core temperature above threshold, cpu clock throttled (total events = 4412)", "mce: CPU0: Package temperature above threshold, cpu clock throttled", "thermal thermal_zone2: critical temperature reached (99 C), shutting down" },
                _ => new[] { "watchdog: BUG: soft lockup - CPU#12 stuck for 23s! [kworker/12:1]", "rcu: INFO: rcu_sched self-detected stall on CPU", "INFO: task jbd2/md0-8:412 blocked for more than 120 seconds." },
            };
            var all = new List<LogLine>();
            for (int i = 0; i < 11; i++) all.Add(new LogLine { text = Noise[rnd.Next(Noise.Length)] });
            foreach (var k in keys) all.Insert(3 + rnd.Next(all.Count - 3), new LogLine { text = k, key = true });
            float t = 81000f + rnd.Next(4000);
            foreach (var l in all) { t += (float)rnd.NextDouble() * 40f; lines.Add(new LogLine { text = $"[{t,9:0.000}] {l.text}", key = l.key }); }
            options = new List<(string, Fault)>
            {
                ("Loose or unseated network cable", Fault.LinkDown),
                ("Failed SFP+ optic", Fault.BadSfp),
                ("Hung operating system (kernel lockup)", Fault.Hung),
                ("Failed memory module (DIMM)", Fault.BadDimm),
                ("Failed fan (CPU overheating)", Fault.FanFail),
                ("Failed RAID controller", Fault.None),
                ("Failed power supply", Fault.None),
            }.OrderBy(_ => rnd.Next()).ToList();
            title = $"Diagnose: {d.Def.name} in {task.rack} U{task.u}" + (task.cart ? " (crash cart)" : "");
            hint = task.cart
                ? "The BMC is down, so all you have is the console on the crash cart. Read the log carefully, then pick the root cause."
                : "Read the console log and the BMC health panel together, then pick the root cause. A wrong call costs reputation.";
        }

        /// <summary>A top-of-rack switch: its own log dialect, a supervisor panel instead of a BMC, and switch causes.</summary>
        void BeginSwitch(Device d, System.Random rnd)
        {
            string[] keys = fault switch
            {
                Fault.BadSfp => new[] { "%ETHPORT-5-IF_DOWN_LINK_FAILURE: Interface Ethernet1/49 is down (Link failure)", "%ETHPORT-3-IF_SFP_ERROR: Interface Ethernet1/49, transceiver TX fault", "%LACP-5-PORT_DOWN: port-channel1 member Ethernet1/49 removed, uplink lost" },
                Fault.ConfigLost => new[] { "%VSHD-2-STARTUP_CONFIG_LOADED: startup-config is empty or corrupt, booting with default configuration", "%ETHPORT-5-IF_ADMIN_DOWN: Interface Ethernet1/1-48 is admin down (Administratively down)", "%VLAN_MGR-2-CRITICAL_MSG: no VLANs configured, only default VLAN 1 present" },
                _ => new[] { "%KERN-2-SYSTEM_MSG: watchdog: BUG: soft lockup - CPU#1 stuck for 22s! [sysmgr]", "%SYSMGR-2-HEARTBEAT_FAILURE: Service \"ethpm\" heartbeat failure", "%SYSMGR-2-SERVICE_CRASHED: Service \"ethpm\" (PID 4123) hasn't caught signal 9" },
            };
            var all = new List<LogLine>();
            for (int i = 0; i < 11; i++) all.Add(new LogLine { text = SwitchNoise[rnd.Next(SwitchNoise.Length)] });
            foreach (var k in keys) all.Insert(3 + rnd.Next(all.Count - 3), new LogLine { text = k, key = true });
            int t = 3600 * 7 + rnd.Next(3000);
            foreach (var l in all) { t += rnd.Next(40); lines.Add(new LogLine { text = $"{t / 3600:00}:{t / 60 % 60:00}:{t % 60:00} {l.text}", key = l.key }); }
            options = new List<(string, Fault)>
            {
                ("Failed SFP+ uplink optic", Fault.BadSfp),
                ("Hung control plane (supervisor lockup)", Fault.Hung),
                ("Lost startup configuration", Fault.ConfigLost),
                ("Failed fan tray", Fault.None),
                ("Failed power supply", Fault.None),
                ("Spanning-tree loop from a server", Fault.None),
                ("Failed supervisor module", Fault.None),
            }.OrderBy(_ => rnd.Next()).ToList();
            title = $"Diagnose: {d.Def.name} ({d.Def.model}) in {task.rack} U{task.u}" + (task.cart ? " (crash cart)" : "");
            hint = "Every server in the rack is dark, so it's the switch. Read its log and the supervisor panel, then pick the root cause.";
        }

        protected override void Update(float dt, Vector2 m) { }

        public override void Draw()
        {
            Frame();
            var p = P;
            var con = new Rect(p.x + 30, p.y + 84, 760, 560);
            Ui.Box(con, new Color(0, 0, 0, 0.75f));
            Ui.Label(new Rect(con.x + 12, con.y + 8, 600, 22), isSwitch ? $"{Dev.Host}# show logging last 14" : $"root@{Dev.Def.model.ToLowerInvariant()}-{Dev.id}:~# journalctl -k -n 14", 15, Ui.Good);
            bool scan = career.Has("scanner");
            for (int i = 0; i < lines.Count; i++)
            {
                var r = new Rect(con.x + 12, con.y + 36 + i * 36, con.width - 24, 34);
                if (scan && lines[i].key) Ui.Box(r, new Color(1f, 0.7f, 0.1f, 0.18f));
                Ui.Label(r, lines[i].text, 15, scan && lines[i].key ? Ui.Warn : new Color(0.75f, 0.85f, 0.78f));
            }
            var bmc = new Rect(p.x + 810, p.y + 84, 440, 230);
            Ui.Panel(bmc, Ui.Bg2);
            Ui.Label(new Rect(bmc.x + 14, bmc.y + 10, 400, 24), isSwitch ? "SUPERVISOR" : "BMC HEALTH", 16, Ui.Accent, TextAnchor.UpperLeft, true);
            void Row(int i, string k, string v, Color c)
            {
                Ui.Label(new Rect(bmc.x + 14, bmc.y + 40 + i * 26, 200, 24), k, 16, Ui.Dim);
                Ui.Label(new Rect(bmc.x + 200, bmc.y + 40 + i * 26, 230, 24), v, 16, c, TextAnchor.UpperLeft, true);
            }
            if (isSwitch && task.cart)
            {
                Ui.Label(new Rect(bmc.x + 14, bmc.y + 50, 410, 150), "<b>mgmt0 unreachable</b>\n\nThe management port went down with the switch. You're on its serial console through the crash cart.", 17, Ui.Bad);
            }
            else if (isSwitch)
            {
                // As with servers, each fault shows in two places.
                bool cfg = fault == Fault.ConfigLost, hung = fault == Fault.Hung, sfp = fault == Fault.BadSfp;
                Row(0, "Power supplies", "2/2 OK", Ui.Good);
                Row(1, "Fan trays", "4/4 OK", Ui.Good);
                Row(2, "Control plane", hung ? "UNRESPONSIVE" : "OK", hung ? Ui.Bad : Ui.Good);
                Row(3, "Uplinks", sfp ? "1 of 2 DOWN" : cfg ? "2 of 2 ADMIN DOWN" : hung ? "unknown" : "2 of 2 UP", sfp || cfg ? Ui.Bad : hung ? Ui.Warn : Ui.Good);
                Row(4, "Server ports", cfg ? "48 admin down" : sfp ? "48 up, no route" : "unknown", Ui.Warn);
                Row(5, "Running config", cfg ? "factory default" : "matches startup", cfg ? Ui.Warn : Ui.Good);
            }
            else if (task.cart)
            {
                Ui.Label(new Rect(bmc.x + 14, bmc.y + 50, 410, 150), "<b>No response from 10.20.0." + (Dev.id % 200 + 20) + "</b>\n\nThe BMC is down too. You're reading the server's own console through the crash cart's monitor and keyboard.", 17, Ui.Bad);
            }
            else
            {
                // Each fault shows up in two places, so no single row gives the answer away.
                bool crashed = fault == Fault.Hung || fault == Fault.BadDimm;
                bool hot = fault == Fault.FanFail;
                bool net = fault == Fault.LinkDown || fault == Fault.BadSfp;
                Row(0, "Power supplies", "2/2 OK (redundant)", Ui.Good);
                Row(1, "CPU temp", hot ? "88 C (warning)" : "47 C (normal)", hot ? Ui.Warn : Ui.Good);
                Row(2, "Fans", hot ? "5/6 OK" : "6/6 OK", hot ? Ui.Warn : Ui.Good);
                Row(3, "Memory", fault == Fault.BadDimm ? "1 uncorrectable error" : "OK (no ECC errors)", fault == Fault.BadDimm ? Ui.Warn : Ui.Good);
                Row(4, "Host heartbeat", crashed ? "LOST" : "OK", crashed ? Ui.Bad : Ui.Good);
                Row(5, "Network links", net ? "DEGRADED" : "UP", net ? Ui.Bad : Ui.Good);
            }
            Ui.Label(new Rect(p.x + 810, p.y + 322, 440, 26), "ROOT CAUSE", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            for (int i = 0; i < options.Count; i++)
            {
                var (label, f) = options[i];
                if (Ui.Button(new Rect(p.x + 810, p.y + 350 + i * 42, 440, 36), label, true, 17))
                {
                    if (f != Fault.None && f == fault) Win($"Root cause found: {Shift.FaultName(fault)}.");
                    else Oops($"Wrong diagnosis: it wasn't \"{label}\"", 2.5f);
                }
            }
        }
    }

    /// <summary>
    /// Hold the power button through a hard reset, then boot from the right device. In power-on mode
    /// (after the technician dropped the server's power) a short press is enough.
    /// </summary>
    public class PowerCycleGame : Minigame
    {
        public override bool OnScreen => true;
        public bool powerOn;
        int phase;
        float hold, timer;
        bool warned;
        List<string> boot;
        string Correct;
        bool isSwitch;

        protected override void Begin()
        {
            isSwitch = Dev.Def.kind == DevKind.Switch;
            Correct = isSwitch ? "bootflash:/nxos64.10.3.4.M.bin" : "RAID Virtual Disk 0 (OS)";
            title = (powerOn ? "Power on: " : "Power-cycle: ") + $"{Dev.Def.name} in {task.rack} U{task.u}";
            hint = powerOn
                ? "The server is off. Give the power button a short press (don't hold it) to power it on."
                : isSwitch ? "The switch's control plane is frozen. Press and HOLD its reset until it forces off (about 4 seconds)."
                : "The OS is frozen. Press and HOLD the power button until it forces off (about 4 seconds).";
            var rnd = new System.Random(task.deviceId);
            boot = (isSwitch
                ? new List<string> { Correct, "bootflash:/nxos64.10.2.6.M.bin.old", "TFTP: netboot from 10.0.0.5", "usb1:/recovery.bin", "bootflash:/nxos64.10.3.4.M.bin.upg-test" }
                : new List<string> { Correct, "PXE: Intel X710 port 1", "USB: SanDisk Cruzer", "UEFI Shell", "RAID Virtual Disk 1 (scratch)" }).OrderBy(_ => rnd.Next()).ToList();
        }

        Vector2 Button => new Vector2(P.x + 320, P.y + 360);

        protected override void Update(float dt, Vector2 m)
        {
            if (phase == 0 && powerOn)
            {
                if (Holding && Vector2.Distance(m, Button) < 90f) hold += dt / 4f;
                if (Released && hold > 0f)
                {
                    if (hold < 0.3f) { phase = 1; timer = 0; Audio.Play("clunk"); hint = "Powering on... watch the POST screen."; }
                    else { Oops("You held it too long: that's a forced power-off request. Short press!", 0.5f); }
                    hold = 0;
                }
            }
            else if (phase == 0)
            {
                if (Holding && Vector2.Distance(m, Button) < 90f) hold += dt / 4f;
                else
                {
                    if (hold > 0.15f && hold < 1f && !warned) { warned = true; game.Toast("Too short: a quick press only asks the hung OS to shut down. Hold it!", 2); }
                    hold = 0;
                }
                if (hold >= 1f) { phase = 1; timer = 0; Audio.Play("clunk"); hint = "Powered off. Powering back on... watch the POST screen."; }
            }
            else if (phase == 1)
            {
                timer += dt;
                if (timer > 4f) { phase = 2; hint = isSwitch ? "The loader prompt is up. Boot the current production image (not a backup or a test build)." : "The boot menu is up. Boot the server from its OS disk."; Audio.Play("beep"); }
            }
            else if (phase == 3)
            {
                timer += dt;
                if (timer > 2.5f) Win(isSwitch ? "The switch is back up and the rack's servers are reachable again." : "Server rebooted cleanly and is answering pings.");
            }
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            var scr = new Rect(p.x + 560, p.y + 90, 680, 540);
            Ui.Box(scr, new Color(0.01f, 0.02f, 0.05f));
            if (phase == 0)
            {
                Ui.Circle(Button, 90f, new Color(0.15f, 0.16f, 0.18f));
                Ui.Ring(Button, 96f, Color.Lerp(Ui.Line, Ui.Accent, hold));
                Ui.Circle(Button, 60f, Holding && Vector2.Distance(Ui.Mouse, Button) < 90f ? new Color(0.3f, 0.5f, 0.7f) : new Color(0.25f, 0.27f, 0.3f));
                Ui.Label(new Rect(Button.x - 60, Button.y - 20, 120, 40), "POWER", 20, Color.white, TextAnchor.MiddleCenter, true);
                Ui.Bar(new Rect(Button.x - 120, Button.y + 120, 240, 18), hold, Ui.Accent);
                Ui.Label(new Rect(scr.x + 20, scr.y + 20, scr.width - 40, 200), powerOn ? "[ no signal ]" : "[ console frozen ]\n\nwatchdog: BUG: soft lockup - CPU#12 stuck for 31s!\nwatchdog: BUG: soft lockup - CPU#3 stuck for 31s!", 16, new Color(0.7f, 0.75f, 0.8f));
            }
            else
            {
                string[] post = isSwitch
                    ? new[] { "Loader version 8.06", "Checking bootflash: ok", "Supervisor: 1 module OK", "Ethernet: 48x 25G + 6x 100G ports detected", "Fans: 4/4  PSUs: 2/2", "loader> dir bootflash:" }
                    : new[] { "Initializing firmware interfaces...", "Memory: 384 GB DDR5 OK", "CPU0: 32 cores OK   CPU1: 32 cores OK", "RAID controller: 2 virtual disks online", "Network: 4 ports detected", "Press F11 for Boot Menu" };
                int shown = phase == 1 ? Mathf.Clamp((int)(timer / 0.6f), 0, post.Length) : post.Length;
                for (int i = 0; i < shown; i++) Ui.Label(new Rect(scr.x + 20, scr.y + 20 + i * 28, scr.width - 40, 26), post[i], 17, new Color(0.75f, 0.8f, 0.9f));
                if (phase == 2)
                {
                    Ui.Label(new Rect(scr.x + 20, scr.y + 210, 500, 26), "BOOT MENU", 18, Ui.Accent, TextAnchor.UpperLeft, true);
                    for (int i = 0; i < boot.Count; i++)
                        if (Ui.Button(new Rect(scr.x + 20, scr.y + 244 + i * 54, 460, 44), boot[i], true, 18))
                        {
                            if (boot[i] == Correct) { phase = 3; timer = 0; hint = "Booting..."; }
                            else { Oops($"Booted from {boot[i]}: wrong device. Reboot and try again", 1); phase = 1; timer = 0; }
                        }
                }
                if (phase == 3) Ui.Label(new Rect(scr.x + 20, scr.y + 220, scr.width - 40, 60), isSwitch ? "Booting NX-OS 10.3(4) ..." : "Booting Linux 6.8.0 ...", 20, Ui.Good);
                Ui.Label(new Rect(p.x + 40, p.y + 200, 480, 300), isSwitch
                    ? "The switch is restarting.\nThe loader checks its flash and modules, then boots whichever image you pick."
                    : "The server is restarting.\nPOST checks memory, CPUs and the RAID controller before handing off to the boot loader.", 18, Ui.Dim);
            }
        }
    }
}
