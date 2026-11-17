using UnityEngine;

namespace RH
{
    /// <summary>
    /// A hands-on job shown as a full-screen panel. Logic runs in Tick (mouse in virtual canvas
    /// coordinates), drawing in Draw (inside OnGUI).
    /// </summary>
    public abstract class Minigame
    {
        public Game game;
        public WorkTask task;
        public string title = "", hint = "";
        public bool finished, success;
        protected float time;

        protected Shift shift => game.shift;
        protected Career career => game.career;
        protected Device Dev => shift.Dev(task.rack, task.deviceId);
        protected static Rect P => Ui.Center(1280, 720);

        public static Minigame For(WorkTask t)
        {
            switch (t.kind)
            {
                case TaskKind.Mount: return new RackMountGame();
                case TaskKind.Cable: return new CablingGame();
                case TaskKind.Reseat: return new CablingGame { reseat = true };
                case TaskKind.SwapDrive: return new SwapGame { mode = SwapMode.Drive };
                case TaskKind.SwapPsu: return new SwapGame { mode = SwapMode.Psu };
                case TaskKind.SwapSfp: return new SwapGame { mode = SwapMode.Sfp };
                case TaskKind.Firmware: return new FirmwareGame();
                case TaskKind.Diagnose: return new DiagnoseGame();
                default: return new PowerCycleGame();
            }
        }

        public void Start(Game g, WorkTask t)
        {
            game = g;
            task = t;
            g.shift.working = g.shift.TicketOf(t);
            Begin();
        }

        protected abstract void Begin();
        protected abstract void Update(float dt, Vector2 m);
        public abstract void Draw();

        public void Tick(float dt)
        {
            if (finished) return;
            time += dt;
            Update(dt, Ui.Mouse);
        }

        protected void Win(string msg)
        {
            if (finished) return;
            finished = true;
            success = true;
            Audio.Play("good");
            if (!string.IsNullOrEmpty(msg)) game.Toast(msg, 1);
        }

        protected void Oops(string msg, float rep)
        {
            Audio.Play("bad");
            shift.Mistake(msg, rep);
        }

        protected static bool Pressed => Input.GetMouseButtonDown(0);
        protected static bool Holding => Input.GetMouseButton(0);
        protected static bool Released => Input.GetMouseButtonUp(0);

        /// <summary>Panel frame with the title and hint drawn by every game.</summary>
        protected void Frame()
        {
            var p = P;
            Ui.Panel(p);
            Ui.Box(new Rect(p.x, p.y, p.width, 64), Ui.Bg2);
            Ui.Label(new Rect(p.x + 24, p.y + 10, p.width - 320, 44), title, 26, Ui.Text, TextAnchor.MiddleLeft, true, false);
            Ui.Label(new Rect(p.xMax - 300, p.y + 10, 276, 44), "[Esc] step away", 18, Ui.Dim, TextAnchor.MiddleRight);
            Ui.Label(new Rect(p.x + 24, p.yMax - 52, p.width - 48, 40), hint, 18, Ui.Warn, TextAnchor.MiddleLeft);
        }
    }

    /// <summary>Slide a heavy device onto the rails at the right U, then drive the screws home.</summary>
    public class RackMountGame : Minigame
    {
        DevDef def;
        int target, lo, phase;
        float pos, vel, push, jam, misalign, tol;
        const float UPix = 30f, Rows = 14;

        class Screw { public Vector2 p; public float t; public bool done; public float spin; }
        Screw[] screws;
        Rect panel;

        float RailBottom => P.y + 110 + Rows * UPix;
        float YofU(float u) => RailBottom - (u - lo) * UPix;

