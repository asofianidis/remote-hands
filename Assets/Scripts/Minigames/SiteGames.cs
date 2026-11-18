using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// The receiving bay: check every carton on the inbound pallet before signing for it. Accept the good
    /// ones, refuse anything crushed, with a tripped tip-and-tell, or addressed to another site.
    /// </summary>
    public class ReceiveGame : Minigame
    {
        public override bool InWorld => true;
        Ticket ticket;
        readonly List<Transform> boxes = new List<Transform>();
        int sel = -1;
        string po;

        protected override void Begin()
        {
            ticket = shift.TicketOf(task);
            var cartons = ticket.cartons;
            po = cartons.FirstOrDefault(c => !c.foreign)?.po ?? "PO-0000";
            var rnd = new System.Random(ticket.id);
            MakeRig(game.facility.transform);
            var basePos = Facility.ReceivingPos + new Vector3(0, 0.141f, 0);
            var red = Mats.Unlit(new Color(1f, 0.15f, 0.1f));
            var white = Mats.Unlit(new Color(0.92f, 0.92f, 0.9f));
            var crush = Mats.Lit(new Color(0.35f, 0.26f, 0.16f), 0.05f);
            for (int i = 0; i < cartons.Count; i++)
            {
                var c = cartons[i];
                int layer = i / 4, k = i % 4;
                var t = new GameObject("carton").transform;
                t.SetParent(rig, false);
                t.localPosition = basePos + new Vector3((k % 2) * 0.5f - 0.25f, layer * 0.185f, (k / 2) * 0.48f - 0.24f);
                // Crushed or tripped: half the damaged ones show it on the box, half on the indicator.
                bool crushed = c.damaged && rnd.Next(2) == 0;
                bool tripped = c.damaged && !crushed;
                Models.Spawn("carton_2u", t, Vector3.zero, crushed ? Quaternion.Euler(0, 0, 6f) : Quaternion.identity, scale: crushed ? new Vector3(1f, 0.82f, 1f) : Vector3.one);
                if (crushed) Box(t, new Vector3(0.17f, 0.13f, -0.2f), new Vector3(0.12f, 0.06f, 0.12f), crush, "dent").transform.localRotation = Quaternion.Euler(25f, 30f, 10f);
                // Tip-and-tell indicator on the front face.
                Box(t, new Vector3(-0.15f, 0.12f, -0.252f), new Vector3(0.035f, 0.035f, 0.002f), tripped ? red : white, "tip-n-tell");
                boxes.Add(t);
            }
            var target = basePos + new Vector3(0.1f, 0.1f, 0);
            var dir = new Vector3(-0.3f, 0.6f, -0.75f).normalized;
            Look(target + dir * FitDistance(2.6f, 1.6f, 0.85f), target);
            title = $"Receiving: {cartons.Count} cartons for {po}";
            hint = "Click a carton to read its label, then accept or refuse it. Look at the box itself too: crushed corners and red tip-and-tell dots mean refuse.";
        }

        Rect BoxRect(int i) { var r = UiRect(boxes[i]); return new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8); }

        protected override void Update(float dt, Vector2 m)
        {
            if (!Pressed) return;
            for (int i = boxes.Count - 1; i >= 0; i--)
                if (BoxRect(i).Contains(m) && !PanelRect.Contains(m)) { sel = i; Audio.Play("click"); return; }
        }

        Rect PanelRect => new Rect(Ui.W - 520, 110, 500, 420);

        void Decide(Carton c, bool accept)
        {
            c.decision = accept ? 1 : 2;
            if (accept && c.foreign) Oops("That carton was addressed to another site. Now it's on your shelf and someone else is short", 1f);
            else if (accept && c.damaged) Oops("You signed for a damaged carton. Its parts are dead on arrival and you can't send it back now", 1.5f);
            else if (!accept && c.Good) Oops("Nothing was wrong with that carton. It goes back to the vendor for nothing", 0.5f);
            else Audio.Play(accept ? "pickup" : "beep");
            sel = -1;
        }

        public override void Draw()
        {
            WorldHud();
            var cartons = ticket.cartons;
            var m = Ui.Mouse;
            for (int i = 0; i < boxes.Count; i++)
            {
                var c = cartons[i];
                var r = BoxRect(i);
                if (i == sel) Ui.Border(r, Ui.Accent, 2f);
                else if (r.Contains(m)) Ui.Border(r, new Color(1, 1, 1, 0.4f), 1.5f);
                if (c.decision != 0) Chip(boxes[i].position + new Vector3(0, 0.26f, 0), c.decision == 1 ? "ACCEPTED" : "REFUSED", 12, c.decision == 1 ? Ui.Good : Ui.Warn);
            }
            // The packing list for this PO.
            var lines = cartons.Where(c => !c.foreign).GroupBy(c => c.part).Select(g => $"{g.Sum(c => c.qty)}x {Catalog.PartName(g.Key)}  ({g.Count()} carton{(g.Count() > 1 ? "s" : "")})").ToList();
            var pl = new Rect(20, 110, 440, 80 + lines.Count * 26);
            Ui.Box(pl, new Color(0.92f, 0.92f, 0.88f, 0.96f));
            Ui.Label(new Rect(pl.x + 16, pl.y + 10, pl.width - 32, 26), $"PACKING LIST  {po}", 17, new Color(0.1f, 0.1f, 0.12f), TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(pl.x + 16, pl.y + 36, pl.width - 32, 22), "SHIP TO: DC-1, data hall 1 (this site)", 14, new Color(0.25f, 0.25f, 0.3f));
            for (int i = 0; i < lines.Count; i++) Ui.Label(new Rect(pl.x + 16, pl.y + 64 + i * 26, pl.width - 32, 24), lines[i], 16, new Color(0.1f, 0.1f, 0.12f));
            if (sel >= 0)
            {
                var c = cartons[sel];
                var p = PanelRect;
                Ui.Box(p, new Color(0.03f, 0.05f, 0.07f, 0.95f));
                Ui.Label(new Rect(p.x + 20, p.y + 16, p.width - 40, 28), "CARTON LABEL", 15, Ui.Accent, TextAnchor.UpperLeft, true);
                var lab = new Rect(p.x + 20, p.y + 48, p.width - 40, 170);
                Ui.Box(lab, new Color(0.95f, 0.95f, 0.9f));
                var ink = new Color(0.08f, 0.08f, 0.1f);
                Ui.Label(new Rect(lab.x + 14, lab.y + 10, lab.width - 28, 150),
                    $"<b>{c.po}</b>\nSHIP TO: {(c.foreign ? "DC-2, Frankfurt" : "DC-1, data hall 1")}\nCONTENTS: {c.qty}x {Catalog.PartName(c.part)}\nCARTON {sel + 1} OF {cartons.Count}", 18, ink);
                if (c.decision == 0)
                {
                    if (Ui.Button(new Rect(p.x + 20, p.y + 240, 220, 60), "Accept", true, 20, Ui.Good)) Decide(c, true);
                    if (Ui.Button(new Rect(p.xMax - 240, p.y + 240, 220, 60), "Refuse", true, 20, Ui.Bad)) Decide(c, false);
                }
                else Ui.Label(new Rect(p.x + 20, p.y + 250, p.width - 40, 40), c.decision == 1 ? "Accepted." : "Refused.", 20, Ui.Dim, TextAnchor.MiddleCenter, true);
            }
            if (cartons.All(c => c.decision != 0) && Ui.Button(new Rect(Ui.W / 2 - 220, Ui.H - 200, 440, 60), "Sign for the delivery", true, 20, Ui.Good))
                Win($"Signed. {cartons.Where(c => c.decision == 1 && c.Good).Sum(c => c.qty)} parts are on the shelves.");
        }
    }

    /// <summary>
    /// The security kiosk: compare the visitor's ID card with the customer's access list. Badge them in
    /// if everything matches, deny entry if anything doesn't.
    /// </summary>
    public class BadgeGame : Minigame
    {
        public override bool InWorld => true;
        string name, company, empId, reason;
        int expires;
        List<(string name, string id, int until)> access;
        Color tint;

        protected override void Begin()
        {
            var t = shift.TicketOf(task);
            var rnd = new System.Random(task.slot);
            name = task.tag;
            reason = task.version;
            string cust = t.customer;
            company = cust;
            empId = $"E{rnd.Next(10000, 99999)}";
            expires = career.day + 30 + rnd.Next(300);
            int until = career.day + 5 + rnd.Next(60);
            string[] first = { "Alex", "Jordan", "Sam", "Riley", "Morgan", "Casey", "Taylor", "Jamie" };
            string[] last = { "Park", "Ivanova", "Diallo", "Kowalski", "Santos", "Murphy", "Ito", "Becker" };
            access = new List<(string, string, int)>();
            for (int i = 0; i < 4; i++) access.Add(($"{first[rnd.Next(first.Length)]} {last[rnd.Next(last.Length)]}", $"E{rnd.Next(10000, 99999)}", career.day + 3 + rnd.Next(90)));
            var parts = name.Split(' ');
            switch (reason)
            {
                case "unlisted":
                    // Someone with the same first name is on the list; this visitor isn't.
                    access.Add(($"{parts[0]} {last[rnd.Next(last.Length)]}", $"E{rnd.Next(10000, 99999)}", until));
                    break;
                case "expired":
                    access.Add((name, empId, career.day - 1 - rnd.Next(20)));
                    break;
                case "company":
                    access.Add((name, empId, until));
                    company = Catalog.Customers.Where(c => c.name != cust).OrderBy(_ => rnd.Next()).First().name;
                    break;
                default:
                    access.Add((name, empId, until));
                    break;
            }
            access = access.OrderBy(_ => rnd.Next()).ToList();
            tint = Catalog.CustColor(company);
            MakeRig(game.facility.transform);
            var c = Facility.KioskPos + new Vector3(0, 1.2f, 0);
            Look(c + new Vector3(0.25f, 0.35f, 1.1f), c);
            title = $"Security kiosk: {name} for {cust}";
            hint = $"Compare the ID card with {cust}'s access list: the name, the employee ID, the company, and the date the access runs out. Today is day {career.day}.";
        }

        protected override void Update(float dt, Vector2 m) { }

        void Choose(bool admit)
        {
            bool shouldAdmit = reason == null;
            task.cart = admit;
            if (admit && !shouldAdmit)
            {
                Oops(reason switch
                {
                    "expired" => "Their access had expired. You just badged an unauthorized person onto the floor",
                    "company" => "Their ID says another company. You just badged a stranger onto the floor",
                    _ => "They weren't on the access list. You just badged a stranger onto the floor",
                }, 4f);
                Win(null);
            }
            else if (!admit && shouldAdmit)
            {
                Oops($"{name} was on the list and everything matched. {t.customer} is furious you turned their engineer away", 2f);
                career.ChangeSat(t.customer, -6f);
                Win(null);
            }
            else Win(admit ? $"{name} is badged in. Walk them to {task.rack} and stay with them." : $"Entry denied. Security escorted {name} off the premises: {Why()}.");
        }

        Ticket t => shift.TicketOf(task);

        string Why() => reason switch { "expired" => "their access had expired", "company" => "their ID was from another company", _ => "they weren't on the access list" };

        public override void Draw()
        {
            WorldHud();
            // The visitor's ID card.
            var card = new Rect(40, 110, 520, 300);
            Ui.Box(card, new Color(0.96f, 0.96f, 0.94f));
            Ui.Box(new Rect(card.x, card.y, card.width, 52), tint);
            var ink = new Color(0.08f, 0.08f, 0.1f);
            Ui.Label(new Rect(card.x + 20, card.y + 10, card.width - 40, 34), company.ToUpperInvariant(), 22, ink, TextAnchor.MiddleLeft, true);
            Ui.Box(new Rect(card.x + 20, card.y + 72, 130, 160), new Color(0.75f, 0.78f, 0.82f));
            Ui.Circle(new Vector2(card.x + 85, card.y + 130), 34, new Color(0.45f, 0.48f, 0.52f));
            Ui.Box(new Rect(card.x + 40, card.y + 172, 90, 60), new Color(0.45f, 0.48f, 0.52f));
            Ui.Label(new Rect(card.x + 170, card.y + 76, 330, 160), $"<b>{name}</b>\nField Engineer\n\nEmployee ID  {empId}\nValid until  day {expires}", 20, ink);
            // The customer's access list, from the NOC.
            var cust = t.customer;
            var al = new Rect(Ui.W - 640, 110, 600, 120 + access.Count * 40);
            Ui.Box(al, new Color(0.03f, 0.05f, 0.07f, 0.95f));
            Ui.Label(new Rect(al.x + 20, al.y + 14, al.width - 40, 28), $"ACCESS LIST: {cust.ToUpperInvariant()}", 18, Ui.Accent, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(al.x + 20, al.y + 48, al.width - 40, 22), "NAME                         EMPLOYEE ID        ACCESS UNTIL", 14, Ui.Dim, TextAnchor.UpperLeft, true);
            for (int i = 0; i < access.Count; i++)
            {
                var (n, id, until) = access[i];
                var y = al.y + 78 + i * 40;
                Ui.Label(new Rect(al.x + 20, y, 250, 34), n, 18, Ui.Text);
                Ui.Label(new Rect(al.x + 270, y, 160, 34), id, 18, Ui.Text);
                Ui.Label(new Rect(al.x + 440, y, 140, 34), $"day {until}", 18, until < career.day ? Ui.Bad : Ui.Text);
            }
            if (Ui.Button(new Rect(40, 440, 250, 64), "Badge in", !finished, 21, Ui.Good)) Choose(true);
            if (Ui.Button(new Rect(310, 440, 250, 64), "Deny entry", !finished, 21, Ui.Bad)) Choose(false);
        }
    }
}
