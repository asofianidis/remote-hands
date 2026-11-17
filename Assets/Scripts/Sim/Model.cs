using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RH
{
    public enum Fault { None, LinkDown, BadSfp, Hung }

    public class Device
    {
        public int id;
        public string def, owner;
        public int u;
        public string bios = "2.1.0", bmc = "4.10.2", nic = "22.4.1";
        public int failedBay = -1, failedPsu = -1;
        public Fault fault;
        public int badSfp = -1;
        /// <summary>Ticket currently touching this device (one ticket per device at a time).</summary>
        public int ticket;

        public DevDef Def => Catalog.Dev(def);
        public int Top => u + Def.heightU - 1;
        public bool Healthy => failedBay < 0 && failedPsu < 0 && fault == Fault.None;
        public string Name => $"{Def.name} #{id}";

        public string Firmware(string comp) => comp == "BIOS" ? bios : comp == "BMC" ? bmc : nic;
        public void SetFirmware(string comp, string v)
        {
            if (comp == "BIOS") bios = v; else if (comp == "BMC") bmc = v; else nic = v;
        }
    }

    public class Rack
    {
        public const int Units = 42;
        public string label, owner;
        public int row, col;
        public readonly List<Device> devices = new List<Device>();
        /// <summary>U ranges promised to open install tickets (x = bottom U, y = height).</summary>
        public readonly List<Vector2Int> reserved = new List<Vector2Int>();

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
    }

    public enum TicketType { Expand, FixIssue, ReplaceHardware, Firmware, RackCustomer, NewContract }
    public enum TaskKind { Mount, Cable, SwapDrive, SwapPsu, SwapSfp, Firmware, Diagnose, PowerCycle, Reseat }
    public enum TicketStatus { Open, Done, HandedOver }

    public class WorkTask
    {
        public TaskKind kind;
        public string rack, def, tag, component, version;
        public int u, deviceId;
        public bool done;
    }

    public class Ticket
    {
        public int id, created, due, pay, finished;
        public TicketType type;
        public string customer, title, body, rack;
        public TicketStatus status;
        public readonly List<WorkTask> tasks = new List<WorkTask>();

        public int DoneCount { get { int n = 0; foreach (var t in tasks) if (t.done) n++; return n; } }
        public bool Open => status == TicketStatus.Open;

        public static string TypeName(TicketType t) => t switch
        {
            TicketType.Expand => "Capacity expansion",
            TicketType.FixIssue => "Incident",
            TicketType.ReplaceHardware => "Hardware replacement",
            TicketType.Firmware => "Firmware update",
            TicketType.RackCustomer => "Customer install",
            _ => "New contract build-out",
        };
    }

    /// <summary>A piece of rack equipment the technician can carry.</summary>
    public class Item
    {
        public string def, tag, label;
        public DevDef Def => Catalog.Dev(def);
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

    /// <summary>Everything that persists between shifts.</summary>
    [Serializable]
    public class Career
    {
        public int day = 1, money, ticketsDone, ticketsLate, handedOver, totalEarned, bestDay;
        public float rep = 50f;
        public List<string> upgrades = new List<string>();

        public bool Has(string id) => upgrades.Contains(id);
        public float PayMul => 0.75f + rep / 200f;

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
        }
    }
}
