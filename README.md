# Remote Hands

A first-person data center technician game, spun off from *Uptime: Data Center Tycoon*.
In data centers, "remote hands" is the on-site service customers pay for when they need someone
to physically rack, cable, swap or reboot their gear. In this game, that someone is you.

Each shift starts with a work order on your tablet: four tickets on day one, one more every day
after (up to twelve). Each ticket names a rack and the hands-on jobs to do there. Collect equipment
from the storage room or the loading dock, walk the hall, and do the work yourself through a
minigame for every task. There is no clock: the shift ends when you clock out.

The data hall persists across your career: servers you rack stay racked, decommissioned gear stays
gone, new contracts claim vacant racks, and now and then a customer leaves and frees one up. Old
hardware fails more often than new hardware.

## Ticket types

| Ticket | What you do |
| --- | --- |
| Capacity expansion | Grab servers from storage, rack them at the given U, cable them |
| Customer install | Customer shipments land on the loading dock; rack and cable their own gear |
| New contract build-out | Build out an empty rack: ToR switch at U42, servers from the bottom up, all cabled |
| Hardware replacement | Hot-swap a failed drive or power supply with a spare from storage |
| Firmware update | Flash BIOS / BMC / NIC firmware on several servers |
| Incident | A server is down: diagnose it from the console, then fix the cause (some need the crash cart) |
| Smart hands | Read a serial number or asset tag to the customer, or send a photo of a rack |
| Decommission | Unplug a unit, de-rack it, wipe and shred its drives at the bench, ship it from the dock |
| PDU rebalancing | Restore A+B power redundancy without dropping any unit (Electrical Safety cert) |
| Cross-connect | Trace a customer's cable, then patch it to a carrier in the meet-me room (Fiber cert) |

Incidents are **P1**: close one before any lower-priority ticket and it pays 25% more. Urgent
incidents and surprise deliveries can also land in the middle of a shift.

Power, cabling, optics and PDU work are done from the **rear** of the rack, in the hot aisle.
Rack-mounting, drives, memory, fans and consoles are done from the front.

## Minigames

- **Rack-mount / de-rack**: slide a heavy, sagging unit onto the rails at the right U and land the
  screws, or back the screws out and ease it off the rails.
- **Cabling**: follow the work order to patch every port to the right switch port and PDU outlet.
- **Unplug**: pull every cable of a unit being decommissioned, and none of the neighbours'.
- **Reseat**: find the link that is down, unplug it and plug it back in without disturbing healthy links.
- **Hot-swap**: find the faulty drive / PSU / optic / DIMM / fan, release it, pull it, seat the spare, lock it.
- **Firmware**: choose the exact image for the model, component and version, then keep the
  supply voltage steady while it flashes.
- **Diagnose**: read the kernel log and BMC health to find the root cause. Every fault shows up in
  two places, and some scary-looking log lines are harmless.
- **Power-cycle / power on**: hold the power button through a hard reset (or tap it), then boot from the right device.
- **Smart hands**: count U on the rail to find the right unit and read its label, watching for near misses.
- **Trace**: follow one fiber through the tangle to its patch panel port.
- **Patch**: in the meet-me room, patch the right customer circuit to the right carrier port.
- **PDU rebalancing**: move power cords between feeds A and B without pulling a unit's only live cord.
- **Wipe & shred**: pull each drive, scan it into the destruction log, then shred it.
- **RAID rebuild**: clear a reinserted drive's foreign config and add it back as a hot spare.

Clock out from the tablet (or the NOC desk) when you're done. Anything unfinished is handed to the
night shift, who finish it, but it costs reputation and the customer's goodwill. Mistakes such as
pulling a healthy drive or plugging into the wrong port cost reputation, and a ticket finished
without a single mistake pays a 20% clean-work bonus. Some mistakes also break something: pulling a
live PSU or the wrong drive opens a P1 follow-up ticket that pays nothing until you fix it.
If your reputation hits zero you're fired on the spot.

## Progression

- **Levels**: tickets earn XP (more for clean work and P1s closed first). Each level raises your pay
  rate and unlocks better gear, from Trainee up to Site Lead.
- **Customers**: each customer's mood rises with clean work and drops with mistakes and handed-over
  tickets. Happy customers pay up to 15% more; unhappy ones up to 10% less. See the tablet.
- **Tech shop**: spend your pay between shifts on tools (with second tiers at higher levels) and on
  certifications that unlock new ticket types.

Day one runs a short guided tutorial (F1 skips it). You can save and quit in the middle of a shift
from the pause menu and resume it from the main menu; the game also autosaves after every ticket.

## Controls

| Key | Action |
| --- | --- |
| WASD / Shift | Move / sprint |
| E | Interact (work on a rack, take stock, pick up deliveries) |
| Tab | Ticket queue tablet |
| F | Raise the smart-hands camera (click to take a photo) |
| G | Drop what you're carrying |
| Esc | Step away from a job / pause |

Every key except Esc can be rebound in Settings, which also has field of view and graphics quality.

## Development

- Unity **6000.6.9f1**, Universal Render Pipeline. All geometry, UI and audio are generated in code.
- `Assets/Scripts/Sim` holds the scene-independent shift, ticket, hall and scoring logic, covered by
  edit-mode tests in `Assets/Tests/Editor`. Saves are `career.json` (career and data hall) and
  `shift.json` (a shift in progress) in `Application.persistentDataPath`.
- Menu **Remote Hands > Setup Main Scene** recreates `Assets/Scenes/Main.unity`.
- Menu **Remote Hands > Build Windows Player** builds to `Builds/Windows/RemoteHands.exe`.

Batch-mode equivalents:

```sh
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
Unity -batchmode -quit -projectPath . -executeMethod RH.EditorTools.BuildTools.BuildWindows
```

Run the built player with `-shotdir <folder>` to capture screenshots of every screen and minigame,
then quit. It also checks photo framing from the front and rear of a rack and logs the result
(`[shot] ... accepted`).

### Continuous integration

`.github/workflows/tests.yml` runs the edit-mode tests on push. It skips until `UNITY_LICENSE`
(or `UNITY_EMAIL` and `UNITY_PASSWORD`) repository secrets are added.
