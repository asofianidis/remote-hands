using UnityEngine;

namespace RH
{
    public enum SwapMode { Drive, Psu, Sfp, Dimm, Fan }

    /// <summary>
    /// Hot-swap a module on the real unit: find the faulty one, release its latch, pull it into the RMA
    /// bag, seat the spare from your pouch and lock it in. Drives are at the front, PSUs and optics at
    /// the rear; for memory and fans the server slides out on its rails with the lid off.
    /// </summary>
    public class SwapGame : Minigame
    {
        public override bool InWorld => true;
        public SwapMode mode;
        int slots, bad, step, cols;
        float latch, verify, pull, seat = 1f;
        bool dragLatch, dragPart, carrying, spareDrag;
        Vector2 dragStart;
        readonly System.Random rnd = new System.Random();
        float[] activity;
        float spin;
        /// <summary>Only one follow-up ticket per job, however many times the wrong part gets pulled.</summary>
        bool followedUp;

        class Slot { public Transform part, latch, socket, blades; public Renderer led; public Vector3 home, latchHome, ledHome; public string label; }
        Slot[] S;
        Vector3 pullDir;
        float pullLen;
        Transform dev;
        Material ledGreen, ledDim, ledAmber, ledRed, ledOff, partOk;

        const float Front = -RackView.Depth / 2f + 0.05f;
        const float Back = Front + 0.9f;

        string Part => mode switch
        {
            SwapMode.Drive => "drive",
            SwapMode.Psu => "PSU",
            SwapMode.Sfp => "optic",
            SwapMode.Dimm => "DIMM",
            _ => "fan",
        };

        Rect Bin => new Rect(20, Ui.H - 290, 300, 150);
        Rect Tray => new Rect(Ui.W - 320, Ui.H - 290, 300, 150);

        protected override void Begin()
        {
            var d = Dev;
            var def = d.Def;
            if (mode == SwapMode.Drive) { slots = Mathf.Max(1, def.bays); bad = d.failedBay; cols = slots <= 4 ? 4 : slots <= 12 ? 6 : 12; }
            else if (mode == SwapMode.Psu) { slots = def.psus; bad = d.failedPsu; cols = 2; }
            else if (mode == SwapMode.Sfp) { slots = Mathf.Max(2, def.sfps); bad = d.badSfp; cols = slots; }
            else if (mode == SwapMode.Dimm) { slots = 16; bad = d.badDimm; cols = 8; }
            else { slots = 6; bad = d.badFan; cols = 6; }
            bad = Mathf.Clamp(bad, 0, slots - 1);
            activity = new float[slots];
            ledGreen = Mats.Unlit(new Color(0.4f, 1f, 0.45f));
            ledDim = Mats.Unlit(new Color(0.08f, 0.45f, 0.15f));
            ledAmber = Mats.Unlit(new Color(1f, 0.6f, 0.05f));
            ledRed = Mats.Unlit(new Color(1f, 0.15f, 0.1f));
            ledOff = Mats.Unlit(new Color(0.06f, 0.06f, 0.06f));
            MakeRig(RackV.transform);
            // Rear jobs also hide the cords and cable bundle, which would sit right in front of the PSUs and NIC.
            HideOnRack(d.id, mode == SwapMode.Psu || mode == SwapMode.Sfp);
            Build(d);
            title = mode switch
            {
                SwapMode.Drive => $"Hot-swap drive: {def.name} in {task.rack} U{task.u}",
                SwapMode.Psu => $"Replace PSU: {def.name} in {task.rack} U{task.u} (rear)",
                SwapMode.Sfp => $"Replace SFP+ optic: {def.name} in {task.rack} U{task.u} (rear)",
                SwapMode.Dimm => $"Replace DIMM: {def.name} in {task.rack} U{task.u} (slid out, lid off)",
                _ => $"Replace fan: {def.name} in {task.rack} U{task.u} (slid out, lid off)",
            };
            Hint();
        }

