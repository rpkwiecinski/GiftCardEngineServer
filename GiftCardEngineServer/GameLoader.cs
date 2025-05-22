using System.Text.Json;
using GiftCardBaskets.Core;
using System.Collections.Generic;

public static class GameLoader
{
    public static List<Game> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Game>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<Game>();
    }
}
