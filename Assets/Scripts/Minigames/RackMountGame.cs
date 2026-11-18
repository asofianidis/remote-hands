using UnityEngine;

namespace RH
{
    /// <summary>
    /// On the real rack: slide a heavy unit onto the rails at the right U, then drive the screws home
    /// through the rack ears. In remove mode (decommissioning) it runs backwards: back the screws out,
    /// then ease the unit off the rails into your arms.
    /// </summary>
    public class RackMountGame : Minigame
    {
        public override bool InWorld => true;
        public bool remove;

        DevDef def;
        int target;
        bool sliding;
        float pos, vel, push, jam, misalign, tol, h, yc;
        Transform unit;
        GameObject ghost;

        class Screw { public Transform t; public float phase; public bool done; public float spin; }
        Screw[] screws;

        float ScrewWindow => career.Has("driver2") ? 24f : career.Has("driver") ? 16f : 9f;
        const float Front = -RackView.Depth / 2f + 0.05f;

        protected override void Begin()
        {
            def = Catalog.Dev(task.def);
            target = task.u;
            tol = 0.3f;
            h = def.heightU * RackView.UnitH;
            yc = RackView.UY(target) + h / 2f;
            MakeRig(RackV.transform);
            if (remove) HideOnRack(task.deviceId);
            BuildUnit();
            ghost = Box(rig, new Vector3(0, yc, Front + 0.02f), new Vector3(0.5f, h - 0.004f, 0.03f), Mats.Glass(new Color(0.2f, 1f, 0.4f, 0.28f)), "slot");
            var screwMat = Mats.Lit(new Color(0.75f, 0.77f, 0.8f), 0.6f, 0.8f);
            float y0 = RackView.UY(target) + h * (def.heightU == 1 ? 0.5f : 0.28f), y1 = RackView.UY(target) + h * 0.72f;
            var spots = def.heightU == 1
                ? new[] { new Vector3(-0.262f, y0, Front - 0.012f), new Vector3(0.262f, y0, Front - 0.012f) }
                : new[] { new Vector3(-0.262f, y0, Front - 0.012f), new Vector3(-0.262f, y1, Front - 0.012f), new Vector3(0.262f, y0, Front - 0.012f), new Vector3(0.262f, y1, Front - 0.012f) };
            screws = new Screw[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                var g = Box(rig, spots[i], new Vector3(0.011f, 0.011f, 0.006f), screwMat, "screw");
                screws[i] = new Screw { t = g.transform, phase = 0.1f + i * 0.3f };
                g.SetActive(remove);
            }
            if (remove)
            {
                pos = target;
                push = 1f;
                ghost.SetActive(false);
                ScrewCamera();
                title = $"De-rack: {def.name} at {task.rack} U{target}";
                hint = "Back the screws out: click each screw when the shrinking ring lands on the screw head.";
            }
            else
            {
                sliding = true;
                pos = target + 4f;
                SlideCamera();
                title = $"Rack-mount: {def.name} into {task.rack} U{target}";
                hint = "Move the mouse up and down to line the unit up with the green slot, then HOLD the left button to slide it in. It's heavy and sags, so keep it steady!";
            }
            PlaceUnit();
        }

        void BuildUnit()
        {
            unit = new GameObject("unit").transform;
            unit.SetParent(rig, false);
            Models.Spawn(Models.For(def), unit, Vector3.zero, palette: Models.Unit(def));
        }

