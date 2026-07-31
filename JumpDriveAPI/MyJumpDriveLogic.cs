using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
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
        private static bool _controls;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _block = (IMyJumpDrive)Entity;
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_block.CubeGrid?.Physics == null) // ignore projected and other non-physical grids
                return;

            if(!_controls)
            {
                CreateControls();
                _controls = true;
            }
        }

        private static void CreateControls()
        {
            // Properties

            IMyTerminalControlProperty<Vector3D?> propScriptTarget = MyAPIGateway.TerminalControls.CreateProperty<Vector3D?, IMyJumpDrive>("ScriptJumpTarget");
            propScriptTarget.Getter = GetJumpTarget;
            propScriptTarget.Setter = SetJumpTarget;
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propScriptTarget);

            IMyTerminalControlProperty<Vector3D?> propRealTarget = MyAPIGateway.TerminalControls.CreateProperty<Vector3D?, IMyJumpDrive>("JumpTarget");
            propRealTarget.Getter = GetRealJumpTarget;
            propRealTarget.Setter = (b, v) => { };
            MyAPIGateway.TerminalControls.AddControl<IMyJumpDrive>(propRealTarget);


            // Actions

            IMyTerminalAction actionJump = MyAPIGateway.TerminalControls.CreateAction<IMyJumpDrive>("ScriptJump");
            actionJump.ValidForGroups = false;
            actionJump.Action = PerformJump;
            actionJump.Name = new System.Text.StringBuilder("Script Jump");
            actionJump.InvalidToolbarTypes = new List<MyToolbarType>() { MyToolbarType.Character, MyToolbarType.SmallCockpit, MyToolbarType.LargeCockpit, MyToolbarType.Ship, MyToolbarType.Seat, MyToolbarType.Spectator, MyToolbarType.BuildCockpit }; // All except button panel
            MyAPIGateway.TerminalControls.AddAction<IMyJumpDrive>(actionJump);

            IMyTerminalAction actionAbort = MyAPIGateway.TerminalControls.CreateAction<IMyJumpDrive>("AbortJump");
            actionAbort.ValidForGroups = false;
            actionAbort.Action = AbortJump;
            actionAbort.Name = new System.Text.StringBuilder("Abort Jump");
            actionAbort.InvalidToolbarTypes = new List<MyToolbarType>() { MyToolbarType.Character, MyToolbarType.SmallCockpit, MyToolbarType.LargeCockpit, MyToolbarType.Ship, MyToolbarType.Seat, MyToolbarType.Spectator, MyToolbarType.BuildCockpit }; // All except button panel
            MyAPIGateway.TerminalControls.AddAction<IMyJumpDrive>(actionAbort);

        }

        private static void AbortJump(IMyTerminalBlock block)
        {
            if (MyAPIGateway.Session?.IsServer != true)
                return;
            
            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            if (jumpSystem == null || !jumpSystem.IsJumping)
                return;

            jumpSystem.AbortJump();
        }

        private static void PerformJump(IMyTerminalBlock block)
        {
            if (MyAPIGateway.Session?.IsServer != true)
                return;

            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null || !logic._target.HasValue)
                return;

            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            if (jumpSystem == null || jumpSystem.IsJumping)
                return;

            IMyJumpDrive jumpDrive = (IMyJumpDrive)block;
            if (jumpDrive.Status != Sandbox.ModAPI.Ingame.MyJumpDriveStatus.Ready)
                return;

            Vector3D target = logic._target.Value;
            if(logic.IsJumpValid(ref target))
                jumpSystem.Jump(target, block.OwnerId);
        }

        private static void SetJumpTarget(IMyTerminalBlock block, Vector3D? pos)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return;

            logic._target = pos;
        }

        private bool IsJumpValid(ref Vector3D jumpTarget)
        {
            IMyCubeGrid grid = _block.CubeGrid;
            IMyGridJumpDriveSystem jumpSystem = grid.JumpSystem;
            long userId = _block.OwnerId;
            Vector3D gridPos = grid.WorldMatrix.Translation;

            // Check if grid is immovable
            if (!jumpSystem.IsJumpValid(userId))
                return false;

            // Check if the grid is leaving or entering gravity
            if (IsWithinGravity(jumpTarget) || IsWithinGravity(gridPos))
                return false;

	            // Check if the jump is within the world size
	            BoundingBoxD? worldBounds = MyAPIGateway.Session?.WorldBoundaries;
	            if(worldBounds.HasValue && worldBounds.Value.Contains(jumpTarget) != ContainmentType.Contains)
	                return false;

            // Check if a planet is in the way
            IMyEntity intersection = GetIntersectionWithLine(gridPos, jumpTarget);
            if(intersection != null)
            {
                if (intersection is MyPlanet)
                    return false;

                Vector3D point = intersection.WorldMatrix.Translation;
                Vector3D closestPointOnLine = MyUtils.GetClosestPointOnLine(ref gridPos, ref jumpTarget, ref point);
                float halfExtents = intersection.PositionComp.LocalAABB.HalfExtents.Length();
                jumpTarget = closestPointOnLine - Vector3D.Normalize(jumpTarget - gridPos) * halfExtents;
            }

            // Check if there is an available place to jump to
            Vector3D? newTarget = jumpSystem.FindSuitableJumpLocation(jumpTarget);
            if (!newTarget.HasValue)
                return false;
            jumpTarget = newTarget.Value;

            // Check if grid is too close or too far
            double distance = (jumpTarget - gridPos).Length();
            if (distance < jumpSystem.GetMinJumpDistance(userId) || distance > jumpSystem.GetMaxJumpDistance(userId))
                return false;

            return true;
        }

        private IMyEntity GetIntersectionWithLine(Vector3D start, Vector3D end)
        {
            LineD line = new LineD(start, end);
            VRage.Game.Models.MyIntersectionResultLineTriangleEx? intersectionWithLine = MyEntities.GetIntersectionWithLine(ref line, (MyEntity)_block.CubeGrid, null, ignoreChildren: true, ignoreFloatingObjects: true, ignoreHandWeapons: true, ignoreObjectsWithoutPhysics: false, ignoreSubgridsOfIgnoredEntities: true);

            if (!intersectionWithLine.HasValue)
                return null;
            return intersectionWithLine.Value.Entity;
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

        private static Vector3D? GetJumpTarget(IMyTerminalBlock block)
        {
            MyJumpDriveLogic logic = block.GameLogic.GetAs<MyJumpDriveLogic>();
            if (logic == null)
                return null;

            return logic._target;
        }

        private static Vector3D? GetRealJumpTarget(IMyTerminalBlock block)
        {
            IMyGridJumpDriveSystem jumpSystem = block.CubeGrid?.JumpSystem;
            if (jumpSystem == null)
                return null;

            return jumpSystem.GetJumpDriveTarget();
        }
    }
}