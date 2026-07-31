# JumpDriveAPI

This mod lets you control Jump Drives from a Programmable Block script. Normally, scripts cannot use jump drives. This mod adds new commands that scripts can use.

---

## How It Works

The mod adds two **Actions** and two **Properties** to every Jump Drive block. Your script can read or change the properties, and can run the actions.

---

## Actions

Actions are commands you can run on a jump drive block.

### `ScriptJump`

Makes the jump drive jump.

The jump will go to the position stored in the `ScriptJumpTarget` property (see below). You must set `ScriptJumpTarget` **before** calling this action.

**Example:**
```csharp
// Set the target position
jumpDrive.SetValue<Vector3D?>("ScriptJumpTarget", targetPosition);

// Tell the jump drive to jump
jumpDrive.ApplyAction("ScriptJump");
```

### `AbortJump`

Stops a jump that is currently charging or in progress. Does nothing if no jump is active.

**Example:**
```csharp
jumpDrive.ApplyAction("AbortJump");
```

---

## Properties

Properties store information about the jump drive. You can read them or change them from your script.

### `ScriptJumpTarget`

**Type:** `Vector3D?` (a 3D position, or nothing)

This is the position the jump drive will jump to when you run the `ScriptJump` action. Store a GPS coordinate here before jumping.

**Set a target:**
```csharp
Vector3D destination = new Vector3D(50000, 70000, 30000);
jumpDrive.SetValue<Vector3D?>("ScriptJumpTarget", destination);
```

**Read the current target:**
```csharp
Vector3D? currentTarget = jumpDrive.GetValue<Vector3D?>("ScriptJumpTarget");
```

### `JumpTarget`

**Type:** `Vector3D?` (a 3D position, or nothing)

This is **read-only**. It shows the actual target of the jump drive, set by the game itself. You cannot change this value.

**Read the real target:**
```csharp
Vector3D? realTarget = jumpDrive.GetValue<Vector3D?>("JumpTarget");
```

---

## Complete Example Script

Put this in a Programmable Block. It jumps to a GPS waypoint you type into the "Run" argument box.

```csharp
const string jumpDriveName = "Jump Drive";

IMyJumpDrive jumpDrive;

public Program()
{
    // Find the jump drive by name
    jumpDrive = GridTerminalSystem.GetBlockWithName(jumpDriveName) as IMyJumpDrive;

    // If not found by name, use the first jump drive on the grid
    if (jumpDrive == null)
    {
        List<IMyJumpDrive> drives = new List<IMyJumpDrive>();
        GridTerminalSystem.GetBlocksOfType(drives);
        if (drives.Count > 0)
            jumpDrive = drives[0];
    }
}

public void Main(string argument, UpdateType updateSource)
{
    if (jumpDrive == null)
    {
        Echo("No jump drive found!");
        return;
    }

    if (argument.ToLower() == "abort")
    {
        // Abort any active jump
        jumpDrive.ApplyAction("AbortJump");
        Echo("Jump aborted.");
    }
    else
    {
        // Parse the GPS string and jump
        Vector3D pos;
        if (TryParseGPS(argument, out pos))
        {
            jumpDrive.SetValue<Vector3D?>("ScriptJumpTarget", pos);
            jumpDrive.ApplyAction("ScriptJump");
            Echo("Jumping to " + argument);
        }
        else
        {
            Echo("Invalid GPS. Use format: GPS:Name:X:Y:Z:");
        }
    }
}

bool TryParseGPS(string gps, out Vector3D pos)
{
    pos = new Vector3D();

    // GPS format: GPS:name:x:y:z:
    string[] parts = gps.Split(':');
    if (parts.Length < 5 || parts[0] != "GPS")
        return false;

    double x, y, z;
    if (!double.TryParse(parts[2], out x)) return false;
    if (!double.TryParse(parts[3], out y)) return false;
    if (!double.TryParse(parts[4], out z)) return false;

    pos = new Vector3D(x, y, z);
    return true;
}
```

---

## Important Notes

- **Server only.** `ScriptJump` and `AbortJump` only work when the Programmable Block runs on the server. In multiplayer, make sure the block is owned by a player (not "Nobody") so it runs server-side.

- **Jump restrictions still apply.** The jump drive must be charged and ready. Normal game rules about minimum/maximum distance and gravity still apply.

- **You cannot jump into or out of gravity.** If the start or target position is inside a planet's gravity, the jump will be blocked.

- **Planets in the way.** If a planet is between the ship and the target, the mod will try to move the target around the planet. If that is not possible, the jump is blocked.

- **One jump drive per grid.** The mod works with any jump drive. If you have multiple jump drives, pick one to send the commands to.
