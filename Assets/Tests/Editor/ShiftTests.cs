using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace RH.Tests
{
    public class ShiftTests
    {
        static Shift NewShift(int day = 5, int seed = 1) => new Shift(new Career { day = day }, seed);

        [Test]
        public void HallHasFortyRacksWithoutOverlaps()
        {
            var s = NewShift();
            Assert.AreEqual(Shift.Rows.Length * Shift.RacksPerRow, s.racks.Count);
            foreach (var r in s.racks)
                for (int u = 1; u <= Rack.Units; u++)
                    Assert.LessOrEqual(r.devices.Count(d => u >= d.u && u <= d.Top), 1, $"{r.label} U{u} overlaps");
            Assert.IsTrue(s.racks.Any(r => r.Empty), "some racks should be vacant for new contracts");
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
        }

        [Test]
        public void DiagnosisAddsTheRightFix()
        {
            for (int seed = 1; seed < 20; seed++)
            {
                var s = NewShift(5, seed);
                var t = s.SpawnTicket(TicketType.FixIssue);
                var dev = s.Dev(t.rack, t.tasks[0].deviceId);
                var fault = dev.fault;
                Assert.AreNotEqual(Fault.None, fault);
                s.Complete(t.tasks[0]);
                Assert.AreEqual(2, t.tasks.Count);
                var fix = t.tasks[1];
                var expected = fault == Fault.BadSfp ? TaskKind.SwapSfp : fault == Fault.Hung ? TaskKind.PowerCycle : TaskKind.Reseat;
                Assert.AreEqual(expected, fix.kind);
                if (fix.kind == TaskKind.SwapSfp) s.inv.pouch.Add(PartKind.Sfp);
                s.Complete(fix);
                Assert.AreEqual(Fault.None, dev.fault);
                Assert.AreEqual(TicketStatus.Done, t.status);
                Assert.AreEqual(0, dev.ticket);
            }
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
            float cleanMul = s.career.PayMul; // captured before the ticket's own reputation gain
            foreach (var task in clean.tasks) s.Complete(task);
            int cleanPay = s.career.money - money;
            Assert.AreEqual(Mathf.RoundToInt(clean.pay * cleanMul * 1.2f), cleanPay, "fumble-free work pays +20%");

            float rep = s.career.rep;
            s.working = messy;
            s.Mistake("pulled the wrong drive", 5f);
            Assert.AreEqual(rep - 5f, s.career.rep, 0.001f);
            Assert.AreEqual(1, messy.mistakes);
            money = s.career.money;
            float mul = s.career.PayMul;
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
        }
    }
}
