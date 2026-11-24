namespace ProjectZ.InGame.Map
{
    /// <summary>
    /// A tile-map animation decoded from the Oracle .map timeline. It swaps the
    /// visual atlas index of one 16x16 cell; collision/terrain stay untouched.
    /// </summary>
    public sealed class TileAnimation
    {
        public int X;
        public int Y;
        public int[] Frames;
        public int[] Durations;

        private int _index;
        private float _time;

        public TileAnimation(int x, int y, int[] frames, int[] durations)
        {
            X = x;
            Y = y;
            Frames = frames;
            Durations = durations;
        }

        public void Apply(Map map)
        {
            if (Frames.Length > 0 && map.TileMap.ArrayTileMap != null &&
                0 <= X && X < map.MapWidth && 0 <= Y && Y < map.MapHeight)
                map.TileMap.ArrayTileMap[X, Y, 0] = Frames[0];
        }

        public void Update(Map map, float deltaTime)
        {
            if (Frames.Length <= 1 || map.TileMap.ArrayTileMap == null)
                return;

            _time += deltaTime;

            while (_time >= Durations[_index])
            {
                _time -= Durations[_index];
                _index = (_index + 1) % Frames.Length;

                if (0 <= X && X < map.MapWidth && 0 <= Y && Y < map.MapHeight)
                    map.TileMap.ArrayTileMap[X, Y, 0] = Frames[_index];
            }
        }
    }
}
