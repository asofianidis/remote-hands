using System.Collections.Generic;
using UnityEngine;

namespace RH
{
    public enum DevKind { Server, Switch, Storage, Customer }
    public enum PartKind { Drive, Psu, Sfp }

    /// <summary>A piece of rack-mounted equipment.</summary>
    public class DevDef
    {
        public string id, name, model;
        public DevKind kind;
        public int heightU, bays, psus = 2, sfps;
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
    }

    public static class Catalog
    {
        public static readonly List<DevDef> Devices = new List<DevDef>
        {
            new DevDef { id = "srv1u", name = "1U Compute Server", model = "RX-110", kind = DevKind.Server, heightU = 1, bays = 4, sfps = 2,
                color = new Color(0.17f, 0.19f, 0.23f), ports = new[] { "eth0", "eth1", "mgmt", "PSU A", "PSU B" } },
            new DevDef { id = "srv2u", name = "2U Storage Server", model = "RX-220", kind = DevKind.Server, heightU = 2, bays = 12, sfps = 2,
                color = new Color(0.14f, 0.16f, 0.2f), ports = new[] { "eth0", "eth1", "mgmt", "PSU A", "PSU B" } },
            new DevDef { id = "sw48", name = "48-port ToR Switch", model = "NX-4810", kind = DevKind.Switch, heightU = 1, bays = 0, sfps = 6,
                color = new Color(0.1f, 0.24f, 0.32f), ports = new[] { "uplink1", "uplink2", "mgmt", "PSU A", "PSU B" } },
            new DevDef { id = "ds4u", name = "4U Disk Shelf", model = "DS-460", kind = DevKind.Storage, heightU = 4, bays = 24, sfps = 0,
                color = new Color(0.22f, 0.22f, 0.24f), ports = new[] { "SAS A", "SAS B", "PSU A", "PSU B" } },
            new DevDef { id = "cust1u", name = "Customer 1U Appliance", model = "CX-1", kind = DevKind.Customer, heightU = 1, bays = 0, sfps = 0,
                color = new Color(0.3f, 0.26f, 0.2f), ports = new[] { "net0", "net1", "PSU A", "PSU B" } },
            new DevDef { id = "cust2u", name = "Customer 2U Server", model = "CX-2", kind = DevKind.Customer, heightU = 2, bays = 8, sfps = 0,
                color = new Color(0.26f, 0.2f, 0.26f), ports = new[] { "net0", "net1", "ipmi", "PSU A", "PSU B" } },
            new DevDef { id = "cust4u", name = "Customer 4U Storage", model = "CX-4", kind = DevKind.Customer, heightU = 4, bays = 24, sfps = 0,
                color = new Color(0.2f, 0.26f, 0.24f), ports = new[] { "net0", "SAS", "PSU A", "PSU B" } },
        };

        static Dictionary<string, DevDef> map;
        public static DevDef Dev(string id)
        {
            if (map == null) { map = new Dictionary<string, DevDef>(); foreach (var d in Devices) map[d.id] = d; }
            return id != null && map.TryGetValue(id, out var v) ? v : null;
        }

        /// <summary>Equipment the storage room keeps in stock.</summary>
        public static readonly string[] Stocked = { "srv1u", "srv2u", "sw48" };

        public static string PartName(PartKind k) => k switch
        {
            PartKind.Drive => "Spare drive",
            PartKind.Psu => "800W PSU",
            _ => "10G SFP+ optic",
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

        public static Customer Cust(string name)
        {
            foreach (var c in Customers) if (c.name == name) return c;
            return null;
        }

        public static Color CustColor(string name) => Cust(name)?.color ?? new Color(0.5f, 0.5f, 0.5f);

        public static readonly string[] Components = { "BIOS", "BMC", "NIC" };

        public static readonly UpgradeDef[] Upgrades =
        {
            new UpgradeDef { id = "shoes", name = "Running Shoes", cost = 250, desc = "Walk and sprint 25% faster." },
            new UpgradeDef { id = "belt", name = "Tool Belt", cost = 200, desc = "Carry 6 small parts instead of 4." },
            new UpgradeDef { id = "cart", name = "Equipment Cart", cost = 600, desc = "Carry two pieces of rack equipment at once." },
            new UpgradeDef { id = "driver", name = "Magnetic Screwdriver", cost = 350, desc = "Rack screws are much easier to land." },
            new UpgradeDef { id = "tester", name = "Cable Tester", cost = 300, desc = "Highlights the right port while you hold a cable." },
            new UpgradeDef { id = "scanner", name = "Log Scanner", cost = 450, desc = "Highlights the log lines that matter when diagnosing." },
            new UpgradeDef { id = "usb", name = "Firmware USB Kit", cost = 400, desc = "Only shows images built for the device you're flashing." },
        };
    }
}
