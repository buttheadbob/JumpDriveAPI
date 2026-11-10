using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRageMath;

namespace avaness.JumpDriveAPI
{
    public partial class MyJumpDriveLogic
    {
        // Controls are added once per session. They must not be added before the game has created
        // the vanilla jump drive controls (MyJumpDrive.CreateTerminalControls returns early once the
        // block type has any controls), so the first drive's Init sets _driveSeen first.
        private static bool _controls;
        private static bool _driveSeen;

        private static readonly List<VRage.Game.MyToolbarType> HiddenToolbars = new List<VRage.Game.MyToolbarType>
        {
            VRage.Game.MyToolbarType.Character, VRage.Game.MyToolbarType.SmallCockpit, VRage.Game.MyToolbarType.LargeCockpit,
            VRage.Game.MyToolbarType.Ship, VRage.Game.MyToolbarType.Seat, VRage.Game.MyToolbarType.Spectator,
            VRage.Game.MyToolbarType.BuildCockpit
        };

        internal static void EnsureControls()
        {
            if (_controls || !_driveSeen || MyAPIGateway.TerminalControls == null)
                return;
            _controls = true;
            CreateControls();
        }

        internal static void ResetControls()
        {
            _controls = false;
            _driveSeen = false;
        }

        private static void CreateControls()
        {
            // ----- Jump settings (read/write) -----
            // NaN or infinite values would pass every distance check, so they clear the setting.
            AddProperty("ScriptJumpTarget", l => l._target, (l, v) => l._target = Finite(v));
            AddProperty("ScriptJumpClamp", l => l._clampToMaxRange, (l, v) => l._clampToMaxRange = v);
            AddProperty("ScriptJumpDirection", l => l._blindDirection, (l, v) => l._blindDirection = Finite(v));
            AddProperty("ScriptJumpDistance", l => l._blindDistance, (l, v) => l._blindDistance = IsFinite(v) ? Math.Max(0, v) : 0);
            AddProperty("ScriptJumpGpsName", l => l._gpsName, (l, v) => l._gpsName = v ?? "");
            AddProperty("ScriptJumpDelay", l => l._jumpDelay, (l, v) => l._jumpDelay = IsFinite(v) ? Math.Max(0, v) : 0);

            // ----- Results (read-only) -----
            AddProperty("ScriptJumpStatus", l => StatusText(l._status), null);
            AddProperty("ScriptJumpStatusCode", l => (int)l._status, null);
            AddProperty<Vector3D?>("PredictedJumpTarget", l => l._predictedTarget, null);
            AddProperty("Version", l => VersionText, null);
            AddProperty("VersionMajor", l => VersionMajor, null);
            AddProperty("VersionMinor", l => VersionMinor, null);
            AddProperty("VersionPatch", l => VersionPatch, null);
            AddProperty("BlockingEntityType", l => l._blockingType, null);
            AddProperty("BlockingEntityName", l => l._blockingName, null);
            AddProperty<Vector3D?>("BlockingEntityPosition", l => l._blockingPosition, null);
            AddProperty<Vector3D?>("LastJumpOrigin", l => l._hasLastJump ? (Vector3D?)l._lastJumpOrigin : null, null);
            AddProperty<Vector3D?>("LastJumpDestination", l => l._hasLastJump ? (Vector3D?)l._lastJumpDestination : null, null);
            AddProperty("RechargeTimeLeft", l => l.ReadRechargeSeconds(), null);
            AddProperty("MaxMassForTarget", l => l.GetMaxMassForTarget(), null);
            AddProperty("GravityEscapeDistance", l => { double d; Vector3D dir; return l.GetGravityEscapeCached(out d, out dir) ? d : 0; }, null);
            AddProperty<Vector3D?>("GravityEscapeDirection", l => { double d; Vector3D dir; return l.GetGravityEscapeCached(out d, out dir) ? (Vector3D?)dir : null; }, null);

            // ----- Drive and grid state (read-only) -----
            AddBlockProperty<Vector3D?>("JumpTarget", b => b.CubeGrid?.JumpSystem?.GetJumpDriveTarget());
            AddBlockProperty("MaxJumpDistance", b => b.CubeGrid?.JumpSystem?.GetMaxJumpDistance(b.OwnerId) ?? 0);
            AddBlockProperty("MinJumpDistance", b => b.CubeGrid?.JumpSystem?.GetMinJumpDistance(b.OwnerId) ?? 0);
            AddBlockProperty("IsJumping", b => b.CubeGrid?.JumpSystem?.IsJumping ?? false);
            AddBlockProperty("RemainingJumpTime", b => b.CubeGrid?.JumpSystem?.GetRemainingJumpTime() ?? 0f);
            AddBlockProperty("GridStoredPower", b => { float s, m; GetGridCharge(b.CubeGrid, out s, out m); return s; });
            AddBlockProperty("GridMaxStoredPower", b => { float s, m; GetGridCharge(b.CubeGrid, out s, out m); return m; });
            AddBlockProperty("ReadyDriveCount", b => CountDrives(b.CubeGrid, b.OwnerId, readyOnly: true));
            AddBlockProperty("TotalDriveCount", b => CountDrives(b.CubeGrid, b.OwnerId, readyOnly: false));

            // ----- Callbacks (read/write) -----
            AddProperty("CallbackPB", l => l._callbackPB, (l, v) => { l._callbackPB = v; l._monitorInitialized = false; });
            AddProperty("CallbackEvents", l => l._callbackEvents, (l, v) => { l._callbackEvents = v; l._monitorInitialized = false; });
            AddProperty("CallbackArg", l => l._callbackArg, (l, v) => l._callbackArg = v ?? "");
            AddProperty("CallbackRangeThreshold", l => l._callbackThreshold, (l, v) => l._callbackThreshold = IsFinite(v) ? v : 0);
            AddProperty("CallbackRangeStep", l => l._callbackRangeStep, (l, v) => l._callbackRangeStep = IsFinite(v) ? Math.Max(0, v) : 0);

            // ----- Actions -----
            AddAction("ScriptJump", "Script Jump", PerformJump);
            AddAction("ScriptJumpCheck", "Script Jump Check", CheckJump);
            AddAction("ScriptJumpBlind", "Script Blind Jump", PerformBlindJump);
            AddAction("ScriptJumpReturn", "Script Return Jump", PerformReturnJump);
            AddAction("ScriptJumpTargetFromGps", "Script Target From GPS", SetTargetFromGps);
            AddAction("AbortJump", "Abort Jump", AbortJump);
        }

