using System.Collections.Generic;
using UnityEngine;

namespace RH
{
    /// <summary>
    /// A customer's engineer on an escorted visit. They follow in the technician's footsteps (so they
    /// get round racks the same way), stop when they're close behind, and wait if left behind.
    /// </summary>
    public class Visitor : MonoBehaviour
    {
        public string visitorName;
        public int ticketId;
        readonly List<Vector3> trail = new List<Vector3>();
        const float Speed = 2.6f, Keep = 1.3f;
        TextMesh nameTag;

        public static Visitor Spawn(Transform parent, Vector3 pos, Color vest, string name, int ticketId)
        {
            var g = new GameObject("Visitor " + name);
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var v = g.AddComponent<Visitor>();
            v.visitorName = name;
            v.ticketId = ticketId;
            var skin = Mats.Lit(new Color(0.78f, 0.62f, 0.5f), 0.2f);
            var clothes = Mats.Lit(new Color(0.2f, 0.22f, 0.28f), 0.1f);
            Part(g.transform, PrimitiveType.Capsule, new Vector3(0, 0.82f, 0), new Vector3(0.42f, 0.82f, 0.32f), clothes);
            Part(g.transform, PrimitiveType.Sphere, new Vector3(0, 1.6f, 0), new Vector3(0.22f, 0.25f, 0.23f), skin);
            // A hi-vis visitor vest in the customer's colour, with a badge.
            Part(g.transform, PrimitiveType.Cube, new Vector3(0, 1.18f, 0), new Vector3(0.44f, 0.42f, 0.34f), Mats.Lit(vest * 0.9f + new Color(0.1f, 0.1f, 0.1f), 0.2f));
            Part(g.transform, PrimitiveType.Cube, new Vector3(0.1f, 1.28f, -0.175f), new Vector3(0.08f, 0.1f, 0.01f), Mats.Unlit(Color.white));
            v.nameTag = Mats.Text(g.transform, new Vector3(0, 2.0f, 0), $"VISITOR  {name}", 0.07f, new Color(1f, 0.85f, 0.3f));
            return v;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>Leave breadcrumbs where the technician walks.</summary>
        public void Record(Vector3 player)
        {
            player.y = 0;
            if (trail.Count == 0 || Vector3.Distance(trail[trail.Count - 1], player) > 0.35f) trail.Add(player);
            if (trail.Count > 400) trail.RemoveAt(0);
        }

        /// <summary>Walk the trail toward the technician, stopping a step behind them.</summary>
        public void Follow(Vector3 player, float dt)
        {
            player.y = 0;
            var pos = transform.position;
            pos.y = 0;
            if (Vector3.Distance(pos, player) < Keep) { trail.Clear(); return; }
            // Skip crumbs already passed.
            while (trail.Count > 1 && Vector3.Distance(pos, trail[0]) < 0.2f) trail.RemoveAt(0);
            var target = trail.Count > 0 ? trail[0] : player;
            Step(target, dt);
        }

        /// <summary>Walk straight to a spot (the rack front, the kiosk) and stand there.</summary>
        public bool GoTo(Vector3 spot, float dt)
        {
            spot.y = 0;
            var pos = transform.position;
            pos.y = 0;
            if (Vector3.Distance(pos, spot) < 0.1f) return true;
            Step(spot, dt);
            return false;
        }

        void Step(Vector3 target, float dt)
        {
            var pos = transform.position;
            var d = target - new Vector3(pos.x, 0, pos.z);
            if (d.sqrMagnitude < 0.0001f) return;
            var step = Vector3.ClampMagnitude(d, Speed * dt);
            transform.position = pos + step;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(d.x, 0, d.z)), dt * 8f);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null && nameTag != null) nameTag.transform.rotation = Quaternion.LookRotation(nameTag.transform.position - cam.transform.position);
        }
    }
}
