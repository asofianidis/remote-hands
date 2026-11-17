using UnityEngine;

namespace RH
{
    /// <summary>
    /// A hands-on job. Logic runs in Tick (mouse in canvas coordinates), drawing in Draw (inside OnGUI).
    ///
    /// Physical jobs (InWorld) build a temporary 3D rig on the real rack, bench or frame, move the
    /// camera in close and draw only small overlays pinned to world points. Console jobs (OnScreen)
    /// wheel a crash cart up to the rack and draw their screen onto its monitor.
    /// </summary>
    public abstract class Minigame
    {
        public Game game;
        public WorkTask task;
        public string title = "", hint = "";
        public bool finished, success;
        protected float time;

        /// <summary>Built in 3D on the real equipment, with overlays.</summary>
        public virtual bool InWorld => false;
        /// <summary>Drawn on an in-world monitor (the crash cart in front of the rack).</summary>
        public virtual bool OnScreen => false;
        /// <summary>Where the camera should be while the job runs (world space).</summary>
        public Vector3 camPos;
        public Quaternion camRot = Quaternion.identity;
        /// <summary>OnScreen: the monitor's display, 16:9, as world-space top-left and bottom-right corners.</summary>
        public Vector3 displayTL, displayBR;

        protected Transform rig;
        bool hidOnRack;

        protected Shift shift => game.shift;
        protected Career career => game.career;
        protected Device Dev => shift.Dev(task.rack, task.deviceId);
        protected Camera Cam => game.player.cam;
        protected RackView RackV => task.rack != null && game.facility.racks.TryGetValue(task.rack, out var v) ? v : null;
        /// <summary>The panel area for 2D jobs, and the canvas mapped onto the monitor for OnScreen ones.</summary>
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
            if (OnScreen) BuildCrashCart();
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

