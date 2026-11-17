using UnityEngine;

namespace RH
{
    /// <summary>
    /// Slide a heavy device onto the rails at the right U, then drive the screws home. In remove mode
    /// (decommissioning) it runs backwards: back the screws out, then ease the unit off the rails.
    /// </summary>
    public class RackMountGame : Minigame
    {
        public bool remove;
        DevDef def;
        int target, lo;
        bool sliding;
        float pos, vel, push, jam, misalign, tol;
        const float UPix = 30f, Rows = 14;

        class Screw { public Vector2 p; public float t; public bool done; public float spin; }
        Screw[] screws;
        Rect panel;

        float RailBottom => P.y + 110 + Rows * UPix;
        float YofU(float u) => RailBottom - (u - lo) * UPix;
        float ScrewWindow => career.Has("driver2") ? 24f : career.Has("driver") ? 16f : 9f;

        protected override void Begin()
        {
            def = Catalog.Dev(task.def);
            target = task.u;
            lo = Mathf.Clamp(target - 5, 1, Rack.Units - (int)Rows + 1);
            tol = 0.3f;
            if (remove)
            {
                pos = target;
                push = 1f;
                SetupScrews();
                title = $"De-rack: {def.name} at U{target}";
                hint = "Back the four screws out: click each screw when the shrinking ring lands on the screw head.";
            }
            else
            {
                sliding = true;
                pos = target + 4f;
                title = $"Rack-mount: {def.name} -> U{target}";
                hint = "Move the mouse to line the unit up with the green slot, then HOLD the left button to slide it in. It's heavy and sags, so keep it steady!";
            }
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (sliding) Slide(dt, m);
            else Screws(dt, m);
        }

        void Slide(float dt, Vector2 m)
        {
            float desired = lo + (RailBottom - m.y) / UPix - def.heightU / 2f;
            float noise = (Mathf.PerlinNoise(time * 0.9f, 3.1f) - 0.5f) * 9f;
            // Coming off the rails the unit hangs on your arms, so it sags harder the further out it is.
            float sag = 2.4f * def.heightU * (remove ? 2f - push : 1f);
            float acc = (desired - pos) * 26f - vel * 7.5f - sag + noise;
            vel += acc * dt;
            pos = Mathf.Clamp(pos + vel * dt, lo - 1, lo + Rows);
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
            if (!remove && push >= 1f)
            {
                sliding = false;
                pos = target;
                Audio.Play("clunk");
                SetupScrews();
                hint = "Click each screw when the shrinking ring lands on the screw head.";
            }
            else if (remove && push <= 0f) Win($"{def.name} is off the rails and in your arms.");
        }

        void Screws(float dt, Vector2 m)
        {
            float speed = career.Has("driver2") ? 0.45f : career.Has("driver") ? 0.55f : 0.8f;
            foreach (var s in screws)
            {
                if (s.done) { s.spin += dt * 720f; continue; }
                s.t += dt * speed;
                if (s.t > 1f) s.t -= 1f;
            }
            if (Pressed)
            {
                Screw best = null;
                float bd = 60f;
                foreach (var s in screws)
                    if (!s.done && Vector2.Distance(m, s.p) < bd) { bd = Vector2.Distance(m, s.p); best = s; }
                if (best != null)
                {
                    float r = Ring(best.t);
                    if (r <= 14f + ScrewWindow && r >= 10f) { best.done = true; Audio.Play("screw"); }
                    else { best.t = 0f; Audio.Play("bad", 0.4f); }
                }
            }
            if (!System.Array.TrueForAll(screws, s => s.done)) return;
            if (remove)
            {
                sliding = true;
                Audio.Play("clunk");
                hint = "Now HOLD the left button to ease it off the rails, keeping it level with the mouse. It gets heavier as it comes out!";
            }
            else Win($"{def.name} racked at U{target}.");
        }

        static float Ring(float t) => Mathf.Lerp(74f, 4f, t);

        void SetupScrews()
        {
            var p = P;
            float h = Mathf.Max(70f, def.heightU * 60f);
            panel = new Rect(p.x + 190, p.y + 300 - h / 2, 900, h);
            float y0 = panel.y + h * 0.28f, y1 = panel.y + h * 0.72f;
            screws = new[]
            {
                new Screw { p = new Vector2(panel.x - 45, y0), t = 0.1f }, new Screw { p = new Vector2(panel.x - 45, y1), t = 0.45f },
                new Screw { p = new Vector2(panel.xMax + 45, y0), t = 0.7f }, new Screw { p = new Vector2(panel.xMax + 45, y1), t = 0.25f },
            };
        }

        public override void Draw()
        {
            Frame();
            var p = P;
            if (sliding) DrawSlide(p);
            else DrawScrews(p);
        }