        // ================================================================ building the unit
        void Build(Device d)
        {
            var def = d.Def;
            float h = def.heightU * RackView.UnitH, y0 = RackView.UY(d.u);
            bool open = mode == SwapMode.Dimm || mode == SwapMode.Fan;
            // Open-lid jobs: the server is slid out on its rails into the cold aisle.
            float slide = open ? -0.62f : 0f;
            dev = new GameObject("unit").transform;
            dev.SetParent(rig, false);
            dev.localPosition = new Vector3(0, y0, slide);
            var body = Mats.Lit(def.color, 0.35f, 0.3f);
            // Slightly self-lit so the parts read even in the rack's shadow.
            partOk = Mats.Glow(new Color(0.55f, 0.57f, 0.6f), 0.35f);
            S = new Slot[slots];
            if (open)
            {
                // Chassis tray with the lid off: floor, walls, two CPU heatsinks, a fan wall and two DIMM banks.
                Box(dev, new Vector3(0, 0.002f, Front + 0.45f), new Vector3(0.52f, 0.004f, 0.9f), Mats.Lit(def.color * 0.7f), "floor");
                Box(dev, new Vector3(-0.258f, h / 2f, Front + 0.45f), new Vector3(0.004f, h, 0.9f), body, "wall");
                Box(dev, new Vector3(0.258f, h / 2f, Front + 0.45f), new Vector3(0.004f, h, 0.9f), body, "wall");
                Box(dev, new Vector3(0, h / 2f, Front + 0.002f), new Vector3(0.52f, h, 0.004f), Mats.Lit(def.color * 1.6f, 0.5f, 0.2f), "face");
                Box(dev, new Vector3(0, 0.004f, Front + 0.6f), new Vector3(0.48f, 0.004f, 0.45f), Mats.Lit(new Color(0.08f, 0.3f, 0.15f)), "board");
                foreach (float cz in new[] { 0.36f, 0.56f })
                    Box(dev, new Vector3(0, Mathf.Min(h - 0.006f, 0.03f) / 2f + 0.006f, Front + cz), new Vector3(0.09f, Mathf.Min(h - 0.006f, 0.03f), 0.09f), Mats.Lit(new Color(0.7f, 0.72f, 0.75f), 0.5f, 0.8f), "heatsink");
                // Rails back into the rack.
                Box(rig, new Vector3(-0.27f, y0 + 0.01f, Front + 0.1f), new Vector3(0.01f, 0.01f, 0.9f), partOk, "rail");
                Box(rig, new Vector3(0.27f, y0 + 0.01f, Front + 0.1f), new Vector3(0.01f, 0.01f, 0.9f), partOk, "rail");
            }
            else
            {
                Box(dev, new Vector3(0, h / 2f, Front + 0.45f), new Vector3(0.53f, h - 0.004f, 0.9f), body, "body");
                if (mode == SwapMode.Drive) Box(dev, new Vector3(0, h / 2f, Front - 0.001f), new Vector3(0.48f, h * 0.9f, 0.004f), Mats.Lit(def.color * 1.3f, 0.5f, 0.2f), "face");
                else Box(dev, new Vector3(0, h / 2f, Back + 0.003f), new Vector3(0.5f, h * 0.86f, 0.006f), Mats.Lit(def.color * 0.8f, 0.3f, 0.3f), "rear");
            }

            var dark = Mats.Unlit(new Color(0.01f, 0.01f, 0.012f));
            var latchMat = Mats.Lit(new Color(0.9f, 0.55f, 0.15f), 0.4f, 0.3f);
            var latchIdle = Mats.Lit(new Color(0.45f, 0.47f, 0.5f), 0.4f, 0.4f);
            for (int i = 0; i < slots; i++)
            {
                var s = S[i] = new Slot();
                Vector3 home, size, socketSize;
                Vector3 latchOff, latchSize;
                switch (mode)
                {
                    case SwapMode.Drive:
                    {
                        int rows = Mathf.CeilToInt(slots / (float)cols);
                        int r = i / cols, c = i % cols;
                        float w = 0.42f / cols, hh = (h - 0.006f) / rows;
                        home = new Vector3(-0.21f + w * (c + 0.5f), 0.003f + hh * (rows - 1 - r + 0.5f), Front + 0.05f - 0.003f);
                        size = new Vector3(w - 0.004f, hh - 0.003f, 0.1f);
                        socketSize = new Vector3(w - 0.002f, hh - 0.001f, 0.002f);
                        latchOff = new Vector3(0, -hh * 0.25f, -0.052f);
                        latchSize = new Vector3((w - 0.004f) * 0.7f, hh * 0.28f, 0.004f);
                        pullDir = Vector3.back; pullLen = 0.13f;
                        s.label = $"BAY {i}";
                        break;
                    }
                    case SwapMode.Psu:
                    {
                        float ph = Mathf.Max(0.03f, h * 0.8f);
                        home = new Vector3(0.1f + i * 0.09f, h / 2f, Back - 0.15f + 0.012f);
                        size = new Vector3(0.078f, ph, 0.3f);
                        socketSize = new Vector3(0.08f, ph + 0.002f, 0.002f);
                        latchOff = new Vector3(-0.03f, 0, 0.153f);
                        latchSize = new Vector3(0.012f, ph * 0.7f, 0.006f);
                        pullDir = Vector3.forward; pullLen = 0.26f;
                        s.label = $"PSU {(i == 0 ? "A" : "B")}";
                        break;
                    }
                    case SwapMode.Sfp:
                    {
                        float x0 = -0.17f, gap = Mathf.Min(0.03f, 0.2f / slots);
                        home = new Vector3(x0 + i * gap, h / 2f, Back + 0.015f);
                        size = new Vector3(0.014f, 0.009f, 0.05f);
                        socketSize = new Vector3(0.017f, 0.012f, 0.002f);
                        latchOff = new Vector3(0, -0.007f, 0.026f);
                        latchSize = new Vector3(0.012f, 0.003f, 0.004f);
                        pullDir = Vector3.forward; pullLen = 0.06f;
                        s.label = $"PORT {i + 1}";
                        break;
                    }
                    case SwapMode.Dimm:
                    {
                        int bank = i / 8, k = i % 8;
                        float stickH = Mathf.Min(h - 0.008f, 0.032f);
                        home = new Vector3((bank == 0 ? -0.22f : 0.07f) + k * 0.021f, 0.006f + stickH / 2f, Front + 0.46f);
                        size = new Vector3(0.005f, stickH, 0.13f);
                        socketSize = new Vector3(0.008f, 0.003f, 0.135f);
                        latchOff = new Vector3(0, stickH * 0.35f, -0.068f);
                        latchSize = new Vector3(0.008f, stickH * 0.35f, 0.006f);
                        pullDir = Vector3.up; pullLen = 0.07f;
                        s.label = $"{(char)('A' + bank)}{k + 1}";
                        break;
                    }
                    default:
                    {
                        float fh = Mathf.Min(h - 0.006f, 0.04f);
                        home = new Vector3(-0.2f + i * 0.08f, 0.004f + fh / 2f, Front + 0.2f);
                        size = new Vector3(0.068f, fh, 0.035f);
                        socketSize = new Vector3(0.07f, 0.003f, 0.037f);
                        latchOff = new Vector3(0, fh / 2f + 0.003f, -0.012f);
                        latchSize = new Vector3(0.03f, 0.005f, 0.008f);
                        pullDir = Vector3.up; pullLen = 0.08f;
                        s.label = $"FAN {i + 1}";
                        break;
                    }
                }
                var socketPos = pullDir == Vector3.up ? new Vector3(home.x, 0.006f, home.z) : home + Vector3.Scale(-pullDir, size) * 0.5f + pullDir * 0.002f;
                if (pullDir == Vector3.back) socketPos = new Vector3(home.x, home.y, Front + 0.002f);
                if (pullDir == Vector3.forward) socketPos = new Vector3(home.x, home.y, Back + 0.008f);
                s.socket = Box(dev, socketPos, socketSize, dark, "socket").transform;
                s.home = home;
                var part = Box(dev, home, size, mode == SwapMode.Dimm ? Mats.Glow(new Color(0.1f, 0.45f, 0.2f), 0.4f) : partOk, "part");
                s.part = part.transform;
                s.latchHome = latchOff;
                s.latch = Box(s.part, Vector3.zero, Vector3.one, i == bad ? latchMat : latchIdle, "latch").transform;
                // The latch is sized in world terms, so undo the part's scale.
                s.latch.localScale = new Vector3(latchSize.x / size.x, latchSize.y / size.y, latchSize.z / size.z);
                s.latch.localPosition = new Vector3(latchOff.x / size.x, latchOff.y / size.y, latchOff.z / size.z);
                // Status LED on the outer face.
                var ledPos = pullDir == Vector3.up ? home + new Vector3(0.006f, -size.y / 2f + 0.002f, -size.z / 2f - 0.006f) : home + new Vector3(size.x * 0.35f, size.y * 0.25f, 0) + pullDir * (size.z / 2f + 0.002f);
                if (mode == SwapMode.Sfp) ledPos = home + new Vector3(0, 0.009f, size.z / 2f);
                s.led = Box(dev, ledPos, new Vector3(0.006f, 0.006f, 0.003f), ledGreen, "led").GetComponent<Renderer>();
                s.ledHome = ledPos;
                if (mode == SwapMode.Fan)
                {
                    s.blades = new GameObject("blades").transform;
                    s.blades.SetParent(dev, false);
                    s.blades.localPosition = home + new Vector3(0, 0, -size.z / 2f - 0.003f);
                    for (int b = 0; b < 4; b++)
                    {
                        var blade = Box(s.blades, Vector3.zero, new Vector3(size.y * 0.85f, 0.008f, 0.002f), Mats.Lit(new Color(0.12f, 0.12f, 0.14f)), "blade");
                        blade.transform.localRotation = Quaternion.Euler(0, 0, b * 45f);
                    }
                }
            }

            // Camera: head-on at the front or rear, or looking down into the open chassis.
            var dp = dev.localPosition;
            if (mode == SwapMode.Drive)
            {
                float dist = FitDistance(0.56f, Mathf.Max(0.14f, h * 1.8f), 0.62f);
                Look(dp + new Vector3(0, h / 2f, Front - dist), dp + new Vector3(0, h / 2f, Front));
            }
            else if (mode == SwapMode.Psu)
            {
                float dist = FitDistance(0.5f, 0.16f, 0.62f);
                Look(dp + new Vector3(0.145f, h / 2f, Back + dist), dp + new Vector3(0.145f, h / 2f, Back));
            }
            else if (mode == SwapMode.Sfp)
            {
                float cx = S[0].home.x + (S[slots - 1].home.x - S[0].home.x) / 2f;
                float dist = FitDistance(Mathf.Max(0.32f, slots * 0.05f), 0.12f, 0.62f);
                Look(dp + new Vector3(cx, h / 2f + 0.01f, Back + dist), dp + new Vector3(cx, h / 2f, Back));
            }
            else
            {
                float cz = Front + (mode == SwapMode.Dimm ? 0.46f : 0.24f);
                Look(dp + new Vector3(0, 0.5f, cz - 0.32f), dp + new Vector3(0, 0f, cz + 0.02f));
            }
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
                1 => mode == SwapMode.Dimm ? "Open the ejector clip (orange): drag it DOWN." : "Release the latch (orange): drag it DOWN.",
                2 => $"Drag the failed {Part} out, then drop it in the RMA bag.",
                3 => $"Drag the spare {Part} from your pouch into the empty slot.",
                4 => "Click the latch to lock the new part in.",
                _ => mode == SwapMode.Drive ? "Rebuilding the array..." : "Verifying...",
            };
        }

