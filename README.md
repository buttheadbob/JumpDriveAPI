[h1]JumpDriveAPI[/h1]

This mod lets Programmable Block scripts control Jump Drives and receive callbacks when things happen.

[b]Current version: 1.1.0[/b] — see [b]Versions[/b] below for what the numbers mean and what changed.

[hr]

[h2]Quick Start[/h2]

Paste this into a Programmable Block and run it with a GPS argument:

[code]
GPS:MyTarget:50000:70000:30000:
[/code]

[code]
IMyJumpDrive drive;

public void Main(string argument, UpdateType updateSource)
{
    // Look the drive up again if it is missing, for example after it was rebuilt.
    if (drive == null || drive.Closed)
    {
        // Only blocks on this ship, not on ships docked by a connector.
        List<IMyJumpDrive> drives = new List<IMyJumpDrive>();
        GridTerminalSystem.GetBlocksOfType(drives, b => b.IsSameConstructAs(Me));
        drive = drives.Count > 0 ? drives[0] : null;
    }
    if (drive == null) { Echo("No jump drive on this ship."); return; }

    // Without the mod, setting ScriptJumpTarget would crash the script.
    if (drive.GetProperty("ScriptJumpTarget") == null) { Echo("Jump Drive API mod is not loaded."); return; }

    if (argument.Trim().ToLower() == "abort")
    {
        drive.ApplyAction("AbortJump");
        Echo(drive.GetValue<string>("ScriptJumpStatus"));
        return;
    }

    Vector3D pos;
    if (!TryParseGPS(argument.Trim(), out pos)) { Echo("Run with a GPS point: GPS:Name:X:Y:Z:"); return; }

    drive.SetValue<Vector3D?>("ScriptJumpTarget", pos);   // where to jump
    drive.ApplyAction("ScriptJump");                      // try the jump
    Echo(drive.GetValue<string>("ScriptJumpStatus"));     // "Success" or the reason it failed
}

// GPS:Name:X:Y:Z:Color:  ->  X, Y and Z are parts [2], [3] and [4]
bool TryParseGPS(string gps, out Vector3D pos)
{
    pos = new Vector3D();
    string[] parts = gps.Split(':');
    if (parts.Length < 5 || parts[0] != "GPS") return false;
    double x, y, z;
    if (!double.TryParse(parts[2], out x)) return false;
    if (!double.TryParse(parts[3], out y)) return false;
    if (!double.TryParse(parts[4], out z)) return false;
    pos = new Vector3D(x, y, z);
    return true;
}
[/code]

