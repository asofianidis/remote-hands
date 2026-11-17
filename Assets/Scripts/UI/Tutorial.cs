using System.Linq;
using UnityEngine;

namespace RH
{
    /// <summary>A guided first shift: one instruction at a time, each cleared by actually doing it.</summary>
    public partial class Game
    {
        int tutStep = -1;
        float tutTimer;

        bool TutorialOn => tutStep >= 0 && !career.tutorialDone && career.day == 1;

        void TutorialReset()
        {
            tutStep = career.day == 1 && !career.tutorialDone ? 0 : -1;
            tutTimer = 0;
        }

        void TutorialSkip()
        {
            if (!TutorialOn) return;
            tutStep = -1;
            career.tutorialDone = true;
            Toast("Training skipped.", 0);
        }

        static readonly string[] TutText =
        {
            "Press [{tab}] to open your tablet. Today's work order is in there: read a ticket.",
            "Close the tablet with [{tab}]. The ticket you're tracking puts a blue marker in the hall.",
            "Follow the marker. If the job needs equipment or a spare part, grab it from the storage room shelves with [{e}].",
            "At the rack, press [{e}] to open the rack panel and Start a job. Power, cabling and optics are done from the REAR, in the hot aisle.",
            "Finish the job. Mistakes cost reputation and the 20% clean-work bonus; P1 tickets pay 25% more if you close them first.",
            "Nice. Keep going until the work order is done, then clock out from your tablet. Unfinished tickets cost reputation.",
        };

        void TutorialTick()
        {
            if (!TutorialOn) return;
            tutTimer += Time.deltaTime;
            bool next = tutStep switch
            {
                0 => screen == GameScreen.Tablet,
                1 => screen == GameScreen.Play && tutTimer > 0.5f,
                2 => shift.inv.hands.Count > 0 || shift.inv.pouch.Count > 0 || (screen == GameScreen.Rack && currentRack != null),
                3 => screen == GameScreen.Minigame,
                4 => shift.tickets.Any(t => t.DoneCount > 0),
                _ => tutTimer > 12f,
            };
            if (!next) return;
            tutStep++;
            tutTimer = 0;
            if (tutStep >= TutText.Length)
            {
                tutStep = -1;
                career.tutorialDone = true;
            }
            else Audio.Play("beep");
        }

        void DrawTutorial()
        {
            if (!TutorialOn) return;
            string text = TutText[tutStep].Replace("{tab}", Keys.Label(Keys.Action.Tablet)).Replace("{e}", Keys.Label(Keys.Action.Interact));
            float w = 760;
            float h = Ui.TextHeight(text, w - 40, 20) + 64;
            var r = new Rect(Ui.W / 2 - w / 2, 24, w, h);
            Ui.Box(r, new Color(0.05f, 0.12f, 0.18f, 0.92f));
            Ui.Border(r, Ui.Accent, 2f);
            Ui.Label(new Rect(r.x + 20, r.y + 10, 400, 22), $"TRAINING  {tutStep + 1}/{TutText.Length}", 14, Ui.Accent, TextAnchor.UpperLeft, true);
            Ui.Label(new Rect(r.xMax - 260, r.y + 10, 240, 22), "[F1] skip training", 14, Ui.Dim, TextAnchor.UpperRight);
            Ui.Label(new Rect(r.x + 20, r.y + 36, w - 40, h - 44), text, 20, Ui.Text);
        }
    }
}
