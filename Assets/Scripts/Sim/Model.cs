using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// What's wrong with a device. NoPower and ArrayCritical are only ever caused by the technician
    /// (pulling the wrong PSU, cord or drive) and come with a follow-up ticket.
    /// </summary>
    public enum Fault { None, LinkDown, BadSfp, Hung, BadDimm, FanFail, NoPower, ArrayCritical }

    /// <summary>JsonUtility writes null strings as "". These turn them back into nulls after loading.</summary>
    static class Json
    {
        public static string N(string s) => string.IsNullOrEmpty(s) ? null : s;
    }

    [Serializable]
    public class Device : ISerializationCallbackReceiver
    {
        public int id;
        public string def, owner, serial;
        public int u;
        /// <summary>Career day it was installed. Gear that predates you has a negative day.</summary>
        public int installDay;
        public string bios = "2.1.0", bmc = "4.10.2", nic = "22.4.1";
        public int failedBay = -1, failedPsu = -1, badSfp = -1, badDimm = -1, badFan = -1;
        public Fault fault;

        public DevDef Def => Catalog.Dev(def);
        public int Top => u + Def.heightU - 1;
        public bool Healthy => failedBay < 0 && failedPsu < 0 && fault == Fault.None;
        public string Name => $"{Def.name} #{id}";
        public string Host => Catalog.Hostname(this);

        public string Firmware(string comp) => comp == "BIOS" ? bios : comp == "BMC" ? bmc : nic;
        public void SetFirmware(string comp, string v)
        {
            if (comp == "BIOS") bios = v; else if (comp == "BMC") bmc = v; else nic = v;
        }

        /// <summary>The night shift (or a fix) clears everything.</summary>
        public void ClearFaults()
        {
            failedBay = failedPsu = badSfp = badDimm = badFan = -1;
            fault = Fault.None;
        }

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { owner = Json.N(owner); }
    }

    [Serializable]
    public class Rack : ISerializationCallbackReceiver
    {
        public const int Units = 42;
        public string label, owner;
        public int row, col;
        public List<Device> devices = new List<Device>();
        /// <summary>U ranges promised to open install tickets (x = bottom U, y = height).</summary>
        public List<Vector2Int> reserved = new List<Vector2Int>();

        public Device At(int u)
        {
            foreach (var d in devices) if (u >= d.u && u <= d.Top) return d;
            return null;
        }

        public bool Reserved(int u)
        {
            foreach (var r in reserved) if (u >= r.x && u < r.x + r.y) return true;
            return false;
        }

        public bool Free(int u, int h)
        {
            if (u < 1 || u + h - 1 > Units) return false;
            for (int i = u; i < u + h; i++) if (At(i) != null || Reserved(i)) return false;
            return true;
        }

        public int FreeUnits
        {
            get { int n = 0; for (int u = 1; u <= Units; u++) if (At(u) == null && !Reserved(u)) n++; return n; }
        }

        /// <summary>A free spot for a device of height h, preferring low positions with a little randomness.</summary>
        public int FindFree(int h, System.Random r)
        {
            var spots = new List<int>();
            for (int u = 1; u <= Units - h + 1; u++) if (Free(u, h)) spots.Add(u);
            if (spots.Count == 0) return -1;
            return spots[Math.Min(spots.Count - 1, r.Next(Math.Min(4, spots.Count)))];
        }

        public bool Empty => owner == null && devices.Count == 0 && reserved.Count == 0;

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { owner = Json.N(owner); }
    }

    public enum TicketType { Expand, FixIssue, ReplaceHardware, Firmware, RackCustomer, NewContract, Decommission, SmartHands, CrossConnect, PowerAudit }
    public enum TaskKind
    {
        Mount, Cable, SwapDrive, SwapPsu, SwapSfp, Firmware, Diagnose, PowerCycle, Reseat,
        Unmount, Uncable, Wipe, Ship, SwapDimm, SwapFan, Inspect, Photo, Power, Trace, Patch, PowerOn, RaidRebuild,
    }
    public enum TicketStatus { Open, Done, HandedOver }

    [Serializable]
    public class WorkTask : ISerializationCallbackReceiver
    {
        public TaskKind kind;
        /// <summary>
        /// rack: the rack the work belongs to. station: null when the job is done at the rack,
        /// otherwise "bench", "dock" or "mmr". component/version/slot carry kind-specific details.
        /// </summary>
        public string rack, def, tag, component, version, station;
        public int u, deviceId, slot = -1;
        /// <summary>Diagnose only: the BMC is down too, so the crash cart is needed.</summary>
        public bool cart;
        public bool done;

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize()
        {
            def = Json.N(def); tag = Json.N(tag); component = Json.N(component); version = Json.N(version); station = Json.N(station);
        }
    }

    [Serializable]
    public class Ticket
    {
        public int id, pay, mistakes;
        /// <summary>1 = P1 (urgent) .. 3 = P3 (routine).</summary>
        public int priority = 3;
        public TicketType type;
        public string customer, title, body, rack;
        public TicketStatus status;
        /// <summary>Raised because of the technician's own mistake: no pay, but it still has to be fixed.</summary>
        public bool followUp;
        /// <summary>A P1 that was left open while lower-priority work got closed: no rush bonus.</summary>
        public bool neglected;
        public List<WorkTask> tasks = new List<WorkTask>();

        public int DoneCount { get { int n = 0; foreach (var t in tasks) if (t.done) n++; return n; } }
        public bool Open => status == TicketStatus.Open;
        public string TypeLabel => followUp ? "Follow-up incident" : TypeName(type);
        public string PriorityLabel => $"P{priority}";
        public bool RushBonus => priority == 1 && !neglected && !followUp;

        public static string TypeName(TicketType t) => t switch
        {
            TicketType.Expand => "Capacity expansion",
            TicketType.FixIssue => "Incident",
            TicketType.ReplaceHardware => "Hardware replacement",
            TicketType.Firmware => "Firmware update",
            TicketType.RackCustomer => "Customer install",
            TicketType.NewContract => "New contract build-out",
            TicketType.Decommission => "Decommission",
            TicketType.SmartHands => "Smart hands",
            TicketType.CrossConnect => "Cross-connect",
            _ => "PDU rebalancing",
        };
    }

    /// <summary>A piece of rack equipment (or the crash cart) the technician can carry.</summary>
    [Serializable]
    public class Item : ISerializationCallbackReceiver
    {
        public string def, tag, label;
        public DevDef Def => Catalog.Dev(def);
        /// <summary>Customer shipments are tagged T…, decommissioned units D….</summary>
        public bool CustomerGear => tag != null && tag.StartsWith("T");
        public bool Decom => tag != null && tag.StartsWith("D");

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { tag = Json.N(tag); }
    }

    public class Inventory
    {
        public readonly List<Item> hands = new List<Item>();
        public readonly List<PartKind> pouch = new List<PartKind>();
        public int handCap = 1, pouchCap = 4;

        public bool HandsFull => hands.Count >= handCap;
        public bool PouchFull => pouch.Count >= pouchCap;
        public bool Has(string def, string tag) => hands.Exists(i => i.def == def && i.tag == tag);
        public bool HasPart(PartKind k) => pouch.Contains(k);

        public Item Take(string def, string tag)
        {
            int i = hands.FindIndex(x => x.def == def && x.tag == tag);
            if (i < 0) return null;
            var it = hands[i];
            hands.RemoveAt(i);
            return it;
        }

        public bool TakePart(PartKind k) => pouch.Remove(k);
    }

    [Serializable]
    public class CustRel
    {
        public string name;
        public float sat = 60f;
    }

    /// <summary>Everything that persists between shifts, including the data hall itself.</summary>
    [Serializable]
    public class Career
    {
        public int day = 1, money, ticketsDone, handedOver, totalEarned, bestDay, xp;
        public int nextDeviceId = 1, nextTicketId = 101;
        public float rep = 50f;
        public bool tutorialDone;
        public List<string> upgrades = new List<string>();
        public List<Rack> hall = new List<Rack>();
        public List<CustRel> customers = new List<CustRel>();

        public bool Has(string id) => upgrades.Contains(id);

        public int Level
        {
            get { int l = 1; for (int i = 1; i < Catalog.LevelXp.Length; i++) if (xp >= Catalog.LevelXp[i]) l = i + 1; return l; }
        }
        public string Title => Catalog.LevelTitles[Level - 1];
        /// <summary>XP needed for the next level, or -1 at the top.</summary>
        public int NextLevelXp => Level < Catalog.LevelXp.Length ? Catalog.LevelXp[Level] : -1;

        /// <summary>Reputation and seniority both raise the rate.</summary>
        public float PayMul => 0.75f + rep / 200f + 0.04f * (Level - 1);

        public float Sat(string customer) => customers.Find(c => c.name == customer)?.sat ?? 60f;
        public void ChangeSat(string customer, float d)
        {
            if (string.IsNullOrEmpty(customer)) return;
            var c = customers.Find(x => x.name == customer);
            if (c == null) customers.Add(c = new CustRel { name = customer });
            c.sat = Mathf.Clamp(c.sat + d, 0f, 100f);
        }
        /// <summary>Happy customers tip, unhappy ones haggle: 0.9x at 0 satisfaction, 1.15x at 100.</summary>
        public float CustMul(string customer) => 0.9f + Sat(customer) / 400f;

        public static string PathOverride;
        public static string SavePath => PathOverride ?? Path.Combine(Application.persistentDataPath, "career.json");
        public static bool Exists => File.Exists(SavePath);

        public void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning("Save failed: " + e.Message); }
        }

        public static Career Load()
        {
            try { if (Exists) return JsonUtility.FromJson<Career>(File.ReadAllText(SavePath)) ?? new Career(); }
            catch (Exception e) { Debug.LogWarning("Load failed: " + e.Message); }
            return new Career();
        }

        public static void Delete()
        {
            try { if (Exists) File.Delete(SavePath); } catch { }
            ShiftSave.Delete();
        }
    }

    [Serializable]
    public class FloorItem
    {
        public Item item;
        public Vector3 pos;
    }

    /// <summary>A shift in progress, saved next to the career so the player can quit and resume mid-shift.</summary>
    [Serializable]
    public class ShiftSave
    {
        public List<Ticket> tickets = new List<Ticket>();
        public List<Item> hands = new List<Item>();
        public List<PartKind> pouch = new List<PartKind>();
        public List<FloorItem> floor = new List<FloorItem>();
        public int earned, completed, mistakes, handedOver, interrupts, tracked, xpStart;
        public float repStart, seconds, playerYaw;
        public Vector3 playerPos;

        public static string SavePath => Path.Combine(Path.GetDirectoryName(Career.SavePath), "shift.json");
        public static bool Exists => File.Exists(SavePath);

        public void Write()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(this)); }
            catch (Exception e) { Debug.LogWarning("Shift save failed: " + e.Message); }
        }

        public static ShiftSave Load()
        {
            try { if (Exists) return JsonUtility.FromJson<ShiftSave>(File.ReadAllText(SavePath)); }
            catch (Exception e) { Debug.LogWarning("Shift load failed: " + e.Message); }
            return null;
        }

        public static void Delete()
        {
            try { if (Exists) File.Delete(SavePath); } catch { }
        }
    }
}
