using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectZ.InGame.GameObjects.Base;
using ProjectZ.InGame.GameObjects.Base.CObjects;
using ProjectZ.InGame.GameObjects.Base.Components;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.GameObjects.Things
{
    /// <summary>
    /// Native-backed Oracle lightable torch. The flame is the map tile
    /// ($09 lit / $08 unlit); this object owns the lamp tag and the native
    /// LightDrawComponent, and starts inactive/off until the future Ember Seed
    /// path calls Ignite(). No light is drawn for an unlit tile.
    /// </summary>
    internal class ObjOracleTorch : GameObject, ILamp
    {
        private readonly Rectangle _lightRectangle;
        private bool _lit;

        public bool IsOn() => _lit;

        public ObjOracleTorch(Map.Map map, int posX, int posY, bool initialLit) : base(map)
        {
            EntityPosition = new CPosition(posX + 8, posY + 8, 0);
            EntitySize = new Rectangle(-80, -80, 160, 160);
            Tags = Values.GameObjectTag.Lamp;

            _lit = initialLit;
            _lightRectangle = new Rectangle(posX + 8 - 80, posY + 8 - 80, 160, 160);

            AddComponent(LightDrawComponent.Index,
                new LightDrawComponent(DrawLight) { Layer = Values.LightLayer0 });
        }

        public void Ignite()
        {
            _lit = true;
        }

        private void DrawLight(SpriteBatch spriteBatch)
        {
            if (!_lit)
                return;

            spriteBatch.Draw(Resources.SprLight, _lightRectangle,
                new Color(255, 200, 200));
        }
    }
}
