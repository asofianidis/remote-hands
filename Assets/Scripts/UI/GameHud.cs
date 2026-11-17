using System.Linq;
using UnityEngine;

namespace RH
{
    public partial class Game
    {
        Vector2 tabletScroll, rackScroll;

        void OnGUI()
        {
            Ui.Begin();
            switch (screen)
            {
                case GameScreen.Menu: DrawMenu(); break;
                case GameScreen.Briefing: DrawBriefing(); break;
                case GameScreen.Play: DrawHud(); break;
                case GameScreen.Tablet: Ui.Shade(); DrawTablet(); break;
                case GameScreen.Rack: Ui.Shade(); DrawRack(); break;
                case GameScreen.Minigame: Ui.Shade(0.75f); minigame?.Draw(); break;
                case GameScreen.Pause: DrawHud(false); DrawPause(); break;
                case GameScreen.Summary: DrawSummary(); break;
                case GameScreen.Shop: DrawShop(); break;
                case GameScreen.Fired: DrawFired(); break;
            }
            DrawToasts();
        }

        static Color Sev(int s) => s == 1 ? Ui.Good : s == 2 ? Ui.Warn : s == 3 ? Ui.Bad : Ui.Accent;

        void DrawToasts()
        {
            // In the world they stack on the right; over a panel only the newest two show, above it.
            bool world = screen == GameScreen.Play;
            float y = world ? 150 : 8;
            int shown = 0;
            for (int i = toasts.Count - 1; i >= 0 && (world || shown < 2); i--, shown++)
            {
                var t = toasts[i];
                float a = Mathf.Clamp01(6f - (Time.unscaledTime - t.time));
                float h = world ? Ui.TextHeight(t.text, 520, 18) + 20 : 44;
                var r = world ? new Rect(Ui.W - 580, y, 560, h) : new Rect(Ui.W / 2 - 560, y, 1120, h);
                Ui.Box(r, new Color(0.05f, 0.07f, 0.09f, 0.88f * a));
                Ui.Box(new Rect(r.x, r.y, 5, r.height), Sev(t.sev) * new Color(1, 1, 1, a));
                Ui.Label(new Rect(r.x + 20, r.y + (world ? 10 : 0), r.width - 40, world ? h - 20 : h), t.text, 18, new Color(1, 1, 1, a), world ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, false, world);
                y += h + (world ? 8 : 4);
            }
        }

        // ================================================================ menus
        void DrawMenu()
        {
            Ui.Box(new Rect(0, 0, 720, Ui.H), new Color(0.02f, 0.03f, 0.05f, 0.86f));
            Ui.Label(new Rect(90, 200, 640, 110), "REMOTE HANDS", 70, Ui.Text, TextAnchor.UpperLeft, true, false);
            Ui.Label(new Rect(94, 300, 600, 40), "A data center technician sim", 26, Ui.Accent);
            Ui.Label(new Rect(94, 350, 560, 90), "Tickets pile up. Customers are waiting. You are the only pair of hands on the floor.", 20, Ui.Dim);
            float y = 480;
            if (Career.Exists)
            {
                var c = Career.Load();
                if (Ui.Button(new Rect(90, y, 420, 64), $"Continue: Day {c.day}  ({Ui.Money(c.money)})", true, 24)) ContinueCareer();
                y += 80;
            }
            if (Ui.Button(new Rect(90, y, 420, 64), Career.Exists ? "New career (erases save)" : "Start career", true, 24)) NewCareer();
            y += 80;
            if (Ui.Button(new Rect(90, y, 420, 64), "Quit", true, 24)) Application.Quit();
            Ui.Label(new Rect(94, Ui.H - 60, 600, 30), "A spin-off from Uptime: Data Center Tycoon", 16, Ui.Dim);
        }

