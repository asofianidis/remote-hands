using UnityEngine;

namespace RH
{
    public enum SwapMode { Drive, Psu, Sfp, Dimm, Fan }

    /// <summary>Hot-swap a module: find the faulty one, release it, pull it, seat the spare and lock it in.</summary>
    public class SwapGame : Minigame
    {
        public SwapMode mode;
        int slots, bad, step, cols;
        float latch, verify;
        Vector2 dragStart, dragPos;
        bool dragging;
        Rect chassis, bin, tray;
        readonly System.Random rnd = new System.Random();
        float[] activity;
        float spin;
        /// <summary>Only one follow-up ticket per job, however many times the wrong part gets pulled.</summary>
        bool followedUp;

        string Part => mode switch
        {
            SwapMode.Drive => "drive",
            SwapMode.Psu => "PSU",
            SwapMode.Sfp => "optic",
            SwapMode.Dimm => "DIMM",
            _ => "fan",
        };

        protected override void Begin()
        {
            var dev = Dev;
            var def = dev.Def;
            var p = P;
            if (mode == SwapMode.Drive) { slots = Mathf.Max(1, def.bays); bad = dev.failedBay; cols = slots <= 4 ? 4 : slots <= 12 ? 6 : 12; }
            else if (mode == SwapMode.Psu) { slots = def.psus; bad = dev.failedPsu; cols = 2; }
            else if (mode == SwapMode.Sfp) { slots = Mathf.Max(2, def.sfps); bad = dev.badSfp; cols = slots; }
            else if (mode == SwapMode.Dimm) { slots = 16; bad = dev.badDimm; cols = 8; }
            else { slots = 6; bad = dev.badFan; cols = 6; }
            bad = Mathf.Clamp(bad, 0, slots - 1);
            int rows = Mathf.CeilToInt(slots / (float)cols);
            float h = mode == SwapMode.Psu ? 190 : mode == SwapMode.Sfp ? 120 : mode == SwapMode.Fan ? 170 : mode == SwapMode.Dimm ? 300 : rows * 96 + 30;
            chassis = new Rect(p.x + 60, p.y + 110, p.width - 120, h + 40);
            bin = new Rect(p.x + 80, p.yMax - 200, 280, 120);
            tray = new Rect(p.xMax - 360, p.yMax - 200, 280, 120);
            activity = new float[slots];
            title = mode switch
            {
                SwapMode.Drive => $"Hot-swap drive: {def.name} in {task.rack} U{task.u}",
                SwapMode.Psu => $"Replace PSU: {def.name} in {task.rack} U{task.u} (rear)",
                SwapMode.Sfp => $"Replace SFP+ optic: {def.name} in {task.rack} U{task.u} (rear)",
                SwapMode.Dimm => $"Replace DIMM: {def.name} in {task.rack} U{task.u} (lid open)",
                _ => $"Replace fan: {def.name} in {task.rack} U{task.u} (lid open)",
            };
            Hint();
        }

        void Hint()
        {
            hint = step switch
            {
                0 => mode == SwapMode.Drive ? "Find the failed drive: steady AMBER light. Green flicker is normal activity. Click it." :
                     mode == SwapMode.Psu ? "Find the failed power supply (red status light) and click it. The other one is keeping the server alive!" :
                     mode == SwapMode.Sfp ? "Find the optic reporting a TX fault (red light) and click it." :
                     mode == SwapMode.Dimm ? "The server is powered down with the lid off. Find the DIMM with the amber fault LED next to it and click it." :
                     "Find the fan that isn't spinning (red LED) and click it. The others are keeping the CPUs cool!",
                1 => mode == SwapMode.Dimm ? "Open the ejector clips: drag them DOWN." : $"Pull the release latch: drag it DOWN.",
                2 => $"Drag the failed {Part} into the RMA bin.",
                3 => $"Drag the spare {Part} from your pouch into the empty slot.",
                4 => "Click the latch to lock the new part in.",
                _ => mode == SwapMode.Drive ? "Rebuilding the array..." : "Verifying...",
            };
        }

        Rect Slot(int i)
        {
            int rows = Mathf.CeilToInt(slots / (float)cols);
            float gw = (chassis.width - 40) / cols;
            float gh = (chassis.height - 40) / rows;
            int r = i / cols, c = i % cols;
            return new Rect(chassis.x + 20 + c * gw + 4, chassis.y + 20 + r * gh + 4, gw - 8, gh - 8);
        }

        Rect Latch(int i)
        {
            var s = Slot(i);
            float pull = step == 1 ? latch * 50f : step >= 2 && step < 4 ? 50f : 0f;
            return new Rect(s.x + s.width * 0.15f, s.y + 4 + pull, s.width * 0.7f, Mathf.Min(26f, s.height * 0.25f));
        }

