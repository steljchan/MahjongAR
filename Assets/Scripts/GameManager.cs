using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum Mode { Menu, Scan, Learn, Combo, HowTo }
    private enum LearnCategory { All, Characters, Bamboo, Dots, Honor }

    // ---------- warna UI ----------
    static readonly Color Gold = new Color32(0xD9, 0xAE, 0x57, 0xFF);
    static readonly Color GoldText = new Color32(0xF5, 0xC9, 0x6B, 0xFF);
    static readonly Color DarkText = new Color32(0x0F, 0x23, 0x26, 0xFF);
    static readonly Color CardBg = new Color32(0x17, 0x30, 0x2F, 0xFF);

    // =========================================================
    // PANELS
    // =========================================================
    [Header("MAIN PANELS")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject scanPanel;
    [SerializeField] private GameObject learnPanel;
    [SerializeField] private GameObject comboPanel;
    [SerializeField] private GameObject howToPanel;

    // =========================================================
    // SCAN PANEL
    // =========================================================
    [Header("SCAN PANEL")]
    [SerializeField] private TMP_Text scanStatusText;
    [SerializeField] private TMP_Text scanTileNameText;
    [SerializeField] private TMP_Text scanTileSubText;
    [SerializeField] private GameObject scanSwitchButton;
    [SerializeField] private TMP_Text scanSwitchText;

    // =========================================================
    // LEARN PANEL
    // =========================================================
    [Header("LEARN PANEL")]
    [SerializeField] private Image learnTileImage;
    [SerializeField] private TMP_Text learnCounterText;
    [SerializeField] private TMP_Text learnTileNameText;
    [SerializeField] private TMP_Text learnTileCategoryText;
    [SerializeField] private TMP_Text learnTileDescriptionText;
    [SerializeField] private TMP_Text learnChiText;
    [SerializeField] private TMP_Text learnPonText;
    [Tooltip("Tab kategori: CHARACTERS, BAMBOO, DOTS, HONORS (urut)")]
    [SerializeField] private Image[] learnTabImages = new Image[4];
    [SerializeField] private TMP_Text[] learnTabTexts = new TMP_Text[4];

    // =========================================================
    // CHI & PON GUIDE PANEL (comboPanel)
    // =========================================================
    [Header("CHI & PON GUIDE PANEL")]
    [SerializeField] private TMP_Text comboStatusText;
    [SerializeField] private TMP_Text oneTileNameText;
    [SerializeField] private TMP_Text oneTileChiText;
    [SerializeField] private TMP_Text oneTilePonText;

    [Header("AI & AR")]
    [Tooltip("Objek yang memegang TileDetector (AI dimatikan di Menu/Learn agar hemat baterai)")]
    [SerializeField] private TileDetector detector;
    [SerializeField] private DetectedTileManager tileManager;

    [Header("PENGATURAN")]
    [Tooltip("Folder di Assets/Resources berisi gambar 2D tile (nama file = id tile)")]
    [SerializeField] private string tileImageFolder = "MahjongTiles";

    // =========================================================
    // RUNTIME STATE
    // =========================================================
    public Mode CurrentMode { get; private set; } = Mode.Menu;
    public DetectedTile Selected { get; private set; }

    /// <summary>Putar & zoom: Scan (model tile) dan Chi & Pon Guide (baris CHI/PON).</summary>
    public bool AllowModelControl { get { return CurrentMode == Mode.Scan || CurrentMode == Mode.Combo; } }
    /// <summary>Ketuk untuk memilih tile: Scan dan Chi & Pon Guide.</summary>
    public bool AllowTapSelect { get { return CurrentMode == Mode.Scan || CurrentMode == Mode.Combo; } }

    private readonly List<DetectedTile> tracked = new List<DetectedTile>();
    private float nextStatusRefresh;

    private readonly List<TileData> learnTiles = new List<TileData>();
    private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private LearnCategory learnCategory = LearnCategory.All;
    private int learnIndex;

    // =========================================================
    // LIFECYCLE
    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        BuildLearnList();
        BackToMenu();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // status "cari meja" diperbarui walau belum ada tile terdeteksi
        if (tracked.Count == 0 && Time.unscaledTime >= nextStatusRefresh &&
            (CurrentMode == Mode.Scan || CurrentMode == Mode.Combo))
        {
            nextStatusRefresh = Time.unscaledTime + 0.5f;
            if (CurrentMode == Mode.Scan) SetText(scanStatusText, EmptyStatus("WAITING FOR TILE..."));
            else SetText(comboStatusText, EmptyStatus("POINT CAMERA AT ONE TILE"));
        }
    }

    // Jika ARCore belum menemukan meja, minta pengguna menggerakkan HP.
    private string EmptyStatus(string normal)
    {
        if (tileManager != null && !tileManager.HasPlane) return "MOVE YOUR PHONE SLOWLY TO FIND THE TABLE";
        return normal;
    }

    // =========================================================
    // NAVIGASI PANEL
    // =========================================================
    private void ShowOnly(GameObject target)
    {
        SetActive(menuPanel, target == menuPanel);
        SetActive(scanPanel, target == scanPanel);
        SetActive(learnPanel, target == learnPanel);
        SetActive(comboPanel, target == comboPanel);
        SetActive(howToPanel, target == howToPanel);
    }

    private void ChangeMode(Mode mode, GameObject panel)
    {
        CurrentMode = mode;
        ShowOnly(panel);
        if (detector != null) detector.Running = mode == Mode.Scan || mode == Mode.Combo;
        foreach (var h in tracked) if (h != null) h.ResetView();
        EnsureSelection();
        RefreshAll();
    }

    public void BackToMenu() { ChangeMode(Mode.Menu, menuPanel); }

    // ---------- tombol menu ----------
    public void StartScan() { ChangeMode(Mode.Scan, scanPanel); }

    public void StartLearn() { OpenLearn(LearnCategory.Characters, null); }

    private void OpenLearn(LearnCategory category, TileData focus)
    {
        learnCategory = category;
        learnIndex = 0;
        BuildLearnList();
        if (focus != null)
        {
            int idx = learnTiles.FindIndex(t => t.index == focus.index);
            if (idx >= 0) learnIndex = idx;
        }
        ChangeMode(Mode.Learn, learnPanel);
    }

    private static LearnCategory CategoryOf(TileData t)
    {
        switch (t.suit)
        {
            case Suit.Man: return LearnCategory.Characters;
            case Suit.Sou: return LearnCategory.Bamboo;
            case Suit.Pin: return LearnCategory.Dots;
            default: return LearnCategory.Honor;
        }
    }

    public void OpenCombo() { ChangeMode(Mode.Combo, comboPanel); }

    public void OpenHowTo() { ChangeMode(Mode.HowTo, howToPanel); }

    // =========================================================
    // EVENT DETEKSI (dipanggil DetectedTileManager / DetectedTile)
    // =========================================================
    public void TileFound(DetectedTile h)
    {
        if (h == null || h.Tile == null) return;

        tracked.Remove(h);
        tracked.Add(h);

        EnsureSelection();
        RefreshAll();
    }

    public void TileLost(DetectedTile h)
    {
        if (h == null) return;
        tracked.Remove(h);
        if (Selected == h) Selected = null;

        EnsureSelection();
        RefreshAll();
    }

    // =========================================================
    // PEMILIHAN TILE (1 tile per scan)
    // =========================================================
    private void EnsureSelection()
    {
        tracked.RemoveAll(x => x == null);
        if (Selected != null && Selected.IsTracked && tracked.Contains(Selected)) return;
        Selected = tracked.Count > 0 ? tracked[tracked.Count - 1] : null;
    }

    /// <summary>Dipanggil ModelInteraction saat tile diketuk.</summary>
    public void SelectTile(DetectedTile h)
    {
        if (h == null || !tracked.Contains(h) || !AllowTapSelect) return;
        Selected = h;
        Selected.ResetView();
        RefreshAll();
    }

    /// <summary>Tombol SWITCH TILE.</summary>
    public void SelectNextTile()
    {
        if (tracked.Count < 2) return;
        int i = tracked.IndexOf(Selected);
        SelectTile(tracked[(i + 1) % tracked.Count]);
    }

    // =========================================================
    // REFRESH SEMUA (UI + tampilan 3D)
    // =========================================================
    private void RefreshAll()
    {
        RefreshTileViews();

        switch (CurrentMode)
        {
            case Mode.Scan: RefreshScanUI(); break;
            case Mode.Learn: UpdateLearnUI(); break;
            case Mode.Combo: RefreshComboUI(); break;
        }
    }

    private void RefreshTileViews()
    {
        bool hintMode = CurrentMode == Mode.Combo;

        foreach (var h in tracked)
        {
            if (h == null) continue;
            h.SetView(ViewFor(h));
            if (!(hintMode && h == Selected)) h.ClearHint();
        }

        // baris petunjuk hanya untuk tile terpilih di Chi & Pon Guide
        if (hintMode && Selected != null)
            Selected.ShowHint();
    }

    private TileView ViewFor(DetectedTile h)
    {
        switch (CurrentMode)
        {
            case Mode.Scan:
            case Mode.Combo:
                return h == Selected ? TileView.Selected : TileView.Candidate;

            default:
                return TileView.Hidden;
        }
    }

    // =========================================================
    // SCAN
    // =========================================================
    private void RefreshScanUI()
    {
        int n = tracked.Count;

        if (Selected == null)
        {
            SetText(scanStatusText, EmptyStatus("WAITING FOR TILE..."));
            SetText(scanTileNameText, "NO TILE");
            SetText(scanTileSubText, "Point your camera at one Mahjong tile");
        }
        else
        {
            SetText(scanStatusText, n > 1 ? n + " TILES FOUND - TAP ONE TO SELECT" : "TILE DETECTED");
            SetText(scanTileNameText, Selected.Tile.name.ToUpper());
            SetText(scanTileSubText, Selected.Tile.englishName);
        }

        SetActive(scanSwitchButton, n > 1);
        if (n > 1) SetText(scanSwitchText, "SWITCH TILE (" + (tracked.IndexOf(Selected) + 1) + "/" + n + ")");
    }

    /// <summary>Tombol LEARN di Scan: buka Learn tepat di tile yang dipilih.</summary>
    public void OpenLearnFromScan()
    {
        TileData scanned = Selected != null ? Selected.Tile : null;
        OpenLearn(scanned != null ? CategoryOf(scanned) : LearnCategory.Characters, scanned);
    }

    public void ZoomIn() { if (ModelInteraction.Instance != null) ModelInteraction.Instance.Step(1.25f); }
    public void ZoomOut() { if (ModelInteraction.Instance != null) ModelInteraction.Instance.Step(0.8f); }
    public void ResetView() { if (ModelInteraction.Instance != null) ModelInteraction.Instance.ResetView(); }

    // =========================================================
    // LEARN
    // =========================================================
    private void BuildLearnList()
    {
        learnTiles.Clear();
        foreach (TileData t in TileDatabase.All)
        {
            bool include =
                learnCategory == LearnCategory.All ||
                (learnCategory == LearnCategory.Characters && t.suit == Suit.Man) ||
                (learnCategory == LearnCategory.Bamboo && t.suit == Suit.Sou) ||
                (learnCategory == LearnCategory.Dots && t.suit == Suit.Pin) ||
                (learnCategory == LearnCategory.Honor && t.IsHonor);
            if (include) learnTiles.Add(t);
        }
        learnIndex = Mathf.Clamp(learnIndex, 0, Mathf.Max(0, learnTiles.Count - 1));
    }

    private void UpdateLearnUI()
    {
        // tab aktif = emas, lainnya gelap (urutan enum: All, Characters, Bamboo, Dots, Honor)
        for (int i = 0; i < 4; i++)
        {
            Image img = i < learnTabImages.Length ? learnTabImages[i] : null;
            TMP_Text txt = i < learnTabTexts.Length ? learnTabTexts[i] : null;
            SetTab(img, txt, (int)learnCategory == i + 1);
        }

        if (learnTiles.Count == 0) return;
        TileData t = learnTiles[learnIndex];

        SetText(learnCounterText, (learnIndex + 1) + " / " + learnTiles.Count);
        SetText(learnTileNameText, t.name.ToUpper() + "  <size=70%>(" + t.englishName + ")</size>");
        SetText(learnTileCategoryText, t.CategoryLine);
        SetText(learnTileDescriptionText, t.description);
        SetText(learnChiText, "<b>CHI</b>\n" + TileRules.ChiText(t));
        SetText(learnPonText, "<b>PON</b>\n" + TileRules.PonText(t));

        if (learnTileImage != null)
        {
            Sprite sp = GetTileSprite(t);
            learnTileImage.sprite = sp;
            learnTileImage.preserveAspect = true;
            learnTileImage.enabled = sp != null;
        }
    }

    public void ShowCharacters() { ChangeLearnCategory(LearnCategory.Characters); }
    public void ShowBamboo() { ChangeLearnCategory(LearnCategory.Bamboo); }
    public void ShowDots() { ChangeLearnCategory(LearnCategory.Dots); }
    public void ShowHonor() { ChangeLearnCategory(LearnCategory.Honor); }
    public void ShowAllTiles() { ChangeLearnCategory(LearnCategory.All); }

    private void ChangeLearnCategory(LearnCategory c)
    {
        learnCategory = c;
        learnIndex = 0;
        BuildLearnList();
        UpdateLearnUI();
    }

    public void NextTile()
    {
        if (learnTiles.Count == 0) return;
        learnIndex = (learnIndex + 1) % learnTiles.Count;
        UpdateLearnUI();
    }

    public void PreviousTile()
    {
        if (learnTiles.Count == 0) return;
        learnIndex = (learnIndex - 1 + learnTiles.Count) % learnTiles.Count;
        UpdateLearnUI();
    }

    // =========================================================
    // CHI & PON GUIDE
    // =========================================================
    private void SetTab(Image bg, TMP_Text label, bool active)
    {
        if (bg != null) bg.color = active ? Gold : CardBg;
        if (label != null) label.color = active ? DarkText : GoldText;
    }

    private void RefreshComboUI()
    {
        if (Selected == null)
        {
            SetText(comboStatusText, EmptyStatus("POINT CAMERA AT ONE TILE"));
            SetText(oneTileNameText, "NO TILE");
            SetText(oneTileChiText, "<b>CHI</b>\n-");
            SetText(oneTilePonText, "<b>PON</b>\n-");
            return;
        }

        TileData t = Selected.Tile;
        SetText(comboStatusText, tracked.Count > 1 ? "TAP A TILE TO SWITCH" : "PINCH OR + / - TO ZOOM THE HINTS");
        SetText(oneTileNameText, t.name.ToUpper() + "  <size=70%>(" + t.englishName + ")</size>");
        SetText(oneTileChiText, "<b>CHI</b>\n" + TileRules.ChiText(t));
        SetText(oneTilePonText, "<b>PON</b>\n" + TileRules.PonText(t));
    }

    // =========================================================
    // UTIL
    // =========================================================
    private Sprite GetTileSprite(TileData tile)
    {
        Sprite sp;
        if (spriteCache.TryGetValue(tile.id, out sp)) return sp;

        string path = tileImageFolder + "/" + tile.id;
        sp = Resources.Load<Sprite>(path);
        if (sp == null)
        {
            Texture2D tex = Resources.Load<Texture2D>(path);
            if (tex != null)
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        spriteCache[tile.id] = sp;
        return sp;
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null) target.text = value;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