A longer sample with comments, a check-only mode and a countdown display is [c]TestScript/Program.cs[/c] in the [url=https://github.com/buttheadbob/JumpDriveAPI]GitHub repository[/url].

[hr]

[h2]Versions[/h2]

The mod uses three-part version numbers: [b]Major.Minor.Patch[/b], for example [b]1.1.0[/b]. Each Workshop update raises exactly one of them, and the parts to its right go back to 0.

[table]
[tr][th]Part[/th][th]Raised when[/th][th]What it means for your script[/th][/tr]
[tr][td][b]Major[/b] (1.1.0 → 2.0.0)[/td][td]A change can break existing scripts: something removed, renamed, or working differently[/td][td]Read the version history below before updating your script[/td][/tr]
[tr][td][b]Minor[/b] (1.1.0 → 1.2.0)[/td][td]New features are added and nothing that already exists changes[/td][td]Your script keeps working. You may use the new features if you want[/td][/tr]
[tr][td][b]Patch[/b] (1.1.0 → 1.1.1)[/td][td]Bugs are fixed, with no new features[/td][td]Your script keeps working[/td][/tr]
[/table]

[b]The promise:[/b] a script written for any 1.x version keeps working on every later 1.x version. Only a new major version (2.0.0) can break it, and the version history below will say what changed.

[h3]Checking the version from a script[/h3]

The drive has four read-only properties: [c]Version[/c] ([c]string[/c], such as [c]"1.1.0"[/c]) for showing to players, and [c]VersionMajor[/c], [c]VersionMinor[/c] and [c]VersionPatch[/c] ([c]int[/c]) for comparing.

1.0.0 does not have these properties. A script should treat a missing property as 1.0.0.

[code]
// Needs 1.1.0 or newer, and stops on a future 2.x that may work differently.
int major = drive.GetProperty("VersionMajor") != null ? drive.GetValue<int>("VersionMajor") : 1;
int minor = drive.GetProperty("VersionMinor") != null ? drive.GetValue<int>("VersionMinor") : 0;
if (major != 1 || minor < 1)
{
    Echo("This script needs Jump Drive API 1.1 or a later 1.x version.");
    return;
}
[/code]

Check the minor number for the newest feature your script uses. A script that only uses [c]ScriptJumpTarget[/c], [c]ScriptJump[/c], [c]AbortJump[/c] and [c]JumpTarget[/c] works on every 1.x version, including 1.0.0.

[h3]Version history[/h3]

[b]1.1.0[/b]

Nothing that worked in 1.0.0 has changed, so 1.0.0 scripts keep working.

New:
[list]
[*]Results: [c]ScriptJumpStatus[/c] and [c]ScriptJumpStatusCode[/c] say whether a jump started and, if not, why. In 1.0.0 a failed jump did nothing and said nothing.
[*]Actions: [c]ScriptJumpCheck[/c], [c]ScriptJumpBlind[/c], [c]ScriptJumpReturn[/c], [c]ScriptJumpTargetFromGps[/c].
[*]Settings: [c]ScriptJumpClamp[/c], [c]ScriptJumpDirection[/c], [c]ScriptJumpDistance[/c], [c]ScriptJumpGpsName[/c], [c]ScriptJumpDelay[/c].
[*]Information: [c]MaxJumpDistance[/c], [c]MinJumpDistance[/c], [c]IsJumping[/c], [c]RemainingJumpTime[/c], [c]PredictedJumpTarget[/c], the blocking entity, the last jump, grid charge, recharge time, drive counts, mass limit, gravity escape, and the version properties.
[*]Callbacks: the drive can run your programmable block when something happens.
[*]Settings are saved with the block and survive a world reload.
[*]Server settings file.
[/list]

Fixed:
[list]
[*]Jumps failed in worlds with no world border.
[*]In multiplayer, players on board saw no countdown, sound or effect before a script jump. Script jumps now start the same way as a jump from the cockpit.
[*]In multiplayer, [c]AbortJump[/c] stopped the jump on the server only, so players' screens could stay stuck in the jump effect.
[/list]

[b]1.0.0[/b] — the first Workshop release: [c]ScriptJumpTarget[/c], [c]ScriptJump[/c], [c]AbortJump[/c] and [c]JumpTarget[/c]. It has no version properties.

[hr]

[h2]Actions[/h2]

Commands you run on the jump drive block.

[h3][c]ScriptJump[/c][/h3]

Jump to the position stored in [c]ScriptJumpTarget[/c].

[code]
drive.SetValue<Vector3D?>("ScriptJumpTarget", targetPos);
drive.ApplyAction("ScriptJump");
[/code]

[h3][c]ScriptJumpCheck[/c][/h3]

Runs every check [c]ScriptJump[/c] runs, without jumping. [c]ScriptJumpStatus[/c] then holds [c]"Success: Can jump"[/c] or the reason it would fail, and [c]PredictedJumpTarget[/c] holds where the ship would arrive. Use it to show "can jump / why not" on an LCD.

[code]
drive.SetValue<Vector3D?>("ScriptJumpTarget", targetPos);
drive.ApplyAction("ScriptJumpCheck");
Echo(drive.GetValue<string>("ScriptJumpStatus"));
[/code]

A check searches the path and the space at the target, which costs server time. Running it again within 10 frames (1/6 second) with the same target gives the same result without a new search, so a script that checks every tick does not slow the server.

[h3][c]ScriptJumpBlind[/c][/h3]

Jumps in a direction instead of to a point, like the blind jump from the cockpit. Set [c]ScriptJumpDirection[/c] first, and [c]ScriptJumpDistance[/c] if you want a set distance. With no distance (0), the ship jumps as far as the drives allow.

[code]
// Jump 20 km straight ahead of the cockpit
drive.SetValue<Vector3D?>("ScriptJumpDirection", cockpit.WorldMatrix.Forward);
drive.SetValue<double>("ScriptJumpDistance", 20000);
drive.ApplyAction("ScriptJumpBlind");
[/code]

[h3][c]ScriptJumpReturn[/c][/h3]

Jumps back to where the last completed script jump started ([c]LastJumpOrigin[/c]). Useful for scouting and mining trips. Fails with [c]"Error: No jump to return from"[/c] if this drive has not finished a script jump yet.

[code]
drive.ApplyAction("ScriptJumpReturn");
[/code]

[h3][c]ScriptJumpTargetFromGps[/c][/h3]

Sets [c]ScriptJumpTarget[/c] from a GPS point saved by the drive's owner, so players can type a name instead of pasting coordinates. Set [c]ScriptJumpGpsName[/c] first. An exact name match wins; otherwise the first match ignoring upper and lower case. The status is [c]"Success: Target set"[/c] or [c]"Error: GPS not found"[/c].

[code]
drive.SetValue<string>("ScriptJumpGpsName", "Home");
drive.ApplyAction("ScriptJumpTargetFromGps");
if (drive.GetValue<int>("ScriptJumpStatusCode") == 22)
    drive.ApplyAction("ScriptJump");
[/code]

[h3][c]AbortJump[/c][/h3]

Cancel a jump that is charging or in progress.

[code]
drive.ApplyAction("AbortJump");
[/code]

[hr]

[h2]Properties[/h2]

[h3][c]ScriptJumpTarget[/c] — read / write — [c]Vector3D?[/c][/h3]

The position to jump to. Set this before calling [c]ScriptJump[/c].

[code]
drive.SetValue<Vector3D?>("ScriptJumpTarget", new Vector3D(50000, 70000, 30000));
Vector3D? current = drive.GetValue<Vector3D?>("ScriptJumpTarget");
[/code]

[h3][c]ScriptJumpClamp[/c] — read / write — [c]bool[/c][/h3]

When true, a target past the max range is shortened to the max range, like a jump from the cockpit. When false (the default), the jump fails with [c]"Error: Too far"[/c]. Use it for long trips in several hops.

[code]
drive.SetValue<bool>("ScriptJumpClamp", true);
[/code]

[h3][c]ScriptJumpDirection[/c] — read / write — [c]Vector3D?[/c][/h3]

Direction for [c]ScriptJumpBlind[/c], in world space. It does not need to be normalized.

[h3][c]ScriptJumpDistance[/c] — read / write — [c]double[/c][/h3]

Distance in meters for [c]ScriptJumpBlind[/c]. 0 means as far as possible.

[h3][c]ScriptJumpGpsName[/c] — read / write — [c]string[/c][/h3]

Name of the GPS point for [c]ScriptJumpTargetFromGps[/c].

[h3][c]ScriptJumpDelay[/c] — read / write — [c]float[/c][/h3]

Countdown in seconds before a script jump. 0 uses the game's 10 seconds. The server limits it, by default to between 10 and 60 seconds, so scripts can make the warning longer but not shorter.

[code]
drive.SetValue<float>("ScriptJumpDelay", 30);
[/code]

[h3][c]JumpTarget[/c] — read-only — [c]Vector3D?[/c][/h3]

The actual jump target set by the game's jump system.

[code]
Vector3D? realTarget = drive.GetValue<Vector3D?>("JumpTarget");
[/code]

[h3][c]ScriptJumpStatus[/c] — read-only — [c]string[/c][/h3]

Result of the last action. Read this right after calling the action. Use [c]ScriptJumpStatusCode[/c] in code; the text is for showing to players and may change.

[table]
[tr][th]Code[/th][th]Text[/th][th]Meaning[/th][/tr]
[tr][td]0[/td][td][c]""[/c] (empty)[/td][td]No action taken yet[/td][/tr]
[tr][td]1[/td][td][c]"Success"[/c][/td][td]Jump started[/td][/tr]
[tr][td]2[/td][td][c]"Success: Aborted"[/c][/td][td]Jump was cancelled[/td][/tr]
[tr][td]3[/td][td][c]"Error: Not server"[/c][/td][td]Script is not running on the server[/td][/tr]
[tr][td]4[/td][td][c]"Error: No target"[/c][/td][td][c]ScriptJumpTarget[/c] was not set[/td][/tr]
[tr][td]5[/td][td][c]"Error: No jump system"[/c][/td][td]Grid has no jump system[/td][/tr]
[tr][td]6[/td][td][c]"Error: Already jumping"[/c][/td][td]A jump is already in progress[/td][/tr]
[tr][td]7[/td][td][c]"Error: Not ready"[/c][/td][td]Drive is not charged or damaged[/td][/tr]
[tr][td]8[/td][td][c]"Error: No grid"[/c][/td][td]Block has no grid (rare)[/td][/tr]
[tr][td]9[/td][td][c]"Error: Immovable or no power"[/c][/td][td]Grid cannot move or no power[/td][/tr]
[tr][td]10[/td][td][c]"Error: Inside gravity"[/c][/td][td]Ship or target is in gravity[/td][/tr]
[tr][td]11[/td][td][c]"Error: Outside world"[/c][/td][td]Target is past the world border[/td][/tr]
[tr][td]12[/td][td][c]"Error: Planet blocks path"[/c][/td][td]A planet is in the way[/td][/tr]
[tr][td]13[/td][td][c]"Error: No clear space at target"[/c][/td][td]No empty space to land[/td][/tr]
[tr][td]14[/td][td][c]"Error: Too close"[/c][/td][td]Closer than minimum jump distance[/td][/tr]
[tr][td]15[/td][td][c]"Error: Too far"[/c][/td][td]Further than maximum jump distance[/td][/tr]
[tr][td]16[/td][td][c]"Error: No active jump"[/c][/td][td]Tried to abort but nothing is jumping[/td][/tr]
[tr][td]17[/td][td][c]"Success: Can jump"[/c][/td][td][c]ScriptJumpCheck[/c] passed[/td][/tr]
[tr][td]18[/td][td][c]"Error: No direction"[/c][/td][td][c]ScriptJumpDirection[/c] was not set[/td][/tr]
[tr][td]19[/td][td][c]"Error: Too many jumps, wait"[/c][/td][td]The server's jumps-per-minute limit was reached[/td][/tr]
[tr][td]20[/td][td][c]"Error: Disabled by server"[/c][/td][td]The server turned this feature off[/td][/tr]
[tr][td]21[/td][td][c]"Error: GPS not found"[/c][/td][td]No GPS point with that name[/td][/tr]
[tr][td]22[/td][td][c]"Success: Target set"[/c][/td][td][c]ScriptJumpTargetFromGps[/c] set the target[/td][/tr]
[tr][td]23[/td][td][c]"Error: No jump to return from"[/c][/td][td][c]ScriptJumpReturn[/c] has no earlier jump[/td][/tr]
[tr][td]24[/td][td][c]"Error: The game refused the jump"[/c][/td][td]The mod's checks passed but the game's own check, just before the countdown, did not[/td][/tr]
[/table]

[h3][c]ScriptJumpStatusCode[/c] — read-only — [c]int[/c][/h3]

The same result as a number, from the table above.

[code]
if (drive.GetValue<int>("ScriptJumpStatusCode") == 15)
    Echo("Too far, try a closer target.");
[/code]

[h3][c]PredictedJumpTarget[/c] — read-only — [c]Vector3D?[/c][/h3]

Where the ship will arrive after the last [c]ScriptJump[/c], [c]ScriptJumpBlind[/c] or [c]ScriptJumpCheck[/c]. The mod moves the target around obstacles and into clear space, so it can differ from [c]ScriptJumpTarget[/c]. Null when the last action failed.

[code]
Vector3D? landing = drive.GetValue<Vector3D?>("PredictedJumpTarget");
[/code]

[h3][c]BlockingEntityType[/c], [c]BlockingEntityName[/c], [c]BlockingEntityPosition[/c] — read-only[/h3]

What is in the path of the last checked jump. The type is [c]"Planet"[/c], [c]"Asteroid"[/c], [c]"Grid"[/c], [c]"Other"[/c], or empty when nothing was in the way. The name ([c]string[/c]) and position ([c]Vector3D?[/c]) are given for planets, asteroids, and ships owned by the drive's owner or their faction. For other ships only the type is given, so jump checks cannot be used to find hidden ships.

[code]
drive.ApplyAction("ScriptJumpCheck");
string type = drive.GetValue<string>("BlockingEntityType");
if (type != "")
    Echo("In the way: " + type + " " + drive.GetValue<string>("BlockingEntityName"));
[/code]

[h3][c]LastJumpOrigin[/c] and [c]LastJumpDestination[/c] — read-only — [c]Vector3D?[/c][/h3]

Where the last completed script jump started and ended. Null until this drive has finished one. Saved with the block.

[h3][c]MaxJumpDistance[/c] — read-only — [c]double[/c][/h3]

The grid's combined maximum jump range (in meters). This accounts for all jump drives and current grid mass — it changes when you mine, dock, or add/remove blocks.

[code]
double maxRange = drive.GetValue<double>("MaxJumpDistance");
Echo("Max range: " + maxRange.ToString("N0") + "m");
[/code]

[h3][c]MinJumpDistance[/c] — read-only — [c]double[/c][/h3]

The grid's combined minimum jump distance (in meters).

[code]
double minRange = drive.GetValue<double>("MinJumpDistance");
[/code]

[h3][c]IsJumping[/c] — read-only — [c]bool[/c][/h3]

True while the grid is charging or performing a jump.

[code]
bool jumping = drive.GetValue<bool>("IsJumping");
[/code]

[h3][c]RemainingJumpTime[/c] — read-only — [c]float[/c][/h3]

Seconds left before the jump happens. Returns 0 if not jumping.

[code]
float seconds = drive.GetValue<float>("RemainingJumpTime");
Echo(seconds + "s until jump");
[/code]

[h3][c]GridStoredPower[/c] and [c]GridMaxStoredPower[/c] — read-only — [c]float[/c][/h3]

Stored and maximum charge of all working jump drives on the grid, in MWh.

[code]
float stored = drive.GetValue<float>("GridStoredPower");
float max = drive.GetValue<float>("GridMaxStoredPower");
Echo("Charge: " + (max > 0 ? stored / max * 100 : 0).ToString("0") + "%");
[/code]

[h3][c]RechargeTimeLeft[/c] — read-only — [c]double[/c][/h3]

Estimated seconds until every drive on the grid is full. 0 when full, -1 when unknown or not charging. The mod measures the charge rate only while a script reads this, so the first reading is -1 and a number appears about 3 seconds later.

[code]
double seconds = drive.GetValue<double>("RechargeTimeLeft");
if (seconds > 0)
    Echo("Ready in " + (seconds / 60).ToString("0") + " min");
[/code]

[h3][c]ReadyDriveCount[/c] and [c]TotalDriveCount[/c] — read-only — [c]int[/c][/h3]

How many drives on the grid can take part in a jump now (working, charged and usable by the owner), and how many there are in total. A ready count below the total means a drive is charging, damaged, off or not shared.

[h3][c]MaxMassForTarget[/c] — read-only — [c]double[/c][/h3]

The heaviest the ship can be, in kg, and still reach [c]ScriptJumpTarget[/c] in one jump with every working drive charged. 0 means the target is too far at any mass. Compare it with the ship's mass from a cockpit or remote control:

[code]
double maxMass = drive.GetValue<double>("MaxMassForTarget");
double mass = cockpit.CalculateShipMass().PhysicalMass;
if (mass > maxMass)
    Echo("Drop " + ((mass - maxMass) / 1000).ToString("0") + " t of cargo to make this jump.");
[/code]

[h3][c]GravityEscapeDistance[/c] — read-only — [c]double[/c][/h3]
[h3][c]GravityEscapeDirection[/c] — read-only — [c]Vector3D?[/c][/h3]

How far, in meters, the ship must fly to leave the nearest planet's gravity, and which way (straight up from the planet's center). 0 and null when the ship is not in gravity. Only the nearest planet is checked.