        private static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static Vector3D? Finite(Vector3D? v)
        {
            if (!v.HasValue)
                return null;
            Vector3D x = v.Value;
            return IsFinite(x.X) && IsFinite(x.Y) && IsFinite(x.Z) ? v : null;
        }

        private static string StatusText(Status status)
        {
            string message;
            return StatusMessages.TryGetValue(status, out message) ? message : "";
        }

        /// <summary>Adds a property stored on this mod's logic. A null setter makes it read-only.</summary>
        private static void AddProperty<T>(string id, Func<MyJumpDriveLogic, T> get, Action<MyJumpDriveLogic, T> set)
        {
            IMyTerminalControlProperty<T> prop = MyAPIGateway.TerminalControls.CreateProperty<T, IMyJumpDrive>(id);
            prop.Getter = b =>
            {
                MyJumpDriveLogic logic = b.GameLogic.GetAs<MyJumpDriveLogic>();
                return logic != null ? get(logic) : default(T);
            };
            prop.Setter = (b, v) =>
            {
                if (set == null)
                    return;
                MyJumpDriveLogic logic = b.GameLogic.GetAs<MyJumpDriveLogic>();
                if (logic != null)
                {
                    set(logic, v);
                    logic._settingsChanged = true;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(prop);
        }

        /// <summary>Adds a read-only property read from the block and its grid.</summary>
        private static void AddBlockProperty<T>(string id, Func<IMyTerminalBlock, T> get)
        {
            IMyTerminalControlProperty<T> prop = MyAPIGateway.TerminalControls.CreateProperty<T, IMyJumpDrive>(id);
            prop.Getter = get;
            prop.Setter = (b, v) => { };
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(prop);
        }

        private static void AddAction(string id, string name, Action<IMyTerminalBlock> action)
        {
            IMyTerminalAction a = MyAPIGateway.TerminalControls.CreateAction<IMyJumpDrive>(id);
            a.ValidForGroups = false;
            a.Action = action;
            a.Name = new StringBuilder(name);
            a.InvalidToolbarTypes = new List<VRage.Game.MyToolbarType>(HiddenToolbars);
            MyAPIGateway.TerminalControls.AddAction<IMyJumpDrive>(a);
        }
    }
}