        // ================================================================ interaction
        Rect SlotRect(int i) => Grow(UiRect(S[i].part), 4f);
        Rect SocketRect(int i) => Grow(UiRect(S[i].socket), mode == SwapMode.Sfp ? 18f : 10f);
        Rect LatchRect(int i) => Grow(UiRect(S[i].latch), 12f);
        static Rect Grow(Rect r, float g) => new Rect(r.x - g, r.y - g, r.width + 2 * g, r.height + 2 * g);

        protected override void Update(float dt, Vector2 m)
        {
            for (int i = 0; i < slots; i++) activity[i] = Mathf.Max(0, activity[i] - dt * 4f);
            if (rnd.NextDouble() < dt * 6) activity[rnd.Next(slots)] = 1f;
            spin += dt * 900f;
            switch (step)
            {
                case 0:
                    if (!Pressed) break;
                    for (int i = 0; i < slots; i++)
                        if (SlotRect(i).Contains(m))
                        {
                            if (i == bad) { step = 1; Audio.Play("beep"); }
                            else WrongPart(i);
                            Hint();
                            break;
                        }
                    break;
                case 1:
                    if (Pressed && LatchRect(bad).Contains(m)) { dragLatch = true; dragStart = m; }
                    if (dragLatch && Holding) latch = Mathf.Clamp01((m.y - dragStart.y) / 50f);
                    if (Released) { dragLatch = false; if (latch < 1f) latch = 0f; }
                    if (latch >= 1f) { step = 2; dragLatch = false; Audio.Play("clunk"); Hint(); }
                    break;
                case 2:
                    if (Pressed && (SlotRect(bad).Contains(m) || carrying))
                    {
                        dragPart = true;
                        dragStart = m;
                        // Already pulled all the way out: pick it straight up again.
                        if (pull >= 1f) carrying = true;
                    }
                    if (dragPart && Holding && !carrying)
                    {
                        pull = Mathf.Clamp01(Vector2.Distance(m, dragStart) / 150f);
                        if (pull >= 1f) { carrying = true; Audio.Play("pickup"); }
                    }
                    if (Released && dragPart)
                    {
                        dragPart = false;
                        if (carrying && Bin.Contains(m)) { step = 3; carrying = false; Audio.Play("pickup"); Hint(); }
                        else if (carrying) { carrying = false; pull = 1f; }
                        else if (pull < 1f) pull = 0f;
                    }
                    break;
                case 3:
                    if (Pressed && Tray.Contains(m)) spareDrag = true;
                    if (Released && spareDrag)
                    {
                        spareDrag = false;
                        if (SocketRect(bad).Contains(m) || SlotRect(bad).Contains(m)) { step = 4; seat = 0f; latch = 1f; Audio.Play("clunk"); Hint(); }
                    }
                    break;
                case 4:
                    seat = Mathf.MoveTowards(seat, 1f, dt * 4f);
                    if (Pressed && seat >= 1f && (LatchRect(bad).Contains(m) || SlotRect(bad).Contains(m))) { step = 5; latch = 0f; Audio.Play("clunk"); Hint(); }
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
            Pose();
        }

        /// <summary>Move the 3D parts to match the job's state.</summary>
        void Pose()
        {
            float blink = Mathf.Repeat(time * 2f, 1f);
            for (int i = 0; i < slots; i++)
            {
                var s = S[i];
                bool isBad = i == bad;
                float out01 = isBad ? (step == 2 ? pull : step == 4 ? 1f - seat : 0f) : 0f;
                bool gone = isBad && (step == 3 || (step == 2 && carrying));
                s.part.gameObject.SetActive(!gone);
                s.part.localPosition = s.home + pullDir * out01 * pullLen;
                s.led.transform.localPosition = s.ledHome + pullDir * out01 * pullLen;
                float lat = isBad ? latch : 0f;
                s.latch.localPosition = new Vector3(s.latchHome.x / s.part.localScale.x, (s.latchHome.y - lat * 0.012f) / s.part.localScale.y, s.latchHome.z / s.part.localScale.z);
                s.latch.localRotation = Quaternion.Euler(lat * 35f, 0, 0);
                bool failed = isBad && step < 5 && step != 4;
                Material led;
                if (failed || (isBad && step == 4)) led = mode == SwapMode.Drive || mode == SwapMode.Dimm ? ledAmber : ledRed;
                else if (isBad && step == 5) led = blink < 0.5f ? ledGreen : ledAmber;
                else if (mode == SwapMode.Dimm) led = ledOff;
                // Drives flicker with I/O; power supplies, optics and fans just show steady green.
                else led = mode != SwapMode.Drive || activity[i] > 0.5f ? ledGreen : ledDim;
                if (isBad && step >= 3 && step < 5) led = ledOff;
                s.led.sharedMaterial = led;
                s.led.enabled = !gone;
                if (s.blades != null)
                {
                    s.blades.gameObject.SetActive(!gone);
                    s.blades.localPosition = s.home + new Vector3(0, 0, -s.part.localScale.z / 2f - 0.003f) + pullDir * out01 * pullLen;
                    bool still = isBad && step < 5;
                    s.blades.localRotation = Quaternion.Euler(0, 0, still ? 20f : spin + i * 37f);
                }
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
            WorldHud();
            var m = Ui.Mouse;
            // Slot labels and hover highlight.
            for (int i = 0; i < slots; i++)
            {
                var r = UiRect(S[i].socket);
                bool hover = step == 0 && SlotRect(i).Contains(m);
                if (hover) Ui.Border(SlotRect(i), Ui.Accent, 2f);
                if (slots <= 12 || i % 2 == 0)
                    Ui.Label(new Rect(r.center.x - 40, Mathf.Max(r.yMax, SlotRect(i).yMax) + 2, 80, 18), S[i].label, 12, Ui.Dim, TextAnchor.UpperCenter, true);
            }
            if (step == 1 || step == 4) Ui.Border(LatchRect(bad), Ui.Warn, 2f);
            if (step == 2 && !carrying) Ui.Border(SlotRect(bad), Ui.Warn, 2f);
            if (step == 3) Ui.Border(SocketRect(bad), Ui.Good, 2f);
            // The RMA bag and your pouch, in your hands.
            Ui.Box(Bin, new Color(0.3f, 0.08f, 0.06f, step == 2 && carrying ? 0.95f : 0.75f));
            Ui.Border(Bin, step == 2 && carrying ? Ui.Warn : Ui.Line, 2f);
            Ui.Label(Bin, "RMA BAG\n(failed parts)", 19, Ui.Text, TextAnchor.MiddleCenter, true);
            Ui.Box(Tray, new Color(0.06f, 0.2f, 0.1f, step == 3 ? 0.95f : 0.75f));
            Ui.Border(Tray, step == 3 ? Ui.Good : Ui.Line, 2f);
            Ui.Label(Tray, step <= 3 ? $"SPARE {Part.ToUpperInvariant()}\n(from your pouch)" : "Spare installed", 19, Ui.Text, TextAnchor.MiddleCenter, true);
            if (carrying || spareDrag)
            {
                var r = new Rect(m.x - 50, m.y - 22, 100, 44);
                Ui.Box(r, carrying ? new Color(0.45f, 0.25f, 0.15f) : new Color(0.2f, 0.4f, 0.25f));
                Ui.Border(r, carrying ? Ui.Bad : Ui.Good, 2f);
                Ui.Label(r, Part.ToUpperInvariant(), 15, Color.white, TextAnchor.MiddleCenter, true);
            }
            if (step == 5)
            {
                var bar = new Rect(Ui.W / 2 - 300, Ui.H - 160, 600, 18);
                Ui.Bar(bar, verify, Ui.Good);
            }
        }
    }
}