[h3][c]Version[/c] — read-only — [c]string[/c][/h3]
[h3][c]VersionMajor[/c], [c]VersionMinor[/c], [c]VersionPatch[/c] — read-only — [c]int[/c][/h3]

The installed mod's version as text, such as [c]"1.1.0"[/c], and its three parts as numbers. See [b]Versions[/b] near the top of this page for how to use them.

[hr]

[h2]Callbacks (advanced)[/h2]

Callbacks let the jump drive notify your Programmable Block when events happen. Your PB does not need to poll (repeatedly check) — the jump drive calls [c]Main()[/c] for you with your chosen argument.

[h3]How to set up a callback[/h3]

There are three properties you need to set:

[table]
[tr][th]Property[/th][th]Type[/th][th]What it does[/th][/tr]
[tr][td][c]CallbackPB[/c][/td][td][c]long[/c][/td][td]Entity ID of your Programmable Block[/td][/tr]
[tr][td][c]CallbackEvents[/c][/td][td][c]long[/c][/td][td]Which events you want (add the numbers)[/td][/tr]
[tr][td][c]CallbackArg[/c][/td][td][c]string[/c][/td][td]Argument passed to your PB, can contain [c]{tokens}[/c][/td][/tr]
[/table]

The callback block must be on the same ship as the jump drive and not owned by an enemy. Server admins can also allow blocks on other ships with the same owner or faction (see Server settings).

