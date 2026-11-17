using System.Collections.Generic;


public enum Suit { Man, Pin, Sou, Wind, Dragon }

public class TileData
{
    public int index;           // 0-33 (urutan: man, pin, sou, angin, naga)
    public string id;           // id tile, mis. "sou3"
    public Suit suit;
    public int number;          // 1-9 (angka), 1-4 (angin), 1-3 (naga)
    public string name;         // "3 Sou", "Ton", "Haku"
    public string englishName;  // "3 Bamboo", "East Wind", "White Dragon"
    public string description;  // kalimat penjelasan (Bahasa Inggris)

    public bool IsNumber { get { return suit <= Suit.Sou; } }
    public bool IsHonor { get { return !IsNumber; } }
    public bool IsTerminal { get { return IsNumber && (number == 1 || number == 9); } }

    public string GroupName
    {
        get
        {
            switch (suit)
            {
                case Suit.Man: return "Manzu (Characters)";
                case Suit.Pin: return "Pinzu (Dots)";
                case Suit.Sou: return "Souzu (Bamboo)";
                case Suit.Wind: return "Kazehai (Winds)";
                default: return "Sangenpai (Dragons)";
            }
        }
    }

    public string TypeName
    {
        get
        {
            if (IsHonor) return "Honor Tile";
            if (IsTerminal) return "Terminal Tile";
            return "Simple Tile";
        }
    }

    // Contoh: "MANZU (CHARACTERS) - TERMINAL TILE"
    public string CategoryLine { get { return (GroupName + " - " + TypeName).ToUpper(); } }
}

public static class TileDatabase
{
    static TileData[] all;
    static Dictionary<string, TileData> byId;

    public static TileData[] All { get { if (all == null) Build(); return all; } }
    public static TileData ByIndex(int i) { return All[i]; }

    public static TileData ById(string id)
    {
        if (all == null) Build();
        if (string.IsNullOrEmpty(id)) return null;
        TileData t;
        return byId.TryGetValue(id.Trim().ToLower(), out t) ? t : null;
    }

    static void Build()
    {
        var list = new List<TileData>();

        // ---------- 27 tile angka ----------
        string[] suitId = { "man", "pin", "sou" };
        string[] suitJp = { "Man", "Pin", "Sou" };
        string[] suitEn = { "Character", "Dots", "Bamboo" };

        for (int s = 0; s < 3; s++)
        {
            for (int n = 1; n <= 9; n++)
            {
                list.Add(new TileData
                {
                    index = s * 9 + n - 1,
                    id = suitId[s] + n,
                    suit = (Suit)s,
                    number = n,
                    name = n + " " + suitJp[s],
                    englishName = n + " " + suitEn[s],
                    description = NumberDescription((Suit)s, n)
                });
            }
        }

        // ---------- 4 tile angin ----------
        string[] windId = { "ton", "nan", "sha", "pei" };
        string[] windJp = { "Ton", "Nan", "Sha", "Pei" };
        string[] windEn = { "East Wind", "South Wind", "West Wind", "North Wind" };
        string[] windDir = { "east", "south", "west", "north" };

        for (int i = 0; i < 4; i++)
        {
            list.Add(new TileData
            {
                index = 27 + i,
                id = windId[i],
                suit = Suit.Wind,
                number = i + 1,
                name = windJp[i],
                englishName = windEn[i],
                description = "Shows the kanji for \"" + windDir[i] + "\". " +
                              "Wind tiles are honor tiles: they have no number order, " +
                              "so they cannot form a CHI, only a PON."
            });
        }

        // ---------- 3 tile naga ----------
        string[] dragId = { "haku", "hatsu", "chun" };
        string[] dragJp = { "Haku", "Hatsu", "Chun" };
        string[] dragEn = { "White Dragon", "Green Dragon", "Red Dragon" };
        string[] dragLook =
        {
            "Usually a blank tile or a tile with a blue frame.",
            "Shows a green kanji character.",
            "Shows a red kanji character meaning \"middle\"."
        };

        for (int i = 0; i < 3; i++)
        {
            list.Add(new TileData
            {
                index = 31 + i,
                id = dragId[i],
                suit = Suit.Dragon,
                number = i + 1,
                name = dragJp[i],
                englishName = dragEn[i],
                description = dragLook[i] + " Dragon tiles are honor tiles: " +
                              "they cannot form a CHI, only a PON."
            });
        }

        all = list.ToArray();
        byId = new Dictionary<string, TileData>();
        foreach (var t in all) byId[t.id] = t;
    }

    static string NumberDescription(Suit s, int n)
    {
        string look;
        switch (s)
        {
            case Suit.Man:
                look = "Shows the kanji number " + n + " above the character \"man\" (ten thousand).";
                break;
            case Suit.Pin:
                look = "Shows " + n + (n == 1 ? " circle." : " circles.");
                break;
            default:
                look = (n == 1)
                    ? "Usually shows a bird instead of a single bamboo stick."
                    : "Shows " + n + " bamboo sticks.";
                break;
        }

        string type = (n == 1 || n == 9)
            ? " Numbers 1 and 9 are terminal tiles: they can only be at the end of a CHI."
            : " Numbers 2 to 8 are simple tiles: they can be in the middle of a CHI.";

        return look + type;
    }
}
