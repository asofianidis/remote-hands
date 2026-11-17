using System.Collections.Generic;
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
        Transform root, dynamic;
        GameObject marker;
        int deliverySlot;

        public static Vector3 RackPos(int row, int col) => new Vector3(3.6f + (col - 1) * 0.62f, 0f, 7f + row * 4f);
        public static float RowFrontZ(int row) => 7f + row * 4f - RackView.Depth / 2f;

        public void Build(Shift shift)
        {
            if (root != null) Destroy(root.gameObject);
            racks.Clear();
            shelves.Clear();
            stations.Clear();
            deliverySlot = 0;
            root = new GameObject("Hall").transform;
            root.SetParent(transform, false);
            dynamic = new GameObject("Dynamic").transform;
            dynamic.SetParent(root, false);

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
            var door = Mats.Lit(new Color(0.42f, 0.45f, 0.48f), 0.3f, 0.5f);
            for (int i = 0; i < 9; i++)
                Mats.Box(root, new Vector3(16f, 0.15f + i * 0.3f, HallD + 0.05f), new Vector3(5.6f, 0.27f, 0.08f), door, true, false, "door slat");

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

            var sun = new GameObject("Key light").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional;
            sun.intensity = 0.55f;
            sun.color = new Color(0.92f, 0.96f, 1f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.Euler(62f, -35f, 0f);

            var panel = Mats.Glow(new Color(0.95f, 0.98f, 1f), 2.5f);
            foreach (float x in new[] { 4.5f, 11f, 17.5f })
                foreach (float z in new[] { 4f, 10.5f, 17f, 22f })
                {
                    Mats.Box(root, new Vector3(x, HallH - 0.03f, z), new Vector3(1.6f, 0.04f, 0.35f), panel, false, false, "light panel");
                    var l = new GameObject("Point light").AddComponent<Light>();
                    l.transform.SetParent(root, false);
                    l.transform.localPosition = new Vector3(x, HallH - 0.25f, z);
                    l.type = LightType.Point;
                    l.range = 9f;
                    l.intensity = 2.2f;
                    l.color = new Color(0.9f, 0.95f, 1f);
                    l.shadows = LightShadows.None;
                }
        }

        void BuildRowDressing()
        {
            var tray = Mats.Lit(new Color(0.85f, 0.7f, 0.15f), 0.3f, 0.4f);
            var cold = Mats.Lit(new Color(0.47f, 0.52f, 0.6f), 0.15f);
            var hot = Mats.Lit(new Color(0.6f, 0.5f, 0.47f), 0.15f);
            for (int r = 0; r < Shift.Rows.Length; r++)
            {
                var a = RackPos(r, 1);
                var b = RackPos(r, Shift.RacksPerRow);
                float cx = (a.x + b.x) / 2f, len = b.x - a.x + RackView.Width;
                // Overhead cable tray and perforated cold-aisle tiles in front of the row.
                Mats.Box(root, new Vector3(cx, 2.55f, a.z + 0.2f), new Vector3(len, 0.05f, 0.4f), tray, false, true, "tray");
                Mats.Box(root, new Vector3(cx, 2.55f, a.z + 0.02f), new Vector3(len, 0.12f, 0.02f), tray, false, true, "tray lip");
                Mats.Box(root, new Vector3(cx, 0.002f, RowFrontZ(r) - 0.9f), new Vector3(len, 0.003f, 1.2f), cold, false, false, "cold aisle");
                // Hot aisle behind the row: the rear of every rack, where power and cabling jobs happen.
                Mats.Box(root, new Vector3(cx, 0.002f, RowFrontZ(r) + RackView.Depth + 0.7f), new Vector3(len, 0.003f, 1.2f), hot, false, false, "hot aisle");
                Mats.Text(root, new Vector3(cx, 2.75f, RowFrontZ(r) + RackView.Depth + 0.6f), $"HOT AISLE  ROW {Shift.Rows[r]}  (REAR)", 0.13f, new Color(1f, 0.6f, 0.4f), TextAnchor.MiddleCenter, 180f);
                // Row signs at both ends of the aisle.
                string name = $"ROW {Shift.Rows[r]}";
                float sz = RowFrontZ(r) - 1.2f;
                foreach (var (x, yaw) in new[] { (a.x - 0.8f, 90f), (b.x + 0.8f, -90f) })
                {
                    Mats.Box(root, new Vector3(x, 2.35f, sz), new Vector3(0.04f, 0.42f, 1.1f), Mats.Lit(new Color(0.08f, 0.1f, 0.13f)), false, false, "sign");
                    float off = yaw > 0 ? -0.025f : 0.025f;
                    Mats.Text(root, new Vector3(x + off, 2.35f, sz), $"{name}  {Shift.Rows[r]}01-{Shift.Rows[r]}{Shift.RacksPerRow:00}", 0.16f, new Color(0.5f, 0.85f, 1f), TextAnchor.MiddleCenter, yaw);
                }
            }
        }

        void BuildStorage()
        {
            var steel = Mats.Lit(new Color(0.3f, 0.36f, 0.45f), 0.3f, 0.5f);
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
                Mats.Box(t, new Vector3(-0.78f, 1f, 0), new Vector3(0.05f, 2f, 0.6f), steel);
                Mats.Box(t, new Vector3(0.78f, 1f, 0), new Vector3(0.05f, 2f, 0.6f), steel);
                for (int s = 0; s < 4; s++) Mats.Box(t, new Vector3(0, 0.15f + s * 0.55f, 0), new Vector3(1.6f, 0.04f, 0.6f), steel);
                var sh = g.AddComponent<StockShelf>();
                sh.def = def; sh.part = part; sh.isPart = isPart;
                shelves.Add(sh);
                var col = g.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 1f, 0);
                col.size = new Vector3(1.6f, 2f, 0.6f);
                Color c = isPart ? new Color(0.55f, 0.45f, 0.3f) : Catalog.Dev(def).color * 1.4f;
                var mat = Mats.Lit(c, 0.3f);
                for (int s = 0; s < 3; s++)
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 size = isPart ? new Vector3(0.3f, 0.12f, 0.25f) : new Vector3(0.45f, 0.045f * Catalog.Dev(def).heightU * 2f, 0.5f);
                        Mats.Box(t, new Vector3(-0.5f + k * 0.5f, 0.17f + s * 0.55f + size.y / 2, 0), size, mat);
                    }
                Mats.Text(t, new Vector3(0, 2.18f, -0.05f), isPart ? Catalog.PartName(part).ToUpperInvariant() : Catalog.Dev(def).name.ToUpperInvariant(), 0.12f, new Color(0.85f, 0.92f, 1f));
            }
            // Returns bin and the wipe & shred bench.
            var bin = Mats.Box(root, new Vector3(18.6f, 0.45f, 4f), new Vector3(1.2f, 0.9f, 0.8f), Mats.Lit(new Color(0.15f, 0.35f, 0.6f)), true, true, "returns");
            bin.AddComponent<ReturnBin>();
            Mats.Text(root, new Vector3(18.6f, 1.1f, 3.55f), "RETURNS", 0.14f, Color.white);
            var top = Mats.Box(root, new Vector3(15f, 0.85f, 4.1f), new Vector3(2.2f, 0.06f, 0.8f), Mats.Lit(new Color(0.45f, 0.35f, 0.25f)), true);
            var body = Mats.Box(root, new Vector3(15f, 0.41f, 4.1f), new Vector3(2.1f, 0.82f, 0.7f), steel, true);
            var shred = Mats.Box(root, new Vector3(15.65f, 1.1f, 4.15f), new Vector3(0.6f, 0.45f, 0.5f), Mats.Lit(new Color(0.5f, 0.12f, 0.08f), 0.3f, 0.3f), true, true, "shredder");
            AddStation(body, "bench", "Wipe & shred station");
            AddStation(shred, "bench", "Wipe & shred station");
            Mats.Box(root, new Vector3(14.6f, 0.92f, 4.1f), new Vector3(0.5f, 0.08f, 0.35f), Mats.Lit(new Color(0.2f, 0.22f, 0.25f)), false);
            AddStation(top, "bench", "Wipe & shred station");
            stations["bench"] = top.transform.position + Vector3.up * 0.9f;
            Mats.Text(root, new Vector3(15f, 1.65f, 3.7f), "WIPE & SHRED", 0.14f, new Color(1f, 0.7f, 0.6f));
            // The crash cart, parked by the bench.
            var cart = new GameObject("Crash cart");
            cart.transform.SetParent(root, false);
            cart.transform.localPosition = new Vector3(13.2f, 0f, 6.6f);
            cart.transform.localRotation = Quaternion.Euler(0, 90f, 0);
            var ct = cart.transform;
            var dark = Mats.Lit(new Color(0.15f, 0.16f, 0.18f), 0.3f, 0.4f);
            Mats.Box(ct, new Vector3(0, 0.08f, 0), new Vector3(0.7f, 0.06f, 0.5f), dark);
            Mats.Box(ct, new Vector3(0, 0.85f, 0), new Vector3(0.7f, 0.04f, 0.5f), dark);
            Mats.Box(ct, new Vector3(-0.32f, 0.5f, 0), new Vector3(0.04f, 0.9f, 0.04f), steel);
            Mats.Box(ct, new Vector3(0.32f, 0.5f, 0), new Vector3(0.04f, 0.9f, 0.04f), steel);
            Mats.Box(ct, new Vector3(0, 1.12f, 0.1f), new Vector3(0.5f, 0.34f, 0.04f), dark);
            Mats.Box(ct, new Vector3(0, 1.12f, 0.075f), new Vector3(0.45f, 0.28f, 0.01f), Mats.Glow(new Color(0.2f, 0.5f, 0.9f), 1.2f), false, false);
            Mats.Box(ct, new Vector3(0, 0.89f, -0.08f), new Vector3(0.45f, 0.02f, 0.16f), Mats.Lit(new Color(0.1f, 0.1f, 0.1f)));
            var cs = cart.AddComponent<StockShelf>();
            cs.def = "kvm";
            shelves.Add(cs);
            var ccol = cart.AddComponent<BoxCollider>();
            ccol.center = new Vector3(0, 0.65f, 0);
            ccol.size = new Vector3(0.7f, 1.3f, 0.5f);
            Mats.Text(ct, new Vector3(0, 1.45f, 0.05f), "CRASH CART (KVM)", 0.08f, new Color(0.85f, 0.92f, 1f), TextAnchor.MiddleCenter, 0f);
        }

        void AddStation(GameObject g, string id, string name)
        {
            var s = g.AddComponent<Station>();
            s.id = id;
            s.title = name;
        }

        /// <summary>The meet-me room cross-connect frame on the west wall, where carriers' panels meet the customers'.</summary>
        void BuildMeetMe()
        {
            var frame = Mats.Box(root, new Vector3(0.3f, 1.15f, 12.5f), new Vector3(0.3f, 2.2f, 2.6f), Mats.Lit(new Color(0.1f, 0.11f, 0.13f), 0.3f, 0.4f), true, true, "MMR frame");
            for (int i = 0; i < 9; i++)
            {
                Mats.Box(root, new Vector3(0.46f, 0.35f + i * 0.2f, 12.5f), new Vector3(0.02f, 0.12f, 2.4f), Mats.Lit(new Color(0.25f, 0.27f, 0.3f)), false, false);
                Mats.Box(root, new Vector3(0.47f, 0.35f + i * 0.2f, 12.5f), new Vector3(0.01f, 0.03f, 2.2f), Mats.Unlit(i % 3 == 0 ? new Color(0.95f, 0.78f, 0.15f) : new Color(0.2f, 0.6f, 0.3f)), false, false);
            }
            AddStation(frame, "mmr", "Meet-me room frame");
            stations["mmr"] = new Vector3(0.6f, 2.6f, 12.5f);
            Mats.Text(root, new Vector3(0.5f, 2.5f, 12.5f), "MEET-ME ROOM", 0.2f, new Color(0.95f, 0.8f, 0.3f), TextAnchor.MiddleCenter, -90f);
        }

        void BuildDock()
        {
            var pallet = Mats.Lit(new Color(0.55f, 0.42f, 0.25f));
            for (int i = 0; i < 6; i++)
            {
                var p = DeliverySpot(i);
                Mats.Box(root, new Vector3(p.x, 0.07f, p.z), new Vector3(1.1f, 0.14f, 1f), pallet, true, true, "pallet");
            }
            // Outbound pallet for decommissioned gear, outside the delivery lane.
            var outbound = Mats.Box(root, new Vector3(11.2f, 0.12f, 22f), new Vector3(1.3f, 0.24f, 1.2f), Mats.Lit(new Color(0.35f, 0.5f, 0.6f)), true, true, "outbound");
            AddStation(outbound, "dock", "Outbound pallet");
            stations["dock"] = new Vector3(11.2f, 0.9f, 22f);
            Mats.Box(root, new Vector3(11.2f, 1.2f, 22.7f), new Vector3(0.05f, 1.2f, 0.05f), Mats.Lit(new Color(0.4f, 0.4f, 0.42f)));
            Mats.Box(root, new Vector3(11.2f, 1.9f, 22.7f), new Vector3(1f, 0.3f, 0.03f), Mats.Lit(new Color(0.1f, 0.25f, 0.35f)));
            Mats.Text(root, new Vector3(11.2f, 1.9f, 22.68f), "OUTBOUND", 0.16f, Color.white, TextAnchor.MiddleCenter, 0f);
        }

        static Vector3 DeliverySpot(int i) => new Vector3(14f + (i % 3) * 2f, 0.14f, 21.2f + (i / 3) * 1.5f);

        void BuildNoc()
        {
            var desk = Mats.Lit(new Color(0.2f, 0.22f, 0.26f), 0.3f);
            Mats.Box(root, new Vector3(1.8f, 0.75f, 2.6f), new Vector3(1.0f, 0.05f, 2.4f), desk, true);
            Mats.Box(root, new Vector3(1.8f, 0.37f, 2.6f), new Vector3(0.9f, 0.74f, 2.3f), Mats.Lit(new Color(0.12f, 0.13f, 0.16f)), true);
            var screen = Mats.Box(root, new Vector3(1.55f, 1.1f, 2.6f), new Vector3(0.05f, 0.5f, 0.9f), Mats.Glow(new Color(0.2f, 0.55f, 1f), 1.4f), true, false, "NOC screen");
            screen.AddComponent<Terminal>();
            Mats.Text(root, new Vector3(1.6f, 1.45f, 2.6f), "NOC  TICKETS", 0.1f, new Color(0.6f, 0.85f, 1f), TextAnchor.MiddleCenter, -90f);
        }

        // ================================================================ dynamic objects
        public void Refresh(Shift shift, string label)
        {
            if (label != null && racks.TryGetValue(label, out var v)) v.Rebuild(shift.Rack(label), shift);
        }

        public void RefreshAll(Shift shift)
        {
            foreach (var r in shift.racks) Refresh(shift, r.label);
        }

        public void SpawnDeliveries(Shift shift)
        {
            foreach (var it in shift.deliveries) SpawnItem(it, DeliverySpot(deliverySlot++ % 6) + new Vector3(0, 0.25f * (deliverySlot / 6), 0), true);
            shift.deliveries.Clear();
        }

        public Pickup SpawnItem(Item it, Vector3 pos, bool boxed)
        {
            var def = it.Def;
            float h = Mathf.Max(0.12f, def.heightU * 0.06f + (boxed ? 0.1f : 0f));
            var g = Mats.Box(dynamic, pos + new Vector3(0, h / 2, 0), new Vector3(boxed ? 0.75f : 0.55f, h, boxed ? 0.9f : 0.8f),
                boxed ? Mats.Lit(new Color(0.7f, 0.55f, 0.35f)) : Mats.Lit(def.color * 1.3f, 0.35f, 0.3f), true, true, "item");
            if (boxed)
            {
                Mats.Box(g.transform, new Vector3(0, 0.51f, 0), new Vector3(0.15f, 0.02f, 1.01f), Mats.Lit(new Color(0.85f, 0.75f, 0.55f)));
                Mats.Box(g.transform, new Vector3(0.3f, 0.51f, 0.3f), new Vector3(0.25f, 0.02f, 0.25f), Mats.Unlit(Catalog.CustColor(it.label.Split(" - ")[0])));
            }
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
            racks.TryGetValue(label, out var v) ? v.transform.position + new Vector3(0, RackView.Height + 0.25f, rear ? 0.7f : -0.7f) : Vector3.zero;

        /// <summary>Racks face -Z (the cold aisle); anything at a larger Z is behind them, in the hot aisle.</summary>
        public bool IsBehind(string label, Vector3 pos) => racks.TryGetValue(label, out var v) && pos.z > v.transform.position.z;

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
                if (pos.z > rear && pos.z < rear + 2f && pos.x > RackPos(r, 1).x - 1f && pos.x < RackPos(r, Shift.RacksPerRow).x + 1f) level += 0.15f;
            }
            return Mathf.Clamp01(level);
        }
    }
}