Callbacks run on the frame after the event, so they also work when the PB that started the jump is the callback block.

Set them once in your PB's constructor:

[code]
public Program()
{
    drive = GridTerminalSystem.GetBlockWithName("Jump Drive") as IMyJumpDrive;
    if (drive == null)
    {
        Echo("Error: No jump drive named 'Jump Drive' on this grid!");
        return;
    }

    // Tell the drive which PB to call
    drive.SetValue<long>("CallbackPB", Me.EntityId);

    // Subscribe to events (add the numbers you want)
    drive.SetValue<long>("CallbackEvents", 1 + 2 + 32);

    // The argument your Main() will receive
    drive.SetValue<string>("CallbackArg", "jump_event {target}");
}
[/code]

[h3]Event list[/h3]

Add these numbers together to subscribe:

[table]
[tr][th]#[/th][th]Event[/th][th]Fires when...[/th][th]Tokens[/th][/tr]
[tr][td]1[/td][td][c]Recharged[/c][/td][td]Drive reaches 100% charge[/td][td][c]{charge}[/c] → [c]"100"[/c][/td][/tr]
[tr][td]2[/td][td][c]JumpStarted[/c][/td][td]Jump begins[/td][td][c]{target}[/c] → GPS string[/td][/tr]
[tr][td]4[/td][td][c]JumpCompleted[/c][/td][td]Grid arrives at destination[/td][td][c]{target}[/c] → GPS string[/td][/tr]
[tr][td]8[/td][td][c]JumpAborted[/c][/td][td]Jump is cancelled[/td][td][c]{reason}[/c] → see below[/td][/tr]
[tr][td]16[/td][td][c]JumpFailed[/c][/td][td]Jump is blocked[/td][td][c]{status}[/c] → error message, [c]{code}[/c] → status code[/td][/tr]
[tr][td]32[/td][td][c]RangeChanged[/c][/td][td]MaxJumpDistance changes (see below)[/td][td][c]{range}[/c] → meters[/td][/tr]
[tr][td]64[/td][td][c]RangeAbove[/c][/td][td]Crosses above threshold[/td][td][c]{range}[/c], [c]{threshold}[/c][/td][/tr]
[tr][td]128[/td][td][c]RangeBelow[/c][/td][td]Crosses below threshold[/td][td][c]{range}[/c], [c]{threshold}[/c][/td][/tr]
[tr][td]256[/td][td][c]RangeEquals[/c][/td][td]Matches threshold[/td][td][c]{range}[/c], [c]{threshold}[/c][/td][/tr]
[tr][td]512[/td][td][c]GridRecharged[/c][/td][td]All drives on the grid reach 100%[/td][td][c]{charge}[/c] → [c]"100"[/c][/td][/tr]
[tr][td]1024[/td][td][c]JumpCountdown[/c][/td][td]Each second of the countdown[/td][td][c]{seconds}[/c] → seconds left[/td][/tr]
[/table]