        protected override void Begin()
        {
            def = Catalog.Dev(task.def);
            target = task.u;
            lo = Mathf.Clamp(target - 5, 1, Rack.Units - (int)Rows + 1);
            pos = target + 4f;
            tol = 0.3f;
            title = $"Rack-mount: {def.name} -> U{target}";
            hint = "Move the mouse to line the unit up with the green slot, then HOLD the left button to slide it in. It's heavy and sags, so keep it steady!";
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (phase == 0)
            {
                float desired = lo + (RailBottom - m.y) / UPix - def.heightU / 2f;
                float noise = (Mathf.PerlinNoise(time * 0.9f, 3.1f) - 0.5f) * 9f;
                float acc = (desired - pos) * 26f - vel * 7.5f - 2.4f * def.heightU + noise;
                vel += acc * dt;
                pos = Mathf.Clamp(pos + vel * dt, lo - 1, lo + Rows);
                jam -= dt;
                bool aligned = Mathf.Abs(pos - target) <= tol;
                if (Holding && jam <= 0)
                {
                    if (!aligned)
                    {
                        misalign += dt;
                        if (misalign > 0.25f && push > 0.02f)
                        {
                            jam = 1.1f;
                            push = 0;
                            misalign = 0;
                            Audio.Play("clunk");
                            Oops("The unit jammed on the rails. Pull it back and line it up", 0.5f);
                        }
                    }
                    else misalign = 0;
                    push += dt / 1.5f;
                }
                else push = Mathf.Max(0, push - dt * 1.6f);
                if (push >= 1f)
                {
                    phase = 1;
                    pos = target;
                    Audio.Play("clunk");
                    SetupScrews();
                    hint = "Click each screw when the shrinking ring lands on the screw head.";
                }
            }
            else
            {
                float speed = career.Has("driver") ? 0.55f : 0.8f;
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
                        float window = career.Has("driver") ? 16f : 9f;
                        if (r <= 14f + window && r >= 10f) { best.done = true; Audio.Play("screw"); }
                        else { best.t = 0f; Audio.Play("bad", 0.4f); }
                    }
                }
                if (System.Array.TrueForAll(screws, s => s.done)) Win($"{def.name} racked at U{target}.");
            }
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
            if (phase == 0)
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
                    if (d != null && d.u == u)
                    {
                        var r = new Rect(x0 + 4, YofU(u + d.Def.heightU) + 2, x1 - x0 - 8, d.Def.heightU * UPix - 4);
                        Ui.Box(r, d.Def.color * 1.5f);
                        Ui.Label(r, d.Def.name, 14, Ui.Dim, TextAnchor.MiddleCenter);
                    }
                }
                // Rails posts.
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
                Ui.Label(new Rect(p.x + 30, p.y + 190, 230, 30), "INSERTED", 16, Ui.Dim, TextAnchor.UpperLeft, true);
                Ui.Bar(new Rect(p.x + 30, p.y + 220, 220, 22), push, aligned ? Ui.Good : Ui.Warn);
                if (jam > 0) Ui.Label(new Rect(p.x + 30, p.y + 260, 230, 40), "JAMMED!", 28, Ui.Bad, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(p.x + 30, p.y + 320, 230, 200), $"Rack {task.rack}\n{def.name} ({def.heightU}U)\nTarget: U{target}" + (def.heightU > 1 ? $"-U{target + def.heightU - 1}" : ""), 18, Ui.Text);
            }
            else
            {
                Ui.Box(new Rect(panel.x - 90, panel.y - 30, panel.width + 180, panel.height + 60), new Color(0.03f, 0.04f, 0.05f));
                Ui.Box(panel, def.color * 2f);
                Ui.Label(panel, def.name + "  " + def.model, 22, Color.white, TextAnchor.MiddleCenter, true);
                foreach (var side in new[] { panel.x - 80, panel.xMax + 10 })
                    Ui.Box(new Rect(side, panel.y, 70, panel.height), new Color(0.55f, 0.57f, 0.6f));
                foreach (var s in screws)
                {
                    Ui.Circle(s.p, 14f, s.done ? new Color(0.75f, 0.78f, 0.8f) : new Color(0.4f, 0.42f, 0.45f));
                    float a = (s.done ? s.spin : 0f) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 10f;
                    Ui.Segment(s.p - d, s.p + d, new Color(0.15f, 0.15f, 0.17f), 3f);
                    if (!s.done)
                    {
                        float r = Ring(s.t);
                        bool inWin = r <= 14f + (career.Has("driver") ? 16f : 9f) && r >= 10f;
                        Ui.Ring(s.p, r, inWin ? Ui.Good : Ui.Accent);
                    }
                }
                int done = System.Array.FindAll(screws, s => s.done).Length;
                Ui.Label(new Rect(p.x + 30, p.yMax - 110, 600, 40), $"Screws: {done}/4", 22, Ui.Text, TextAnchor.MiddleLeft, true);
            }
        }
    }
}
