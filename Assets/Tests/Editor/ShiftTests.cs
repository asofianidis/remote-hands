using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace RH.Tests
{
    public class ShiftTests
    {
        /// <summary>A shift with mid-shift interruptions off, so ticket counts are predictable.</summary>
        static Shift NewShift(int day = 5, int seed = 1, Career career = null)
        {
            career ??= new Career { day = day };
            return new Shift(career, seed) { interruptions = false };
        }

        [Test]
        public void HallHasFortyRacksWithoutOverlaps()
        {
            var s = NewShift();
            Assert.AreEqual(Shift.Rows.Length * Shift.RacksPerRow, s.racks.Count);
            foreach (var r in s.racks)
                for (int u = 1; u <= Rack.Units; u++)
                    Assert.LessOrEqual(r.devices.Count(d => u >= d.u && u <= d.Top), 1, $"{r.label} U{u} overlaps");
            Assert.IsTrue(s.racks.Any(r => r.Empty), "some racks should be vacant for new contracts");
            var ids = s.racks.SelectMany(r => r.devices).Select(d => d.id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "device ids are unique");
            Assert.IsTrue(s.racks.SelectMany(r => r.devices).All(d => d.serial.Length == 8), "every device has a serial");
        }

        [Test]
        public void EveryTicketTypeCanBeGenerated()
        {
            foreach (TicketType t in System.Enum.GetValues(typeof(TicketType)))
            {
                var s = NewShift();
                var k = s.SpawnTicket(t);
                Assert.IsNotNull(k, t.ToString());
                Assert.IsNotEmpty(k.tasks, t.ToString());
                Assert.Greater(k.pay, 0);
                // Facility work (receiving, a power failure) isn't tied to a rack: it all happens at stations.
                if (k.rack != null) Assert.IsNotNull(s.Rack(k.rack), t.ToString());
                else Assert.IsTrue(k.tasks.All(x => x.station != null), t.ToString());
                foreach (var task in k.tasks) Assert.IsNotEmpty(Shift.Describe(task));
                foreach (var task in k.tasks.Where(x => !Shift.WorldJob(x.kind) && x.kind != TaskKind.Ship && x.kind != TaskKind.Mop && x.kind != TaskKind.ResetAlarm))
                    Assert.IsNotNull(Minigame.For(task), $"{t}: {task.kind} has a job to play");
            }
        }

        [Test]
        public void WorkOrderIsHandedOutUpFrontAndGrowsWithTheDays()
        {
            Assert.AreEqual(4, Shift.QueueSizeFor(1));
            Assert.AreEqual(8, Shift.QueueSizeFor(5));
            Assert.AreEqual(12, Shift.QueueSizeFor(30));
            for (int day = 1; day <= 6; day++)
            {
                var s = NewShift(day, day * 13);
                s.GenerateQueue();
                Assert.AreEqual(Shift.QueueSizeFor(day), s.tickets.Count, $"day {day}");
                Assert.IsTrue(s.tickets.All(t => s.Available().Contains(t.type)), $"day {day} only gets unlocked ticket types");
                Assert.IsFalse(s.AllDone);
            }
            var d3 = NewShift(3, 99);
            d3.GenerateQueue();
            Assert.IsTrue(d3.tickets.Any(t => t.type == TicketType.NewContract), "the newly unlocked type shows up on its first day");
            var d4 = NewShift(4, 7);
            d4.GenerateQueue();
            Assert.IsTrue(d4.tickets.Any(t => t.type == TicketType.Decommission), "decommissioning shows up on day 4");
        }

        [Test]
        public void ExpandTicketNeedsTheServerThenCompletes()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.Expand);
            var mount = t.tasks[0];
            var cable = t.tasks[1];
            Assert.IsNotNull(s.Blocker(mount), "can't mount without carrying the server");
            Assert.IsNotNull(s.Blocker(cable), "can't cable before mounting");
            int money = s.career.money;
            foreach (var task in t.tasks.ToList())
            {
                if (task.kind == TaskKind.Mount) s.inv.hands.Add(new Item { def = task.def });
                Assert.IsNull(s.Blocker(task), Shift.Describe(task));
                s.Complete(task);
            }
            Assert.AreEqual(TicketStatus.Done, t.status);
            Assert.Greater(s.career.money, money);
            Assert.AreEqual(0, s.inv.hands.Count, "installed servers leave the player's hands");
            var rack = s.Rack(t.rack);
            Assert.IsEmpty(rack.reserved);
            foreach (var m in t.tasks.Where(x => x.kind == TaskKind.Mount)) Assert.AreEqual(m.def, rack.At(m.u).def);
        }

        [Test]
        public void CustomerGearIsDeliveredAndTagged()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.RackCustomer);
            var mounts = t.tasks.Where(x => x.kind == TaskKind.Mount).ToList();
            Assert.AreEqual(mounts.Count, s.deliveries.Count);
            s.inv.hands.Add(new Item { def = mounts[0].def });
            Assert.IsNotNull(s.Blocker(mounts[0]), "stock equipment is not the customer's own unit");
            s.inv.hands.Clear();
            s.inv.hands.Add(s.deliveries[0]);
            Assert.IsNull(s.Blocker(mounts[0]));
            Assert.IsTrue(s.deliveries[0].CustomerGear);
        }

        [Test]
        public void DiagnosisAddsTheRightFix()
        {
            var seen = new System.Collections.Generic.HashSet<Fault>();
            for (int seed = 1; seed < 60; seed++)
            {
                var s = NewShift(5, seed);
                var t = s.SpawnTicket(TicketType.FixIssue);
                Assert.AreEqual(1, t.priority, "incidents are P1");
                var dev = s.Dev(t.rack, t.tasks[0].deviceId);
                var fault = dev.fault;
                seen.Add(fault);
                Assert.AreNotEqual(Fault.None, fault);
                if (t.tasks[0].cart)
                {
                    Assert.IsNotNull(s.Blocker(t.tasks[0]), "a dead BMC needs the crash cart");
                    s.inv.hands.Add(new Item { def = "kvm" });
                }
                s.Complete(t.tasks[0]);
                // (Customers who want evidence also get a photo task, kept last.)
                Assert.AreEqual(2, t.tasks.Count(x => x.kind != TaskKind.Photo));
                var fix = t.tasks[1];
                var expected = fault switch
                {
                    Fault.BadSfp => TaskKind.SwapSfp,
                    Fault.Hung => TaskKind.PowerCycle,
                    Fault.BadDimm => TaskKind.SwapDimm,
                    Fault.FanFail => TaskKind.SwapFan,
                    _ => TaskKind.Reseat,
                };
                Assert.AreEqual(expected, fix.kind);
                if (fix.kind == TaskKind.SwapSfp) s.inv.pouch.Add(PartKind.Sfp);
                if (fix.kind == TaskKind.SwapDimm) s.inv.pouch.Add(PartKind.Dimm);
                if (fix.kind == TaskKind.SwapFan) s.inv.pouch.Add(PartKind.Fan);
                Assert.IsNull(s.Blocker(fix));
                s.Complete(fix);
                Assert.AreEqual(Fault.None, dev.fault);
                foreach (var photo in t.tasks.Where(x => x.kind == TaskKind.Photo).ToList()) s.Complete(photo);
                Assert.AreEqual(TicketStatus.Done, t.status);
            }
            Assert.IsTrue(seen.Contains(Fault.BadDimm) && seen.Contains(Fault.FanFail), "memory and fan faults are real causes now");
        }

        [Test]
        public void SwapsConsumePartsAndClearFaults()
        {
            var s = NewShift();
            for (int i = 0; i < 10; i++)
            {
                var t = s.SpawnTicket(TicketType.ReplaceHardware);
                var task = t.tasks[0];
                var dev = s.Dev(t.rack, task.deviceId);
                var part = task.kind == TaskKind.SwapDrive ? PartKind.Drive : PartKind.Psu;
                Assert.IsNotNull(s.Blocker(task));
                s.inv.pouch.Add(part);
                s.Complete(task);
                Assert.IsTrue(dev.Healthy);
                Assert.IsFalse(s.inv.HasPart(part));
            }
        }

        [Test]
        public void FirmwareTicketTargetsOneModelAndUpdatesIt()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.Firmware);
            var devs = t.tasks.Select(x => s.Dev(t.rack, x.deviceId)).ToList();
            Assert.GreaterOrEqual(devs.Count, 1);
            Assert.AreEqual(1, devs.Select(d => d.def).Distinct().Count());
            foreach (var task in t.tasks) s.Complete(task);
            foreach (var d in devs) Assert.AreEqual(t.tasks[0].version, d.Firmware(t.tasks[0].component));
        }

        [Test]
        public void NewContractClaimsAnEmptyRack()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.NewContract);
            var rack = s.Rack(t.rack);
            Assert.AreEqual(t.customer, rack.owner);
            Assert.IsTrue(t.tasks.Any(x => x.kind == TaskKind.Mount && x.def == "sw48" && x.u == 42));
            Assert.IsFalse(rack.Empty);
        }

        [Test]
        public void CleanWorkPaysABonusAndMistakesCostIt()
        {
            var s = NewShift();
            var clean = s.SpawnTicket(TicketType.Firmware);
            var messy = s.SpawnTicket(TicketType.Firmware);
            int expectedBase = messy.pay;
            int money = s.career.money;
            // Captured before the ticket's own reputation, XP and customer gains.
            float cleanMul = s.career.PayMul * s.career.CustMul(clean.customer);
            foreach (var task in clean.tasks) s.Complete(task);
            int cleanPay = s.career.money - money;
            Assert.AreEqual(Mathf.RoundToInt(clean.pay * cleanMul * 1.2f), cleanPay, "fumble-free work pays +20%");

            float rep = s.career.rep;
            float sat = s.career.Sat(messy.customer);
            s.working = messy;
            s.Mistake("pulled the wrong drive", 5f);
            Assert.AreEqual(rep - 5f, s.career.rep, 0.001f);
            Assert.AreEqual(1, messy.mistakes);
            Assert.Less(s.career.Sat(messy.customer), sat, "the customer noticed");
            money = s.career.money;
            float mul = s.career.PayMul * s.career.CustMul(messy.customer);
            foreach (var task in messy.tasks) s.Complete(task);
            Assert.AreEqual(Mathf.RoundToInt(expectedBase * mul), s.career.money - money, "a fumbled ticket pays base rate");
            Assert.IsTrue(s.AllDone);
        }

        [Test]
        public void ClockingOutWithOpenWorkCostsReputation()
        {
            var s = NewShift();
            var done = s.SpawnTicket(TicketType.Firmware);
            foreach (var task in done.tasks) s.Complete(task);
            s.SpawnTicket(TicketType.Firmware);
            s.SpawnTicket(TicketType.Firmware);
            float before = s.career.rep;
            int day = s.career.day;
            s.End();
            Assert.AreEqual(before - 8f, s.career.rep, 0.001f);
            Assert.AreEqual(2, s.handedOver);
            Assert.AreEqual(day + 1, s.career.day);
            Assert.IsFalse(s.OpenTickets.Any());
        }

        [Test]
        public void EarlyDaysOnlyOfferBasicTickets()
        {
            var s = NewShift(1);
            var types = s.Available();
            CollectionAssert.DoesNotContain(types, TicketType.NewContract);
            CollectionAssert.DoesNotContain(types, TicketType.FixIssue);
            CollectionAssert.DoesNotContain(types, TicketType.Decommission);
            CollectionAssert.DoesNotContain(types, TicketType.PowerAudit);
        }

        // ================================================================ the persistent hall
        [Test]
        public void TheHallCarriesOverBetweenShifts()
        {
            var career = new Career { day = 5 };
            var s = NewShift(career: career);
            var t = s.SpawnTicket(TicketType.Expand);
            foreach (var task in t.tasks.ToList())
            {
                if (task.kind == TaskKind.Mount) s.inv.hands.Add(new Item { def = task.def });
                s.Complete(task);
            }
            var mount = t.tasks[0];
            s.End();
            var next = NewShift(career: career, seed: 2);
            Assert.AreEqual(mount.def, next.Rack(mount.rack).At(mount.u)?.def, "yesterday's install is still in the rack");
        }

        [Test]
        public void CareerSaveRoundTripsTheHall()
        {
            var career = new Career { day = 5 };
            var s = NewShift(career: career);
            s.SpawnTicket(TicketType.NewContract);
            var json = JsonUtility.ToJson(career);
            var back = JsonUtility.FromJson<Career>(json);
            Assert.AreEqual(career.hall.Count, back.hall.Count);
            for (int i = 0; i < career.hall.Count; i++)
            {
                var a = career.hall[i];
                var b = back.hall[i];
                Assert.AreEqual(a.label, b.label);
                Assert.AreEqual(a.owner, b.owner, "vacant racks stay vacant (null, not \"\")");
                Assert.AreEqual(a.devices.Count, b.devices.Count);
                Assert.AreEqual(a.reserved.Count, b.reserved.Count);
            }
            Assert.AreEqual(career.nextDeviceId, back.nextDeviceId);
        }

        [Test]
        public void ShiftSaveRoundTripsTickets()
        {
            var career = new Career { day = 5 };
            var s = NewShift(career: career);
            var t = s.SpawnTicket(TicketType.RackCustomer);
            s.inv.hands.Add(s.deliveries[0]);
            s.inv.pouch.Add(PartKind.Fan);
            var json = JsonUtility.ToJson(s.Snapshot());
            var save = JsonUtility.FromJson<ShiftSave>(json);
            var resumed = new Shift(career, 3, save) { interruptions = false };
            var t2 = resumed.tickets.Single();
            Assert.AreEqual(t.id, t2.id);
            Assert.AreEqual(t.tasks.Count, t2.tasks.Count);
            Assert.IsNull(t2.tasks[1].tag, "untagged tasks stay untagged after loading");
            Assert.IsNull(resumed.Blocker(t2.tasks[0]), "the carried customer unit still matches its task");
            Assert.IsTrue(resumed.inv.HasPart(PartKind.Fan));
        }

        [Test]
        public void NightShiftFinishesWhatWasHandedOver()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.ReplaceHardware);
            var dev = s.Dev(t.rack, t.tasks[0].deviceId);
            Assert.IsFalse(dev.Healthy);
            var install = s.SpawnTicket(TicketType.Expand);
            s.End();
            Assert.IsTrue(dev.Healthy, "the night shift swapped the part");
            var m = install.tasks[0];
            Assert.AreEqual(m.def, s.Rack(m.rack).At(m.u)?.def, "the night shift racked the server");
            Assert.IsTrue(s.racks.All(r => r.reserved.Count == 0));
        }

        // ================================================================ new ticket types
        [Test]
        public void DecommissionRemovesTheUnitAndShipsIt()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.Decommission);
            var rack = s.Rack(t.rack);
            var un = t.tasks.First(x => x.kind == TaskKind.Uncable);
            var unmount = t.tasks.First(x => x.kind == TaskKind.Unmount && x.deviceId == un.deviceId);
            var ship = t.tasks.First(x => x.kind == TaskKind.Ship && x.tag == unmount.tag);
            Assert.IsNotNull(s.Blocker(unmount), "unplug before de-racking");
            s.Complete(un);
            Assert.IsNull(s.Blocker(unmount));
            s.Complete(unmount);
            Assert.IsNull(rack.devices.Find(d => d.id == un.deviceId), "it's out of the rack");
            Assert.IsTrue(s.inv.hands.Any(h => h.Decom && h.tag == unmount.tag), "and in the technician's arms");
            var wipe = t.tasks.FirstOrDefault(x => x.kind == TaskKind.Wipe && x.tag == unmount.tag);
            if (wipe != null)
            {
                Assert.AreEqual("bench", wipe.station);
                Assert.IsNotNull(s.Blocker(ship), "wipe the drives before shipping");
                s.Complete(wipe);
            }
            Assert.IsNull(s.Blocker(ship));
            s.Complete(ship);
            Assert.IsFalse(s.inv.hands.Any(h => h.tag == unmount.tag));
            Assert.IsFalse(s.TasksAt(t.rack).Any(x => x.task.station != null), "station jobs aren't listed at the rack");
        }

        [Test]
        public void CrossConnectTraceFillsInThePatch()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.CrossConnect);
            var trace = t.tasks[0];
            var patch = t.tasks[1];
            Assert.AreEqual("mmr", patch.station);
            Assert.IsNotNull(s.Blocker(patch));
            s.Complete(trace);
            Assert.AreEqual(trace.slot, patch.u, "the traced patch panel port goes on the patch order");
            Assert.IsNull(s.Blocker(patch));
            Assert.IsTrue(Shift.RearJob(trace.kind));
        }

        [Test]
        public void PowerLayoutsStartOverloadedAndBalanceUnder80Percent()
        {
            for (int seed = 1; seed < 40; seed++)
            {
                var s = NewShift(5, seed);
                var t = s.SpawnTicket(TicketType.PowerAudit);
                var rows = Shift.PowerLayout(s.Rack(t.rack), t.tasks[0].slot, out float breaker);
                Assert.Greater(Shift.FeedLoad(rows, 1), breaker * 0.8f, "feed A starts over the limit");
                Assert.IsTrue(rows.Any(r => !r.Redundant));
                foreach (var r in rows) { r.c1 = 1; r.c2 = 2; }
                Assert.LessOrEqual(Shift.FeedLoad(rows, 1), breaker * 0.8f);
                Assert.LessOrEqual(Shift.FeedLoad(rows, 2), breaker * 0.8f);
            }
        }

        [Test]
        public void SmartHandsAsksAboutRealDevices()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.SmartHands);
            foreach (var task in t.tasks.Where(x => x.kind == TaskKind.Inspect))
            {
                var d = s.Dev(t.rack, task.deviceId);
                Assert.IsNotNull(d);
                Assert.That(task.component == "serial" || task.component == "host");
                StringAssert.Contains("-", d.Host);
            }
        }

        // ================================================================ priority, follow-ups, progression
        [Test]
        public void P1ClosedFirstPaysARushBonus()
        {
            var s = NewShift();
            var p1 = s.SpawnTicket(TicketType.FixIssue);
            var p3 = s.SpawnTicket(TicketType.Firmware);
            foreach (var task in p3.tasks) s.Complete(task);
            Assert.IsTrue(p1.neglected, "closing routine work while a P1 waits loses its rush bonus");
            var fresh = s.SpawnTicket(TicketType.FixIssue);
            Assert.IsTrue(fresh.RushBonus);
            int money = s.career.money;
            float mul = s.career.PayMul * s.career.CustMul(fresh.customer);
            var diag = fresh.tasks[0];
            if (diag.cart) s.inv.hands.Add(new Item { def = "kvm" });
            s.Complete(diag);
            s.inv.pouch.AddRange(Catalog.Parts);
            foreach (var task in fresh.tasks.ToList()) s.Complete(task);
            Assert.AreEqual(Mathf.RoundToInt(fresh.pay * mul * 1.2f * 1.25f), s.career.money - money);
        }

        [Test]
        public void MistakesCanOpenAFollowUpTicket()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.ReplaceHardware);
            var dev = s.Dev(t.rack, t.tasks[0].deviceId);
            var f = s.FollowUp(t.rack, dev, TaskKind.PowerOn, Fault.NoPower, "lost power", "test");
            Assert.IsTrue(f.followUp);
            Assert.AreEqual(1, f.priority);
            Assert.AreEqual(Fault.NoPower, dev.fault);
            Assert.IsNull(s.FollowUp(t.rack, dev, TaskKind.PowerOn, Fault.NoPower, "again", "test"), "one follow-up per problem");
            int money = s.career.money;
            s.Complete(f.tasks[0]);
            Assert.AreEqual(Fault.None, dev.fault);
            Assert.AreEqual(money, s.career.money, "follow-ups pay nothing");
        }

        [Test]
        public void InterruptionsCanAddUrgentWork()
        {
            int extra = 0;
            for (int seed = 1; seed < 30; seed++)
            {
                var s = new Shift(new Career { day = 5 }, seed);
                for (int i = 0; i < 4; i++)
                {
                    var t = s.SpawnTicket(TicketType.Firmware);
                    if (t == null) break;
                    foreach (var task in t.tasks) s.Complete(task);
                }
                Assert.LessOrEqual(s.interrupts, Shift.MaxInterrupts);
                extra += s.tickets.Count(t => t.type != TicketType.Firmware);
            }
            Assert.Greater(extra, 0, "urgent incidents and surprise deliveries land mid-shift");
        }

        [Test]
        public void XpLevelsAndCustomerMoodRiseWithGoodWork()
        {
            var s = NewShift();
            int xp = s.career.xp;
            var t = s.SpawnTicket(TicketType.Firmware);
            float sat = s.career.Sat(t.customer);
            foreach (var task in t.tasks) s.Complete(task);
            Assert.AreEqual(xp + 15, s.career.xp, "10 XP plus 5 for clean work");
            Assert.Greater(s.career.Sat(t.customer), sat);
            var c = new Career { xp = Catalog.LevelXp[2] };
            Assert.AreEqual(3, c.Level);
            Assert.Greater(c.PayMul, new Career().PayMul);
        }

        [Test]
        public void CertificationsUnlockTicketTypes()
        {
            var c = new Career { day = 5 };
            var s = NewShift(career: c);
            CollectionAssert.DoesNotContain(s.Available(), TicketType.CrossConnect);
            c.upgrades.Add("cert_fiber");
            c.upgrades.Add("cert_power");
            CollectionAssert.Contains(s.Available(), TicketType.CrossConnect);
            CollectionAssert.Contains(s.Available(), TicketType.PowerAudit);
        }

        // ================================================================ stock, RMA and receiving
        static Ticket Spawn(Shift s, TicketType type, System.Func<Ticket, bool> want = null, int tries = 60)
        {
            for (int i = 0; i < tries; i++)
            {
                var t = s.SpawnTicket(type);
                if (t != null && (want == null || want(t))) return t;
                if (t != null) s.tickets.Remove(t);
            }
            return null;
        }

        [Test]
        public void SpareShelvesRunOutAndOrdersArriveAsAReceivingTicket()
        {
            var career = new Career { day = 5, money = 1000 };
            var s = NewShift(career: career);
            Assert.AreEqual(Catalog.StartStock(PartKind.Drive), career.Stock(PartKind.Drive), "a new career starts with stocked shelves");
            career.AddStock(PartKind.Drive, -career.Stock(PartKind.Drive) + 1);
            Assert.IsTrue(s.TakePart(PartKind.Drive));
            Assert.IsFalse(s.TakePart(PartKind.Drive), "the shelf is empty");
            Assert.AreEqual(0, career.Stock(PartKind.Drive));
            s.ReturnPart(PartKind.Drive);
            Assert.AreEqual(1, career.Stock(PartKind.Drive), "unused parts go back on the shelf");
            Assert.IsNull(s.Order(PartKind.Drive, 5));
            Assert.AreEqual(1000 - 5 * Catalog.PartPrice(PartKind.Drive), career.money);
            Assert.IsNotNull(s.Order(PartKind.Psu, 100), "can't order what you can't pay for");
            s.End();

            var next = NewShift(career: career, seed: 9);
            next.GenerateQueue();
            var recv = next.tickets.Single(t => t.type == TicketType.Receiving);
            Assert.AreEqual(5, recv.cartons.Where(c => !c.foreign).Sum(c => c.qty), "the whole order is on the pallet");
            Assert.IsEmpty(career.orders);
            int before = career.Stock(PartKind.Drive);
            foreach (var c in recv.cartons) c.decision = c.Good ? 1 : 2;
            next.Complete(recv.tasks[0]);
            int good = recv.cartons.Where(c => c.Good && c.part == PartKind.Drive).Sum(c => c.qty);
            int bad = recv.cartons.Where(c => c.damaged && !c.foreign).Sum(c => c.qty);
            Assert.AreEqual(before + good, career.Stock(PartKind.Drive), "accepted cartons go on the shelf");
            Assert.AreEqual(bad, career.Ordered(PartKind.Drive), "refused damaged cartons are re-shipped");
        }

        [Test]
        public void FailedPartsGoInRmaBagsAndEarnCredit()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.ReplaceHardware);
            var part = t.tasks[0].kind == TaskKind.SwapDrive ? PartKind.Drive : PartKind.Psu;
            Assert.IsTrue(s.TakePart(part));
            s.Complete(t.tasks[0]);
            Assert.AreEqual(1, s.inv.rma.Count, "the failed part comes out in an RMA bag");
            s.inv.pouchCap = 1;
            Assert.IsTrue(s.inv.PouchFull, "RMA bags take pouch space");
            int money = s.career.money;
            int credit = s.ReturnRma();
            Assert.AreEqual(Mathf.RoundToInt(Catalog.PartPrice(part) * Catalog.RmaCredit), credit);
            Assert.AreEqual(money + credit, s.career.money);
            Assert.IsEmpty(s.inv.rma);
        }

        [Test]
        public void ProcurementSendsAnEmergencySpareWhenAShelfIsEmpty()
        {
            var career = new Career { day = 5 };
            var s = NewShift(career: career);
            career.AddStock(PartKind.Fan, -99);
            s.End();
            var next = NewShift(career: career, seed: 4);
            next.GenerateQueue();
            Assert.AreEqual(1, career.Stock(PartKind.Fan));
            StringAssert.Contains("emergency spare", next.news);
        }

        // ================================================================ new ticket types
        [Test]
        public void SwitchOutagesTakeTheRackDownUntilFixed()
        {
            var seen = new System.Collections.Generic.HashSet<Fault>();
            for (int seed = 1; seed < 40; seed++)
            {
                var s = NewShift(5, seed);
                var t = s.SpawnTicket(TicketType.SwitchOutage);
                Assert.AreEqual(1, t.priority);
                var rack = s.Rack(t.rack);
                var sw = Shift.SwitchOf(rack);
                Assert.AreEqual(sw.id, t.tasks[0].deviceId);
                Assert.IsTrue(Shift.Isolated(rack), "the whole rack is cut off");
                seen.Add(sw.fault);
                if (t.tasks[0].cart) s.inv.hands.Add(new Item { def = "kvm" });
                var fault = sw.fault;
                s.Complete(t.tasks[0]);
                var fix = t.tasks.First(x => x.kind != TaskKind.Diagnose && x.kind != TaskKind.Photo);
                Assert.AreEqual(fault == Fault.ConfigLost ? TaskKind.ConfigRestore : fault == Fault.BadSfp ? TaskKind.SwapSfp : TaskKind.PowerCycle, fix.kind);
                s.inv.pouch.Add(PartKind.Sfp);
                if (fix.cart) s.inv.hands.Add(new Item { def = "kvm" });
                s.Complete(fix);
                Assert.IsFalse(Shift.Isolated(rack));
            }
            Assert.IsTrue(seen.Contains(Fault.ConfigLost));
        }

        [Test]
        public void StorageServiceSwapsALocatedDriveOrASasPath()
        {
            bool locate = false, sas = false;
            for (int seed = 1; seed < 30 && !(locate && sas); seed++)
            {
                var s = NewShift(5, seed);
                var t = s.SpawnTicket(TicketType.StorageService);
                if (t == null) continue;
                var task = t.tasks[0];
                Assert.AreEqual("ds4u", s.Dev(t.rack, task.deviceId).def);
                if (task.kind == TaskKind.SasPath) { sas = true; Assert.IsTrue(Shift.RearJob(task.kind)); }
                else { locate = true; Assert.AreEqual("locate", task.component); Assert.IsFalse(s.Dev(t.rack, task.deviceId).Healthy); }
            }
            Assert.IsTrue(locate && sas, "both kinds of shelf work show up");
        }

        [Test]
        public void BlankingPanelsCoolAHotRack()
        {
            var s = NewShift(6);
            var t = Spawn(s, TicketType.Cooling, k => k.tasks[0].kind == TaskKind.Blanking);
            Assert.IsNotNull(t);
            var rack = s.Rack(t.rack);
            Assert.IsTrue(rack.hot);
            Assert.GreaterOrEqual(rack.openGaps.Count, 3);
            foreach (int u in rack.openGaps) Assert.IsNull(rack.At(u), "gaps are empty slots");
            s.Complete(t.tasks[0]);
            Assert.IsFalse(rack.hot);
            Assert.IsEmpty(rack.openGaps);
        }

        [Test]
        public void DecommissioningLeavesGapsAndInstallsCloseThem()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.Decommission);
            var rack = s.Rack(t.rack);
            var un = t.tasks.First(x => x.kind == TaskKind.Uncable);
            int u = un.u;
            s.Complete(un);
            s.Complete(t.tasks.First(x => x.kind == TaskKind.Unmount && x.deviceId == un.deviceId));
            Assert.Contains(u, rack.openGaps, "nobody blanks the slot on the way out");
        }

        [Test]
        public void FloorTileLayoutsStartWarmAndCanBeBalanced()
        {
            for (int seed = 1; seed < 25; seed++)
            {
                var s = NewShift(6, seed);
                var t = Spawn(s, TicketType.Cooling, k => k.tasks[0].kind == TaskKind.Tiles);
                if (t == null) continue;
                var task = t.tasks[0];
                Assert.AreEqual($"aisle{task.u}", task.station);
                var l = Shift.Tiles(s.racks, task.u, task.slot);
                Assert.IsTrue(Enumerable.Range(0, l.Cols).Any(i => Shift.InletTemp(l, i) > 27f), "someone left the tiles wrong");
                int perf = 0, need = 0;
                foreach (var p in l.perf) if (p) perf++;
                for (int i = 0; i < l.Cols; i++) need += Shift.TilesNeeded(l.racks[i]);
                Assert.AreEqual(need, perf, "there are exactly enough perforated tiles");
                System.Array.Clear(l.perf, 0, l.perf.Length);
                for (int i = 0; i < l.Cols; i++) for (int d = 0; d < Shift.TilesNeeded(l.racks[i]); d++) l.perf[i, d] = true;
                Assert.IsTrue(Enumerable.Range(0, l.Cols).All(i => Shift.InletTemp(l, i) <= 27f), "tiles in front of the racks that need them fix it");
                s.Complete(task);
                Assert.IsFalse(s.racks.Any(r => r.row == task.u && r.hot));
            }
        }

        [Test]
        public void HotRacksCookTheirFans()
        {
            var s = NewShift(6);
            foreach (var r in s.racks) r.hot = r.owner != null;
            int fans = 0;
            for (int i = 0; i < 20; i++)
            {
                var t = s.SpawnTicket(TicketType.FixIssue);
                if (t != null && s.Dev(t.rack, t.tasks[0].deviceId).fault == Fault.FanFail) fans++;
            }
            Assert.Greater(fans, 5, "hot racks mostly fail with fans");
        }

        [Test]
        public void CableAuditsTidyTheRack()
        {
            var s = NewShift(6);
            var t = s.SpawnTicket(TicketType.CableAudit);
            var rack = s.Rack(t.rack);
            Assert.IsFalse(rack.tidy);
            Assert.IsTrue(Shift.RearJob(t.tasks[0].kind));
            s.Complete(t.tasks[0]);
            Assert.IsTrue(rack.tidy);
            Assert.AreEqual(1, s.career.stats.racksTidied);
        }

        [Test]
        public void VisitorsWithTheRightIdGetEscortedIn()
        {
            var s = NewShift();
            var t = Spawn(s, TicketType.Escort, k => k.tasks[0].component == "allow");
            var badge = t.tasks[0];
            var walkIn = t.tasks[1];
            var walkOut = t.tasks[2];
            Assert.AreEqual("kiosk", badge.station);
            Assert.IsNotNull(s.Blocker(walkIn), "badge them in first");
            badge.cart = true;
            s.Complete(badge);
            Assert.IsTrue(t.Open);
            Assert.IsNotNull(s.Blocker(walkIn), "the escort itself happens in the world");
            Assert.IsTrue(Shift.WorldJob(walkIn.kind));
            s.Complete(walkIn);
            s.Complete(walkOut);
            Assert.AreEqual(TicketStatus.Done, t.status);
            Assert.AreEqual(1, s.career.stats.visitors);
        }

        [Test]
        public void ImpostorsTurnedAwayCloseTheTicket()
        {
            var s = NewShift();
            var t = Spawn(s, TicketType.Escort, k => k.tasks[0].component == "deny", 200);
            Assert.IsNotNull(t);
            Assert.IsNotNull(t.tasks[0].version, "there's a reason they shouldn't get in");
            t.tasks[0].cart = false;
            s.Complete(t.tasks[0]);
            Assert.AreEqual(TicketStatus.Done, t.status);
            Assert.AreEqual(1, s.career.stats.impostorsCaught);
        }

        [Test]
        public void MaintenanceOffersAreOptionalAndProtectTheRack()
        {
            var career = new Career { day = 5 };
            var s = NewShift(career: career);
            s.GenerateQueue();
            Assert.IsNotEmpty(s.offers);
            Assert.IsFalse(s.tickets.Any(t => t.type == TicketType.Maintenance), "offers aren't on the work order until accepted");
            var offer = s.offers[0];
            s.AcceptOffer(offer);
            Assert.Contains(offer, s.tickets);
            foreach (var task in offer.tasks) s.Complete(task);
            Assert.AreEqual(TicketStatus.Done, offer.status);
            Assert.AreEqual(career.day, s.Rack(offer.rack).pmDay);
            Assert.Greater(career.stats.caughtEarly, 0);
            float rep = career.rep;
            int open = s.OpenTickets.Count();
            s.End();
            Assert.AreEqual(rep - 4f * open, career.rep, 0.001f, "lapsed offers cost nothing");
        }

        // ================================================================ customers
        [Test]
        public void EvidenceCustomersWantAPhotoAndUrgentOnesAreP2()
        {
            var s = NewShift();
            var bank = Spawn(s, TicketType.ReplaceHardware, k => k.customer == "Northwind Bank", 300);
            Assert.IsNotNull(bank);
            Assert.IsTrue(bank.tasks.Any(x => x.kind == TaskKind.Photo && x.component == "front"));
            var games = Spawn(s, TicketType.Firmware, k => k.customer == "Orbital Games", 300);
            Assert.IsNotNull(games);
            Assert.AreEqual(2, games.priority, "everything is urgent for them");
            var c = new Career();
            c.ChangeSat("Quanta Retail", 10f);
            c.ChangeSat("Helix Genomics", 10f);
            Assert.AreEqual(75f, c.Sat("Quanta Retail"), 0.01f, "fickle customers swing harder");
            Assert.AreEqual(70f, c.Sat("Helix Genomics"), 0.01f);
            Assert.Greater(c.CustMul("Helix Genomics"), new Career().CustMul("Kestrel Insurance"), "generous customers tip more");
        }

        // ================================================================ progression
        [Test]
        public void TheTraineeFinishesSimpleTicketsForLessPay()
        {
            var career = new Career { day = 8, xp = Catalog.LevelXp[Catalog.TraineeLevel - 1] };
            var s = NewShift(career: career);
            Assert.IsTrue(s.HasTrainee);
            var t = s.SpawnTicket(TicketType.Firmware);
            var p1 = s.SpawnTicket(TicketType.FixIssue);
            Assert.IsNotNull(s.TraineeBlocker(p1), "incidents are too tricky");
            Assert.IsTrue(s.AssignTrainee(t));
            Assert.IsNotNull(s.TraineeBlocker(s.SpawnTicket(TicketType.Firmware)), "one ticket at a time");
            int money = career.money;
            s.Tick(1000f);
            Assert.AreEqual(TicketStatus.Done, t.status);
            Assert.Greater(career.money, money);
            Assert.AreEqual(1, career.traineeDone);
            Assert.IsFalse(p1.neglected, "the trainee's work doesn't cost your P1 its rush bonus");
        }

        [Test]
        public void TheWeeklyReviewPaysForGoalsMet()
        {
            var career = new Career { day = 7, weekStart = 1, money = 0 };
            career.goals.Add(new Goal { id = "clean", target = 2, progress = 2 });
            career.goals.Add(new Goal { id = "nofollow" });
            career.goals.Add(new Goal { id = "earn", target = 5000, progress = 10 });
            var s = NewShift(career: career);
            s.End();
            Assert.AreEqual(2 * Catalog.GoalCash, career.money, "two of three goals met");
            Assert.IsEmpty(career.goals, "a new week starts");
            Assert.IsNotEmpty(s.endNotes);
            var next = NewShift(career: career, seed: 3);
            next.GenerateQueue();
            Assert.AreEqual(3, career.goals.Count);
            Assert.AreEqual(8, career.weekStart);
        }

        [Test]
        public void AchievementsUnlockAsTheCareerGrows()
        {
            var s = NewShift();
            var t = s.SpawnTicket(TicketType.Firmware);
            foreach (var task in t.tasks) s.Complete(task);
            CollectionAssert.Contains(s.career.achievements, "first");
            CollectionAssert.Contains(s.unlocked, "first");
            Assert.AreEqual(1, s.career.stats.streak);
        }

        [Test]
        public void TheExpansionCagesOpenForALeadTechnician()
        {
            var career = new Career { day = 9, xp = Catalog.LevelXp[Catalog.ExpansionLevel - 1] };
            var s = NewShift(career: career);
            Assert.AreEqual(Shift.RacksPerRow, Shift.ColsOf(s.racks));
            s.GenerateQueue();
            Assert.AreEqual(Shift.RacksPerRow + Shift.ExpansionCols, Shift.ColsOf(s.racks));
            Assert.AreEqual(Shift.Rows.Length * (Shift.RacksPerRow + Shift.ExpansionCols), s.racks.Count);
            StringAssert.Contains("expansion", s.news);
        }

        // ================================================================ emergencies
        [Test]
        public void APowerFailureNeedsTheGeneratorBeforeTheUpsRunsOut()
        {
            var s = NewShift(8);
            var t = s.TriggerEmergency("power");
            Assert.AreEqual(1, t.priority);
            Assert.IsNull(t.rack);
            Assert.AreEqual(Shift.UpsSeconds, s.ups, 0.01f);
            s.Tick(10f);
            s.Complete(t.tasks[0]);
            Assert.AreEqual(0f, s.ups);
            Assert.AreEqual(TicketStatus.Done, t.status);
            Assert.AreEqual(1, s.career.stats.emergencies);

            var late = NewShift(8, 2);
            var t2 = late.TriggerEmergency("power");
            float rep = late.career.rep;
            late.Tick(Shift.UpsSeconds + 1f);
            Assert.AreEqual(TicketStatus.HandedOver, t2.status, "the hall went dark");
            Assert.AreEqual(rep - 12f, late.career.rep, 0.001f);
        }

        [Test]
        public void LeaksAndSmokeAlarmsGoInOrder()
        {
            var s = NewShift(8);
            var leak = s.TriggerEmergency("leak");
            var valve = leak.tasks.First(x => x.kind == TaskKind.Valve);
            var mop = leak.tasks.First(x => x.kind == TaskKind.Mop);
            Assert.AreEqual($"crac{Shift.CracFor(valve.u)}", valve.station);
            Assert.IsNotNull(s.Blocker(mop), "isolate the loop before mopping");
            s.Complete(valve);
            Assert.IsNull(s.Blocker(mop));
            s.Complete(mop);
            Assert.AreEqual(TicketStatus.Done, leak.status);

            var smoke = s.TriggerEmergency("vesda");
            var psu = smoke.tasks.First(x => x.kind == TaskKind.SwapPsu);
            var reset = smoke.tasks.First(x => x.kind == TaskKind.ResetAlarm);
            Assert.IsNotNull(s.Blocker(reset));
            s.inv.pouch.Add(PartKind.Psu);
            s.Complete(psu);
            Assert.IsNull(s.Blocker(reset));
        }
    }
}