[c]JumpCompleted[/c] and [c]JumpCountdown[/c] are reported only for jumps started by a script action ([c]ScriptJump[/c], [c]ScriptJumpBlind[/c], [c]ScriptJumpReturn[/c]). The [c]{reason}[/c] of [c]JumpAborted[/c] is one of:

[table]
[tr][th]Reason[/th][th]Meaning[/th][/tr]
[tr][td][c]Script[/c][/td][td]A script ran [c]AbortJump[/c][/td][/tr]
[tr][td][c]NoDrives[/c][/td][td]No drive could jump any more during the countdown: power lost, drive damaged, turned off or access lost[/td][/tr]
[tr][td][c]Blocked[/c][/td][td]The game's last check at the end of the countdown failed: no clear space at the target, out of range, or the ship became static or locked[/td][/tr]
[tr][td][c]Cancelled[/c][/td][td]Anything else, for example aborted from the cockpit[/td][/tr]
[/table]

The game does not tell mods why it cancelled a jump, so the mod works the reason out from what it can see.

[b]Example:[/b] To get [c]JumpStarted[/c] + [c]JumpCompleted[/c] + [c]JumpFailed[/c]:
[code]
2 + 4 + 16 = 22
[/code]

[h3]Tokens in [c]CallbackArg[/c][/h3]

