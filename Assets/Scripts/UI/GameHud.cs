using System.Linq;
using UnityEngine;

namespace RH
{
    public partial class Game
    {
        /// <summary>Tablet page: 0 tickets, 1 customers, 2 career.</summary>
        public int tabletTab;

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
                case GameScreen.Settings: Ui.Shade(0.8f); DrawSettings(); break;
                case GameScreen.Summary: DrawSummary(); break;
                case GameScreen.Shop: DrawShop(); break;
                case GameScreen.Fired: DrawFired(); break;
            }
            if (photoFlash > 0) Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(1, 1, 1, photoFlash * 0.7f));
            DrawToasts();
        }

        static Color Sev(int s) => s == 1 ? Ui.Good : s == 2 ? Ui.Warn : s == 3 ? Ui.Bad : Ui.Accent;
        static Color PriColor(int p) => p == 1 ? Ui.Bad : p == 2 ? Ui.Warn : Ui.Dim;
        static string K(Keys.Action a) => Keys.Label(a);

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
            float y = 470;
            if (Career.Exists)
            {
                var c = Career.Load();
                string what = ShiftSave.Exists ? $"Resume shift: Day {c.day}" : $"Continue: Day {c.day}";
                if (Ui.Button(new Rect(90, y, 420, 64), $"{what}  ({Ui.Money(c.money)})", true, 22)) ContinueCareer();
                y += 80;
            }
            if (Ui.Button(new Rect(90, y, 420, 64), Career.Exists ? "New career (erases save)" : "Start career", true, 24)) NewCareer();
            y += 80;
            if (Ui.Button(new Rect(90, y, 420, 64), "Settings", true, 24)) OpenSettings();
            y += 80;
            if (Ui.Button(new Rect(90, y, 420, 64), "Quit", true, 24)) Application.Quit();
            Ui.Label(new Rect(94, Ui.H - 60, 600, 30), "A spin-off from Uptime: Data Center Tycoon", 16, Ui.Dim);
        }

        void DrawBriefing()
        {
            Ui.Shade(0.7f);
            var r = Ui.Center(1040, 820);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 50, r.y + 40, 900, 70), $"DAY {career.day}", 56, Ui.Text, TextAnchor.UpperLeft, true);
            int p1 = shift.tickets.Count(t => t.priority == 1);
            Ui.Label(new Rect(r.x + 52, r.y + 110, 940, 30), $"Today's work order: {shift.tickets.Count} tickets" + (p1 > 0 ? $" ({p1} P1)" : "") + "   Data hall 1", 22, Ui.Accent);
            Ui.Label(new Rect(r.x + 52, r.y + 165, 440, 200),
                $"{career.Title} (level {career.Level})\nWallet: {Ui.Money(career.money)}\nReputation: {career.rep:0}/100\nTickets closed: {career.ticketsDone}\nBest shift: {Ui.Money(career.bestDay)}", 21, Ui.Text);
            string news = career.day switch
            {
                1 => "First day! Expect firmware updates, failed hardware and capacity expansions.",
                2 => "New today: P1 incidents (servers down), customer deliveries at the loading dock, and smart-hands requests.",
                3 => "New today: full rack build-outs for brand-new contracts. Some incidents need the crash cart.",
                4 => "New today: decommissioning. Old gear comes out, gets wiped at the bench and ships from the dock.",
                _ => "The site keeps growing, and so does the work order.",
            };
            if (shift.news != null) news += "\n\n" + shift.news;
            Ui.Label(new Rect(r.x + 520, r.y + 165, 470, 220), "<b>Dispatch notes</b>\n" + news, 19, Ui.Dim);
            Ui.Label(new Rect(r.x + 52, r.y + 400, 940, 280),
                "<b>How it works</b>\n" +
                $"- Today's tickets are on your tablet [{K(Keys.Action.Tablet)}]. Each one names a rack and the hands-on jobs to do there.\n" +
                "- Grab servers and spare parts in the storage room, customer gear at the loading dock.\n" +
                $"- Walk up to the rack and press [{K(Keys.Action.Interact)}]. Power, cabling and optics are done from the REAR (the hot aisle).\n" +
                "- P1 tickets pay 25% more if you close them before any lower-priority ticket.\n" +
                "- There's no clock. Clock out from the tablet when you're done; unfinished tickets cost reputation.\n" +
                "- Mistake-free tickets pay 20% more. Breaking something opens a follow-up ticket you have to fix.",
                18, Ui.Text);
            if (Ui.Button(new Rect(r.x + 50, r.yMax - 100, 300, 60), "Clock in", true, 24, Ui.Good)) ClockIn();
            if (Ui.Button(new Rect(r.xMax - 250, r.yMax - 100, 200, 60), "Main menu", true, 20)) ShowMenu();
        }

        void DrawPause()
        {
            Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(0, 0, 0, 0.6f));
            var r = Ui.Center(560, 420);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x, r.y + 30, r.width, 50), "Paused", 40, Ui.Text, TextAnchor.UpperCenter, true);
            float y = r.y + 120;
            if (Ui.Button(new Rect(r.x + 50, y, 460, 56), "Resume", true, 22, Ui.Good)) ClosePause();
            if (Ui.Button(new Rect(r.x + 50, y + 70, 460, 56), "Settings", true, 22)) OpenSettings();
            if (Ui.Button(new Rect(r.x + 50, y + 140, 460, 56), "Save and quit to menu", true, 20, Ui.Warn))
            {
                SaveMidShift();
                ShowMenu();
            }
            Ui.Label(new Rect(r.x + 50, y + 210, 460, 40), "Your shift is saved: pick it up from the main menu.", 16, Ui.Dim, TextAnchor.UpperCenter);
        }

        // ================================================================ HUD
        void DrawHud(bool full = true)
        {
            if (shift == null) return;
            if (full && cameraUp) { DrawViewfinder(); return; }
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
            Ui.Label(new Rect(tr.x + 18, tr.y + 58, tr.width - 36, 24), career.Title, 15, Ui.Dim, TextAnchor.UpperRight);
            Ui.Bar(new Rect(tr.x + 18, tr.y + 84, tr.width - 36, 10), career.rep / 100f, career.rep > 60 ? Ui.Good : career.rep > 30 ? Ui.Warn : Ui.Bad);

            // Ticket strip, most urgent first
            float y = 150;
            var open = shift.OpenTickets.OrderBy(t => t.priority).ThenBy(t => t.id).ToList();
            foreach (var t in open.Take(6))
            {
                var r = new Rect(20, y, 420, 62);
                Ui.Box(r, new Color(0.05f, 0.07f, 0.09f, 0.82f));
                Ui.Box(new Rect(r.x, r.y, 5, r.height), Catalog.CustColor(t.customer));
                if (t.id == trackedTicket) Ui.Border(r, Ui.Accent, 2f);
                Ui.Label(new Rect(r.x + 16, r.y + 6, 40, 24), t.PriorityLabel, 16, PriColor(t.priority), TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(r.x + 52, r.y + 6, 300, 24), $"#{t.id}  {t.TypeLabel}  {t.rack}", 16, Ui.Text, TextAnchor.UpperLeft, true, false);
                Ui.Label(new Rect(r.x + 16, r.y + 32, 300, 24), $"{t.DoneCount}/{t.tasks.Count} tasks", 15, Ui.Dim);
                Ui.Bar(new Rect(r.x + 290, r.y + 26, 112, 10), t.DoneCount / (float)t.tasks.Count, Ui.Accent);
                y += 70;
            }
            if (shift.AllDone)
            {
                var done = new Rect(20, y, 420, 70);
                Ui.Box(done, new Color(0.05f, 0.2f, 0.1f, 0.88f));
                Ui.Border(done, Ui.Good, 2f);
                Ui.Label(done, $"Work order complete!\nOpen your tablet [{K(Keys.Action.Tablet)}] to clock out.", 18, Ui.Good, TextAnchor.MiddleCenter, true);
            }
            else if (open.Count > 6) Ui.Label(new Rect(24, y, 420, 30), $"+{open.Count - 6} more on your tablet [{K(Keys.Action.Tablet)}]", 16, Ui.Dim);

            if (!full) return;
            // Crosshair and interaction prompt
            var c = new Vector2(Ui.W / 2, Ui.H / 2);
            Ui.Circle(c, player.focus != null ? 5f : 3f, player.focus != null ? Ui.Accent : new Color(1, 1, 1, 0.8f));
            if (player.focus != null)
            {
                string prompt = player.focus.Prompt(this);
                float w = Mathf.Min(1100, prompt.Length * 11f + 60);
                var pr = new Rect(c.x - w / 2, c.y + 40, w, 44);
                Ui.Box(pr, new Color(0.05f, 0.07f, 0.09f, 0.85f));
                Ui.Label(pr, prompt, 20, Ui.Text, TextAnchor.MiddleCenter, false, false);
            }
            DrawWaypoint();
            DrawCarried();
            DrawTutorial();
            Ui.Label(new Rect(Ui.W - 1020, Ui.H - 40, 1000, 30),
                $"{K(Keys.Action.Forward)}{K(Keys.Action.Left)}{K(Keys.Action.Back)}{K(Keys.Action.Right)} move   {K(Keys.Action.Sprint)} sprint   {K(Keys.Action.Interact)} interact   {K(Keys.Action.Tablet)} tickets   {K(Keys.Action.Camera)} camera   {K(Keys.Action.Drop)} drop   Esc pause",
                15, Ui.Dim, TextAnchor.MiddleRight);
        }

        void DrawViewfinder()
        {
            float w = Ui.W, h = Ui.H;
            var col = new Color(1, 1, 1, 0.85f);
            float m = 60, len = 70;
            foreach (var (x, y, dx, dy) in new[] { (m, m, 1, 1), (w - m, m, -1, 1), (m, h - m, 1, -1), (w - m, h - m, -1, -1) })
            {
                Ui.Box(new Rect(dx > 0 ? x : x - len, y - (dy > 0 ? 0 : 3), len, 3), col);
                Ui.Box(new Rect(x - (dx > 0 ? 0 : 3), dy > 0 ? y : y - len, 3, len), col);
            }
            Ui.Ring(new Vector2(w / 2, h / 2), 18f, col);
            Ui.Box(new Rect(w / 2 - 70, 70, 14, 14), new Color(1f, 0.2f, 0.2f, Mathf.Repeat(Time.time, 1f) < 0.5f ? 1f : 0.3f));
            Ui.Label(new Rect(w / 2 - 50, 64, 300, 26), "REC  smart-hands camera", 18, col, TextAnchor.MiddleLeft, true);
            var want = shift.OpenTickets.SelectMany(k => k.tasks.Where(x => x.kind == TaskKind.Photo && !x.done)).Select(x => $"{x.component} of {x.rack}").ToList();
            string need = want.Count > 0 ? "Wanted: " + string.Join(", ", want) : "No ticket needs a photo right now";
            Ui.Box(new Rect(w / 2 - 520, h - 128, 1040, 76), new Color(0, 0, 0, 0.55f));
            Ui.Label(new Rect(0, h - 120, w, 30), need, 20, col, TextAnchor.MiddleCenter, true);
            Ui.Label(new Rect(0, h - 88, w, 30), $"Click: take photo     [{K(Keys.Action.Camera)}] or Esc: lower camera     Frame the whole rack, top to bottom", 17, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleCenter);
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
            string what = next == null ? "" : shift.Blocker(next) ?? Shift.Describe(next) + (next.station == null && next.kind != TaskKind.Photo && Shift.RearJob(next.kind) ? " (rear)" : "");
            Ui.Label(new Rect(p.x - 220, p.y + 22, 440, 50), $"{dist:0}m\n{what}", 15, Ui.Accent, TextAnchor.UpperCenter, true);
        }

        // ================================================================ tablet
        void DrawTablet()
        {
            var r = Ui.Center(1500, 860);
            Ui.Panel(r);
            string[] tabs = { "TICKETS", "CUSTOMERS", "CAREER" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var tr = new Rect(r.x + 30 + i * 190, r.y + 18, 180, 44);
                bool on = tabletTab == i;
                Ui.Box(tr, on ? new Color(0.15f, 0.25f, 0.35f, 0.95f) : Ui.Hover(tr) ? new Color(0.12f, 0.16f, 0.2f) : new Color(0.08f, 0.1f, 0.12f));
                Ui.Label(tr, tabs[i], 18, on ? Ui.Text : Ui.Dim, TextAnchor.MiddleCenter, true);
                if (Event.current.type == EventType.MouseDown && tr.Contains(Event.current.mousePosition)) { tabletTab = i; Event.current.Use(); Audio.Play("click"); }
            }
            Ui.Label(new Rect(r.xMax - 630, r.y + 24, 600, 30), $"{shift.DoneCount}/{shift.tickets.Count} done   {Ui.Money(career.money)}   Rep {career.rep:0}      [{K(Keys.Action.Tablet)}] close", 18, Ui.Dim, TextAnchor.UpperRight);
            if (tabletTab == 1) DrawCustomers(r);
            else if (tabletTab == 2) DrawCareer(r);
            else DrawTickets(r);
        }

        void DrawTickets(Rect r)
        {
            var list = shift.tickets.OrderBy(t => t.Open ? 0 : 1).ThenBy(t => t.priority).ThenBy(t => t.id).ToList();
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
                Ui.Label(new Rect(row.x + 16, row.y + 6, 40, 26), t.PriorityLabel, 17, t.Open ? PriColor(t.priority) : Ui.Dim, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(row.x + 56, row.y + 6, 340, 26), $"#{t.id}  {t.TypeLabel}", 17, t.Open ? Ui.Text : Ui.Dim, TextAnchor.UpperLeft, true, false);
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
            float mul = career.PayMul * career.CustMul(sel2.customer);
            string pays = sel2.followUp ? "Pays nothing (your mistake)" : $"Pays ~{Ui.Money(Mathf.RoundToInt(sel2.pay * mul))}";
            string bonus = !sel2.Open || sel2.followUp ? "" : (sel2.mistakes == 0 ? "  (+20% if clean)" : "  (clean bonus lost)") + (sel2.priority == 1 ? sel2.neglected ? "  (rush bonus lost)" : "  (+25% if closed first)" : "");
            Ui.Label(new Rect(dr.x, dr.y + 66, dr.width, 26),
                $"{sel2.PriorityLabel} {sel2.TypeLabel}   |   Rack {sel2.rack}   |   {sel2.customer}   |   {pays}{bonus}", 17, Ui.Accent);
            float bh = Ui.TextHeight(sel2.body, dr.width, 19);
            Ui.Label(new Rect(dr.x, dr.y + 106, dr.width, bh), sel2.body, 19, Ui.Text);
            float ty = dr.y + 126 + bh;
            Ui.Label(new Rect(dr.x, ty, 300, 26), "TASKS", 16, Ui.Dim, TextAnchor.UpperLeft, true);
            ty += 30;
            foreach (var task in sel2.tasks)
            {
                string b = task.done ? null : shift.Blocker(task);
                string where = task.station == "bench" ? "  [bench]" : task.station == "dock" ? "  [dock]" : task.station == "mmr" ? "  [meet-me room]" : task.kind == TaskKind.Photo ? "  [camera]" : Shift.RearJob(task.kind) ? "  [rear]" : "";
                Ui.Label(new Rect(dr.x, ty, 30, 26), task.done ? "[x]" : "[ ]", 18, task.done ? Ui.Good : Ui.Text, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(dr.x + 40, ty, 520, 26), Shift.Describe(task) + $"<color=#8fa3b0>{where}</color>", 18, task.done ? Ui.Dim : Ui.Text, TextAnchor.UpperLeft, false, false);
                if (b != null) Ui.Label(new Rect(dr.x + 570, ty, dr.width - 570, 26), b, 16, Ui.Warn, TextAnchor.UpperLeft, false, false);
                ty += 32;
            }
            if (sel2.Open)
            {
                bool tracked = trackedTicket == sel2.id;
                if (Ui.Button(new Rect(dr.x, dr.yMax - 64, 300, 56), tracked ? "Tracking (waypoint on)" : "Track with waypoint", !tracked, 20)) trackedTicket = sel2.id;
            }
        }

        void DrawCustomers(Rect r)
        {
            Ui.Label(new Rect(r.x + 30, r.y + 80, 1000, 30), "How each customer feels about your work. Happy customers tip (up to +15%), unhappy ones haggle (down to -10%).", 18, Ui.Dim);
            var custs = Catalog.Customers.OrderByDescending(c => career.Sat(c.name)).ToList();
            for (int i = 0; i < custs.Count; i++)
            {
                var c = custs[i];
                var row = new Rect(r.x + 30 + (i % 2) * 720, r.y + 130 + (i / 2) * 120, 700, 104);
                Ui.Panel(row, Ui.Bg2);
                Ui.Box(new Rect(row.x, row.y, 6, row.height), c.color);
                float sat = career.Sat(c.name);
                int racks = shift.racks.Count(x => x.owner == c.name);
                int units = shift.racks.Where(x => x.owner == c.name).Sum(x => x.devices.Count);
                Ui.Label(new Rect(row.x + 22, row.y + 10, 400, 30), c.name, 22, Ui.Text, TextAnchor.UpperLeft, true);
                Ui.Label(new Rect(row.x + 22, row.y + 42, 400, 24), $"{racks} rack{(racks == 1 ? "" : "s")}, {units} units", 16, Ui.Dim);
                string mood = sat >= 80 ? "Delighted" : sat >= 60 ? "Happy" : sat >= 40 ? "Neutral" : sat >= 20 ? "Unhappy" : "Furious";
                Color mc = sat >= 60 ? Ui.Good : sat >= 40 ? Ui.Text : sat >= 20 ? Ui.Warn : Ui.Bad;
                Ui.Label(new Rect(row.xMax - 300, row.y + 10, 280, 30), $"{mood}  ({sat:0})", 20, mc, TextAnchor.UpperRight, true);
                Ui.Bar(new Rect(row.x + 22, row.y + 74, row.width - 44, 12), sat / 100f, mc);
                Ui.Label(new Rect(row.xMax - 300, row.y + 42, 280, 24), $"pay x{career.CustMul(c.name):0.00}", 16, Ui.Dim, TextAnchor.UpperRight);
            }
        }

        void DrawCareer(Rect r)
        {
            var a = new Rect(r.x + 30, r.y + 90, 700, 640);
            Ui.Label(new Rect(a.x, a.y, 700, 50), career.Title, 40, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(a.x, a.y + 56, 700, 30), $"Level {career.Level}   {career.xp} XP", 22, Ui.Accent);
            int next = career.NextLevelXp;
            if (next > 0)
            {
                int prev = Catalog.LevelXp[career.Level - 1];
                Ui.Bar(new Rect(a.x, a.y + 100, 600, 16), (career.xp - prev) / (float)(next - prev), Ui.Accent);
                Ui.Label(new Rect(a.x, a.y + 122, 600, 24), $"{next - career.xp} XP to {Catalog.LevelTitles[career.Level]}", 17, Ui.Dim);
            }
            else Ui.Label(new Rect(a.x, a.y + 100, 600, 24), "Top of the ladder.", 17, Ui.Good);
            Ui.Label(new Rect(a.x, a.y + 170, 680, 300),
                "<b>How you earn XP</b>\n- 10 per ticket, +5 if it's clean, +5 for a P1 closed first\n- +5 more for build-outs and decommissions\n\n" +
                "<b>What levels give you</b>\n- +4% pay per level\n- Better tools and certifications in the tech shop\n\n" +
                $"<b>Pay rate now</b>: x{career.PayMul:0.00} (reputation and level)", 18, Ui.Text);
            var b = new Rect(r.x + 780, r.y + 90, 690, 640);
            Ui.Label(new Rect(b.x, b.y, 600, 30), "CERTIFICATIONS AND TOOLS", 18, Ui.Dim, TextAnchor.UpperLeft, true);
            float y = b.y + 40;
            foreach (var u in Catalog.Upgrades.Where(x => career.Has(x.id)))
            {
                Ui.Label(new Rect(b.x, y, 680, 26), (u.cert ? "<color=#ffb733>CERT</color>  " : "") + u.name, 19, Ui.Text, TextAnchor.UpperLeft, true);
                y += 30;
            }
            if (!career.upgrades.Any()) Ui.Label(new Rect(b.x, y, 680, 30), "Nothing yet. The tech shop opens after every shift.", 18, Ui.Dim);
            Ui.Label(new Rect(b.x, b.yMax - 90, 680, 80), $"Career: {career.ticketsDone} tickets closed, {career.handedOver} handed to the night shift, {Ui.Money(career.totalEarned)} earned.", 17, Ui.Dim);
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

        // ================================================================ rack / station work panel
        void DrawRack()
        {
            if (currentStation != null) { DrawStation(); return; }
            var rack = shift.Rack(currentRack);
            if (rack == null) { screen = GameScreen.Play; return; }
            var r = Ui.Center(1460, 860);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 30, r.y + 20, 800, 44), $"RACK {rack.label}", 30, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.x + 230, r.y + 28, 160, 30), rearSide ? "REAR" : "FRONT", 20, rearSide ? new Color(1f, 0.6f, 0.4f) : Ui.Accent, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.x + 340, r.y + 28, 600, 30), rack.owner ?? "vacant", 20, rack.owner != null ? Catalog.CustColor(rack.owner) : Ui.Dim, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.xMax - 530, r.y + 26, 500, 30), $"[{K(Keys.Action.Interact)}] / [Esc] step back", 18, Ui.Dim, TextAnchor.UpperRight);

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
                string flag = d.fault == Fault.NoPower ? "  NO POWER" : d.fault == Fault.ArrayCritical ? "  ARRAY CRITICAL" : d.failedBay >= 0 ? "  DRIVE FAULT" : d.failedPsu >= 0 ? "  PSU FAULT" : d.fault != Fault.None ? "  UNREACHABLE" : "";
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
            DrawTaskList(tr, tasks, "No open tickets for this rack. Check your tablet for where you're needed.");
            DrawCarrying(new Rect(tr.x, r.yMax - 180, tr.width, 150));
        }

        void DrawStation()
        {
            string title = currentStation switch { "bench" => "WIPE & SHRED STATION", "dock" => "OUTBOUND PALLET", _ => "MEET-ME ROOM" };
            string about = currentStation switch
            {
                "bench" => "Decommissioned units get their drives pulled, logged and shredded here before they leave the building. Bring the unit with you.",
                "dock" => "Wiped, decommissioned units wait here for the freight company. Bring the unit with you.",
                _ => "Every carrier's panel and every customer cage's uplinks meet on this frame. Cross-connects are patched here.",
            };
            var r = Ui.Center(1100, 760);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 30, r.y + 20, 800, 44), title, 30, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.xMax - 530, r.y + 26, 500, 30), $"[{K(Keys.Action.Interact)}] / [Esc] step back", 18, Ui.Dim, TextAnchor.UpperRight);
            Ui.Label(new Rect(r.x + 30, r.y + 74, r.width - 60, 50), about, 18, Ui.Dim);
            var tr = new Rect(r.x + 30, r.y + 140, r.width - 60, 420);
            DrawTaskList(tr, shift.TasksAtStation(currentStation).ToList(), "Nothing to do here right now.");
            DrawCarrying(new Rect(r.x + 30, r.yMax - 180, r.width - 60, 150));
        }

        void DrawTaskList(Rect tr, System.Collections.Generic.List<(Ticket ticket, WorkTask task)> tasks, string empty)
        {
            float y = tr.y + 34;
            if (tasks.Count == 0) Ui.Label(new Rect(tr.x, y, tr.width, 60), empty, 19, Ui.Dim);
            int lastTicket = -1;
            foreach (var (ticket, task) in tasks.Take(14))
            {
                if (ticket.id != lastTicket)
                {
                    lastTicket = ticket.id;
                    Ui.Label(new Rect(tr.x, y, tr.width, 26), $"{ticket.PriorityLabel}  #{ticket.id}  {ticket.title}", 16, ticket.priority == 1 ? Ui.Bad : Ui.Accent, TextAnchor.UpperLeft, true, false);
                    y += 30;
                }
                var row = new Rect(tr.x, y, tr.width, 50);
                Ui.Box(row, new Color(0.08f, 0.1f, 0.12f, 0.9f));
                Ui.Label(new Rect(row.x + 14, row.y, 440, 50), Shift.Describe(task), 18, task.done ? Ui.Dim : Ui.Text, TextAnchor.MiddleLeft, false, false);
                if (task.done) Ui.Label(new Rect(row.xMax - 170, row.y, 150, 50), "DONE", 18, Ui.Good, TextAnchor.MiddleRight, true);
                else
                {
                    string b = shift.Blocker(task) ?? SideBlocker(task);
                    if (b == null && task.kind == TaskKind.Photo) b = $"Use the camera [{K(Keys.Action.Camera)}]";
                    if (b != null) Ui.Label(new Rect(row.x + 460, row.y, row.width - 480, 50), b, 15, Ui.Warn, TextAnchor.MiddleRight);
                    else if (Ui.Button(new Rect(row.xMax - 170, row.y + 6, 160, 38), task.kind == TaskKind.Ship ? "Load" : "Start", true, 19, Ui.Good)) StartTask(task);
                }
                y += 56;
            }
        }

        void DrawCarrying(Rect ir)
        {
            var inv = shift.inv;
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
            var r = Ui.Center(900, 760);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 50, r.y + 40, 800, 60), $"Shift over: Day {career.day - 1}", 44, Ui.Text, TextAnchor.UpperLeft, true);
            float repD = career.rep - shift.repStart;
            int xpD = career.xp - shift.xpStart;
            Ui.Label(new Rect(r.x + 52, r.y + 125, 800, 420),
                $"Tickets closed:  {shift.completed} of {shift.tickets.Count}\n" +
                $"Clean (no mistakes):  {shift.tickets.Count(t => t.status == TicketStatus.Done && t.mistakes == 0 && !t.followUp)}\n" +
                $"P1s closed first:  {shift.tickets.Count(t => t.status == TicketStatus.Done && t.RushBonus)} of {shift.tickets.Count(t => t.priority == 1 && !t.followUp)}\n" +
                $"Follow-ups you caused:  {shift.tickets.Count(t => t.followUp)}\n" +
                $"Handed to night shift:  {shift.handedOver}\n" +
                $"Mistakes:  {shift.mistakes}\n\n" +
                $"Earned today:  <color=#7fe08a><b>{Ui.Money(shift.earned)}</b></color>\n" +
                $"Reputation:  {career.rep:0}  ({(repD >= 0 ? "+" : "")}{repD:0})\n" +
                $"Experience:  +{xpD} XP  ({career.Title})\n" +
                $"Time on the floor:  {Mathf.FloorToInt(shiftSeconds / 60)}m {Mathf.FloorToInt(shiftSeconds % 60):00}s",
                24, Ui.Text);
            string verdict = shift.handedOver == 0 && shift.mistakes == 0 ? "Spotless. The NOC loves you." : shift.handedOver == 0 ? "Everything closed. Nice work." : shift.handedOver > 2 ? "The night shift is not happy with you." : "Close. Try to finish the whole work order.";
            Ui.Label(new Rect(r.x + 52, r.y + 575, 800, 40), verdict, 22, Ui.Accent);
            if (Ui.Button(new Rect(r.x + 50, r.yMax - 100, 340, 60), "Visit the tech shop", true, 22, Ui.Good)) screen = GameScreen.Shop;
        }

        void DrawShop()
        {
            Ui.Box(new Rect(0, 0, Ui.W, Ui.H), new Color(0, 0, 0, 0.75f));
            var r = Ui.Center(1500, 900);
            Ui.Panel(r);
            Ui.Label(new Rect(r.x + 40, r.y + 30, 800, 50), "TECH SHOP", 38, Ui.Text, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.x + 320, r.y + 44, 600, 30), $"{career.Title}, level {career.Level}", 20, Ui.Accent);
            Ui.Label(new Rect(r.xMax - 440, r.y + 36, 400, 40), Ui.Money(career.money), 34, Ui.Good, TextAnchor.UpperRight, true);
            // Show each upgrade line once: the tier you own (or the next tier you can buy).
            var shown = Catalog.Upgrades.Where(u => u.requires == null || Catalog.Upgrade(u.requires).cert || career.Has(u.requires))
                .Where(u => !Catalog.Upgrades.Any(n => n.requires == u.id && !n.cert && !Catalog.Upgrade(n.requires).cert && career.Has(u.id)))
                .ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var u = shown[i];
                var c = new Rect(r.x + 40 + (i % 3) * 480, r.y + 105 + (i / 3) * 140, 460, 128);
                bool owned = career.Has(u.id);
                Ui.Panel(c, u.cert ? new Color(0.16f, 0.13f, 0.08f, 0.95f) : Ui.Bg2);
                Ui.Label(new Rect(c.x + 18, c.y + 12, 280, 28), u.name, 20, Ui.Text, TextAnchor.UpperLeft, true, false);
                if (u.cert) Ui.Label(new Rect(c.x + 300, c.y + 14, 140, 24), "CERTIFICATION", 13, Ui.Warn, TextAnchor.UpperRight, true);
                Ui.Label(new Rect(c.x + 18, c.y + 44, 260, 80), u.desc, 15, Ui.Dim);
                var br = new Rect(c.xMax - 160, c.y + 56, 145, 46);
                if (owned) Ui.Label(br, "OWNED", 19, Ui.Good, TextAnchor.MiddleCenter, true);
                else if (career.Level < u.level) Ui.Label(br, $"Needs level {u.level}\n({Catalog.LevelTitles[u.level - 1]})", 14, Ui.Warn, TextAnchor.MiddleCenter, true);
                else if (u.requires != null && !career.Has(u.requires)) Ui.Label(br, $"Needs\n{Catalog.Upgrade(u.requires).name}", 14, Ui.Warn, TextAnchor.MiddleCenter, true);
                else if (Ui.Button(br, Ui.Money(u.cost), career.money >= u.cost, 20, Ui.Good)) Buy(u);
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
                $"Your reputation hit zero on day {career.day - 1}. Too many unfinished tickets and fumbles.\n\n" +
                $"Career: {career.Title}, {career.ticketsDone} tickets closed, {Ui.Money(career.totalEarned)} earned.", 22, Ui.Text);
            if (Ui.Button(new Rect(r.x + 50, r.yMax - 100, 300, 60), "Main menu", true, 22)) ShowMenu();
        }
    }
}
