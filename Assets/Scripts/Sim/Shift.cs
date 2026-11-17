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

        public static readonly Dictionary<string, string[]> FirmwareVersions = new Dictionary<string, string[]>
        {
            { "BIOS", new[] { "2.1.0", "2.2.3", "2.3.0", "2.4.1" } },
            { "BMC", new[] { "4.10.2", "4.11.0", "4.12.3" } },
            { "NIC", new[] { "22.4.1", "22.5.0", "23.1.2" } },
        };
        public static string Latest(string comp) => FirmwareVersions[comp][FirmwareVersions[comp].Length - 1];

        public readonly Career career;
        public readonly Inventory inv = new Inventory();
        public List<Rack> racks => career.hall;
        public readonly List<Ticket> tickets = new List<Ticket>();
        /// <summary>Customer shipments waiting to be placed on the loading dock by the world.</summary>
        public readonly List<Item> deliveries = new List<Item>();
        public readonly System.Random rng;

        public int earned, completed, mistakes, handedOver, interrupts;
        /// <summary>The ticket being worked on, so fumbles count against its clean-work bonus.</summary>
        public Ticket working;
        public float repStart;
        public int xpStart;
        /// <summary>What happened overnight, for the briefing (null when nothing did).</summary>
        public string news;
        /// <summary>Urgent tickets and surprise deliveries can land mid-shift. Tests switch this off.</summary>
        public bool interruptions = true;
        public const int MaxInterrupts = 2;

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
            if (career.hall.Count == 0) BuildRacks();
            if (resume != null) Restore(resume);
        }

        // ================================================================ saving mid-shift
        void Restore(ShiftSave s)
        {
            tickets.AddRange(s.tickets);
            inv.hands.AddRange(s.hands);
            inv.pouch.AddRange(s.pouch);
            earned = s.earned; completed = s.completed; mistakes = s.mistakes; handedOver = s.handedOver; interrupts = s.interrupts;
            repStart = s.repStart;
            xpStart = s.xpStart;
        }

        /// <summary>Everything about the shift except the world positions, which the game fills in.</summary>
        public ShiftSave Snapshot()
        {
            var s = new ShiftSave { earned = earned, completed = completed, mistakes = mistakes, handedOver = handedOver, interrupts = interrupts, repStart = repStart, xpStart = xpStart };
            s.tickets.AddRange(tickets);
            s.hands.AddRange(inv.hands);
            s.pouch.AddRange(inv.pouch);
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

        void Populate(Rack rack)
        {
            AddDevice(rack, "sw48", 42).installDay = -rng.Next(60, 1800);
            float fill = 0.35f + (float)rng.NextDouble() * 0.4f;
            int u = 1 + rng.Next(3);
            string[] pool = { "srv1u", "srv1u", "srv1u", "srv2u", "srv2u", "ds4u", "cust1u", "cust2u" };
            while (u < 40 && (RH.Rack.Units - rack.FreeUnits) < fill * RH.Rack.Units)
            {
                var def = Catalog.Dev(pool[rng.Next(pool.Length)]);
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

        public Rack Rack(string label) => racks.Find(r => r.label == label);

        public Device Dev(string rack, int id) => Rack(rack)?.devices.Find(d => d.id == id);

        public Ticket TicketOf(WorkTask t) => tickets.Find(k => k.tasks.Contains(t));

        /// <summary>Open tasks done at the rack itself (not at a station elsewhere in the building).</summary>
        public IEnumerable<(Ticket ticket, WorkTask task)> TasksAt(string rack) =>
            OpenTickets.SelectMany(k => k.tasks.Where(t => t.rack == rack && t.station == null).Select(t => (k, t)));

        public IEnumerable<(Ticket ticket, WorkTask task)> TasksAtStation(string station) =>
            OpenTickets.SelectMany(k => k.tasks.Where(t => t.station == station).Select(t => (k, t)));

        /// <summary>Jobs done from the hot aisle behind the rack: power, cabling and the NIC optics.</summary>
        public static bool RearJob(TaskKind k) =>
            k == TaskKind.Cable || k == TaskKind.Uncable || k == TaskKind.Reseat || k == TaskKind.SwapPsu ||
            k == TaskKind.SwapSfp || k == TaskKind.Power || k == TaskKind.Trace;

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
            foreach (var t in NewTypes(career.day)) SpawnTicket(t);
            // A freshly bought certification shows up quickly.
            if (career.Has("cert_power") && rng.NextDouble() < 0.6) SpawnTicket(TicketType.PowerAudit);
            if (career.Has("cert_fiber") && rng.NextDouble() < 0.6) SpawnTicket(TicketType.CrossConnect);
            for (int guard = 0; tickets.Count < QueueSize && guard < QueueSize * 4; guard++) SpawnTicket();
        }

        /// <summary>Ticket types that unlock on a given day, so each one shows up on its first day.</summary>
        public static TicketType[] NewTypes(int day) => day switch
        {
            1 => new[] { TicketType.Expand },
            2 => new[] { TicketType.FixIssue, TicketType.RackCustomer, TicketType.SmartHands },
            3 => new[] { TicketType.NewContract },
            4 => new[] { TicketType.Decommission },
            _ => new TicketType[0],
        };

        public List<TicketType> Available()
        {
            var l = new List<TicketType> { TicketType.Firmware, TicketType.ReplaceHardware, TicketType.Expand };
            if (career.day >= 2) { l.Add(TicketType.FixIssue); l.Add(TicketType.RackCustomer); l.Add(TicketType.SmartHands); }
            if (career.day >= 3) l.Add(TicketType.NewContract);
            if (career.day >= 4)
            {
                l.Add(TicketType.Decommission);
                // A crowded hall needs more room freed up.
                if (Owned.Count(r => r.FreeUnits < 8) > Owned.Count / 2) l.Add(TicketType.Decommission);
            }
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
                    news = $"Overnight, {r.owner}'s contract for rack {r.label} ended and the night shift emptied it. It's free for a new customer.";
                    r.devices.Clear();
                    r.owner = null;
                }
            }
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
                    tickets.Add(t);
                    TicketArrived?.Invoke(t);
                    return t;
                }
                if (force != null) return null;
            }
            return null;
        }

        Ticket NewTicket(TicketType type, Rack rack, string customer, int pay, int priority = 3) =>
            new Ticket { id = career.nextTicketId++, type = type, rack = rack.label, customer = customer, pay = pay, priority = priority };

        T Pick<T>(IList<T> l) => l[rng.Next(l.Count)];

        List<Rack> Owned => racks.Where(r => r.owner != null).ToList();

        /// <summary>Devices some open ticket is already working on.</summary>
        HashSet<int> Busy() => new HashSet<int>(OpenTickets.SelectMany(t => t.tasks).Where(x => x.deviceId > 0).Select(x => x.deviceId));

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
            for (int i = 0; i < n; i++)
            {
                string def = customerGear ? Pick(new[] { "cust1u", "cust1u", "cust2u", "cust2u", "cust4u" }) : Pick(new[] { "srv1u", "srv1u", "srv2u" });
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
                t.pay = 55 + 45 * items;
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

        /// <summary>Old hardware fails more: of a few random candidates, take the one in service longest.</summary>
        Device Oldest(List<Device> devs)
        {
            Device best = null;
            for (int i = 0; i < 3; i++)
            {
                var d = Pick(devs);
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
            var devs = IdleDevices(d => d.Def.kind != DevKind.Storage);
            if (devs.Count == 0) return null;
            var dev = Oldest(devs);
            var rack = RackOf(dev);
            var faults = new List<Fault> { Fault.LinkDown, Fault.Hung };
            if (dev.Def.sfps > 0) faults.Add(Fault.BadSfp);
            if (dev.Def.kind == DevKind.Server) { faults.Add(Fault.BadDimm); faults.Add(Fault.FanFail); }
            dev.fault = Pick(faults);
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
            t.title = $"Decommission {devs.Count} end-of-life unit{(devs.Count > 1 ? "s" : "")} in {rack.label}";
            t.body = $"{rack.owner} is retiring old hardware in {rack.label}:\n- " +
                     string.Join("\n- ", devs.Select(d => $"{d.Def.name} at U{d.u} ({Age(career, d)})")) +
                     "\nUnplug ONLY its cables (rear), de-rack it (front), wipe and shred its drives at the bench in the storage room, and leave it on the OUTBOUND pallet at the loading dock.";
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
            var rack = Pick(cands);
            var dev = Pick(rack.devices.Where(d => d.Def.ports.Length > 1 && d.Def.kind != DevKind.Storage && !busy.Contains(d.id)).ToList());
            string port = dev.Def.ports[1];
            string carrier = Pick(Catalog.Carriers);
            int pp = 1 + rng.Next(24), cport = 1 + rng.Next(12);
            var t = NewTicket(TicketType.CrossConnect, rack, rack.owner, 165);
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
            var rack = Pick(cands);
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
                    return TicketOf(t)?.tasks.Find(x => x.kind == TaskKind.Trace) is WorkTask tr && !tr.done ? $"Trace the customer's cable at {t.rack} first" : null;
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
                case TaskKind.SwapDrive: return $"Hot-swap the failed drive (U{t.u})";
                case TaskKind.SwapPsu: return $"Replace the failed PSU (U{t.u})";
                case TaskKind.SwapSfp: return $"Replace the faulty SFP+ optic (U{t.u})";
                case TaskKind.SwapDimm: return $"Replace the failed memory module (U{t.u})";
                case TaskKind.SwapFan: return $"Replace the failed fan (U{t.u})";
                case TaskKind.Firmware: return $"Flash {t.component} {t.version} (U{t.u})";
                case TaskKind.Diagnose: return $"Diagnose the outage (U{t.u})" + (t.cart ? " with the crash cart" : "");
                case TaskKind.PowerCycle: return $"Hard power-cycle the hung server (U{t.u})";
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
                    if (rack.Free(t.u, Catalog.Dev(t.def).heightU))
                    {
                        var nd = AddDevice(rack, t.def, t.u, ticket?.customer);
                        foreach (var comp in Catalog.Components) nd.SetFirmware(comp, Latest(comp));
                    }
                    break;
                case TaskKind.Unmount:
                    if (dev != null) rack.devices.Remove(dev);
                    if (!night) inv.hands.Add(new Item { def = t.def, tag = t.tag, label = $"Decom {Catalog.Dev(t.def).name} ({t.tag})" });
                    break;
                case TaskKind.Ship: if (!night) inv.Take(t.def, t.tag); break;
                case TaskKind.SwapDrive: if (!night) inv.TakePart(PartKind.Drive); if (dev != null) dev.failedBay = -1; break;
                case TaskKind.SwapPsu: if (!night) inv.TakePart(PartKind.Psu); if (dev != null) dev.failedPsu = -1; break;
                case TaskKind.SwapSfp:
                    if (!night) inv.TakePart(PartKind.Sfp);
                    if (dev != null) { dev.badSfp = -1; if (dev.fault == Fault.BadSfp) dev.fault = Fault.None; }
                    break;
                case TaskKind.SwapDimm:
                    if (!night) inv.TakePart(PartKind.Dimm);
                    if (dev != null) { dev.badDimm = -1; if (dev.fault == Fault.BadDimm) dev.fault = Fault.None; }
                    break;
                case TaskKind.SwapFan:
                    if (!night) inv.TakePart(PartKind.Fan);
                    if (dev != null) { dev.badFan = -1; if (dev.fault == Fault.FanFail) dev.fault = Fault.None; }
                    break;
                case TaskKind.Firmware: dev?.SetFirmware(t.component, t.version); break;
                case TaskKind.PowerCycle: if (dev != null && dev.fault == Fault.Hung) dev.fault = Fault.None; break;
                case TaskKind.PowerOn: if (dev != null && dev.fault == Fault.NoPower) dev.fault = Fault.None; break;
                case TaskKind.Reseat: if (dev != null && dev.fault == Fault.LinkDown) dev.fault = Fault.None; break;
                case TaskKind.RaidRebuild: if (dev != null && dev.fault == Fault.ArrayCritical) dev.fault = Fault.None; break;
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
                        _ => TaskKind.Reseat,
                    };
                    ticket.tasks.Add(new WorkTask { kind = kind, rack = t.rack, deviceId = dev.id, u = dev.u });
                    Message?.Invoke($"Diagnosis: {FaultName(dev.fault)}. Next: {Describe(ticket.tasks[ticket.tasks.Count - 1])}", 0);
                    break;
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
            _ => "none",
        };

        /// <summary>
        /// Pay out a finished ticket. Work done without a single fumble earns a 20% clean-work bonus,
        /// a P1 closed before any lower-priority work earns 25% more, and the customer's mood tips it.
        /// </summary>
        void FinishTicket(Ticket t)
        {
            bool clean = t.mistakes == 0;
            bool rush = t.RushBonus;
            float mul = career.PayMul * career.CustMul(t.customer) * (clean ? 1.2f : 1f) * (rush ? 1.25f : 1f);
            int pay = Mathf.RoundToInt(t.pay * mul);
            t.status = TicketStatus.Done;
            // Closing routine work while a P1 waits costs that P1 its rush bonus.
            if (t.priority > 1) foreach (var p in OpenTickets) if (p.priority == 1) p.neglected = true;
            earned += pay;
            career.money += pay;
            career.totalEarned += pay;
            completed++;
            career.ticketsDone++;
            int level = career.Level;
            if (t.followUp)
            {
                ChangeRep(1f);
            }
            else
            {
                ChangeRep(clean ? 3f : 2f);
                career.xp += 10 + (clean ? 5 : 0) + (rush ? 5 : 0) + (t.type == TicketType.NewContract || t.type == TicketType.Decommission ? 5 : 0);
                career.ChangeSat(t.customer, clean ? 4f : 1f);
            }
            bool interrupted = interruptions && career.day >= 2 && interrupts < MaxInterrupts && rng.NextDouble() < 0.3 && Interrupt() != null;
            Message?.Invoke($"Ticket #{t.id} closed: +${pay}" + (clean && !t.followUp ? " (clean work bonus!)" : "") + (rush ? " (P1 rush bonus!)" : "") +
                            (AllDone && !interrupted ? ". That's the whole work order: clock out from your tablet." : ""), 1);
            if (career.Level > level) Message?.Invoke($"Promoted to {career.Title}! Better pay and new gear in the tech shop.", 1);
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
                career.ChangeSat(working.customer, -2f);
            }
            ChangeRep(-rep);
            Message?.Invoke(what + (rep > 0 ? $" (-{rep:0.#} rep)" : ""), 3);
        }

        public void ChangeRep(float d) => career.rep = Mathf.Clamp(career.rep + d, 0f, 100f);

        public bool Fired => career.rep <= 0f;

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
                career.ChangeSat(t.customer, -6f);
                foreach (var task in t.tasks.Where(x => !x.done).ToList()) Apply(task, t, true);
            }
            foreach (var r in racks) r.reserved.Clear();
            career.bestDay = Mathf.Max(career.bestDay, earned);
            career.day++;
        }
    }
}