You can put [c]{token}[/c] placeholders in your [c]CallbackArg[/c]. The mod replaces them with real values before calling your PB.

[table]
[tr][th]Token[/th][th]Replaced with[/th][th]Example value[/th][/tr]
[tr][td][c]{target}[/c][/td][td]GPS string: where the ship will arrive (JumpStarted) or arrived (JumpCompleted)[/td][td][c]GPS::50000:70000:30000:[/c][/td][/tr]
[tr][td][c]{status}[/c][/td][td]Error message[/td][td][c]Error: Too far[/c][/td][/tr]
[tr][td][c]{code}[/c][/td][td]Status code[/td][td][c]15[/c][/td][/tr]
[tr][td][c]{reason}[/c][/td][td]Why the jump was aborted[/td][td][c]NoDrives[/c][/td][/tr]
[tr][td][c]{seconds}[/c][/td][td]Seconds left in the countdown[/td][td][c]7[/c][/td][/tr]
[tr][td][c]{range}[/c][/td][td]Current max jump range[/td][td][c]15000.5[/c][/td][/tr]
[tr][td][c]{threshold}[/c][/td][td]Your [c]CallbackRangeThreshold[/c][/td][td][c]10000[/c][/td][/tr]
[tr][td][c]{charge}[/c][/td][td]Charge percentage[/td][td][c]100[/c][/td][/tr]
[/table]