        protected override void Update(float dt, Vector2 m)
        {
            for (int i = 0; i < slots; i++) activity[i] = Mathf.Max(0, activity[i] - dt * 4f);
            if (rnd.NextDouble() < dt * 6) activity[rnd.Next(slots)] = 1f;
            switch (step)
            {
                case 0:
                    if (!Pressed) break;
                    for (int i = 0; i < slots; i++)
                        if (Slot(i).Contains(m))
                        {
                            if (i == bad) { step = 1; Audio.Play("beep"); }
                            else WrongPart(i);
                            Hint();
                            break;
                        }
                    break;
                case 1:
                    if (Pressed && Latch(bad).Contains(m)) { dragging = true; dragStart = m; }
                    if (dragging && Holding) latch = Mathf.Clamp01((m.y - dragStart.y) / 50f);
                    if (Released) { dragging = false; if (latch < 1f) latch = 0f; }
                    if (latch >= 1f) { step = 2; dragging = false; Audio.Play("clunk"); Hint(); }
                    break;
                case 2:
                case 3:
                    var from = step == 2 ? Slot(bad) : tray;
                    if (Pressed && from.Contains(m)) dragging = true;
                    if (dragging) dragPos = m;
                    if (Released && dragging)
                    {
                        dragging = false;
                        var to = step == 2 ? bin : Slot(bad);
                        if (to.Contains(m)) { step++; Audio.Play("pickup"); Hint(); }
                    }
                    break;
                case 4:
                    if (Pressed && (Latch(bad).Contains(m) || Slot(bad).Contains(m))) { step = 5; Audio.Play("clunk"); Hint(); }
                    break;
                default:
                    verify += dt / 2.5f;
                    if (verify >= 1f) Win(mode switch
                    {
                        SwapMode.Drive => "Drive replaced. The array is rebuilding.",
                        SwapMode.Psu => "PSU replaced. Both power feeds are healthy.",
                        SwapMode.Sfp => "Optic replaced. The link is up.",
                        SwapMode.Dimm => "DIMM replaced. Memory test passed and the server is booting.",
                        _ => "Fan replaced. CPU temperatures are dropping back to normal.",
                    });
                    break;
            }
        }

        /// <summary>Pulling a healthy part hurts the customer and leaves a follow-up ticket to clean up.</summary>
        void WrongPart(int i)
        {
            var dev = Dev;
            switch (mode)
            {
                case SwapMode.Drive:
                    Oops("You pulled a HEALTHY drive. The RAID array is now critical and the customer saw I/O errors", 5);
                    if (!followedUp && shift.FollowUp(task.rack, dev, TaskKind.RaidRebuild, Fault.ArrayCritical,
                            $"Array critical on {dev.Name} ({task.rack} U{dev.u})",
                            $"You pulled the healthy drive in bay {i} of {dev.Name}. It's back in, but the controller now sees it as foreign and the array is critical. Clear its foreign config and add it back as a hot spare so the array rebuilds.", i) != null)
                        followedUp = true;
                    break;
                case SwapMode.Psu:
                    Oops("That PSU was carrying the whole server. It just lost power!", 7.5f);
                    if (!followedUp && shift.FollowUp(task.rack, dev, TaskKind.PowerOn, Fault.NoPower,
                            $"{dev.Name} lost power ({task.rack} U{dev.u})",
                            $"You pulled the live PSU on {dev.Name} and the server dropped. Once both PSUs are in, power it back on and boot it from its OS disk.") != null)
                        followedUp = true;
                    break;
                case SwapMode.Sfp:
                    Oops("Wrong optic: you cut a working link", 2.5f);
                    if (!followedUp && shift.FollowUp(task.rack, dev, TaskKind.Reseat, Fault.None,
                            $"Link flapping on {dev.Name} ({task.rack} U{dev.u})",
                            $"The healthy optic you pulled on {dev.Name} didn't seat properly when you pushed it back. Find the link that's down at the rear and reseat it.") != null)
                        followedUp = true;
                    break;
                case SwapMode.Dimm:
                    Oops("That DIMM was fine. You'll have to re-seat it and re-run the memory test", 2f);
                    break;
                default:
                    Oops("You pulled a working fan and the CPUs spiked toward their thermal limit", 2.5f);
                    break;
            }
        }

