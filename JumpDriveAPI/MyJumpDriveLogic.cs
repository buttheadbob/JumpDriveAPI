using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace avaness.JumpDriveAPI
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive), false)]
    public class MyJumpDriveLogic : MyGameLogicComponent
    {
        private IMyJumpDrive _block;
        private Vector3D? _target;
        private Status _status;
        private static bool _controls;

        // Callback fields
        private long _callbackPB;
        private long _callbackEvents;
        private string _callbackArg = "";
        private double _callbackThreshold;

        // Monitoring state
        private double _lastMaxDistance;
        private float _lastChargePercent;
        private bool _lastJumpingWasActive;
        private bool _jumpInitiated;
        private bool _maxDistanceInitialized;

        private enum Status
        {
            None = 0,
            Success = 1,
            Aborted = 2,
            NotServer = 3,
            NoTarget = 4,
            NoJumpSystem = 5,
            AlreadyJumping = 6,
            NotReady = 7,
            NoGrid = 8,
            Immovable = 9,
            Gravity = 10,
            OutsideWorld = 11,
            Planet = 12,
            NoClearSpace = 13,
            TooClose = 14,
            TooFar = 15,
            NoActiveJump = 16
        }

        [Flags]
        private enum CallbackEvent : long
        {
            None          = 0,
            Recharged     = 1L << 0,
            JumpStarted   = 1L << 1,
            JumpCompleted = 1L << 2,
            JumpAborted   = 1L << 3,
            JumpFailed    = 1L << 4,
            RangeChanged  = 1L << 5,
            RangeAbove    = 1L << 6,
            RangeBelow    = 1L << 7,
            RangeEquals   = 1L << 8
        }

        private static readonly Dictionary<Status, string> StatusMessages = new Dictionary<Status, string>
        {
            { Status.None, "" },
            { Status.Success, "Success" },
            { Status.Aborted, "Success: Aborted" },
            { Status.NotServer, "Error: Not server" },
            { Status.NoTarget, "Error: No target" },
            { Status.NoJumpSystem, "Error: No jump system" },
            { Status.AlreadyJumping, "Error: Already jumping" },
            { Status.NotReady, "Error: Not ready" },
            { Status.NoGrid, "Error: No grid" },
            { Status.Immovable, "Error: Immovable or no power" },
            { Status.Gravity, "Error: Inside gravity" },
            { Status.OutsideWorld, "Error: Outside world" },
            { Status.Planet, "Error: Planet blocks path" },
            { Status.NoClearSpace, "Error: No clear space at target" },
            { Status.TooClose, "Error: Too close" },
            { Status.TooFar, "Error: Too far" },
            { Status.NoActiveJump, "Error: No active jump" }
        };

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _block = (IMyJumpDrive)Entity;
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_block.CubeGrid?.Physics == null)
                return;

            if (!_controls)
            {
                CreateControls();
                _controls = true;
            }
        }

        public override void UpdateAfterSimulation10()
        {
            if (_block.CubeGrid?.Physics == null)
                return;

            if (!_controls)
            {
                CreateControls();
                _controls = true;
            }

            MonitorCallbacks();
        }

        public override void UpdateAfterSimulation100()
        {
            ValidateCallback();
        }

        public override void Close()
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        // ============================================================
        //  Terminal control registration
        // ============================================================

        private static void CreateControls()
        {
            // Read/write properties

            IMyTerminalControlProperty<Vector3D?> propScriptTarget = MyAPIGateway.TerminalControls.CreateProperty<Vector3D?, IMyJumpDrive>("ScriptJumpTarget");
            propScriptTarget.Getter = GetJumpTarget;
            propScriptTarget.Setter = SetJumpTarget;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propScriptTarget);


            // Read-only properties

            IMyTerminalControlProperty<Vector3D?> propRealTarget = MyAPIGateway.TerminalControls.CreateProperty<Vector3D?, IMyJumpDrive>("JumpTarget");
            propRealTarget.Getter = GetRealJumpTarget;
            propRealTarget.Setter = (b, v) => { };
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propRealTarget);

            IMyTerminalControlProperty<string> propStatus = MyAPIGateway.TerminalControls.CreateProperty<string, IMyJumpDrive>("ScriptJumpStatus");
            propStatus.Getter = GetJumpStatus;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propStatus);

            IMyTerminalControlProperty<double> propMaxDist = MyAPIGateway.TerminalControls.CreateProperty<double, IMyJumpDrive>("MaxJumpDistance");
            propMaxDist.Getter = GetMaxJumpDistance;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propMaxDist);

            IMyTerminalControlProperty<double> propMinDist = MyAPIGateway.TerminalControls.CreateProperty<double, IMyJumpDrive>("MinJumpDistance");
            propMinDist.Getter = GetMinJumpDistance;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propMinDist);

            IMyTerminalControlProperty<bool> propIsJumping = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyJumpDrive>("IsJumping");
            propIsJumping.Getter = GetIsJumping;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propIsJumping);

            IMyTerminalControlProperty<float> propRemaining = MyAPIGateway.TerminalControls.CreateProperty<float, IMyJumpDrive>("RemainingJumpTime");
            propRemaining.Getter = GetRemainingJumpTime;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propRemaining);


            // Callback properties (read/write)

            IMyTerminalControlProperty<long> propCallbackPB = MyAPIGateway.TerminalControls.CreateProperty<long, IMyJumpDrive>("CallbackPB");
            propCallbackPB.Getter = GetCallbackPB;
            propCallbackPB.Setter = SetCallbackPB;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propCallbackPB);

            IMyTerminalControlProperty<long> propCallbackEvents = MyAPIGateway.TerminalControls.CreateProperty<long, IMyJumpDrive>("CallbackEvents");
            propCallbackEvents.Getter = GetCallbackEvents;
            propCallbackEvents.Setter = SetCallbackEvents;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propCallbackEvents);

            IMyTerminalControlProperty<string> propCallbackArg = MyAPIGateway.TerminalControls.CreateProperty<string, IMyJumpDrive>("CallbackArg");
            propCallbackArg.Getter = GetCallbackArg;
            propCallbackArg.Setter = SetCallbackArg;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propCallbackArg);

            IMyTerminalControlProperty<double> propCallbackThresh = MyAPIGateway.TerminalControls.CreateProperty<double, IMyJumpDrive>("CallbackRangeThreshold");
            propCallbackThresh.Getter = GetCallbackThreshold;
            propCallbackThresh.Setter = SetCallbackThreshold;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propCallbackThresh);


            // Actions

            IMyTerminalAction actionJump = MyAPIGateway.TerminalControls.CreateAction<IMyJumpDrive>("ScriptJump");
            actionJump.ValidForGroups = false;
            actionJump.Action = PerformJump;
            actionJump.Name = new System.Text.StringBuilder("Script Jump");
            actionJump.InvalidToolbarTypes = new List<MyToolbarType>() { MyToolbarType.Character, MyToolbarType.SmallCockpit, MyToolbarType.LargeCockpit, MyToolbarType.Ship, MyToolbarType.Seat, MyToolbarType.Spectator, MyToolbarType.BuildCockpit };
            MyAPIGateway.TerminalControls.AddAction<IMyJumpDrive>(actionJump);

            IMyTerminalAction actionAbort = MyAPIGateway.TerminalControls.CreateAction<IMyJumpDrive>("AbortJump");
            actionAbort.ValidForGroups = false;
            actionAbort.Action = AbortJump;
            actionAbort.Name = new System.Text.StringBuilder("Abort Jump");
            actionAbort.InvalidToolbarTypes = new List<MyToolbarType>() { MyToolbarType.Character, MyToolbarType.SmallCockpit, MyToolbarType.LargeCockpit, MyToolbarType.Ship, MyToolbarType.Seat, MyToolbarType.Spectator, MyToolbarType.BuildCockpit };
            MyAPIGateway.TerminalControls.AddAction<IMyJumpDrive>(actionAbort);
        }

        // ============================================================
        //  Actions
        // ============================================================

        private static void AbortJump(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();

            if (MyAPIGateway.Session?.IsServer != true)
            {
                if (logic != null)
                    logic._status = Status.NotServer;
                return;
            }

            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            if (jumpSystem == null || !jumpSystem.IsJumping)
            {
                if (logic != null)
                    logic._status = Status.NoActiveJump;
                return;
            }

            jumpSystem.AbortJump();
            if (logic != null)
            {
                logic._status = Status.Aborted;
                logic._jumpInitiated = false;
                logic.FireCallback(CallbackEvent.JumpAborted, null);
            }
        }

        private static void PerformJump(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return;

            if (MyAPIGateway.Session?.IsServer != true)
            {
                logic._status = Status.NotServer;
                return;
            }

            if (!logic._target.HasValue)
            {
                logic._status = Status.NoTarget;
                var tokens = new Dictionary<string, string> { { "{status}", "Error: No target" } };
                logic.FireCallback(CallbackEvent.JumpFailed, tokens);
                return;
            }

            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            if (jumpSystem == null)
            {
                logic._status = Status.NoJumpSystem;
                var tokens = new Dictionary<string, string> { { "{status}", "Error: No jump system" } };
                logic.FireCallback(CallbackEvent.JumpFailed, tokens);
                return;
            }
            if (jumpSystem.IsJumping)
            {
                logic._status = Status.AlreadyJumping;
                var tokens = new Dictionary<string, string> { { "{status}", "Error: Already jumping" } };
                logic.FireCallback(CallbackEvent.JumpFailed, tokens);
                return;
            }

            IMyJumpDrive jumpDrive = (IMyJumpDrive)block;
            if (jumpDrive.Status != Sandbox.ModAPI.Ingame.MyJumpDriveStatus.Ready)
            {
                logic._status = Status.NotReady;
                var tokens = new Dictionary<string, string> { { "{status}", "Error: Not ready" } };
                logic.FireCallback(CallbackEvent.JumpFailed, tokens);
                return;
            }

            Vector3D target = logic._target.Value;
            if (logic.IsJumpValid(ref target))
            {
                jumpSystem.Jump(target, block.OwnerId);
                logic._status = Status.Success;
                logic._jumpInitiated = true;
                logic._lastJumpingWasActive = true;
                var tokens = new Dictionary<string, string>
                {
                    { "{target}", FormatGPS(logic._target) }
                };
                logic.FireCallback(CallbackEvent.JumpStarted, tokens);
            }
            else
            {
                string msg;
                StatusMessages.TryGetValue(logic._status, out msg);
                var failTokens = new Dictionary<string, string>
                {
                    { "{status}", msg }
                };
                logic.FireCallback(CallbackEvent.JumpFailed, failTokens);
            }
        }

        /// <summary>IsJumpValid sets _status on failure; caller fires JumpFailed callback.</summary>
        private bool IsJumpValid(ref Vector3D jumpTarget)
        {
            IMyCubeGrid grid = _block.CubeGrid;
            if (grid == null)
            {
                _status = Status.NoGrid;
                return false;
            }

            IMyGridJumpDriveSystem jumpSystem = grid.JumpSystem;
            long userId = _block.OwnerId;
            Vector3D gridPos = grid.WorldMatrix.Translation;

            if (!jumpSystem.IsJumpValid(userId))
            {
                _status = Status.Immovable;
                return false;
            }

            if (IsWithinGravity(jumpTarget) || IsWithinGravity(gridPos))
            {
                _status = Status.Gravity;
                return false;
            }

            if (IsOutsideWorld(jumpTarget))
            {
                _status = Status.OutsideWorld;
                return false;
            }

            IMyEntity intersection = GetIntersectionWithLine(gridPos, jumpTarget);
            if (intersection != null)
            {
                if (intersection is MyPlanet)
                {
                    _status = Status.Planet;
                    return false;
                }

                Vector3D point = intersection.WorldMatrix.Translation;
                Vector3D closestPointOnLine = MyUtils.GetClosestPointOnLine(ref gridPos, ref jumpTarget, ref point);
                float halfExtents = intersection.PositionComp.LocalAABB.HalfExtents.Length();
                jumpTarget = closestPointOnLine - Vector3D.Normalize(jumpTarget - gridPos) * halfExtents;
            }

            Vector3D? newTarget = jumpSystem.FindSuitableJumpLocation(jumpTarget);
            if (!newTarget.HasValue)
            {
                _status = Status.NoClearSpace;
                return false;
            }
            jumpTarget = newTarget.Value;

            double distance = (jumpTarget - gridPos).Length();
            if (distance < jumpSystem.GetMinJumpDistance(userId))
            {
                _status = Status.TooClose;
                return false;
            }
            if (distance > jumpSystem.GetMaxJumpDistance(userId))
            {
                _status = Status.TooFar;
                return false;
            }

            return true;
        }

        // ============================================================
        //  Property getters (read-only)
        // ============================================================

        private static Vector3D? GetJumpTarget(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            return logic?._target;
        }

        private static Vector3D? GetRealJumpTarget(IMyTerminalBlock block)
        {
            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            return jumpSystem?.GetJumpDriveTarget();
        }

        private static string GetJumpStatus(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return "";
            string message;
            return StatusMessages.TryGetValue(logic._status, out message) ? message : "";
        }

        private static double GetMaxJumpDistance(IMyTerminalBlock block)
        {
            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            if (jumpSystem == null)
                return 0;
            return jumpSystem.GetMaxJumpDistance(block.OwnerId);
        }

        private static double GetMinJumpDistance(IMyTerminalBlock block)
        {
            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            if (jumpSystem == null)
                return 0;
            return jumpSystem.GetMinJumpDistance(block.OwnerId);
        }

        private static bool GetIsJumping(IMyTerminalBlock block)
        {
            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            return jumpSystem != null && jumpSystem.IsJumping;
        }

        private static float GetRemainingJumpTime(IMyTerminalBlock block)
        {
            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            float? remaining = jumpSystem?.GetRemainingJumpTime();
            return remaining ?? 0f;
        }

        // ============================================================
        //  Property getters/setters (read/write)
        // ============================================================

        private static void SetJumpTarget(IMyTerminalBlock block, Vector3D? pos)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic != null)
                logic._target = pos;
        }

        private static long GetCallbackPB(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            return logic?._callbackPB ?? 0;
        }
        private static void SetCallbackPB(IMyTerminalBlock block, long value)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic != null)
                logic._callbackPB = value;
        }

        private static long GetCallbackEvents(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            return logic?._callbackEvents ?? 0;
        }
        private static void SetCallbackEvents(IMyTerminalBlock block, long value)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic != null)
                logic._callbackEvents = value;
        }

        private static string GetCallbackArg(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            return logic?._callbackArg ?? "";
        }
        private static void SetCallbackArg(IMyTerminalBlock block, string value)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic != null)
                logic._callbackArg = value ?? "";
        }

        private static double GetCallbackThreshold(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            return logic?._callbackThreshold ?? 0;
        }
        private static void SetCallbackThreshold(IMyTerminalBlock block, double value)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic != null)
                logic._callbackThreshold = value;
        }

        // ============================================================
        //  Callback system
        // ============================================================

        private void FireCallback(CallbackEvent eventType, Dictionary<string, string> tokens)
        {
            if ((_callbackEvents & (long)eventType) == 0)
                return;

            string arg = BuildCallbackArg(tokens);

            long pbId = _callbackPB;
            if (pbId == 0)
                return;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(pbId, out entity))
                return;

            IMyProgrammableBlock pb = entity as IMyProgrammableBlock;
            if (pb == null)
                return;

            pb.Run(arg);
        }

        private string BuildCallbackArg(Dictionary<string, string> tokens)
        {
            string template = _callbackArg;
            if (string.IsNullOrEmpty(template))
                return "";

            if (tokens == null || tokens.Count == 0)
                return template;

            string result = template;
            foreach (KeyValuePair<string, string> kvp in tokens)
                result = result.Replace(kvp.Key, kvp.Value);

            return result;
        }

        /// <summary>Called every 10th frame to detect state changes and fire callbacks.</summary>
        private void MonitorCallbacks()
        {
            IMyGridJumpDriveSystem jumpSystem = _block.CubeGrid?.JumpSystem;

            // ----- Range detection -----
            if (jumpSystem != null)
            {
                double currentMax = jumpSystem.GetMaxJumpDistance(_block.OwnerId);

                if (!_maxDistanceInitialized)
                {
                    _lastMaxDistance = currentMax;
                    _maxDistanceInitialized = true;
                }
                else if (Math.Abs(currentMax - _lastMaxDistance) > 0.01)
                {
                    double oldMax = _lastMaxDistance;
                    _lastMaxDistance = currentMax;

                    var tokens = new Dictionary<string, string>
                    {
                        { "{range}", currentMax.ToString("F1") },
                        { "{threshold}", _callbackThreshold.ToString("F1") }
                    };

                    FireCallback(CallbackEvent.RangeChanged, tokens);

                    if (_callbackThreshold > 0)
                    {
                        if (oldMax <= _callbackThreshold && currentMax > _callbackThreshold)
                            FireCallback(CallbackEvent.RangeAbove, tokens);
                        if (oldMax >= _callbackThreshold && currentMax < _callbackThreshold)
                            FireCallback(CallbackEvent.RangeBelow, tokens);
                        const double epsilon = 0.01;
                        if (Math.Abs(oldMax - _callbackThreshold) > epsilon && Math.Abs(currentMax - _callbackThreshold) <= epsilon)
                            FireCallback(CallbackEvent.RangeEquals, tokens);
                    }
                }
            }

            // ----- Recharged detection -----
            float chargePercent = _block.MaxStoredPower > 0f ? _block.CurrentStoredPower / _block.MaxStoredPower : 0f;
            if (_lastChargePercent < 0.999f && chargePercent >= 0.999f)
            {
                var tokens = new Dictionary<string, string>
                {
                    { "{charge}", "100" }
                };
                FireCallback(CallbackEvent.Recharged, tokens);
            }
            _lastChargePercent = chargePercent;

            // ----- JumpCompleted detection -----
            if (jumpSystem != null && _jumpInitiated && _lastJumpingWasActive && !jumpSystem.IsJumping)
            {
                _jumpInitiated = false;
                var tokens = new Dictionary<string, string>
                {
                    { "{target}", FormatGPS(jumpSystem.GetJumpDriveTarget()) }
                };
                FireCallback(CallbackEvent.JumpCompleted, tokens);
            }
            if (jumpSystem != null)
                _lastJumpingWasActive = jumpSystem.IsJumping;
        }

        // ============================================================
        //  Callback cleanup
        // ============================================================

        private void ValidateCallback()
        {
            if (_callbackPB == 0)
                return;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(_callbackPB, out entity) || !(entity is IMyProgrammableBlock))
            {
                _callbackPB = 0;
                _callbackEvents = 0;
                _callbackArg = "";
                _callbackThreshold = 0;
            }
        }

        // ============================================================
        //  Helpers
        // ============================================================

        private static string FormatGPS(Vector3D? target)
        {
            if (!target.HasValue)
                return "";
            Vector3D t = target.Value;
            return "GPS::" + t.X + ":" + t.Y + ":" + t.Z + ":";
        }

        private IMyEntity GetIntersectionWithLine(Vector3D start, Vector3D end)
        {
            LineD line = new LineD(start, end);
            VRage.Game.Models.MyIntersectionResultLineTriangleEx? intersectionWithLine = MyEntities.GetIntersectionWithLine(ref line, (MyEntity)_block.CubeGrid, null, ignoreChildren: true, ignoreFloatingObjects: true, ignoreHandWeapons: true, ignoreObjectsWithoutPhysics: false, ignoreSubgridsOfIgnoredEntities: true);

            if (!intersectionWithLine.HasValue)
                return null;
            return intersectionWithLine.Value.Entity;
        }

        /// <summary>
        /// The session returns an invalid box when the world has no scenario boundary,
        /// and the world size setting is stored separately as a radius from the origin.
        /// </summary>
        private static bool IsOutsideWorld(Vector3D pos)
        {
            IMySession session = MyAPIGateway.Session;
            if (session == null)
                return false;

            BoundingBoxD bounds = session.WorldBoundaries;
            if (bounds.Valid && bounds.Contains(pos) != ContainmentType.Contains)
                return true;

            int worldSizeKm = session.SessionSettings?.WorldSizeKm ?? 0;
            return worldSizeKm > 0 && pos.Length() > worldSizeKm * 500.0;
        }

        private bool IsWithinGravity(Vector3D pos)
        {
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(pos);
            return planet != null && IsWithinGravity(pos, planet);
        }

        private bool IsWithinGravity(Vector3D pos, MyPlanet planet)
        {
            MyGravityProviderComponent gravity = planet.Components.Get<MyGravityProviderComponent>();
            return gravity != null && gravity.IsPositionInRange(pos);
        }
    }
}