[b]Example template:[/b] [c]"range_update {range} threshold {threshold}"[/c]
[b]PB receives:[/b] [c]"range_update 15000.5 threshold 10000"[/c]

Tokens you do not use are simply ignored — no error.

[h3]Range threshold callbacks[/h3]

[c]RangeAbove[/c], [c]RangeBelow[/c], and [c]RangeEquals[/c] need a threshold value. Set it with [c]CallbackRangeThreshold[/c]:

[code]
// Fire callbacks when max range crosses 5000m
drive.SetValue<double>("CallbackRangeThreshold", 5000);
drive.SetValue<long>("CallbackEvents", 64);  // RangeAbove only
[/code]

These fire only on [b]edge crossing[/b] — not every frame. [c]RangeAbove[/c] fires once when range goes from ≤ threshold to > threshold. [c]RangeBelow[/c] fires once when it drops below. [c]RangeEquals[/c] fires when it lands exactly on the threshold (±small tolerance).

[h3]How often RangeChanged fires[/h3]

Mining or refining changes the ship's mass all the time, so [c]RangeChanged[/c] is limited. It fires only when the range has moved by at least [c]CallbackRangeStep[/c] meters ([c]double[/c], read / write) since the last call, and not more often than the server allows. The server default is at least 10 m and at least 1 second apart.

[code]
drive.SetValue<double>("CallbackRangeStep", 1000);  // only tell me about 1 km changes
[/code]

[h3]Complete callback example[/h3]

This script attaches to a jump drive and logs every event to the PB's detail area:

[code]
IMyJumpDrive drive;

public Program()
{
    drive = GridTerminalSystem.GetBlockWithName("Jump Drive") as IMyJumpDrive;
    if (drive == null)
    {
        Echo("Error: No jump drive named 'Jump Drive' on this grid!");
        return;
    }

    // Register for all jump events
    drive.SetValue<long>("CallbackPB", Me.EntityId);
    drive.SetValue<long>("CallbackEvents", 1 + 2 + 4 + 8 + 16);

    // Use different arguments so Main() knows which event fired
    // (we do it differently below with one generic argument)
    drive.SetValue<string>("CallbackArg", "event");
}

public void Main(string argument, UpdateType updateSource)
{
    if (drive == null) return;

    if (argument == "event")
    {
        string status = drive.GetValue<string>("ScriptJumpStatus");
        bool jumping = drive.GetValue<bool>("IsJumping");
        double range = drive.GetValue<double>("MaxJumpDistance");

        Echo("Event received!");
        Echo("  Status: " + status);
        Echo("  IsJumping: " + jumping);
        Echo("  MaxRange: " + range.ToString("N0") + "m");
    }
    else
    {
        // Normal run — parse GPS and jump
        Vector3D pos;
        if (TryParseGPS(argument, out pos))
        {
            drive.SetValue<Vector3D?>("ScriptJumpTarget", pos);
            drive.ApplyAction("ScriptJump");
        }
    }
}

bool TryParseGPS(string gps, out Vector3D pos)
{
    pos = new Vector3D();
    string[] parts = gps.Split(':');
    if (parts.Length < 5 || parts[0] != "GPS") return false;
    double x, y, z;
    if (!double.TryParse(parts[2], out x)) return false;
    if (!double.TryParse(parts[3], out y)) return false;
    if (!double.TryParse(parts[4], out z)) return false;
    pos = new Vector3D(x, y, z);
    return true;
}
[/code]

[h3]Range watcher example[/h3]

This example locks a button panel light red when max jump range drops below 5000m (too heavy):

[code]
IMyJumpDrive drive;
IMyLightingBlock light;

public Program()
{
    drive = GridTerminalSystem.GetBlockWithName("Jump Drive") as IMyJumpDrive;
    if (drive == null)
    {
        Echo("Error: No jump drive named 'Jump Drive' on this grid!");
        return;
    }

    light = GridTerminalSystem.GetBlockWithName("Range Light") as IMyLightingBlock;

    drive.SetValue<long>("CallbackPB", Me.EntityId);
    drive.SetValue<long>("CallbackEvents", 128 + 64);  // RangeBelow + RangeAbove
    drive.SetValue<double>("CallbackRangeThreshold", 5000);
    drive.SetValue<string>("CallbackArg", "range_check {range} {threshold}");
}

