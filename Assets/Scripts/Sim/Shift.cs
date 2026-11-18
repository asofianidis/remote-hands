using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// One working day in the data hall: the ticket queue, the work and the scoring. The racks
    /// themselves belong to the career, so installs, removals and new contracts carry over.
    /// Pure C# so it can be driven from tests without a scene.
    /// </summary>
    public class Shift
    {
        public static readonly string[] Rows = { "A", "B", "C", "D" };
        public const int RacksPerRow = 10;
        /// <summary>Expansion cages added at the end of each row once the technician is senior enough.</summary>
        public const int ExpansionCols = 2;

        public static readonly Dictionary<string, string[]> FirmwareVersions = new Dictionary<string, string[]>
        {
            { "BIOS", new[] { "2.1.0", "2.2.3", "2.3.0", "2.4.1" } },
            { "BMC", new[] { "4.10.2", "4.11.0", "4.12.3" } },
            { "NIC", new[] { "22.4.1", "22.5.0", "23.1.2" } },
        };
        public static string Latest(string comp) => FirmwareVersions[comp][FirmwareVersions[comp].Length - 1];

        public static readonly string[] EmergencyKinds = { "power", "leak", "vesda" };
        /// <summary>Seconds the UPS batteries carry the hall after a utility failure.</summary>
        public const float UpsSeconds = 150f;

        public readonly Career career;
        public readonly Inventory inv = new Inventory();
        public List<Rack> racks => career.hall;
        public readonly List<Ticket> tickets = new List<Ticket>();
        /// <summary>Optional preventive-maintenance work the technician can accept from the tablet.</summary>
        public readonly List<Ticket> offers = new List<Ticket>();
        /// <summary>Customer shipments waiting to be placed on the loading dock by the world.</summary>
        public readonly List<Item> deliveries = new List<Item>();
        public readonly System.Random rng;

        public int earned, completed, mistakes, handedOver, interrupts, rmaCredit;
        /// <summary>The ticket being worked on, so fumbles count against its clean-work bonus.</summary>
        public Ticket working;
        public float repStart;
        public int xpStart;
        /// <summary>What happened overnight, for the briefing (null when nothing did).</summary>
        public string news;
        /// <summary>End-of-shift notes for the summary: the weekly review, lost RMA parts.</summary>
        public readonly List<string> endNotes = new List<string>();
        public readonly List<string> unlocked = new List<string>();
        /// <summary>Urgent tickets and surprise deliveries can land mid-shift. Tests switch this off.</summary>
        public bool interruptions = true;
        public const int MaxInterrupts = 2;
        /// <summary>UPS battery left while utility power is out (0 when it isn't).</summary>
        public float ups;
        /// <summary>A facility emergency is planned once this many tickets are closed (0 = none today).</summary>
        public int emergencyAfter;
        public string emergencyKind;

        public event Action<Ticket> TicketArrived;
        public event Action<Ticket> TicketDone;
        /// <summary>Message to show the player: text and severity (0 info, 1 good, 2 warn, 3 bad).</summary>
        public event Action<string, int> Message;

        public IEnumerable<Ticket> OpenTickets => tickets.Where(t => t.Open);
        public bool AllDone => tickets.Count > 0 && !OpenTickets.Any();
        public int DoneCount => tickets.Count(t => t.status == TicketStatus.Done);

        public Shift(Career career, int seed, ShiftSave resume = null)
        {
            this.career = career;
            rng = new System.Random(seed);
            repStart = career.rep;
            xpStart = career.xp;
            inv.handCap = career.Has("cart2") ? 3 : career.Has("cart") ? 2 : 1;
            inv.pouchCap = career.Has("belt2") ? 9 : career.Has("belt") ? 6 : 4;
            if (career.hall == null) career.hall = new List<Rack>();
            if (career.nextDeviceId < 1) career.nextDeviceId = 1;
            if (career.nextTicketId < 101) career.nextTicketId = 101;
            if (career.stats == null) career.stats = new CareerStats();
            if (career.stock == null) career.stock = new List<StockLine>();
            if (career.orders == null) career.orders = new List<StockLine>();
            if (career.goals == null) career.goals = new List<Goal>();
            if (career.achievements == null) career.achievements = new List<string>();
            // Older saves (and new careers) start with a stocked storage room.
            if (career.stock.Count == 0) foreach (var p in Catalog.Parts) career.AddStock(p, Catalog.StartStock(p));
            if (career.hall.Count == 0) BuildRacks();
            foreach (var r in racks) r.openGaps ??= new List<int>();
            if (resume != null) Restore(resume);
        }

        // ================================================================ saving mid-shift
        void Restore(ShiftSave s)
        {
            tickets.AddRange(s.tickets);
            if (s.offers != null) offers.AddRange(s.offers);
            inv.hands.AddRange(s.hands);
            inv.pouch.AddRange(s.pouch);
            if (s.rma != null) inv.rma.AddRange(s.rma);
            earned = s.earned; completed = s.completed; mistakes = s.mistakes; handedOver = s.handedOver; interrupts = s.interrupts;
            repStart = s.repStart;
            xpStart = s.xpStart;
            ups = s.ups;
            emergencyAfter = s.emergencyAfter;
            emergencyKind = Json.N(s.emergencyKind);
        }

        /// <summary>Everything about the shift except the world positions, which the game fills in.</summary>
        public ShiftSave Snapshot()
        {
            var s = new ShiftSave
            {
                earned = earned, completed = completed, mistakes = mistakes, handedOver = handedOver, interrupts = interrupts, repStart = repStart, xpStart = xpStart,
                ups = ups, emergencyAfter = emergencyAfter, emergencyKind = emergencyKind,
            };
            s.tickets.AddRange(tickets);
            s.offers.AddRange(offers);
            s.hands.AddRange(inv.hands);
            s.pouch.AddRange(inv.pouch);
            s.rma.AddRange(inv.rma);
            return s;
        }

        // ================================================================ hall
        void BuildRacks()
        {
            var custs = Catalog.Customers.ToList();
            for (int r = 0; r < Rows.Length; r++)
                for (int c = 1; c <= RacksPerRow; c++)
                {
                    var rack = new Rack { label = $"{Rows[r]}{c:00}", row = r, col = c };
                    racks.Add(rack);
                    if (rng.NextDouble() < 0.15) continue; // vacant, available for new contracts
                    rack.owner = custs[rng.Next(custs.Count)].name;
                    Populate(rack);
                }
        }

        /// <summary>Columns in each row right now (the expansion cages add two at the end).</summary>
        public static int ColsOf(List<Rack> hall) => hall.Count == 0 ? RacksPerRow : hall.Max(r => r.col);

        /// <summary>Open the expansion cages: two empty racks at the end of every row. True if any were added.</summary>
        public bool OpenExpansion()
        {
            bool added = false;
            for (int r = 0; r < Rows.Length; r++)
                for (int c = RacksPerRow + 1; c <= RacksPerRow + ExpansionCols; c++)
                {
                    string label = $"{Rows[r]}{c:00}";
                    if (Rack(label) != null) continue;
                    racks.Add(new Rack { label = label, row = r, col = c });
                    added = true;
                }
            return added;
        }

        void Populate(Rack rack)
        {
            AddDevice(rack, "sw48", 42).installDay = -rng.Next(60, 1800);
            float fill = 0.35f + (float)rng.NextDouble() * 0.4f;
            int u = 1 + rng.Next(3);
            var trait = Catalog.TraitOf(rack.owner);
            var pool = new List<string> { "srv1u", "srv1u", "srv1u", "srv2u", "srv2u", "ds4u", "cust1u", "cust2u" };
            if (trait == Trait.GpuFarm) pool.AddRange(new[] { "gpu4u", "gpu4u", "gpu4u" });
            if (trait == Trait.StorageHeavy) pool.AddRange(new[] { "ds4u", "ds4u", "tl4u" });
            if (rng.NextDouble() < 0.15) pool.Add("tl4u");
            while (u < 40 && (RH.Rack.Units - rack.FreeUnits) < fill * RH.Rack.Units)
            {
                var def = Catalog.Dev(pool[rng.Next(pool.Count)]);
                if (u + def.heightU - 1 >= 42) break;
                AddDevice(rack, def.id, u).installDay = -rng.Next(60, 1800);
                u += def.heightU + (rng.NextDouble() < 0.3 ? 1 + rng.Next(3) : 0);
            }
        }

        const string SerialChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string NewSerial()
        {
            var c = new char[8];
            for (int i = 0; i < c.Length; i++) c[i] = SerialChars[rng.Next(SerialChars.Length)];
            return new string(c);
        }

        Device AddDevice(Rack rack, string def, int u, string owner = null)
        {
            var d = new Device { id = career.nextDeviceId++, def = def, u = u, owner = owner ?? rack.owner, serial = NewSerial(), installDay = career.day };
            foreach (var comp in Catalog.Components)
            {
                var vs = FirmwareVersions[comp];
                d.SetFirmware(comp, vs[rng.Next(vs.Length - 1)]);
            }
            rack.devices.Add(d);
            return d;
        }

        public Rack Rack(string label) => label == null ? null : racks.Find(r => r.label == label);

        public Device Dev(string rack, int id) => Rack(rack)?.devices.Find(d => d.id == id);

        public Ticket TicketOf(WorkTask t) => tickets.Find(k => k.tasks.Contains(t)) ?? offers.Find(k => k.tasks.Contains(t));

        /// <summary>The rack's top-of-rack switch, if it has one.</summary>
        public static Device SwitchOf(Rack r) => r?.devices.Find(d => d.Def.kind == DevKind.Switch);

        /// <summary>Is the rack cut off from the network because its switch is down?</summary>
        public static bool Isolated(Rack r) => SwitchOf(r) is Device sw && sw.fault != Fault.None;

        /// <summary>Open tasks done at the rack itself (not at a station elsewhere in the building).</summary>
        public IEnumerable<(Ticket ticket, WorkTask task)> TasksAt(string rack) =>
            OpenTickets.Where(k => !k.trainee).SelectMany(k => k.tasks.Where(t => t.rack == rack && t.station == null).Select(t => (k, t)));

        public IEnumerable<(Ticket ticket, WorkTask task)> TasksAtStation(string station) =>
            OpenTickets.Where(k => !k.trainee).SelectMany(k => k.tasks.Where(t => t.station == station).Select(t => (k, t)));

        /// <summary>Jobs done from the hot aisle behind the rack: power, cabling and the NIC optics.</summary>
        public static bool RearJob(TaskKind k) =>
            k == TaskKind.Cable || k == TaskKind.Uncable || k == TaskKind.Reseat || k == TaskKind.SwapPsu ||
            k == TaskKind.SwapSfp || k == TaskKind.Power || k == TaskKind.Trace || k == TaskKind.SasPath || k == TaskKind.CableTidy;

        /// <summary>Jobs that finish on their own when something happens in the world (no Start button).</summary>
        public static bool WorldJob(TaskKind k) => k == TaskKind.Escort || k == TaskKind.Photo;

        /// <summary>The CRAC unit that cools a row: CRAC-1 the south half of the hall, CRAC-2 the north half.</summary>
        public static int CracFor(int row) => row < 2 ? 1 : 2;

        /// <summary>How many sensor readings are out of spec in a health check (from the task's seed).</summary>
        public static int Anomalies(int seed) => 1 + Math.Abs(seed) % 2;

        // ================================================================ the day's work order
        /// <summary>Tickets in today's work order: 4 on day one, one more each day, up to 12.</summary>
        public int QueueSize => QueueSizeFor(career.day);
        public static int QueueSizeFor(int day) => Mathf.Min(12, 3 + day);

        /// <summary>
        /// Start of a new day: the night shift's leftovers are cleared, maybe a contract ends, then the
        /// whole day's work is handed out. There is no clock: the shift ends when the player clocks out.
        /// </summary>
        public void GenerateQueue()
        {
            Overnight();
            if (career.Level >= Catalog.ExpansionLevel && OpenExpansion())
                AddNews($"The expansion cages at the end of every row are open: {Rows.Length * ExpansionCols} empty racks, ready for new contracts.");
            if (career.orders.Count > 0)
            {
                var r = GenReceiving(true);
                if (r != null) { tickets.Add(r); TicketArrived?.Invoke(r); }
            }
            foreach (var t in NewTypes(career.day)) SpawnTicket(t);
            // A freshly bought certification shows up quickly.
            if (career.Has("cert_power") && rng.NextDouble() < 0.6) SpawnTicket(TicketType.PowerAudit);
            if (career.Has("cert_fiber") && rng.NextDouble() < 0.6) SpawnTicket(TicketType.CrossConnect);
            // Racks left running hot get their cooling ticket.
            if (career.day >= 6 && racks.Any(r => r.hot) && !tickets.Any(t => t.type == TicketType.Cooling)) SpawnTicket(TicketType.Cooling);
            for (int guard = 0; tickets.Count < QueueSize && guard < QueueSize * 4; guard++) SpawnTicket();
            GenerateOffers();
            PlanEmergency();
            if (career.goals.Count == 0) NewWeek();
        }

        void AddNews(string s) => news = news == null ? s : news + "\n\n" + s;

        /// <summary>Ticket types that unlock on a given day, so each one shows up on its first day.</summary>
        public static TicketType[] NewTypes(int day) => day switch
        {
            1 => new[] { TicketType.Expand },
            2 => new[] { TicketType.FixIssue, TicketType.RackCustomer, TicketType.SmartHands },
            3 => new[] { TicketType.NewContract, TicketType.Receiving },
            4 => new[] { TicketType.Decommission, TicketType.Escort },
            5 => new[] { TicketType.SwitchOutage, TicketType.StorageService },
            6 => new[] { TicketType.Cooling, TicketType.CableAudit },
            _ => new TicketType[0],
        };

        public List<TicketType> Available()
        {
            var l = new List<TicketType> { TicketType.Firmware, TicketType.ReplaceHardware, TicketType.Expand };
            if (career.day >= 2) { l.Add(TicketType.FixIssue); l.Add(TicketType.RackCustomer); l.Add(TicketType.SmartHands); }
            if (career.day >= 3) { l.Add(TicketType.NewContract); l.Add(TicketType.Receiving); }
            if (career.day >= 4)
            {
                l.Add(TicketType.Decommission);
                l.Add(TicketType.Escort);
                // A crowded hall needs more room freed up.
                if (Owned.Count(r => r.FreeUnits < 8) > Owned.Count / 2) l.Add(TicketType.Decommission);
            }
            if (career.day >= 5) { l.Add(TicketType.SwitchOutage); l.Add(TicketType.StorageService); }
            if (career.day >= 6) { l.Add(TicketType.Cooling); l.Add(TicketType.CableAudit); }
            if (career.Has("cert_power")) l.Add(TicketType.PowerAudit);
            if (career.Has("cert_fiber")) l.Add(TicketType.CrossConnect);
            return l;
        }

        /// <summary>Night-shift leftovers are gone; now and then a customer leaves and frees a rack.</summary>
        void Overnight()
        {
            news = null;
            foreach (var r in racks)
            {
                r.reserved.Clear();
                foreach (var d in r.devices) d.ClearFaults();
            }
            if (career.day >= 4 && (!racks.Any(r => r.Empty) || rng.NextDouble() < 0.1))
            {
                var cands = Owned.OrderBy(r => r.devices.Count).Take(4).ToList();
                if (cands.Count > 0)
                {
                    var r = Pick(cands);
                    AddNews($"Overnight, {r.owner}'s contract for rack {r.label} ended and the night shift emptied it. It's free for a new customer.");
                    r.devices.Clear();
                    r.owner = null;
                    r.openGaps.Clear();
                    r.hot = false;
                    r.tidy = false;
                }
            }
            // Procurement never lets a shelf sit empty with nothing on order.
            var sent = new List<string>();
            foreach (var p in Catalog.Parts)
                if (career.Stock(p) == 0 && career.Ordered(p) == 0) { career.AddStock(p, 1); sent.Add(Catalog.PartName(p)); }
            if (sent.Count > 0) AddNews($"The {string.Join(", ", sent)} shelf ran dry, so procurement sent one emergency spare. Order more from the STOCK page on your tablet.");
        }

        public Ticket SpawnTicket(TicketType? force = null)
        {
            var types = Available();
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var type = force ?? types[rng.Next(types.Count)];
                var t = Generate(type);
                if (t != null)
                {
                    AddEvidence(t);
                    tickets.Add(t);
                    TicketArrived?.Invoke(t);
                    return t;
                }
                if (force != null) return null;
            }
            return null;
        }

        /// <summary>A customer who wants evidence gets a photo of the rack front on every hardware ticket.</summary>
        void AddEvidence(Ticket t)
        {
            if (Catalog.TraitOf(t.customer) != Trait.Evidence || t.rack == null || t.tasks.Any(x => x.kind == TaskKind.Photo)) return;
            if (t.type != TicketType.ReplaceHardware && t.type != TicketType.FixIssue && t.type != TicketType.StorageService &&
                t.type != TicketType.Expand && t.type != TicketType.RackCustomer && t.type != TicketType.SwitchOutage) return;
            t.tasks.Add(new WorkTask { kind = TaskKind.Photo, rack = t.rack, component = "front" });
            t.body += $"\n{t.customer} wants evidence for the change record: when you're done, send a photo of the whole front of {t.rack}.";
            t.pay += 20;
        }

        Ticket NewTicket(TicketType type, Rack rack, string customer, int pay, int priority = 3)
        {
            // Some customers treat everything as urgent.
            if (priority == 3 && Catalog.TraitOf(customer) == Trait.Urgent) priority = 2;
            return new Ticket { id = career.nextTicketId++, type = type, rack = rack?.label, customer = customer, pay = pay, priority = priority };
        }

        T Pick<T>(IList<T> l) => l[rng.Next(l.Count)];

        /// <summary>Usually picks from the preferred subset (when it isn't empty), otherwise from everything.</summary>
        T Prefer<T>(IList<T> all, Func<T, bool> preferred, double chance)
        {
            var sub = all.Where(preferred).ToList();
            return sub.Count > 0 && rng.NextDouble() < chance ? Pick(sub) : Pick(all);
        }

        List<Rack> Owned => racks.Where(r => r.owner != null).ToList();

        /// <summary>Devices some open ticket (or open offer) is already working on.</summary>
        HashSet<int> Busy() => new HashSet<int>(OpenTickets.Concat(offers).SelectMany(t => t.tasks).Where(x => x.deviceId > 0).Select(x => x.deviceId));

        /// <summary>Racks some open ticket is already working on as a whole.</summary>
        HashSet<string> BusyRacks() => new HashSet<string>(OpenTickets.Concat(offers).SelectMany(t => t.tasks).Where(x => x.rack != null).Select(x => x.rack));

        Ticket Generate(TicketType type)
        {
            switch (type)
            {
                case TicketType.Expand: return GenInstall(type, false);
                case TicketType.RackCustomer: return GenInstall(type, true);
                case TicketType.FixIssue: return GenFix();
                case TicketType.ReplaceHardware: return GenReplace();
                case TicketType.Firmware: return GenFirmware();
                case TicketType.Decommission: return GenDecommission();
                case TicketType.SmartHands: return GenSmartHands();
                case TicketType.CrossConnect: return GenCrossConnect();
                case TicketType.PowerAudit: return GenPowerAudit();
                case TicketType.SwitchOutage: return GenSwitchOutage();
                case TicketType.StorageService: return GenStorageService();
                case TicketType.Cooling: return GenCooling();
                case TicketType.CableAudit: return GenCableAudit();
                case TicketType.Receiving: return GenReceiving(false);
                case TicketType.Escort: return GenEscort();
                case TicketType.Maintenance: return GenMaintenance();
                case TicketType.Emergency: return GenEmergency(Pick(EmergencyKinds));
                default: return GenNewContract();
            }
        }

        Ticket GenInstall(TicketType type, bool customerGear)
        {
            var candidates = Owned.Where(r => r.FreeUnits >= 6).ToList();
            if (candidates.Count == 0) return null;
            var rack = Pick(candidates);
            int n = 1 + rng.Next(3);
            var t = NewTicket(type, rack, rack.owner, 0);
            var placed = new List<string>();
            bool gpu = customerGear && Catalog.TraitOf(rack.owner) == Trait.GpuFarm;
            for (int i = 0; i < n; i++)
            {
                string def = gpu ? Pick(new[] { "gpu4u", "gpu4u", "cust2u" })
                    : customerGear ? Pick(new[] { "cust1u", "cust1u", "cust2u", "cust2u", "cust4u" }) : Pick(new[] { "srv1u", "srv1u", "srv2u" });
                var d = Catalog.Dev(def);
                int u = rack.FindFree(d.heightU, rng);
                if (u < 0) continue;
                rack.reserved.Add(new Vector2Int(u, d.heightU));
                string tag = customerGear ? $"T{t.id}-{i + 1}" : null;
                t.tasks.Add(new WorkTask { kind = TaskKind.Mount, rack = rack.label, def = def, u = u, tag = tag });
                t.tasks.Add(new WorkTask { kind = TaskKind.Cable, rack = rack.label, def = def, u = u });
                placed.Add($"{d.name} at U{u}");
                if (customerGear) deliveries.Add(new Item { def = def, tag = tag, label = $"{rack.owner} - {d.name} ({tag})" });
            }
            if (placed.Count == 0) return null;
            int items = placed.Count;
            if (customerGear)
            {
                t.title = $"Rack {rack.owner}'s delivery ({items} item{(items > 1 ? "s" : "")}) in {rack.label}";
                t.body = $"{rack.owner} shipped their own equipment. It is waiting on the loading dock. Rack and cable it in {rack.label}:\n- " + string.Join("\n- ", placed);
                t.pay = 55 + 45 * items + (gpu ? 30 : 0);
            }
            else
            {
                t.title = $"Expand {rack.owner}: install {items} server{(items > 1 ? "s" : "")} in {rack.label}";
                t.body = $"{rack.owner} is adding capacity. Collect the servers from the storage room, then rack and cable them in {rack.label}:\n- " + string.Join("\n- ", placed);
                t.pay = 40 + 45 * items;
            }
            return t;
        }

        List<Device> IdleDevices(Func<Device, bool> filter)
        {
            var busy = Busy();
            return Owned.SelectMany(r => r.devices).Where(d => !busy.Contains(d.id) && d.Healthy && filter(d)).ToList();
        }

        bool Maintained(Device d) => RackOf(d) is Rack r && r.pmDay > 0 && career.day - r.pmDay <= 10;

        /// <summary>
        /// Old hardware fails more: of a few random candidates, take the one in service longest.
        /// Racks that had preventive maintenance recently are usually spared.
        /// </summary>
        Device Oldest(List<Device> devs)
        {
            Device best = null;
            for (int i = 0; i < 3; i++)
            {
                var d = Pick(devs);
                if (Maintained(d) && rng.NextDouble() < 0.7) d = Pick(devs);
                if (best == null || d.installDay < best.installDay) best = d;
            }
            return best;
        }

        Rack RackOf(Device d) => racks.Find(r => r.devices.Contains(d));

        static string Age(Career c, Device d)
        {
            int days = c.day - d.installDay;
            return days >= 365 ? $"{days / 365} year{(days >= 730 ? "s" : "")} old" : days > 1 ? $"{days} days old" : "brand new";
        }

        Ticket GenFix() => GenFix(false);

        Ticket GenFix(bool urgent)
        {
            // Switches have their own outage tickets; shelves and tape don't run an OS to diagnose.
            var devs = IdleDevices(d => d.Def.kind != DevKind.Storage && d.Def.kind != DevKind.Switch);
            if (devs.Count == 0) return null;
            Device dev;
            var hot = devs.Where(d => d.Def.kind == DevKind.Server && RackOf(d).hot).ToList();
            bool cooked = hot.Count > 0 && rng.NextDouble() < 0.5;
            if (cooked) dev = Pick(hot);
            else
            {
                // Some customers' servers just go down more.
                var urgentOwners = devs.Where(d => Catalog.TraitOf(d.owner) == Trait.Urgent).ToList();
                dev = urgentOwners.Count > 0 && rng.NextDouble() < 0.3 ? Pick(urgentOwners) : Oldest(devs);
            }
            var rack = RackOf(dev);
            var faults = new List<Fault> { Fault.LinkDown, Fault.Hung };
            if (dev.Def.sfps > 0) faults.Add(Fault.BadSfp);
            if (dev.Def.kind == DevKind.Server) { faults.Add(Fault.BadDimm); faults.Add(Fault.FanFail); }
            dev.fault = cooked ? Fault.FanFail : Pick(faults);
            if (dev.fault == Fault.BadSfp) dev.badSfp = rng.Next(dev.Def.sfps);
            if (dev.fault == Fault.BadDimm) dev.badDimm = rng.Next(16);
            if (dev.fault == Fault.FanFail) dev.badFan = rng.Next(6);
            bool cart = career.day >= 3 && rng.NextDouble() < 0.3;
            var t = NewTicket(TicketType.FixIssue, rack, dev.owner, cart ? 140 : 110, 1);
            bool network = dev.fault == Fault.LinkDown || dev.fault == Fault.BadSfp;
            string symptom = network ? "is unreachable on the network"
                : Pick(new[] { "stopped responding to pings and SSH", "keeps dropping off monitoring", "raised a hardware alarm and the customer's app is timing out" });
            t.title = (urgent ? "URGENT: " : "") + $"{dev.owner}: {dev.Def.name} down in {rack.label} U{dev.u}";
            t.body = $"Monitoring reports that {dev.Name} at {rack.label} U{dev.u} {symptom}. The customer is losing traffic. Connect to it, find the cause and fix it." +
                     (cooked ? $"\n{rack.label} has been running hot lately." : "") +
                     (cart ? "\nIts BMC isn't answering either, so you can't reach it remotely: bring the crash cart (KVM) from the storage room and read the console directly." : "") +
                     "\nP1: close it before any lower-priority ticket for a 25% rush bonus.";
            t.tasks.Add(new WorkTask { kind = TaskKind.Diagnose, rack = rack.label, deviceId = dev.id, u = dev.u, cart = cart });
            return t;
        }

        Ticket GenReplace()
        {
            bool drive = rng.NextDouble() < 0.6;
            var devs = IdleDevices(d => drive ? d.Def.bays > 0 : d.Def.psus > 0);
            if (devs.Count == 0) return null;
            var dev = Oldest(devs);
            var rack = RackOf(dev);
            var t = NewTicket(TicketType.ReplaceHardware, rack, dev.owner, 85, 2);
            if (drive)
            {
                dev.failedBay = rng.Next(dev.Def.bays);
                t.title = $"Replace failed drive in {rack.label} U{dev.u}";
                t.body = $"The RAID controller in {dev.Name} ({rack.label} U{dev.u}, {Age(career, dev)}) flagged a failed drive. The array is degraded. Grab a spare drive from the storage room and hot-swap it. Look for the amber LED, and don't pull a healthy drive.";
                t.tasks.Add(new WorkTask { kind = TaskKind.SwapDrive, rack = rack.label, deviceId = dev.id, u = dev.u });
            }
            else
            {
                dev.failedPsu = rng.Next(dev.Def.psus);
                t.title = $"Replace failed PSU in {rack.label} U{dev.u}";
                t.body = $"{dev.Name} ({rack.label} U{dev.u}, {Age(career, dev)}) lost power supply {(dev.failedPsu == 0 ? "A" : "B")} and is running on one feed. Swap in a spare PSU from the storage room. The PSUs are at the back, so work from the hot aisle.";
                t.tasks.Add(new WorkTask { kind = TaskKind.SwapPsu, rack = rack.label, deviceId = dev.id, u = dev.u });
            }
            return t;
        }

        Ticket GenFirmware()
        {
            string comp = Pick(Catalog.Components);
            string target = Latest(comp);
            var busy = Busy();
            var groups = Owned.Select(r => (r, devs: r.devices.Where(d => !busy.Contains(d.id) && d.Healthy && d.Def.kind == DevKind.Server && d.Firmware(comp) != target).ToList()))
                              .Where(g => g.devs.Count >= 2).ToList();
            if (groups.Count == 0) return null;
            var (rack, list) = Pick(groups);
            var model = Pick(list.Select(d => d.def).Distinct().ToList());
            var devs = list.Where(d => d.def == model).Take(2 + rng.Next(3)).ToList();
            if (devs.Count == 0) return null;
            var t = NewTicket(TicketType.Firmware, rack, rack.owner, 25 + 30 * devs.Count);
            foreach (var d in devs)
                t.tasks.Add(new WorkTask { kind = TaskKind.Firmware, rack = rack.label, deviceId = d.id, u = d.u, component = comp, version = target });
            var def = Catalog.Dev(model);
            t.title = $"Firmware: {comp} {target} on {devs.Count}x {def.model} in {rack.label}";
            t.body = $"{rack.owner} approved a maintenance window. Flash {comp} firmware {target} onto these {def.name}s in {rack.label}: " +
                     string.Join(", ", devs.Select(d => $"U{d.u}")) + $". Pick the image for model {def.model}, and don't let the flash get interrupted.";
            return t;
        }

        Ticket GenNewContract()
        {
            var empties = racks.Where(r => r.Empty).ToList();
            if (empties.Count == 0) return null;
            var rack = Pick(empties);
            var cust = Pick(Catalog.Customers).name;
            rack.owner = cust;
            int servers = 2 + rng.Next(3);
            var t = NewTicket(TicketType.NewContract, rack, cust, 0);
            var plan = new List<(string def, int u)> { ("sw48", 42) };
            int u = 1;
            for (int i = 0; i < servers; i++)
            {
                string def = rng.NextDouble() < 0.7 ? "srv1u" : "srv2u";
                plan.Add((def, u));
                u += Catalog.Dev(def).heightU;
            }
            foreach (var (def, pu) in plan)
            {
                rack.reserved.Add(new Vector2Int(pu, Catalog.Dev(def).heightU));
                t.tasks.Add(new WorkTask { kind = TaskKind.Mount, rack = rack.label, def = def, u = pu });
            }
            foreach (var (def, pu) in plan) t.tasks.Add(new WorkTask { kind = TaskKind.Cable, rack = rack.label, def = def, u = pu });
            t.title = $"New contract: build out {rack.label} for {cust}";
            t.body = $"{cust} signed for rack {rack.label}. Install a top-of-rack switch at U42 and {servers} servers from the bottom up, then cable everything:\n- " +
                     string.Join("\n- ", plan.Select(p => $"{Catalog.Dev(p.def).name} at U{p.u}"));
            t.pay = 180 + 50 * servers;
            return t;
        }

        /// <summary>End-of-life gear comes out: unplug, de-rack, wipe the drives at the bench, ship it from the dock.</summary>
        Ticket GenDecommission()
        {
            var busy = Busy();
            var cands = Owned.Where(r => r.devices.Count(d => d.def != "sw48" && !busy.Contains(d.id) && d.Healthy) >= 3).ToList();
            if (cands.Count == 0) return null;
            var rack = Pick(cands);
            var devs = rack.devices.Where(d => d.def != "sw48" && !busy.Contains(d.id) && d.Healthy).OrderBy(d => d.installDay).Take(1 + rng.Next(2)).ToList();
            var t = NewTicket(TicketType.Decommission, rack, rack.owner, 0);
            int i = 0;
            foreach (var d in devs)
            {
                string tag = $"D{t.id}-{++i}";
                t.tasks.Add(new WorkTask { kind = TaskKind.Uncable, rack = rack.label, deviceId = d.id, u = d.u, def = d.def });
                t.tasks.Add(new WorkTask { kind = TaskKind.Unmount, rack = rack.label, deviceId = d.id, u = d.u, def = d.def, tag = tag });
                if (d.Def.bays > 0) t.tasks.Add(new WorkTask { kind = TaskKind.Wipe, rack = rack.label, def = d.def, tag = tag, station = "bench", u = d.u });
                t.tasks.Add(new WorkTask { kind = TaskKind.Ship, rack = rack.label, def = d.def, tag = tag, station = "dock", u = d.u });
            }
            t.pay = 50 + 60 * devs.Count;
            if (Catalog.TraitOf(rack.owner) == Trait.Compliance) t.pay = Mathf.RoundToInt(t.pay * 1.25f);
            t.title = $"Decommission {devs.Count} end-of-life unit{(devs.Count > 1 ? "s" : "")} in {rack.label}";
            t.body = $"{rack.owner} is retiring old hardware in {rack.label}:\n- " +
                     string.Join("\n- ", devs.Select(d => $"{d.Def.name} at U{d.u} ({Age(career, d)})")) +
                     "\nUnplug ONLY its cables (rear), de-rack it (front), wipe and shred its drives at the bench in the storage room, and leave it on the OUTBOUND pallet at the loading dock." +
                     (Catalog.TraitOf(rack.owner) == Trait.Compliance ? $"\n{rack.owner}'s auditors read the destruction log line by line. Every drive gets scanned before it's shredded." : "");
            return t;
        }

        /// <summary>The customer can't see their gear: read labels for them and send a photo.</summary>
        Ticket GenSmartHands()
        {
            var busy = Busy();
            var cands = Owned.Where(r => r.devices.Count(d => !busy.Contains(d.id)) >= 4).ToList();
            if (cands.Count == 0) return null;
            var rack = Pick(cands);
            var free = rack.devices.Where(d => !busy.Contains(d.id)).OrderBy(_ => rng.Next()).ToList();
            int n = 1 + rng.Next(2);
            var t = NewTicket(TicketType.SmartHands, rack, rack.owner, 0);
            var asks = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var d = free[i];
                string what = rng.NextDouble() < 0.5 ? "serial" : "host";
                t.tasks.Add(new WorkTask { kind = TaskKind.Inspect, rack = rack.label, deviceId = d.id, u = d.u, component = what });
                asks.Add(what == "serial" ? $"Read us the serial number of the {d.Def.name} at U{d.u}" : $"Tell us which hostname is on the asset tag of the {d.Def.name} at U{d.u}");
            }
            bool photo = rng.NextDouble() < 0.65;
            if (photo)
            {
                string side = rng.NextDouble() < 0.6 ? "front" : "rear";
                t.tasks.Add(new WorkTask { kind = TaskKind.Photo, rack = rack.label, component = side });
                asks.Add($"Send a photo of the whole {side} of {rack.label}");
            }
            t.pay = 30 + 30 * n + (photo ? 35 : 0);
            t.title = $"Smart hands for {rack.owner} in {rack.label}";
            t.body = $"{rack.owner}'s engineers are remote and need eyes on {rack.label}:\n- " + string.Join("\n- ", asks) +
                     (photo ? "\nRaise the camera with [F] and frame the whole rack, top to bottom." : "");
            return t;
        }

        Ticket GenCrossConnect()
        {
            var busy = Busy();
            var cands = Owned.Where(r => r.devices.Any(d => d.Def.ports.Length > 1 && d.Def.kind != DevKind.Storage && !busy.Contains(d.id))).ToList();
            if (cands.Count == 0) return null;
            var rack = Prefer(cands, r => Catalog.TraitOf(r.owner) == Trait.FiberHungry, 0.5);
            var dev = Pick(rack.devices.Where(d => d.Def.ports.Length > 1 && d.Def.kind != DevKind.Storage && !busy.Contains(d.id)).ToList());
            string port = dev.Def.ports[1];
            string carrier = Pick(Catalog.Carriers);
            int pp = 1 + rng.Next(24), cport = 1 + rng.Next(12);
            int pay = Catalog.TraitOf(rack.owner) == Trait.FiberHungry ? 198 : 165;
            var t = NewTicket(TicketType.CrossConnect, rack, rack.owner, pay);
            t.tasks.Add(new WorkTask { kind = TaskKind.Trace, rack = rack.label, deviceId = dev.id, u = dev.u, component = port, slot = pp });
            t.tasks.Add(new WorkTask { kind = TaskKind.Patch, rack = rack.label, station = "mmr", component = carrier, slot = cport });
            t.title = $"Cross-connect: {rack.owner} to {carrier}";
            t.body = $"{rack.owner} ordered a cross-connect to {carrier}. At the rear of {rack.label}, trace the {dev.Def.name}'s {port} cable (U{dev.u}) to find which patch panel port it lands on. " +
                     $"Then go to the meet-me room frame on the west wall and patch that {rack.label} port through to {carrier} port {cport}.";
            return t;
        }

        Ticket GenPowerAudit()
        {
            var busy = Busy();
            var cands = Owned.Where(r => r.devices.Count >= 5 && r.devices.All(d => !busy.Contains(d.id) && d.Healthy)).ToList();
            if (cands.Count == 0) return null;
            var rack = Prefer(cands, r => Catalog.TraitOf(r.owner) == Trait.GpuFarm, 0.4);
            int seed = rng.Next(1, int.MaxValue);
            var rows = PowerLayout(rack, seed, out float breaker);
            float a = FeedLoad(rows, 1) / breaker;
            var t = NewTicket(TicketType.PowerAudit, rack, rack.owner, 150 + 6 * rack.devices.Count, 2);
            t.tasks.Add(new WorkTask { kind = TaskKind.Power, rack = rack.label, slot = seed });
            t.title = $"PDU A in {rack.label} at {a * 100:0}% of its breaker";
            t.body = $"Monitoring shows the A-side PDU in {rack.label} drawing {a * 100:0}% of its {breaker:0}A breaker (the limit is 80%). Someone left servers single-corded or with both cords on feed A. " +
                     "At the rear of the rack, give every unit one cord on A and one on B without dropping power to anything: never pull a unit's only live cord.";
            return t;
        }

        // ================================================================ new work
        /// <summary>A rack's top-of-rack switch is down, and everything in the rack with it.</summary>
        Ticket GenSwitchOutage()
        {
            var busy = Busy();
            var cands = Owned.Where(r => r.devices.Count >= 3 && r.devices.All(d => !busy.Contains(d.id) && d.Healthy) && SwitchOf(r) != null).ToList();
            if (cands.Count == 0) return null;
            var rack = Pick(cands);
            var sw = SwitchOf(rack);
            sw.fault = Pick(new[] { Fault.BadSfp, Fault.Hung, Fault.ConfigLost, Fault.ConfigLost });
            if (sw.fault == Fault.BadSfp) sw.badSfp = rng.Next(sw.Def.sfps);
            bool cart = rng.NextDouble() < 0.6;
            int units = rack.devices.Count - 1;
            var t = NewTicket(TicketType.SwitchOutage, rack, rack.owner, 200 + 12 * units, 1);
            t.title = $"{rack.label} offline: top-of-rack switch down";
            t.body = $"Every server in {rack.label} dropped off the network at once: {units} of {rack.owner}'s units are dark. That's the top-of-rack switch ({sw.Def.model}, U42). " +
                     "Get on its console, work out what's wrong and fix it." +
                     (cart ? "\nThe switch's management port is down with it: bring the crash cart (KVM) from the storage room." : "") +
                     "\nP1: the whole rack is down. Close it before any lower-priority ticket for a 25% rush bonus.";
            t.tasks.Add(new WorkTask { kind = TaskKind.Diagnose, rack = rack.label, deviceId = sw.id, u = sw.u, cart = cart });
            return t;
        }

        /// <summary>Disk shelf work: swap a drive the controller has lit up for you, or replace a degraded SAS path.</summary>
        Ticket GenStorageService()
        {
            var shelves = IdleDevices(d => d.def == "ds4u");
            if (shelves.Count == 0) return null;
            var dev = Prefer(shelves, d => Catalog.TraitOf(d.owner) == Trait.StorageHeavy, 0.7);
            var rack = RackOf(dev);
            bool sas = rng.NextDouble() < 0.45;
            var t = NewTicket(TicketType.StorageService, rack, dev.owner, sas ? 120 : 95, 2);
            if (sas)
            {
                int path = rng.Next(2);
                string p = path == 0 ? "A" : "B", other = path == 0 ? "B" : "A";
                t.title = $"Degraded SAS path {p} on the disk shelf in {rack.label} U{dev.u}";
                t.body = $"The storage head is logging CRC errors on SAS path {p} to {dev.Name} ({rack.label} U{dev.u}). Path {other} is carrying all the I/O on its own. " +
                         $"At the rear, replace the path {p} cable: unplug ONLY the degraded cable (amber link light), fit a new one from the shelf's IOM to the storage head's HBA port {p}. " +
                         $"Pull path {other} and the customer loses the whole shelf.";
                t.tasks.Add(new WorkTask { kind = TaskKind.SasPath, rack = rack.label, deviceId = dev.id, u = dev.u, slot = path });
            }
            else
            {
                dev.failedBay = rng.Next(dev.Def.bays);
                t.title = $"Predictive drive failure in the disk shelf, {rack.label} U{dev.u}";
                t.body = $"SMART says the drive in bay {dev.failedBay} of {dev.Name} ({rack.label} U{dev.u}, {Age(career, dev)}) is about to fail. It hasn't failed yet, so there's no amber light: " +
                         "the controller has lit its blue LOCATE light instead. Hot-swap it with a spare drive before it dies, and don't touch the 23 healthy ones.";
                t.tasks.Add(new WorkTask { kind = TaskKind.SwapDrive, rack = rack.label, deviceId = dev.id, u = dev.u, component = "locate" });
            }
            return t;
        }

        /// <summary>A rack running hot: open U slots with no blanking panels, a bad floor-tile layout, or a dirty CRAC.</summary>
        Ticket GenCooling()
        {
            var busyRacks = BusyRacks();
            double roll = rng.NextDouble();
            var hotRack = racks.FirstOrDefault(r => r.hot && r.owner != null && !busyRacks.Contains(r.label));
            if (hotRack != null && hotRack.openGaps.Count > 0) roll = 0;
            if (roll < 0.45)
            {
                var cands = Owned.Where(r => !busyRacks.Contains(r.label) && FreeSlots(r).Count >= 3).ToList();
                if (cands.Count == 0) return null;
                var rack = hotRack != null && cands.Contains(hotRack) ? hotRack : Prefer(cands, r => r.openGaps.Count > 0, 0.8);
                var free = FreeSlots(rack).Where(u => !rack.openGaps.Contains(u)).OrderBy(_ => rng.Next()).ToList();
                for (int i = 0; rack.openGaps.Count < 3 + rng.Next(3) && i < free.Count; i++) rack.openGaps.Add(free[i]);
                rack.hot = true;
                var t = NewTicket(TicketType.Cooling, rack, rack.owner, 60 + 12 * rack.openGaps.Count, 2);
                t.title = $"Hot spot: {rack.label} inlet at {30 + rack.openGaps.Count}°C";
                t.body = $"The inlet sensors on {rack.label} read {30 + rack.openGaps.Count}°C (the limit is 27°C). Hot exhaust is leaking back to the front through {rack.openGaps.Count} empty U slots with no blanking panels. " +
                         "At the front of the rack, fit a blanking panel into every open gap. Panels go in empty slots only: don't force one over a unit.";
                t.tasks.Add(new WorkTask { kind = TaskKind.Blanking, rack = rack.label });
                return t;
            }
            if (roll < 0.75)
            {
                var rows = Enumerable.Range(0, Rows.Length).Where(r => racks.Count(x => x.row == r && x.owner != null && x.devices.Count > 0) >= 3 && !racks.Any(x => x.row == r && busyRacks.Contains(x.label))).ToList();
                if (rows.Count == 0) return null;
                int row = Pick(rows);
                var rowRacks = racks.Where(x => x.row == row && x.owner != null && x.devices.Count > 0).OrderByDescending(x => x.devices.Sum(d => d.Def.watts)).ToList();
                foreach (var r in rowRacks.Take(2)) r.hot = true;
                var top = rowRacks[0];
                var t = NewTicket(TicketType.Cooling, top, top.owner, 110);
                t.title = $"Row {Rows[row]} cold aisle running warm";
                t.body = $"Several racks in row {Rows[row]} are pulling inlet air above 27°C. Someone moved the perforated floor tiles: some are blowing cold air in front of empty racks. " +
                         $"Go to the cold aisle in front of row {Rows[row]} and move the perforated tiles in front of the racks that need them, until every loaded rack reads 27°C or less.";
                t.tasks.Add(new WorkTask { kind = TaskKind.Tiles, rack = top.label, station = $"aisle{row}", u = row, slot = rng.Next(1, int.MaxValue) });
                return t;
            }
            int crac = 1 + rng.Next(2);
            var served = racks.Where(x => CracFor(x.row) == crac && x.owner != null && x.devices.Count > 0).ToList();
            if (served.Count == 0 || TasksAtStation($"crac{crac}").Any()) return null;
            foreach (var r in served.OrderBy(_ => rng.Next()).Take(3)) r.hot = true;
            var hottest = served.First(x => x.hot);
            var tk = NewTicket(TicketType.Cooling, hottest, hottest.owner, 120, 2);
            tk.title = $"CRAC-{crac}: dirty filter alarm, rows {Rows[(crac - 1) * 2]} and {Rows[(crac - 1) * 2 + 1]} warming up";
            tk.body = $"CRAC-{crac} on the west wall is raising a high filter pressure-drop alarm, and its rows are warming up. Open its front panel, swap the clogged air filters for clean ones (leave the clean ones in), then reset the alarm on its controller.";
            tk.tasks.Add(new WorkTask { kind = TaskKind.CracService, rack = hottest.label, station = $"crac{crac}", u = crac, slot = rng.Next(1, int.MaxValue) });
            return tk;
        }

        /// <summary>Empty, unreserved U slots in a rack.</summary>
        static List<int> FreeSlots(Rack r)
        {
            var l = new List<int>();
            for (int u = 1; u <= RH.Rack.Units; u++) if (r.At(u) == null && !r.Reserved(u)) l.Add(u);
            return l;
        }

        /// <summary>The rear of a rack is a rat's nest: dress the cables and label them.</summary>
        Ticket GenCableAudit()
        {
            var busyRacks = BusyRacks();
            var cands = Owned.Where(r => !r.tidy && r.devices.Count >= 5 && !busyRacks.Contains(r.label)).ToList();
            if (cands.Count == 0) return null;
            var rack = Pick(cands);
            var t = NewTicket(TicketType.CableAudit, rack, rack.owner, 80 + 5 * rack.devices.Count);
            t.title = $"Cable audit: tidy the rear of {rack.label}";
            t.body = $"{rack.owner}'s auditors flagged the rear of {rack.label}: unlabelled patch cables hanging in loops across the exhaust. " +
                     "Dress every cable into the vertical cable manager on its own side of the rack (don't cross the rack), then label each one with the port it comes from. " +
                     "A tidy rack is much easier to work on next time.";
            t.tasks.Add(new WorkTask { kind = TaskKind.CableTidy, rack = rack.label, slot = rng.Next(1, int.MaxValue) });
            return t;
        }

        /// <summary>An inbound pallet of spares: your own orders, or procurement's routine restock.</summary>
        Ticket GenReceiving(bool fromOrders)
        {
            if (TasksAtStation("receiving").Any()) return null;
            var t = NewTicket(TicketType.Receiving, null, null, 0);
            int po = 4000 + rng.Next(5000);
            if (fromOrders)
            {
                foreach (var o in career.orders)
                    for (int left = o.count; left > 0; left -= 3)
                        t.cartons.Add(new Carton { part = o.part, qty = Math.Min(3, left), ordered = true, po = $"PO-{po}" });
                career.orders.Clear();
            }
            else
            {
                po = 9000 + rng.Next(900);
                foreach (var p in Catalog.Parts.OrderBy(_ => rng.Next()).Take(2 + rng.Next(2)))
                    t.cartons.Add(new Carton { part = p, qty = 1 + rng.Next(3), po = $"PO-{po}" });
            }
            if (t.cartons.Count == 0) return null;
            foreach (var c in t.cartons) c.damaged = rng.NextDouble() < 0.18;
            if (rng.NextDouble() < 0.5)
                t.cartons.Insert(rng.Next(t.cartons.Count + 1), new Carton { part = Pick(Catalog.Parts), qty = 1 + rng.Next(2), foreign = true, po = $"PO-{1000 + rng.Next(3000)}" });
            t.pay = 35 + 10 * t.cartons.Count;
            t.title = fromOrders ? $"Receiving: your parts order is at the dock ({t.cartons.Count} cartons)" : $"Receiving: procurement restock at the dock ({t.cartons.Count} cartons)";
            t.body = $"An inbound pallet for PO-{po} is waiting at the receiving bay by the loading dock. Check every carton against the packing list before you sign for it. " +
                     "Refuse anything crushed or with a tripped tip-and-tell indicator, and anything addressed to another site. Accepted parts go straight onto the storage room shelves." +
                     (fromOrders ? "\nRefused cartons from your order are re-shipped for free." : "");
            t.tasks.Add(new WorkTask { kind = TaskKind.Receive, station = "receiving" });
            return t;
        }

        static readonly string[] First = { "Dana", "Ravi", "Mei", "Tom", "Ines", "Kofi", "Sara", "Luca", "Noor", "Jonas", "Priya", "Owen" };
        static readonly string[] Last = { "Okafor", "Lindqvist", "Tanaka", "Moreau", "Silva", "Brennan", "Haddad", "Novak", "Reyes", "Fischer", "Chen", "Walsh" };

        /// <summary>A customer's engineer is on site: check them in, walk them to their rack, stay with them, walk them out.</summary>
        Ticket GenEscort()
        {
            if (OpenTickets.Any(t => t.type == TicketType.Escort)) return null;
            var cands = Owned.Where(r => r.devices.Count >= 3).ToList();
            if (cands.Count == 0) return null;
            var rack = Pick(cands);
            string name = $"{Pick(First)} {Pick(Last)}";
            bool impostor = rng.NextDouble() < 0.25;
            var t = NewTicket(TicketType.Escort, rack, rack.owner, 100, 2);
            t.title = $"Visitor: {name} from {rack.owner} for {rack.label}";
            t.body = $"{name} says they're an engineer from {rack.owner}, here to look at {rack.label}. They're waiting at the security kiosk by the entrance. " +
                     $"Check their ID against {rack.owner}'s access list. If it all matches, badge them in, walk them to {rack.label}, stay close while they work, then walk them back to the kiosk. " +
                     "Never leave a visitor alone on the floor. If anything doesn't match, deny entry.";
            t.tasks.Add(new WorkTask { kind = TaskKind.Badge, rack = rack.label, station = "kiosk", tag = name, component = impostor ? "deny" : "allow", version = impostor ? Pick(new[] { "expired", "unlisted", "company" }) : null, slot = rng.Next(1, int.MaxValue) });
            t.tasks.Add(new WorkTask { kind = TaskKind.Escort, rack = rack.label, component = "in", tag = name });
            t.tasks.Add(new WorkTask { kind = TaskKind.Escort, rack = rack.label, station = "kiosk", component = "out", tag = name });
            return t;
        }

        /// <summary>Optional preventive maintenance: read the BMC sensors on a few servers and flag what's drifting.</summary>
        Ticket GenMaintenance()
        {
            var busy = Busy();
            var cands = Owned.Where(r => (r.pmDay == 0 || career.day - r.pmDay > 10) &&
                                         r.devices.Count(d => d.Def.kind == DevKind.Server && d.Healthy && !busy.Contains(d.id)) >= 2).ToList();
            if (cands.Count == 0) return null;
            var rack = Pick(cands);
            var devs = rack.devices.Where(d => d.Def.kind == DevKind.Server && d.Healthy && !busy.Contains(d.id)).OrderBy(d => d.installDay).Take(2 + rng.Next(2)).ToList();
            var t = NewTicket(TicketType.Maintenance, rack, rack.owner, 25 + 20 * devs.Count);
            foreach (var d in devs) t.tasks.Add(new WorkTask { kind = TaskKind.HealthCheck, rack = rack.label, deviceId = d.id, u = d.u, slot = rng.Next(1, int.MaxValue) });
            t.title = $"Preventive maintenance: health check in {rack.label}";
            t.body = $"{rack.owner} would like a preventive health check on {devs.Count} of their oldest servers in {rack.label} ({string.Join(", ", devs.Select(d => $"U{d.u}"))}). " +
                     "On each one, read the BMC sensor table and flag every reading outside its limits before it turns into an outage. " +
                     "It doesn't pay much, but a rack that's had maintenance fails a lot less over the next ten days.";
            return t;
        }

        void GenerateOffers()
        {
            offers.Clear();
            if (career.day < 5) return;
            int n = 1 + rng.Next(2);
            for (int i = 0; i < n; i++)
            {
                var t = GenMaintenance();
                if (t != null) offers.Add(t);
            }
        }

        /// <summary>Take an optional job onto today's work order.</summary>
        public void AcceptOffer(Ticket t)
        {
            if (!offers.Remove(t)) return;
            tickets.Add(t);
            TicketArrived?.Invoke(t);
        }

        // ================================================================ facility emergencies
        void PlanEmergency()
        {
            emergencyAfter = 0;
            emergencyKind = null;
            if (career.day < 7 || career.day - career.lastEmergencyDay < 4 || rng.NextDouble() >= 0.25) return;
            emergencyAfter = 1 + rng.Next(3);
            emergencyKind = Pick(EmergencyKinds);
        }

        /// <summary>Something goes badly wrong with the building itself. Lands mid-shift as a P1.</summary>
        public Ticket TriggerEmergency(string kind)
        {
            emergencyAfter = 0;
            var t = GenEmergency(kind);
            if (t == null) return null;
            career.lastEmergencyDay = career.day;
            tickets.Add(t);
            TicketArrived?.Invoke(t);
            return t;
        }

        Ticket GenEmergency(string kind)
        {
            if (OpenTickets.Any(t => t.type == TicketType.Emergency)) return null;
            switch (kind)
            {
                case "power":
                {
                    var t = NewTicket(TicketType.Emergency, null, null, 260, 1);
                    t.variant = kind;
                    string cause = Pick(new[] { "estop", "fuel", "battery" });
                    t.title = "UTILITY POWER FAILURE: the generator didn't start";
                    t.body = $"The utility feed just dropped. The hall is running on UPS batteries and generator 1 failed to start. Get to the generator controller on the plant wall by the entrance, " +
                             $"clear whatever stopped it, start the engine and transfer the load before the batteries run out (about {UpsSeconds / 60f:0.#} minutes). If the UPS runs dry, the whole hall goes dark.";
                    t.tasks.Add(new WorkTask { kind = TaskKind.Generator, station = "plant", component = cause });
                    ups = UpsSeconds;
                    return t;
                }
                case "leak":
                {
                    int row = rng.Next(Rows.Length);
                    var rowRacks = racks.Where(r => r.row == row).OrderBy(r => r.col).ToList();
                    var rack = rowRacks[rng.Next(2, Math.Max(3, rowRacks.Count - 2))];
                    var t = NewTicket(TicketType.Emergency, rack, rack.owner, 180, 1);
                    t.variant = kind;
                    int crac = CracFor(row);
                    t.title = $"WATER LEAK detected in front of {rack.label}";
                    t.body = $"The leak detection cable is wet in the row {Rows[row]} cold aisle, in front of {rack.label}. A chilled-water line feeding row {Rows[row]} has sprung a leak. " +
                             $"Go to CRAC-{crac} on the west wall and close the supply and return valves for row {Rows[row]} (only those two: the other rows still need their cooling), then clean up the water before it reaches anything.";
                    t.tasks.Add(new WorkTask { kind = TaskKind.Valve, rack = rack.label, station = $"crac{crac}", u = row });
                    t.tasks.Add(new WorkTask { kind = TaskKind.Mop, rack = rack.label, station = "leak", u = row });
                    return t;
                }
                default:
                {
                    var devs = IdleDevices(d => d.Def.kind == DevKind.Server && d.Def.psus > 0);
                    if (devs.Count == 0) return null;
                    var dev = Oldest(devs);
                    var rack = RackOf(dev);
                    dev.failedPsu = rng.Next(dev.Def.psus);
                    var t = NewTicket(TicketType.Emergency, rack, dev.owner, 200, 1);
                    t.variant = "vesda";
                    t.title = $"SMOKE ALARM: VESDA pre-alarm at {rack.label}";
                    t.body = $"The smoke detection system picked up something burning at the rear of {rack.label}. It's power supply {(dev.failedPsu == 0 ? "A" : "B")} in {dev.Name} at U{dev.u}: it's cooking. " +
                             "Swap it out from the hot aisle before it catches, then reset the fire alarm panel on the plant wall by the entrance so the gas suppression stands down.";
                    t.tasks.Add(new WorkTask { kind = TaskKind.SwapPsu, rack = rack.label, deviceId = dev.id, u = dev.u });
                    t.tasks.Add(new WorkTask { kind = TaskKind.ResetAlarm, rack = rack.label, station = "plant" });
                    return t;
                }
            }
        }

        /// <summary>Run the clock on anything that has one: the UPS batteries, and the trainee's work.</summary>
        public void Tick(float dt)
        {
            if (ups > 0)
            {
                ups -= dt;
                if (ups <= 0) Blackout();
            }
            TickTrainee(dt);
        }

        /// <summary>The UPS ran dry before the generator took the load: the whole hall went dark.</summary>
        void Blackout()
        {
            ups = 0;
            var t = OpenTickets.FirstOrDefault(x => x.type == TicketType.Emergency && x.variant == "power");
            if (t == null) return;
            t.status = TicketStatus.HandedOver;
            t.mistakes++;
            foreach (var task in t.tasks) task.done = true;
            ChangeRep(-12f);
            foreach (var c in Owned.Select(r => r.owner).Distinct()) career.ChangeSat(c, -5f);
            career.stats.streak = 0;
            Message?.Invoke("The UPS batteries ran dry and the whole hall went dark until utility power came back. Every customer noticed (-12 rep).", 3);
            TicketDone?.Invoke(t);
        }

        // ================================================================ the trainee
        public bool HasTrainee => career.Level >= Catalog.TraineeLevel;
        public Ticket TraineeTicket => OpenTickets.FirstOrDefault(t => t.trainee);

        /// <summary>Why this ticket can't go to the trainee right now (null when it can).</summary>
        public string TraineeBlocker(Ticket t)
        {
            if (!HasTrainee) return $"You get a trainee at {Catalog.LevelTitles[Catalog.TraineeLevel - 1]}";
            if (!t.Open || t.trainee) return "Already handled";
            if (TraineeTicket != null) return "Your trainee is busy";
            if (t.followUp) return "Clean up your own mistakes";
            if (t.type != TicketType.Firmware && t.type != TicketType.ReplaceHardware && t.type != TicketType.Expand &&
                t.type != TicketType.SmartHands && t.type != TicketType.CableAudit && t.type != TicketType.Maintenance) return "Too tricky for a trainee";
            if (t.tasks.Any(x => x.done)) return "You've already started it";
            foreach (var p in PartsFor(t)) if (career.Stock(p) <= 0) return $"No {Catalog.PartName(p)} in stock";
            return null;
        }

        static IEnumerable<PartKind> PartsFor(Ticket t) => t.tasks.Select(x => x.kind switch
        {
            TaskKind.SwapDrive => (PartKind?)PartKind.Drive,
            TaskKind.SwapPsu => PartKind.Psu,
            TaskKind.SwapSfp => PartKind.Sfp,
            TaskKind.SwapDimm => PartKind.Dimm,
            TaskKind.SwapFan => PartKind.Fan,
            _ => null,
        }).Where(p => p.HasValue).Select(p => p.Value);

        public bool AssignTrainee(Ticket t)
        {
            if (TraineeBlocker(t) != null) return false;
            // The trainee takes their spares off the shelf when they do the job (see Apply at night).
            t.trainee = true;
            t.traineeTime = 40f + 20f * t.tasks.Count;
            foreach (var m in t.tasks.Where(x => x.kind == TaskKind.Mount)) Rack(m.rack)?.reserved.RemoveAll(r => r.x == m.u);
            Message?.Invoke($"Your trainee took ticket #{t.id}. They'll radio in when it's done.", 0);
            return true;
        }

        void TickTrainee(float dt)
        {
            var t = TraineeTicket;
            if (t == null) return;
            t.traineeTime -= dt;
            if (t.traineeTime > 0) return;
            bool ok = rng.NextDouble() < career.TraineeSkill;
            foreach (var task in t.tasks.Where(x => !x.done))
            {
                // Reserved slots were released on assignment; installs go straight in.
                Apply(task, t, true);
                task.done = true;
            }
            career.traineeDone++;
            if (!ok)
            {
                t.mistakes++;
                var rack = Rack(t.rack);
                var dev = rack?.devices.Where(d => d.Def.psus > 0).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (dev != null)
                    FollowUp(rack.label, dev, TaskKind.PowerOn, Fault.NoPower, $"Trainee knocked out {dev.Name} ({rack.label} U{dev.u})",
                        $"While working on ticket #{t.id}, your trainee caught both power cords of {dev.Name}. Power it back on and boot it from its OS disk. You're the one signing off their work.");
            }
            FinishTicket(t);
        }

        // ================================================================ stock and RMA
        /// <summary>Take a spare part off the storage room shelf into the pouch. False when the shelf is empty.</summary>
        public bool TakePart(PartKind k)
        {
            if (career.Stock(k) <= 0 || inv.PouchFull) return false;
            career.AddStock(k, -1);
            inv.pouch.Add(k);
            return true;
        }

        /// <summary>Put an unused part from the pouch back on the shelf.</summary>
        public void ReturnPart(PartKind k)
        {
            if (inv.pouch.Remove(k)) career.AddStock(k, 1);
        }

        /// <summary>Hand every failed part in the pouch to the vendor: half the part's price comes back as RMA credit.</summary>
        public int ReturnRma()
        {
            int credit = 0;
            foreach (var p in inv.rma) credit += Mathf.RoundToInt(Catalog.PartPrice(p) * Catalog.RmaCredit);
            career.stats.rmaReturned += inv.rma.Count;
            inv.rma.Clear();
            career.money += credit;
            rmaCredit += credit;
            return credit;
        }

        /// <summary>Order spares from procurement; they arrive on a pallet at the dock tomorrow morning. Returns why not, or null.</summary>
        public string Order(PartKind k, int n)
        {
            int cost = Catalog.PartPrice(k) * n;
            if (career.money < cost) return "Not enough money";
            career.money -= cost;
            career.AddOrder(k, n);
            career.stats.partsOrdered += n;
            return null;
        }

        // ================================================================ power layouts (PDU rebalancing)
        /// <summary>One device's two power cords. Each cord is 0 (unplugged), 1 (feed A) or 2 (feed B).</summary>
        public class PowerRow
        {
            public Device dev;
            public float amps;
            public int c1, c2;
            public bool dead;
            public int Live => (c1 != 0 ? 1 : 0) + (c2 != 0 ? 1 : 0);
            public bool Redundant => (c1 == 1 && c2 == 2) || (c1 == 2 && c2 == 1);
        }

        /// <summary>
        /// The rack's power as the previous technician left it: deterministic for a ticket's seed, with
        /// enough units single-fed or doubled up on A that feed A runs over 80% of its breaker.
        /// Balanced (one cord per feed) it always lands near 71%.
        /// </summary>
        public static List<PowerRow> PowerLayout(Rack rack, int seed, out float breaker)
        {
            var r = new System.Random(seed);
            var rows = rack.devices.OrderByDescending(d => d.u)
                .Select(d => new PowerRow { dev = d, amps = Mathf.Max(0.5f, d.Def.watts / 208f), c1 = 1, c2 = 2 }).ToList();
            float total = rows.Sum(x => x.amps);
            breaker = Mathf.Ceil(total * 0.7f);
            int broken = 0;
            foreach (var row in rows.OrderBy(_ => r.Next()))
            {
                if (broken >= 2 && FeedLoad(rows, 1) > breaker * 0.83f) break;
                if (r.Next(2) == 0) row.c2 = 0; else row.c2 = 1;
                broken++;
            }
            return rows;
        }

        /// <summary>Current on a feed: each live cord carries an equal share of its unit's load.</summary>
        public static float FeedLoad(List<PowerRow> rows, int feed)
        {
            float a = 0;
            foreach (var x in rows)
            {
                if (x.dead || x.Live == 0) continue;
                if (x.c1 == feed) a += x.amps / x.Live;
                if (x.c2 == feed) a += x.amps / x.Live;
            }
            return a;
        }

        // ================================================================ floor tiles (cooling)
        /// <summary>The cold aisle in front of a row: two tiles per rack, some perforated.</summary>
        public class TileLayout
        {
            public List<Rack> racks;
            /// <summary>[col index, depth 0 next to the racks / 1 further out]: perforated?</summary>
            public bool[,] perf;
            public int Cols => racks.Count;
        }

        /// <summary>
        /// The row's floor tiles as someone left them: enough perforated tiles to cool every loaded rack,
        /// but some are wasted in front of empty racks. Deterministic for a ticket's seed.
        /// </summary>
        public static TileLayout Tiles(List<Rack> hall, int row, int seed)
        {
            var r = new System.Random(seed);
            var l = new TileLayout { racks = hall.Where(x => x.row == row).OrderBy(x => x.col).ToList() };
            l.perf = new bool[l.Cols, 2];
            int need = 0;
            for (int i = 0; i < l.Cols; i++) need += TilesNeeded(l.racks[i]);
            var slots = new List<(int, int)>();
            for (int i = 0; i < l.Cols; i++) for (int d = 0; d < 2; d++) slots.Add((i, d));
            // Scatter them, favouring the wrong places, until the layout is actually broken.
            for (int tries = 0; tries < 50; tries++)
            {
                System.Array.Clear(l.perf, 0, l.perf.Length);
                var order = slots.OrderBy(s => (TilesNeeded(l.racks[s.Item1]) == 0 ? 0 : 1) * 2 + r.Next(3)).ToList();
                for (int k = 0; k < need && k < order.Count; k++) l.perf[order[k].Item1, order[k].Item2] = true;
                if (Enumerable.Range(0, l.Cols).Any(i => InletTemp(l, i) > 27f)) break;
            }
            return l;
        }

        /// <summary>Loaded racks need one perforated tile in front, heavy ones (over 4 kW) two.</summary>
        public static int TilesNeeded(Rack r) => r.devices.Count == 0 ? 0 : r.devices.Sum(d => d.Def.watts) > 4000 ? 2 : 1;

        /// <summary>Inlet temperature of rack i: hot with no cold air, cooled by perforated tiles in front (and a little by the neighbours').</summary>
        public static float InletTemp(TileLayout l, int i)
        {
            var rack = l.racks[i];
            if (rack.devices.Count == 0) return 22f;
            float cool = 0;
            for (int d = 0; d < 2; d++)
            {
                if (l.perf[i, d]) cool += 1f;
                if (i > 0 && l.perf[i - 1, d]) cool += 0.15f;
                if (i < l.Cols - 1 && l.perf[i + 1, d]) cool += 0.15f;
            }
            float need = TilesNeeded(rack);
            return Mathf.Round((21f + 9f * Mathf.Clamp01(1f - cool / need)) * 10f) / 10f;
        }

        // ================================================================ doing the work
        WorkTask Sibling(WorkTask t, TaskKind kind, bool sameTag)
        {
            var ticket = TicketOf(t);
            return ticket?.tasks.Find(x => x.kind == kind && (sameTag ? x.tag == t.tag : x.deviceId == t.deviceId));
        }

        /// <summary>Why a task can't be started right now (null when it can).</summary>
        public string Blocker(WorkTask t)
        {
            if (t.done) return "Done";
            var ticket = TicketOf(t);
            if (ticket != null && ticket.trainee) return "Your trainee is on it";
            var rack = Rack(t.rack);
            switch (t.kind)
            {
                case TaskKind.Mount:
                    if (inv.Has(t.def, t.tag)) return null;
                    return t.tag != null ? $"Bring the {Catalog.Dev(t.def).name} ({t.tag}) from the loading dock" : $"Bring a {Catalog.Dev(t.def).name} from the storage room";
                case TaskKind.Cable:
                    var d = rack.At(t.u);
                    return d != null && d.u == t.u && d.def == t.def ? null : "Mount the equipment first";
                case TaskKind.Unmount:
                    if (Sibling(t, TaskKind.Uncable, false) is WorkTask un && !un.done) return "Unplug its cables at the rear first";
                    return inv.HandsFull ? "Free your hands first: the unit comes out into your arms" : null;
                case TaskKind.Wipe:
                    return inv.Has(t.def, t.tag) ? null : $"Carry the de-racked unit ({t.tag}) to the wipe station";
                case TaskKind.Ship:
                    if (Sibling(t, TaskKind.Wipe, true) is WorkTask w && !w.done) return "Wipe its drives at the bench first";
                    return inv.Has(t.def, t.tag) ? null : $"Carry the de-racked unit ({t.tag}) to the dock";
                case TaskKind.SwapDrive: return inv.HasPart(PartKind.Drive) ? null : "Need a spare drive from the storage room";
                case TaskKind.SwapPsu: return inv.HasPart(PartKind.Psu) ? null : "Need a spare PSU from the storage room";
                case TaskKind.SwapSfp: return inv.HasPart(PartKind.Sfp) ? null : "Need an SFP+ optic from the storage room";
                case TaskKind.SwapDimm: return inv.HasPart(PartKind.Dimm) ? null : "Need a spare DIMM from the storage room";
                case TaskKind.SwapFan: return inv.HasPart(PartKind.Fan) ? null : "Need a spare fan from the storage room";
                case TaskKind.Diagnose: return t.cart && !inv.Has("kvm", null) ? "Bring the crash cart (KVM) from the storage room" : null;
                case TaskKind.Patch:
                    return ticket?.tasks.Find(x => x.kind == TaskKind.Trace) is WorkTask tr && !tr.done ? $"Trace the customer's cable at {t.rack} first" : null;
                case TaskKind.Escort:
                    if (ticket?.tasks.Find(x => x.kind == TaskKind.Badge) is WorkTask b && !b.done) return "Check the visitor in at the security kiosk first";
                    if (t.component == "out" && ticket?.tasks.Find(x => x.kind == TaskKind.Escort && x.component == "in") is WorkTask inTask && !inTask.done)
                        return $"Take the visitor to {t.rack} first";
                    return t.component == "out" ? "Walk the visitor back to the security kiosk" : $"Walk the visitor to the front of {t.rack} and stay with them";
                case TaskKind.Mop:
                    return ticket?.tasks.Find(x => x.kind == TaskKind.Valve) is WorkTask v && !v.done ? $"Close the leaking loop's valves at CRAC-{CracFor(t.u)} first" : null;
                case TaskKind.ResetAlarm:
                    return ticket?.tasks.Find(x => x.kind == TaskKind.SwapPsu) is WorkTask s && !s.done ? "Deal with the smoking PSU first" : null;
                default: return null;
            }
        }

        public static string Describe(WorkTask t)
        {
            string name = t.def != null ? Catalog.Dev(t.def).name : null;
            switch (t.kind)
            {
                case TaskKind.Mount: return $"Rack {name} at U{t.u}" + (t.tag != null ? $" ({t.tag})" : "");
                case TaskKind.Cable: return $"Cable {name} at U{t.u}";
                case TaskKind.SwapDrive: return t.component == "locate" ? $"Swap the drive with the blue LOCATE light (U{t.u})" : $"Hot-swap the failed drive (U{t.u})";
                case TaskKind.SwapPsu: return $"Replace the failed PSU (U{t.u})";
                case TaskKind.SwapSfp: return $"Replace the faulty SFP+ optic (U{t.u})";
                case TaskKind.SwapDimm: return $"Replace the failed memory module (U{t.u})";
                case TaskKind.SwapFan: return $"Replace the failed fan (U{t.u})";
                case TaskKind.Firmware: return $"Flash {t.component} {t.version} (U{t.u})";
                case TaskKind.Diagnose: return $"Diagnose the outage (U{t.u})" + (t.cart ? " with the crash cart" : "");
                case TaskKind.PowerCycle: return $"Hard power-cycle the hung {(t.u == 42 ? "switch" : "server")} (U{t.u})";
                case TaskKind.Reseat: return $"Reseat the loose network cable (U{t.u})";
                case TaskKind.Uncable: return $"Unplug the {name} at U{t.u}";
                case TaskKind.Unmount: return $"De-rack the {name} at U{t.u} ({t.tag})";
                case TaskKind.Wipe: return $"Wipe and shred the drives ({t.tag})";
                case TaskKind.Ship: return $"Leave the unit ({t.tag}) on the outbound pallet";
                case TaskKind.Inspect: return t.component == "serial" ? $"Read the serial number at U{t.u}" : $"Read the hostname tag at U{t.u}";
                case TaskKind.Photo: return $"Photograph the {t.component} of {t.rack}";
                case TaskKind.Power: return $"Restore A+B power redundancy in {t.rack}";
                case TaskKind.Trace: return $"Trace the {t.component} cable from U{t.u}";
                case TaskKind.Patch: return t.u > 0 ? $"Patch {t.rack} PP-{t.u} to {t.component} port {t.slot}" : $"Patch the cross-connect to {t.component}";
                case TaskKind.PowerOn: return $"Power the server back on (U{t.u})";
                case TaskKind.ConfigRestore: return $"Restore the switch configuration (U{t.u})";
                case TaskKind.SasPath: return $"Replace the degraded SAS path {(t.slot == 0 ? "A" : "B")} cable (U{t.u})";
                case TaskKind.Blanking: return $"Fit blanking panels in the open gaps of {t.rack}";
                case TaskKind.Tiles: return $"Rearrange the perforated tiles in front of row {Rows[Mathf.Clamp(t.u, 0, Rows.Length - 1)]}";
                case TaskKind.CracService: return $"Swap the clogged filters in CRAC-{t.u}";
                case TaskKind.CableTidy: return $"Dress and label the cables at the rear of {t.rack}";
                case TaskKind.Receive: return "Check the inbound pallet against the packing list";
                case TaskKind.Badge: return $"Check {t.tag}'s ID at the security kiosk";
                case TaskKind.Escort: return t.component == "out" ? $"Walk {t.tag} back to the kiosk" : $"Escort {t.tag} to {t.rack}";
                case TaskKind.HealthCheck: return $"Health check: flag out-of-spec sensors (U{t.u})";
                case TaskKind.Generator: return "Start generator 1 and transfer the load";
                case TaskKind.Valve: return $"Close the row {Rows[Mathf.Clamp(t.u, 0, Rows.Length - 1)]} chilled-water valves at CRAC-{CracFor(t.u)}";
                case TaskKind.Mop: return $"Clean up the water in front of {t.rack}";
                case TaskKind.ResetAlarm: return "Reset the fire alarm panel";
                default: return $"Rebuild the RAID array (U{t.u})";
            }
        }

        /// <summary>Apply a successfully finished job to the world.</summary>
        public void Complete(WorkTask t)
        {
            if (t.done) return;
            var ticket = TicketOf(t);
            Apply(t, ticket, false);
            t.done = true;
            if (ticket != null && ticket.Open && ticket.tasks.All(x => x.done)) FinishTicket(ticket);
        }

        /// <summary>A failed part comes out into an RMA bag (the night shift just bins them).</summary>
        void SwapOut(PartKind k, bool night)
        {
            if (night) { career.AddStock(k, -1); return; }
            inv.TakePart(k);
            inv.rma.Add(k);
            career.stats.partsSwapped++;
        }

        /// <summary>The world side of a job. At night the night shift does it without the player's inventory.</summary>
        void Apply(WorkTask t, Ticket ticket, bool night)
        {
            var rack = Rack(t.rack);
            var dev = t.deviceId > 0 ? rack?.devices.Find(d => d.id == t.deviceId) : null;
            switch (t.kind)
            {
                case TaskKind.Mount:
                    if (!night) inv.Take(t.def, t.tag);
                    rack.reserved.RemoveAll(r => r.x == t.u);
                    int h = Catalog.Dev(t.def).heightU;
                    if (rack.Free(t.u, h))
                    {
                        var nd = AddDevice(rack, t.def, t.u, ticket?.customer);
                        foreach (var comp in Catalog.Components) nd.SetFirmware(comp, Latest(comp));
                        rack.openGaps.RemoveAll(g => g >= t.u && g < t.u + h);
                    }
                    break;
                case TaskKind.Unmount:
                    if (dev != null)
                    {
                        rack.devices.Remove(dev);
                        // Nobody fits blanking panels on the way out.
                        for (int u = dev.u; u <= dev.Top; u++) if (!rack.openGaps.Contains(u)) rack.openGaps.Add(u);
                    }
                    if (!night) inv.hands.Add(new Item { def = t.def, tag = t.tag, label = $"Decom {Catalog.Dev(t.def).name} ({t.tag})" });
                    break;
                case TaskKind.Ship: if (!night) inv.Take(t.def, t.tag); break;
                case TaskKind.Wipe: if (!night) career.stats.drivesShredded += Catalog.Dev(t.def)?.bays ?? 0; break;
                case TaskKind.SwapDrive: SwapOut(PartKind.Drive, night); if (dev != null) dev.failedBay = -1; break;
                case TaskKind.SwapPsu: SwapOut(PartKind.Psu, night); if (dev != null) dev.failedPsu = -1; break;
                case TaskKind.SwapSfp:
                    SwapOut(PartKind.Sfp, night);
                    if (dev != null) { dev.badSfp = -1; if (dev.fault == Fault.BadSfp) dev.fault = Fault.None; }
                    break;
                case TaskKind.SwapDimm:
                    SwapOut(PartKind.Dimm, night);
                    if (dev != null) { dev.badDimm = -1; if (dev.fault == Fault.BadDimm) dev.fault = Fault.None; }
                    break;
                case TaskKind.SwapFan:
                    SwapOut(PartKind.Fan, night);
                    if (dev != null) { dev.badFan = -1; if (dev.fault == Fault.FanFail) dev.fault = Fault.None; }
                    break;
                case TaskKind.Firmware: dev?.SetFirmware(t.component, t.version); break;
                case TaskKind.PowerCycle: if (dev != null && dev.fault == Fault.Hung) dev.fault = Fault.None; break;
                case TaskKind.PowerOn: if (dev != null && dev.fault == Fault.NoPower) dev.fault = Fault.None; break;
                case TaskKind.Reseat: if (dev != null && dev.fault == Fault.LinkDown) dev.fault = Fault.None; break;
                case TaskKind.RaidRebuild: if (dev != null && dev.fault == Fault.ArrayCritical) dev.fault = Fault.None; break;
                case TaskKind.ConfigRestore: if (dev != null && dev.fault == Fault.ConfigLost) dev.fault = Fault.None; break;
                case TaskKind.Trace:
                    var patch = ticket?.tasks.Find(x => x.kind == TaskKind.Patch);
                    if (patch != null) patch.u = t.slot;
                    break;
                case TaskKind.Diagnose:
                    if (night || dev == null || ticket == null) break;
                    var kind = dev.fault switch
                    {
                        Fault.BadSfp => TaskKind.SwapSfp,
                        Fault.Hung => TaskKind.PowerCycle,
                        Fault.BadDimm => TaskKind.SwapDimm,
                        Fault.FanFail => TaskKind.SwapFan,
                        Fault.ConfigLost => TaskKind.ConfigRestore,
                        _ => TaskKind.Reseat,
                    };
                    // Keep the customer's evidence photo last.
                    int at = ticket.tasks.FindIndex(x => x.kind == TaskKind.Photo && !x.done);
                    var fix = new WorkTask { kind = kind, rack = t.rack, deviceId = dev.id, u = dev.u, cart = t.cart && kind == TaskKind.ConfigRestore };
                    if (at >= 0) ticket.tasks.Insert(at, fix); else ticket.tasks.Add(fix);
                    Message?.Invoke($"Diagnosis: {FaultName(dev.fault)}. Next: {Describe(fix)}", 0);
                    break;
                case TaskKind.Blanking:
                    if (rack != null) { rack.openGaps.Clear(); rack.hot = false; }
                    break;
                case TaskKind.Tiles:
                    foreach (var r in racks.Where(x => x.row == t.u)) r.hot = false;
                    break;
                case TaskKind.CracService:
                    foreach (var r in racks.Where(x => CracFor(x.row) == t.u)) r.hot = false;
                    break;
                case TaskKind.CableTidy:
                    if (rack != null && !rack.tidy) { rack.tidy = true; career.stats.racksTidied++; }
                    break;
                case TaskKind.Receive:
                    if (ticket == null) break;
                    foreach (var c in ticket.cartons)
                    {
                        bool accept = night ? c.Good : c.decision == 1;
                        if (accept && c.Good) career.AddStock(c.part, c.qty);
                        else if (!accept && !c.foreign && c.ordered) career.AddOrder(c.part, c.qty);
                    }
                    break;
                case TaskKind.Badge:
                    // task.cart is the technician's call: badge them in. Nobody gets in on the night shift.
                    if ((night || !t.cart) && ticket != null)
                    {
                        foreach (var x in ticket.tasks) x.done = true;
                        if (!night && t.component == "deny") career.stats.impostorsCaught++;
                    }
                    break;
                case TaskKind.Escort:
                    if (t.component == "out" && !night) career.stats.visitors++;
                    break;
                case TaskKind.HealthCheck:
                    if (rack != null) rack.pmDay = career.day;
                    if (!night) career.stats.caughtEarly += Anomalies(t.slot);
                    break;
                case TaskKind.Generator: ups = 0; break;
            }
            if (night) dev?.ClearFaults();
        }

        public static string FaultName(Fault f) => f switch
        {
            Fault.LinkDown => "loose network cable",
            Fault.BadSfp => "failed SFP+ optic",
            Fault.Hung => "hung operating system",
            Fault.BadDimm => "failed memory module (DIMM)",
            Fault.FanFail => "failed fan (CPU overheating)",
            Fault.NoPower => "no power",
            Fault.ArrayCritical => "RAID array critical",
            Fault.ConfigLost => "lost startup configuration",
            _ => "none",
        };

        /// <summary>
        /// Pay out a finished ticket. Work done without a single fumble earns a 20% clean-work bonus,
        /// a P1 closed before any lower-priority work earns 25% more, and the customer's mood tips it.
        /// The trainee's work pays 70% (and never the clean bonus: you didn't do it).
        /// </summary>
        void FinishTicket(Ticket t)
        {
            bool clean = t.mistakes == 0;
            bool rush = t.RushBonus && !t.trainee;
            float mul = career.PayMul * career.CustMul(t.customer) * (clean && !t.trainee ? 1.2f : 1f) * (rush ? 1.25f : 1f) * (t.trainee ? 0.7f : 1f);
            int pay = Mathf.RoundToInt(t.pay * mul);
            t.status = TicketStatus.Done;
            // Closing routine work while a P1 waits costs that P1 its rush bonus.
            if (t.priority > 1 && !t.trainee) foreach (var p in OpenTickets) if (p.priority == 1) p.neglected = true;
            earned += pay;
            career.money += pay;
            career.totalEarned += pay;
            completed++;
            career.ticketsDone++;
            int level = career.Level;
            var st = career.stats;
            if (t.followUp)
            {
                ChangeRep(1f);
            }
            else if (t.trainee)
            {
                ChangeRep(clean ? 1f : 0f);
                career.xp += 5;
                career.ChangeSat(t.customer, clean ? 2f : -2f);
            }
            else
            {
                ChangeRep(clean ? 3f : 2f);
                career.xp += 10 + (clean ? 5 : 0) + (rush ? 5 : 0) + (t.type == TicketType.NewContract || t.type == TicketType.Decommission || t.type == TicketType.Emergency ? 5 : 0);
                career.ChangeSat(t.customer, clean ? 4f : 1f);
                st.streak = clean ? st.streak + 1 : 0;
                st.bestStreak = Mathf.Max(st.bestStreak, st.streak);
                if (rush) st.p1First++;
                if (t.type == TicketType.NewContract) st.racksBuilt++;
                if (t.type == TicketType.Emergency) st.emergencies++;
                Progress("p1first", rush ? 1 : 0);
                Progress("clean", clean ? 1 : 0);
                Progress("pm", t.type == TicketType.Maintenance ? 1 : 0);
            }
            Progress("earn", pay);
            bool interrupted = false;
            if (emergencyAfter > 0 && completed >= emergencyAfter && !t.trainee) interrupted = TriggerEmergency(emergencyKind ?? "vesda") != null;
            else if (!t.trainee) interrupted = interruptions && career.day >= 2 && interrupts < MaxInterrupts && rng.NextDouble() < 0.3 && Interrupt() != null;
            string who = t.trainee ? $"Your trainee closed ticket #{t.id}" + (clean ? "" : " (but made a mess)") : $"Ticket #{t.id} closed";
            Message?.Invoke($"{who}: +${pay}" + (clean && !t.followUp && !t.trainee ? " (clean work bonus!)" : "") + (rush ? " (P1 rush bonus!)" : "") +
                            (AllDone && !interrupted ? ". That's the whole work order: clock out from your tablet." : ""), t.trainee && !clean ? 2 : 1);
            if (career.Level > level) Message?.Invoke($"Promoted to {career.Title}! Better pay and new gear in the tech shop." +
                (career.Level == Catalog.ExpansionLevel ? " The expansion cages open tomorrow." : career.Level == Catalog.TraineeLevel ? " You've been given a trainee: hand them simple tickets from your tablet." : ""), 1);
            CheckAchievements();
            TicketDone?.Invoke(t);
        }

        /// <summary>Something lands mid-shift: an urgent incident or a delivery nobody announced.</summary>
        Ticket Interrupt()
        {
            interrupts++;
            Ticket t;
            if (rng.NextDouble() < 0.6) t = GenFix(true);
            else
            {
                t = GenInstall(TicketType.RackCustomer, true);
                if (t != null) { t.priority = 2; t.title = "Surprise delivery: " + t.title; }
            }
            if (t == null) return null;
            AddEvidence(t);
            tickets.Add(t);
            TicketArrived?.Invoke(t);
            return t;
        }

        /// <summary>
        /// The technician broke something: open a P1 follow-up on that device. It pays nothing,
        /// and leaving it for the night shift costs reputation like any other ticket.
        /// </summary>
        public Ticket FollowUp(string rackLabel, Device dev, TaskKind kind, Fault fault, string title, string body, int slot = -1)
        {
            var rack = Rack(rackLabel);
            if (rack == null || dev == null) return null;
            if (OpenTickets.Any(k => k.followUp && k.tasks.Any(x => x.deviceId == dev.id && x.kind == kind && !x.done))) return null;
            if (fault != Fault.None && dev.fault == Fault.None) dev.fault = fault;
            var t = NewTicket(TicketType.FixIssue, rack, dev.owner, 0, 1);
            t.followUp = true;
            t.title = title;
            t.body = body + "\nThis one's on you: it pays nothing, but leaving it open costs reputation.";
            t.tasks.Add(new WorkTask { kind = kind, rack = rackLabel, deviceId = dev.id, u = dev.u, slot = slot });
            tickets.Add(t);
            BreakGoal("nofollow");
            TicketArrived?.Invoke(t);
            return t;
        }

        /// <summary>Penalty for fumbling a job: it costs reputation, the customer's goodwill and the ticket's clean-work bonus.</summary>
        public void Mistake(string what, float rep)
        {
            mistakes++;
            if (working != null)
            {
                working.mistakes++;
                career.ChangeSat(working.customer, Catalog.TraitOf(working.customer) == Trait.Strict ? -4f : -2f);
            }
            ChangeRep(-rep);
            Message?.Invoke(what + (rep > 0 ? $" (-{rep:0.#} rep)" : ""), 3);
        }

        public void ChangeRep(float d) => career.rep = Mathf.Clamp(career.rep + d, 0f, 100f);

        public bool Fired => career.rep <= 0f;

        // ================================================================ weekly review and achievements
        void NewWeek()
        {
            career.weekStart = career.day;
            career.goals.Clear();
            var ids = Catalog.GoalIds.Where(g => (g != "pm" || career.day >= 5) && (g != "p1first" || career.day >= 2)).OrderBy(_ => rng.Next()).Take(3);
            foreach (var id in ids) career.goals.Add(new Goal { id = id, target = Catalog.GoalTarget(id, career.Level) });
        }

        void Progress(string id, int n)
        {
            if (n == 0) return;
            foreach (var g in career.goals) if (g.id == id) g.progress += n;
        }

        void BreakGoal(string id)
        {
            foreach (var g in career.goals) if (g.id == id) g.broken = true;
        }

        /// <summary>A week is seven shifts. At the end of the seventh, the site manager goes through the goals.</summary>
        void Review()
        {
            if (career.goals.Count == 0 || career.day < career.weekStart + 7) return;
            int met = career.goals.Count(g => g.Met);
            int cash = met * Catalog.GoalCash, xp = met * Catalog.GoalXp;
            bool perfect = met == career.goals.Count;
            if (perfect) { cash += Catalog.PerfectWeekCash; career.stats.perfectWeeks++; }
            career.money += cash;
            career.totalEarned += cash;
            career.xp += xp;
            var lines = career.goals.Select(g => $"{(g.Met ? "[x]" : "[ ]")} {Catalog.GoalText(g.id, g.target)}" + (Catalog.GoalIsStreak(g.id) ? "" : $" ({Mathf.Min(g.progress, g.target)}/{g.target})"));
            endNotes.Add($"<b>Weekly review</b>: {met} of {career.goals.Count} goals met: +{Ui.Money(cash)}, +{xp} XP" + (perfect ? " (perfect week bonus!)" : "") + "\n" + string.Join("\n", lines));
            career.goals.Clear();
        }

        void CheckAchievements()
        {
            foreach (var a in Catalog.Achievements)
            {
                if (career.achievements.Contains(a.id) || !a.met(career)) continue;
                career.achievements.Add(a.id);
                unlocked.Add(a.id);
                Message?.Invoke($"Achievement unlocked: {a.name}. {a.desc}", 1);
            }
        }

        /// <summary>
        /// Clock out: unfinished tickets go to the night shift, who finish the job in the world but it
        /// costs reputation and the customer's goodwill.
        /// </summary>
        public void End()
        {
            foreach (var t in OpenTickets.ToList())
            {
                t.status = TicketStatus.HandedOver;
                handedOver++;
                career.handedOver++;
                ChangeRep(-4f);
                float sat = t.type == TicketType.SwitchOutage || t.type == TicketType.Emergency ? -10f : -6f;
                if (Catalog.TraitOf(t.customer) == Trait.NightAverse) sat *= 2f;
                career.ChangeSat(t.customer, sat);
                career.stats.streak = 0;
                BreakGoal("nohandover");
                foreach (var task in t.tasks.Where(x => !x.done).ToList()) Apply(task, t, true);
            }
            ups = 0;
            offers.Clear();
            foreach (var r in racks) r.reserved.Clear();
            // Unused spares go back on the shelf; failed parts nobody returned get binned.
            foreach (var p in inv.pouch) career.AddStock(p, 1);
            inv.pouch.Clear();
            if (inv.rma.Count > 0)
            {
                int lost = inv.rma.Sum(p => Mathf.RoundToInt(Catalog.PartPrice(p) * Catalog.RmaCredit));
                endNotes.Add($"You left {inv.rma.Count} failed part{(inv.rma.Count > 1 ? "s" : "")} in your pouch. The night shift binned them: {Ui.Money(lost)} of RMA credit lost.");
                inv.rma.Clear();
            }
            career.bestDay = Mathf.Max(career.bestDay, earned);
            career.day++;
            Review();
            CheckAchievements();
        }
    }
}
