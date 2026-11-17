using System.Linq;
using NUnit.Framework;

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
                Assert.Greater(k.due, k.created);
                Assert.Greater(k.pay, 0);
                Assert.IsNotNull(s.Rack(k.rack));
            }
        }

        [Test]
        public void TicketsArriveOverTheDay()
        {
            var s = NewShift();
            int n = 0;
            s.TicketArrived += t => { n++; t.status = TicketStatus.Done; }; // keep the queue below its cap
            while (!s.Over) s.Advance(1f);
            Assert.Greater(n, 8);
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
        public void LateAndUnfinishedWorkCostsReputation()
        {
            var s = NewShift();
            float rep = s.career.rep;
            var t = s.SpawnTicket(TicketType.Firmware);
            s.minute = t.due + 10;
            foreach (var task in t.tasks) s.Complete(task);
            Assert.Less(s.career.rep, rep);
            Assert.AreEqual(1, s.late);
            s.SpawnTicket(TicketType.Firmware);
            float before = s.career.rep;
            int day = s.career.day;
            s.End();
            Assert.Less(s.career.rep, before);
            Assert.AreEqual(1, s.handedOver);
            Assert.AreEqual(day + 1, s.career.day);
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
