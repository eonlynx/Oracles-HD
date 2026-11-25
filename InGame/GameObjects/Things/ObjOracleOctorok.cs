using System;
using Microsoft.Xna.Framework;
using ProjectZ.InGame.GameObjects.Base;
using ProjectZ.InGame.GameObjects.Base.CObjects;
using ProjectZ.InGame.GameObjects.Base.Components;
using ProjectZ.InGame.GameObjects.Base.Components.AI;
using ProjectZ.InGame.Map;
using ProjectZ.InGame.SaveLoad;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.GameObjects.Things
{
    /// <summary>
    /// First Oracle enemy host: ENEMY_OCTOROK $09:$00..$02. It uses the
    /// native Body/AI/hittable/damage/pushable/shadow components and the
    /// imported Oracle OAM frames. The state machine follows octorok.s:
    /// decide, 16-frame windup, PART_OCTOROK_PROJECTILE spawn, 32-frame wait.
    /// </summary>
    internal class ObjOracleOctorok : GameObject
    {
        // Direction order is left, up, right, down (AnimationHelper).
        private static readonly Vector2[] ShotOffset =
        {
            new(-8, -1), new(0, -6), new(8, -1), new(0, 11),
        };

        // Native octorok.s tables (frames at 60 Hz).
        private static readonly int[] CounterValues = { 30, 45, 60, 75, 45, 60, 75, 90 };
        private static readonly int[] WalkCounterValues = { 25, 33, 41, 49 };

        private readonly BodyComponent _body;
        private readonly AiComponent _ai;
        private readonly AiDamageState _damageState;
        private readonly CSprite _sprite;
        private readonly DictAtlasEntry[] _frames;
        private readonly int[] _durations;
        private readonly int _loopStart;
        private readonly DictAtlasEntry[][] _framesByDirection = new DictAtlasEntry[4][];
        private readonly int[][] _durationsByDirection = new int[4][];
        private readonly Vector2[] _offsetByDirection = new Vector2[4];
        private readonly int _subId;
        private readonly int _counterMask;
        private readonly float _walkSpeed;

        private readonly DictAtlasEntry[] _shotFrames;
        private readonly string _shotFrameIds;
        private readonly string _shotDurationValues;
        private readonly int _shotOffsetX;
        private readonly int _shotOffsetY;
        private readonly float _shotSpeed;
        private readonly int _shotDamage;
        private readonly int _shotLifetime;

        private int _frame;
        private float _elapsed;
        private int _direction;
        private int _lastLives;
        private bool _deathLogged;
        private float _knockbackTime;
        private Vector2 _knockback;
        private bool _knockbackActive;
        private float _walkCounter;
        private float _idleCounter;
        private float _shootCounter;
        private float _pendingIdleFrames;

        public ObjOracleOctorok(Map.Map map, int posX, int posY, string frames,
            string durations, int offsetX, int offsetY, int subId, int health,
            int damage, int speed, int counterMask,
            string shotFrames, string shotDurations, int shotOffsetX, int shotOffsetY,
            int shotSpeed, int shotDamage, int shotLifetime,
            string directionSpec) : base(map)
        {
            Tags = Values.GameObjectTag.Enemy;
            _subId = subId;
            _counterMask = Math.Max(1, counterMask);
            _walkSpeed = Math.Max(0.1f, speed / 40f);

            _frames = OracleSpriteFrames.Load(frames);
            if (_frames.Length == 0)
            {
                IsDead = true;
                return;
            }
            _durations = OracleSpriteFrames.LoadDurations(durations, _frames.Length);
            _loopStart = 0;
            LoadDirectionalFrames(directionSpec, offsetX, offsetY);

            _shotFrames = OracleSpriteFrames.Load(shotFrames);
            _shotFrameIds = shotFrames;
            _shotDurationValues = shotDurations;
            _shotOffsetX = shotOffsetX;
            _shotOffsetY = shotOffsetY;
            _shotSpeed = Math.Max(0.25f, shotSpeed / 40f);
            _shotDamage = Math.Max(1, shotDamage);
            _shotLifetime = Math.Max(1, shotLifetime);

            // Oracle object positions are the top-left of the 16x16 OAM cell;
            // center X and place the feet 8px below it. The previous +12 feet
            // anchor left the body 4px inside the solid tree row.
            EntityPosition = new CPosition(posX + 8, posY + 8, 0);
            var width = _frames[0].SourceRectangle.Width;
            var height = _frames[0].SourceRectangle.Height;
            _sprite = new CSprite(_framesByDirection[0][0], EntityPosition,
                _offsetByDirection[0]);

            var radiusX = 6;
            var radiusY = 6;
            _body = new BodyComponent(EntityPosition, -radiusX, -radiusY * 2,
                radiusX * 2, radiusY * 2, 8)
            {
                MoveCollision = OnCollision,
                AbsorbPercentage = 0.9f,
                CollisionTypes = Values.CollisionTypes.Normal |
                                 Values.CollisionTypes.Enemy |
                                 Values.CollisionTypes.Player,
                AvoidTypes = Values.CollisionTypes.Hole |
                             Values.CollisionTypes.NPCWall,
                Bounciness = 0.25f,
                Drag = 0.85f,
            };

            // Culling and component-box assertions require EntitySize to cover
            // the sprite plus every collision box (body, damage, hittable).
            var minX = Math.Min(offsetX, -8);
            var minY = Math.Min(offsetY, -15);
            var maxX = Math.Max(offsetX + width, 8);
            var maxY = Math.Max(offsetY + height, 1);
            EntitySize = new Rectangle(minX, minY, maxX - minX, maxY - minY);

            // octorok.s: state 9 (stand) -> state $0a (walk) -> state 8
            // (decide shoot/stand) -> state $0b (16-frame windup) -> state 9.
            var walking = new AiState(UpdateWalking) { Init = ToWalking };
            var idle = new AiState(UpdateIdle) { Init = ToIdle };
            var shoot = new AiState(UpdateShoot) { Init = ToShoot };

            _ai = new AiComponent();
            _ai.States.Add("walking", walking);
            _ai.States.Add("idle", idle);
            _ai.States.Add("shoot", shoot);
            new AiFallState(_ai, _body, OnHoleAbsorb, null);
            _ai.ChangeState("walking");

            _damageState = new AiDamageState(this, _body, _ai, _sprite,
                Math.Max(1, health)) { MoveBody = false };
            _lastLives = _damageState.CurrentLives;

            var damageBox = new CBox(EntityPosition, -8, -13, 0, 16, 13, 4);
            var hittableBox = new CBox(EntityPosition, -7, -15, 0, 14, 15, 8);
            var pushableBox = new CBox(EntityPosition, -7, -13, 0, 14, 13, 4);

            AddComponent(DamageFieldComponent.Index,
                new DamageFieldComponent(damageBox, HitType.Enemy, Math.Max(1, damage)));
            AddComponent(HittableComponent.Index,
                new HittableComponent(hittableBox, OnHit));
            AddComponent(BodyComponent.Index, _body);
            AddComponent(AiComponent.Index, _ai);
            AddComponent(PushableComponent.Index,
                new PushableComponent(pushableBox, OnPush));
            AddComponent(DrawComponent.Index,
                new BodyDrawComponent(_body, _sprite, Values.LayerPlayer));
            AddComponent(DrawShadowComponent.Index,
                new DrawShadowCSpriteComponent(_sprite) { Height = 1.0f, Rotation = 0.1f });
            AddComponent(UpdateComponent.Index, new UpdateComponent(Update));
        }

        public override void Init()
        {
            base.Init();
            if (IsDead || !IsActive)
                return;

            _direction = Game1.RandomNumber.Next(0, 4);
            SetDirection(_direction);
            _ai.ChangeState(Game1.RandomNumber.Next(0, 2) == 0 ? "idle" : "walking");
            OracleScriptRuntime.Trace(
                $"octorok ${_subId:x2} spawn at {EntityPosition.X:0},{EntityPosition.Y:0} lives {_lastLives}");
        }

        private void LoadDirectionalFrames(string directionSpec, int fallbackOffsetX,
            int fallbackOffsetY)
        {
            var groups = string.IsNullOrWhiteSpace(directionSpec)
                ? Array.Empty<string>()
                : directionSpec.Split(';');

            for (var direction = 0; direction < 4; direction++)
            {
                if (direction < groups.Length)
                {
                    var parts = groups[direction].Split('~');
                    if (parts.Length >= 4)
                    {
                        var frames = OracleSpriteFrames.Load(parts[0]);
                        if (frames.Length > 0 &&
                            int.TryParse(parts[2], out var offsetX) &&
                            int.TryParse(parts[3], out var offsetY))
                        {
                            _framesByDirection[direction] = frames;
                            _durationsByDirection[direction] =
                                OracleSpriteFrames.LoadDurations(parts[1], frames.Length);
                            _offsetByDirection[direction] = new Vector2(offsetX, offsetY);
                            continue;
                        }
                    }
                }

                _framesByDirection[direction] = _frames;
                _durationsByDirection[direction] = _durations;
                _offsetByDirection[direction] = new Vector2(fallbackOffsetX, fallbackOffsetY);
            }
        }

        private void Update()
        {
            if (Game1.OracleFreezeEnemies && _knockbackTime <= 0)
                _body.VelocityTarget = Vector2.Zero;

            if (_knockbackTime > 0)
            {
                _knockbackTime -= Game1.DeltaTime;
                _body.VelocityTarget = _knockback;
                if (_knockbackTime <= 0)
                {
                    _body.VelocityTarget = Vector2.Zero;
                    _knockbackActive = false;
                }
            }

            if (_damageState.CurrentLives != _lastLives)
            {
                _lastLives = _damageState.CurrentLives;
                OracleScriptRuntime.Trace(
                    $"octorok ${_subId:x2} lives {_lastLives} at {EntityPosition.X:0},{EntityPosition.Y:0}");
            }
            if (IsDead && !_deathLogged)
            {
                _deathLogged = true;
                OracleScriptRuntime.Trace(
                    $"octorok ${_subId:x2} died at {EntityPosition.X:0},{EntityPosition.Y:0}");
            }

            if (_ai.CurrentStateId == "walking")
            {
                var frames = _framesByDirection[_direction];
                var durations = _durationsByDirection[_direction];
                if (frames.Length > 1)
                {
                    _elapsed += Game1.DeltaTime;
                    while (_elapsed >= durations[_frame])
                    {
                        _elapsed -= durations[_frame];
                        _frame++;
                        if (_frame >= frames.Length)
                            _frame = 0;
                        _sprite.SetSprite(frames[_frame]);
                    }
                }
            }
        }

        private void ToWalking()
        {
            // octorok_state_09: 1-in-4 chance to head toward Link, otherwise
            // a random cardinal angle; walk for one of the four native
            // octorok_walkCounterValues.
            if (Game1.RandomNumber.Next(0, 4) == 0)
                FaceLink();
            else
                _direction = Game1.RandomNumber.Next(0, 4);

            SetDirection(_direction);
            _walkCounter = FramesToMs(WalkCounterValues[
                Game1.RandomNumber.Next(0, WalkCounterValues.Length)]);
            _body.VelocityTarget = AnimationHelper.DirectionOffset[_direction] * _walkSpeed;
        }

        private void UpdateWalking()
        {
            _walkCounter -= Game1.DeltaTime;
            if (_walkCounter <= 0)
                OnDecided();
        }

        private void OnDecided()
        {
            // octorok_state_08: random & var32 == 0 -> shoot, else stand.
            // Enemies only act inside the player's room; the camera culling
            // margin otherwise lets adjacent-room enemies fire at Link.
            if (_shotFrames.Length > 0 && PlayerInCurrentRoom() &&
                (Game1.RandomNumber.Next(0, 256) & _counterMask) == 0)
                _ai.ChangeState("shoot");
            else
                _ai.ChangeState("idle");
        }

        private void ToIdle()
        {
            _body.VelocityTarget = Vector2.Zero;

            // octorok_state_09: stand for a native counter1 value. The
            // post-shot wait is fixed at $20 frames.
            if (_pendingIdleFrames > 0)
            {
                _idleCounter = FramesToMs((int)_pendingIdleFrames);
                _pendingIdleFrames = 0;
            }
            else
            {
                var count = _counterMask + 1;
                _idleCounter = FramesToMs(CounterValues[
                    Game1.RandomNumber.Next(0, count)]);
            }

            _frame = 0;
            _elapsed = 0;
            _sprite.SetSprite(_framesByDirection[_direction][0]);
        }

        private void UpdateIdle()
        {
            _idleCounter -= Game1.DeltaTime;
            if (_idleCounter <= 0)
                _ai.ChangeState("walking");
        }

        private void SetDirection(int direction)
        {
            _direction = direction;
            _frame = 0;
            _elapsed = 0;
            _sprite.SetSprite(_framesByDirection[direction][0]);
            _sprite.DrawOffset = _offsetByDirection[direction];
        }

        private static float FramesToMs(int frames) => frames * 1000f / 60f;

        private void FaceLink()
        {
            var delta = MapManager.ObjLink.EntityPosition.Position - EntityPosition.Position;
            if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                _direction = delta.X < 0 ? 0 : 2;
            else
                _direction = delta.Y < 0 ? 1 : 3;
        }

        private bool PlayerInCurrentRoom()
        {
            var info = Map.OracleInfo;
            if (info == null)
                return true;

            var room = OracleRoomRuntime.ResolveRoom(info, EntityPosition.Position);
            var playerRoom = Game1.GameManager.OracleSession.Room;
            return playerRoom < 0 || room == playerRoom;
        }

        private void ToShoot()
        {
            _body.VelocityTarget = Vector2.Zero;
            _shootCounter = FramesToMs(16);

            // Blue/golden Octoroks turn to face Link before firing (octorok.s).
            if (_subId >= 2)
            {
                FaceLink();
                SetDirection(_direction);
            }
        }

        private void UpdateShoot()
        {
            _shootCounter -= Game1.DeltaTime;
            if (_shootCounter <= 0)
                SpawnShot();
        }

        private void SpawnShot()
        {
            if (_shotFrames.Length == 0)
            {
                _ai.ChangeState("idle");
                return;
            }

            // octorok_state_0b: spawn PART_OCTOROK_PROJECTILE, then state 9
            // with counter1 = $20.
            var offset = ShotOffset[_direction];
            var velocity = AnimationHelper.DirectionOffset[_direction] * _shotSpeed;
            Map.Objects.SpawnObject(new ObjOracleOctorokShot(Map,
                EntityPosition.X + offset.X, EntityPosition.Y + offset.Y,
                _shotFrameIds, _shotDurationValues,
                _shotOffsetX, _shotOffsetY, velocity, _shotDamage, _shotLifetime));

            _pendingIdleFrames = 32;
            _ai.ChangeState("idle");
        }

        private Values.HitCollision OnHit(GameObject gameObject, Vector2 direction,
            HitType damageType, int damage, bool pieceOfPower)
        {
            var result = _damageState.OnHit(gameObject, direction, damageType, damage, pieceOfPower);
            if (result != Values.HitCollision.None)
            {
                // Directed knockback so the movement goes through SystemBody's
                // tile-collision path instead of the collision-free velocity.
                _knockback = direction * 1.75f;
                _knockbackTime = 180;
                _knockbackActive = true;
            }
            return result;
        }

        private bool OnPush(Vector2 direction, PushableComponent.PushType type)
        {
            if (type == PushableComponent.PushType.Impact)
                _body.Velocity = new Vector3(direction.X * 1.75f,
                    direction.Y * 1.75f, _body.Velocity.Z);
            return true;
        }

        private void OnCollision(Values.BodyCollision direction)
        {
            if (_ai.CurrentStateId != "walking")
                return;

            if ((direction & Values.BodyCollision.Left) != 0)
                SetDirection(2);
            else if ((direction & Values.BodyCollision.Right) != 0)
                SetDirection(0);
            else if ((direction & Values.BodyCollision.Top) != 0)
                SetDirection(3);
            else if ((direction & Values.BodyCollision.Bottom) != 0)
                SetDirection(1);

            _body.VelocityTarget = AnimationHelper.DirectionOffset[_direction] * _walkSpeed;
        }

        private void OnHoleAbsorb()
        {
            // Kept as a hook for the native fall state; the importer supplies
            // the sprite frames but not a separate hole animation yet.
        }
    }
}
