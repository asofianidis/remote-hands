using System.Collections.Generic;
using UnityEngine;

namespace RH
{
    /// <summary>First-person technician: walking, looking, interacting and carrying equipment.</summary>
    public class Player : MonoBehaviour
    {
        public Camera cam;
        public Interactable focus;
        public bool inputEnabled;
        public static float Sensitivity = 2.2f;
        public static float Fov = 72f;

        CharacterController cc;
        float yaw, pitch, vy, bob;
        Transform heldRoot;
        readonly List<GameObject> heldViews = new List<GameObject>();
        int heldVersion = -1;

        public void Init()
        {
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.28f;
            cc.center = new Vector3(0, 0.875f, 0);
            cc.stepOffset = 0.3f;
            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            cam = camGo.GetComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.transform.localPosition = new Vector3(0, 1.62f, 0);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 72f;
            cam.backgroundColor = Color.black;
            heldRoot = new GameObject("Held").transform;
            heldRoot.SetParent(cam.transform, false);
            heldRoot.localPosition = new Vector3(0.05f, -0.42f, 0.62f);
        }

        public void Teleport(Vector3 pos, float yawDeg, float pitchDeg = 0f)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            yaw = yawDeg;
            pitch = pitchDeg;
            vy = 0;
            Apply();
        }

        /// <summary>Hand the camera back to the player after a job moved it.</summary>
        public void ResetCamera()
        {
            cam.transform.localPosition = new Vector3(0, 1.62f, 0);
            Apply();
        }

        /// <summary>Hide what's in the technician's hands while the camera is off doing a job.</summary>
        public void ShowHeld(bool on)
        {
            if (heldRoot.gameObject.activeSelf != on) heldRoot.gameObject.SetActive(on);
        }

        void Apply()
        {
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        public void Tick(Game g, float dt)
        {
            var inv = g.shift?.inv;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, g.cameraUp ? Fov * 0.8f : Fov, dt * 10f);
            if (inputEnabled)
            {
                yaw += Input.GetAxisRaw("Mouse X") * Sensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * Sensitivity, -85f, 85f);
                Apply();
                var move = new Vector3((Keys.Held(Keys.Action.Right) ? 1 : 0) - (Keys.Held(Keys.Action.Left) ? 1 : 0), 0, (Keys.Held(Keys.Action.Forward) ? 1 : 0) - (Keys.Held(Keys.Action.Back) ? 1 : 0));
                move = Vector3.ClampMagnitude(move, 1f);
                bool sprint = Keys.Held(Keys.Action.Sprint);
                float shoes = g.career.Has("shoes2") ? 1.45f : g.career.Has("shoes") ? 1.25f : 1f;
                float speed = (sprint ? 5.4f : 3.3f) * shoes * (inv != null && inv.hands.Count > 0 ? 0.82f : 1f);
                var world = transform.TransformDirection(move) * speed;
                vy = cc.isGrounded ? -1f : vy - 18f * dt;
                cc.Move((world + Vector3.up * vy) * dt);
                bob += move.magnitude * dt * (sprint ? 11f : 8f);
                cam.transform.localPosition = new Vector3(0, 1.62f + Mathf.Sin(bob) * 0.025f * move.magnitude, 0);
            }

            focus = null;
            if (inputEnabled && !g.cameraUp && Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 2.6f))
                focus = hit.collider.GetComponentInParent<Interactable>();

            if (inv != null) SyncHeld(inv);
        }

        /// <summary>Rebuild the carried-equipment view when the hands change.</summary>
        void SyncHeld(Inventory inv)
        {
            int v = inv.hands.Count * 1000 + (inv.hands.Count > 0 ? inv.hands[inv.hands.Count - 1].GetHashCode() % 997 : 0);
            if (v == heldVersion) return;
            heldVersion = v;
            foreach (var h in heldViews) Destroy(h);
            heldViews.Clear();
            float y = 0;
            foreach (var it in inv.hands)
            {
                float h = it.Def.heightU * 0.0445f;
                bool boxed = it.CustomerGear;
                var g = Mats.Box(heldRoot, new Vector3(0, y + h / 2, 0), new Vector3(0.5f, boxed ? h + 0.08f : h, 0.55f),
                    boxed ? Mats.Lit(new Color(0.7f, 0.55f, 0.35f)) : Mats.Lit(it.Def.color * 1.4f, 0.35f, 0.3f), false, false, "held");
                g.layer = 0;
                heldViews.Add(g);
                y += (boxed ? h + 0.08f : h) + 0.01f;
            }
        }
    }
}