        /// <summary>The unit's front face on screen, for the alignment outline.</summary>
        Rect FaceRect()
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in new[] { new Vector3(-0.27f, 0, 0), new Vector3(0.27f, 0, 0), new Vector3(-0.27f, h, 0), new Vector3(0.27f, h, 0) })
            {
                var p = ToUi(unit.TransformPoint(c));
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Unit height follows pos (in U); push 0 is fully out in front of the rack, 1 is home.</summary>
        void PlaceUnit()
        {
            unit.localPosition = new Vector3(0, RackView.UY(pos), Front - (1f - push) * 0.95f);
            float tilt = Mathf.Clamp(vel * 1.5f, -6f, 6f) + (1f - push) * 2.5f;
            unit.localRotation = Quaternion.Euler(tilt, 0, 0);
        }

        void SlideCamera() => Look(new Vector3(-0.8f, yc + 0.32f, -1.55f), new Vector3(0.05f, yc - 0.02f, -0.75f));

        void ScrewCamera()
        {
            float d = FitDistance(0.72f, Mathf.Max(0.3f, h * 2.5f), 0.85f);
            Look(new Vector3(0, yc, Front - d), new Vector3(0, yc, Front));
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (sliding) Slide(dt, m);
            else Screws(dt, m);
            PlaceUnit();
        }

        void Slide(float dt, Vector2 m)
        {
            // Where on screen is the slot, and how many pixels is one U there?
            var p = rig.parent;
            var slotUi = ToUi(p.TransformPoint(new Vector3(0, yc, Front)));
            float perU = Mathf.Max(4f, Mathf.Abs(ToUi(p.TransformPoint(new Vector3(0, yc + RackView.UnitH, Front))).y - slotUi.y));
            float desired = target + (slotUi.y - m.y) / perU;
            float noise = (Mathf.PerlinNoise(time * 0.9f, 3.1f) - 0.5f) * 9f;
            // Coming off the rails the unit hangs on your arms, so it sags harder the further out it is.
            float sag = 2.4f * def.heightU * (remove ? 2f - push : 1f);
            float acc = (desired - pos) * 26f - vel * 7.5f - sag + noise;
            vel += acc * dt;
            pos = Mathf.Clamp(pos + vel * dt, Mathf.Max(0.5f, target - 7), Mathf.Min(Rack.Units + 1, target + 9));
            jam -= dt;
            bool aligned = Mathf.Abs(pos - target) <= tol;
            if (Holding && jam <= 0)
            {
                if (!aligned)
                {
                    misalign += dt;
                    if (misalign > 0.25f && (remove ? push < 0.98f : push > 0.02f))
                    {
                        jam = 1.1f;
                        misalign = 0;
                        Audio.Play("clunk");
                        if (remove) Oops("The unit twisted on the rails and caught. Level it before you keep pulling", 0.5f);
                        else { push = 0; Oops("The unit jammed on the rails. Pull it back and line it up", 0.5f); }
                    }
                }
                else misalign = 0;
                push += (remove ? -1f : 1f) * dt / 1.5f;
            }
            else if (!remove) push = Mathf.Max(0, push - dt * 1.6f);
            push = Mathf.Clamp01(push);
            if (!remove && push >= 1f)
            {
                sliding = false;
                pos = target;
                vel = 0;
                ghost.SetActive(false);
                foreach (var s in screws) s.t.gameObject.SetActive(true);
                Audio.Play("clunk");
                ScrewCamera();
                hint = "Click each screw when the shrinking ring lands on the screw head.";
            }
            else if (remove && push <= 0f) Win($"{def.name} is off the rails and in your arms.");
        }

        void Screws(float dt, Vector2 m)
        {
            float speed = career.Has("driver2") ? 0.45f : career.Has("driver") ? 0.55f : 0.8f;
            foreach (var s in screws)
            {
                if (s.done) { s.spin += dt * 720f; if (!remove) s.t.localRotation = Quaternion.Euler(0, 0, s.spin); continue; }
                s.phase += dt * speed;
                if (s.phase > 1f) s.phase -= 1f;
            }
            if (Pressed)
            {
                Screw best = null;
                float bd = 60f;
                foreach (var s in screws)
                {
                    float d = Vector2.Distance(m, ToUi(s.t.position));
                    if (!s.done && d < bd) { bd = d; best = s; }
                }
                if (best != null)
                {
                    float r = Ring(best.phase);
                    if (r <= 14f + ScrewWindow && r >= 10f)
                    {
                        best.done = true;
                        Audio.Play("screw");
                        if (remove) best.t.gameObject.SetActive(false);
                    }
                    else { best.phase = 0f; Audio.Play("bad", 0.4f); }
                }
            }
            if (!System.Array.TrueForAll(screws, s => s.done)) return;
            if (remove)
            {
                sliding = true;
                Audio.Play("clunk");
                SlideCamera();
                hint = "Now HOLD the left button to ease it off the rails, keeping it level with the mouse. It gets heavier as it comes out!";
            }
            else Win($"{def.name} racked at {task.rack} U{target}.");
        }

        static float Ring(float t) => Mathf.Lerp(74f, 4f, t);

        public override void Draw()
        {
            WorldHud();
            if (sliding)
            {
                bool aligned = Mathf.Abs(pos - target) <= tol;
                // Outline the unit's face so alignment is easy to read against the rack.
                Ui.Border(FaceRect(), jam > 0 ? Ui.Bad : aligned ? Ui.Good : Ui.Warn, 3f);
                var panel = new Rect(20, 20, 300, 230);
                Ui.Box(panel, new Color(0.03f, 0.05f, 0.07f, 0.85f));
                Ui.Label(new Rect(panel.x + 18, panel.y + 14, 260, 24), "ALIGNMENT", 15, Ui.Dim, TextAnchor.UpperLeft, true);
                float off = pos - target;
                Ui.Label(new Rect(panel.x + 18, panel.y + 40, 270, 34), aligned ? "ON THE RAILS" : off > 0 ? $"Too high ({off:0.0}U)" : $"Too low ({-off:0.0}U)", 22, aligned ? Ui.Good : Ui.Warn, TextAnchor.UpperLeft, true, false);
                Ui.Label(new Rect(panel.x + 18, panel.y + 90, 260, 24), remove ? "STILL ON THE RAILS" : "INSERTED", 15, Ui.Dim, TextAnchor.UpperLeft, true);
                Ui.Bar(new Rect(panel.x + 18, panel.y + 118, 264, 20), push, aligned ? Ui.Good : Ui.Warn);
                if (jam > 0) Ui.Label(new Rect(panel.x + 18, panel.y + 150, 260, 36), "JAMMED!", 26, Ui.Bad, TextAnchor.UpperLeft, true);
                else Ui.Label(new Rect(panel.x + 18, panel.y + 150, 270, 70), $"{def.name} ({def.heightU}U)\n{(remove ? "From" : "Target")}: U{target}" + (def.heightU > 1 ? $"-U{target + def.heightU - 1}" : ""), 16, Ui.Text);
                Tag(rig.parent.TransformPoint(new Vector3(0.33f, yc, Front)), $"U{target}", 18, Ui.Good, 0, 80, TextAnchor.MiddleLeft);
            }
            else
            {
                foreach (var s in screws)
                {
                    if (s.done) continue;
                    var c = ToUi(s.t.position);
                    float r = Ring(s.phase);
                    bool inWin = r <= 14f + ScrewWindow && r >= 10f;
                    Ui.Ring(c, r, inWin ? Ui.Good : Ui.Accent);
                    Ui.Ring(c, 12f, new Color(1, 1, 1, 0.35f));
                }
                int done = System.Array.FindAll(screws, s => s.done).Length;
                var panel = new Rect(20, 20, 260, 60);
                Ui.Box(panel, new Color(0.03f, 0.05f, 0.07f, 0.85f));
                Ui.Label(panel, $"Screws {(remove ? "out" : "in")}: {done}/{screws.Length}", 22, Ui.Text, TextAnchor.MiddleCenter, true);
            }
        }
    }
}
