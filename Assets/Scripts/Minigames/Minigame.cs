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

        /// <summary>The minigame for a job, or null for jobs done in the world (photos, shipping).</summary>
        public static Minigame For(WorkTask t)
        {
            switch (t.kind)
            {
                case TaskKind.Mount: return new RackMountGame();
                case TaskKind.Unmount: return new RackMountGame { remove = true };
                case TaskKind.Cable: return new CablingGame();
                case TaskKind.Reseat: return new CablingGame { mode = CableMode.Reseat };
                case TaskKind.Uncable: return new CablingGame { mode = CableMode.Unplug };
                case TaskKind.SwapDrive: return new SwapGame { mode = SwapMode.Drive };
                case TaskKind.SwapPsu: return new SwapGame { mode = SwapMode.Psu };
                case TaskKind.SwapSfp: return new SwapGame { mode = SwapMode.Sfp };
                case TaskKind.SwapDimm: return new SwapGame { mode = SwapMode.Dimm };
                case TaskKind.SwapFan: return new SwapGame { mode = SwapMode.Fan };
                case TaskKind.Firmware: return new FirmwareGame();
                case TaskKind.Diagnose: return new DiagnoseGame();
                case TaskKind.PowerCycle: return new PowerCycleGame();
                case TaskKind.PowerOn: return new PowerCycleGame { powerOn = true };
                case TaskKind.Inspect: return new InspectGame();
                case TaskKind.Trace: return new TraceGame();
                case TaskKind.Patch: return new PatchGame();
                case TaskKind.Power: return new PowerGame();
                case TaskKind.RaidRebuild: return new RaidGame();
                case TaskKind.Wipe: return new WipeGame();
                default: return null;
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
}
