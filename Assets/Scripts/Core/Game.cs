using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RH
{
    public enum GameScreen { Menu, Briefing, Play, Tablet, Rack, Minigame, Pause, Settings, Summary, Shop, Fired }

    /// <summary>Bootstraps the game and runs the shift: input, screens, interactions and saving.</summary>
    public partial class Game : MonoBehaviour
    {
        public GameScreen screen = GameScreen.Menu;
        public Career career = new Career();
        public Shift shift;
        public Facility facility;
        public Player player;
        public Minigame minigame;
        /// <summary>The rack panel being shown, or null when the panel is for a station (bench, dock, meet-me room).</summary>
        public string currentRack, currentStation, currentStationTitle;
        /// <summary>Whether the player opened the rack from the hot aisle behind it.</summary>
        public bool rearSide;
        public int selectedTicket, trackedTicket;
        GameScreen pausedFrom, settingsFrom;
        /// <summary>Set when the player asked to clock out with work still open, to show the confirmation.</summary>
        public bool confirmClockOut;
        /// <summary>Real time spent on the floor this shift (just for the summary; nothing is timed).</summary>
        public float shiftSeconds;
        /// <summary>The smart-hands camera is raised: clicking takes a photo.</summary>
        public bool cameraUp;
        float photoFlash;
        /// <summary>Screenshot mode: jump the work camera into place instead of gliding.</summary>
        bool snapCamera;

        class ToastMsg { public string text; public int sev; public float time; }
        readonly List<ToastMsg> toasts = new List<ToastMsg>();

        void Awake()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 144;
            Player.Sensitivity = PlayerPrefs.GetFloat("sens", 2.2f);
            Player.Fov = PlayerPrefs.GetFloat("fov", 72f);
            Audio.Volume = PlayerPrefs.GetFloat("vol", 0.8f);
            if (PlayerPrefs.HasKey("quality")) QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt("quality"), 0, QualitySettings.names.Length - 1), true);
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
            CloseMinigame();
            cameraUp = false;
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
            var save = ShiftSave.Load();
            if (save != null && save.tickets.Count > 0) Resume(save);
            else Briefing();
        }

        void ResetShiftUi()
        {
            confirmClockOut = false;
            cameraUp = false;
            CloseMinigame();
            toasts.Clear();
            TutorialReset();
            ClearVisitor();
        }

        void Hook()
        {
            shift.Message += (t, s) => Toast(t, s);
            shift.TicketDone += t =>
            {
                facility.RefreshAll(shift);
                if (trackedTicket == t.id) trackedTicket = shift.OpenTickets.OrderBy(x => x.priority).FirstOrDefault()?.id ?? 0;
                if (shift.AllDone) Audio.Play("ticket");
                SaveMidShift();
            };
            shift.TicketArrived += t =>
            {
                facility.RefreshAll(shift);
                Toast($"New {t.PriorityLabel} ticket #{t.id}: {t.title}", t.priority == 1 ? 3 : 2);
                Audio.Play("alarm");
                if (t.priority == 1 && !t.followUp) trackedTicket = t.id;
            };
        }

        void Briefing()
        {
            screen = GameScreen.Briefing;
            shift = new Shift(career, 7919 * career.day + 17);
            shift.GenerateQueue();
            Hook();
            facility.Build(shift);
            facility.SpawnDeliveries(shift);
            player.Teleport(Facility.Spawn, 15f);
            selectedTicket = trackedTicket = shift.tickets.OrderBy(t => t.priority).FirstOrDefault()?.id ?? 0;
            shiftSeconds = 0f;
            ResetShiftUi();
        }

        /// <summary>Pick a saved shift back up exactly where the player left it.</summary>
        void Resume(ShiftSave save)
        {
            shift = new Shift(career, 7919 * career.day + 31 * save.tickets.Count + 5, save);
            Hook();
            facility.Build(shift);
            foreach (var f in save.floor) facility.SpawnItem(f.item, f.pos, f.item.CustomerGear);
            player.Teleport(save.playerPos, save.playerYaw);
            trackedTicket = save.tracked;
            selectedTicket = trackedTicket;
            shiftSeconds = save.seconds;
            ResetShiftUi();
            screen = GameScreen.Play;
            Toast($"Shift resumed: day {career.day}, {shift.OpenTickets.Count()} tickets open.", 0);
        }

        /// <summary>Save the career (including the data hall) and the shift in progress.</summary>
        public void SaveMidShift()
        {
            if (shift == null || shift.career != career || screen == GameScreen.Menu || screen == GameScreen.Summary || screen == GameScreen.Shop || screen == GameScreen.Fired) return;
            var s = shift.Snapshot();
            foreach (var p in FindObjectsByType<Pickup>())
                s.floor.Add(new FloorItem { item = p.item, pos = p.transform.position });
            s.playerPos = player.transform.position;
            s.playerYaw = player.transform.eulerAngles.y;
            s.tracked = trackedTicket;
            s.seconds = shiftSeconds;
            career.Save();
            s.Write();
        }

        public void ClockIn()
        {
            screen = GameScreen.Play;
            SaveMidShift();
        }

        /// <summary>The shift ends when the player says so. With work left open, ask first.</summary>
        public void ClockOut()
        {
            if (!shift.AllDone && !confirmClockOut) { confirmClockOut = true; return; }
            EndShift();
        }

        void EndShift()
        {
            confirmClockOut = false;
            cameraUp = false;
            CloseMinigame();
            ClearVisitor();
            shift.End();
            facility.SetPowerFail(false);
            ShiftSave.Delete();
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
            if (!CanBuy(u)) return;
            career.money -= u.cost;
            career.upgrades.Add(u.id);
            career.Save();
            Audio.Play("good");
        }

        public bool CanBuy(UpgradeDef u) =>
            !career.Has(u.id) && career.money >= u.cost && career.Level >= u.level && (u.requires == null || career.Has(u.requires));

        public void NextDay() => Briefing();

        bool Working => screen == GameScreen.Play || screen == GameScreen.Tablet || screen == GameScreen.Rack || screen == GameScreen.Minigame;

        // ================================================================ frame
        void Update()
        {
            float dt = Time.deltaTime;
            bool working = Working && shift != null && shift.career == career;
            if (working)
            {
                shiftSeconds += dt;
                // The UPS batteries and the trainee run on real time, even with the tablet open.
                shift.Tick(dt);
                facility.SpawnDeliveries(shift);
                facility.SetPowerFail(shift.ups > 0);
                VisitorTick(dt);
                if (shift.Fired)
                {
                    // Reputation can hit zero in the middle of a job: that's the end of the shift, right now.
                    Toast("Your reputation hit zero. The site manager wants a word.", 3);
                    EndShift();
                    return;
                }
            }
            else facility.SetPowerFail(false);
            if (screen != GameScreen.Tablet) confirmClockOut = false;
            if (screen != GameScreen.Play) cameraUp = false;
            HandleInput();
            if (screen == GameScreen.Minigame && minigame != null)
            {
                minigame.Tick(dt);
                if (minigame.finished)
                {
                    var t = minigame.task;
                    if (minigame.success) shift.Complete(t);
                    CloseMinigame();
                    facility.Refresh(shift, t.rack);
                    facility.RefreshStations(shift);
                    screen = GameScreen.Rack;
                }
            }
            if (working) TutorialTick();
            player.inputEnabled = screen == GameScreen.Play;
            player.Tick(this, dt);
            MoveWorkCamera(dt);
            if (screen == GameScreen.Menu)
                player.Teleport(new Vector3(11f, 0f, 3.2f), -40f + Mathf.Sin(Time.time * 0.08f) * 25f, 8f);
            facility.ShowMarker(working ? WaypointTarget() : null);
            Audio.HumLevel = working || screen == GameScreen.Menu ? facility.Loudness(player.transform.position) : 0.3f;
            bool locked = screen == GameScreen.Play;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            photoFlash = Mathf.Max(0, photoFlash - dt * 3f);
            toasts.RemoveAll(t => Time.unscaledTime - t.time > 6f);
        }

        void HandleInput()
        {
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            switch (screen)
            {
                case GameScreen.Play:
                    if (esc && cameraUp) cameraUp = false;
                    else if (esc) { pausedFrom = screen; screen = GameScreen.Pause; }
                    else if (Keys.Down(Keys.Action.Camera)) { cameraUp = !cameraUp; Audio.Play("click"); }
                    else if (cameraUp && Input.GetMouseButtonDown(0)) TakePhoto();
                    else if (Keys.Down(Keys.Action.Tablet)) OpenTablet();
                    else if (Keys.Down(Keys.Action.Interact) && player.focus != null && !cameraUp) player.focus.Use(this);
                    else if (Keys.Down(Keys.Action.Drop)) Drop();
                    else if (Input.GetKeyDown(KeyCode.F1)) TutorialSkip();
                    break;
                case GameScreen.Tablet:
                    if (esc || Keys.Down(Keys.Action.Tablet)) screen = GameScreen.Play;
                    break;
                case GameScreen.Rack:
                    if (esc || Keys.Down(Keys.Action.Interact)) screen = GameScreen.Play;
                    else if (Keys.Down(Keys.Action.Tablet)) OpenTablet();
                    break;
                case GameScreen.Minigame:
                    if (esc) { CloseMinigame(); screen = GameScreen.Rack; }
                    break;
                case GameScreen.Pause:
                    if (esc) ClosePause();
                    break;
                case GameScreen.Settings:
                    if (esc && rebinding < 0) CloseSettings();
                    break;
            }
        }

        void ClosePause()
        {
            PlayerPrefs.SetFloat("sens", Player.Sensitivity);
            PlayerPrefs.SetFloat("vol", Audio.Volume);
            screen = pausedFrom;
        }

        public void OpenSettings()
        {
            settingsFrom = screen;
            screen = GameScreen.Settings;
        }

        void CloseSettings()
        {
            PlayerPrefs.SetFloat("sens", Player.Sensitivity);
            PlayerPrefs.SetFloat("vol", Audio.Volume);
            PlayerPrefs.SetFloat("fov", Player.Fov);
            PlayerPrefs.Save();
            screen = settingsFrom;
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
            currentStation = null;
            rearSide = facility.IsBehind(label, player.transform.position);
            screen = GameScreen.Rack;
            Audio.Play("beep");
        }

        public void OpenStation(string id, string title = null)
        {
            currentRack = null;
            currentStation = id;
            currentStationTitle = title;
            screen = GameScreen.Rack;
            Audio.Play("beep");
        }

        /// <summary>Why the player can't do this job from where they're standing (null when they can).</summary>
        public string SideBlocker(WorkTask t)
        {
            if (t.station != null || t.kind == TaskKind.Photo || currentRack == null) return null;
            bool rear = Shift.RearJob(t.kind);
            if (rear == rearSide) return null;
            return rear ? "Rear job: work from the hot aisle behind the rack" : "Front job: walk around to the front of the rack";
        }

        /// <summary>End the current job (if any): tear down its rig and give the camera back to the player.</summary>
        void CloseMinigame()
        {
            if (minigame != null) minigame.End();
            minigame = null;
            player.ResetCamera();
            Ui.SetMouseCanvas(null);
        }

        /// <summary>3D jobs fly the camera in close; console jobs also route the mouse onto the cart's monitor.</summary>
        void MoveWorkCamera(float dt)
        {
            bool workCam = screen == GameScreen.Minigame && minigame != null && (minigame.InWorld || minigame.OnScreen);
            player.ShowHeld(!workCam);
            if (!workCam) { Ui.SetMouseCanvas(null); return; }
            var ct = player.cam.transform;
            float k = snapCamera ? 1f : 1f - Mathf.Exp(-dt * 9f);
            ct.position = Vector3.Lerp(ct.position, minigame.camPos, k);
            ct.rotation = Quaternion.Slerp(ct.rotation, minigame.camRot, k);
            Ui.SetMouseCanvas(minigame.OnScreen && MonitorCanvas(out var m) ? m : (Matrix4x4?)null);
        }

        /// <summary>The matrix that maps the 1280x720 job canvas onto the monitor's display, in GUI pixels.</summary>
        bool MonitorCanvas(out Matrix4x4 m)
        {
            m = Matrix4x4.identity;
            var cam = player.cam;
            var a = cam.WorldToScreenPoint(minigame.displayTL);
            var b = cam.WorldToScreenPoint(minigame.displayBR);
            if (a.z <= 0 || b.z <= 0) return false;
            var tl = new Vector2(a.x, UnityEngine.Screen.height - a.y);
            var br = new Vector2(b.x, UnityEngine.Screen.height - b.y);
            float k = (br.x - tl.x) / 1280f;
            if (k <= 0.01f) return false;
            var p = Ui.Center(1280, 720);
            m = Matrix4x4.TRS(new Vector3(tl.x - p.x * k, tl.y - p.y * k, 0), Quaternion.identity, new Vector3(k, k, 1));
            return true;
        }

        public void StartTask(WorkTask t)
        {
            if (shift.Blocker(t) != null) return;
            if (SideBlocker(t) is string side) { Toast(side, 2); return; }
            if (t.kind == TaskKind.Ship || t.kind == TaskKind.Mop || t.kind == TaskKind.ResetAlarm)
            {
                shift.working = shift.TicketOf(t);
                shift.Complete(t);
                Audio.Play("clunk");
                Toast(t.kind == TaskKind.Ship ? $"Unit {t.tag} is on the outbound pallet. Shipping will pick it up."
                    : t.kind == TaskKind.Mop ? "You mopped up the water and dried the leak detection cable. Monitoring is green."
                    : "Fire panel reset. The suppression system is back to normal standby.", 1);
                facility.RefreshStations(shift);
                return;
            }
            if (t.kind == TaskKind.Escort) { Toast(shift.Blocker(t), 0); return; }
            if (t.kind == TaskKind.Photo) { Toast($"Raise your camera with [{Keys.Label(Keys.Action.Camera)}] and frame the {t.component} of {t.rack}.", 0); return; }
            minigame = Minigame.For(t);
            if (minigame == null) return;
            minigame.Start(this, t);
            screen = GameScreen.Minigame;
        }

        public void TakeStock(StockShelf s)
        {
            var inv = shift.inv;
            if (s.isPart)
            {
                if (inv.PouchFull) { Toast($"Your tool pouch is full ({inv.pouchCap} parts" + (inv.rma.Count > 0 ? $", {inv.rma.Count} of them failed parts for the returns bin" : "") + ").", 2); return; }
                if (!shift.TakePart(s.part)) { Toast($"The {s.ItemName} shelf is empty. Order more from the STOCK page on your tablet: it arrives tomorrow.", 2); return; }
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
            if (inv.rma.Count > 0)
            {
                int n = inv.rma.Count;
                int credit = shift.ReturnRma();
                Toast($"{n} failed part{(n > 1 ? "s" : "")} sent back under RMA: +{Ui.Money(credit)} credit.", 1);
                Audio.Play("pickup");
                return;
            }
            if (inv.hands.Count > 0)
            {
                var it = inv.hands[inv.hands.Count - 1];
                if (it.CustomerGear) { Toast("That belongs to a customer. Install it, don't stock it!", 2); return; }
                if (it.Decom) { Toast("That unit is being decommissioned: wipe it at the bench and ship it from the dock.", 2); return; }
                inv.hands.RemoveAt(inv.hands.Count - 1);
                Toast($"Returned the {it.Def.name}.", 0);
            }
            else if (inv.pouch.Count > 0)
            {
                var p = inv.pouch[inv.pouch.Count - 1];
                Toast($"Returned the {Catalog.PartName(p)} to the shelf.", 0);
                shift.ReturnPart(p);
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
            facility.SpawnItem(it, new Vector3(pos.x, 0f, pos.z), it.CustomerGear);
            Audio.Play("clunk");
        }

        /// <summary>Smart-hands photo: it counts when the whole face of the right rack is in frame.</summary>
        void TakePhoto()
        {
            Audio.Play("shutter");
            photoFlash = 1f;
            var wanted = shift.OpenTickets.SelectMany(k => k.tasks.Where(x => x.kind == TaskKind.Photo && !x.done)).ToList();
            if (wanted.Count == 0) { Toast("Nice shot, but no ticket needs a photo right now.", 0); return; }
            string why = null;
            float bestD = float.MaxValue;
            foreach (var t in wanted)
            {
                if (facility.PhotoCheck(t.rack, t.component == "rear", player.cam, out string reason))
                {
                    shift.working = shift.TicketOf(t);
                    shift.Complete(t);
                    Toast($"Photo of the {t.component} of {t.rack} sent to the customer.", 1);
                    return;
                }
                float d = Vector3.Distance(player.transform.position, facility.RackTop(t.rack, false));
                if (d < bestD) { bestD = d; why = reason; }
            }
            Toast(why, 2);
        }

        public void Toast(string text, int sev)
        {
            toasts.Add(new ToastMsg { text = text, sev = sev, time = Time.unscaledTime });
            while (toasts.Count > 5) toasts.RemoveAt(0);
        }

        // ================================================================ waypoint
        /// <summary>Where the tracked ticket needs the player next: a rack side, a shelf, a station or an item on the floor.</summary>
        public Vector3? WaypointTarget()
        {
            var t = shift?.tickets.Find(x => x.id == trackedTicket && x.Open);
            if (t == null || t.trainee) return null;
            var next = NextStep(t);
            if (next == null) return t.rack != null ? facility.RackTop(t.rack, false) : (Vector3?)null;
            var blocker = shift.Blocker(next);
            if (blocker == null) return Spot(next);
            switch (next.kind)
            {
                case TaskKind.Escort:
                    if (t.tasks.Any(x => x.kind == TaskKind.Badge && !x.done)) return facility.StationPos("kiosk");
                    return next.component == "out" ? facility.StationPos("kiosk") : facility.RackTop(next.rack, false);
                case TaskKind.Mop: return facility.StationPos($"crac{Shift.CracFor(next.u)}");
                case TaskKind.ResetAlarm:
                    var psu = t.tasks.FirstOrDefault(x => x.kind == TaskKind.SwapPsu && !x.done);
                    return psu != null && shift.Blocker(psu) != null ? facility.ShelfPos(null, PartKind.Psu) : facility.RackTop(next.rack, true);
                case TaskKind.Mount:
                    return next.tag != null ? ItemOnFloor(next.tag) : facility.ShelfPos(next.def, null);
                case TaskKind.Wipe:
                case TaskKind.Ship:
                    if (next.kind == TaskKind.Ship && t.tasks.Any(x => x.kind == TaskKind.Wipe && x.tag == next.tag && !x.done)) return facility.StationPos("bench");
                    return ItemOnFloor(next.tag) ?? facility.RackTop(next.rack, false);
                case TaskKind.SwapDrive: return facility.ShelfPos(null, PartKind.Drive);
                case TaskKind.SwapPsu: return facility.ShelfPos(null, PartKind.Psu);
                case TaskKind.SwapSfp: return facility.ShelfPos(null, PartKind.Sfp);
                case TaskKind.SwapDimm: return facility.ShelfPos(null, PartKind.Dimm);
                case TaskKind.SwapFan: return facility.ShelfPos(null, PartKind.Fan);
                case TaskKind.Diagnose: return facility.ShelfPos("kvm", null);
                default: return Spot(next);
            }
        }

        Vector3? ItemOnFloor(string tag)
        {
            var p = FindObjectsByType<Pickup>().FirstOrDefault(x => x.item.tag == tag);
            return p != null ? p.transform.position + Vector3.up * 0.5f : (Vector3?)null;
        }

        Vector3 Spot(WorkTask t)
        {
            if (t.station != null) return facility.StationPos(t.station);
            if (t.kind == TaskKind.Photo) return facility.RackTop(t.rack, t.component == "rear");
            return facility.RackTop(t.rack, Shift.RearJob(t.kind));
        }

        /// <summary>The first unfinished task, preferring ones that can be started now.</summary>
        public WorkTask NextStep(Ticket t) =>
            t.tasks.FirstOrDefault(x => !x.done && shift.Blocker(x) == null) ?? t.tasks.FirstOrDefault(x => !x.done);

        // ================================================================ escorted visitors
        Visitor visitor;
        float visitorWork, visitorAlone;
        readonly HashSet<int> visitorWarned = new HashSet<int>();

        void ClearVisitor()
        {
            if (visitor != null) Destroy(visitor.gameObject);
            visitor = null;
            visitorWork = visitorAlone = 0;
            visitorWarned.Clear();
        }

        /// <summary>The checked-in visitor follows the technician, works at their rack, and leaves from the kiosk.</summary>
        void VisitorTick(float dt)
        {
            var t = shift.OpenTickets.FirstOrDefault(k => k.type == TicketType.Escort && k.tasks.Any(x => x.kind == TaskKind.Badge && x.done) && k.tasks.Any(x => !x.done));
            if (t == null)
            {
                if (visitor != null) { Destroy(visitor.gameObject); visitor = null; }
                return;
            }
            var inTask = t.tasks.First(x => x.kind == TaskKind.Escort && x.component == "in");
            var outTask = t.tasks.First(x => x.kind == TaskKind.Escort && x.component == "out");
            var kiosk = Facility.KioskPos + new Vector3(0.6f, 0f, 0.9f);
            if (visitor == null || visitor.ticketId != t.id)
            {
                if (visitor != null) Destroy(visitor.gameObject);
                visitor = Visitor.Spawn(facility.transform, inTask.done ? facility.RackFront(t.rack) : kiosk, Catalog.CustColor(t.customer), inTask.tag, t.id);
                visitorWork = visitorAlone = 0;
            }
            var me = player.transform.position;
            var vp = visitor.transform.position;
            float dist = Vector2.Distance(new Vector2(me.x, me.z), new Vector2(vp.x, vp.z));
            visitor.Record(me);
            // Left alone on the floor: they wait, and after a while it counts against you (once per visit).
            bool alone = dist > 8f;
            visitorAlone = alone ? visitorAlone + dt : Mathf.Max(0, visitorAlone - dt * 2f);
            if (visitorAlone > 6f && visitorWarned.Add(t.id))
            {
                shift.working = t;
                shift.Mistake($"You left {inTask.tag} alone on the data hall floor. Visitors are escorted at all times", 2f);
            }
            if (!inTask.done)
            {
                var front = facility.RackFront(t.rack);
                bool there = Vector2.Distance(new Vector2(vp.x, vp.z), new Vector2(front.x, front.z)) < 1.8f;
                if (there && dist < 4f)
                {
                    visitor.GoTo(front, dt);
                    visitorWork += dt;
                    if (visitorWork > 12f)
                    {
                        shift.working = t;
                        shift.Complete(inTask);
                        Toast($"{inTask.tag} is done at {t.rack}. Walk them back to the security kiosk.", 1);
                    }
                }
                else if (!alone) visitor.Follow(me, dt);
            }
            else
            {
                if (!alone) visitor.Follow(me, dt);
                if (Vector2.Distance(new Vector2(vp.x, vp.z), new Vector2(kiosk.x, kiosk.z)) < 2.2f && dist < 4f)
                {
                    shift.working = t;
                    shift.Complete(outTask);
                    Toast($"{inTask.tag} signed out and handed back their badge.", 1);
                }
            }
        }

        /// <summary>What the visitor is doing, for the HUD (null when there's no visitor).</summary>
        public string VisitorStatus()
        {
            if (visitor == null) return null;
            var t = shift.tickets.Find(x => x.id == visitor.ticketId);
            if (t == null) return null;
            var inTask = t.tasks.First(x => x.kind == TaskKind.Escort && x.component == "in");
            if (visitorAlone > 0.5f) return $"{visitor.visitorName} is waiting for you. Go back to them!";
            if (!inTask.done) return visitorWork > 0 ? $"{visitor.visitorName} is working at {t.rack}: {Mathf.Max(0, 12f - visitorWork):0}s. Stay close." : $"Escorting {visitor.visitorName} to {t.rack}";
            return $"Escorting {visitor.visitorName} back to the security kiosk";
        }

        // ================================================================ screenshot mode (-shotdir <dir>)
        IEnumerator Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-shotdir");
            if (i < 0 || i + 1 >= args.Length) yield break;
            string dir = args[i + 1];
            Directory.CreateDirectory(dir);
            Career.PathOverride = Path.Combine(dir, "career.json");
            Career.Delete();
            snapCamera = true;
            IEnumerator Shot(string name)
            {
                for (int k = 0; k < 8; k++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                yield return null;
                yield return null;
            }
            yield return Shot("00_menu");
            career = new Career { day = 4, money = 2600, rep = 62, xp = 320, tutorialDone = true };
            Briefing();
            yield return Shot("01_briefing");
            ClockIn();
            var tickets = new List<Ticket>();
            foreach (TicketType tt in System.Enum.GetValues(typeof(TicketType)))
            {
                // Some types are one-at-a-time (a visitor, a receiving pallet): use today's if there is one.
                var t = shift.SpawnTicket(tt) ?? shift.tickets.FirstOrDefault(k => k.type == tt && k.Open && !tickets.Contains(k));
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
            player.Teleport(new Vector3(rp.x + 0.3f, 0f, Facility.RowFrontZ(rack.row) + RackView.Depth + 1.3f), 190f, 12f);
            yield return Shot("04_rack_rear");
            player.Teleport(new Vector3(17.5f, 0f, 9f), 80f, 5f);
            yield return Shot("05_storage");
            player.Teleport(new Vector3(16f, 0f, 18.5f), 0f, 15f);
            yield return Shot("06_dock");
            player.Teleport(new Vector3(3f, 0f, 12f), -90f, 5f);
            yield return Shot("07_meetme");
            player.Teleport(new Vector3(2.6f, 0f, 9.6f), -110f, 8f);
            yield return Shot("07b_crac");
            player.Teleport(new Vector3(7.4f, 0f, 3.4f), 205f, 10f);
            yield return Shot("07c_plant_kiosk");
            player.Teleport(new Vector3(18.4f, 0f, 17.6f), 50f, 14f);
            yield return Shot("07d_receiving");
            player.Teleport(new Vector3(rp.x, 0f, Facility.RowFrontZ(rack.row) - 2.6f), 0f, 3f);
            cameraUp = true;
            screen = GameScreen.Play;
            yield return Shot("08_camera");
            // Photo framing check, front and rear, logged for the build verification.
            foreach (bool rearShot in new[] { false, true })
            {
                var ph = new WorkTask { kind = TaskKind.Photo, rack = fw.rack, component = rearShot ? "rear" : "front" };
                fw.tasks.Add(ph);
                float z = rearShot ? Facility.RowFrontZ(rack.row) + RackView.Depth + 2.3f : Facility.RowFrontZ(rack.row) - 2.3f;
                player.Teleport(new Vector3(rp.x, 0f, z), rearShot ? 180f : 0f, 14f);
                for (int k = 0; k < 4; k++) yield return null;
                TakePhoto();
                Debug.Log($"[shot] {ph.component} photo of {fw.rack}: {(ph.done ? "accepted" : "rejected: " + toasts.LastOrDefault()?.text)}");
                fw.tasks.Remove(ph);
            }
            cameraUp = false;
            selectedTicket = fw.id;
            OpenTablet();
            yield return Shot("09_tablet");
            tabletTab = 1;
            yield return Shot("10_tablet_customers");
            tabletTab = 2;
            yield return Shot("10_tablet_career");
            tabletTab = 3;
            yield return Shot("10_tablet_stock");
            tabletTab = 4;
            yield return Shot("10_tablet_record");
            tabletTab = 0;
            OpenRack(fw.rack);
            yield return Shot("11_rack_panel");
            // Give the technician what each job needs, then open every minigame.
            shift.inv.handCap = 3;
            shift.inv.pouchCap = 9;
            shift.inv.pouch.AddRange(Catalog.Parts);
            shift.inv.hands.Add(new Item { def = "kvm", label = "Crash cart" });
            int n = 12;
            IEnumerator Play(WorkTask task)
            {
                if (task.kind == TaskKind.Mount && !shift.inv.Has(task.def, task.tag)) shift.inv.hands.Add(new Item { def = task.def, tag = task.tag, label = "test" });
                if (shift.Blocker(task) != null || Minigame.For(task) == null) yield break;
                currentRack = task.rack;
                rearSide = Shift.RearJob(task.kind);
                StartTask(task);
                yield return Shot($"{n++:00}_{task.kind}");
            }
            foreach (var t in tickets)
            {
                yield return Play(t.tasks[0]);
                CloseMinigame();
                if (t.type == TicketType.FixIssue || t.type == TicketType.SwitchOutage)
                {
                    shift.Complete(t.tasks[0]);
                    yield return Play(t.tasks[t.tasks.Count - 1]);
                    CloseMinigame();
                }
                if (t.type == TicketType.Decommission)
                {
                    shift.Complete(t.tasks[0]);
                    shift.inv.hands.Clear();
                    yield return Play(t.tasks[1]);
                    CloseMinigame();
                    shift.Complete(t.tasks[1]);
                    var wipe = t.tasks.FirstOrDefault(x => x.kind == TaskKind.Wipe);
                    if (wipe != null) { yield return Play(wipe); CloseMinigame(); }
                }
                if (t.type == TicketType.CrossConnect)
                {
                    shift.Complete(t.tasks[0]);
                    yield return Play(t.tasks[1]);
                    CloseMinigame();
                }
            }
            // Second half of a rack-mount (the screws) and cabling after a completed mount.
            var mountTicket = tickets.FirstOrDefault(t => t.type == TicketType.Expand);
            if (mountTicket != null)
            {
                var mount = mountTicket.tasks[0];
                if (!shift.inv.Has(mount.def, mount.tag)) shift.inv.hands.Add(new Item { def = mount.def, tag = mount.tag, label = "test" });
                shift.Complete(mount);
                yield return Play(mountTicket.tasks[1]);
            }
            CloseMinigame();
            // Follow-ups: what a pulled PSU and a pulled healthy drive leave behind.
            var victim = shift.racks.Where(r => r.owner != null).SelectMany(r => r.devices.Select(d => (r, d))).First(x => x.d.Def.bays > 0 && x.d.Healthy);
            var up = shift.FollowUp(victim.r.label, victim.d, TaskKind.RaidRebuild, Fault.ArrayCritical, "Array critical (screenshot)", "test", 1);
            if (up != null) { yield return Play(up.tasks[0]); CloseMinigame(); }
            var off = shift.FollowUp(victim.r.label, victim.d, TaskKind.PowerOn, Fault.None, "Lost power (screenshot)", "test");
            if (off != null) { yield return Play(off.tasks[0]); CloseMinigame(); }
            // Every hot-swap flavour on one server: PSU and optic at the rear, memory and fans with the lid off.
            var srv = shift.racks.Where(r => r.owner != null).SelectMany(r => r.devices.Select(d => (r, d))).First(x => x.d.Def.kind == DevKind.Server && x.d.Def.sfps > 0 && x.d.Healthy);
            foreach (var kind in new[] { TaskKind.SwapPsu, TaskKind.SwapSfp, TaskKind.SwapDimm, TaskKind.SwapFan })
            {
                yield return Play(new WorkTask { kind = kind, rack = srv.r.label, deviceId = srv.d.id, u = srv.d.u });
                CloseMinigame();
            }
            // Every newer job, whatever today's random tickets happened to roll.
            var owned = shift.racks.Where(r => r.owner != null && r.devices.Count >= 5).ToList();
            var swRack = owned.First(r => Shift.SwitchOf(r) != null);
            var sw = Shift.SwitchOf(swRack);
            var shelf = shift.racks.SelectMany(r => r.devices.Select(d => (r, d))).FirstOrDefault(x => x.d.def == "ds4u");
            var gapRack = owned.First(r => r.FreeUnits >= 6 && r.reserved.Count == 0);
            gapRack.openGaps.Clear();
            for (int u = 1; u <= Rack.Units && gapRack.openGaps.Count < 4; u++) if (gapRack.At(u) == null) gapRack.openGaps.Add(u);
            facility.Refresh(shift, gapRack.label);
            var extra = new List<WorkTask>
            {
                new WorkTask { kind = TaskKind.ConfigRestore, rack = swRack.label, deviceId = sw.id, u = sw.u },
                new WorkTask { kind = TaskKind.PowerCycle, rack = swRack.label, deviceId = sw.id, u = sw.u },
                new WorkTask { kind = TaskKind.Blanking, rack = gapRack.label },
                new WorkTask { kind = TaskKind.CableTidy, rack = owned[1].label, slot = 5 },
                new WorkTask { kind = TaskKind.Tiles, rack = owned[0].label, station = "aisle1", u = 1, slot = 4242 },
                new WorkTask { kind = TaskKind.CracService, station = "crac1", u = 1, slot = 77 },
                new WorkTask { kind = TaskKind.Valve, station = "crac2", u = 2 },
                new WorkTask { kind = TaskKind.Generator, station = "plant", component = "fuel" },
                new WorkTask { kind = TaskKind.SwapDrive, rack = swRack.label, deviceId = owned.SelectMany(r => r.devices).First(d => d.Def.bays > 0).id, component = "locate" },
            };
            if (shelf.d != null) extra.Add(new WorkTask { kind = TaskKind.SasPath, rack = shelf.r.label, deviceId = shelf.d.id, u = shelf.d.u, slot = 1 });
            foreach (var task in extra)
            {
                shift.ups = task.kind == TaskKind.Generator ? 95f : 0f;
                if (task.kind == TaskKind.SwapDrive)
                {
                    var host = shift.racks.First(r => r.devices.Any(d => d.id == task.deviceId));
                    task.rack = host.label;
                    task.u = host.devices.First(d => d.id == task.deviceId).u;
                    host.devices.First(d => d.id == task.deviceId).failedBay = 0;
                }
                yield return Play(task);
                CloseMinigame();
            }
            shift.ups = 0;
            screen = GameScreen.Play;
            yield return Shot($"{n++:00}_hud");
            OpenSettings();
            yield return Shot($"{n++:00}_settings");
            screen = GameScreen.Play;
            confirmClockOut = false;
            career.rep = 100f; // a screenshot shift hands over a pile of tickets; don't get fired for it
            foreach (var k in shift.OpenTickets.Skip(4).ToList()) shift.tickets.Remove(k);
            OpenTablet();
            ClockOut();
            yield return Shot($"{n++:00}_clockout_confirm");
            ClockOut();
            yield return Shot($"{n++:00}_summary");
            screen = GameScreen.Shop;
            yield return Shot($"{n++:00}_shop");
            // A first day with the tutorial on.
            career = new Career();
            Briefing();
            ClockIn();
            yield return Shot($"{n++:00}_tutorial");
            Career.Delete();
            Application.Quit();
        }
    }
}
