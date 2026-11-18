using System.Collections.Generic;
using UnityEngine;

namespace RH
{
    public enum DevKind { Server, Switch, Storage, Customer, Tool }
    public enum PartKind { Drive, Psu, Sfp, Dimm, Fan }

    /// <summary>A piece of rack-mounted equipment (or a tool the technician wheels around).</summary>
    public class DevDef
    {
        public string id, name, model;
        public DevKind kind;
        public int heightU, bays, psus = 2, sfps, watts;
        public Color color;
        /// <summary>Connectors on the back of the unit that get cabled during install.</summary>
        public string[] ports;
    }

    public class Customer
    {
        public string name;
        public Color color;
    }

    public class UpgradeDef
    {
        public string id, name, desc;
        public int cost;
        /// <summary>Technician level needed before the shop sells it.</summary>
        public int level = 1;
        /// <summary>Upgrade that must be owned first (lower tier or the matching certification).</summary>
        public string requires;
        /// <summary>Certifications unlock ticket types rather than tools.</summary>
        public bool cert;
    }

    public static class Catalog
    {
        public static readonly List<DevDef> Devices = new List<DevDef>
        {
            new DevDef { id = "srv1u", name = "1U Compute Server", model = "RX-110", kind = DevKind.Server, heightU = 1, bays = 4, sfps = 2, watts = 380,
                color = new Color(0.17f, 0.19f, 0.23f), ports = new[] { "eth0", "eth1", "mgmt", "PSU A", "PSU B" } },
            new DevDef { id = "srv2u", name = "2U Storage Server", model = "RX-220", kind = DevKind.Server, heightU = 2, bays = 12, sfps = 2, watts = 520,
                color = new Color(0.14f, 0.16f, 0.2f), ports = new[] { "eth0", "eth1", "mgmt", "PSU A", "PSU B" } },
            new DevDef { id = "sw48", name = "48-port ToR Switch", model = "NX-4810", kind = DevKind.Switch, heightU = 1, bays = 0, sfps = 6, watts = 160,
                color = new Color(0.1f, 0.24f, 0.32f), ports = new[] { "uplink1", "uplink2", "mgmt", "PSU A", "PSU B" } },
            new DevDef { id = "ds4u", name = "4U Disk Shelf", model = "DS-460", kind = DevKind.Storage, heightU = 4, bays = 24, sfps = 0, watts = 640,
                color = new Color(0.22f, 0.22f, 0.24f), ports = new[] { "SAS A", "SAS B", "PSU A", "PSU B" } },
            new DevDef { id = "cust1u", name = "Customer 1U Appliance", model = "CX-1", kind = DevKind.Customer, heightU = 1, bays = 0, sfps = 0, watts = 300,
                color = new Color(0.3f, 0.26f, 0.2f), ports = new[] { "net0", "net1", "PSU A", "PSU B" } },
            new DevDef { id = "cust2u", name = "Customer 2U Server", model = "CX-2", kind = DevKind.Customer, heightU = 2, bays = 8, sfps = 0, watts = 480,
                color = new Color(0.26f, 0.2f, 0.26f), ports = new[] { "net0", "net1", "ipmi", "PSU A", "PSU B" } },
            new DevDef { id = "cust4u", name = "Customer 4U Storage", model = "CX-4", kind = DevKind.Customer, heightU = 4, bays = 24, sfps = 0, watts = 700,
                color = new Color(0.2f, 0.26f, 0.24f), ports = new[] { "net0", "SAS", "PSU A", "PSU B" } },
            new DevDef { id = "kvm", name = "Crash Cart (KVM)", model = "KVM-CART", kind = DevKind.Tool, heightU = 6, psus = 0, watts = 0,
                color = new Color(0.35f, 0.38f, 0.42f), ports = new string[0] },
        };

        static Dictionary<string, DevDef> map;
        public static DevDef Dev(string id)
        {
            if (map == null) { map = new Dictionary<string, DevDef>(); foreach (var d in Devices) map[d.id] = d; }
            return id != null && map.TryGetValue(id, out var v) ? v : null;
        }

        /// <summary>Equipment the storage room keeps in stock (the crash cart is parked by the bench).</summary>
        public static readonly string[] Stocked = { "srv1u", "srv2u", "sw48" };
        public static readonly PartKind[] Parts = { PartKind.Drive, PartKind.Psu, PartKind.Sfp, PartKind.Dimm, PartKind.Fan };

        public static string PartName(PartKind k) => k switch
        {
            PartKind.Drive => "Spare drive",
            PartKind.Psu => "800W PSU",
            PartKind.Sfp => "10G SFP+ optic",
            PartKind.Dimm => "32GB DDR5 DIMM",
            _ => "Hot-swap fan",
        };

        public static readonly Customer[] Customers =
        {
            new Customer { name = "Helix Genomics", color = new Color(0.35f, 0.8f, 0.45f) },
            new Customer { name = "Northwind Bank", color = new Color(0.3f, 0.55f, 1f) },
            new Customer { name = "Bluefin Media", color = new Color(0.2f, 0.85f, 0.9f) },
            new Customer { name = "Quanta Retail", color = new Color(1f, 0.6f, 0.2f) },
            new Customer { name = "Orbital Games", color = new Color(0.75f, 0.45f, 1f) },
            new Customer { name = "Meridian Health", color = new Color(1f, 0.4f, 0.5f) },
            new Customer { name = "Copperline ISP", color = new Color(0.85f, 0.65f, 0.35f) },
            new Customer { name = "Atlas Logistics", color = new Color(0.95f, 0.9f, 0.3f) },
            new Customer { name = "Vantage AI", color = new Color(0.4f, 1f, 0.75f) },
            new Customer { name = "Kestrel Insurance", color = new Color(0.6f, 0.7f, 0.85f) },
        };

