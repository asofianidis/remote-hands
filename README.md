# Remote Hands

A first-person data center technician game, spun off from *Uptime: Data Center Tycoon*.
In data centers, "remote hands" is the on-site service customers pay for when they need someone
to physically rack, cable, swap or reboot their gear. In this game, that someone is you.

Each shift starts with a work order on your tablet: four tickets on day one, one more every day
after (up to twelve). Each ticket names a rack and the hands-on jobs to do there. Collect equipment
from the storage room or the loading dock, walk the hall, and do the work yourself through a
minigame for every task. There is no clock: the shift ends when you clock out.

## Ticket types

| Ticket | What you do |
| --- | --- |
| Capacity expansion | Grab servers from storage, rack them at the given U, cable them |
| Customer install | Customer shipments land on the loading dock; rack and cable their own gear |
| New contract build-out | Build out an empty rack: ToR switch at U42, servers from the bottom up, all cabled |
| Hardware replacement | Hot-swap a failed drive or power supply with a spare from storage |
| Firmware update | Flash BIOS / BMC / NIC firmware on several servers |
| Incident | A server is down: diagnose it from the console, then fix the cause |

## Minigames

- **Rack-mount**: slide a heavy, sagging unit onto the rails at the right U, then land the screws.
- **Cabling**: follow the work order to patch every port to the right switch port and PDU outlet.
- **Reseat**: find the link that is down, unplug it and plug it back in without disturbing healthy links.
- **Hot-swap**: find the faulty drive / PSU / optic, release it, pull it, seat the spare, lock it.
- **Firmware**: choose the exact image for the model, component and version, then keep the
  supply voltage steady while it flashes.
- **Diagnose**: read the kernel log and BMC health to find the root cause.
- **Power-cycle**: hold the power button through a hard reset, then boot from the right device.

Clock out from the tablet (or the NOC desk) when you're done. Anything unfinished is handed to the
night shift and costs reputation, so the goal is to finish the whole work order. Mistakes such as
pulling a healthy drive or plugging into the wrong port cost reputation, and a ticket finished
without a single mistake pays a 20% clean-work bonus.
If your reputation hits zero you're fired. Spend your pay between shifts on tools in the tech shop.

## Controls

| Key | Action |
| --- | --- |
| WASD / Shift | Move / sprint |
| E | Interact (work on a rack, take stock, pick up deliveries) |
| Tab | Ticket queue tablet |
| G | Drop what you're carrying |
| Esc | Step away from a job / pause |

## Development

- Unity **6000.6.9f1**, Universal Render Pipeline. All geometry, UI and audio are generated in code.
- `Assets/Scripts/Sim` holds the scene-independent shift, ticket and scoring logic, covered by
  edit-mode tests in `Assets/Tests/Editor`.
- Menu **Remote Hands > Setup Main Scene** recreates `Assets/Scenes/Main.unity`.
- Menu **Remote Hands > Build Windows Player** builds to `Builds/Windows/RemoteHands.exe`.

Batch-mode equivalents:

```sh
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
Unity -batchmode -quit -projectPath . -executeMethod RH.EditorTools.BuildTools.BuildWindows
```

Run the built player with `-shotdir <folder>` to capture screenshots of every screen and minigame,
then quit.

### Continuous integration

`.github/workflows/tests.yml` runs the edit-mode tests on push. It skips until `UNITY_LICENSE`
(or `UNITY_EMAIL` and `UNITY_PASSWORD`) repository secrets are added.
