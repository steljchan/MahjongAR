using System.Collections.Generic;

public static class TileLabelMap
{
    // Urutan tile kehormatan untuk notasi 1z..7z / honor1..7:
    // 1 Timur, 2 Selatan, 3 Barat, 4 Utara, 5 Putih, 6 Hijau, 7 Merah
    static readonly string[] HonorOrder = { "ton", "nan", "sha", "pei", "haku", "hatsu", "chun" };

    static readonly Dictionary<string, string> HonorWords = new Dictionary<string, string>
    {
        { "ton", "ton" }, { "east", "ton" }, { "dong", "ton" },
        { "nan", "nan" }, { "south", "nan" },
        { "sha", "sha" }, { "west", "sha" }, { "xi", "sha" },
        { "pei", "pei" }, { "north", "pei" }, { "bei", "pei" },
        { "haku", "haku" }, { "white", "haku" }, { "bai", "haku" },
        { "hatsu", "hatsu" }, { "green", "hatsu" }, { "fa", "hatsu" },
        { "chun", "chun" }, { "red", "chun" }, { "zhong", "chun" }
    };

    static readonly Dictionary<string, string> SuitWords = new Dictionary<string, string>
    {
        { "m", "man" }, { "man", "man" }, { "wan", "man" }, { "w", "man" },
        { "character", "man" }, { "characters", "man" }, { "char", "man" }, { "crak", "man" }, { "crack", "man" },
        { "p", "pin" }, { "pin", "pin" }, { "tong", "pin" }, { "t", "pin" },
        { "dot", "pin" }, { "dots", "pin" }, { "circle", "pin" }, { "circles", "pin" }, { "coin", "pin" },
        { "s", "sou" }, { "sou", "sou" }, { "so", "sou" }, { "tiao", "sou" },
        { "bamboo", "sou" }, { "bamboos", "sou" }, { "bam", "sou" }, { "stick", "sou" },
        { "z", "honor" }, { "honor", "honor" }, { "honour", "honor" }, { "zi", "honor" }
    };

    public static string ToTileId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string s = raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");

        // 1) sudah sama dengan id aplikasi
        if (TileDatabase.ById(s) != null) return s;

        // 2) nama tile kehormatan (east, white_dragon, haku, ...)
        foreach (string token in s.Split('_'))
        {
            string id;
            if (HonorWords.TryGetValue(token, out id)) return id;
        }

        // 3) tile angka / 1z..7z: ambil angka + kata suit
        int number = -1;
        var letters = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (char.IsDigit(c)) { if (number < 0) number = c - '0'; }
            else if (char.IsLetter(c)) letters.Append(c);
        }
        if (number < 0) return null;
        if (number == 0) number = 5;                       // 0m = lima merah

        string suitToken = letters.ToString();
        if (suitToken.Length == 2 && suitToken[0] == 'r') // r5m = lima merah
            suitToken = suitToken.Substring(1);

        string suit;
        if (!SuitWords.TryGetValue(suitToken, out suit)) return null;

        if (suit == "honor")
            return (number >= 1 && number <= 7) ? HonorOrder[number - 1] : null;

        if (number < 1 || number > 9) return null;
        return suit + number;
    }
}
