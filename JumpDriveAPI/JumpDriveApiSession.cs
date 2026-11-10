using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using VRage.Game.Components;
using VRage.Utils;

namespace avaness.JumpDriveAPI
{
    /// <summary>
    /// Server settings, saved in the world folder as JumpDriveAPI.xml.
    /// The file is written back on every load, so new settings appear with their defaults.
    /// </summary>
    public class JumpDriveApiConfig
    {
        /// <summary>When false, no callback ever runs a programmable block.</summary>
        public bool CallbacksEnabled = true;

        /// <summary>
        /// When false, a callback may only run a programmable block on the same ship as the jump drive.
        /// When true, it may also run one on another ship that has the same owner or a shared faction.
        /// </summary>
        public bool AllowCallbacksToOtherGrids = false;

        /// <summary>Smallest change in max jump range, in meters, that fires RangeChanged.</summary>
        public double MinRangeChangeMeters = 10;

        /// <summary>Shortest time, in seconds, between two RangeChanged callbacks from one drive.</summary>
        public double MinRangeChangeIntervalSeconds = 1;

        /// <summary>Most jumps a script may start per ship per minute. 0 means no limit.</summary>
        public int MaxScriptJumpsPerMinute = 0;

        /// <summary>Shortest countdown, in seconds, a script may set with ScriptJumpDelay. The game's own is 10.</summary>
        public float MinJumpDelaySeconds = 10;

        /// <summary>Longest countdown, in seconds, a script may set with ScriptJumpDelay.</summary>
        public float MaxJumpDelaySeconds = 60;

        /// <summary>When false, ScriptJumpBlind is turned off.</summary>
        public bool AllowBlindJump = true;

        /// <summary>When false, ScriptJumpClamp is turned off.</summary>
        public bool AllowClampToMaxRange = true;

        /// <summary>When false, ScriptJumpTargetFromGps is turned off.</summary>
        public bool AllowGpsTargets = true;

        internal void Validate()
        {
            if (MaxScriptJumpsPerMinute < 0)
                MaxScriptJumpsPerMinute = 0;
            // The countdown must leave time for the game's 0.4 second jump effect.
            if (float.IsNaN(MinJumpDelaySeconds) || MinJumpDelaySeconds < 1)
                MinJumpDelaySeconds = 1;
            if (float.IsNaN(MaxJumpDelaySeconds) || MaxJumpDelaySeconds < MinJumpDelaySeconds)
                MaxJumpDelaySeconds = MinJumpDelaySeconds;
            if (double.IsNaN(MinRangeChangeMeters) || MinRangeChangeMeters < 0)
                MinRangeChangeMeters = 0;
            if (double.IsNaN(MinRangeChangeIntervalSeconds) || MinRangeChangeIntervalSeconds < 0)
                MinRangeChangeIntervalSeconds = 0;
        }
    }

    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class JumpDriveApiSession : MySessionComponentBase
    {
        private const string ConfigFileName = "JumpDriveAPI.xml";

        public static JumpDriveApiConfig Config { get; private set; } = new JumpDriveApiConfig();

        // Frames at which each ship started a script jump, for MaxScriptJumpsPerMinute
        private static readonly Dictionary<long, List<int>> RecentJumps = new Dictionary<long, List<int>>();
        private const int MinuteInFrames = 3600;

        /// <summary>Returns false when the ship has used up its script jumps for this minute; otherwise counts this one.</summary>
        internal static bool TryRecordJump(long gridId)
        {
            int limit = Config.MaxScriptJumpsPerMinute;
            if (limit <= 0)
                return true;

            int now = MyAPIGateway.Session.GameplayFrameCounter;
            List<int> frames;
            if (!RecentJumps.TryGetValue(gridId, out frames))
            {
                frames = new List<int>();
                RecentJumps[gridId] = frames;
            }
            frames.RemoveAll(f => now - f >= MinuteInFrames);

            if (frames.Count >= limit)
                return false;
            frames.Add(now);

            // Drop ships that have not jumped in the last minute, so the table stays small.
            if (RecentJumps.Count > 256)
            {
                List<long> stale = new List<long>();
                foreach (KeyValuePair<long, List<int>> kvp in RecentJumps)
                {
                    kvp.Value.RemoveAll(f => now - f >= MinuteInFrames);
                    if (kvp.Value.Count == 0)
                        stale.Add(kvp.Key);
                }
                foreach (long id in stale)
                    RecentJumps.Remove(id);
            }
            return true;
        }

        public override void LoadData()
        {
            Config = LoadConfig();
        }

        public override void BeforeStart()
        {
            // Runs after the world has loaded and before any programmable block starts its script.
            MyJumpDriveLogic.EnsureControls();
        }

        protected override void UnloadData()
        {
            MyJumpDriveLogic.ResetControls();
            RecentJumps.Clear();
            Config = new JumpDriveApiConfig();
        }

        private static JumpDriveApiConfig LoadConfig()
        {
            JumpDriveApiConfig config = null;
            if (MyAPIGateway.Session?.IsServer != true)
                return new JumpDriveApiConfig();

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(ConfigFileName, typeof(JumpDriveApiSession)))
                {
                    using (TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(ConfigFileName, typeof(JumpDriveApiSession)))
                        config = MyAPIGateway.Utilities.SerializeFromXML<JumpDriveApiConfig>(reader.ReadToEnd());
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("JumpDriveAPI: could not read " + ConfigFileName + ", using defaults. " + e.Message);
            }

            if (config == null)
                config = new JumpDriveApiConfig();
            config.Validate();

            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigFileName, typeof(JumpDriveApiSession)))
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(config));
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("JumpDriveAPI: could not write " + ConfigFileName + ". " + e.Message);
            }

            return config;
        }
    }
}
