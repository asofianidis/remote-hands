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

    /// <summary>What makes a customer different to work for. See Catalog.TraitName/TraitDesc.</summary>
    public enum Trait { None, Generous, Evidence, StorageHeavy, Fickle, Urgent, Compliance, FiberHungry, NightAverse, GpuFarm, Strict }

    public class Customer
    {
        public string name;
        public Color color;
        public Trait trait;
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
            new DevDef { id = "gpu4u", name = "4U GPU Server", model = "GX-480", kind = DevKind.Server, heightU = 4, bays = 8, sfps = 2, watts = 2400,
                color = new Color(0.13f, 0.17f, 0.15f), ports = new[] { "eth0", "eth1", "mgmt", "PSU A", "PSU B" } },
            new DevDef { id = "tl4u", name = "4U Tape Library", model = "TL-40", kind = DevKind.Storage, heightU = 4, bays = 0, sfps = 0, watts = 180,
                color = new Color(0.24f, 0.24f, 0.22f), ports = new[] { "SAS A", "mgmt", "PSU A", "PSU B" } },
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

        /// <summary>Spare parts the technician orders; what the procurement desk charges for one.</summary>
        public static int PartPrice(PartKind k) => k switch
        {
            PartKind.Drive => 60,
            PartKind.Psu => 90,
            PartKind.Sfp => 45,
            PartKind.Dimm => 70,
            _ => 35,
        };

        /// <summary>Stock the storage room starts a career with.</summary>
        public static int StartStock(PartKind k) => k switch
        {
            PartKind.Drive => 6,
            PartKind.Psu => 3,
            _ => 4,
        };

        /// <summary>Share of a part's price the vendor credits for a failed part sent back under RMA.</summary>
        public const float RmaCredit = 0.5f;

        public static readonly Customer[] Customers =
        {
            new Customer { name = "Helix Genomics", color = new Color(0.35f, 0.8f, 0.45f), trait = Trait.Generous },
            new Customer { name = "Northwind Bank", color = new Color(0.3f, 0.55f, 1f), trait = Trait.Evidence },
            new Customer { name = "Bluefin Media", color = new Color(0.2f, 0.85f, 0.9f), trait = Trait.StorageHeavy },
            new Customer { name = "Quanta Retail", color = new Color(1f, 0.6f, 0.2f), trait = Trait.Fickle },
            new Customer { name = "Orbital Games", color = new Color(0.75f, 0.45f, 1f), trait = Trait.Urgent },
            new Customer { name = "Meridian Health", color = new Color(1f, 0.4f, 0.5f), trait = Trait.Compliance },
            new Customer { name = "Copperline ISP", color = new Color(0.85f, 0.65f, 0.35f), trait = Trait.FiberHungry },
            new Customer { name = "Atlas Logistics", color = new Color(0.95f, 0.9f, 0.3f), trait = Trait.NightAverse },
            new Customer { name = "Vantage AI", color = new Color(0.4f, 1f, 0.75f), trait = Trait.GpuFarm },
            new Customer { name = "Kestrel Insurance", color = new Color(0.6f, 0.7f, 0.85f), trait = Trait.Strict },
        };

        public static Trait TraitOf(string customer) => Cust(customer)?.trait ?? Trait.None;

        public static string TraitName(Trait t) => t switch
        {
            Trait.Generous => "Generous",
            Trait.Evidence => "Wants evidence",
            Trait.StorageHeavy => "Storage heavy",
            Trait.Fickle => "Fickle",
            Trait.Urgent => "Everything is urgent",
            Trait.Compliance => "Compliance first",
            Trait.FiberHungry => "Fiber hungry",
            Trait.NightAverse => "Hates the night shift",
            Trait.GpuFarm => "GPU farm",
            Trait.Strict => "Strict",
            _ => "",
        };

        public static string TraitDesc(Trait t) => t switch
        {
            Trait.Generous => "Tips more when they're happy (up to +20%).",
            Trait.Evidence => "Wants a photo of the rack front on every hardware ticket, for the change record.",
            Trait.StorageHeavy => "Runs lots of disk shelves, so most storage work is theirs.",
            Trait.Fickle => "Their mood swings 50% harder, both ways.",
            Trait.Urgent => "Routine work comes in as P2, and their servers go down more often.",
            Trait.Compliance => "Decommissions pay 25% more, but a broken chain of custody costs double.",
            Trait.FiberHungry => "Orders most of the cross-connects, and pays 20% more for them.",
            Trait.NightAverse => "Handing their tickets to the night shift costs double goodwill.",
            Trait.GpuFarm => "Ships 4U GPU servers that pull 2.4 kW each. Their PDUs run hot.",
            Trait.Strict => "Every mistake costs double goodwill.",
            _ => "",
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
            new UpgradeDef { id = "esd", name = "ESD Wrist Strap", cost = 250, desc = "No more static zaps killing a fresh DIMM while you seat it." },
            new UpgradeDef { id = "labeler", name = "Label Printer", cost = 300, level = 2, desc = "Prints and sticks cable labels for you while you tidy a rack." },
            new UpgradeDef { id = "flashlight", name = "Head Torch", cost = 150, desc = "Lights up the dim hot aisle when you work at the rear of a rack." },
        };

        public static UpgradeDef Upgrade(string id) => System.Array.Find(Upgrades, u => u.id == id);

        // ================================================================ technician levels
        public static readonly int[] LevelXp = { 0, 120, 300, 560, 900, 1350 };
        public static readonly string[] LevelTitles = { "Trainee", "Technician", "Senior Technician", "Lead Technician", "Site Specialist", "Site Lead" };
        /// <summary>Level that opens the expansion cages at the end of every row.</summary>
        public const int ExpansionLevel = 4;
        /// <summary>Level at which the site lead gets a trainee to hand simple tickets to.</summary>
        public const int TraineeLevel = 6;

        // ================================================================ achievements
        public class Achievement
        {
            public string id, name, desc;
            public System.Func<Career, bool> met;
        }

        public static readonly Achievement[] Achievements =
        {
            new Achievement { id = "first", name = "First day on the floor", desc = "Close your first ticket.", met = c => c.ticketsDone >= 1 },
            new Achievement { id = "streak10", name = "Steady hands", desc = "Close 10 tickets in a row without a mistake.", met = c => c.stats.bestStreak >= 10 },
            new Achievement { id = "p1x10", name = "First responder", desc = "Close 10 P1s before any lower-priority work.", met = c => c.stats.p1First >= 10 },
            new Achievement { id = "shred100", name = "Chain of custody", desc = "Shred 100 drives.", met = c => c.stats.drivesShredded >= 100 },
            new Achievement { id = "swap50", name = "Parts cannon", desc = "Hot-swap 50 parts.", met = c => c.stats.partsSwapped >= 50 },
            new Achievement { id = "build5", name = "Rack builder", desc = "Build out 5 racks for new contracts.", met = c => c.stats.racksBuilt >= 5 },
            new Achievement { id = "tidy5", name = "Cable whisperer", desc = "Tidy 5 racks.", met = c => c.stats.racksTidied >= 5 },
            new Achievement { id = "escort5", name = "Tour guide", desc = "Escort 5 customer visitors.", met = c => c.stats.visitors >= 5 },
            new Achievement { id = "impostor", name = "Not on the list", desc = "Turn away a visitor who shouldn't get in.", met = c => c.stats.impostorsCaught >= 1 },
            new Achievement { id = "emergency3", name = "Cool under pressure", desc = "Handle 3 facility emergencies.", met = c => c.stats.emergencies >= 3 },
            new Achievement { id = "pm10", name = "An ounce of prevention", desc = "Catch 10 failing parts early during maintenance.", met = c => c.stats.caughtEarly >= 10 },
            new Achievement { id = "week", name = "Perfect week", desc = "Meet every goal in a weekly review.", met = c => c.stats.perfectWeeks >= 1 },
            new Achievement { id = "earn10k", name = "Five figures", desc = "Earn $10,000 over your career.", met = c => c.totalEarned >= 10000 },
            new Achievement { id = "lead", name = "Site Lead", desc = "Reach the top of the ladder.", met = c => c.Level >= 6 },
        };

        // ================================================================ weekly review
        /// <summary>The goals a weekly review can set. Targets scale with the technician's level.</summary>
        public static readonly string[] GoalIds = { "p1first", "clean", "nohandover", "earn", "nofollow", "pm" };

        public static int GoalTarget(string id, int level) => id switch
        {
            "p1first" => 3 + level / 2,
            "clean" => 8 + 2 * level,
            "earn" => 1500 + 600 * level,
            "pm" => 2,
            _ => 0,
        };

        public static string GoalText(string id, int target) => id switch
        {
            "p1first" => $"Close {target} P1s before any lower-priority work",
            "clean" => $"Close {target} tickets without a mistake",
            "nohandover" => "Hand nothing over to the night shift",
            "earn" => $"Earn {Ui.Money(target)}",
            "nofollow" => "Cause no follow-up incidents",
            _ => $"Accept and finish {target} preventive maintenance jobs",
        };

        /// <summary>"Hold the line" goals start met and can only be lost.</summary>
        public static bool GoalIsStreak(string id) => id == "nohandover" || id == "nofollow";

        public const int GoalXp = 25, GoalCash = 100, PerfectWeekCash = 250;
    }
}
