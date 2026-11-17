using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Prompt(Game g);
        public abstract void Use(Game g);
    }

    /// <summary>A rack in the hall. Rebuilds its installed equipment from the shift model.</summary>
    public class RackView : Interactable
    {
        public const float Width = 0.6f, Depth = 1.1f, Height = 2.05f, UnitH = 0.0445f, Bottom = 0.1f;
        public string label;
        Transform content;
        GameObject beacon;
        readonly List<Renderer> blinkers = new List<Renderer>();

        public static float UY(int u) => Bottom + (u - 1) * UnitH;

        public void Init(string label)
        {
            this.label = label;
            var t = transform;
            var frame = Mats.Lit(new Color(0.06f, 0.065f, 0.075f), 0.35f, 0.3f);
            var post = Mats.Lit(new Color(0.45f, 0.47f, 0.5f), 0.5f, 0.7f);
            Mats.Box(t, new Vector3(-Width / 2 + 0.01f, Height / 2, 0), new Vector3(0.02f, Height, Depth), frame);
            Mats.Box(t, new Vector3(Width / 2 - 0.01f, Height / 2, 0), new Vector3(0.02f, Height, Depth), frame);
            Mats.Box(t, new Vector3(0, Height - 0.01f, 0), new Vector3(Width, 0.02f, Depth), frame);
            Mats.Box(t, new Vector3(0, 0.05f, 0), new Vector3(Width, 0.1f, Depth), frame);
            // The rear is open: posts, a red A-feed PDU and a blue B-feed PDU down the back corners.
            foreach (float x in new[] { -0.255f, 0.255f })
            {
                Mats.Box(t, new Vector3(x, Height / 2, -Depth / 2 + 0.04f), new Vector3(0.025f, Height - 0.1f, 0.02f), post);
                Mats.Box(t, new Vector3(x, Height / 2, Depth / 2 - 0.04f), new Vector3(0.025f, Height - 0.1f, 0.02f), post);
            }
            Mats.Box(t, new Vector3(-0.22f, 1.05f, Depth / 2 - 0.07f), new Vector3(0.05f, 1.8f, 0.05f), Mats.Lit(new Color(0.55f, 0.12f, 0.1f), 0.3f, 0.3f), false, true, "PDU A");
            Mats.Box(t, new Vector3(0.22f, 1.05f, Depth / 2 - 0.07f), new Vector3(0.05f, 1.8f, 0.05f), Mats.Lit(new Color(0.12f, 0.2f, 0.55f), 0.3f, 0.3f), false, true, "PDU B");
            Mats.Text(t, new Vector3(0, Height + 0.09f, Depth / 2 - 0.05f), label + "  REAR", 0.09f, new Color(1f, 0.75f, 0.6f), TextAnchor.MiddleCenter, 180f);
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, Height / 2, 0);
            col.size = new Vector3(Width, Height, Depth);
            Mats.Text(t, new Vector3(0, Height + 0.09f, -Depth / 2 + 0.05f), label, 0.11f, new Color(0.85f, 0.9f, 0.95f));
            beacon = Mats.Box(t, new Vector3(0, Height + 0.02f, 0.2f), new Vector3(0.18f, 0.04f, 0.18f), Mats.Glow(new Color(1f, 0.65f, 0.1f), 3f), false, false, "beacon");
            beacon.SetActive(false);
        }

        public void Rebuild(Rack rack, Shift shift)
        {
            if (content != null) Destroy(content.gameObject);
            blinkers.Clear();
            content = new GameObject("content").transform;
            content.SetParent(transform, false);
            float front = -Depth / 2 + 0.05f;
            foreach (var d in rack.devices)
            {
                var def = d.Def;
                float h = def.heightU * UnitH - 0.004f;
                float y = UY(d.u) + h / 2 + 0.002f;
                var body = Mats.Lit(def.color, 0.35f, 0.3f);
                Mats.Box(content, new Vector3(0, y, front + 0.45f), new Vector3(0.53f, h, 0.9f), body, false, true, "dev");
                Mats.Box(content, new Vector3(0, y, front - 0.005f), new Vector3(0.48f, h * 0.86f, 0.012f), Mats.Lit(def.color * 1.6f, 0.5f, 0.2f), false, false);
                Mats.Box(content, new Vector3(-0.245f, y, front - 0.008f), new Vector3(0.012f, h * 0.9f, 0.014f), Mats.Unlit(Catalog.CustColor(d.owner)), false, false);
                if (def.bays > 0)
                {
                    int cols = Mathf.Min(def.bays, def.heightU == 1 ? 4 : 6);
                    for (int i = 0; i < cols; i++)
                    {
                        float bx = -0.17f + i * (0.3f / Mathf.Max(1, cols - 1));
                        bool bad = d.failedBay >= 0 && d.failedBay % cols == i;
                        var led = Mats.Box(content, new Vector3(bx, y - h * 0.3f, front - 0.014f), new Vector3(0.008f, 0.008f, 0.004f),
                            bad ? Mats.Unlit(new Color(1f, 0.6f, 0.05f)) : Mats.Unlit(new Color(0.2f, 0.9f, 0.3f)), false, false);
                        if (bad) blinkers.Add(led.GetComponent<Renderer>());
                    }
                }
                bool off = d.fault == Fault.NoPower;
                bool alarm = !off && (d.failedPsu >= 0 || d.fault != Fault.None);
                var pwr = Mats.Box(content, new Vector3(0.21f, y, front - 0.014f), new Vector3(0.012f, 0.012f, 0.004f),
                    off ? Mats.Unlit(new Color(0.05f, 0.05f, 0.05f)) : alarm ? Mats.Unlit(new Color(1f, 0.15f, 0.1f)) : Mats.Unlit(new Color(0.3f, 0.6f, 1f)), false, false);
                if (alarm) blinkers.Add(pwr.GetComponent<Renderer>());
                BuildRear(d, y, h, front + 0.9f);
            }
            if (rack.devices.Count > 0)
            {
                // Data cables gathered into a bundle running up to the overhead tray.
                float lowY = UY(rack.devices.Min(d => d.u));
                Mats.Box(content, new Vector3(-0.15f, (lowY + Height) / 2f, Depth / 2 - 0.1f), new Vector3(0.05f, Height - lowY, 0.04f), Mats.Lit(new Color(0.2f, 0.45f, 0.85f), 0.3f), false, false, "cable bundle");
            }
            // Ghost markers where open tickets want equipment installed.
            foreach (var (ticket, task) in shift.TasksAt(rack.label))
            {
                if (task.kind != TaskKind.Mount || task.done) continue;
                var def = Catalog.Dev(task.def);
                float h = def.heightU * UnitH;
                Mats.Box(content, new Vector3(0, UY(task.u) + h / 2, front + 0.1f), new Vector3(0.5f, h - 0.004f, 0.2f), Mats.Glass(new Color(1f, 0.7f, 0.15f, 0.35f)), false, false);
            }
            beacon.SetActive(shift.TasksAt(rack.label).Any(x => !x.task.done));
        }

        /// <summary>The back of a unit: its PSUs with status lights, a port block and the two power cords.</summary>
        void BuildRear(Device d, float y, float h, float back)
        {
            float z = back + 0.006f;
            Mats.Box(content, new Vector3(0, y, z), new Vector3(0.5f, h * 0.86f, 0.01f), Mats.Lit(d.Def.color * 0.8f, 0.3f, 0.3f), false, false);
            if (d.Def.kind == DevKind.Switch || d.Def.ports.Length > 3)
                Mats.Box(content, new Vector3(-0.08f, y, z + 0.008f), new Vector3(0.16f, Mathf.Min(h * 0.6f, 0.03f), 0.008f), Mats.Lit(new Color(0.05f, 0.05f, 0.06f)), false, false);
            for (int i = 0; i < d.Def.psus; i++)
            {
                float px = 0.1f + i * 0.09f;
                float ph = Mathf.Min(h * 0.75f, 0.035f);
                Mats.Box(content, new Vector3(px, y, z + 0.008f), new Vector3(0.075f, ph, 0.008f), Mats.Lit(new Color(0.3f, 0.31f, 0.33f), 0.4f, 0.5f), false, false);
                bool failed = d.failedPsu == i;
                bool off = d.fault == Fault.NoPower;
                var led = Mats.Box(content, new Vector3(px + 0.028f, y, z + 0.014f), new Vector3(0.008f, 0.008f, 0.004f),
                    Mats.Unlit(off ? new Color(0.05f, 0.05f, 0.05f) : failed ? new Color(1f, 0.15f, 0.1f) : new Color(0.2f, 0.9f, 0.3f)), false, false);
                if (failed) blinkers.Add(led.GetComponent<Renderer>());
                // Cord to the PDU on its side: PSU 1 to the red A feed, PSU 2 to the blue B feed.
                float pdx = i == 0 ? -0.22f : 0.22f;
                if (!failed)
                    Mats.Box(content, new Vector3((px + pdx) / 2f, y - 0.006f, Depth / 2 - 0.07f), new Vector3(Mathf.Abs(px - pdx), 0.008f, 0.008f),
                        Mats.Lit(i == 0 ? new Color(0.6f, 0.15f, 0.12f) : new Color(0.15f, 0.25f, 0.6f)), false, false);
            }
        }

        void Update()
        {
            bool on = Mathf.Repeat(Time.time * 2.2f, 1f) < 0.55f;
            foreach (var r in blinkers) if (r != null) r.enabled = on;
            if (beacon.activeSelf) beacon.transform.localScale = new Vector3(0.18f, 0.04f + 0.02f * Mathf.Sin(Time.time * 6f), 0.18f);
        }

        public override string Prompt(Game g)
        {
            var rack = g.shift.Rack(label);
            string owner = rack.owner != null ? rack.owner : "vacant";
            bool rear = g.facility.IsBehind(label, g.player.transform.position);
            var open = g.shift.TasksAt(label).Where(x => !x.task.done && x.task.kind != TaskKind.Photo).ToList();
            int here = open.Count(x => Shift.RearJob(x.task.kind) == rear);
            int other = open.Count - here;
            string side = rear ? "rear" : "front";
            return $"[{Keys.Label(Keys.Action.Interact)}] Work on rack {label} {side} ({owner})" +
                   (here > 0 ? $"  -  {here} task{(here > 1 ? "s" : "")} here" : "") +
                   (other > 0 ? $"  -  {other} on the {(rear ? "front" : "rear")}" : "");
        }

        public override void Use(Game g) => g.OpenRack(label);
    }

    /// <summary>Storage-room stock: equipment or spare parts, never runs out.</summary>
    public class StockShelf : Interactable
    {
        public string def;
        public PartKind part;
        public bool isPart;

        public string ItemName => isPart ? Catalog.PartName(part) : Catalog.Dev(def).name;

        public override string Prompt(Game g)
        {
            string e = Keys.Label(Keys.Action.Interact);
            if (isPart) return g.shift.inv.PouchFull ? $"Tool pouch full ({g.shift.inv.pouchCap})" : $"[{e}] Take a {ItemName}";
            return g.shift.inv.HandsFull ? "Your hands are full. Return or install what you're carrying first" : $"[{e}] Take the {ItemName}";
        }

        public override void Use(Game g) => g.TakeStock(this);
    }

    /// <summary>Equipment lying in the world: customer deliveries or something the player put down.</summary>
    public class Pickup : Interactable
    {
        public Item item;
        public override string Prompt(Game g) => g.shift.inv.HandsFull ? "Your hands are full" : $"[{Keys.Label(Keys.Action.Interact)}] Pick up {item.label}";
        public override void Use(Game g) => g.PickUp(this);
    }

    public class ReturnBin : Interactable
    {
        public override string Prompt(Game g)
        {
            var inv = g.shift.inv;
            string e = Keys.Label(Keys.Action.Interact);
            if (inv.hands.Count > 0)
            {
                var it = inv.hands[inv.hands.Count - 1];
                if (it.CustomerGear) return "Customer equipment can't go back into stock";
                if (it.Decom) return "Decommissioned units go to the wipe station, then the outbound pallet";
                return $"[{e}] Return {it.Def.name} to stock";
            }
            if (inv.pouch.Count > 0) return $"[{e}] Return {Catalog.PartName(inv.pouch[inv.pouch.Count - 1])} to stock";
            return "Returns bin (nothing to return)";
        }

        public override void Use(Game g) => g.ReturnItem();
    }

    public class Terminal : Interactable
    {
        public override string Prompt(Game g) => $"[{Keys.Label(Keys.Action.Interact)}] " + (g.shift.AllDone ? "Work order complete: clock out" : "Open the ticket queue");
        public override void Use(Game g) => g.OpenTablet();
    }

    /// <summary>A place jobs happen away from the racks: the wipe bench, the outbound pallet, the meet-me room.</summary>
    public class Station : Interactable
    {
        public string id, title;

        public override string Prompt(Game g)
        {
            int open = g.shift.TasksAtStation(id).Count(x => !x.task.done);
            return $"[{Keys.Label(Keys.Action.Interact)}] {title}" + (open > 0 ? $"  -  {open} task{(open > 1 ? "s" : "")} here" : "");
        }

        public override void Use(Game g) => g.OpenStation(id);
    }
}
