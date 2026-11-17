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
            Mats.Box(t, new Vector3(0, Height / 2, Depth / 2 - 0.01f), new Vector3(Width, Height, 0.02f), Mats.Lit(new Color(0.03f, 0.03f, 0.035f)));
            foreach (float x in new[] { -0.255f, 0.255f })
                Mats.Box(t, new Vector3(x, Height / 2, -Depth / 2 + 0.04f), new Vector3(0.025f, Height - 0.1f, 0.02f), post);
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
                bool alarm = d.failedPsu >= 0 || d.fault != Fault.None;
                var pwr = Mats.Box(content, new Vector3(0.21f, y, front - 0.014f), new Vector3(0.012f, 0.012f, 0.004f),
                    alarm ? Mats.Unlit(new Color(1f, 0.15f, 0.1f)) : Mats.Unlit(new Color(0.3f, 0.6f, 1f)), false, false);
                if (alarm) blinkers.Add(pwr.GetComponent<Renderer>());
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
            int open = g.shift.TasksAt(label).Count(x => !x.task.done);
            return $"[E] Work on rack {label} ({owner})" + (open > 0 ? $"  -  {open} task{(open > 1 ? "s" : "")} here" : "");
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
            if (isPart) return g.shift.inv.PouchFull ? $"Tool pouch full ({g.shift.inv.pouchCap})" : $"[E] Take a {ItemName}";
            return g.shift.inv.HandsFull ? "Your hands are full. Return or install what you're carrying first" : $"[E] Take a {ItemName}";
        }

        public override void Use(Game g) => g.TakeStock(this);
    }

    /// <summary>Equipment lying in the world: customer deliveries or something the player put down.</summary>
    public class Pickup : Interactable
    {
        public Item item;
        public override string Prompt(Game g) => g.shift.inv.HandsFull ? "Your hands are full" : $"[E] Pick up {item.label}";
        public override void Use(Game g) => g.PickUp(this);
    }

    public class ReturnBin : Interactable
    {
        public override string Prompt(Game g)
        {
            var inv = g.shift.inv;
            if (inv.hands.Count > 0) return inv.hands[inv.hands.Count - 1].tag != null ? "Customer equipment can't go back into stock" : $"[E] Return {inv.hands[inv.hands.Count - 1].Def.name} to stock";
            if (inv.pouch.Count > 0) return $"[E] Return {Catalog.PartName(inv.pouch[inv.pouch.Count - 1])} to stock";
            return "Returns bin (nothing to return)";
        }

        public override void Use(Game g) => g.ReturnItem();
    }

    public class Terminal : Interactable
    {
        public override string Prompt(Game g) => g.shift.AllDone ? "[E] Work order complete: clock out" : "[E] Open the ticket queue";
        public override void Use(Game g) => g.OpenTablet();
    }
}
