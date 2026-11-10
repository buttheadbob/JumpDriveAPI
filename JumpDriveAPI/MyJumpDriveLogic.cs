using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage;
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
    public partial class MyJumpDriveLogic : MyGameLogicComponent
    {
        // Semantic version of the API, changed once per Workshop release:
        //   Major: breaking changes. Minor: new features, nothing breaks. Patch: bug fixes only.
        // Releases before these properties existed count as 1.0.0.
        private const int VersionMajor = 1;
        private const int VersionMinor = 1;
        private const int VersionPatch = 0;
        private static readonly string VersionText = VersionMajor + "." + VersionMinor + "." + VersionPatch;

        /// <summary>Must match the GUID registered in Data/EntityComponents.sbc, or the game will not save it.</summary>
        private static readonly Guid StorageGuid = new Guid("66EAF97E-4052-44BF-A3F2-298ECD2B094E");

        private const double FramesPerSecond = 60;

        private IMyJumpDrive _block;

        // Set when a saved setting changes; the settings are written on the next 10th frame.
        private bool _settingsChanged;

        // Script settings (saved with the block)
        private Vector3D? _target;
        private bool _clampToMaxRange;
        private Vector3D? _blindDirection;
        private double _blindDistance;
        private string _gpsName = "";
        private float _jumpDelay;

        // Result of the last action
        private Status _status;
        private Vector3D? _predictedTarget;
        private string _blockingType = "";
        private string _blockingName = "";
        private Vector3D? _blockingPosition;

        // Last ScriptJumpCheck, reused for 10 frames when nothing it depends on changed
        private bool _checkValid;
        private int _checkFrame;
        private Vector3D? _checkTarget;
        private bool _checkClamp;

        // Last completed script jump, for ScriptJumpReturn (saved with the block)
        private bool _hasLastJump;
        private Vector3D _lastJumpOrigin;
        private Vector3D _lastJumpDestination;

        // Recharge time estimate, measured only while a script reads it
        private int _rechargeReadFrame = int.MinValue;
        private float _rechargeLastStored = -1f;
        private int _rechargeLastFrame;
        private double _rechargeSeconds = -1;

        // Callback settings (saved with the block)
        private long _callbackPB;
        private long _callbackEvents;
        private string _callbackArg = "";
        private double _callbackThreshold;
        private double _callbackRangeStep;

        // Callbacks waiting to run on the next frame
        private List<string> _pendingCallbacks = new List<string>();
        private List<string> _runningCallbacks = new List<string>();

        // Monitoring state
        private bool _monitorInitialized;
        private double _lastMaxDistance;
        private double _lastReportedRange;
        private int _lastRangeReportFrame;
        private float _lastChargePercent;
        private float _lastGridChargePercent;

        // Jump tracking for a jump started by ScriptJump or ScriptJumpBlind
        private bool _jumpInitiated;
        private bool _jumpArrived;
        private Vector3D _jumpOrigin;
        private Vector3D _arrivalPosition;
        private int _lastCountdownSecond;
        private float _lastRemaining;

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
            NoActiveJump = 16,
            CanJump = 17,
            NoDirection = 18,
            RateLimited = 19,
            Disabled = 20,
            GpsNotFound = 21,
            TargetSet = 22,
            NoReturnPoint = 23,
            Refused = 24
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
            RangeEquals   = 1L << 8,
            GridRecharged = 1L << 9,
            JumpCountdown = 1L << 10
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
            { Status.NoActiveJump, "Error: No active jump" },
            { Status.CanJump, "Success: Can jump" },
            { Status.NoDirection, "Error: No direction" },
            { Status.RateLimited, "Error: Too many jumps, wait" },
            { Status.Disabled, "Error: Disabled by server" },
            { Status.GpsNotFound, "Error: GPS not found" },
            { Status.TargetSet, "Success: Target set" },
            { Status.NoReturnPoint, "Error: No jump to return from" },
            { Status.Refused, "Error: The game refused the jump" }
        };

        // ============================================================
        //  Lifecycle
        // ============================================================

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _block = (IMyJumpDrive)Entity;

            // The game creates the vanilla jump drive controls just before this runs,
            // so the mod's controls can now be added without replacing them.
            _driveSeen = true;

            LoadFromStorage();
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            EnsureControls();
            RunPendingCallbacks();
        }

        public override void UpdateAfterSimulation10()
        {
            if (MyAPIGateway.Session?.IsServer != true)
                return;

            // Settings are written here, never from IsSerialized: adding the storage component
            // while the game serializes the block's components would change the collection it
            // is looping over, and a second mod's game logic on the same block would stop
            // IsSerialized from being called at all.
            if (_settingsChanged)
            {
                _settingsChanged = false;
                SaveToStorage();
            }

            if (_block.CubeGrid?.Physics == null)
                return;

            IMyGridJumpDriveSystem jumpSystem = _block.CubeGrid.JumpSystem;
            if (jumpSystem == null)
                return;

            // Jump tracking runs without callbacks too, because ScriptJumpReturn needs it.
            MonitorJump(jumpSystem);
            MonitorCallbacks(jumpSystem);
        }

        public override void UpdateAfterSimulation100()
        {
            if (MyAPIGateway.Session?.IsServer != true)
                return;

            ValidateCallback();
            SampleRecharge();
        }

        public override void Close()
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
            _pendingCallbacks.Clear();
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

            // The mod API's AbortJump only cancels on the server. The grid's own abort is a
            // network event, like the cockpit's, so players' clients stop the countdown too.
            ((MyCubeGrid)block.CubeGrid).SendAbortJump();

            if (logic != null)
            {
                logic._status = Status.Aborted;
                logic._checkValid = false;
            }

            // The jump may have been started from another drive on the grid. Report the abort
            // once from each drive that was following it, so none of them later reports it again
            // as "Cancelled".
            foreach (IMyJumpDrive drive in block.CubeGrid.GetFatBlocks<IMyJumpDrive>())
            {
                MyJumpDriveLogic other = drive.GameLogic.GetAs<MyJumpDriveLogic>();
                if (other == null || (!other._jumpInitiated && other != logic))
                    continue;
                other._jumpInitiated = false;
                other.FireCallback(CallbackEvent.JumpAborted, new Dictionary<string, string> { { "{reason}", "Script" } });
            }
        }

        private static void PerformJump(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic != null)
                logic.TryJump(logic._target, logic.UseClamp, execute: true);
        }

        /// <summary>ScriptJumpClamp, unless the server has turned clamping off.</summary>
        private bool UseClamp
        {
            get { return _clampToMaxRange && JumpDriveApiSession.Config.AllowClampToMaxRange; }
        }

        private static void PerformBlindJump(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return;

            if (!JumpDriveApiSession.Config.AllowBlindJump)
            {
                logic.Fail(Status.Disabled, execute: true);
                return;
            }

            Vector3D? target;
            bool clamp;
            if (!logic.GetBlindJumpTarget(out target, out clamp))
            {
                logic.Fail(Status.NoDirection, execute: true);
                return;
            }
            logic.TryJump(target, clamp, execute: true);
        }

        private static void PerformReturnJump(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return;

            if (!logic._hasLastJump)
            {
                logic.Fail(Status.NoReturnPoint, execute: true);
                return;
            }
            logic.TryJump(logic._lastJumpOrigin, logic.UseClamp, execute: true);
        }

        private static void CheckJump(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return;

            // A check does a raycast and a clear-space search. Scripts that check every tick
            // for an LCD get the result from the last 10 frames instead of a new search.
            int frame = MyAPIGateway.Session?.GameplayFrameCounter ?? 0;
            bool clamp = logic.UseClamp;
            if (logic._checkValid && frame - logic._checkFrame < 10
                && logic._checkTarget == logic._target && logic._checkClamp == clamp)
                return;

            logic.TryJump(logic._target, clamp, execute: false);
            logic._checkValid = true;
            logic._checkFrame = frame;
            logic._checkTarget = logic._target;
            logic._checkClamp = clamp;
        }

        /// <summary>Sets ScriptJumpTarget from a GPS point in the drive owner's GPS list.</summary>
        private static void SetTargetFromGps(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return;

            logic.ClearResult();
            if (MyAPIGateway.Session?.IsServer != true)
            {
                logic._status = Status.NotServer;
                return;
            }
            if (!JumpDriveApiSession.Config.AllowGpsTargets)
            {
                logic._status = Status.Disabled;
                return;
            }

            IMyGps gps = FindGps(block.OwnerId, logic._gpsName);
            if (gps == null)
            {
                logic._status = Status.GpsNotFound;
                return;
            }

            logic._target = gps.Coords;
            logic._settingsChanged = true;
            logic._status = Status.TargetSet;
        }

        /// <summary>An exact name match wins; otherwise the first match ignoring case.</summary>
        private static IMyGps FindGps(long identityId, string name)
        {
            if (identityId == 0 || string.IsNullOrWhiteSpace(name))
                return null;

            name = name.Trim();
            IMyGps caseInsensitive = null;
            foreach (IMyGps gps in MyAPIGateway.Session.GPS.GetGpsList(identityId))
            {
                if (gps.Name == name)
                    return gps;
                if (caseInsensitive == null && string.Equals(gps.Name, name, StringComparison.OrdinalIgnoreCase))
                    caseInsensitive = gps;
            }
            return caseInsensitive;
        }

        private void ClearResult()
        {
            _checkValid = false;
            _predictedTarget = null;
            _blockingType = "";
            _blockingName = "";
            _blockingPosition = null;
        }

        /// <summary>
        /// Runs every check ScriptJump runs. With execute false it only reports the result
        /// in ScriptJumpStatus and PredictedJumpTarget; with execute true it also starts the jump.
        /// </summary>
        private void TryJump(Vector3D? requestedTarget, bool clamp, bool execute)
        {
            ClearResult();

            if (MyAPIGateway.Session?.IsServer != true)
            {
                _status = Status.NotServer;
                return;
            }

            if (!requestedTarget.HasValue)
            {
                Fail(Status.NoTarget, execute);
                return;
            }

            IMyGridJumpDriveSystem jumpSystem = _block.CubeGrid?.JumpSystem;
            if (jumpSystem == null)
            {
                Fail(Status.NoJumpSystem, execute);
                return;
            }
            if (jumpSystem.IsJumping)
            {
                Fail(Status.AlreadyJumping, execute);
                return;
            }
            if (_block.Status != Sandbox.ModAPI.Ingame.MyJumpDriveStatus.Ready)
            {
                Fail(Status.NotReady, execute);
                return;
            }

            Vector3D target = requestedTarget.Value;
            Status result = ValidateJump(ref target, clamp);
            if (result != Status.Success)
            {
                Fail(result, execute);
                return;
            }

            if (!execute)
            {
                _status = Status.CanJump;
                _predictedTarget = target;
                return;
            }

            if (!JumpDriveApiSession.TryRecordJump(_block.CubeGrid.EntityId))
            {
                Fail(Status.RateLimited, execute);
                return;
            }

            _jumpOrigin = _block.CubeGrid.WorldMatrix.Translation;

            // RequestJump is the cockpit's path: the server checks the jump once more and then
            // starts the countdown on every client too, so players see and hear it. The mod
            // API's Jump would start it on the server only. On the server the request runs
            // at once, so IsJumping is already set when it returns.
            jumpSystem.RequestJump(target, _block.OwnerId, GetJumpDelay(), _block.EntityId);
            if (!jumpSystem.IsJumping)
            {
                Fail(Status.Refused, execute);
                return;
            }

            // The game may shorten the jump to the max range; report where the ship will really go.
            _predictedTarget = jumpSystem.GetJumpDriveTarget() ?? target;
            _status = Status.Success;
            _jumpInitiated = true;
            _jumpArrived = false;
            _lastCountdownSecond = -1;
            _lastRemaining = float.MaxValue;
            FireCallback(CallbackEvent.JumpStarted, new Dictionary<string, string> { { "{target}", FormatGPS(_predictedTarget) } });
        }

        private void Fail(Status status, bool execute)
        {
            _status = status;
            _predictedTarget = null;
            if (!execute)
                return;

            string message;
            StatusMessages.TryGetValue(status, out message);
            FireCallback(CallbackEvent.JumpFailed, new Dictionary<string, string>
            {
                { "{status}", message },
                { "{code}", ((int)status).ToString() }
            });
        }

        private bool GetBlindJumpTarget(out Vector3D? target, out bool clamp)
        {
            target = null;
            clamp = UseClamp;

            if (!_blindDirection.HasValue || Vector3D.IsZero(_blindDirection.Value))
                return false;

            IMyGridJumpDriveSystem jumpSystem = _block.CubeGrid?.JumpSystem;
            double distance = _blindDistance;
            if (distance <= 0)
            {
                // No distance set: jump as far as the drives allow, like the vanilla blind jump.
                distance = jumpSystem?.GetMaxJumpDistance(_block.OwnerId) ?? 0;
                clamp = true;
            }

            Vector3D direction = Vector3D.Normalize(_blindDirection.Value);
            target = _block.CubeGrid.WorldMatrix.Translation + direction * distance;
            return true;
        }

        /// <summary>Checks a jump to jumpTarget and moves jumpTarget to where the ship would really arrive.</summary>
        private Status ValidateJump(ref Vector3D jumpTarget, bool clamp)
        {
            IMyCubeGrid grid = _block.CubeGrid;
            if (grid == null)
                return Status.NoGrid;

            IMyGridJumpDriveSystem jumpSystem = grid.JumpSystem;
            long userId = _block.OwnerId;
            Vector3D gridPos = grid.WorldMatrix.Translation;

            if (!jumpSystem.IsJumpValid(userId))
                return Status.Immovable;

            double maxDistance = jumpSystem.GetMaxJumpDistance(userId);
            if (clamp)
            {
                Vector3D offset = jumpTarget - gridPos;
                double length = offset.Length();
                if (length > maxDistance && length > 0)
                    jumpTarget = gridPos + offset * (maxDistance / length);
            }

            if (IsWithinGravity(jumpTarget) || IsWithinGravity(gridPos))
                return Status.Gravity;

            if (IsOutsideWorld(jumpTarget))
                return Status.OutsideWorld;

            IMyEntity intersection = GetIntersectionWithLine(gridPos, jumpTarget);
            if (intersection != null)
            {
                RecordBlocking(intersection);
                if (intersection is MyPlanet)
                    return Status.Planet;

                Vector3D point = intersection.WorldMatrix.Translation;
                Vector3D closestPointOnLine = MyUtils.GetClosestPointOnLine(ref gridPos, ref jumpTarget, ref point);
                float halfExtents = intersection.PositionComp.LocalAABB.HalfExtents.Length();
                jumpTarget = closestPointOnLine - Vector3D.Normalize(jumpTarget - gridPos) * halfExtents;
            }

            Vector3D? newTarget = jumpSystem.FindSuitableJumpLocation(jumpTarget);
            if (!newTarget.HasValue)
                return Status.NoClearSpace;
            jumpTarget = newTarget.Value;

            double distance = (jumpTarget - gridPos).Length();
            if (distance < jumpSystem.GetMinJumpDistance(userId))
                return Status.TooClose;

            // With clamping on, the game itself shortens a jump that the clear-space search pushed past the max range.
            if (!clamp && distance > maxDistance)
                return Status.TooFar;

            return Status.Success;
        }

        /// <summary>
        /// Remembers what is in the jump path. Names and positions of ships are only given when the
        /// ship is friendly to the drive owner, so scripts cannot use jump checks to find hidden enemies.
        /// </summary>
        private void RecordBlocking(IMyEntity entity)
        {
            if (entity is MyPlanet)
                _blockingType = "Planet";
            else if (entity is IMyVoxelBase)
                _blockingType = "Asteroid";
            else if (entity is IMyCubeGrid)
                _blockingType = "Grid";
            else
                _blockingType = "Other";

            IMyCubeGrid grid = entity as IMyCubeGrid;
            if (grid != null && !IsFriendlyGrid(grid))
                return;
            if (grid == null && !(entity is IMyVoxelBase))
                return;

            _blockingName = entity.DisplayName ?? "";
            _blockingPosition = entity.WorldMatrix.Translation;
        }

        private bool IsFriendlyGrid(IMyCubeGrid grid)
        {
            long owner = _block.OwnerId;
            if (owner == 0)
                return false;

            IMyFaction ownFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);
            foreach (long bigOwner in grid.BigOwners)
            {
                if (bigOwner == owner)
                    return true;
                if (ownFaction != null && ownFaction.IsMember(bigOwner))
                    return true;
            }
            return false;
        }

        /// <summary>The countdown in seconds, kept inside the range the server allows.</summary>
        private float GetJumpDelay()
        {
            JumpDriveApiConfig config = JumpDriveApiSession.Config;
            float delay = _jumpDelay > 0 ? _jumpDelay : 10f;
            return (float)MathHelper.Clamp(delay, config.MinJumpDelaySeconds, config.MaxJumpDelaySeconds);
        }

        // ============================================================
        //  Grid information
        // ============================================================

        /// <summary>Counts drives the owner may use, the way the game does (MyJumpDrive.CanJumpAndHasAccess).</summary>
        private static int CountDrives(IMyCubeGrid grid, long userId, bool readyOnly)
        {
            int count = 0;
            if (grid == null)
                return 0;

            foreach (IMyJumpDrive drive in grid.GetFatBlocks<IMyJumpDrive>())
            {
                if (!readyOnly)
                {
                    count++;
                    continue;
                }
                if (drive.IsWorking && drive.IsFunctional && drive.CurrentStoredPower >= drive.MaxStoredPower
                    && drive.GetUserRelationToOwner(userId).IsFriendly())
                    count++;
            }
            return count;
        }

        /// <summary>
        /// The heaviest the ship may be and still reach ScriptJumpTarget, in kg, with every working
        /// drive charged. The game's range is min(sum of D, sum of D * MaxJumpMass / mass), with D the
        /// drive's max distance (MyGridJumpDriveSystem.GetMaxJumpDistance), so mass must be at most
        /// sum(D * MaxJumpMass) / distance. Returns 0 when no mass is light enough.
        /// </summary>
        private double GetMaxMassForTarget()
        {
            IMyCubeGrid grid = _block.CubeGrid;
            if (!_target.HasValue || grid == null)
                return 0;

            double distance = (_target.Value - grid.WorldMatrix.Translation).Length();
            double sumDistance = 0;
            double sumDistanceMass = 0;
            foreach (IMyJumpDrive drive in grid.GetFatBlocks<IMyJumpDrive>())
            {
                if (!drive.IsFunctional || !drive.Enabled || !drive.GetUserRelationToOwner(_block.OwnerId).IsFriendly())
                    continue;
                MyJumpDriveDefinition def = MyDefinitionManager.Static.GetCubeBlockDefinition(drive.BlockDefinition) as MyJumpDriveDefinition;
                if (def == null)
                    continue;
                sumDistance += def.MaxJumpDistance;
                sumDistanceMass += def.MaxJumpDistance * def.MaxJumpMass;
            }

            if (distance <= 0)
                return double.MaxValue;
            if (distance > sumDistance)
                return 0;
            return sumDistanceMass / distance;
        }

        /// <summary>Asks for the recharge estimate to be kept up to date for the next minute.</summary>
        private double ReadRechargeSeconds()
        {
            int frame = MyAPIGateway.Session.GameplayFrameCounter;
            if (frame - _rechargeReadFrame > 3600)
            {
                // Not read recently: the old sample is stale.
                _rechargeLastStored = -1f;
                _rechargeSeconds = -1;
            }
            _rechargeReadFrame = frame;

            float stored, max;
            GetGridCharge(_block.CubeGrid, out stored, out max);
            if (max > 0 && stored >= max)
                return 0;
            return _rechargeSeconds;
        }

        /// <summary>Every 100 frames, while a script reads RechargeTimeLeft, measures how fast the grid charges.</summary>
        private void SampleRecharge()
        {
            int frame = MyAPIGateway.Session.GameplayFrameCounter;
            if (frame - _rechargeReadFrame > 3600)
                return;

            float stored, max;
            GetGridCharge(_block.CubeGrid, out stored, out max);

            if (_rechargeLastStored >= 0f && frame > _rechargeLastFrame)
            {
                double perSecond = (stored - _rechargeLastStored) / ((frame - _rechargeLastFrame) / FramesPerSecond);
                _rechargeSeconds = perSecond > 0 ? (max - stored) / perSecond : -1;
            }
            _rechargeLastStored = stored;
            _rechargeLastFrame = frame;
        }

        // ============================================================
        //  Callback system
        // ============================================================

        /// <summary>
        /// Queues a callback for the next frame. Running it now would fail when the
        /// callback block is the one whose script is running the action.
        /// </summary>
        private void FireCallback(CallbackEvent eventType, Dictionary<string, string> tokens)
        {
            if (!JumpDriveApiSession.Config.CallbacksEnabled)
                return;
            if ((_callbackEvents & (long)eventType) == 0 || _callbackPB == 0)
                return;

            _pendingCallbacks.Add(BuildCallbackArg(tokens));
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        private void RunPendingCallbacks()
        {
            if (_pendingCallbacks.Count == 0)
                return;

            // Swap lists so callbacks queued by the scripts we run go to the next frame.
            List<string> callbacks = _pendingCallbacks;
            _pendingCallbacks = _runningCallbacks;
            _runningCallbacks = callbacks;

            IMyProgrammableBlock pb = GetCallbackBlock();
            if (pb != null)
            {
                foreach (string arg in callbacks)
                    pb.Run(arg);
            }
            callbacks.Clear();
        }

        private IMyProgrammableBlock GetCallbackBlock()
        {
            if (_callbackPB == 0 || MyAPIGateway.Session?.IsServer != true)
                return null;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(_callbackPB, out entity))
                return null;

            IMyProgrammableBlock pb = entity as IMyProgrammableBlock;
            if (pb == null || !IsCallbackAllowed(pb))
                return null;
            return pb;
        }

        /// <summary>Stops a script from running another player's programmable block through this drive.</summary>
        private bool IsCallbackAllowed(IMyProgrammableBlock pb)
        {
            MyRelationsBetweenPlayerAndBlock relation = pb.GetUserRelationToOwner(_block.OwnerId);

            if (pb.CubeGrid.IsSameConstructAs(_block.CubeGrid))
                return relation != MyRelationsBetweenPlayerAndBlock.Enemies;

            if (!JumpDriveApiSession.Config.AllowCallbacksToOtherGrids)
                return false;
            return relation == MyRelationsBetweenPlayerAndBlock.Owner || relation == MyRelationsBetweenPlayerAndBlock.FactionShare;
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

        /// <summary>Called every 10th frame on the server to detect state changes and fire callbacks.</summary>
        private void MonitorCallbacks(IMyGridJumpDriveSystem jumpSystem)
        {
            if (_callbackPB == 0 || _callbackEvents == 0 || !JumpDriveApiSession.Config.CallbacksEnabled)
            {
                _monitorInitialized = false;
                return;
            }

            double currentMax = jumpSystem.GetMaxJumpDistance(_block.OwnerId);
            float chargePercent = _block.MaxStoredPower > 0f ? _block.CurrentStoredPower / _block.MaxStoredPower : 0f;
            bool watchGridCharge = (_callbackEvents & (long)CallbackEvent.GridRecharged) != 0;
            float gridChargePercent = watchGridCharge ? GetGridChargePercent(_block.CubeGrid) : 0f;
            int frame = MyAPIGateway.Session.GameplayFrameCounter;

            if (!_monitorInitialized)
            {
                // First pass after registering: record the current values without firing anything.
                _lastMaxDistance = currentMax;
                _lastReportedRange = currentMax;
                _lastRangeReportFrame = frame;
                _lastChargePercent = chargePercent;
                _lastGridChargePercent = gridChargePercent;
                _monitorInitialized = true;
            }
            else
            {
                MonitorRange(currentMax, frame);

                if (_lastChargePercent < 0.999f && chargePercent >= 0.999f)
                    FireCallback(CallbackEvent.Recharged, new Dictionary<string, string> { { "{charge}", "100" } });
                _lastChargePercent = chargePercent;

                if (watchGridCharge && _lastGridChargePercent < 0.999f && gridChargePercent >= 0.999f)
                    FireCallback(CallbackEvent.GridRecharged, new Dictionary<string, string> { { "{charge}", "100" } });
                _lastGridChargePercent = gridChargePercent;
            }
        }

        private void MonitorRange(double currentMax, int frame)
        {
            if (Math.Abs(currentMax - _lastMaxDistance) > 0.01)
            {
                double oldMax = _lastMaxDistance;
                _lastMaxDistance = currentMax;

                if (_callbackThreshold > 0)
                {
                    Dictionary<string, string> tokens = RangeTokens(currentMax);
                    if (oldMax <= _callbackThreshold && currentMax > _callbackThreshold)
                        FireCallback(CallbackEvent.RangeAbove, tokens);
                    if (oldMax >= _callbackThreshold && currentMax < _callbackThreshold)
                        FireCallback(CallbackEvent.RangeBelow, tokens);
                    const double epsilon = 0.01;
                    if (Math.Abs(oldMax - _callbackThreshold) > epsilon && Math.Abs(currentMax - _callbackThreshold) <= epsilon)
                        FireCallback(CallbackEvent.RangeEquals, tokens);
                }
            }

            // RangeChanged is limited by size and time, so mining or refining does not run the script every 10 frames.
            JumpDriveApiConfig config = JumpDriveApiSession.Config;
            double step = Math.Max(0.01, Math.Max(_callbackRangeStep, config.MinRangeChangeMeters));
            double minFrames = config.MinRangeChangeIntervalSeconds * FramesPerSecond;
            if (Math.Abs(currentMax - _lastReportedRange) >= step && frame - _lastRangeReportFrame >= minFrames)
            {
                _lastReportedRange = currentMax;
                _lastRangeReportFrame = frame;
                FireCallback(CallbackEvent.RangeChanged, RangeTokens(currentMax));
            }
        }

        private Dictionary<string, string> RangeTokens(double range)
        {
            return new Dictionary<string, string>
            {
                { "{range}", range.ToString("F1") },
                { "{threshold}", _callbackThreshold.ToString("F1") }
            };
        }

        /// <summary>
        /// Follows a jump this mod started. The game keeps Jumped set for about 36 frames after
        /// the teleport, so a jump that ends without it was cancelled.
        /// </summary>
        private void MonitorJump(IMyGridJumpDriveSystem jumpSystem)
        {
            if (!_jumpInitiated)
                return;

            if (jumpSystem.IsJumping)
            {
                if (jumpSystem.Jumped)
                {
                    if (!_jumpArrived)
                    {
                        _jumpArrived = true;
                        _arrivalPosition = _block.CubeGrid.WorldMatrix.Translation;
                    }
                    return;
                }

                float? remaining = jumpSystem.GetRemainingJumpTime();
                if (remaining.HasValue)
                {
                    _lastRemaining = remaining.Value;
                    int seconds = (int)Math.Ceiling(remaining.Value);
                    if (seconds > 0 && seconds != _lastCountdownSecond)
                    {
                        _lastCountdownSecond = seconds;
                        FireCallback(CallbackEvent.JumpCountdown, new Dictionary<string, string> { { "{seconds}", seconds.ToString() } });
                    }
                }
                return;
            }

            _jumpInitiated = false;
            if (_jumpArrived)
            {
                _hasLastJump = true;
                _lastJumpOrigin = _jumpOrigin;
                _lastJumpDestination = _arrivalPosition;
                _settingsChanged = true;
                FireCallback(CallbackEvent.JumpCompleted, new Dictionary<string, string> { { "{target}", FormatGPS(_arrivalPosition) } });
            }
            else
            {
                FireCallback(CallbackEvent.JumpAborted, new Dictionary<string, string> { { "{reason}", GetAbortReason() } });
            }
        }

        /// <summary>
        /// The game does not tell mods why it cancelled a jump, so work it out from what it checks:
        /// during the countdown it aborts when no drive can jump any more (MyGridJumpDriveSystem
        /// UpdateJumpDriveSystem), and at the end when the target is no longer valid.
        /// </summary>
        private string GetAbortReason()
        {
            if (CountDrives(_block.CubeGrid, _block.OwnerId, readyOnly: true) == 0)
                return "NoDrives";
            // The final check (clear space at the target, range, grid not locked or static) failed.
            if (_lastRemaining <= 0.5f)
                return "Blocked";
            return "Cancelled";
        }

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
                _callbackRangeStep = 0;
                _settingsChanged = true;
            }
        }

        // ============================================================
        //  Saving with the block
        // ============================================================

        public class StorageData
        {
            public bool HasTarget;
            public SerializableVector3D Target;
            public bool ClampToMaxRange;
            public bool HasBlindDirection;
            public SerializableVector3D BlindDirection;
            public double BlindDistance;
            public string GpsName = "";
            public float JumpDelay;
            public bool HasLastJump;
            public SerializableVector3D LastJumpOrigin;
            public SerializableVector3D LastJumpDestination;
            public long CallbackPB;
            public long CallbackEvents;
            public string CallbackArg = "";
            public double CallbackRangeThreshold;
            public double CallbackRangeStep;
        }

        private bool HasSettingsToSave()
        {
            return _target.HasValue || _clampToMaxRange || _blindDirection.HasValue || _blindDistance != 0
                || !string.IsNullOrEmpty(_gpsName) || _jumpDelay != 0 || _hasLastJump
                || _callbackPB != 0 || _callbackEvents != 0 || !string.IsNullOrEmpty(_callbackArg)
                || _callbackThreshold != 0 || _callbackRangeStep != 0;
        }

        private void SaveToStorage()
        {
            if (MyAPIGateway.Session?.IsServer != true)
                return;

            if (!HasSettingsToSave())
            {
                Entity.Storage?.RemoveValue(StorageGuid);
                return;
            }

            StorageData data = new StorageData
            {
                HasTarget = _target.HasValue,
                Target = _target ?? Vector3D.Zero,
                ClampToMaxRange = _clampToMaxRange,
                HasBlindDirection = _blindDirection.HasValue,
                BlindDirection = _blindDirection ?? Vector3D.Zero,
                BlindDistance = _blindDistance,
                GpsName = _gpsName,
                JumpDelay = _jumpDelay,
                HasLastJump = _hasLastJump,
                LastJumpOrigin = _lastJumpOrigin,
                LastJumpDestination = _lastJumpDestination,
                CallbackPB = _callbackPB,
                CallbackEvents = _callbackEvents,
                CallbackArg = _callbackArg,
                CallbackRangeThreshold = _callbackThreshold,
                CallbackRangeStep = _callbackRangeStep
            };

            if (Entity.Storage == null)
                Entity.Storage = new MyModStorageComponent();
            Entity.Storage.SetValue(StorageGuid, MyAPIGateway.Utilities.SerializeToXML(data));
        }

        private void LoadFromStorage()
        {
            string xml;
            if (MyAPIGateway.Session?.IsServer != true || Entity.Storage == null || !Entity.Storage.TryGetValue(StorageGuid, out xml))
                return;

            try
            {
                StorageData data = MyAPIGateway.Utilities.SerializeFromXML<StorageData>(xml);
                if (data == null)
                    return;

                _target = data.HasTarget ? (Vector3D?)data.Target : null;
                _clampToMaxRange = data.ClampToMaxRange;
                _blindDirection = data.HasBlindDirection ? (Vector3D?)data.BlindDirection : null;
                _blindDistance = data.BlindDistance;
                _gpsName = data.GpsName ?? "";
                _jumpDelay = data.JumpDelay;
                _hasLastJump = data.HasLastJump;
                _lastJumpOrigin = data.LastJumpOrigin;
                _lastJumpDestination = data.LastJumpDestination;
                _callbackPB = data.CallbackPB;
                _callbackEvents = data.CallbackEvents;
                _callbackArg = data.CallbackArg ?? "";
                _callbackThreshold = data.CallbackRangeThreshold;
                _callbackRangeStep = data.CallbackRangeStep;
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("JumpDriveAPI: could not load saved settings for block " + Entity.EntityId + ". " + e.Message);
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

        // Scripts often read GridStoredPower and GridMaxStoredPower together, and the monitor
        // reads them for every drive. One scan per grid per frame serves all of them.
        private static long _chargeGridId;
        private static int _chargeFrame = -1;
        private static float _chargeStored;
        private static float _chargeMax;

        private static void GetGridCharge(IMyCubeGrid grid, out float stored, out float max)
        {
            stored = 0f;
            max = 0f;
            if (grid == null)
                return;

            int frame = MyAPIGateway.Session?.GameplayFrameCounter ?? -1;
            if (frame >= 0 && frame == _chargeFrame && grid.EntityId == _chargeGridId)
            {
                stored = _chargeStored;
                max = _chargeMax;
                return;
            }

            foreach (IMyJumpDrive drive in grid.GetFatBlocks<IMyJumpDrive>())
            {
                if (!drive.IsFunctional)
                    continue;
                stored += drive.CurrentStoredPower;
                max += drive.MaxStoredPower;
            }

            _chargeGridId = grid.EntityId;
            _chargeFrame = frame;
            _chargeStored = stored;
            _chargeMax = max;
        }

        private static float GetGridChargePercent(IMyCubeGrid grid)
        {
            float stored, max;
            GetGridCharge(grid, out stored, out max);
            return max > 0f ? stored / max : 0f;
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

        /// <summary>
        /// How far the ship must go to leave the nearest planet's gravity, and which way.
        /// Returns false when the ship is not in planet gravity.
        /// </summary>
        // GravityEscapeDistance and GravityEscapeDirection are read together; compute once per frame.
        private int _gravityFrame = -1;
        private bool _gravityResult;
        private double _gravityDistance;
        private Vector3D _gravityDirection;

        private bool GetGravityEscapeCached(out double distance, out Vector3D direction)
        {
            int frame = MyAPIGateway.Session?.GameplayFrameCounter ?? -1;
            if (frame < 0 || frame != _gravityFrame)
            {
                _gravityResult = GetGravityEscape(out _gravityDistance, out _gravityDirection);
                _gravityFrame = frame;
            }
            distance = _gravityDistance;
            direction = _gravityDirection;
            return _gravityResult;
        }

        private bool GetGravityEscape(out double distance, out Vector3D direction)
        {
            distance = 0;
            direction = Vector3D.Zero;

            IMyCubeGrid grid = _block.CubeGrid;
            if (grid == null)
                return false;

            Vector3D pos = grid.WorldMatrix.Translation;
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(pos);
            if (planet == null || !IsWithinGravity(pos, planet))
                return false;

            MyGravityProviderComponent gravity = planet.Components.Get<MyGravityProviderComponent>();
            Vector3D fromCenter = pos - planet.PositionComp.GetPosition();
            double fromCenterLength = fromCenter.Length();
            if (fromCenterLength < 1)
                fromCenter = Vector3D.Up;

            direction = Vector3D.Normalize(fromCenter);
            // Add the ship's own size, so the whole ship ends up outside.
            distance = Math.Max(0, gravity.GravityLimit - fromCenterLength) + grid.WorldVolume.Radius;
            return true;
        }

        private bool IsWithinGravity(Vector3D pos, MyPlanet planet)
        {
            MyGravityProviderComponent gravity = planet.Components.Get<MyGravityProviderComponent>();
            return gravity != null && gravity.IsPositionInRange(pos);
        }
    }
}