public void Main(string argument, UpdateType updateSource)
{
    if (drive == null) return;

    if (argument.StartsWith("range_check"))
    {
        double range = drive.GetValue<double>("MaxJumpDistance");
        if (light != null)
        {
            light.SetValue<Color>("Color", range < 5000 ? Color.Red : Color.Green);
        }
    }
}
[/code]

[hr]

[h2]Callback Cleanup[/h2]

[b]To stop callbacks:[/b] set [c]CallbackEvents[/c] to [c]0[/c] or [c]CallbackPB[/c] to [c]0[/c].

The mod also automatically cleans up. If the PB is destroyed or the jump drive
is removed, callbacks stop.

[hr]

[h2]Saved settings[/h2]

The mod saves these with the block, so they survive a world reload: [c]ScriptJumpTarget[/c], [c]ScriptJumpClamp[/c], [c]ScriptJumpDirection[/c], [c]ScriptJumpDistance[/c], [c]ScriptJumpGpsName[/c], [c]ScriptJumpDelay[/c], the last jump's origin and destination, and all [c]Callback...[/c] settings. The last status, [c]PredictedJumpTarget[/c] and the blocking entity are not saved.

[hr]

[h2]Server settings[/h2]

Each time the world loads, the mod reads [c]JumpDriveAPI.xml[/c] from the world's [c]Storage[/c] folder, in this mod's subfolder, and writes it back with any new settings added. Edit it while the server is stopped.

[table]
[tr][th]Setting[/th][th]Default[/th][th]What it does[/th][/tr]
[tr][td][c]CallbacksEnabled[/c][/td][td][c]true[/c][/td][td]When false, no callback runs a PB[/td][/tr]
[tr][td][c]AllowCallbacksToOtherGrids[/c][/td][td][c]false[/c][/td][td]When true, the callback PB may be on another ship with the same owner or a shared faction[/td][/tr]
[tr][td][c]MinRangeChangeMeters[/c][/td][td][c]10[/c][/td][td]Smallest range change that fires [c]RangeChanged[/c][/td][/tr]
[tr][td][c]MinRangeChangeIntervalSeconds[/c][/td][td][c]1[/c][/td][td]Shortest time between two [c]RangeChanged[/c] calls from one drive[/td][/tr]
[tr][td][c]MaxScriptJumpsPerMinute[/c][/td][td][c]0[/c][/td][td]Most script jumps one ship may start per minute. 0 means no limit[/td][/tr]
[tr][td][c]MinJumpDelaySeconds[/c][/td][td][c]10[/c][/td][td]Shortest countdown a script may set (at least 1)[/td][/tr]
[tr][td][c]MaxJumpDelaySeconds[/c][/td][td][c]60[/c][/td][td]Longest countdown a script may set[/td][/tr]
[tr][td][c]AllowBlindJump[/c][/td][td][c]true[/c][/td][td]When false, [c]ScriptJumpBlind[/c] is off[/td][/tr]
[tr][td][c]AllowClampToMaxRange[/c][/td][td][c]true[/c][/td][td]When false, [c]ScriptJumpClamp[/c] is ignored: a target that is too far fails with [c]"Error: Too far"[/c][/td][/tr]
[tr][td][c]AllowGpsTargets[/c][/td][td][c]true[/c][/td][td]When false, [c]ScriptJumpTargetFromGps[/c] is off[/td][/tr]
[/table]

[hr]

[h2]Important Notes[/h2]

[list]
[*][b]Always null-check your blocks.[/b] Scripts run even if a block was renamed or removed. Check that [c]drive[/c] is not null or [c]Closed[/c] before calling any methods on it.
[*][b]Check that the mod is loaded.[/b] Without it, [c]SetValue("ScriptJumpTarget", ...)[/c] throws and stops the script. Check [c]drive.GetProperty("ScriptJumpTarget") != null[/c] first.
[*][b]Use drives on your own ship.[/b] [c]GetBlocksOfType[/c] also finds blocks on ships docked by connector. Filter with [c]b.IsSameConstructAs(Me)[/c].
[*][b]Server only.[/b] Actions only work when the PB runs on the server. Make sure the block owner is a real player, not "Nobody".
[*][b]Multiple jump drives.[/b] Send commands to any one drive — the whole grid jumps together.
[*][b]Jump rules still apply.[/b] Gravity, min/max distance, and charge requirements are the same as normal.
[*][b]You cannot jump into or out of gravity.[/b]
[*][b]Planets in the way.[/b] The mod tries to shift the target around objects. If that fails, the jump is blocked.
[/list]
