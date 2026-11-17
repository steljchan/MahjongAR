using System.Collections.Generic;
using System.Text;

public static class TileRules
{
    public static TileData Num(Suit s, int n) { return TileDatabase.ByIndex((int)s * 9 + n - 1); }

    // ---------------------------------------------------------------
    // ATURAN DASAR
    // ---------------------------------------------------------------

    // Semua CHI yang bisa dibentuk dengan tile ini (maksimal 3).
    public static List<TileData[]> GetChiOptions(TileData t)
    {
        var res = new List<TileData[]>();
        if (t == null || !t.IsNumber) return res;

        for (int start = t.number - 2; start <= t.number; start++)
        {
            if (start >= 1 && start + 2 <= 9)
                res.Add(new[] { Num(t.suit, start), Num(t.suit, start + 1), Num(t.suit, start + 2) });
        }
        return res;
    }

    public static TileData[] GetPon(TileData t) { return new[] { t, t, t }; }

    // ---------------------------------------------------------------
    // TEKS (dipakai Learn dan mode 1 TILE)
    // ---------------------------------------------------------------

    // Contoh: "3-4-5 Pin", "Ton x3"
    public static string ComboName(TileData[] combo, bool isPon)
    {
        if (isPon) return combo[0].name + " x3";
        string suit = combo[0].name.Substring(combo[0].name.IndexOf(' ') + 1);
        return combo[0].number + "-" + combo[1].number + "-" + combo[2].number + " " + suit;
    }

    // Satu baris per kemungkinan CHI, lengkap dengan tile yang dibutuhkan.
    public static string ChiText(TileData t)
    {
        var chi = GetChiOptions(t);
        if (chi.Count == 0)
            return "Not possible (honor tiles have no number order)";

        var sb = new StringBuilder();
        for (int i = 0; i < chi.Count; i++)
        {
            var need = new List<string>();
            foreach (var x in chi[i]) if (x.index != t.index) need.Add(x.name);

            if (i > 0) sb.Append("\n");
            sb.Append(ComboName(chi[i], false) + "  (need " + string.Join(" + ", need.ToArray()) + ")");
        }
        return sb.ToString();
    }

    public static string PonText(TileData t)
    {
        return ComboName(GetPon(t), true) + "  (need 2 more " + t.name + ")";
    }
}