        public override void Draw()
        {
            Frame();
            Ui.Box(chassis, Dev.Def.color * 1.5f);
            Ui.Border(chassis, Ui.Line);
            Ui.Label(new Rect(chassis.x + 10, chassis.y + 2, 400, 18), $"{Dev.Def.model}  #{Dev.id}", 13, Ui.Dim);
            float blink = Mathf.Repeat(time * 2f, 1f);
            spin += Time.deltaTime * 900f;
            for (int i = 0; i < slots; i++)
            {
                var s = Slot(i);
                bool empty = i == bad && (step == 3 || (step == 2 && dragging));
                if (empty) { Ui.Box(s, new Color(0.01f, 0.01f, 0.01f)); Ui.Border(s, Ui.Accent, 2f); continue; }
                bool isBad = i == bad && step < 5;
                Ui.Box(s, mode == SwapMode.Psu ? new Color(0.22f, 0.23f, 0.25f) : mode == SwapMode.Dimm ? new Color(0.06f, 0.25f, 0.12f) : new Color(0.12f, 0.13f, 0.15f));
                Ui.Border(s, step == 0 && s.Contains(Ui.Mouse) ? Ui.Accent : new Color(0.35f, 0.37f, 0.4f), 2f);
                if (mode == SwapMode.Fan)
                {
                    // Spinning blades, except on the dead fan.
                    var c = s.center + new Vector2(0, -10);
                    Ui.Circle(c, s.height * 0.32f, new Color(0.05f, 0.05f, 0.06f));
                    float a0 = (isBad || (i == bad && step < 5) ? 20f : spin + i * 37f) * Mathf.Deg2Rad;
                    for (int k = 0; k < 5; k++)
                    {
                        float a = a0 + k * Mathf.PI * 2f / 5f;
                        Ui.Segment(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s.height * 0.3f, new Color(0.35f, 0.37f, 0.4f), 6f);
                    }
                }
                if (mode == SwapMode.Dimm)
                {
                    // Memory chips on the stick.
                    for (int k = 0; k < 4; k++) Ui.Box(new Rect(s.x + 6, s.y + 40 + k * (s.height - 80) / 4f, s.width - 12, (s.height - 80) / 4f - 6), new Color(0.08f, 0.08f, 0.09f));
                }
                // Label detail
                string label = mode switch
                {
                    SwapMode.Drive => $"BAY {i}",
                    SwapMode.Psu => $"PSU {(i == 0 ? "A" : "B")}  800W",
                    SwapMode.Sfp => $"PORT {i + 1}",
                    SwapMode.Dimm => $"{(char)('A' + i / 8)}{i % 8 + 1}",
                    _ => $"FAN {i + 1}",
                };
                Ui.Label(new Rect(s.x, s.yMax - 22, s.width, 20), label, 12, Ui.Dim, TextAnchor.MiddleCenter);
                var l = Latch(i);
                Ui.Box(l, i == bad && step >= 1 ? new Color(0.9f, 0.55f, 0.15f) : new Color(0.45f, 0.47f, 0.5f));
                Color led;
                if (isBad) led = mode == SwapMode.Drive || mode == SwapMode.Dimm ? new Color(1f, 0.6f, 0.05f) : Ui.Bad;
                else if (mode == SwapMode.Dimm && !(i == bad && step == 5)) led = new Color(0.1f, 0.12f, 0.1f);
                else if (i == bad && step == 5) led = blink < 0.5f ? Ui.Good : new Color(1f, 0.6f, 0.05f);
                else led = Color.Lerp(new Color(0.1f, 0.6f, 0.2f), new Color(0.5f, 1f, 0.5f), activity[i]);
                Ui.Circle(new Vector2(s.xMax - 14, s.y + s.height * 0.5f), 7f, led);
            }
            Ui.Panel(bin, new Color(0.3f, 0.1f, 0.08f, 0.9f));
            Ui.Label(bin, "RMA BIN\n(failed parts)", 18, Ui.Text, TextAnchor.MiddleCenter, true);
            Ui.Panel(tray, new Color(0.08f, 0.2f, 0.12f, 0.9f));
            Ui.Label(tray, step <= 3 ? $"SPARE {Part.ToUpperInvariant()}\n(from your pouch)" : "Installed", 18, Ui.Text, TextAnchor.MiddleCenter, true);
            if (dragging && (step == 2 || step == 3))
            {
                var s = Slot(bad);
                var r = new Rect(dragPos.x - s.width / 2, dragPos.y - s.height / 2, s.width, s.height);
                Ui.Box(r, step == 2 ? new Color(0.35f, 0.2f, 0.12f) : new Color(0.18f, 0.32f, 0.22f));
                Ui.Border(r, step == 2 ? Ui.Bad : Ui.Good, 2f);
            }
            if (step == 5) Ui.Bar(new Rect(chassis.x, chassis.yMax + 12, chassis.width, 14), verify, Ui.Good);
        }
    }
}