        void DrawSlide(Rect p)
        {
            float x0 = p.x + 600, x1 = p.x + 1230;
            // Rack U grid (side view: the rack opens to the left).
            Ui.Box(new Rect(x0, YofU(lo + Rows), x1 - x0, Rows * UPix), new Color(0.03f, 0.04f, 0.05f, 1f));
            for (int u = lo; u <= lo + (int)Rows; u++)
            {
                float y = YofU(u);
                Ui.Box(new Rect(x0, y - 1, x1 - x0, 1.5f), new Color(0.3f, 0.34f, 0.4f, 0.6f));
                if (u < lo + Rows) Ui.Label(new Rect(x0 - 70, y - UPix, 60, UPix), $"U{u}", 15, Ui.Dim, TextAnchor.MiddleRight);
            }
            var slot = new Rect(x0, YofU(target + def.heightU), x1 - x0, def.heightU * UPix);
            Ui.Box(slot, new Color(0.2f, 0.9f, 0.4f, 0.18f));
            Ui.Border(slot, Ui.Good, 2f);
            // Other gear already in the rack around the slot.
            var rack = shift.Rack(task.rack);
            for (int u = lo; u < lo + Rows; u++)
            {
                var d = rack.At(u);
                if (d != null && d.u == u && !(remove && d.id == task.deviceId))
                {
                    var r = new Rect(x0 + 4, YofU(u + d.Def.heightU) + 2, x1 - x0 - 8, d.Def.heightU * UPix - 4);
                    Ui.Box(r, d.Def.color * 1.5f);
                    Ui.Label(r, d.Def.name, 14, Ui.Dim, TextAnchor.MiddleCenter);
                }
            }
            // Rail post.
            Ui.Box(new Rect(x0 - 6, YofU(lo + Rows), 6, Rows * UPix), new Color(0.6f, 0.62f, 0.66f));
            bool aligned = Mathf.Abs(pos - target) <= tol;
            float len = 330f;
            float right = x0 + 20 + push * (x1 - x0 - 40);
            var dev = new Rect(right - len, YofU(pos + def.heightU), len, def.heightU * UPix);
            Ui.Box(dev, def.color * 2.2f);
            Ui.Border(dev, jam > 0 ? Ui.Bad : aligned ? Ui.Good : Ui.Warn, 3f);
            Ui.Label(dev, def.name, 16, Color.white, TextAnchor.MiddleCenter, true);
            Ui.Label(new Rect(p.x + 30, p.y + 100, 230, 30), "ALIGNMENT", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            float off = pos - target;
            Ui.Label(new Rect(p.x + 30, p.y + 128, 230, 40), Mathf.Abs(off) <= tol ? "ON THE RAILS" : off > 0 ? $"Too high ({off:0.0}U)" : $"Too low ({-off:0.0}U)", 22, aligned ? Ui.Good : Ui.Warn, TextAnchor.UpperLeft, true, false);
            Ui.Label(new Rect(p.x + 30, p.y + 190, 230, 30), remove ? "STILL ON THE RAILS" : "INSERTED", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Bar(new Rect(p.x + 30, p.y + 220, 220, 22), push, aligned ? Ui.Good : Ui.Warn);
            if (jam > 0) Ui.Label(new Rect(p.x + 30, p.y + 260, 230, 40), "JAMMED!", 28, Ui.Bad, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(p.x + 30, p.y + 320, 230, 200), $"Rack {task.rack}\n{def.name} ({def.heightU}U)\n{(remove ? "From" : "Target")}: U{target}" + (def.heightU > 1 ? $"-U{target + def.heightU - 1}" : ""), 18, Ui.Text);
        }

        void DrawScrews(Rect p)
        {
            Ui.Box(new Rect(panel.x - 90, panel.y - 30, panel.width + 180, panel.height + 60), new Color(0.03f, 0.04f, 0.05f));
            Ui.Box(panel, def.color * 2f);
            Ui.Label(panel, def.name + "  " + def.model, 22, Color.white, TextAnchor.MiddleCenter, true);
            foreach (var side in new[] { panel.x - 80, panel.xMax + 10 })
                Ui.Box(new Rect(side, panel.y, 70, panel.height), new Color(0.55f, 0.57f, 0.6f));
            foreach (var s in screws)
            {
                // Mounting: a done screw is seated. Removing: a done screw is gone, leaving the empty hole.
                if (remove && s.done) { Ui.Circle(s.p, 9f, new Color(0.05f, 0.05f, 0.06f)); continue; }
                Ui.Circle(s.p, 14f, s.done ? new Color(0.75f, 0.78f, 0.8f) : new Color(0.4f, 0.42f, 0.45f));
                float a = (s.done ? s.spin : 0f) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 10f;
                Ui.Segment(s.p - d, s.p + d, new Color(0.15f, 0.15f, 0.17f), 3f);
                if (!s.done)
                {
                    float r = Ring(s.t);
                    bool inWin = r <= 14f + ScrewWindow && r >= 10f;
                    Ui.Ring(s.p, r, inWin ? Ui.Good : Ui.Accent);
                }
            }
            int done = System.Array.FindAll(screws, s => s.done).Length;
            Ui.Label(new Rect(p.x + 30, p.yMax - 110, 600, 40), $"Screws {(remove ? "out" : "in")}: {done}/4", 22, Ui.Text, TextAnchor.MiddleLeft, true);
        }
    }
}
