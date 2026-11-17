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
                Assert.IsNotNull(s.Rack(k.rack));
                foreach (var task in k.tasks) Assert.IsNotEmpty(Shift.Describe(task));
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
                Assert.AreEqual(2, t.tasks.Count);
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
            s.Complete(fresh.tasks[1]);
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
    }
}
