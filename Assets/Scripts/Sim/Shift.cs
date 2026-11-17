using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// One working day in the data hall: the racks, the ticket queue, the clock and the scoring.
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
        public readonly List<Rack> racks = new List<Rack>();
        public readonly List<Ticket> tickets = new List<Ticket>();
        /// <summary>Customer shipments waiting to be placed on the loading dock by the world.</summary>
        public readonly List<Item> deliveries = new List<Item>();
        public readonly System.Random rng;
        int nextId = 1, nextTicketId = 101;

        public int earned, completed, mistakes, handedOver;
        /// <summary>The ticket being worked on, so fumbles count against its clean-work bonus.</summary>
        public Ticket working;
        public float repStart;

        public event Action<Ticket> TicketArrived;
        public event Action<Ticket> TicketDone;
        /// <summary>Message to show the player: text and severity (0 info, 1 good, 2 warn, 3 bad).</summary>
        public event Action<string, int> Message;

        public IEnumerable<Ticket> OpenTickets => tickets.Where(t => t.Open);
        public bool AllDone => tickets.Count > 0 && !OpenTickets.Any();
        public int DoneCount => tickets.Count(t => t.status == TicketStatus.Done);

        public Shift(Career career, int seed)
        {
            this.career = career;
            rng = new System.Random(seed);
            repStart = career.rep;
            inv.handCap = career.Has("cart") ? 2 : 1;
            inv.pouchCap = career.Has("belt") ? 6 : 4;
            BuildRacks();
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
            AddDevice(rack, "sw48", 42);
            float fill = 0.35f + (float)rng.NextDouble() * 0.4f;
            int u = 1 + rng.Next(3);
            string[] pool = { "srv1u", "srv1u", "srv1u", "srv2u", "srv2u", "ds4u", "cust1u", "cust2u" };
            while (u < 40 && (RH.Rack.Units - rack.FreeUnits) < fill * RH.Rack.Units)
            {
                var def = Catalog.Dev(pool[rng.Next(pool.Length)]);
                if (u + def.heightU - 1 >= 42) break;
                AddDevice(rack, def.id, u);
                u += def.heightU + (rng.NextDouble() < 0.3 ? 1 + rng.Next(3) : 0);
            }
        }

        Device AddDevice(Rack rack, string def, int u, string owner = null)
        {
            var d = new Device { id = nextId++, def = def, u = u, owner = owner ?? rack.owner };
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

        public IEnumerable<(Ticket ticket, WorkTask task)> TasksAt(string rack) =>
            OpenTickets.SelectMany(k => k.tasks.Where(t => t.rack == rack).Select(t => (k, t)));

        // ================================================================ the day's work order
        /// <summary>Tickets in today's work order: 4 on day one, one more each day, up to 12.</summary>
        public int QueueSize => QueueSizeFor(career.day);
        public static int QueueSizeFor(int day) => Mathf.Min(12, 3 + day);

        /// <summary>Hand out the whole day's work at clock-in. There is no clock: the shift ends when the player clocks out.</summary>
        public void GenerateQueue()
        {
            var types = Available();
            // Make sure the newest ticket type shows up on the day it unlocks.
            if (career.day <= 3) SpawnTicket(types[types.Count - 1]);
            for (int guard = 0; tickets.Count < QueueSize && guard < QueueSize * 4; guard++) SpawnTicket();
        }

        public List<TicketType> Available()
        {
            var l = new List<TicketType> { TicketType.Firmware, TicketType.ReplaceHardware, TicketType.Expand };
            if (career.day >= 2) { l.Add(TicketType.FixIssue); l.Add(TicketType.RackCustomer); }
            if (career.day >= 3) l.Add(TicketType.NewContract);
            return l;
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

        Ticket NewTicket(TicketType type, Rack rack, string customer, int pay) =>
            new Ticket { id = nextTicketId++, type = type, rack = rack.label, customer = customer, pay = pay };

        T Pick<T>(IList<T> l) => l[rng.Next(l.Count)];

        List<Rack> Owned => racks.Where(r => r.owner != null).ToList();

        Ticket Generate(TicketType type)
        {
            switch (type)
            {
                case TicketType.Expand: return GenInstall(type, false);
                case TicketType.RackCustomer: return GenInstall(type, true);
                case TicketType.FixIssue: return GenFix();
                case TicketType.ReplaceHardware: return GenReplace();
                case TicketType.Firmware: return GenFirmware();
                default: return GenNewContract();
            }
        }

        Ticket GenInstall(TicketType type, bool customerGear)
        {
            var candidates = Owned.Where(r => r.FreeUnits >= 6).ToList();
            if (candidates.Count == 0) return null;
            var rack = Pick(candidates);
            int n = 1 + rng.Next(customerGear ? 3 : 3);
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

        List<Device> IdleDevices(Func<Device, bool> filter) =>
            Owned.SelectMany(r => r.devices).Where(d => d.ticket == 0 && d.Healthy && filter(d)).ToList();

        Rack RackOf(Device d) => racks.Find(r => r.devices.Contains(d));

        Ticket GenFix()
        {
            var devs = IdleDevices(d => d.Def.kind != DevKind.Storage);
            if (devs.Count == 0) return null;
            var dev = Pick(devs);
            var rack = RackOf(dev);
            var faults = new List<Fault> { Fault.LinkDown, Fault.Hung };
            if (dev.Def.sfps > 0) faults.Add(Fault.BadSfp);
            dev.fault = Pick(faults);
            if (dev.fault == Fault.BadSfp) dev.badSfp = rng.Next(dev.Def.sfps);
            var t = NewTicket(TicketType.FixIssue, rack, dev.owner, 110);
            dev.ticket = t.id;
            string symptom = dev.fault == Fault.Hung ? "stopped responding to pings and SSH" : "is unreachable on the network";
            t.title = $"{dev.owner}: {dev.Def.name} down in {rack.label} U{dev.u}";
            t.body = $"Monitoring reports that {dev.Name} at {rack.label} U{dev.u} {symptom}. The customer is losing traffic. Connect to it, find the cause and fix it.";
            t.tasks.Add(new WorkTask { kind = TaskKind.Diagnose, rack = rack.label, deviceId = dev.id, u = dev.u });
            return t;
        }

        Ticket GenReplace()
        {
            bool drive = rng.NextDouble() < 0.6;
            var devs = IdleDevices(d => drive ? d.Def.bays > 0 : d.Def.psus > 0);
            if (devs.Count == 0) return null;
            var dev = Pick(devs);
            var rack = RackOf(dev);
            var t = NewTicket(TicketType.ReplaceHardware, rack, dev.owner, 85);
            dev.ticket = t.id;
            if (drive)
            {
                dev.failedBay = rng.Next(dev.Def.bays);
                t.title = $"Replace failed drive in {rack.label} U{dev.u}";
                t.body = $"The RAID controller in {dev.Name} ({rack.label} U{dev.u}) flagged a failed drive. The array is degraded. Grab a spare drive from the storage room and hot-swap it. Look for the amber LED, and don't pull a healthy drive.";
                t.tasks.Add(new WorkTask { kind = TaskKind.SwapDrive, rack = rack.label, deviceId = dev.id, u = dev.u });
            }
            else
            {
                dev.failedPsu = rng.Next(dev.Def.psus);
                t.title = $"Replace failed PSU in {rack.label} U{dev.u}";
                t.body = $"{dev.Name} ({rack.label} U{dev.u}) lost power supply {(dev.failedPsu == 0 ? "A" : "B")} and is running on one feed. Swap in a spare PSU from the storage room.";
                t.tasks.Add(new WorkTask { kind = TaskKind.SwapPsu, rack = rack.label, deviceId = dev.id, u = dev.u });
            }
            return t;
        }

        Ticket GenFirmware()
        {
            string comp = Pick(Catalog.Components);
            string target = Latest(comp);
            var groups = Owned.Select(r => (r, devs: r.devices.Where(d => d.ticket == 0 && d.Healthy && d.Def.kind == DevKind.Server && d.Firmware(comp) != target).ToList()))
                              .Where(g => g.devs.Count >= 2).ToList();
            if (groups.Count == 0) return null;
            var (rack, list) = Pick(groups);
            var model = Pick(list.Select(d => d.def).Distinct().ToList());
            var devs = list.Where(d => d.def == model).Take(2 + rng.Next(3)).ToList();
            if (devs.Count == 0) return null;
            var t = NewTicket(TicketType.Firmware, rack, rack.owner, 25 + 30 * devs.Count);
            foreach (var d in devs)
            {
                d.ticket = t.id;
                t.tasks.Add(new WorkTask { kind = TaskKind.Firmware, rack = rack.label, deviceId = d.id, u = d.u, component = comp, version = target });
            }
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

        // ================================================================ doing the work
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
                case TaskKind.SwapDrive: return inv.HasPart(PartKind.Drive) ? null : "Need a spare drive from the storage room";
                case TaskKind.SwapPsu: return inv.HasPart(PartKind.Psu) ? null : "Need a spare PSU from the storage room";
                case TaskKind.SwapSfp: return inv.HasPart(PartKind.Sfp) ? null : "Need an SFP+ optic from the storage room";
                default: return null;
            }
        }

        public static string Describe(WorkTask t)
        {
            switch (t.kind)
            {
                case TaskKind.Mount: return $"Rack {Catalog.Dev(t.def).name} at U{t.u}" + (t.tag != null ? $" ({t.tag})" : "");
                case TaskKind.Cable: return $"Cable {Catalog.Dev(t.def).name} at U{t.u}";
                case TaskKind.SwapDrive: return $"Hot-swap the failed drive (U{t.u})";
                case TaskKind.SwapPsu: return $"Replace the failed PSU (U{t.u})";
                case TaskKind.SwapSfp: return $"Replace the faulty SFP+ optic (U{t.u})";
                case TaskKind.Firmware: return $"Flash {t.component} {t.version} (U{t.u})";
                case TaskKind.Diagnose: return $"Diagnose the outage (U{t.u})";
                case TaskKind.PowerCycle: return $"Hard power-cycle the hung server (U{t.u})";
                default: return $"Reseat the loose network cable (U{t.u})";
            }
        }

        /// <summary>Apply a successfully finished minigame to the world.</summary>
        public void Complete(WorkTask t)
        {
            if (t.done) return;
            var rack = Rack(t.rack);
            var ticket = TicketOf(t);
            var dev = t.deviceId > 0 ? rack.devices.Find(d => d.id == t.deviceId) : null;
            switch (t.kind)
            {
                case TaskKind.Mount:
                    inv.Take(t.def, t.tag);
                    rack.reserved.RemoveAll(r => r.x == t.u);
                    var nd = AddDevice(rack, t.def, t.u, ticket?.customer);
                    foreach (var comp in Catalog.Components) nd.SetFirmware(comp, Latest(comp));
                    break;
                case TaskKind.SwapDrive: inv.TakePart(PartKind.Drive); if (dev != null) dev.failedBay = -1; break;
                case TaskKind.SwapPsu: inv.TakePart(PartKind.Psu); if (dev != null) dev.failedPsu = -1; break;
                case TaskKind.SwapSfp: inv.TakePart(PartKind.Sfp); if (dev != null) { dev.badSfp = -1; dev.fault = Fault.None; } break;
                case TaskKind.Firmware: dev?.SetFirmware(t.component, t.version); break;
                case TaskKind.PowerCycle:
                case TaskKind.Reseat: if (dev != null) dev.fault = Fault.None; break;
                case TaskKind.Diagnose:
                    if (dev != null && ticket != null)
                    {
                        var kind = dev.fault == Fault.BadSfp ? TaskKind.SwapSfp : dev.fault == Fault.Hung ? TaskKind.PowerCycle : TaskKind.Reseat;
                        ticket.tasks.Add(new WorkTask { kind = kind, rack = t.rack, deviceId = dev.id, u = dev.u });
                        Message?.Invoke($"Diagnosis: {FaultName(dev.fault)}. Next: {Describe(ticket.tasks[ticket.tasks.Count - 1])}", 0);
                    }
                    break;
            }
            t.done = true;
            if (ticket != null && ticket.tasks.All(x => x.done)) FinishTicket(ticket);
        }

        public static string FaultName(Fault f) => f switch
        {
            Fault.LinkDown => "loose network cable",
            Fault.BadSfp => "failed SFP+ optic",
            Fault.Hung => "hung operating system",
            _ => "none",
        };

        /// <summary>Pay out a finished ticket. Work done without a single fumble earns a 20% clean-work bonus.</summary>
        void FinishTicket(Ticket t)
        {
            bool clean = t.mistakes == 0;
            int pay = Mathf.RoundToInt(t.pay * career.PayMul * (clean ? 1.2f : 1f));
            t.status = TicketStatus.Done;
            foreach (var r in racks) foreach (var d in r.devices) if (d.ticket == t.id) d.ticket = 0;
            earned += pay;
            career.money += pay;
            career.totalEarned += pay;
            completed++;
            career.ticketsDone++;
            ChangeRep(clean ? 3f : 2f);
            Message?.Invoke($"Ticket #{t.id} closed: +${pay}" + (clean ? " (clean work bonus!)" : "") + (AllDone ? ". That's the whole work order: clock out from your tablet." : ""), 1);
            TicketDone?.Invoke(t);
        }

        /// <summary>Penalty for fumbling a job: it costs reputation and the ticket's clean-work bonus.</summary>
        public void Mistake(string what, float rep)
        {
            mistakes++;
            if (working != null) working.mistakes++;
            ChangeRep(-rep);
            Message?.Invoke(what + (rep > 0 ? $" (-{rep:0.#} rep)" : ""), 3);
        }

        public void ChangeRep(float d) => career.rep = Mathf.Clamp(career.rep + d, 0f, 100f);

        public bool Fired => career.rep <= 0f;

        /// <summary>Clock out: unfinished tickets go to the night shift and cost reputation.</summary>
        public void End()
        {
            foreach (var t in OpenTickets.ToList())
            {
                t.status = TicketStatus.HandedOver;
                handedOver++;
                career.handedOver++;
                ChangeRep(-4f);
            }
            career.bestDay = Mathf.Max(career.bestDay, earned);
            career.day++;
        }
    }
}
