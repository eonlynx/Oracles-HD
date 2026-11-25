using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using ProjectZ.InGame.GameObjects.Things;
using ProjectZ.InGame.Map;
using ProjectZ.InGame.Scripting;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.Map
{
    /// <summary>
    /// Owns the room's active script runners. Bindings are generated from the
    /// object initializers: an actor whose (interaction, subid) has a source
    /// binding starts that program when the room is loaded. There is no
    /// room/object special case in the runtime.
    /// </summary>
    internal sealed class OracleScriptRuntime
    {
        private static readonly bool TraceEnabled =
            Environment.GetEnvironmentVariable("ORACLE_SCRIPT_TRACE") == "1";

        private readonly OracleSessionState _session;
        private readonly CutsceneScriptTable _table;
        private readonly List<CutsceneCommandRunner> _runners = new();
        private readonly List<CutsceneCommandRunner> _finished = new();
        private readonly List<ObjOracleSprite> _actors = new();

        private Map _map;
        private OracleMapInfo _info;

        // Native wcddb is the shared script flag byte. Only bit 7 is consumed
        // by the imported helpers today.
        private int _wcddb;

        public long FrameCounter { get; private set; }
        public bool Enabled => _table != null;

        public OracleScriptRuntime(OracleSessionState session, OracleGameProfile profile)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            if (CutsceneCommandCatalog.TryLoad(profile, out var table))
            {
                _table = table;
                Trace($"loaded {table.Commands.Count} commands for {profile.Game}");
            }
            else
            {
                Trace($"disabled for {profile.Game}");
            }
        }

        public void OnMapLoading()
        {
            _map = null;
            _info = null;
            _actors.Clear();
            _runners.Clear();
            _finished.Clear();
        }

        internal static void Trace(string message)
        {
            if (TraceEnabled)
                Console.WriteLine("oracle-script: " + message);
        }

        public void OnMapLoaded(Map map)
        {
            OnMapLoading();
            _map = map;
            _info = map?.OracleInfo;
            if (_table == null || _map == null || _info == null)
                return;

            var objects = _map.Objects.GetObjectsOfType(typeof(ObjOracleSprite));
            foreach (var gameObject in objects)
                if (gameObject is ObjOracleSprite actor && !actor.IsDead && actor.IsActive)
                    _actors.Add(actor);

            BindRoom(_info.Group, OracleRoomRuntime.ResolveRoom(_info,
                MapManager.ObjLink?.EntityPosition.Position ?? Vector2.Zero),
                _session.EntryDirection);
            UpdatePresentation();
        }

        /// <summary>
        /// Called when Link crosses into another stitched-map room. Only source
        /// bindings for that room are considered, and an entry-direction-gated
        /// actor stays dormant until the native direction matches.
        /// </summary>
        public void RoomEntered(int group, int room, int direction)
        {
            if (_table == null || _map == null || _info == null)
                return;
            BindRoom(group, room, direction);
            UpdatePresentation();
        }

        /// <summary>
        /// Presents entry-gated actors for the room Link occupies and for the
        /// rooms visible around it. The direction used for a neighbouring room
        /// is the one Link would use to cross the shared edge, mirroring top's
        /// ghost capture; the script still starts only on the real crossing.
        /// </summary>
        public void UpdatePresentation()
        {
            if (_info == null || _session.Room < 0)
                return;

            int currentRoom = _session.Room;
            int currentX = currentRoom & 0x0f;
            int currentY = (currentRoom >> 4) & 0x0f;

            foreach (var actor in _actors)
            {
                if (actor.IsDead || !actor.IsActive)
                    continue;
                if (actor.ScriptBound || actor.ScriptCompleted)
                    continue;

                if (_table.TryGetBinding(actor.InteractionId, actor.SubId, out var binding) &&
                    binding.CompletedFlag >= 0 && _session.GetFlag(binding.CompletedFlag))
                {
                    actor.ScriptCompleted = true;
                    actor.SetScriptVisible(false);
                    if (binding.DeleteOnEnd && !actor.IsDead)
                        _map.Objects.DeleteObjects.Add(actor);
                    continue;
                }

                if (actor.RequiresEntryDirection < 0)
                    continue;

                var actorRoom = OracleRoomRuntime.ResolveRoom(_info,
                    actor.EntityPosition.Position);
                if (actorRoom == currentRoom)
                {
                    SetPresentation(actor,
                        _session.EntryDirection == actor.RequiresEntryDirection,
                        _session.EntryDirection);
                    continue;
                }
                int actorX = actorRoom & 0x0f;
                int actorY = (actorRoom >> 4) & 0x0f;
                int dx = actorX - currentX;
                int dy = actorY - currentY;
                int direction;
                if (dx == 0 && dy == 0)
                    direction = _session.EntryDirection;
                else if (Math.Abs(dx) >= Math.Abs(dy))
                    direction = dx > 0 ? 1 : 3;
                else
                    direction = dy > 0 ? 2 : 0;

                SetPresentation(actor, direction == actor.RequiresEntryDirection, direction);
            }
        }

        private static void SetPresentation(ObjOracleSprite actor, bool visible, int direction)
        {
            if (actor.ScriptVisible == visible)
                return;
            actor.SetScriptVisible(visible);
            Trace($"presentation ${actor.InteractionId:x2}:${actor.SubId:x2} " +
                  $"visible={visible} prospective dir={direction}");
        }

        private void BindRoom(int group, int room, int direction)
        {
            foreach (var actor in _actors)
            {
                if (actor.IsDead || !actor.IsActive)
                    continue;
                if (!_table.TryGetBinding(actor.InteractionId, actor.SubId, out var binding))
                    continue;
                if (actor.ScriptBound || actor.ScriptCompleted)
                    continue;

                if (binding.CompletedFlag >= 0 && _session.GetFlag(binding.CompletedFlag))
                {
                    actor.ScriptCompleted = true;
                    actor.SetScriptVisible(false);
                    if (binding.DeleteOnEnd && !actor.IsDead)
                        _map.Objects.DeleteObjects.Add(actor);
                    continue;
                }

                var actorRoom = OracleRoomRuntime.ResolveRoom(_info,
                    actor.EntityPosition.Position);
                if (actorRoom != room)
                    continue;

                if (actor.RequiresEntryDirection >= 0 &&
                    direction != actor.RequiresEntryDirection)
                {
                    actor.SetScriptVisible(false);
                    continue;
                }

                // ralph.s:@initSubid0d calls objectSetVisiblec0 when the
                // entry direction matches.
                actor.SetScriptVisible(true);

                var host = new OracleScriptHost(_session, this, actor, binding);
                var runner = new CutsceneCommandRunner(_table, host);
                try
                {
                    runner.Start(binding.Script);
                    actor.ScriptBound = true;
                    _runners.Add(runner);
                    Trace($"start {binding.Script} (${binding.Interaction:x2}:${binding.SubId:x2}, " +
                          $"group {group:x}, room {room:x2}, dir {direction})");
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(
                        $"Oracle script binding ${binding.Interaction:x2}:${binding.SubId:x2} " +
                        $"could not start: {exception.Message}");
                }
            }
        }

        public void Update()
        {
            if (_table == null)
                return;

            FrameCounter++;
            _finished.Clear();
            foreach (var runner in _runners)
            {
                runner.Update();
                if (TraceEnabled && FrameCounter % 30 == 0)
                    Trace($"tick {runner.Script} instr {runner.Instruction} " +
                          $"c1 {runner.Counter1} c2 {runner.Counter2} active {runner.Active}");
                if (!runner.Active)
                    _finished.Add(runner);
            }
            foreach (var runner in _finished)
            {
                Trace($"end {runner.Script}");
                _runners.Remove(runner);
            }
        }

        public int ReadMemory(int address)
        {
            return address switch
            {
                1 => _wcddb,
                _ => throw new InvalidOperationException(
                    $"Oracle script reads unsupported memory address ${address:x2}."),
            };
        }

        public void WriteMemory(int address, int value)
        {
            if (address != 1)
                throw new InvalidOperationException(
                    $"Oracle script writes unsupported memory address ${address:x2}.");
            _wcddb = value & 0xff;
        }
    }

    /// <summary>Host for one scripted actor in the current room.</summary>
    internal sealed class OracleScriptHost : ICutsceneCommandHost
    {
        private readonly OracleSessionState _session;
        private readonly OracleScriptRuntime _runtime;
        private readonly ObjOracleSprite _actor;
        private readonly CutsceneScriptBinding _binding;

        private int _zFixed;
        private int _speedZ;

        public OracleScriptHost(OracleSessionState session, OracleScriptRuntime runtime,
            ObjOracleSprite actor, CutsceneScriptBinding binding)
        {
            _session = session;
            _runtime = runtime;
            _actor = actor;
            _binding = binding;
        }

        public long FrameCounter => _runtime.FrameCounter;
        public bool TextVisible => false;
        public bool ScriptExecutionBlocked => false;

        public bool ShowText(int textId, string sourceName)
        {
            // Oracle text payloads are not imported yet. This is a deliberate
            // host policy: the cutscene continues, and the text id is kept on
            // the actor for the future dialogue bridge.
            OracleScriptRuntime.Trace(
                $"showtext {sourceName} (${textId:x4}) skipped: text assets not imported");
            return false;
        }

        public void SetAnimation(int animation)
        {
            _actor.ScriptAnimation = animation & 0xff;
        }

        public void SetMovementAnimation(int frames)
        {
            // Native interactionSetAnimation(angleToDirection(angle)); the
            // generated atlas currently imports only the default animation.
            _actor.ScriptAnimation = _actor.ScriptAngle switch
            {
                0x00 => 0,
                0x08 => 1,
                0x10 => 2,
                0x18 => 3,
                _ => _actor.ScriptAnimation,
            };
        }

        public void SetSpeed(int speed)
        {
            _actor.ScriptSpeed = speed & 0xff;
        }

        public void SetAngle(int angle)
        {
            _actor.ScriptAngle = angle & 0xff;
        }

        public void ApplySpeed()
        {
            var velocity = OracleActorMovement.Velocity(_actor.ScriptSpeed, _actor.ScriptAngle);
            _actor.Body.VelocityTarget = velocity / 256f;
        }

        public void StopSpeed()
        {
            _actor.Body.VelocityTarget = Vector2.Zero;
        }

        public void WriteObjectByte(int address, int value)
        {
            if (address != 1)
                throw new InvalidOperationException(
                    $"Oracle script writes unsupported object byte ${address:x2}.");
            _actor.Var3F = value & 0xff;
        }

        public void SetActorVisible(bool visible)
        {
            _actor.SetScriptVisible(visible);
        }

        public void SetActorZ(int zFixed)
        {
            _actor.SetScriptZ(-zFixed / 256f);
        }

        public int ReadMemory(int address) => _runtime.ReadMemory(address);
        public void WriteMemory(int address, int value) => _runtime.WriteMemory(address, value);

        public bool GetGlobalFlag(int flag) => _session.GetFlag(flag);

        public void SetGlobalFlag(int flag)
        {
            _session.SetFlag(flag, true);
            OracleScriptRuntime.Trace($"global flag ${flag:x2} set by {_binding.Script}");
        }

        public void SetDisabledObjects(int value)
        {
            _session.DisabledObjects = value & 0xff;
        }

        public void PlaySound(string name)
        {
            // Sound ids are not imported yet; keep the source symbol in the
            // command stream until the audio table lands.
            Debug.WriteLine($"Oracle script sound {name} skipped: sound assets not imported.");
        }

        public void RunAsm15(string helper)
        {
            switch (helper)
            {
                case "beginJump":
                    _zFixed = 0;
                    _speedZ = -0x200;
                    _actor.BeginScriptJump();
                    WriteMemory(1, 0);
                    PlaySound("SND_JUMP");
                    break;

                case "updateGravity":
                {
                    _zFixed += _speedZ;
                    if ((_zFixed & 0x8000) == 0)
                    {
                        _zFixed = 0;
                        _speedZ = 0;
                        _actor.EndScriptJump();
                        WriteMemory(1, 0x80);
                    }
                    else
                    {
                        _speedZ += 0x30;
                        SetActorZ(_zFixed);
                        WriteMemory(1, 0);
                    }
                    break;
                }

                case "ralph_flickerVisibility":
                    SetActorVisible((FrameCounter & 1) != 0);
                    break;

                case "ralph_decVar3f":
                {
                    var value = (_actor.Var3F - 1) & 0xff;
                    _actor.Var3F = value;
                    WriteMemory(1, value == 0 ? 0x80 : 0);
                    break;
                }

                case "ralph_restoreMusic":
                    // MUS_OVERWORLD is not imported yet; the native helper
                    // restores the overworld track after the portal cutscene.
                    Debug.WriteLine("Oracle script restores overworld music (not imported).");
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Oracle script helper '{helper}' is not implemented " +
                        $"(binding ${_binding.Interaction:x2}:${_binding.SubId:x2}).");
            }
        }

        public void ScriptEnded()
        {
            _actor.ScriptCompleted = true;
            OracleScriptRuntime.Trace(
                $"scriptend {_binding.Script} (${_binding.Interaction:x2}:${_binding.SubId:x2}, " +
                $"delete={_binding.DeleteOnEnd})");
            if (_binding.DeleteOnEnd && !_actor.IsDead)
                _actor.Map.Objects.DeleteObjects.Add(_actor);
        }
    }

    /// <summary>
    /// Exact bank3.objectSpeedTable contract in 8.8 fixed point. SPEED_100
    /// ($28) is one pixel per frame on a cardinal axis; angles are the native
    /// 32-step circle (0 up, 8 right, 16 down, 24 left).
    /// </summary>
    internal static class OracleActorMovement
    {
        public static Vector2 Velocity(int speed, int angle)
        {
            if (speed <= 0)
                return Vector2.Zero;

            var magnitude = speed * 0x20 / 5;
            var step = angle & 0x1f;
            if ((step & 7) == 0)
            {
                return step switch
                {
                    0 => new Vector2(0, -magnitude),
                    8 => new Vector2(magnitude, 0),
                    16 => new Vector2(0, magnitude),
                    _ => new Vector2(-magnitude, 0),
                };
            }

            var radians = step * Math.PI / 16.0;
            var x = (int)Math.Round(Math.Sin(radians) * magnitude);
            var y = (int)Math.Round(-Math.Cos(radians) * magnitude);
            return new Vector2(x, y);
        }
    }
}
