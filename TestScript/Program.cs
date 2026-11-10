using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // ---- copy from here into the programmable block ----

        // ==============================================================
        //  Jump Drive API sample script
        // ==============================================================
        //
        // HOW TO SET IT UP
        //   1. The world needs the "Jump Drive API" mod (1.1.0 or newer).
        //   2. Build a programmable block on the same ship as your jump drives.
        //   3. Open the block in the terminal, click "Edit", paste this script,
        //      then click "Check Code" and "OK".
        //   4. The block's screen and its terminal panel now show the drive's
        //      range and charge, and any warnings.
        //
        // HOW TO JUMP
        //   Type a command in the "Argument" box and click "Run".
        //   For a button: drag the programmable block onto a cockpit toolbar,
        //   choose "Run", and type the command.
        //
        //   Command                      What it does
        //   ---------------------------  -----------------------------------------
        //   GPS:Name:X:Y:Z:#FF75C9F1:    Jump to that GPS point.
        //                                (Copy it from the GPS screen with "Copy to clipboard".)
        //   Home                         Jump to the GPS point you saved as "Home".
        //   check Home                   Only check the jump: shows whether it would
        //                                work and how far it goes. Works with GPS points too.
        //   return                       Jump back to where the last script jump started.
        //   abort                        Cancel the jump during the countdown.
        //   (nothing)                    Show the range, charge and drive status.
        //
        // IF SOMETHING GOES WRONG
        //   The screen shows a WARNING line and what to do about it.
        //
        // WRITING YOUR OWN SCRIPT? LEARN THE VERSION CHECK FIRST
        //   Look at CheckSetup() further down. Before doing anything else, it checks
        //   that the mod is installed and that its version is one this script works with.
        //   Copy that check into every script you write that uses the Jump Drive API.
        //
        //   Why: the mod is updated over time, and the world's mod may be older or newer
        //   than the one you wrote your script for. Without the check, a missing feature
        //   makes the script crash with an error players can't make sense of. With it,
        //   they get a clear message saying what is wrong.
        //
        //   How the version works: it has three parts, Major.Minor.Patch (like 1.1.0).
        //     Major goes up when a change can break scripts, so check it matches exactly.
        //     Minor goes up when features are added, so check it is at least the version
        //       that added the newest feature you use.
        //     Patch is bug fixes only, so you never need to check it.
        //   The README's "Versions" section lists what each version added.
        //
        // ==============================================================
        //  Settings
        // ==============================================================

        // The name of your jump drive block. If no block has this name,
        // the script uses the first jump drive on this ship.
        private const string JumpDriveName = "Jump Drive";

        // When true, a target that is too far is shortened to the max range,
        // like a jump from the cockpit. When false, the jump fails with "Too far".
        private const bool JumpAsFarAsPossible = false;

        // ==============================================================

        // Status codes from ScriptJumpStatusCode. Compare these numbers, not the
        // status text: the text is for players and may change in a later version.
        private const int StatusJumpStarted = 1;
        private const int StatusAborted = 2;
        private const int StatusCanJump = 17;
        private const int StatusTargetSet = 22;

        private IMyJumpDrive jumpDrive;

        // The result of the last command. It stays on screen until the next command,
        // so the regular refresh does not wipe it.
        private string lastResult = "";
        private string lastWarning = "";

        private readonly StringBuilder screenText = new StringBuilder();

        // Program() runs once, when the script is compiled or the world loads.
        public Program()
        {
            // Run Main every 100 ticks (about 1.6 seconds) to keep the display up to date.
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        // Main runs each time the block is run: by you, a toolbar button,
        // or by the game every 100 ticks (see Program above).
        public void Main(string argument, UpdateType updateSource)
        {
            screenText.Clear();

            string problem = CheckSetup();
            if (problem != null)
            {
                // Nothing else can work until this is fixed, so show only this.
                Line("WARNING: " + problem);
                Show();
                return;
            }

            argument = argument.Trim();
            bool isCommand = (updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0;
            if (isCommand && argument.Length > 0)
                RunCommand(argument);

            ShowState();
            Show();
        }

        // ==============================================================
        //  Commands
        // ==============================================================

        private void RunCommand(string argument)
        {
            lastResult = "";
            lastWarning = "";

            if (string.Equals(argument, "abort", StringComparison.OrdinalIgnoreCase))
            {
                jumpDrive.ApplyAction("AbortJump");
                Report("Jump cancelled.");
                return;
            }

            if (string.Equals(argument, "return", StringComparison.OrdinalIgnoreCase))
            {
                // Jump back to where the last script jump started.
                jumpDrive.SetValue("ScriptJumpClamp", JumpAsFarAsPossible);
                jumpDrive.ApplyAction("ScriptJumpReturn");
                Report("Jumping back.");
                return;
            }

            bool checkOnly = argument.StartsWith("check ", StringComparison.OrdinalIgnoreCase);
            if (checkOnly)
                argument = argument.Substring(6).Trim();

            if (!SetTarget(argument))
                return;

            // Tell the mod whether to shorten a jump that is too far.
            jumpDrive.SetValue("ScriptJumpClamp", JumpAsFarAsPossible);

            // "ScriptJumpCheck" runs every check without jumping; "ScriptJump" also jumps.
            // Either way, the result is in ScriptJumpStatus right after the call.
            jumpDrive.ApplyAction(checkOnly ? "ScriptJumpCheck" : "ScriptJump");
            Report(checkOnly ? "Can jump" : "Jumping");
        }

        // Sets the jump target from a pasted GPS point or the name of a saved one.
        private bool SetTarget(string argument)
        {
            Vector3D target;
            if (TryParseGPS(argument, out target))
            {
                jumpDrive.SetValue<Vector3D?>("ScriptJumpTarget", target);
                return true;
            }

            // Not a GPS point, so try it as the name of one of your saved GPS points.
            // The mod looks it up in the drive owner's GPS list and sets ScriptJumpTarget.
            jumpDrive.SetValue("ScriptJumpGpsName", argument);
            jumpDrive.ApplyAction("ScriptJumpTargetFromGps");
            if (jumpDrive.GetValue<int>("ScriptJumpStatusCode") == StatusTargetSet)
                return true;

            lastWarning = "\"" + argument + "\" is not a GPS point or a saved GPS name.\n"
                + "Paste a GPS point (GPS:Name:X:Y:Z:) or type the exact name\n"
                + "of a GPS point the ship's owner saved.";
            return false;
        }

        // Reads the result of the last action and turns it into a message or a warning.
        private void Report(string successText)
        {
            int code = jumpDrive.GetValue<int>("ScriptJumpStatusCode");

            if (code == StatusJumpStarted || code == StatusCanJump)
            {
                // Where the ship will really arrive, after the mod moved the target
                // around obstacles and into clear space.
                Vector3D? landing = jumpDrive.GetValue<Vector3D?>("PredictedJumpTarget");
                if (landing.HasValue)
                {
                    double km = Vector3D.Distance(Me.CubeGrid.GetPosition(), landing.Value) / 1000;
                    lastResult = successText + " " + km.ToString("0.0") + " km.";
                }
                else
                {
                    lastResult = successText + ".";
                }
                return;
            }

            if (code == StatusAborted)
            {
                lastResult = successText;
                return;
            }

            lastWarning = jumpDrive.GetValue<string>("ScriptJumpStatus") + "\n" + GetHint(code);
        }

        // What the player can do about each failure. The README lists every status code.
        private string GetHint(int code)
        {
            switch (code)
            {
                case 3:  return "The command ran on a player's computer, not the server. Scripts normally run on the server; ask the server admin.";
                case 4:  return "No target was set.";
                case 6:  return "A jump is already counting down. Type \"abort\" to cancel it.";
                case 7:  return "The drives are not ready. Wait for them to charge, and check they are on.";
                case 9:  return "The ship can't move: unlock landing gear and connectors, make it not static, check power,\n"
                              + "and make sure the drives have an owner (not \"Nobody\").";
                case 10:
                    double escape = jumpDrive.GetValue<double>("GravityEscapeDistance");
                    if (escape > 0)
                        return "You can't jump in gravity. Fly " + (escape / 1000).ToString("0.0") + " km up first.";
                    return "The target is in a planet's gravity. Pick a point further out.";
                case 11: return "The target is outside the world's border. Pick a closer point.";
                case 12:
                case 13:
                    string blocking = jumpDrive.GetValue<string>("BlockingEntityType");
                    string name = jumpDrive.GetValue<string>("BlockingEntityName");
                    string what = blocking == "" ? "Something" : (blocking + (name != "" ? " \"" + name + "\"" : ""));
                    return what + " is in the way. Pick another point or fly around it.";
                case 14: return "The target is too close. Use a point further away.";
                case 15:
                    return "The target is too far. Lighten the ship, add drives, pick a closer point,\n"
                         + "or set JumpAsFarAsPossible = true in the script's settings.";
                case 16: return "There is no jump to cancel.";
                case 19: return "The server limits how often ships jump. Wait a minute.";
                case 20: return "The server has turned this feature off.";
                case 23: return "There is no earlier jump to return from.";
                case 24: return "The game's own check failed just before the countdown. Try again, or pick another point.";
                default: return "";
            }
        }

        // ==============================================================
        //  Setup checks
        // ==============================================================

        // Returns null when everything is ready, or a warning saying what to fix.
        private string CheckSetup()
        {
            // Find the drive again when it is missing, for example after it was rebuilt.
            if (jumpDrive == null || jumpDrive.Closed || !GridTerminalSystem.CanAccess(jumpDrive))
                jumpDrive = FindJumpDrive();

            if (jumpDrive == null)
                return "No jump drive on this ship.\nBuild one on the same ship as this programmable block.";

            // Without the mod, the drive has no "ScriptJumpTarget" property,
            // and setting it would crash the script.
            if (jumpDrive.GetProperty("ScriptJumpTarget") == null)
                return "The Jump Drive API mod is not in this world.\nAdd it to the world's mods and reload.";

            // This script uses features added in 1.1.0. Releases before that have no
            // version properties. A new major version (2.x) may change things it relies on.
            int major = jumpDrive.GetProperty("VersionMajor") != null ? jumpDrive.GetValue<int>("VersionMajor") : 1;
            int minor = jumpDrive.GetProperty("VersionMinor") != null ? jumpDrive.GetValue<int>("VersionMinor") : 0;
            if (major == 1 && minor < 1)
                return "The Jump Drive API mod in this world is too old.\nThis script needs 1.1.0 or newer.";
            if (major > 1)
                return "The Jump Drive API mod in this world is version " + jumpDrive.GetValue<string>("Version")
                     + ".\nThis script was written for 1.x. Get an updated script.";

            return null;
        }

        // Only looks at blocks on this ship. Without this check, the script
        // could pick a jump drive on a ship docked by a connector.
        private IMyJumpDrive FindJumpDrive()
        {
            IMyJumpDrive named = GridTerminalSystem.GetBlockWithName(JumpDriveName) as IMyJumpDrive;
            if (named != null && named.IsSameConstructAs(Me))
                return named;

            List<IMyJumpDrive> drives = new List<IMyJumpDrive>();
            GridTerminalSystem.GetBlocksOfType(drives, b => b.IsSameConstructAs(Me));
            return drives.Count > 0 ? drives[0] : null;
        }

        // ==============================================================
        //  Display
        // ==============================================================

        private void ShowState()
        {
            if (lastWarning != "")
            {
                Line("WARNING: " + lastWarning);
                Line("");
            }
            else if (lastResult != "")
            {
                Line(lastResult);
                Line("");
            }

            if (jumpDrive.GetValue<bool>("IsJumping"))
            {
                Line("JUMP IN " + jumpDrive.GetValue<float>("RemainingJumpTime").ToString("0") + " s   (\"abort\" to cancel)");
                Line("");
            }

            double max = jumpDrive.GetValue<double>("MaxJumpDistance");
            double min = jumpDrive.GetValue<double>("MinJumpDistance");
            float stored = jumpDrive.GetValue<float>("GridStoredPower");
            float capacity = jumpDrive.GetValue<float>("GridMaxStoredPower");
            int ready = jumpDrive.GetValue<int>("ReadyDriveCount");
            int total = jumpDrive.GetValue<int>("TotalDriveCount");

            Line("Range:  " + (min / 1000).ToString("0.0") + " - " + (max / 1000).ToString("0.0") + " km");
            Line("Charge: " + (capacity > 0 ? stored / capacity * 100 : 0).ToString("0") + "%");
            Line("Drives ready: " + ready + " of " + total);

            // -1 while the mod is still measuring the charge rate, or when nothing is charging.
            double recharge = jumpDrive.GetValue<double>("RechargeTimeLeft");
            if (recharge > 0)
                Line("Full in " + (recharge / 60).ToString("0.0") + " min");

            if (ready < total && recharge <= 0)
                Line("Some drives are off, damaged or not charging.");

            Line("");
            Line("Commands: a GPS point or name, check <point>, return, abort");
        }

        private void Line(string text)
        {
            screenText.AppendLine(text);
        }

        // Shows the text in the terminal panel and on the programmable block's own screen.
        private void Show()
        {
            string text = screenText.ToString();
            Echo(text);

            IMyTextSurface screen = Me.GetSurface(0);
            screen.ContentType = ContentType.TEXT_AND_IMAGE;
            screen.Font = "Monospace";
            screen.FontSize = 0.6f;
            screen.WriteText(text);
        }

        // ==============================================================
        //  GPS
        // ==============================================================

        // Reads a GPS point as copied from the GPS screen:
        //   GPS:Name:X:Y:Z:#Color:
        // so after splitting on ':', X, Y and Z are parts [2], [3] and [4].
        // Scripts run with the invariant culture, so the decimal point is always '.'.
        private bool TryParseGPS(string argument, out Vector3D pos)
        {
            pos = new Vector3D();

            string[] parts = argument.Split(':');
            if (parts.Length < 5 || parts[0] != "GPS")
                return false;

            double x, y, z;
            if (!double.TryParse(parts[2], out x) || !double.TryParse(parts[3], out y) || !double.TryParse(parts[4], out z))
                return false;

            pos = new Vector3D(x, y, z);
            return true;
        }

        // ---- copy to here ----
    }
}