        void DrawBriefing()
        {
            Ui.Shade(0.7f);
            var r = Ui.Center(1000, 760);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 50, r.y + 40, 900, 70), $"DAY {career.day}", 56, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.x + 52, r.y + 110, 900, 30), $"Today's work order: {shift.tickets.Count} tickets   Data hall 1", 22, Ui.Accent);
            Ui.Label(new Rect(r.x + 52, r.y + 170, 440, 200),
                $"Wallet: {Ui.Money(career.money)}\nReputation: {career.rep:0}/100\nTickets closed: {career.ticketsDone}\nBest shift: {Ui.Money(career.bestDay)}", 22, Ui.Text);
            string news = career.day == 1 ? "First day! Expect firmware updates, failed hardware and capacity expansions."
                : career.day == 2 ? "New today: incidents (servers down) and customer equipment deliveries at the loading dock."
                : career.day == 3 ? "New today: full rack build-outs for brand-new contracts."
                : "The site keeps growing, and so does the work order.";
            Ui.Label(new Rect(r.x + 520, r.y + 170, 430, 200), "<b>Dispatch notes</b>\n" + news, 20, Ui.Dim);
            Ui.Label(new Rect(r.x + 52, r.y + 380, 900, 220),
                "<b>How it works</b>\n" +
                "- Today's tickets are on your tablet [Tab]. Each one names a rack and the jobs to do there.\n" +
                "- Grab servers and spare parts in the storage room, customer gear at the loading dock.\n" +
                "- Walk up to the rack and press [E] to work on it. Every job is hands-on.\n" +
                "- There's no clock. Take your time, but finish everything before you clock out from the tablet.\n" +
                "- Unfinished tickets go to the night shift and cost reputation. Mistake-free tickets pay a 20% bonus.",
                19, Ui.Text);
            if (Ui.Button(new Rect(r.x + 50, r.yMax - 100, 300, 60), "Clock in", true, 24, Ui.Good)) ClockIn();
            if (Ui.Button(new Rect(r.xMax - 250, r.yMax - 100, 200, 60), "Main menu", true, 20)) ShowMenu();
        }

        void DrawPause()
        {
            Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(0, 0, 0, 0.6f));
            var r = Ui.Center(560, 470);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x, r.y + 30, r.width, 50), "Paused", 40, Ui.Text, TextAnchor.UpperCenter, true);
            float y = r.y + 110;
            Ui.Label(new Rect(r.x + 50, y, 300, 30), $"Mouse sensitivity: {Player.Sensitivity:0.0}", 19, Ui.Text);
            Player.Sensitivity = GUI.HorizontalSlider(new Rect(r.x + 50, y + 34, 460, 20), Player.Sensitivity, 0.5f, 6f);
            y += 70;
            Ui.Label(new Rect(r.x + 50, y, 300, 30), $"Volume: {Audio.Volume * 100:0}%", 19, Ui.Text);
            Audio.Volume = GUI.HorizontalSlider(new Rect(r.x + 50, y + 34, 460, 20), Audio.Volume, 0f, 1f);
            y += 90;
            if (Ui.Button(new Rect(r.x + 50, y, 460, 56), "Resume", true, 22, Ui.Good))
            {
                PlayerPrefs.SetFloat("sens", Player.Sensitivity);
                PlayerPrefs.SetFloat("vol", Audio.Volume);
                screen = pausedFrom;
            }
            if (Ui.Button(new Rect(r.x + 50, y + 70, 460, 56), "Quit to menu (lose today's progress)", true, 19, Ui.Bad)) ShowMenu();
        }

        // ================================================================ HUD
        void DrawHud(bool full = true)
        {
            if (shift == null) return;
            // Work-order progress (there is no clock: the shift ends when the player clocks out)
            var tl = new Rect(20, 20, 330, 110);
            Ui.Panel(tl);
            Ui.Label(new Rect(tl.x + 18, tl.y + 8, 200, 56), $"{shift.DoneCount}/{shift.tickets.Count}", 46, shift.AllDone ? Ui.Good : Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(tl.x + 190, tl.y + 16, 130, 30), $"DAY {career.day}", 20, Ui.Accent, TextAnchor.UpperRight, true);
            Ui.Label(new Rect(tl.x + 150, tl.y + 42, 170, 30), "tickets done", 15, Ui.Dim, TextAnchor.UpperRight);
            Ui.Bar(new Rect(tl.x + 18, tl.y + 78, tl.width - 36, 12), shift.tickets.Count == 0 ? 0 : shift.DoneCount / (float)shift.tickets.Count, shift.AllDone ? Ui.Good : Ui.Accent);
            // Wallet and reputation
            var tr = new Rect(Ui.W - 360, 20, 340, 110);
            Ui.Panel(tr);
            Ui.Label(new Rect(tr.x + 18, tr.y + 10, 300, 40), Ui.Money(career.money), 34, Ui.Good, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(tr.x + 18, tr.y + 10, tr.width - 36, 40), $"+{Ui.Money(shift.earned)} today", 16, Ui.Dim, TextAnchor.UpperRight);
            Ui.Label(new Rect(tr.x + 18, tr.y + 58, 200, 24), $"Reputation {career.rep:0}", 17, Ui.Text);
            Ui.Bar(new Rect(tr.x + 18, tr.y + 84, tr.width - 36, 10), career.rep / 100f, career.rep > 60 ? Ui.Good : career.rep > 30 ? Ui.Warn : Ui.Bad);

            // Ticket strip
            float y = 150;
            foreach (var t in shift.OpenTickets.Take(6))
            {
                var r = new Rect(20, y, 420, 62);
                Ui.Box(r, new Color(0.05f, 0.07f, 0.09f, 0.82f));
                Ui.Box(new Rect(r.x, r.y, 5, r.height), Catalog.CustColor(t.customer));
                if (t.id == trackedTicket) Ui.Border(r, Ui.Accent, 2f);
                Ui.Label(new Rect(r.x + 16, r.y + 6, 300, 24), $"#{t.id}  {Ticket.TypeName(t.type)}  {t.rack}", 16, Ui.Text, TextAnchor.UpperLeft, true, false);
                Ui.Label(new Rect(r.x + 16, r.y + 32, 300, 24), $"{t.DoneCount}/{t.tasks.Count} tasks", 15, Ui.Dim);
                Ui.Bar(new Rect(r.x + 290, r.y + 26, 112, 10), t.DoneCount / (float)t.tasks.Count, Ui.Accent);
                y += 70;
            }
            if (shift.AllDone)
            {
                var done = new Rect(20, y, 420, 70);
                Ui.Box(done, new Color(0.05f, 0.2f, 0.1f, 0.88f));
                Ui.Border(done, Ui.Good, 2f);
                Ui.Label(done, "Work order complete!\nOpen your tablet [Tab] to clock out.", 18, Ui.Good, TextAnchor.MiddleCenter, true);
            }
            else if (shift.OpenTickets.Count() > 6) Ui.Label(new Rect(24, y, 420, 30), $"+{shift.OpenTickets.Count() - 6} more on your tablet [Tab]", 16, Ui.Dim);

            if (!full) return;
            // Crosshair and interaction prompt
            var c = new Vector2(Ui.W / 2, Ui.H / 2);
            Ui.Circle(c, player.focus != null ? 5f : 3f, player.focus != null ? Ui.Accent : new Color(1, 1, 1, 0.8f));
            if (player.focus != null)
            {
                string prompt = player.focus.Prompt(this);
                float w = Mathf.Min(900, prompt.Length * 11f + 60);
                var pr = new Rect(c.x - w / 2, c.y + 40, w, 44);
                Ui.Box(pr, new Color(0.05f, 0.07f, 0.09f, 0.85f));
                Ui.Label(pr, prompt, 20, Ui.Text, TextAnchor.MiddleCenter, false, false);
            }
            DrawWaypoint();
            DrawCarried();
            Ui.Label(new Rect(Ui.W - 820, Ui.H - 40, 800, 30), "WASD move   Shift sprint   E interact   Tab tickets   G drop   Esc pause", 15, Ui.Dim, TextAnchor.MiddleRight);
        }

        void DrawCarried()
        {
            var inv = shift.inv;
            float w = 560;
            var r = new Rect(Ui.W / 2 - w / 2, Ui.H - 110, w, 90);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 16, r.y + 8, 260, 22), $"HANDS {inv.hands.Count}/{inv.handCap}", 14, Ui.Dim, TextAnchor.UpperLeft, true);
            string hands = inv.hands.Count == 0 ? "empty" : string.Join("\n", inv.hands.Select(h => h.tag != null ? $"{h.Def.name} ({h.tag})" : h.Def.name));
            Ui.Label(new Rect(r.x + 16, r.y + 30, 300, 56), hands, 16, inv.hands.Count > 0 ? Ui.Text : Ui.Dim);
            Ui.Label(new Rect(r.x + 330, r.y + 8, 220, 22), $"POUCH {inv.pouch.Count}/{inv.pouchCap}", 14, Ui.Dim, TextAnchor.UpperLeft, true);
            string pouch = inv.pouch.Count == 0 ? "empty" : string.Join(", ", inv.pouch.GroupBy(p => p).Select(g => $"{g.Count()}x {Catalog.PartName(g.Key)}"));
            Ui.Label(new Rect(r.x + 330, r.y + 30, 220, 56), pouch, 15, inv.pouch.Count > 0 ? Ui.Text : Ui.Dim);
        }

        void DrawWaypoint()
        {
            var target = WaypointTarget();
            if (!target.HasValue) return;
            var cam = player.cam;
            Vector3 sp = cam.WorldToScreenPoint(target.Value);
            float dist = Vector3.Distance(player.transform.position, target.Value);
            Vector2 p = new Vector2(sp.x, UnityEngine.Screen.height - sp.y) / Ui.Scale;
            bool onScreen = sp.z > 0 && p.x > 40 && p.x < Ui.W - 40 && p.y > 40 && p.y < Ui.H - 40;
            if (!onScreen)
            {
                Vector2 dir = new Vector2(sp.x - UnityEngine.Screen.width / 2f, -(sp.y - UnityEngine.Screen.height / 2f));
                if (sp.z < 0) dir = -dir;
                dir.Normalize();
                var c = new Vector2(Ui.W / 2, Ui.H / 2);
                float k = Mathf.Min((Ui.W / 2 - 60) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (Ui.H / 2 - 60) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                p = c + dir * k;
                Ui.Segment(p - dir * 26 + new Vector2(-dir.y, dir.x) * 12, p, Ui.Accent, 5);
                Ui.Segment(p - dir * 26 - new Vector2(-dir.y, dir.x) * 12, p, Ui.Accent, 5);
            }
            else Ui.Ring(p, 16f, Ui.Accent);
            var t = shift.tickets.Find(x => x.id == trackedTicket);
            var next = t != null ? NextStep(t) : null;
            string what = next == null ? "" : shift.Blocker(next) ?? Shift.Describe(next);
            Ui.Label(new Rect(p.x - 200, p.y + 22, 400, 50), $"{dist:0}m\n{what}", 15, Ui.Accent, TextAnchor.UpperCenter, true);
        }

        // ================================================================ tablet
        void DrawTablet()
        {
            var r = Ui.Center(1500, 860);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 30, r.y + 20, 600, 40), "TICKET QUEUE", 28, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.xMax - 630, r.y + 24, 600, 30), $"{shift.DoneCount}/{shift.tickets.Count} done   {Ui.Money(career.money)}   Rep {career.rep:0}      [Tab] close", 18, Ui.Dim, TextAnchor.UpperRight);
            var list = shift.tickets.OrderBy(t => t.Open ? 0 : 1).ThenBy(t => t.id).ToList();
            var lr = new Rect(r.x + 20, r.y + 80, 520, r.height - 190);
            Ui.Box(lr, new Color(0, 0, 0, 0.25f));
            float y = lr.y + 6;
            foreach (var t in list.Take(9))
            {
                var row = new Rect(lr.x + 6, y, lr.width - 12, 64);
                bool sel = t.id == selectedTicket;
                Ui.Box(row, sel ? new Color(0.15f, 0.25f, 0.35f, 0.9f) : Ui.Hover(row) ? new Color(0.12f, 0.16f, 0.2f, 0.9f) : new Color(0.08f, 0.1f, 0.12f, 0.9f));
                Ui.Box(new Rect(row.x, row.y, 5, row.height), Catalog.CustColor(t.customer));
                string status = t.status == TicketStatus.Done ? "DONE" : $"{t.DoneCount}/{t.tasks.Count}";
                Ui.Label(new Rect(row.x + 16, row.y + 6, 380, 26), $"#{t.id}  {Ticket.TypeName(t.type)}", 17, t.Open ? Ui.Text : Ui.Dim, TextAnchor.UpperLeft, true, false);
                Ui.Label(new Rect(row.x + 16, row.y + 34, 380, 24), $"{t.rack}  {t.customer}", 15, Ui.Dim, TextAnchor.UpperLeft, false, false);
                Ui.Label(new Rect(row.xMax - 130, row.y + 6, 118, 50), status, 19, t.status == TicketStatus.Done ? Ui.Good : Ui.Text, TextAnchor.MiddleRight, true);
                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)) { selectedTicket = t.id; Event.current.Use(); Audio.Play("click"); }
                y += 70;
            }
            if (list.Count > 9) Ui.Label(new Rect(lr.x + 12, lr.yMax - 26, lr.width - 24, 22), $"+{list.Count - 9} more below the fold (finish some to see them)", 14, Ui.Dim);
            DrawClockOut(new Rect(r.x + 20, r.yMax - 100, 520, 80));

            var sel2 = shift.tickets.Find(t => t.id == selectedTicket);
            var dr = new Rect(r.x + 560, r.y + 80, r.width - 580, r.height - 100);
            if (sel2 == null) return;
            Ui.Label(new Rect(dr.x, dr.y, dr.width, 60), sel2.title, 25, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(dr.x, dr.y + 66, dr.width, 26),
                $"{Ticket.TypeName(sel2.type)}   |   Rack {sel2.rack}   |   {sel2.customer}   |   Pays ~{Ui.Money(Mathf.RoundToInt(sel2.pay * career.PayMul))}" + (sel2.Open ? sel2.mistakes == 0 ? "  (+20% if clean)" : "  (clean bonus lost)" : ""),
                17, Ui.Accent);
            float bh = Ui.TextHeight(sel2.body, dr.width, 19);
            Ui.Label(new Rect(dr.x, dr.y + 106, dr.width, bh), sel2.body, 19, Ui.Text);
            float ty = dr.y + 126 + bh;
            Ui.Label(new Rect(dr.x, ty, 300, 26), "TASKS", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            ty += 30;
            foreach (var task in sel2.tasks)
            {
                string b = task.done ? null : shift.Blocker(task);
                Ui.Label(new Rect(dr.x, ty, 30, 26), task.done ? "[x]" : "[ ]", 18, task.done ? Ui.Good : Ui.Text, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(dr.x + 40, ty, 480, 26), Shift.Describe(task), 18, task.done ? Ui.Dim : Ui.Text, TextAnchor.UpperLeft, false, false);
                if (b != null) Ui.Label(new Rect(dr.x + 520, ty, dr.width - 520, 26), b, 16, Ui.Warn, TextAnchor.UpperLeft, false, false);
                ty += 32;
            }
            if (sel2.Open)
            {
                bool tracked = trackedTicket == sel2.id;
                if (Ui.Button(new Rect(dr.x, dr.yMax - 64, 300, 56), tracked ? "Tracking (waypoint on)" : "Track with waypoint", !tracked, 20)) trackedTicket = sel2.id;
            }
        }

        /// <summary>Ending the shift is the player's call. Open work needs a second click to confirm.</summary>
        void DrawClockOut(Rect r)
        {
            int open = shift.OpenTickets.Count();
            if (!confirmClockOut)
            {
                if (Ui.Button(r, open == 0 ? "Clock out: work order complete" : $"Clock out ({open} ticket{(open > 1 ? "s" : "")} unfinished)", true, 21, open == 0 ? Ui.Good : Ui.Warn)) ClockOut();
                return;
            }
            Ui.Box(new Rect(r.x, r.y - 70, r.width, r.height + 70), new Color(0.25f, 0.12f, 0.05f, 0.95f));
            Ui.Label(new Rect(r.x + 16, r.y - 62, r.width - 32, 56), $"{open} unfinished ticket{(open > 1 ? "s" : "")} will go to the night shift and cost about {open * 4} reputation. Clock out anyway?", 17, Ui.Warn);
            if (Ui.Button(new Rect(r.x + 10, r.y + 12, r.width / 2 - 20, r.height - 24), "Yes, clock out", true, 19, Ui.Bad)) ClockOut();
            if (Ui.Button(new Rect(r.x + r.width / 2 + 10, r.y + 12, r.width / 2 - 20, r.height - 24), "Keep working", true, 19, Ui.Good)) confirmClockOut = false;
        }

        // ================================================================ rack work panel
        void DrawRack()
        {
            var rack = shift.Rack(currentRack);
            if (rack == null) { screen = GameScreen.Play; return; }
            var r = Ui.Center(1460, 860);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 30, r.y + 20, 800, 44), $"RACK {rack.label}", 30, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.x + 260, r.y + 28, 600, 30), rack.owner ?? "vacant", 20, rack.owner != null ? Catalog.CustColor(rack.owner) : Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.xMax - 530, r.y + 26, 500, 30), "[E] / [Esc] step back", 18, Ui.Dim, TextAnchor.UpperRight);

            // Elevation
            const float uh = 17.6f;
            var el = new Rect(r.x + 30, r.y + 80, 560, uh * Rack.Units + 8);
            Ui.Box(el, new Color(0.02f, 0.025f, 0.03f));
            var tasks = shift.TasksAt(rack.label).ToList();
            for (int u = 1; u <= Rack.Units; u++)
            {
                float yy = el.yMax - 4 - u * uh;
                Ui.Label(new Rect(el.x + 4, yy, 36, uh), u.ToString(), 12, new Color(0.45f, 0.5f, 0.55f), TextAnchor.MiddleRight);
            }
            foreach (var d in rack.devices)
            {
                float yy = el.yMax - 4 - (d.Top) * uh;
                var dr = new Rect(el.x + 48, yy + 1, el.width - 56, d.Def.heightU * uh - 2);
                Ui.Box(dr, d.Def.color * 1.7f);
                Ui.Box(new Rect(dr.x, dr.y, 6, dr.height), Catalog.CustColor(d.owner));
                string flag = d.failedBay >= 0 ? "  DRIVE FAULT" : d.failedPsu >= 0 ? "  PSU FAULT" : d.fault != Fault.None ? "  UNREACHABLE" : "";
                Ui.Label(new Rect(dr.x + 14, dr.y, dr.width - 20, dr.height), $"{d.Def.name}  #{d.id}<color=#ff7a5c><b>{flag}</b></color>", 13, Ui.Text, TextAnchor.MiddleLeft, false, false);
                if (tasks.Any(x => !x.task.done && x.task.deviceId == d.id)) Ui.Border(dr, Ui.Warn, 2f);
            }
            foreach (var (ticket, task) in tasks)
            {
                if (task.kind != TaskKind.Mount || task.done) continue;
                var def = Catalog.Dev(task.def);
                float yy = el.yMax - 4 - (task.u + def.heightU - 1) * uh;
                var mr = new Rect(el.x + 48, yy + 1, el.width - 56, def.heightU * uh - 2);
                Ui.Box(mr, new Color(1f, 0.7f, 0.15f, 0.18f));
                Ui.Border(mr, Ui.Warn, 1.5f);
                Ui.Label(mr, $"install {def.name} (#{ticket.id})", 13, Ui.Warn, TextAnchor.MiddleCenter, false, false);
            }

            // Tasks at this rack
            var tr = new Rect(r.x + 620, r.y + 80, r.width - 650, 560);
            Ui.Label(new Rect(tr.x, tr.y, 400, 26), "WORK AT THIS RACK", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            float y = tr.y + 34;
            if (tasks.Count == 0) Ui.Label(new Rect(tr.x, y, tr.width, 60), "No open tickets for this rack. Check your tablet [Tab] for where you're needed.", 19, Ui.Dim);
            int lastTicket = -1;
            foreach (var (ticket, task) in tasks.Take(14))
            {
                if (ticket.id != lastTicket)
                {
                    lastTicket = ticket.id;
                    Ui.Label(new Rect(tr.x, y, tr.width, 26), $"#{ticket.id}  {ticket.title}", 16, Ui.Accent, TextAnchor.UpperLeft, true, false);
                    y += 30;
                }
                var row = new Rect(tr.x, y, tr.width, 50);
                Ui.Box(row, new Color(0.08f, 0.1f, 0.12f, 0.9f));
                Ui.Label(new Rect(row.x + 14, row.y, 440, 50), Shift.Describe(task), 18, task.done ? Ui.Dim : Ui.Text, TextAnchor.MiddleLeft, false, false);
                if (task.done) Ui.Label(new Rect(row.xMax - 170, row.y, 150, 50), "DONE", 18, Ui.Good, TextAnchor.MiddleRight, true);
                else
                {
                    string b = shift.Blocker(task);
                    if (b != null) Ui.Label(new Rect(row.x + 460, row.y, row.width - 480, 50), b, 15, Ui.Warn, TextAnchor.MiddleRight);
                    else if (Ui.Button(new Rect(row.xMax - 170, row.y + 6, 160, 38), "Start", true, 19, Ui.Good)) StartTask(task);
                }
                y += 56;
            }
            var inv = shift.inv;
            var ir = new Rect(tr.x, r.yMax - 180, tr.width, 150);
            Ui.Panel(ir, Ui.Bg2);
            Ui.Label(new Rect(ir.x + 16, ir.y + 12, 400, 24), "CARRYING", 15, Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(ir.x + 16, ir.y + 40, ir.width - 32, 120),
                (inv.hands.Count == 0 ? "Hands: empty" : "Hands: " + string.Join(", ", inv.hands.Select(h => h.tag != null ? $"{h.Def.name} ({h.tag})" : h.Def.name))) + "\n" +
                (inv.pouch.Count == 0 ? "Pouch: empty" : "Pouch: " + string.Join(", ", inv.pouch.Select(Catalog.PartName))), 18, Ui.Text);
        }

        // ================================================================ end of shift
        void DrawSummary()
        {
            Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(0, 0, 0, 0.7f));
            var r = Ui.Center(900, 700);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 50, r.y + 40, 800, 60), $"Shift over: Day {career.day - 1}", 44, Ui.Text, TextAnchor.UpperLeft, true);
            float repD = career.rep - shift.repStart;
            Ui.Label(new Rect(r.x + 52, r.y + 130, 800, 360),
                $"Tickets closed:  {shift.completed} of {shift.tickets.Count}\n" +
                $"Clean (no mistakes):  {shift.tickets.Count(t => t.status == TicketStatus.Done && t.mistakes == 0)}\n" +
                $"Handed to night shift:  {shift.handedOver}\n" +
                $"Mistakes:  {shift.mistakes}\n\n" +
                $"Earned today:  <color=#7fe08a><b>{Ui.Money(shift.earned)}</b></color>\n" +
                $"Reputation:  {career.rep:0}  ({(repD >= 0 ? "+" : "")}{repD:0})\n" +
                $"Time on the floor:  {Mathf.FloorToInt(shiftSeconds / 60)}m {Mathf.FloorToInt(shiftSeconds % 60):00}s",
                26, Ui.Text);
            string verdict = shift.handedOver == 0 && shift.mistakes == 0 ? "Spotless. The NOC loves you." : shift.handedOver == 0 ? "Everything closed. Nice work." : shift.handedOver > 2 ? "The night shift is not happy with you." : "Close. Try to finish the whole work order.";
            Ui.Label(new Rect(r.x + 52, r.y + 520, 800, 40), verdict, 22, Ui.Accent);
            if (Ui.Button(new Rect(r.x + 50, r.yMax - 100, 340, 60), "Visit the tech shop", true, 22, Ui.Good)) screen = GameScreen.Shop;
        }

        void DrawShop()
        {
            Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(0, 0, 0, 0.75f));
            var r = Ui.Center(1300, 860);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 40, r.y + 30, 800, 50), "TECH SHOP", 38, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.xMax - 440, r.y + 36, 400, 40), Ui.Money(career.money), 34, Ui.Good, TextAnchor.UpperRight, true);
            var ups = Catalog.Upgrades;
            for (int i = 0; i < ups.Length; i++)
            {
                var u = ups[i];
                var c = new Rect(r.x + 40 + (i % 2) * 615, r.y + 110 + (i / 2) * 160, 595, 145);
                bool owned = career.Has(u.id);
                Ui.Panel(c, Ui.Bg2);
                Ui.Label(new Rect(c.x + 20, c.y + 14, 400, 30), u.name, 23, Ui.Text, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(c.x + 20, c.y + 50, 380, 80), u.desc, 18, Ui.Dim);
                if (owned) Ui.Label(new Rect(c.xMax - 190, c.y + 50, 170, 50), "OWNED", 20, Ui.Good, TextAnchor.MiddleCenter, true);
                else if (Ui.Button(new Rect(c.xMax - 190, c.y + 50, 170, 50), Ui.Money(u.cost), career.money >= u.cost, 21, Ui.Good)) Buy(u);
            }
            if (Ui.Button(new Rect(r.x + 40, r.yMax - 90, 360, 60), $"Start day {career.day}", true, 24, Ui.Good)) NextDay();
            if (Ui.Button(new Rect(r.xMax - 300, r.yMax - 90, 260, 60), "Save & main menu", true, 20)) { career.Save(); ShowMenu(); }
        }

        void DrawFired()
        {
            Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(0.08f, 0, 0, 0.85f));
            var r = Ui.Center(900, 520);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 50, r.y + 50, 800, 60), "You've been let go", 46, Ui.Bad, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.x + 52, r.y + 140, 800, 220),
                $"Your reputation hit zero after {career.day - 1} day{(career.day - 1 == 1 ? "" : "s")}. Too many unfinished tickets and fumbles.\n\n" +
                $"Career: {career.ticketsDone} tickets closed, {Ui.Money(career.totalEarned)} earned.", 22, Ui.Text);
            if (Ui.Button(new Rect(r.x + 50, r.yMax - 100, 300, 60), "Main menu", true, 22)) ShowMenu();
        }
    }
}
