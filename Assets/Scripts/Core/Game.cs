using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RH
{
    public enum GameScreen { Menu, Briefing, Play, Tablet, Rack, Minigame, Pause, Summary, Shop, Fired }

    /// <summary>Bootstraps the game and runs the shift: input, screens, interactions and the clock.</summary>
    public partial class Game : MonoBehaviour
    {
        public GameScreen screen = GameScreen.Menu;
        public Career career = new Career();
        public Shift shift;
        public Facility facility;
        public Player player;
        public Minigame minigame;
        public string currentRack;
        public int selectedTicket, trackedTicket;
        GameScreen pausedFrom;

        public static float MinutesPerSecond = 1f;

        class ToastMsg { public string text; public int sev; public float time; }
        readonly List<ToastMsg> toasts = new List<ToastMsg>();

        void Awake()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 144;
            Player.Sensitivity = PlayerPrefs.GetFloat("sens", 2.2f);
            Audio.Volume = PlayerPrefs.GetFloat("vol", 0.8f);
            MinutesPerSecond = PlayerPrefs.GetFloat("pace", 1f);
            gameObject.AddComponent<Audio>();
            facility = new GameObject("Facility").AddComponent<Facility>();
            player = new GameObject("Player").AddComponent<Player>();
            player.Init();
            ShowMenu();
        }

        // ================================================================ flow
        public void ShowMenu()
        {
            screen = GameScreen.Menu;
            minigame = null;
            shift = new Shift(new Career { day = 3 }, 12345);
            facility.Build(shift);
            player.Teleport(new Vector3(11f, 0f, 3.2f), -40f, 8f);
        }

        public void NewCareer()
        {
            Career.Delete();
            career = new Career();
            career.Save();
            Briefing();
        }

        public void ContinueCareer()
        {
            career = Career.Load();
            Briefing();
        }

        void Briefing()
        {
            screen = GameScreen.Briefing;
            shift = new Shift(career, 7919 * career.day + 17);
            shift.TicketArrived += OnTicket;
            shift.Message += (t, s) => Toast(t, s);
            shift.TicketDone += t => { facility.RefreshAll(shift); if (trackedTicket == t.id) trackedTicket = 0; };
            facility.Build(shift);
            player.Teleport(Facility.Spawn, 15f);
            selectedTicket = trackedTicket = 0;
            toasts.Clear();
        }

        public void ClockIn() => screen = GameScreen.Play;

        void OnTicket(Ticket t)
        {
            Audio.Play("ticket");
            Toast($"New ticket #{t.id}: {t.title}", 0);
            if (trackedTicket == 0 || shift.tickets.Find(x => x.id == trackedTicket)?.Open != true) trackedTicket = t.id;
            if (selectedTicket == 0) selectedTicket = t.id;
            facility.Refresh(shift, t.rack);
        }

        void EndShift()
        {
            minigame = null;
            shift.End();
            if (shift.Fired)
            {
                Career.Delete();
                screen = GameScreen.Fired;
                return;
            }
            career.Save();
            screen = GameScreen.Summary;
        }

        public void Buy(UpgradeDef u)
        {
            if (career.Has(u.id) || career.money < u.cost) return;
            career.money -= u.cost;
            career.upgrades.Add(u.id);
            career.Save();
            Audio.Play("good");
        }

        public void NextDay() => Briefing();

        // ================================================================ frame
        void Update()
        {
            float dt = Time.deltaTime;
            bool working = screen == GameScreen.Play || screen == GameScreen.Tablet || screen == GameScreen.Rack || screen == GameScreen.Minigame;
            if (working && shift != null)
            {
                shift.Advance(dt * MinutesPerSecond);
                facility.SpawnDeliveries(shift);
                if (shift.Over && screen != GameScreen.Minigame) { EndShift(); return; }
            }
            HandleInput();
            if (screen == GameScreen.Minigame && minigame != null)
            {
                minigame.Tick(dt);
                if (minigame.finished)
                {
                    var t = minigame.task;
                    if (minigame.success) shift.Complete(t);
                    facility.Refresh(shift, t.rack);
                    minigame = null;
                    screen = shift.Over ? GameScreen.Play : GameScreen.Rack;
                }
            }
            player.inputEnabled = screen == GameScreen.Play;
            player.Tick(this, dt);
            if (screen == GameScreen.Menu)
                player.Teleport(new Vector3(11f, 0f, 3.2f), -40f + Mathf.Sin(Time.time * 0.08f) * 25f, 8f);
            facility.ShowMarker(working ? WaypointTarget() : null);
            bool locked = screen == GameScreen.Play;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            toasts.RemoveAll(t => Time.unscaledTime - t.time > 6f);
        }

        void HandleInput()
        {
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            switch (screen)
            {
                case GameScreen.Play:
                    if (esc) { pausedFrom = screen; screen = GameScreen.Pause; }
                    else if (Input.GetKeyDown(KeyCode.Tab)) OpenTablet();
                    else if (Input.GetKeyDown(KeyCode.E) && player.focus != null) player.focus.Use(this);
                    else if (Input.GetKeyDown(KeyCode.G)) Drop();
                    break;
                case GameScreen.Tablet:
                    if (esc || Input.GetKeyDown(KeyCode.Tab)) screen = GameScreen.Play;
                    break;
                case GameScreen.Rack:
                    if (esc || Input.GetKeyDown(KeyCode.E)) screen = GameScreen.Play;
                    else if (Input.GetKeyDown(KeyCode.Tab)) OpenTablet();
                    break;
                case GameScreen.Minigame:
                    if (esc) { minigame = null; screen = GameScreen.Rack; }
                    break;
                case GameScreen.Pause:
                    if (esc) screen = pausedFrom;
                    break;
            }
        }

        // ================================================================ interactions
        public void OpenTablet()
        {
            screen = GameScreen.Tablet;
            if (selectedTicket == 0 || shift.tickets.Find(t => t.id == selectedTicket) == null)
                selectedTicket = shift.OpenTickets.FirstOrDefault()?.id ?? 0;
        }

        public void OpenRack(string label)
        {
            currentRack = label;
            screen = GameScreen.Rack;
            Audio.Play("beep");
        }

        public void StartTask(WorkTask t)
        {
            if (shift.Blocker(t) != null) return;
            minigame = Minigame.For(t);
            minigame.Start(this, t);
            screen = GameScreen.Minigame;
        }

        public void TakeStock(StockShelf s)
        {
            var inv = shift.inv;
            if (s.isPart)
            {
                if (inv.PouchFull) { Toast($"Your tool pouch is full ({inv.pouchCap} parts).", 2); return; }
                inv.pouch.Add(s.part);
            }
            else
            {
                if (inv.HandsFull) { Toast("Your hands are full.", 2); return; }
                inv.hands.Add(new Item { def = s.def, label = Catalog.Dev(s.def).name });
            }
            Audio.Play("pickup");
            Toast($"Took a {s.ItemName}.", 0);
        }

        public void PickUp(Pickup p)
        {
            if (shift.inv.HandsFull) { Toast("Your hands are full.", 2); return; }
            shift.inv.hands.Add(p.item);
            Destroy(p.gameObject);
            Audio.Play("pickup");
        }

        public void ReturnItem()
        {
            var inv = shift.inv;
            if (inv.hands.Count > 0)
            {
                var it = inv.hands[inv.hands.Count - 1];
                if (it.tag != null) { Toast("That belongs to a customer. Install it, don't stock it!", 2); return; }
                inv.hands.RemoveAt(inv.hands.Count - 1);
                Toast($"Returned the {it.Def.name}.", 0);
            }
            else if (inv.pouch.Count > 0)
            {
                Toast($"Returned the {Catalog.PartName(inv.pouch[inv.pouch.Count - 1])}.", 0);
                inv.pouch.RemoveAt(inv.pouch.Count - 1);
            }
            Audio.Play("pickup");
        }

        void Drop()
        {
            var inv = shift.inv;
            if (inv.hands.Count == 0) return;
            var it = inv.hands[inv.hands.Count - 1];
            inv.hands.RemoveAt(inv.hands.Count - 1);
            var pos = player.transform.position + player.transform.forward * 0.9f;
            facility.SpawnItem(it, new Vector3(pos.x, 0f, pos.z), it.tag != null);
            Audio.Play("clunk");
        }

        public void Toast(string text, int sev)
        {
            toasts.Add(new ToastMsg { text = text, sev = sev, time = Time.unscaledTime });
            while (toasts.Count > 5) toasts.RemoveAt(0);
        }

        /// <summary>Where the tracked ticket needs the player next: the rack, the shelf or the dock.</summary>
        public Vector3? WaypointTarget()
        {
            var t = shift?.tickets.Find(x => x.id == trackedTicket && x.Open);
            if (t == null) return null;
            var next = NextStep(t);
            if (next == null) return facility.RackTop(t.rack);
            var blocker = shift.Blocker(next);
            if (blocker == null) return facility.RackTop(t.rack);
            if (next.kind == TaskKind.Mount)
            {
                if (next.tag != null)
                {
                    var p = FindObjectsByType<Pickup>().FirstOrDefault(x => x.item.tag == next.tag);
                    return p != null ? p.transform.position + Vector3.up * 0.5f : (Vector3?)null;
                }
                return facility.ShelfPos(next.def, null);
            }
            if (next.kind == TaskKind.SwapDrive) return facility.ShelfPos(null, PartKind.Drive);
            if (next.kind == TaskKind.SwapPsu) return facility.ShelfPos(null, PartKind.Psu);
            if (next.kind == TaskKind.SwapSfp) return facility.ShelfPos(null, PartKind.Sfp);
            return facility.RackTop(t.rack);
        }

        /// <summary>The first unfinished task, preferring ones that can be started now.</summary>
        public WorkTask NextStep(Ticket t) =>
            t.tasks.FirstOrDefault(x => !x.done && shift.Blocker(x) == null) ?? t.tasks.FirstOrDefault(x => !x.done);

        // ================================================================ screenshot mode (-shotdir <dir>)
        IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-shotdir");
            if (i < 0 || i + 1 >= args.Length) yield break;
            string dir = args[i + 1];
            Directory.CreateDirectory(dir);
            Career.PathOverride = Path.Combine(dir, "career.json");
            IEnumerator Shot(string name)
            {
                for (int k = 0; k < 8; k++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                yield return null;
                yield return null;
            }
            yield return Shot("00_menu");
            career = new Career { day = 3, money = 1200, rep = 62 };
            Briefing();
            yield return Shot("01_briefing");
            ClockIn();
            var tickets = new List<Ticket>();
            foreach (TicketType tt in System.Enum.GetValues(typeof(TicketType)))
            {
                var t = shift.SpawnTicket(tt);
                if (t != null) tickets.Add(t);
            }
            facility.SpawnDeliveries(shift);
            facility.RefreshAll(shift);
            player.Teleport(new Vector3(3.2f, 0f, 4.2f), 55f, 6f);
            yield return Shot("02_hall");
            var fw = tickets.FirstOrDefault(t => t.type == TicketType.Firmware) ?? tickets[0];
            var rack = shift.Rack(fw.rack);
            var rp = Facility.RackPos(rack.row, rack.col);
            player.Teleport(new Vector3(rp.x - 0.3f, 0f, Facility.RowFrontZ(rack.row) - 1.3f), 10f, 12f);
            trackedTicket = fw.id;
            yield return Shot("03_rack_front");
            player.Teleport(new Vector3(17.5f, 0f, 9f), 80f, 5f);
            yield return Shot("04_storage");
            player.Teleport(new Vector3(16f, 0f, 18.5f), 0f, 15f);
            yield return Shot("05_dock");
            selectedTicket = fw.id;
            OpenTablet();
            yield return Shot("06_tablet");
            OpenRack(fw.rack);
            yield return Shot("07_rack_panel");
            // Give the technician what each job needs, then open every minigame.
            shift.inv.handCap = 3;
            shift.inv.pouchCap = 6;
            shift.inv.pouch.AddRange(new[] { PartKind.Drive, PartKind.Psu, PartKind.Sfp });
            int n = 8;
            foreach (var t in tickets)
            {
                var task = t.tasks[0];
                if (task.kind == TaskKind.Mount) shift.inv.hands.Add(new Item { def = task.def, tag = task.tag, label = "test" });
                if (shift.Blocker(task) != null) continue;
                currentRack = task.rack;
                StartTask(task);
                yield return Shot($"{n++:00}_{task.kind}");
                if (task.kind == TaskKind.Diagnose)
                {
                    shift.Complete(task);
                    var follow = t.tasks[t.tasks.Count - 1];
                    if (shift.Blocker(follow) == null) { StartTask(follow); yield return Shot($"{n++:00}_{follow.kind}"); }
                }
                minigame = null;
            }
            // Second half of a rack-mount (the screws) and cabling after a completed mount.
            var mountTicket = tickets.FirstOrDefault(t => t.type == TicketType.Expand);
            if (mountTicket != null)
            {
                var mount = mountTicket.tasks[0];
                if (!shift.inv.Has(mount.def, mount.tag)) shift.inv.hands.Add(new Item { def = mount.def, tag = mount.tag, label = "test" });
                shift.Complete(mount);
                StartTask(mountTicket.tasks[1]);
                yield return Shot($"{n++:00}_Cable");
            }
            minigame = null;
            screen = GameScreen.Play;
            shift.minute = Shift.EndMin;
            yield return null;
            yield return null;
            yield return Shot($"{n++:00}_summary");
            screen = GameScreen.Shop;
            yield return Shot($"{n++:00}_shop");
            Application.Quit();
        }
    }
}
