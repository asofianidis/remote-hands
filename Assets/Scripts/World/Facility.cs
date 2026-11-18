using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>Builds the data hall: rack rows, storage room, loading dock, NOC desk, lighting.</summary>
    public class Facility : MonoBehaviour
    {
        public const float HallW = 22f, HallD = 24f, HallH = 3.6f;
        public static readonly Vector3 Spawn = new Vector3(6.5f, 0f, 2.6f);
        public static readonly Vector3 DockCenter = new Vector3(16f, 0f, 21.6f);

        public readonly Dictionary<string, RackView> racks = new Dictionary<string, RackView>();
        readonly List<StockShelf> shelves = new List<StockShelf>();
        readonly Dictionary<string, Vector3> stations = new Dictionary<string, Vector3>();
        readonly List<Light> hallLights = new List<Light>();
        readonly Dictionary<int, GameObject> cracs = new Dictionary<int, GameObject>();
        Transform root, dynamic, stationDyn;
        Light sun;
        GameObject marker;
        int deliverySlot, cols = Shift.RacksPerRow;
        bool powerFail;

        public static Vector3 RackPos(int row, int col) => new Vector3(3.6f + (col - 1) * 0.62f, 0f, 7f + row * 4f);
        public static float RowFrontZ(int row) => 7f + row * 4f - RackView.Depth / 2f;

        // Building plant: two CRAC units on the west wall (facing east), the plant wall by the entrance
        // (generator controller and fire panel, facing north), the visitor kiosk and the receiving bay.
        public const float CracDepth = 0.85f, CracWidth = 1.8f, CracHeight = 1.95f;
        public static Vector3 CracPos(int n) => new Vector3(0.55f, 0f, n == 1 ? 7.4f : 17.6f);
        /// <summary>The chilled-water manifold on the wall next to a CRAC: four loops, x on the wall, z along it.</summary>
        public static float ValveZ(int n, int loop) => CracPos(n).z + 1.25f + loop * 0.32f;
        public static readonly Vector3 PlantPos = new Vector3(9.6f, 0f, 0.12f);
        public static readonly Vector3 KioskPos = new Vector3(4.6f, 0f, 0.5f);
        public static readonly Vector3 ReceivingPos = new Vector3(20.3f, 0f, 19.4f);

        public void Build(Shift shift)
        {
            if (root != null) Destroy(root.gameObject);
            racks.Clear();
            shelves.Clear();
            stations.Clear();
            hallLights.Clear();
            cracs.Clear();
            powerFail = false;
            deliverySlot = 0;
            cols = Shift.ColsOf(shift.racks);
            root = new GameObject("Hall").transform;
            root.SetParent(transform, false);
            dynamic = new GameObject("Dynamic").transform;
            dynamic.SetParent(root, false);
            stationDyn = new GameObject("Station props").transform;
            stationDyn.SetParent(root, false);

            BuildShell();
            BuildLights();
            foreach (var r in shift.racks)
            {
                var g = new GameObject("Rack " + r.label);
                g.transform.SetParent(root, false);
                g.transform.localPosition = RackPos(r.row, r.col);
                var v = g.AddComponent<RackView>();
                v.Init(r.label);
                v.Rebuild(r, shift);
                racks[r.label] = v;
            }
            BuildRowDressing();
            BuildStorage();
            BuildDock();
            BuildNoc();
            BuildMeetMe();
            BuildCracs();
            BuildPlant();
            BuildKiosk();
            BuildReceiving();
            RefreshStations(shift);

            marker = Mats.Box(root, Vector3.zero, new Vector3(0.22f, 0.22f, 0.22f), Mats.Glow(new Color(0.3f, 0.85f, 1f), 4f), false, false, "waypoint");
            marker.transform.rotation = Quaternion.Euler(45, 0, 45);
            marker.SetActive(false);
        }

        void BuildShell()
        {
            var floor = Mats.Lit(new Color(0.55f, 0.57f, 0.6f), 0.15f);
            var wall = Mats.Lit(new Color(0.72f, 0.74f, 0.76f), 0.1f);
            var ceil = Mats.Lit(new Color(0.32f, 0.34f, 0.37f), 0.05f);
            Mats.Box(root, new Vector3(HallW / 2, -0.05f, HallD / 2), new Vector3(HallW, 0.1f, HallD), floor, true, false, "floor");
            var c = Mats.Box(root, new Vector3(HallW / 2, HallH + 0.05f, HallD / 2), new Vector3(HallW, 0.1f, HallD), ceil, true, false, "ceiling");
            Mats.Box(root, new Vector3(HallW / 2, HallH / 2, -0.1f), new Vector3(HallW + 0.4f, HallH, 0.2f), wall, true, false, "wall S");
            Mats.Box(root, new Vector3(-0.1f, HallH / 2, HallD / 2), new Vector3(0.2f, HallH, HallD), wall, true, false, "wall W");
            Mats.Box(root, new Vector3(HallW + 0.1f, HallH / 2, HallD / 2), new Vector3(0.2f, HallH, HallD), wall, true, false, "wall E");
            // North wall with the roll-up dock door.
            Mats.Box(root, new Vector3(6.5f, HallH / 2, HallD + 0.1f), new Vector3(13.4f, HallH, 0.2f), wall, true, false, "wall N1");
            Mats.Box(root, new Vector3(20.5f, HallH / 2, HallD + 0.1f), new Vector3(3.4f, HallH, 0.2f), wall, true, false, "wall N2");
            Mats.Box(root, new Vector3(16f, 3.15f, HallD + 0.1f), new Vector3(5.6f, 0.9f, 0.2f), wall, true, false, "door header");
            var door = Models.Spawn("rollup_door", root, new Vector3(16f, 0f, HallD + 0.03f));
            Models.FitCollider(door);

            // Floor tile grid and hazard striping at the dock.
            var seam = Mats.Lit(new Color(0.42f, 0.44f, 0.47f));
            for (float x = 0.6f; x < HallW; x += 0.6f) Mats.Box(root, new Vector3(x, 0.001f, HallD / 2), new Vector3(0.02f, 0.002f, HallD), seam, false, false, "seam");
            for (float z = 0.6f; z < HallD; z += 0.6f) Mats.Box(root, new Vector3(HallW / 2, 0.001f, z), new Vector3(HallW, 0.002f, 0.02f), seam, false, false, "seam");
            var yellow = Mats.Lit(new Color(0.95f, 0.75f, 0.1f));
            Mats.Box(root, new Vector3(16f, 0.003f, 20.1f), new Vector3(6f, 0.004f, 0.12f), yellow, false, false);
            Mats.Box(root, new Vector3(13f, 0.003f, 22f), new Vector3(0.12f, 0.004f, 3.8f), yellow, false, false);
            Mats.Box(root, new Vector3(19f, 0.003f, 22f), new Vector3(0.12f, 0.004f, 3.8f), yellow, false, false);
            // Storage zone outline
            var blue = Mats.Lit(new Color(0.2f, 0.5f, 0.9f));
            Mats.Box(root, new Vector3(16.5f, 0.003f, 3.3f), new Vector3(9f, 0.004f, 0.1f), blue, false, false);
            Mats.Box(root, new Vector3(12f, 0.003f, 10.45f), new Vector3(0.1f, 0.004f, 14.3f), blue, false, false);
            Mats.Text(root, new Vector3(16.5f, 3.1f, 3.4f), "STORAGE & SPARES", 0.32f, new Color(0.6f, 0.8f, 1f));
            Mats.Text(root, new Vector3(16f, 3.25f, HallD - 0.05f), "LOADING DOCK", 0.32f, new Color(1f, 0.85f, 0.3f));
        }

        void BuildLights()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.55f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.39f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.21f, 0.23f);
            RenderSettings.fog = false;

            sun = new GameObject("Key light").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional;
            sun.intensity = 0.55f;
            sun.color = new Color(0.92f, 0.96f, 1f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.Euler(62f, -35f, 0f);

            foreach (float x in new[] { 4.5f, 11f, 17.5f })
                foreach (float z in new[] { 4f, 10.5f, 17f, 22f })
                {
                    Models.Spawn("light_panel", root, new Vector3(x, HallH - 0.03f, z), shadows: false);
                    var l = new GameObject("Point light").AddComponent<Light>();
                    l.transform.SetParent(root, false);
                    l.transform.localPosition = new Vector3(x, HallH - 0.25f, z);
                    l.type = LightType.Point;
                    l.range = 9f;
                    l.intensity = 2.2f;
                    l.color = new Color(0.9f, 0.95f, 1f);
                    l.shadows = LightShadows.None;
                    hallLights.Add(l);
                }
        }

        /// <summary>On UPS after a utility failure, only the emergency lighting stays on: dim and red.</summary>
        public void SetPowerFail(bool on)
        {
            if (on == powerFail) return;
            powerFail = on;
            foreach (var l in hallLights)
            {
                if (l == null) continue;
                l.intensity = on ? 0.9f : 2.2f;
                l.color = on ? new Color(1f, 0.35f, 0.3f) : new Color(0.9f, 0.95f, 1f);
            }
            float k = on ? 0.4f : 1f;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.55f, 0.62f) * k;
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.39f, 0.43f) * k;
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.21f, 0.23f) * k;
            if (sun != null) sun.intensity = on ? 0.15f : 0.55f;
        }

        void BuildRowDressing()
        {
            var cold = Mats.Lit(new Color(0.47f, 0.52f, 0.6f), 0.15f);
            var hot = Mats.Lit(new Color(0.6f, 0.5f, 0.47f), 0.15f);
            var tape = Mats.Lit(new Color(0.95f, 0.75f, 0.1f));
            for (int r = 0; r < Shift.Rows.Length; r++)
            {
                var a = RackPos(r, 1);
                var b = RackPos(r, cols);
                // Until the expansion cages open, their footprint is taped out on the floor.
                if (cols == Shift.RacksPerRow)
                {
                    var e0 = RackPos(r, Shift.RacksPerRow + 1);
                    var e1 = RackPos(r, Shift.RacksPerRow + Shift.ExpansionCols);
                    float ex = (e0.x + e1.x) / 2f, ew = e1.x - e0.x + RackView.Width;
                    Mats.Box(root, new Vector3(ex, 0.003f, e0.z - RackView.Depth / 2f), new Vector3(ew, 0.004f, 0.06f), tape, false, false);
                    Mats.Box(root, new Vector3(ex, 0.003f, e0.z + RackView.Depth / 2f), new Vector3(ew, 0.004f, 0.06f), tape, false, false);
                    Mats.Box(root, new Vector3(e1.x + RackView.Width / 2f, 0.003f, e0.z), new Vector3(0.06f, 0.004f, RackView.Depth), tape, false, false);
                    var label = Mats.Text(root, new Vector3(ex, 0.01f, e0.z), "FUTURE EXPANSION", 0.09f, new Color(0.95f, 0.75f, 0.1f));
                    label.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                }
                float cx = (a.x + b.x) / 2f, len = b.x - a.x + RackView.Width;
                // Overhead cable tray and perforated cold-aisle tiles in front of the row.
                Models.Spawn("cable_tray", root, new Vector3(cx, 2.52f, a.z + 0.2f));
                Mats.Box(root, new Vector3(cx, 0.002f, RowFrontZ(r) - 0.9f), new Vector3(len, 0.003f, 1.2f), cold, false, false, "cold aisle");
                // Hot aisle behind the row: the rear of every rack, where power and cabling jobs happen.
                Mats.Box(root, new Vector3(cx, 0.002f, RowFrontZ(r) + RackView.Depth + 0.7f), new Vector3(len, 0.003f, 1.2f), hot, false, false, "hot aisle");
                Mats.Text(root, new Vector3(cx, 2.75f, RowFrontZ(r) + RackView.Depth + 0.6f), $"HOT AISLE  ROW {Shift.Rows[r]}  (REAR)", 0.13f, new Color(1f, 0.6f, 0.4f), TextAnchor.MiddleCenter, 180f);
                // Row signs at both ends of the aisle.
                string name = $"ROW {Shift.Rows[r]}";
                float sz = RowFrontZ(r) - 1.2f;
                foreach (var (x, yaw) in new[] { (a.x - 0.8f, 90f), (b.x + 0.8f, -90f) })
                {
                    Models.Spawn("row_sign", root, new Vector3(x, 2.35f, sz), palette: Models.With("body", Mats.Lit(new Color(0.08f, 0.1f, 0.13f))));
                    float off = yaw > 0 ? -0.025f : 0.025f;
                    Mats.Text(root, new Vector3(x + off, 2.35f, sz), $"{name}  {Shift.Rows[r]}01-{Shift.Rows[r]}{cols:00}", 0.16f, new Color(0.5f, 0.85f, 1f), TextAnchor.MiddleCenter, yaw);
                }
                // The cold aisle floor is where tile jobs happen: a walk-on trigger that only offers itself when there's work.
                var aisle = Mats.Box(root, new Vector3(cx, 0.05f, RowFrontZ(r) - 0.6f), new Vector3(len, 0.1f, 1.2f), cold, true, false, "cold aisle tiles");
                aisle.GetComponent<Renderer>().enabled = false;
                aisle.GetComponent<Collider>().isTrigger = true;
                AddStation(aisle, $"aisle{r}", $"Row {Shift.Rows[r]} cold aisle floor tiles", true);
                stations[$"aisle{r}"] = new Vector3(cx, 0.6f, RowFrontZ(r) - 0.6f);
            }
        }

        /// <summary>Two CRAC units on the west wall, each with its chilled-water manifold beside it.</summary>
        void BuildCracs()
        {
            for (int n = 1; n <= 2; n++)
            {
                var p = CracPos(n);
                var g = Models.Spawn("crac", root, p, Quaternion.Euler(0, -90f, 0), Models.With("body", Mats.Lit(new Color(0.78f, 0.8f, 0.82f), 0.3f, 0.2f)));
                var col = g.AddComponent<BoxCollider>();
                col.center = new Vector3(0, CracHeight / 2f, 0);
                col.size = new Vector3(CracWidth, CracHeight, CracDepth);
                AddStation(g, $"crac{n}", $"CRAC-{n} (cooling unit)");
                cracs[n] = g;
                stations[$"crac{n}"] = p + new Vector3(0.6f, 2.25f, 0);
                Mats.Text(root, new Vector3(p.x + CracDepth / 2f + 0.01f, CracHeight + 0.15f, p.z), $"CRAC-{n}", 0.16f, new Color(0.6f, 0.85f, 1f), TextAnchor.MiddleCenter, -90f);
                // Manifold: one chilled-water loop per row it serves, its own main, and the humidifier.
                var man = new GameObject($"CRAC-{n} manifold");
                man.transform.SetParent(root, false);
                var pipe = Mats.Lit(new Color(0.2f, 0.35f, 0.6f), 0.4f, 0.4f);
                var valve = Mats.Lit(new Color(0.55f, 0.58f, 0.62f), 0.5f, 0.7f);
                int r0 = (n - 1) * 2;
                string[] loops = { $"ROW {Shift.Rows[r0]}", $"ROW {Shift.Rows[r0 + 1]}", "MAIN", "HUMID." };
                for (int i = 0; i < 4; i++)
                {
                    float z = ValveZ(n, i);
                    Mats.Box(man.transform, new Vector3(0.12f, HallH / 2f, z), new Vector3(0.06f, HallH, 0.06f), pipe);
                    foreach (float y in new[] { 1.62f, 1.02f }) Mats.Box(man.transform, new Vector3(0.16f, y, z), new Vector3(0.08f, 0.1f, 0.1f), valve);
                    Mats.Text(man.transform, new Vector3(0.2f, 2.0f, z), loops[i], 0.06f, Color.white, TextAnchor.MiddleCenter, -90f);
                }
                var mc = man.AddComponent<BoxCollider>();
                mc.center = new Vector3(0.15f, 1.3f, (ValveZ(n, 0) + ValveZ(n, 3)) / 2f);
                mc.size = new Vector3(0.3f, 1.4f, ValveZ(n, 3) - ValveZ(n, 0) + 0.3f);
                AddStation(man, $"crac{n}", $"CRAC-{n} chilled-water valves");
            }
        }

        /// <summary>Hide a CRAC's front panel while its filters are being swapped.</summary>
        public void ShowCracPanel(int n, bool on)
        {
            if (cracs.TryGetValue(n, out var g) && g != null) Models.ShowPart(g, "Panel", on);
        }

        /// <summary>The plant wall by the entrance: generator controller and fire alarm panel.</summary>
        void BuildPlant()
        {
            var g = Models.Spawn("plant_panel", root, PlantPos, Quaternion.Euler(0, 180f, 0));
            var col = g.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 1.2f, 0);
            col.size = new Vector3(1.9f, 2.0f, 0.4f);
            AddStation(g, "plant", "Generator controller & fire panel");
            stations["plant"] = PlantPos + new Vector3(0, 2.4f, 0.3f);
            Mats.Text(root, PlantPos + new Vector3(0, 2.45f, 0.05f), "PLANT  /  GEN-1  /  FIRE", 0.13f, new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleCenter, 180f);
        }

        /// <summary>The security kiosk by the entrance, where visitors wait to be checked in.</summary>
        void BuildKiosk()
        {
            var g = Models.Spawn("kiosk", root, KioskPos, Quaternion.Euler(0, 180f, 0));
            var col = g.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.65f, 0);
            col.size = new Vector3(0.7f, 1.3f, 0.6f);
            AddStation(g, "kiosk", "Security kiosk");
            stations["kiosk"] = KioskPos + new Vector3(0, 1.75f, 0.3f);
            Mats.Text(root, KioskPos + new Vector3(0, 2.1f, -0.15f), "SECURITY  /  VISITORS", 0.11f, new Color(0.6f, 0.85f, 1f), TextAnchor.MiddleCenter, 180f);
        }

        /// <summary>The receiving bay next to the dock: inbound spares get checked here before they hit the shelves.</summary>
        void BuildReceiving()
        {
            var g = Models.Spawn("pallet", root, ReceivingPos, palette: Models.With("wood", Mats.Lit(new Color(0.45f, 0.55f, 0.35f), 0.2f)), collider: true);
            AddStation(g, "receiving", "Receiving bay");
            stations["receiving"] = ReceivingPos + new Vector3(0, 1.2f, 0);
            Models.Spawn("outbound_sign", root, ReceivingPos + new Vector3(0, 0, 0.7f), palette: Models.With("plastic", Mats.Lit(new Color(0.15f, 0.3f, 0.15f))));
            Mats.Text(root, ReceivingPos + new Vector3(0, 1.9f, 0.65f), "RECEIVING", 0.16f, Color.white, TextAnchor.MiddleCenter, 0f);
        }

        /// <summary>Props that come and go with the tickets: the leak puddle and the cartons waiting at receiving.</summary>
        public void RefreshStations(Shift shift)
        {
            if (stationDyn == null) return;
            foreach (Transform c in stationDyn) Destroy(c.gameObject);
            stations.Remove("leak");
            var mop = shift.TasksAtStation("leak").FirstOrDefault(x => !x.task.done).task;
            if (mop != null && racks.TryGetValue(mop.rack, out var rv))
            {
                var p = new Vector3(rv.transform.position.x, 0.004f, RowFrontZ(mop.u) - 0.65f);
                var puddle = Mats.Box(stationDyn, p, new Vector3(1.6f, 0.006f, 1.0f), Mats.Glass(new Color(0.3f, 0.55f, 0.9f, 0.55f)), true, false, "puddle");
                puddle.GetComponent<Collider>().isTrigger = true;
                var trig = puddle.GetComponent<BoxCollider>();
                trig.size = new Vector3(1f, 40f, 1f);
                AddStation(puddle, "leak", "Water on the floor", true);
                stations["leak"] = p + Vector3.up * 0.8f;
            }
            var recv = shift.TasksAtStation("receiving").FirstOrDefault(x => !x.task.done);
            if (recv.task != null)
                for (int i = 0; i < Mathf.Min(8, recv.ticket.cartons.Count); i++)
                    Models.Spawn("carton_2u", stationDyn, ReceivingPos + new Vector3((i % 4 % 2) * 0.5f - 0.25f, 0.141f + (i / 4) * 0.185f, (i % 4 / 2) * 0.48f - 0.24f));
        }

        void BuildStorage()
        {
            var sources = new List<(string def, PartKind part, bool isPart)>();
            foreach (var d in Catalog.Stocked) sources.Add((d, PartKind.Drive, false));
            foreach (var part in Catalog.Parts) sources.Add((null, part, true));
            for (int i = 0; i < sources.Count; i++)
            {
                var (def, part, isPart) = sources[i];
                var g = new GameObject("Shelf");
                g.transform.SetParent(root, false);
                g.transform.localPosition = new Vector3(21.1f, 0, 4.6f + i * 1.7f);
                g.transform.localRotation = Quaternion.Euler(0, 90f, 0); // local -Z (the front) faces -X, toward the hall
                var t = g.transform;
                Models.Spawn("shelf", t, Vector3.zero);
                var sh = g.AddComponent<StockShelf>();
                sh.def = def; sh.part = part; sh.isPart = isPart;
                shelves.Add(sh);
                var col = g.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 1f, 0);
                col.size = new Vector3(1.6f, 2f, 0.6f);
                // Stock in cartons: small parts boxes, or server-sized boxes for equipment.
                string carton = isPart ? "carton_small" : Models.Carton(Catalog.Dev(def));
                for (int s = 0; s < 3; s++)
                    for (int k = 0; k < 3; k++)
                        Models.Spawn(carton, t, new Vector3(-0.5f + k * 0.5f, 0.157f + s * 0.55f, 0), Quaternion.Euler(0, (s * 3 + k) % 2 == 0 ? 0 : 180, 0));
                Mats.Text(t, new Vector3(0, 2.18f, -0.05f), isPart ? Catalog.PartName(part).ToUpperInvariant() : Catalog.Dev(def).name.ToUpperInvariant(), 0.12f, new Color(0.85f, 0.92f, 1f));
            }
            // Returns bin and the wipe & shred bench.
            // Returns bin, facing the hall (north).
            var bin = Models.Spawn("returns_bin", root, new Vector3(18.6f, 0f, 4f), Quaternion.Euler(0, 180, 0), collider: true);
            bin.AddComponent<ReturnBin>();
            Mats.Text(root, new Vector3(18.6f, 1.15f, 4.41f), "RETURNS", 0.14f, Color.white, TextAnchor.MiddleCenter, 180f);
            // The wipe & shred bench: drawers and work side towards the hall, pegboard against the wall.
            var bench = Models.Spawn("workbench", root, new Vector3(15f, 0f, 4.1f), Quaternion.Euler(0, 180, 0), collider: true);
            var shred = Models.Spawn("shredder", root, new Vector3(15.65f, 0.875f, 4.15f), Quaternion.Euler(0, 180, 0), collider: true);
            AddStation(bench, "bench", "Wipe & shred station");
            AddStation(shred, "bench", "Wipe & shred station");
            stations["bench"] = new Vector3(15f, 1.75f, 4.1f);
            Mats.Text(root, new Vector3(15f, 1.82f, 3.68f), "WIPE & SHRED", 0.14f, new Color(1f, 0.7f, 0.6f), TextAnchor.MiddleCenter, 180f);
            // The crash cart, parked by the bench.
            var cart = new GameObject("Crash cart");
            cart.transform.SetParent(root, false);
            cart.transform.localPosition = new Vector3(13.2f, 0f, 6.6f);
            cart.transform.localRotation = Quaternion.Euler(0, 90f, 0);
            var ct = cart.transform;
            Models.Spawn("crash_cart", ct, Vector3.zero);
            var cs = cart.AddComponent<StockShelf>();
            cs.def = "kvm";
            shelves.Add(cs);
            Models.FitCollider(cart);
            Mats.Text(ct, new Vector3(0, 1.5f, 0.05f), "CRASH CART (KVM)", 0.08f, new Color(0.85f, 0.92f, 1f), TextAnchor.MiddleCenter, 0f);
        }

        void AddStation(GameObject g, string id, string name, bool onlyWithWork = false)
        {
            var s = g.AddComponent<Station>();
            s.id = id;
            s.title = name;
            s.onlyWithWork = onlyWithWork;
        }

        /// <summary>The meet-me room cross-connect frame on the west wall, where carriers' panels meet the customers'.</summary>
        void BuildMeetMe()
        {
            // The model faces -Z; turned so its face looks east into the hall.
            var frame = Models.Spawn("mmr_frame", root, new Vector3(0.3f, 0f, 12.5f), Quaternion.Euler(0, -90f, 0),
                Models.With("body", Mats.Lit(new Color(0.1f, 0.11f, 0.13f), 0.3f, 0.4f)), collider: true);
            AddStation(frame, "mmr", "Meet-me room frame");
            stations["mmr"] = new Vector3(0.6f, 2.75f, 12.5f);
            Mats.Text(root, new Vector3(0.5f, 2.62f, 12.5f), "MEET-ME ROOM", 0.2f, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleCenter, -90f);
        }

        void BuildDock()
        {
            for (int i = 0; i < 6; i++)
            {
                var p = DeliverySpot(i);
                Models.Spawn("pallet", root, new Vector3(p.x, 0f, p.z), collider: true);
            }
            // Outbound pallet for decommissioned gear, outside the delivery lane, with a sign.
            var outbound = Models.Spawn("pallet", root, new Vector3(11.2f, 0f, 22f), Quaternion.Euler(0, 90f, 0),
                Models.With("wood", Mats.Lit(new Color(0.35f, 0.5f, 0.6f), 0.2f)), collider: true);
            AddStation(outbound, "dock", "Outbound pallet");
            stations["dock"] = new Vector3(11.2f, 0.9f, 22f);
            Models.Spawn("outbound_sign", root, new Vector3(11.2f, 0f, 22.73f), palette: Models.With("plastic", Mats.Lit(new Color(0.1f, 0.25f, 0.35f))));
            Mats.Text(root, new Vector3(11.2f, 1.9f, 22.68f), "OUTBOUND", 0.16f, Color.white, TextAnchor.MiddleCenter, 0f);
        }

        static Vector3 DeliverySpot(int i) => new Vector3(14f + (i % 3) * 2f, 0.14f, 21.2f + (i / 3) * 1.5f);

        void BuildNoc()
        {
            // The operator sits on the east side, facing the screens (the model's operator side is -Z).
            Models.Spawn("noc_desk", root, new Vector3(1.8f, 0f, 2.6f), Quaternion.Euler(0, -90f, 0),
                Models.With("body", Mats.Lit(new Color(0.2f, 0.22f, 0.26f), 0.3f)), collider: true);
            Models.Spawn("office_chair", root, new Vector3(2.55f, 0f, 2.75f), Quaternion.Euler(0, 75f, 0), collider: true);
            // The screens are what you interact with: an invisible box over them.
            var screen = Mats.Box(root, new Vector3(1.53f, 1.17f, 2.6f), new Vector3(0.12f, 0.4f, 1.9f), Mats.Unlit(Color.black), true, false, "NOC screens");
            screen.GetComponent<Renderer>().enabled = false;
            screen.AddComponent<Terminal>();
            Mats.Text(root, new Vector3(1.6f, 1.45f, 2.6f), "NOC  TICKETS", 0.1f, new Color(0.6f, 0.85f, 1f), TextAnchor.MiddleCenter, -90f);
        }

        // ================================================================ dynamic objects
        public void Refresh(Shift shift, string label)
        {
            if (label != null && racks.TryGetValue(label, out var v)) v.Rebuild(shift.Rack(label), shift);
        }

        /// <summary>Let a job's rig stand in for a unit (and optionally the cords) while it's being worked on. 0 shows everything again.</summary>
        public void Hide(Shift shift, string label, int deviceId, bool cords = false)
        {
            if (label == null || !racks.TryGetValue(label, out var v)) return;
            if (v.hiddenDevice == deviceId && v.hideCords == cords) return;
            v.hiddenDevice = deviceId;
            v.hideCords = cords;
            v.Rebuild(shift.Rack(label), shift);
        }

        public void RefreshAll(Shift shift)
        {
            foreach (var r in shift.racks) Refresh(shift, r.label);
            RefreshStations(shift);
        }

        public void SpawnDeliveries(Shift shift)
        {
            foreach (var it in shift.deliveries) SpawnItem(it, DeliverySpot(deliverySlot++ % 6) + new Vector3(0, 0.25f * (deliverySlot / 6), 0), true);
            shift.deliveries.Clear();
        }

        public Pickup SpawnItem(Item it, Vector3 pos, bool boxed)
        {
            var def = it.Def;
            GameObject g;
            if (boxed)
            {
                // A customer's shipment: a big carton with their colour on a sticker.
                g = Models.Spawn(Models.Crate(def), dynamic, pos, collider: true);
                float top = Models.LocalBounds(g).max.y;
                Mats.Box(g.transform, new Vector3(0.22f, top + 0.0012f, 0.25f), new Vector3(0.22f, 0.002f, 0.22f), Mats.Unlit(Catalog.CustColor(it.label.Split(" - ")[0])), false, false);
            }
            else if (def.kind == DevKind.Tool) g = Models.Spawn(Models.For(def), dynamic, pos, collider: true);
            else
            {
                // Bare equipment on the floor: the unit itself, centred on the spot.
                g = new GameObject("item");
                g.transform.SetParent(dynamic, false);
                g.transform.localPosition = pos;
                Models.Spawn(Models.For(def), g.transform, new Vector3(0, 0, -0.45f), palette: Models.Unit(def));
                Models.FitCollider(g);
            }
            g.name = "item";
            var p = g.AddComponent<Pickup>();
            p.item = it;
            return p;
        }

        public void ShowMarker(Vector3? pos)
        {
            if (marker == null) return;
            marker.SetActive(pos.HasValue);
            if (!pos.HasValue) return;
            marker.transform.position = pos.Value + new Vector3(0, 0.35f + Mathf.Sin(Time.time * 3f) * 0.08f, 0);
            marker.transform.rotation = Quaternion.Euler(45, Time.time * 90f, 45);
        }

        public Vector3? ShelfPos(string def, PartKind? part)
        {
            foreach (var s in shelves)
                if (part.HasValue ? s.isPart && s.part == part.Value : !s.isPart && s.def == def)
                    return s.transform.position + new Vector3(-0.3f, s.def == "kvm" ? 1.8f : 2.5f, 0);
            return null;
        }

        public Vector3 StationPos(string id) => stations.TryGetValue(id, out var p) ? p : Vector3.zero;

        /// <summary>Marker spot above a rack, nudged toward the side (front or rear) the job is done from.</summary>
        public Vector3 RackTop(string label, bool rear) =>
            label != null && racks.TryGetValue(label, out var v) ? v.transform.position + new Vector3(0, RackView.Height + 0.25f, rear ? 0.7f : -0.7f) : Vector3.zero;

        /// <summary>Racks face -Z (the cold aisle); anything at a larger Z is behind them, in the hot aisle.</summary>
        public bool IsBehind(string label, Vector3 pos) => label != null && racks.TryGetValue(label, out var v) && pos.z > v.transform.position.z;

        /// <summary>The spot a visitor stands at to work on a rack: in the cold aisle, in front of it.</summary>
        public Vector3 RackFront(string label) =>
            label != null && racks.TryGetValue(label, out var v) ? v.transform.position + new Vector3(0, 0, -RackView.Depth / 2f - 0.75f) : Vector3.zero;

        /// <summary>
        /// Does the camera see the whole front (or rear) face of the rack, from that side, close enough to
        /// read? On failure, reason says what to fix.
        /// </summary>
        public bool PhotoCheck(string label, bool rear, Camera cam, out string reason)
        {
            reason = $"That isn't {label}. Find rack {label} and frame it.";
            if (!racks.TryGetValue(label, out var v)) return false;
            var t = v.transform;
            float z = rear ? RackView.Depth / 2f : -RackView.Depth / 2f;
            var corners = new[]
            {
                t.TransformPoint(new Vector3(-RackView.Width / 2, 0f, z)), t.TransformPoint(new Vector3(RackView.Width / 2, 0f, z)),
                t.TransformPoint(new Vector3(-RackView.Width / 2, RackView.Height + 0.15f, z)), t.TransformPoint(new Vector3(RackView.Width / 2, RackView.Height + 0.15f, z)),
            };
            var face = t.TransformPoint(new Vector3(0, RackView.Height / 2, z));
            var camPos = cam.transform.position;
            // Is this rack even what the camera is pointing at?
            var vp = cam.WorldToViewportPoint(face);
            bool inView = vp.z > 0 && vp.x > 0.2f && vp.x < 0.8f && vp.y > 0.1f && vp.y < 0.9f;
            if (!inView) return false;
            bool sideOk = rear ? camPos.z > face.z : camPos.z < face.z;
            if (!sideOk) { reason = $"Wrong side: the customer wants the {(rear ? "rear" : "front")} of {label}."; return false; }
            float dist = Vector3.Distance(camPos, face);
            if (dist > 3.6f) { reason = $"Too far away to read anything. Get closer to {label}."; return false; }
            float minY = 1, maxY = 0;
            foreach (var c in corners)
            {
                var p = cam.WorldToViewportPoint(c);
                if (p.z <= 0 || p.x < 0.01f || p.x > 0.99f || p.y < 0.01f || p.y > 0.99f) { reason = $"Get the whole of {label} in frame, top to bottom. Step back or tilt the camera."; return false; }
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            if (maxY - minY < 0.4f) { reason = $"{label} is too small in the shot. Get closer."; return false; }
            if (Physics.Linecast(camPos, face + (rear ? Vector3.forward : Vector3.back) * 0.05f, out var hit) && hit.collider.GetComponentInParent<RackView>() != v)
            {
                reason = "Something is blocking the shot.";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>How loud the fans are where the player stands: louder near racks, loudest in a hot aisle.</summary>
        public float Loudness(Vector3 pos)
        {
            float best = 99f;
            foreach (var v in racks.Values) best = Mathf.Min(best, Vector3.Distance(new Vector3(pos.x, 0, pos.z), v.transform.position));
            float level = Mathf.Lerp(0.3f, 0.85f, 1f - Mathf.Clamp01((best - 0.6f) / 6f));
            for (int r = 0; r < Shift.Rows.Length; r++)
            {
                float rear = RowFrontZ(r) + RackView.Depth;
                if (pos.z > rear && pos.z < rear + 2f && pos.x > RackPos(r, 1).x - 1f && pos.x < RackPos(r, cols).x + 1f) level += 0.15f;
            }
            return Mathf.Clamp01(level);
        }
    }
}
