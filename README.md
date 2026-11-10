[h1]JumpDriveAPI[/h1]

This mod lets Programmable Block scripts control Jump Drives and receive callbacks when things happen.

[hr]

[h2]Quick Start[/h2]

Paste this into a Programmable Block and run it with a GPS argument:

[code]
GPS:MyTarget:50000:70000:30000:
[/code]

[code]
IMyJumpDrive drive;

public Program()
{
    drive = GridTerminalSystem.GetBlockWithName("Jump Drive") as IMyJumpDrive;
    if (drive == null)
    {
        List<IMyJumpDrive> drives = new List<IMyJumpDrive>();
        GridTerminalSystem.GetBlocksOfType(drives);
        if (drives.Count > 0)
            drive = drives[0];
    }
}

public void Main(string argument, UpdateType updateSource)
{
    if (drive == null) { Echo("No jump drive found!"); return; }

    if (argument == "abort")
    {
        drive.ApplyAction("AbortJump");
        Echo(drive.GetValue<string>("ScriptJumpStatus"));
        return;
    }

    Vector3D pos;
    if (TryParseGPS(argument, out pos))
    {
        drive.SetValue<Vector3D?>("ScriptJumpTarget", pos);
        drive.ApplyAction("ScriptJump");

        string status = drive.GetValue<string>("ScriptJumpStatus");
        if (status == "Success")
            Echo("Jumping to " + argument);
        else
            Echo(status);
    }
    else
    {
        Echo("Bad GPS. Use: GPS:Name:X:Y:Z:");
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

[hr]

[h2]Actions[/h2]

Commands you run on the jump drive block.

[h3][c]ScriptJump[/c][/h3]

Jump to the position stored in [c]ScriptJumpTarget[/c].

[code]
drive.SetValue<Vector3D?>("ScriptJumpTarget", targetPos);
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

[h3][c]JumpTarget[/c] — read-only — [c]Vector3D?[/c][/h3]

The actual jump target set by the game's jump system.

[code]
Vector3D? realTarget = drive.GetValue<Vector3D?>("JumpTarget");
[/code]

[h3][c]ScriptJumpStatus[/c] — read-only — [c]string[/c][/h3]

Result of the last [c]ScriptJump[/c] or [c]AbortJump[/c]. Read this right after calling the action.

[table]
[tr][th]Value[/th][th]Meaning[/th][/tr]
[tr][td][c]""[/c] (empty)[/td][td]No action taken yet[/td][/tr]
[tr][td][c]"Success"[/c][/td][td]Jump started[/td][/tr]
[tr][td][c]"Success: Aborted"[/c][/td][td]Jump was cancelled[/td][/tr]
[tr][td][c]"Error: Not server"[/c][/td][td]Script is not running on the server[/td][/tr]
[tr][td][c]"Error: No target"[/c][/td][td][c]ScriptJumpTarget[/c] was not set[/td][/tr]
[tr][td][c]"Error: No jump system"[/c][/td][td]Grid has no jump system[/td][/tr]
[tr][td][c]"Error: Already jumping"[/c][/td][td]A jump is already in progress[/td][/tr]
[tr][td][c]"Error: Not ready"[/c][/td][td]Drive is not charged or damaged[/td][/tr]
[tr][td][c]"Error: No grid"[/c][/td][td]Block has no grid (rare)[/td][/tr]
[tr][td][c]"Error: Immovable or no power"[/c][/td][td]Grid cannot move or no power[/td][/tr]
[tr][td][c]"Error: Inside gravity"[/c][/td][td]Ship or target is in gravity[/td][/tr]
[tr][td][c]"Error: Outside world"[/c][/td][td]Target is past the world border[/td][/tr]
[tr][td][c]"Error: Planet blocks path"[/c][/td][td]A planet is in the way[/td][/tr]
[tr][td][c]"Error: No clear space at target"[/c][/td][td]No empty space to land[/td][/tr]
[tr][td][c]"Error: Too close"[/c][/td][td]Closer than minimum jump distance[/td][/tr]
[tr][td][c]"Error: Too far"[/c][/td][td]Further than maximum jump distance[/td][/tr]
[tr][td][c]"Error: No active jump"[/c][/td][td]Tried to abort but nothing is jumping[/td][/tr]
[/table]

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
[tr][td]8[/td][td][c]JumpAborted[/c][/td][td]Jump is cancelled[/td][td][i](none)[/i][/td][/tr]
[tr][td]16[/td][td][c]JumpFailed[/c][/td][td]Jump is blocked[/td][td][c]{status}[/c] → error message[/td][/tr]
[tr][td]32[/td][td][c]RangeChanged[/c][/td][td]MaxJumpDistance changes[/td][td][c]{range}[/c] → meters[/td][/tr]
[tr][td]64[/td][td][c]RangeAbove[/c][/td][td]Crosses above threshold[/td][td][c]{range}[/c], [c]{threshold}[/c][/td][/tr]
[tr][td]128[/td][td][c]RangeBelow[/c][/td][td]Crosses below threshold[/td][td][c]{range}[/c], [c]{threshold}[/c][/td][/tr]
[tr][td]256[/td][td][c]RangeEquals[/c][/td][td]Matches threshold[/td][td][c]{range}[/c], [c]{threshold}[/c][/td][/tr]
[/table]

[b]Example:[/b] To get [c]JumpStarted[/c] + [c]JumpCompleted[/c] + [c]JumpFailed[/c]:
[code]
2 + 4 + 16 = 22
[/code]

[h3]Tokens in [c]CallbackArg[/c][/h3]

You can put [c]{token}[/c] placeholders in your [c]CallbackArg[/c]. The mod replaces them with real values before calling your PB.

[table]
[tr][th]Token[/th][th]Replaced with[/th][th]Example value[/th][/tr]
[tr][td][c]{target}[/c][/td][td]GPS string of jump target[/td][td][c]GPS::50000:70000:30000:[/c][/td][/tr]
[tr][td][c]{status}[/c][/td][td]Error message[/td][td][c]Error: Too far[/c][/td][/tr]
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

[h2]Important Notes[/h2]

[list]
[*][b]Always null-check your blocks.[/b] Scripts run even if a block was renamed or removed. Check that [c]drive[/c] is not null before calling any methods on it.
[*][b]Server only.[/b] Actions only work when the PB runs on the server. Make sure the block owner is a real player, not "Nobody".
[*][b]Multiple jump drives.[/b] Send commands to any one drive — the whole grid jumps together.
[*][b]Jump rules still apply.[/b] Gravity, min/max distance, and charge requirements are the same as normal.
[*][b]You cannot jump into or out of gravity.[/b]
[*][b]Planets in the way.[/b] The mod tries to shift the target around objects. If that fails, the jump is blocked.
[/list]