        /// <summary>Tear down the rig and give the rack its unit back.</summary>
        public virtual void End()
        {
            if (rig != null) Object.Destroy(rig.gameObject);
            rig = null;
            if (hidOnRack) game.facility.Hide(shift, task.rack, 0);
            hidOnRack = false;
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

        // ================================================================ 3D rig helpers
        /// <summary>Start a rig parented to something (usually the rack), so its coordinates are local to it.</summary>
        protected Transform MakeRig(Transform parent)
        {
            rig = new GameObject("work rig").transform;
            rig.SetParent(parent, false);
            return rig;
        }

        /// <summary>Stop the rack drawing a unit (and optionally its cords) so the rig's copy can be taken apart.</summary>
        protected void HideOnRack(int deviceId, bool cords = false)
        {
            game.facility.Hide(shift, task.rack, deviceId, cords);
            hidOnRack = true;
        }

        protected GameObject Box(Transform parent, Vector3 local, Vector3 size, Material m, string name = "part") =>
            Mats.Box(parent, local, size, m, false, false, name);

        /// <summary>Point the camera from one spot to another, both in the rig's parent space.</summary>
        protected void Look(Vector3 fromLocal, Vector3 atLocal)
        {
            var p = rig.parent;
            camPos = p.TransformPoint(fromLocal);
            camRot = Quaternion.LookRotation(p.TransformPoint(atLocal) - camPos, Vector3.up);
        }

        /// <summary>How far back the camera must stand to fit a width x height area (filling `fill` of the view).</summary>
        protected float FitDistance(float width, float height, float fill = 0.8f)
        {
            float vt = Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float ht = vt * Cam.aspect;
            return Mathf.Max(height / fill / (2f * vt), width / fill / (2f * ht));
        }

        /// <summary>A world point in canvas units.</summary>
        protected Vector2 ToUi(Vector3 world) => Ui.FromWorld(Cam, world, out _);

        /// <summary>The on-screen rectangle (canvas units) of a world-space box.</summary>
        protected Rect UiRect(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            var b = r != null ? r.bounds : new Bounds(t.position, Vector3.one * 0.01f);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = ToUi(c);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Where the mouse ray meets a plane, in world space.</summary>
        protected Vector3 MouseOn(Plane plane)
        {
            var ray = Cam.ScreenPointToRay(Input.mousePosition);
            return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : ray.GetPoint(1f);
        }

        /// <summary>A label pinned to a world point.</summary>
        protected void Tag(Vector3 world, string text, int size, Color c, float dy = 0f, float w = 220f, TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = true)
        {
            var p = Ui.FromWorld(Cam, world, out bool front);
            if (!front) return;
            float x = anchor == TextAnchor.MiddleRight ? p.x - w : anchor == TextAnchor.MiddleLeft ? p.x : p.x - w / 2;
            Ui.Label(new Rect(x, p.y - 12 + dy, w, 24), text, size, c, anchor, bold, false);
        }

        /// <summary>A label with a dark backing, for text over busy 3D.</summary>
        protected void Chip(Vector3 world, string text, int size, Color c, float dy = 0f)
        {
            var p = Ui.FromWorld(Cam, world, out bool front);
            if (!front) return;
            float w = text.Length * size * 0.56f + 14;
            var r = new Rect(p.x - w / 2, p.y - 11 + dy, w, 22);
            Ui.Box(r, new Color(0.02f, 0.03f, 0.04f, 0.8f));
            Ui.Label(r, text, size, c, TextAnchor.MiddleCenter, true, false);
        }

        /// <summary>The bottom bar every 3D job shows: what it is, what to do, how to leave.</summary>
        protected void WorldHud()
        {
            var r = new Rect(20, Ui.H - 118, Ui.W - 40, 98);
            Ui.Box(r, new Color(0.03f, 0.05f, 0.07f, 0.88f));
            Ui.Box(new Rect(r.x, r.y, 5, r.height), Ui.Accent);
            Ui.Label(new Rect(r.x + 22, r.y + 10, r.width - 300, 34), title, 24, Ui.Text, TextAnchor.UpperLeft, true, false);
            Ui.Label(new Rect(r.xMax - 280, r.y + 12, 260, 30), "[Esc] step away", 18, Ui.Dim, TextAnchor.UpperRight);
            Ui.Label(new Rect(r.x + 22, r.y + 50, r.width - 44, 44), hint, 18, Ui.Warn, TextAnchor.UpperLeft);
        }

        /// <summary>Panel frame with the title and hint, for jobs drawn as a panel (console jobs draw it on the monitor).</summary>
        protected void Frame()
        {
            var p = P;
            Ui.Panel(p);
            Ui.Box(new Rect(p.x, p.y, p.width, 64), Ui.Bg2);
            Ui.Label(new Rect(p.x + 24, p.y + 10, p.width - 320, 44), title, 26, Ui.Text, TextAnchor.MiddleLeft, true, false);
            Ui.Label(new Rect(p.xMax - 300, p.y + 10, 276, 44), "[Esc] step away", 18, Ui.Dim, TextAnchor.MiddleRight);
            Ui.Label(new Rect(p.x + 24, p.yMax - 52, p.width - 48, 40), hint, 18, Ui.Warn, TextAnchor.MiddleLeft);
        }

        /// <summary>Console jobs: a crash cart rolled up to the front of the rack, its monitor filling the view.</summary>
        void BuildCrashCart()
        {
            var rv = RackV;
            if (rv == null) return;
            MakeRig(rv.transform);
            float z = -RackView.Depth / 2f - 0.6f;
            var dark = Mats.Lit(new Color(0.15f, 0.16f, 0.18f), 0.3f, 0.4f);
            var steel = Mats.Lit(new Color(0.3f, 0.36f, 0.45f), 0.3f, 0.5f);
            Box(rig, new Vector3(0, 0.08f, z), new Vector3(0.7f, 0.06f, 0.5f), dark);
            Box(rig, new Vector3(0, 0.95f, z), new Vector3(0.7f, 0.04f, 0.5f), dark);
            Box(rig, new Vector3(-0.32f, 0.5f, z), new Vector3(0.04f, 0.9f, 0.04f), steel);
            Box(rig, new Vector3(0.32f, 0.5f, z), new Vector3(0.04f, 0.9f, 0.04f), steel);
            Box(rig, new Vector3(0, 0.98f, z - 0.1f), new Vector3(0.45f, 0.02f, 0.16f), Mats.Lit(new Color(0.1f, 0.1f, 0.1f)), "keyboard");
            const float w = 0.56f, h = 0.315f;
            float y = 1.25f;
            Box(rig, new Vector3(0, y, z + 0.08f), new Vector3(w + 0.04f, h + 0.04f, 0.03f), dark, "monitor");
            Box(rig, new Vector3(0, y, z + 0.064f), new Vector3(w, h, 0.002f), Mats.Unlit(new Color(0.01f, 0.02f, 0.04f)), "display");
            Box(rig, new Vector3(0, (0.95f + y) / 2f, z + 0.1f), new Vector3(0.04f, y - 0.95f, 0.03f), steel);
            // A KVM lead from the cart into the server.
            var lead = Mats.Line(rig, new Color(0.1f, 0.1f, 0.12f), 0.008f);
            var d = Dev;
            float dy = d != null ? RackView.UY(d.u) + d.Def.heightU * RackView.UnitH / 2f : 1f;
            Mats.Sag(lead, rv.transform.TransformPoint(new Vector3(0.2f, 0.97f, z + 0.15f)), rv.transform.TransformPoint(new Vector3(0.15f, dy, -RackView.Depth / 2f + 0.04f)), 0.15f);
            float face = z + 0.062f;
            displayTL = rv.transform.TransformPoint(new Vector3(-w / 2, y + h / 2, face));
            displayBR = rv.transform.TransformPoint(new Vector3(w / 2, y - h / 2, face));
            float dist = FitDistance(w, h, 0.66f);
            Look(new Vector3(0, y, face - dist), new Vector3(0, y, face));
        }
    }
}