        /// <summary>Network carriers with panels in the meet-me room.</summary>
        public static readonly string[] Carriers = { "Northstar Fiber", "Bluewave Transit", "Corelink Carriers" };

        public static Customer Cust(string name)
        {
            foreach (var c in Customers) if (c.name == name) return c;
            return null;
        }

        public static Color CustColor(string name) => Cust(name)?.color ?? new Color(0.5f, 0.5f, 0.5f);

        /// <summary>The customer's hostname on a device's asset tag, e.g. "hg-db-42".</summary>
        public static string Hostname(Device d)
        {
            string prefix = "dc";
            if (!string.IsNullOrEmpty(d.owner))
            {
                var words = d.owner.Split(' ');
                prefix = (words[0].Substring(0, 1) + (words.Length > 1 ? words[1].Substring(0, 1) : words[0].Substring(1, 1))).ToLowerInvariant();
            }
            string role = d.Def.kind switch
            {
                DevKind.Switch => "tor",
                DevKind.Storage => "nas",
                DevKind.Customer => "cx",
                _ => new[] { "app", "db", "web" }[d.id % 3],
            };
            return $"{prefix}-{role}-{d.id % 90 + 10:00}";
        }

        public static readonly string[] Components = { "BIOS", "BMC", "NIC" };

        /// <summary>
        /// Where drive bay i sits on a unit's front face (centre and size in metres, x from the unit's
        /// centre, y from its bottom). The Blender models use the same grid.
        /// </summary>
        public static void Bay(DevDef def, int i, out Vector2 center, out Vector2 size)
        {
            int cols = def.bays <= 4 ? 4 : def.bays <= 12 ? 6 : 12;
            int rows = Mathf.CeilToInt(def.bays / (float)cols);
            float h = def.heightU * 0.0445f, w = 0.42f / cols, hh = (h - 0.006f) / rows;
            int r = i / cols, c = i % cols;
            center = new Vector2(-0.21f + w * (c + 0.5f), 0.003f + hh * (rows - 1 - r + 0.5f));
            size = new Vector2(w - 0.004f, hh - 0.003f);
        }

        public static readonly UpgradeDef[] Upgrades =
        {
            new UpgradeDef { id = "shoes", name = "Running Shoes", cost = 250, desc = "Walk and sprint 25% faster." },
            new UpgradeDef { id = "shoes2", name = "Carbon Running Shoes", cost = 600, level = 3, requires = "shoes", desc = "Walk and sprint 45% faster." },
            new UpgradeDef { id = "belt", name = "Tool Belt", cost = 200, desc = "Carry 6 small parts instead of 4." },
            new UpgradeDef { id = "belt2", name = "Utility Vest", cost = 500, level = 2, requires = "belt", desc = "Carry 9 small parts." },
            new UpgradeDef { id = "cart", name = "Equipment Cart", cost = 600, desc = "Carry two pieces of rack equipment at once." },
            new UpgradeDef { id = "cart2", name = "Flatbed Cart", cost = 1400, level = 4, requires = "cart", desc = "Carry three pieces of rack equipment at once." },
            new UpgradeDef { id = "driver", name = "Magnetic Screwdriver", cost = 350, desc = "Rack screws are much easier to land." },
            new UpgradeDef { id = "driver2", name = "Electric Screwdriver", cost = 800, level = 3, requires = "driver", desc = "Rack screws are almost impossible to miss." },
            new UpgradeDef { id = "tester", name = "Cable Tester", cost = 300, desc = "Highlights the right port while you hold a cable." },
            new UpgradeDef { id = "scanner", name = "Log Scanner", cost = 450, desc = "Highlights the log lines that matter when diagnosing." },
            new UpgradeDef { id = "usb", name = "Firmware USB Kit", cost = 400, desc = "Only shows images built for the device you're flashing." },
            new UpgradeDef { id = "cert_power", name = "Electrical Safety Cert", cost = 700, level = 2, cert = true, desc = "Unlocks PDU rebalancing tickets: restore A+B power without dropping anything." },
            new UpgradeDef { id = "clamp", name = "Clamp Meter", cost = 400, level = 2, requires = "cert_power", desc = "Shows the current on every power cord in PDU jobs." },
            new UpgradeDef { id = "cert_fiber", name = "Fiber & Cross-Connect Cert", cost = 900, level = 3, cert = true, desc = "Unlocks cross-connect tickets: trace a cable, then patch it to a carrier in the meet-me room." },
            new UpgradeDef { id = "toner", name = "Cable Toner", cost = 350, level = 3, requires = "cert_fiber", desc = "The cable you're tracing pulses so it's easy to follow." },
        };

        public static UpgradeDef Upgrade(string id) => System.Array.Find(Upgrades, u => u.id == id);

        // ================================================================ technician levels
        public static readonly int[] LevelXp = { 0, 120, 300, 560, 900, 1350 };
        public static readonly string[] LevelTitles = { "Trainee", "Technician", "Senior Technician", "Lead Technician", "Site Specialist", "Site Lead" };
    }
}
