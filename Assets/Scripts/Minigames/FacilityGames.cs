using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// At the front of a hot rack: fit a blanking panel into every open U slot so the hot exhaust stops
    /// leaking back to the intakes. Hold the right mouse button for the thermal camera.
    /// </summary>
    public class BlankingGame : Minigame
    {
        public override bool InWorld => true;
        class Gap { public int u; public Transform hole, panel; public bool filled; public float anim; }
        readonly List<Gap> gaps = new List<Gap>();
        bool thermal;
        float verify;
        const float Front = -RackView.Depth / 2f + 0.05f;

        protected override void Begin()
        {
            var rack = shift.Rack(task.rack);
            MakeRig(RackV.transform);
            var hole = Mats.Unlit(new Color(0.015f, 0.015f, 0.02f));
            var plate = Mats.Lit(new Color(0.11f, 0.12f, 0.14f), 0.3f, 0.3f);
            foreach (int u in rack.openGaps.OrderBy(x => x))
            {
                float y = RackView.UY(u) + RackView.UnitH / 2f;
                var g = new Gap { u = u };
                g.hole = Box(rig, new Vector3(0, y, Front + 0.01f), new Vector3(0.48f, RackView.UnitH - 0.004f, 0.004f), hole, "gap").transform;
                g.panel = Box(rig, new Vector3(0, y, Front - 0.2f), new Vector3(0.482f, RackView.UnitH - 0.002f, 0.006f), plate, "blanking panel").transform;
                g.panel.gameObject.SetActive(false);
                gaps.Add(g);
            }
            // The whole rack, sitting above the hint bar at the bottom of the screen.
            float dist = FitDistance(0.9f, RackView.Height + 0.1f, 0.82f);
            float cy = RackView.Height / 2f - 0.12f;
            Look(new Vector3(0, cy, Front - dist), new Vector3(0, cy, Front));
            title = $"Blanking panels: {task.rack} (front)";
            hint = "Click every open U slot to fit a blanking panel. Hold the RIGHT mouse button to see the heat through the thermal camera.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            thermal = Input.GetMouseButton(1);
            if (Pressed)
                foreach (var g in gaps)
                {
                    if (g.filled) continue;
                    var r = UiRect(g.hole);
                    if (!new Rect(r.x - 6, r.y - 4, r.width + 12, r.height + 8).Contains(m)) continue;
                    g.filled = true;
                    g.panel.gameObject.SetActive(true);
                    Audio.Play("clunk");
                    break;
                }
            foreach (var g in gaps)
            {
                if (!g.filled) continue;
                g.anim = Mathf.MoveTowards(g.anim, 1f, dt * 4f);
                var p = g.panel.localPosition;
                g.panel.localPosition = new Vector3(p.x, p.y, Mathf.Lerp(Front - 0.2f, Front - 0.003f, g.anim));
            }
            if (gaps.All(g => g.filled && g.anim >= 1f)) { verify += dt; if (verify > 0.5f) Win("Every gap is sealed. The inlet temperature is already falling."); }
        }

        Rect FaceRect(float y0, float y1)
        {
            var a = ToUi(rig.parent.TransformPoint(new Vector3(-0.24f, y1, Front)));
            var b = ToUi(rig.parent.TransformPoint(new Vector3(0.24f, y0, Front)));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        public override void Draw()
        {
            if (thermal)
            {
                Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(0.02f, 0.03f, 0.25f, 0.55f));
                foreach (var d in shift.Rack(task.rack).devices)
                    Ui.Box(FaceRect(RackView.UY(d.u), RackView.UY(d.u) + d.Def.heightU * RackView.UnitH), new Color(0.1f, 0.75f, 0.6f, 0.45f));
                float pulse = 0.55f + 0.25f * Mathf.Sin(time * 5f);
                foreach (var g in gaps.Where(x => !x.filled))
                {
                    var r = FaceRect(RackView.UY(g.u), RackView.UY(g.u + 1));
                    Ui.Box(new Rect(r.x, r.y - r.height * 1.5f, r.width, r.height * 4f), new Color(1f, 0.45f, 0.1f, 0.25f));
                    Ui.Box(r, new Color(1f, 0.15f, 0.05f, pulse));
                }
            }
            WorldHud();
            int left = gaps.Count(g => !g.filled);
            var q = new Rect(20, 110, 420, 92);
            Ui.Box(q, new Color(0.03f, 0.05f, 0.07f, 0.88f));
            Ui.Label(new Rect(q.x + 16, q.y + 10, q.width - 32, 30), $"Open gaps left: {left}", 20, left == 0 ? Ui.Good : Ui.Warn, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(q.x + 16, q.y + 44, q.width - 32, 40), $"Inlet {27f + left * 0.8f:0.0}°C   (limit 27°C)", 17, Ui.Text);
            var m = Ui.Mouse;
            foreach (var g in gaps.Where(x => !x.filled))
            {
                var r = UiRect(g.hole);
                if (new Rect(r.x - 6, r.y - 4, r.width + 12, r.height + 8).Contains(m)) Ui.Border(r, Ui.Accent, 2f);
            }
        }
    }

    /// <summary>
    /// Standing over a row's cold aisle: move the perforated floor tiles in front of the racks that need
    /// cold air, until every loaded rack's inlet reads 27°C or less.
    /// </summary>
    public class TilesGame : Minigame
    {
        public override bool InWorld => true;
        Shift.TileLayout layout;
        Transform[,] tiles;
        GameObject[,] air;
        int selI = -1, selD = -1;
        float verify;
        int row;
        Material perfMat, solidMat;

        Vector3 TilePos(int i, int d) => new Vector3(Facility.RackPos(row, layout.racks[i].col).x, 0.008f, Facility.RowFrontZ(row) - 0.3f - d * 0.6f);

        protected override void Begin()
        {
            row = task.u;
            layout = Shift.Tiles(shift.racks, row, task.slot);
            MakeRig(game.facility.transform);
            perfMat = Mats.Lit(new Color(0.72f, 0.74f, 0.78f), 0.4f, 0.5f);
            solidMat = Mats.Lit(new Color(0.5f, 0.52f, 0.56f), 0.15f);
            var holes = Mats.Unlit(new Color(0.12f, 0.13f, 0.15f));
            var cold = Mats.Glass(new Color(0.3f, 0.7f, 1f, 0.09f));
            tiles = new Transform[layout.Cols, 2];
            air = new GameObject[layout.Cols, 2];
            for (int i = 0; i < layout.Cols; i++)
                for (int d = 0; d < 2; d++)
                {
                    var t = new GameObject("tile").transform;
                    t.SetParent(rig, false);
                    t.localPosition = TilePos(i, d);
                    Box(t, Vector3.zero, new Vector3(0.585f, 0.012f, 0.585f), solidMat, "slab");
                    var grate = Box(t, new Vector3(0, 0.0065f, 0), new Vector3(0.46f, 0.002f, 0.46f), holes, "grate");
                    air[i, d] = Box(t, new Vector3(0, 0.2f, 0), new Vector3(0.44f, 0.4f, 0.44f), cold, "cold air");
                    tiles[i, d] = t;
                    grate.SetActive(false);
                }
            Sync();
            var a = TilePos(0, 0);
            var b = TilePos(layout.Cols - 1, 1);
            var target = new Vector3((a.x + b.x) / 2f, 0.2f, (a.z + b.z) / 2f + 0.15f);
            var dir = new Vector3(0, 0.9f, -0.44f).normalized;
            Look(target + dir * FitDistance(b.x - a.x + 0.9f, 1.6f, 0.88f), target);
            title = $"Floor tiles: row {Shift.Rows[row]} cold aisle";
            hint = "Click a perforated tile (the ones blowing cold air) to lift it, then click a solid tile to swap them. Every loaded rack needs its inlet at 27°C or less; heavy racks need two tiles.";
        }

        void Sync()
        {
            for (int i = 0; i < layout.Cols; i++)
                for (int d = 0; d < 2; d++)
                {
                    bool p = layout.perf[i, d];
                    var t = tiles[i, d];
                    t.Find("slab").GetComponent<Renderer>().sharedMaterial = p ? perfMat : solidMat;
                    t.Find("grate").gameObject.SetActive(p);
                    air[i, d].SetActive(p && !(i == selI && d == selD));
                }
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (Pressed)
            {
                for (int i = 0; i < layout.Cols; i++)
                    for (int d = 0; d < 2; d++)
                    {
                        if (!UiRect(tiles[i, d].Find("slab")).Contains(m)) continue;
                        if (layout.perf[i, d]) { selI = i; selD = d; Audio.Play("pickup"); }
                        else if (selI >= 0)
                        {
                            layout.perf[selI, selD] = false;
                            layout.perf[i, d] = true;
                            selI = selD = -1;
                            Audio.Play("clunk");
                        }
                        Sync();
                        goto done;
                    }
                done:;
            }
            for (int i = 0; i < layout.Cols; i++)
                for (int d = 0; d < 2; d++)
                {
                    var p = TilePos(i, d);
                    float lift = i == selI && d == selD ? 0.08f : 0f;
                    tiles[i, d].localPosition = Vector3.Lerp(tiles[i, d].localPosition, p + Vector3.up * lift, dt * 12f);
                }
            bool ok = Enumerable.Range(0, layout.Cols).All(i => Shift.InletTemp(layout, i) <= 27f);
            if (ok && selI < 0) { verify += dt; if (verify > 0.6f) Win($"Every rack in row {Shift.Rows[row]} is breathing cold air again."); }
            else verify = 0;
        }

        public override void Draw()
        {
            WorldHud();
            for (int i = 0; i < layout.Cols; i++)
            {
                var rack = layout.racks[i];
                var w = new Vector3(Facility.RackPos(row, rack.col).x, 0.35f, Facility.RowFrontZ(row));
                if (rack.devices.Count == 0) { Chip(w, $"{rack.label} empty", 12, Ui.Dim); continue; }
                float temp = Shift.InletTemp(layout, i);
                Chip(w, $"{rack.label} {temp:0.0}°C" + (Shift.TilesNeeded(rack) > 1 ? " (heavy)" : ""), 13, temp <= 27f ? Ui.Good : Ui.Bad);
            }
            int perf = 0;
            foreach (var p in layout.perf) if (p) perf++;
            var q = new Rect(20, 110, 460, 80);
            Ui.Box(q, new Color(0.03f, 0.05f, 0.07f, 0.88f));
            Ui.Label(new Rect(q.x + 16, q.y + 10, q.width - 32, 64), $"Perforated tiles: {perf}. That's enough for the row, if they're in the right places." + (selI >= 0 ? "\n<color=#40c8ff>Tile lifted: click a solid tile to put it there.</color>" : ""), 16, Ui.Text);
        }
    }

    /// <summary>At a CRAC unit with its front panel off: swap the clogged air filters, leave the clean ones, reset the alarm.</summary>
    public class CracGame : Minigame
    {
        public override bool InWorld => true;
        enum F { Clogged, Clean, Empty, Fresh }
        F[] filters;
        Transform[] views;
        int crac, dragging = -1;
        bool carryingDirty, carryingFresh, reset;
        Material dirty, clean;

        Rect Bag => new Rect(20, Ui.H - 290, 300, 150);
        Rect Stack => new Rect(Ui.W - 320, Ui.H - 290, 300, 150);
        float FrontX => Facility.CracPos(crac).x + Facility.CracDepth / 2f;
        Vector3 Slot(int i) => new Vector3(FrontX + 0.03f, 1.38f, Facility.CracPos(crac).z - 0.63f + i * 0.42f);
        int Clogged => filters.Count(f => f == F.Clogged);
        int Pressure => 110 + 75 * Clogged + 40 * filters.Count(f => f == F.Empty);

        protected override void Begin()
        {
            crac = task.u;
            var rnd = new System.Random(task.slot);
            filters = new F[4];
            for (int i = 0; i < 4; i++) filters[i] = F.Clean;
            foreach (int i in Enumerable.Range(0, 4).OrderBy(_ => rnd.Next()).Take(2 + rnd.Next(2))) filters[i] = F.Clogged;
            dirty = Mats.Lit(new Color(0.28f, 0.22f, 0.15f), 0.05f);
            clean = Mats.Lit(new Color(0.88f, 0.9f, 0.86f), 0.1f);
            MakeRig(game.facility.transform);
            game.facility.ShowCracPanel(crac, false);
            views = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var t = new GameObject("filter").transform;
                t.SetParent(rig, false);
                t.localPosition = Slot(i);
                Box(t, Vector3.zero, new Vector3(0.05f, 0.5f, 0.4f), clean, "media");
                Box(t, new Vector3(0.006f, 0, 0), new Vector3(0.045f, 0.52f, 0.02f), Mats.Lit(new Color(0.6f, 0.62f, 0.66f), 0.4f, 0.6f), "frame").transform.localPosition = new Vector3(0.006f, 0, -0.2f);
                views[i] = t;
            }
            Pose();
            var c = new Vector3(FrontX, 1.25f, Facility.CracPos(crac).z);
            Look(c + new Vector3(FitDistance(1.9f, 1.1f, 0.85f), 0.1f, 0), c);
            title = $"CRAC-{crac}: filter service";
            hint = "Drag each CLOGGED (brown) filter out into the bag, then drag clean filters from the stack into the empty slots. Leave the clean ones alone.";
        }

        public override void End()
        {
            game.facility.ShowCracPanel(crac, true);
            base.End();
        }

        void Pose()
        {
            for (int i = 0; i < 4; i++)
            {
                views[i].gameObject.SetActive(filters[i] != F.Empty && !(i == dragging && carryingDirty));
                views[i].Find("media").GetComponent<Renderer>().sharedMaterial = filters[i] == F.Clogged ? dirty : clean;
            }
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (Pressed && !reset)
            {
                if (Stack.Contains(m)) carryingFresh = true;
                else for (int i = 0; i < 4; i++)
                {
                    if (filters[i] == F.Empty || !UiRect(views[i]).Contains(m)) continue;
                    if (filters[i] == F.Clean || filters[i] == F.Fresh) { Oops("That filter was clean. Pulling it just lets dust into the coil", 0.5f); break; }
                    dragging = i;
                    carryingDirty = true;
                    Audio.Play("pickup");
                    break;
                }
            }
            if (Released)
            {
                if (carryingDirty && Bag.Contains(m)) { filters[dragging] = F.Empty; Audio.Play("pickup"); }
                if (carryingFresh)
                    for (int i = 0; i < 4; i++)
                    {
                        var r = SlotRect(i);
                        if (filters[i] != F.Empty || !r.Contains(m)) continue;
                        filters[i] = F.Fresh;
                        Audio.Play("clunk");
                        break;
                    }
                carryingDirty = carryingFresh = false;
                dragging = -1;
            }
            Pose();
        }

        Rect SlotRect(int i)
        {
            var p = Slot(i);
            var a = ToUi(p + new Vector3(0, 0.25f, -0.2f));
            var b = ToUi(p + new Vector3(0, -0.25f, 0.2f));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - 10, Mathf.Min(a.y, b.y) - 10, Mathf.Max(a.x, b.x) + 10, Mathf.Max(a.y, b.y) + 10);
        }

        public override void Draw()
        {
            WorldHud();
            var m = Ui.Mouse;
            for (int i = 0; i < 4; i++)
            {
                var r = SlotRect(i);
                if (filters[i] == F.Empty) { Ui.Border(r, carryingFresh ? Ui.Good : Ui.Warn, 2f); Ui.Label(r, "empty", 14, Ui.Warn, TextAnchor.MiddleCenter, true); }
                else if (r.Contains(m) && !carryingFresh) Ui.Border(r, Ui.Accent, 2f);
            }
            Ui.Box(Bag, new Color(0.3f, 0.2f, 0.08f, carryingDirty ? 0.95f : 0.75f));
            Ui.Border(Bag, carryingDirty ? Ui.Warn : Ui.Line, 2f);
            Ui.Label(Bag, "DIRTY FILTER BAG", 19, Ui.Text, TextAnchor.MiddleCenter, true);
            Ui.Box(Stack, new Color(0.06f, 0.2f, 0.1f, 0.85f));
            Ui.Border(Stack, Ui.Line, 2f);
            Ui.Label(Stack, "CLEAN FILTERS\n(drag one to a slot)", 19, Ui.Text, TextAnchor.MiddleCenter, true);
            if (carryingDirty || carryingFresh)
            {
                var r = new Rect(m.x - 50, m.y - 22, 100, 44);
                Ui.Box(r, carryingDirty ? new Color(0.35f, 0.25f, 0.12f) : new Color(0.2f, 0.4f, 0.25f));
                Ui.Label(r, "FILTER", 15, Color.white, TextAnchor.MiddleCenter, true);
            }
            // The unit's controller.
            var c = new Rect(Ui.W - 440, 110, 420, 210);
            Ui.Box(c, new Color(0.02f, 0.05f, 0.04f, 0.94f));
            Ui.Border(c, Ui.Line, 1.5f);
            bool alarm = Pressure > 200;
            Ui.Label(new Rect(c.x + 16, c.y + 10, 390, 24), $"CRAC-{crac} CONTROLLER", 15, Ui.Accent, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(c.x + 16, c.y + 40, 390, 26), $"Filter pressure drop: {Pressure} Pa", 19, alarm ? Ui.Bad : Ui.Good, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(c.x + 16, c.y + 70, 390, 24), alarm ? "ALARM: HIGH FILTER DP (limit 200 Pa)" : reset ? "Alarm cleared. Fans at 62%." : "Alarm condition cleared: reset to acknowledge", 16, alarm ? Ui.Bad : Ui.Text);
            bool ready = Clogged == 0 && !filters.Contains(F.Empty);
            if (!reset && Ui.Button(new Rect(c.x + 16, c.y + 130, 388, 56), "Reset alarm", ready, 20, Ui.Good))
            {
                reset = true;
                Win($"CRAC-{crac} is breathing freely. Its rows will cool off over the next hour.");
            }
        }
    }

    /// <summary>
    /// A chilled-water leak: on the manifold beside the CRAC, close the supply and return valves for the
    /// leaking row's loop. Only those: the other loops are still cooling something.
    /// </summary>
    public class ValveGame : Minigame
    {
        public override bool InWorld => true;
        class Valve { public string loop, label; public bool supply, target, main, closed; public Transform handle; public float anim; public Vector3 w; }
        readonly List<Valve> valves = new List<Valve>();
        int crac, row;
        float verify;
        const float WallX = 0.2f;

        protected override void Begin()
        {
            row = task.u;
            crac = Shift.CracFor(row);
            MakeRig(game.facility.transform);
            int r0 = (crac - 1) * 2;
            var loops = new[] { ($"ROW {Shift.Rows[r0]}", r0 == row, false), ($"ROW {Shift.Rows[r0 + 1]}", r0 + 1 == row, false), ($"CRAC-{crac} MAIN", false, true), ("HUMIDIFIER", false, false) };
            var red = Mats.Lit(new Color(0.75f, 0.2f, 0.15f), 0.4f, 0.3f);
            var steel = Mats.Lit(new Color(0.55f, 0.58f, 0.62f), 0.5f, 0.7f);
            for (int i = 0; i < loops.Length; i++)
            {
                var (name, target, main) = loops[i];
                float z = Facility.ValveZ(crac, i);
                foreach (bool supply in new[] { true, false })
                {
                    float y = supply ? 1.62f : 1.02f;
                    var v = new Valve { loop = name, supply = supply, target = target, main = main, label = $"{name} {(supply ? "SUPPLY" : "RETURN")}", w = new Vector3(WallX + 0.1f, y, z) };
                    var h = new GameObject("handle").transform;
                    h.SetParent(rig, false);
                    h.localPosition = new Vector3(WallX + 0.09f, y, z);
                    Box(h, new Vector3(0, 0.1f, 0), new Vector3(0.025f, 0.22f, 0.03f), red, "lever");
                    Box(h, Vector3.zero, new Vector3(0.04f, 0.04f, 0.04f), steel, "hub");
                    v.handle = h;
                    valves.Add(v);
                }
            }
            float mid = (Facility.ValveZ(crac, 0) + Facility.ValveZ(crac, 3)) / 2f;
            var c = new Vector3(WallX, 1.32f, mid);
            Look(c + new Vector3(FitDistance(1.6f, 1.1f, 0.8f), 0.05f, 0), c);
            title = $"Chilled water: isolate the row {Shift.Rows[row]} loop at CRAC-{crac}";
            hint = $"Click a valve to turn it. Close the SUPPLY and the RETURN valves for ROW {Shift.Rows[row]}, and nothing else.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            if (Pressed)
            {
                var v = valves.Where(x => Vector2.Distance(ToUi(x.w), m) < 40f).OrderBy(x => Vector2.Distance(ToUi(x.w), m)).FirstOrDefault();
                if (v != null)
                {
                    Audio.Play("screw");
                    if (v.closed) v.closed = false;
                    else if (v.target) v.closed = true;
                    else
                    {
                        // You feel it go wrong straight away and open it back up.
                        if (v.main) Oops($"You shut the CRAC-{crac} MAIN: both rows lost their cooling for a minute", 3f);
                        else Oops($"{v.label} isn't the leaking loop. You cut cooling to it before opening it back up", 1f);
                    }
                }
            }
            foreach (var v in valves)
            {
                v.anim = Mathf.MoveTowards(v.anim, v.closed ? 1f : 0f, dt * 3f);
                v.handle.localRotation = Quaternion.Euler(v.anim * 90f, 0, 0);
            }
            if (valves.Where(v => v.target).All(v => v.closed && v.anim >= 1f)) { verify += dt; if (verify > 0.4f) Win($"Row {Shift.Rows[row]}'s loop is isolated. The leak has stopped."); }
        }

        public override void Draw()
        {
            WorldHud();
            var m = Ui.Mouse;
            foreach (var v in valves)
            {
                var p = ToUi(v.w);
                if (Vector2.Distance(p, m) < 40f) Ui.Ring(p, 30f, Ui.Accent);
                Chip(v.w + new Vector3(0, -0.13f, 0), v.label + (v.closed ? "  CLOSED" : ""), 12, v.closed ? Ui.Warn : Ui.Text);
            }
        }
    }

    /// <summary>
    /// Utility power is out and the hall is on UPS. At the generator controller: clear what stopped the
    /// start, crank the engine, let it settle, then transfer the load. The UPS clock is running.
    /// </summary>
    public class GeneratorGame : Minigame
    {
        public override bool InWorld => true;
        string cause;
        bool fixedIt, running, fired, horn = true;
        float rpm, crank, fireAt, sinceFire, warm, hz, failedCrank;
        readonly List<string> log = new List<string>();

        Rect Panel => new Rect(Ui.W - 760, 110, 740, Ui.H - 250);
        Rect CrankRect => new Rect(Panel.x + 20, Panel.y + 560, 340, 70);

        protected override void Begin()
        {
            cause = task.component;
            fireAt = 1.6f + (float)new System.Random(task.GetHashCode()).NextDouble();
            MakeRig(game.facility.transform);
            var c = Facility.PlantPos + new Vector3(-0.45f, 1.45f, 0);
            Look(c + new Vector3(0.9f, 0.05f, FitDistance(1.6f, 1.2f, 0.75f)), c);
            log.Add("UTILITY SOURCE LOST");
            log.Add("UPS-1, UPS-2 ON BATTERY");
            log.Add(cause switch
            {
                "estop" => "SHUTDOWN: EMERGENCY STOP LATCHED (local)",
                "fuel" => "SHUTDOWN: FAIL TO START (low fuel pressure)",
                _ => "SHUTDOWN: FAIL TO START (start battery 9.8 V)",
            });
            if (cause == "fuel") log.Add("WARNING: DAY TANK LOW LEVEL, transfer pump in AUTO-OFF");
            title = "Generator 1: emergency start";
            hint = "Read the alarms and clear the cause. Then HOLD Crank until the engine fires and let go. Wait for 60 Hz before you transfer the load.";
        }

        protected override void Update(float dt, Vector2 m)
        {
            bool cranking = Holding && CrankRect.Contains(m) && !running;
            if (cranking)
            {
                crank += dt;
                rpm = Mathf.MoveTowards(rpm, fixedIt ? (fired ? 1800f : 260f) : 220f, dt * (fired ? 900f : 400f));
                if (fixedIt && !fired && crank > fireAt) { fired = true; sinceFire = 0; log.Add("ENGINE FIRED: release the crank"); Audio.Play("clunk"); }
                if (fired) sinceFire += dt;
                if (fired && sinceFire > 0.9f) { Oops("You kept cranking a running engine: the starter ground against the flywheel", 1f); sinceFire = -99f; }
                if (!fixedIt && crank > 2.2f && failedCrank <= 0)
                {
                    failedCrank = 1f;
                    shift.ups = Mathf.Max(1f, shift.ups - 8f);
                    Oops("The engine turns over but won't fire: whatever stopped it is still wrong. That wasted battery and time", 1f);
                    log.Add("CRANK FAILED");
                }
            }
            else
            {
                if (crank > 0 && fired && !running) { running = true; warm = 0; hz = 52f; log.Add("GEN-1 RUNNING, warming up"); }
                else if (crank > 0 && !fired) rpm = 0;
                crank = 0;
                failedCrank = 0;
            }
            if (running)
            {
                warm += dt;
                rpm = 1800f;
                // Frequency hunts around 60 Hz and settles as the governor catches up.
                hz = 60f + Mathf.Sin(warm * 3.1f) * 6f * Mathf.Exp(-warm * 0.55f) + (Mathf.PerlinNoise(warm, 0.3f) - 0.5f) * 0.2f;
            }
        }

        void Press(string id)
        {
            switch (id)
            {
                case "estop": case "fuel": case "battery":
                    if (id == cause && !fixedIt)
                    {
                        fixedIt = true;
                        log.Add(id == "estop" ? "E-STOP RESET" : id == "fuel" ? "FUEL TRANSFER PUMP: HAND, fuel pressure rising" : "START BATTERY: BACKUP (12.7 V)");
                        Audio.Play("beep");
                    }
                    else if (id != cause) Oops(id == "estop" ? "The E-stop wasn't latched" : id == "fuel" ? "The fuel system was fine" : "The start battery was fine", 0.5f);
                    break;
                case "horn": horn = false; log.Add("HORN SILENCED"); break;
                case "transfer":
                    if (!running) { Oops("There's nothing to transfer to: the generator isn't running", 1f); break; }
                    if (Mathf.Abs(hz - 60f) > 0.5f || warm < 3f)
                    {
                        Oops("You threw the load onto a cold, hunting engine. It bogged down and stalled", 2f);
                        running = fired = false; rpm = 0; hz = 0; log.Add("GEN-1 STALLED UNDER LOAD");
                        break;
                    }
                    log.Add("ATS: LOAD ON GENERATOR");
                    Win("The ATS transferred: the hall is on generator power. The UPS batteries are recharging.");
                    break;
            }
            while (log.Count > 9) log.RemoveAt(0);
        }

        public override void Draw()
        {
            WorldHud();
            var p = Panel;
            Ui.Box(p, new Color(0.03f, 0.04f, 0.05f, 0.95f));
            Ui.Border(p, Ui.Line, 1.5f);
            Ui.Label(new Rect(p.x + 20, p.y + 14, 500, 30), "GEN-1 CONTROLLER", 22, Ui.Accent, TextAnchor.UpperLeft, true);
            float ups = shift.ups;
            Ui.Label(new Rect(p.xMax - 330, p.y + 10, 310, 40), $"UPS {Mathf.FloorToInt(ups / 60)}:{Mathf.FloorToInt(ups % 60):00}", 32, ups < 45 ? Ui.Bad : Ui.Warn, TextAnchor.UpperRight, true);
            Ui.Label(new Rect(p.x + 20, p.y + 56, 700, 26), $"ENGINE {(running ? "RUNNING" : fired && crank > 0 ? "FIRED" : crank > 0 ? "CRANKING" : "STOPPED")}    {rpm:0} RPM    {(running ? hz : 0f):0.0} Hz    {(running ? 480 : 0)} V", 19, running ? (Mathf.Abs(hz - 60f) <= 0.5f ? Ui.Good : Ui.Warn) : Ui.Text, TextAnchor.UpperLeft, true);
            var al = new Rect(p.x + 20, p.y + 92, p.width - 40, 220);
            Ui.Box(al, new Color(0, 0, 0, 0.6f));
            for (int i = 0; i < log.Count; i++)
                Ui.Label(new Rect(al.x + 12, al.y + 8 + i * 23, al.width - 24, 22), log[i], 15, log[i].StartsWith("SHUTDOWN") || log[i].StartsWith("CRANK FAILED") || log[i].Contains("STALLED") ? Ui.Bad : log[i].StartsWith("WARNING") ? Ui.Warn : Ui.Good);
            float y = p.y + 330;
            string[] ids = { "estop", "fuel", "battery", "horn" };
            string[] names = { "Reset E-STOP", "Fuel transfer pump to HAND", "Switch to backup start battery", horn ? "Silence horn" : "Horn silenced" };
            for (int i = 0; i < ids.Length; i++)
                if (Ui.Button(new Rect(p.x + 20 + (i % 2) * 360, y + (i / 2) * 64, 340, 54), names[i], i < 3 ? !fixedIt : horn, 17)) Press(ids[i]);
            var cr = CrankRect;
            bool holding = Holding && cr.Contains(Ui.Mouse) && !running;
            Ui.Box(cr, holding ? new Color(0.35f, 0.25f, 0.1f) : new Color(0.12f, 0.14f, 0.16f));
            Ui.Border(cr, running ? Ui.Line : Ui.Warn, 2f);
            Ui.Label(cr, running ? "Engine running" : fired && holding ? "RELEASE!" : "HOLD TO CRANK", 21, running ? Ui.Dim : Ui.Text, TextAnchor.MiddleCenter, true);
            if (Ui.Button(new Rect(p.x + 380, cr.y, 340, 70), "Transfer load (ATS to GEN)", true, 19, Ui.Good)) Press("transfer");
        }
    }
}
