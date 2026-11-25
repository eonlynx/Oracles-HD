using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectZ.Base;
using ProjectZ.InGame.GameObjects.Base;
using ProjectZ.InGame.GameObjects.Base.CObjects;
using ProjectZ.InGame.GameObjects.Base.Components;
using ProjectZ.InGame.SaveLoad;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.GameObjects.Things
{
    /// <summary>
    /// PART_OCTOROK_PROJECTILE ($18). The rock uses the imported Oracle part
    /// frames (same object gfx header as the Octorok) and native body/damage/
    /// hittable components. It flies straight, bounces off walls, and expires
    /// after the native counter.
    /// </summary>
    internal class ObjOracleOctorokShot : GameObject
    {
        private readonly BodyComponent _body;
        private readonly CSprite _sprite;
        private readonly DictAtlasEntry[] _frames;
        private readonly int[] _durations;
        private readonly float _lifetime;
        private readonly int _loopStart;

        private int _frame;
        private float _elapsed;
        private float _life;

        public ObjOracleOctorokShot(Map.Map map, float posX, float posY,
            string frames, string durations, int offsetX, int offsetY,
            Vector2 velocity, int damage, int lifetime) : base(map)
        {
            Tags = Values.GameObjectTag.Enemy;

            _frames = OracleSpriteFrames.Load(frames);
            if (_frames.Length == 0)
            {
                IsDead = true;
                return;
            }
            _durations = OracleSpriteFrames.LoadDurations(durations, _frames.Length);
            _lifetime = Math.Max(1, lifetime) * 16f;
            _life = _lifetime;

            // Do not spawn a rock inside a solid tile (e.g. shooting into a
            // tree); native PART18 collides and bounces.
            if (Map.IsSolid(new RectangleF(posX - 2, posY - 2, 4, 4)))
            {
                IsDead = true;
                return;
            }

            EntityPosition = new CPosition(posX, posY, 8);
            _sprite = new CSprite(_frames[0], EntityPosition,
                new Vector2(offsetX, offsetY));

            _body = new BodyComponent(EntityPosition, -4, -4, 8, 8, 8)
            {
                CollisionTypes = Values.CollisionTypes.Normal,
                Bounciness = 1.0f,
                Drag = 1.0f,
                IgnoreHeight = true,
                IgnoresZ = true,
                VelocityTarget = velocity,
            };

            EntitySize = new Rectangle(-12, -12, 24, 24);

            var damageBox = new CBox(EntityPosition, -3, -3, 0, 6, 6, 8);
            AddComponent(DamageFieldComponent.Index,
                new DamageFieldComponent(damageBox, HitType.Enemy, Math.Max(1, damage)));
            AddComponent(HittableComponent.Index,
                new HittableComponent(_body.BodyBox, OnHit));
            AddComponent(BodyComponent.Index, _body);
            AddComponent(DrawComponent.Index,
                new BodyDrawComponent(_body, _sprite, Values.LayerPlayer));
            AddComponent(DrawShadowComponent.Index,
                new ShadowBodyDrawComponent(EntityPosition) { ShadowWidth = 5, ShadowHeight = 3 });
            AddComponent(UpdateComponent.Index, new UpdateComponent(Update));
        }

        private void Update()
        {
            _life -= Game1.DeltaTime;
            if (_life <= 0)
            {
                Map.Objects.DeleteObjects.Add(this);
                return;
            }

            if (_frames.Length > 1)
            {
                _elapsed += Game1.DeltaTime;
                while (_elapsed >= _durations[_frame])
                {
                    _elapsed -= _durations[_frame];
                    _frame++;
                    if (_frame >= _frames.Length)
                        _frame = _loopStart;
                    _sprite.SetSprite(_frames[_frame]);
                }
            }
        }

        private Values.HitCollision OnHit(GameObject gameObject, Vector2 direction,
            HitType damageType, int damage, bool pieceOfPower)
        {
            // Sword/shield hits deflect and remove the rock.
            Map.Objects.DeleteObjects.Add(this);
            return Values.HitCollision.Enemy;
        }
    }
}
