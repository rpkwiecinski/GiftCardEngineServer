using System.Collections.Generic;
using System.Text.Json;
using GiftCardBaskets.Core;

namespace GiftCardEngine
{
    public static class GameLoader
    {
        public static List<Game> Load(string path)
        {
            var json = System.IO.File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<Game>>(json)
                   ?? new List<Game>();
        }
    }
}
