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
| Receiving | Check an inbound pallet of spares against its packing list; refuse crushed, tripped or misaddressed cartons |
| Visitor escort | Check a customer engineer's ID at the security kiosk, walk them to their rack, stay with them, walk them out |
| Switch outage | A top-of-rack switch is down and the whole rack with it: diagnose it, then swap an uplink optic, reboot it or restore its config |
| Storage service | Swap the disk shelf drive the controller lit up blue (predictive failure), or replace a degraded SAS path |
| Cooling | A rack runs hot: fit blanking panels, rearrange the cold aisle's perforated tiles, or swap a CRAC unit's clogged filters |
| Cable audit | Dress a messy rack's cables into the cable managers on their own side and label them |
| Preventive maintenance | Optional jobs offered on the tablet: read BMC sensors and flag what's drifting. Maintained racks fail less |
| Facility emergency | Rare: start the generator before the UPS runs dry, isolate a chilled-water leak, or deal with a smoking PSU |

Incidents, switch outages and emergencies are **P1**: close one before any lower-priority ticket and
it pays 25% more. Urgent incidents, surprise deliveries and (from day 7, rarely) facility emergencies
can also land in the middle of a shift. A utility power failure is the one thing with a clock: the
UPS batteries last about two and a half minutes.

Power, cabling, optics, SAS and PDU work are done from the **rear** of the rack, in the hot aisle,
which is dim unless you've bought a head torch. Rack-mounting, drives, memory, fans, blanking panels
and consoles are done from the front.

## Minigames

Every job happens in the world. Physical jobs move the camera in close to the real rack, bench or
meet-me room frame and build the parts you work on in 3D: drive sleds and latches, rack screws,
connectors and cables, PSUs, DIMMs, fans, power cords on the PDU strips. For memory and fans the
server slides out on its rails with the lid off. Console jobs (diagnosis, firmware, RAID, power-on)
roll a crash cart up to the rack and run on its monitor.

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
- **Config restore**: pick the right backup of a switch's config off the TFTP server, load it, check
  the ports and save it before anything reboots.
- **Health check**: flag every BMC sensor reading outside its limits (close to a limit is fine).
- **SAS path**: unplug only the degraded path's cable, then fit a new one from the shelf's IOM to the
  matching HBA port.
- **Blanking panels**: seal every open U slot; hold the right mouse button for the thermal camera.
- **Floor tiles**: move perforated tiles in front of the racks that need them until every inlet reads 27°C or less.
- **CRAC filters**: swap the clogged filters (only those) and reset the alarm.
- **Valves**: close the leaking row's supply and return valves on the chilled-water manifold, and nothing else.
- **Generator**: clear the fault that stopped the start, crank until it fires, wait for 60 Hz, transfer the load.
- **Cable tidy**: dress each cable into the manager on its own side, then label it with its port.
- **Receiving**: read each carton's label and look at the box itself, then accept or refuse it.
- **Badge check**: compare the visitor's ID with the customer's access list: name, employee ID,
  company and expiry.

Clock out from the tablet (or the NOC desk) when you're done. Anything unfinished is handed to the
night shift, who finish it, but it costs reputation and the customer's goodwill. Mistakes such as
pulling a healthy drive or plugging into the wrong port cost reputation, and a ticket finished
without a single mistake pays a 20% clean-work bonus. Some mistakes also break something: pulling a
live PSU or the wrong drive opens a P1 follow-up ticket that pays nothing until you fix it.
If your reputation hits zero you're fired on the spot.

## Progression

- **Levels**: tickets earn XP (more for clean work and P1s closed first). Each level raises your pay
  rate and unlocks better gear, from Trainee up to Site Lead. At Lead Technician the expansion cages
  open (two empty racks at the end of every row); at Site Lead you get a trainee who takes simple
  tickets for 70% of the pay, and sometimes leaves you a mess to clean up.
- **Customers**: each customer's mood rises with clean work and drops with mistakes and handed-over
  tickets. Happy customers pay up to 15% more; unhappy ones up to 10% less. Each one also has a
  personality: the bank wants a photo for every change, the games studio thinks everything is urgent,
  the health company's auditors read the destruction log, the AI company ships 2.4 kW GPU servers...
  See the tablet.
- **Spare parts**: the storage room's spares run out. Order more from the tablet's STOCK page and
  they arrive as a receiving ticket the next morning. Failed parts come out in RMA bags that take
  pouch space until you drop them in the returns bin for half the part's price back.
- **Weekly review**: every seven shifts the site manager checks three goals (P1s closed first, clean
  tickets, nothing handed over...) and pays a bonus for each one met.
- **Record**: lifetime stats and achievements on the tablet.
- **Tech shop**: spend your pay between shifts on tools (with second tiers at higher levels) and on
  certifications that unlock new ticket types. Also: an ESD wrist strap (no more static-killed DIMMs),
  a label printer (cable audits label themselves) and a head torch (for the hot aisle).

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
| Right mouse (blanking job) | Thermal camera |
| Esc | Step away from a job / pause |

Every key except Esc can be rebound in Settings, which also has field of view and graphics quality.

## Development

- Unity **6000.6.9f1**, Universal Render Pipeline. All geometry, UI and audio are generated in code.
- `Assets/Scripts/Sim` holds the scene-independent shift, ticket, hall and scoring logic, covered by
  edit-mode tests in `Assets/Tests/Editor`. Saves are `career.json` (career and data hall) and
  `shift.json` (a shift in progress) in `Application.persistentDataPath`.
- 3D models (racks, every rack-mount unit, swap parts, shelving, carts, furniture, the meet-me
  frame, the CRAC units, the plant wall, the visitor kiosk, the dock door...) are generated in Blender by `Tools/blender/build_models.py` and exported
  as FBX to `Assets/Resources/Models`. Their material slots are named (`body`, `trim`, `screen`...)
  and swapped for the game's shaders at runtime by `Assets/Scripts/World/Models.cs`. To regenerate
  (Blender 5.x), then check them on contact sheets:

  ```sh
  blender --background --factory-startup --python Tools/blender/build_models.py -- Assets/Resources/Models
  blender --background --factory-startup --python Tools/blender/preview.py -- Assets/Resources/Models <out_dir>
  ```

  Drive bay, PSU and optic positions in the models match the gameplay code (`Catalog.Bay` and the
  swap game), so change both together.
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
