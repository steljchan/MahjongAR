#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public static class MahjongUIBuilder
{
    const float K = 1080f / 390f;
    static float P(float v) { return v * K; }

    // ---------- palet warna ----------
    static readonly Color BG = Hex("0F2326");
    static readonly Color CARD = Hex("17302F");
    static readonly Color LINE = Hex("24433F");
    static readonly Color HANDLE = Hex("2E4A47");
    static readonly Color GOLD = Hex("D9AE57");
    static readonly Color GOLDTXT = Hex("F5C96B");
    static readonly Color BROWN = Hex("5E5232");
    static readonly Color RED = Hex("E54B4B");
    static readonly Color GREEN = Hex("3DBE7A");
    static readonly Color BLUE = Hex("4A9BE8");
    static readonly Color IVORY = Hex("F6F2E6");
    static readonly Color WHITE = Color.white;
    static readonly Color MUTED = new Color(1f, 1f, 1f, 0.8f);

    const string SpritePath = "Assets/UI/MahjongRounded.png";
    static Sprite rounded;

    enum Weight { Black, Bold, Semi, Medium }
    static TMP_FontAsset fontBlack, fontBold, fontSemi, fontMedium;

    static GameManager gm;
    static SerializedObject so;
    static ComfortSettingsUI comfortUI;   // pengendali panel COMFORT (dipasang di MainCanvas)
    static SerializedObject cso;

    [MenuItem("Tools/Mahjong AR/Build UI")]
    public static void Build()
    {
        gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            EditorUtility.DisplayDialog("Mahjong AR", "GameManager tidak ditemukan di scene. Buka SampleScene dulu.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Mahjong AR",
            "Buat UI baru? MainCanvas lama akan diganti nama menjadi MainCanvas_OLD dan dinonaktifkan (tidak dihapus).",
            "Build", "Batal")) return;

        rounded = GetRoundedSprite();
        LoadFonts();
        so = new SerializedObject(gm);

        GameObject old = GameObject.Find("MainCanvas");
        if (old != null)
        {
            Undo.RecordObject(old, "Disable old canvas");
            old.name = "MainCanvas_OLD";
            old.SetActive(false);
        }

        EnsureEventSystem();

        var canvasGO = new GameObject("MainCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.layer = 5;
        Undo.RegisterCreatedObjectUndo(canvasGO, "Build Mahjong UI");
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0f;   // ikuti lebar: lebar desain selalu 390
        Transform root = canvasGO.transform;

        // Pengendali COMFORT dibuat lebih dulu supaya tombol COMFORT di tiap halaman bisa disambungkan
        comfortUI = canvasGO.AddComponent<ComfortSettingsUI>();
        cso = new SerializedObject(comfortUI);

        // Bingkai scan paling bawah (di belakang semua panel), jadi bagian yang tertutup header/panel bawah tidak terlihat
        var frame = BuildScanFrame(root);

        var menu = BuildMenu(root);
        var scan = BuildScan(root);
        var learn = BuildLearn(root);
        var combo = BuildCombo(root);
        var how = BuildHowTo(root);

        // Kotak debug AI (paling atas, tidak menangkap sentuhan)
        RectTransform debug = Rect("DebugBoxes", root);
        Stretch(debug);
        var detector = Object.FindAnyObjectByType<TileDetector>();
        if (detector != null)
        {
            Undo.RecordObject(detector, "Assign debug root");
            detector.debugBoxRoot = debug;
            EditorUtility.SetDirty(detector);
        }

        // Panel COMFORT paling atas (menutupi halaman lain saat dibuka)
        var comfort = BuildComfort(root);
        CRef("detector", detector);
        CRef("scanFrame", frame);
        cso.ApplyModifiedProperties();
        EditorUtility.SetDirty(comfortUI);
        comfort.gameObject.SetActive(false);
        frame.gameObject.SetActive(false);

        Ref("menuPanel", menu.gameObject);
        Ref("scanPanel", scan.gameObject);
        Ref("learnPanel", learn.gameObject);
        Ref("comboPanel", combo.gameObject);
        Ref("howToPanel", how.gameObject);
        RefIfFound("detector", detector);
        RefIfFound("tileManager", Object.FindAnyObjectByType<DetectedTileManager>());
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(gm);

        scan.gameObject.SetActive(false);
        learn.gameObject.SetActive(false);
        combo.gameObject.SetActive(false);
        how.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
        Selection.activeGameObject = canvasGO;
        Debug.Log("Mahjong AR: UI selesai dibuat. Jangan lupa Save Scene (Ctrl+S).");
    }

    // =====================================================
    // HALAMAN 1 - MENU
    // =====================================================
    static RectTransform BuildMenu(Transform root)
    {
        RectTransform p = Rect("menuPanel", root);
        Stretch(p);
        Img(p, BG, 0);
        VL(p, 24, 24, 48, 24, 16, TextAnchor.UpperCenter);

        // chip lokasi
        RectTransform chipRow = Rect("LocationChipRow", p);
        LE(chipRow, 26);
        RectTransform chip = Rect("LocationChip", chipRow);
        Center(chip, 0, 0, 200, 26);
        Img(chip, BROWN, 13);
        RectTransform chipIn = Rect("Inner", chip);
        Stretch(chipIn, 1, 1, 1, 1);
        Img(chipIn, BG, 12);
        var chipText = Text(chip, "Text", "LANTAI 5 - UC MAKASSAR", Weight.Semi, 11, GOLDTXT, TextAlignmentOptions.Center);
        Stretch(chipText.rectTransform);
        chipText.characterSpacing = 8;

        // tombol COMFORT kecil di kanan atas (di luar layout menu)
        TopRight(ComfortButton(p), 12, 46, 78, 30);

        // logo
        RectTransform logoRow = Rect("LogoRow", p);
        LE(logoRow, 96);
        RectTransform logo = Rect("Logo", logoRow);
        Center(logo, 0, 0, 72, 96);
        Sprite tileSprite = Resources.Load<Sprite>("MahjongTiles/pin4");
        if (tileSprite != null)
        {
            var li = logo.gameObject.AddComponent<Image>();
            li.sprite = tileSprite;
            li.preserveAspect = true;
            li.raycastTarget = false;
        }
        else Img(logo, IVORY, 12);

        LE(Text(p, "Title", "Mahjong AR", Weight.Black, 44, GOLDTXT, TextAlignmentOptions.Center), 52);
        var sub = Text(p, "Subtitle", "DISCOVER THE WORLD OF MAHJONG", Weight.Medium, 13, MUTED, TextAlignmentOptions.Center);
        sub.characterSpacing = 6;
        LE(sub, 20);
        LE(Rect("Spacer", p), 4);

        var b1 = MenuCard(p, "ScanCard", "SCAN MAHJONG", "Point your camera at a real tile", true);
        var b2 = MenuCard(p, "LearnCard", "LEARN MAHJONG", "All 34 tiles, their meaning, CHI and PON", false);
        var b3 = MenuCard(p, "GuideCard", "CHI & PON GUIDE", "See every CHI and PON one tile can make", false);
        var b4 = MenuCard(p, "HowToCard", "HOW TO PLAY", "4 quick steps to use the app", false);
        On(b1, gm.StartScan);
        On(b2, gm.StartLearn);
        On(b3, gm.OpenCombo);
        On(b4, gm.OpenHowTo);

        LE(Rect("FlexSpacer", p), -1, -1, 1);
        LE(Text(p, "Footer", "AI tile detection - works offline", Weight.Medium, 11, new Color(1, 1, 1, 0.7f), TextAlignmentOptions.Center), 18);
        return p;
    }

    static Button MenuCard(Transform parent, string name, string title, string subText, bool primary)
    {
        RectTransform b = Rect(name, parent);
        LE(b, 76);
        var img = Img(b, primary ? GOLD : CARD, 16);
        var btn = b.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        RectTransform icon = Rect("Icon", b);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0f, 0.5f);
        icon.anchoredPosition = new Vector2(P(18), 0);
        icon.sizeDelta = new Vector2(P(44), P(44));
        Img(icon, BG, 12).raycastTarget = false;
        RectTransform mini = Rect("MiniTile", icon);
        Center(mini, 0, 0, 16, 22);
        Img(mini, GOLDTXT, 3).raycastTarget = false;

        var t = Text(b, "Title", title, Weight.Bold, 16, primary ? BG : GOLDTXT, TextAlignmentOptions.BottomLeft);
        Offsets(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), 78, -40, -16, -14);
        var s = Text(b, "Sub", subText, Weight.Medium, 12, primary ? Hex("3A3220") : new Color(1, 1, 1, 0.75f), TextAlignmentOptions.TopLeft);
        Offsets(s.rectTransform, new Vector2(0, 1), new Vector2(1, 1), 78, -62, -16, -42);
        return btn;
    }

    // =====================================================
    // HALAMAN 2 - SCAN
    // =====================================================
    static RectTransform BuildScan(Transform root)
    {
        RectTransform p = Rect("scanPanel", root);
        Stretch(p);

        CameraHeader(p, "SCAN MAHJONG", "Point your camera at a Mahjong tile", 126);
        var status = StatusPill(p, 160, 310, "WAITING FOR TILE...");

        RectTransform zoom = Rect("ZoomGroup", p);
        TopRight(zoom, 16, 236, 48, 166);
        VL(zoom, 0, 0, 0, 0, 10, TextAnchor.UpperCenter);
        TextMeshProUGUI tmp;
        var zin = Btn(zoom, "ZoomInButton", "+", GOLD, BG, 48, 14, 24, out tmp);
        var zout = Btn(zoom, "ZoomOutButton", "-", GOLD, BG, 48, 14, 24, out tmp);
        var zres = Btn(zoom, "ResetViewButton", "RESET", CARD, GOLDTXT, 48, 14, 12, out tmp);
        On(zin, gm.ZoomIn);
        On(zout, gm.ZoomOut);
        On(zres, gm.ResetView);

        // tombol COMFORT di kiri, sejajar tombol zoom (posisi sama dengan di Guide)
        TopLeft(ComfortButton(p), 16, 236, 92, 36);

        RectTransform sheet = BottomSheet(p, "BottomSheet", 290, 24, 14, 28, 14);
        Handle(sheet);
        var name = Text(sheet, "scanTileNameText", "NO TILE", Weight.Black, 40, GOLDTXT, TextAlignmentOptions.Center);
        LE(name, 46);
        var subT = Text(sheet, "scanTileSubText", "Point your camera at one Mahjong tile", Weight.Medium, 14, MUTED, TextAlignmentOptions.Center);
        LE(subT, 20);
        TextMeshProUGUI switchText;
        var sw = Btn(sheet, "scanSwitchButton", "SWITCH TILE (1/2)", BG, GOLDTXT, 48, 14, 14, out switchText, true);
        On(sw, gm.SelectNextTile);

        RectTransform row = ButtonRow(sheet);
        var back = Btn(row, "BackButton", "BACK", CARD, GOLDTXT, 52, 14, 15, out tmp);
        var learn = Btn(row, "LearnButton", "LEARN MORE", GOLD, BG, 52, 14, 15, out tmp);
        On(back, gm.BackToMenu);
        On(learn, gm.OpenLearnFromScan);

        Ref("scanStatusText", status);
        Ref("scanTileNameText", name);
        Ref("scanTileSubText", subT);
        Ref("scanSwitchButton", sw.gameObject);
        Ref("scanSwitchText", switchText);
        return p;
    }

    // =====================================================
    // HALAMAN 3 - LEARN
    // =====================================================
    static RectTransform BuildLearn(Transform root)
    {
        RectTransform p = Rect("learnPanel", root);
        Stretch(p);
        Img(p, BG, 0);
        // Jarak antar bagian diatur satu per satu dengan Gap() (dalam px desain)
        VL(p, 20, 20, 56, 24, 0, TextAnchor.UpperCenter);

        HeaderBlock(p, "LEARN MAHJONG", "Explore and learn Mahjong tiles");
        Gap(p, 20);                                   // judul -> tab kategori

        RectTransform tabs = Rect("CategoryTabs", p);
        LE(tabs, 44);
        Img(tabs, CARD, 14);
        HL(tabs, 4, 4, 4, 4, 4);
        string[] labels = { "CHARACTERS", "BAMBOO", "DOTS", "HONORS" };
        UnityAction[] actions = { gm.ShowCharacters, gm.ShowBamboo, gm.ShowDots, gm.ShowHonor };
        var tabImgs = new Object[4];
        var tabTxts = new Object[4];
        for (int i = 0; i < 4; i++)
        {
            TextMeshProUGUI lbl;
            var b = Btn(tabs, "Tab" + labels[i], labels[i], i == 0 ? GOLD : CARD, i == 0 ? BG : GOLDTXT, 36, 10, 10, out lbl);
            lbl.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(lbl, 6);
            Stretch(lbl.rectTransform, 2, 2, 0, 0);
            On(b, actions[i]);
            tabImgs[i] = b.GetComponent<Image>();
            tabTxts[i] = lbl;
        }

        Gap(p, 40);                                   // tab kategori -> kelompok tile (ubah angka ini untuk menurunkan/menaikkan)

        // KELOMPOK TILE: gambar + tombol < > + nama + kategori + deskripsi.
        // Semuanya ada di satu wadah, jadi selalu turun/naik bersama.
        RectTransform group = Rect("TileInfoGroup", p);
        LE(group, 168 + 12 + 34 + 4 + 18 + 6 + 58);
        VL(group, 0, 0, 0, 0, 0, TextAnchor.UpperCenter);

        // Baris: tombol < , kartu gambar, tombol >  (posisi tetap, tanpa layout group)
        RectTransform row = Rect("TileRow", group);
        LE(row, 168);
        TextMeshProUGUI tmp;
        var prev = Btn(row, "PrevButton", "<", BG, GOLDTXT, 48, 24, 22, out tmp, true);
        SideMiddle((RectTransform)prev.transform, true, 48);
        var next = Btn(row, "NextButton", ">", GOLD, BG, 48, 24, 22, out tmp);
        SideMiddle((RectTransform)next.transform, false, 48);
        On(prev, gm.PreviousTile);
        On(next, gm.NextTile);

        RectTransform card = Rect("TileCard", row);
        Stretch(card, 60, 60, 0, 0);
        Img(card, CARD, 20);
        RectTransform tileImgRT = Rect("learnTileImage", card);
        TopCenter(tileImgRT, 14, 96, 126);
        var tileImg = tileImgRT.gameObject.AddComponent<Image>();
        tileImg.preserveAspect = true;
        tileImg.raycastTarget = false;
        var counter = Text(card, "learnCounterText", "1 / 9", Weight.Semi, 12, new Color(1, 1, 1, 0.7f), TextAlignmentOptions.Center);
        Offsets(counter.rectTransform, new Vector2(0, 0), new Vector2(1, 0), 0, 6, 0, 24);

        Gap(group, 12);                               // gambar -> nama (tetap rapat)
        var name = Text(group, "learnTileNameText", "1 MAN", Weight.Black, 26, GOLDTXT, TextAlignmentOptions.Center);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        AutoSize(name, 14);
        LE(name, 34);
        Gap(group, 4);                                // nama -> kategori
        var cat = Text(group, "learnTileCategoryText", "MANZU (CHARACTERS) - TERMINAL TILE", Weight.Bold, 11, GOLDTXT, TextAlignmentOptions.Center);
        AutoSize(cat, 8);
        LE(cat, 18);
        Gap(group, 6);                                // kategori -> deskripsi
        var desc = Text(group, "learnTileDescriptionText", "Tile description", Weight.Medium, 13, new Color(1, 1, 1, 0.85f), TextAlignmentOptions.Top);
        AutoSize(desc, 9);
        LE(desc, 58);

        // Ruang sisa dibagi 2 : 1 -> kartu CHI/PON turun mendekati tombol BACK
        LE(Rect("FlexSpacerTop", p), -1, -1, 2);

        RectTransform combo = Rect("ComboCard", p);
        LE(combo, 140);
        Img(combo, CARD, 16);
        VL(combo, 16, 16, 10, 10, 6, TextAnchor.UpperLeft);
        var chi = Text(combo, "learnChiText", "<b>CHI</b>\n-", Weight.Medium, 13, WHITE, TextAlignmentOptions.TopLeft);
        AutoSize(chi, 9);
        LE(chi, 72);
        RectTransform div = Rect("Divider", combo);
        LE(div, 1);
        Img(div, LINE, 0);
        var pon = Text(combo, "learnPonText", "<b>PON</b>\n-", Weight.Medium, 13, GOLDTXT, TextAlignmentOptions.TopLeft);
        AutoSize(pon, 9);
        LE(pon, 34);

        LE(Rect("FlexSpacer", p), -1, -1, 1);
        Gap(p, 16);                                   // jarak minimum kartu -> BACK
        var back = Btn(p, "BackButton", "BACK", CARD, GOLDTXT, 52, 14, 15, out tmp);
        On(back, gm.BackToMenu);

        Ref("learnTileImage", tileImg);
        Ref("learnCounterText", counter);
        Ref("learnTileNameText", name);
        Ref("learnTileCategoryText", cat);
        Ref("learnTileDescriptionText", desc);
        Ref("learnChiText", chi);
        Ref("learnPonText", pon);
        RefArray("learnTabImages", tabImgs);
        RefArray("learnTabTexts", tabTxts);
        return p;
    }

    // Ruang kosong setinggi h px desain di dalam Vertical Layout Group
    static void Gap(Transform parent, float h)
    {
        LE(Rect("Gap", parent), h);
    }

    // Tombol bulat di kiri (left = true) atau kanan baris, di tengah vertikal
    static void SideMiddle(RectTransform rt, bool left, float size)
    {
        float x = left ? 0f : 1f;
        rt.anchorMin = rt.anchorMax = new Vector2(x, 0.5f);
        rt.pivot = new Vector2(x, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(P(size), P(size));
    }

    // =====================================================
    // HALAMAN 4 - CHI & PON GUIDE 
    // =====================================================
    static RectTransform BuildCombo(Transform root)
    {
        RectTransform p = Rect("comboPanel", root);
        Stretch(p);

        CameraHeader(p, "CHI & PON GUIDE", "Scan ONE tile to see every CHI and PON it can make", 126);
        var status = StatusPill(p, 160, 310, "POINT CAMERA AT ONE TILE");
        TopLeft(ComfortButton(p), 16, 236, 92, 36);

        // Zoom baris CHI/PON: tombol sebagai alternatif cubit 2 jari 
        RectTransform zoom = Rect("ZoomGroup", p);
        TopRight(zoom, 16, 236, 48, 166);
        VL(zoom, 0, 0, 0, 0, 10, TextAnchor.UpperCenter);
        TextMeshProUGUI tmp;
        var zin = Btn(zoom, "ZoomInButton", "+", GOLD, BG, 48, 14, 24, out tmp);
        var zout = Btn(zoom, "ZoomOutButton", "-", GOLD, BG, 48, 14, 24, out tmp);
        var zres = Btn(zoom, "ResetViewButton", "RESET", CARD, GOLDTXT, 48, 14, 12, out tmp);
        On(zin, gm.ZoomIn);
        On(zout, gm.ZoomOut);
        On(zres, gm.ResetView);

        RectTransform one = BottomSheet(p, "oneTileGroup", 340, 22, 12, 22, 10);
        Handle(one);
        var oneName = Text(one, "oneTileNameText", "NO TILE", Weight.Black, 30, GOLDTXT, TextAlignmentOptions.Center);
        LE(oneName, 36);
        RectTransform card = Rect("ComboCard", one);
        LE(card, 150);
        Img(card, CARD, 14);
        VL(card, 14, 14, 10, 10, 4, TextAnchor.UpperLeft);
        var oneChi = Text(card, "oneTileChiText", "<b>CHI</b>\n-", Weight.Medium, 13, WHITE, TextAlignmentOptions.TopLeft);
        AutoSize(oneChi, 9);
        LE(oneChi, 88);
        RectTransform div = Rect("Divider", card);
        LE(div, 1);
        Img(div, LINE, 0);
        var onePon = Text(card, "oneTilePonText", "<b>PON</b>\n-", Weight.Medium, 13, GOLDTXT, TextAlignmentOptions.TopLeft);
        AutoSize(onePon, 9);
        LE(onePon, 36);

        RectTransform legend = Rect("Legend", one);
        LE(legend, 16);
        var lh = HL(legend, 0, 0, 0, 0, 6);
        lh.childForceExpandWidth = false;
        lh.childControlHeight = false;
        lh.childAlignment = TextAnchor.MiddleCenter;
        LegendItem(legend, GOLDTXT, "Your tile", 70);
        LE(Rect("Gap", legend), -1, 12);
        LegendItem(legend, BLUE, "Tile you need", 96);

        var back = Btn(one, "BackButton", "BACK", CARD, GOLDTXT, 52, 14, 15, out tmp);
        On(back, gm.BackToMenu);

        Ref("comboStatusText", status);
        Ref("oneTileNameText", oneName);
        Ref("oneTileChiText", oneChi);
        Ref("oneTilePonText", onePon);
        return p;
    }

    static void LegendItem(Transform parent, Color color, string label, float width)
    {
        RectTransform sq = Rect("Swatch", parent);
        LE(sq, -1, 10);
        sq.sizeDelta = new Vector2(P(10), P(10));
        Img(sq, color, 3);
        var t = Text(parent, "Label", label, Weight.Medium, 11, MUTED, TextAlignmentOptions.MidlineLeft);
        LE(t, -1, width);
        t.rectTransform.sizeDelta = new Vector2(P(width), P(16));
    }

    // =====================================================
    // PANEL COMFORT (Reduce Motion + height/reach adaptation)
    // =====================================================
    static RectTransform BuildComfort(Transform root)
    {
        RectTransform p = Rect("comfortPanel", root);
        Stretch(p);

        // latar gelap: ketuk di luar kartu = tutup. Dim dan Sheet bersaudara (bukan induk-anak),
        // supaya ketukan di kartu tidak ikut menutup panel.
        RectTransform dim = Rect("Dim", p);
        Stretch(dim);
        var dimImg = Img(dim, new Color(0f, 0f, 0f, 0.55f), 0);
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.targetGraphic = dimImg;
        dimBtn.transition = Selectable.Transition.None;
        On(dimBtn, comfortUI.ClosePanel);

        RectTransform sheet = BottomSheet(p, "Sheet", 560, 22, 12, 22, 8);
        Handle(sheet);
        LE(Text(sheet, "Title", "COMFORT & ACCESSIBILITY", Weight.Black, 20, GOLDTXT, TextAlignmentOptions.Center), 26);
        LE(Text(sheet, "Subtitle", "Make the 3D view calmer and easier to see", Weight.Medium, 12, MUTED, TextAlignmentOptions.Center), 18);

        TextMeshProUGUI reduceText, tmp;
        var reduce = Btn(sheet, "ReduceMotionButton", "REDUCE MOTION: OFF", CARD, GOLDTXT, 48, 14, 14, out reduceText);
        On(reduce, comfortUI.ToggleReduceMotion);
        var hint = Text(sheet, "ReduceHint", "No pop-in animation, less shaking, models turn only when needed, labels stay level.",
            Weight.Medium, 11, new Color(1, 1, 1, 0.7f), TextAlignmentOptions.Center);
        AutoSize(hint, 8);
        LE(hint, 30);

        TextMeshProUGUI hv, dv, sv;
        var hs = SliderRow(sheet, "Height", "MODEL HEIGHT", "Raise or lower the 3D tile above the real tile",
            ComfortSettings.HeightMin, ComfortSettings.HeightMax, out hv);
        var ds = SliderRow(sheet, "Distance", "HINT DISTANCE", "Move the CHI / PON rows closer or farther",
            ComfortSettings.DistanceMin, ComfortSettings.DistanceMax, out dv);
        var ss = SliderRow(sheet, "Size", "MODEL SIZE", "Make 3D tiles and hint rows bigger or smaller",
            ComfortSettings.SizeMin, ComfortSettings.SizeMax, out sv);

        RectTransform row = ButtonRow(sheet);
        var reset = Btn(row, "ResetButton", "RESET", CARD, GOLDTXT, 52, 14, 15, out tmp);
        var done = Btn(row, "DoneButton", "DONE", GOLD, BG, 52, 14, 15, out tmp);
        On(reset, comfortUI.ResetAll);
        On(done, comfortUI.ClosePanel);

        CRef("panel", p.gameObject);
        CRef("reduceMotionBackground", reduce.GetComponent<Image>());
        CRef("reduceMotionText", reduceText);
        CRef("heightSlider", hs);
        CRef("distanceSlider", ds);
        CRef("sizeSlider", ss);
        CRef("heightValueText", hv);
        CRef("distanceValueText", dv);
        CRef("sizeValueText", sv);
        return p;
    }

    // Judul + nilai (kiri-kanan), keterangan kecil, lalu slider
    static Slider SliderRow(Transform parent, string name, string title, string hint, float min, float max, out TextMeshProUGUI valueText)
    {
        RectTransform head = Rect(name + "Header", parent);
        LE(head, 18);
        var t = Text(head, "Label", title, Weight.Bold, 13, GOLDTXT, TextAlignmentOptions.MidlineLeft);
        Stretch(t.rectTransform);
        valueText = Text(head, "Value", "1.0x", Weight.Semi, 13, WHITE, TextAlignmentOptions.MidlineRight);
        Stretch(valueText.rectTransform);

        var h = Text(parent, name + "Hint", hint, Weight.Medium, 11, new Color(1, 1, 1, 0.65f), TextAlignmentOptions.MidlineLeft);
        AutoSize(h, 8);
        LE(h, 14);

        // Slider: track 8 px, isian emas, pegangan bulat 24 px (mudah disentuh)
        RectTransform rt = Rect(name + "Slider", parent);
        LE(rt, 26);
        RectTransform bg = Rect("Background", rt);
        Offsets(bg, new Vector2(0, 0.5f), new Vector2(1, 0.5f), 0, -4, 0, 4);
        Img(bg, LINE, 4).raycastTarget = true;
        RectTransform fillArea = Rect("Fill Area", rt);
        Offsets(fillArea, new Vector2(0, 0.5f), new Vector2(1, 0.5f), 0, -4, 0, 4);
        RectTransform fill = Rect("Fill", fillArea);
        Stretch(fill);
        Img(fill, GOLD, 4).raycastTarget = false;
        RectTransform handleArea = Rect("Handle Slide Area", rt);
        Offsets(handleArea, new Vector2(0, 0.5f), new Vector2(1, 0.5f), 12, -12, -12, 12);
        RectTransform handle = Rect("Handle", handleArea);
        handle.sizeDelta = new Vector2(P(24), 0);
        var handleImg = Img(handle, IVORY, 12);

        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill;
        s.handleRect = handle;
        s.targetGraphic = handleImg;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.value = 1f;
        return s;
    }

    // Tombol COMFORT kecil bergaris emas. Tidak ikut layout group halaman (ignoreLayout).
    static RectTransform ComfortButton(Transform parent)
    {
        TextMeshProUGUI label;
        var b = Btn(parent, "ComfortButton", "COMFORT", BG, GOLDTXT, 36, 18, 11, out label, true);
        b.GetComponent<LayoutElement>().ignoreLayout = true;
        On(b, comfortUI.OpenPanel);
        return (RectTransform)b.transform;
    }

    static RectTransform BuildScanFrame(Transform root)
    {
        RectTransform f = Rect("ScanFrame", root);
        f.anchorMin = f.anchorMax = new Vector2(0.5f, 0.5f);
        f.sizeDelta = new Vector2(P(350), P(350));
        Color c = new Color(GOLD.r, GOLD.g, GOLD.b, 0.9f);
        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = new Vector2(i % 2, i / 2);
            for (int k = 0; k < 2; k++)
            {
                RectTransform bar = Rect("Corner" + i + "_" + k, f);
                bar.anchorMin = bar.anchorMax = bar.pivot = corner;
                bar.sizeDelta = k == 0 ? new Vector2(P(30), P(3)) : new Vector2(P(3), P(30));
                Img(bar, c, 0).raycastTarget = false;
            }
        }
        RectTransform pill = Rect("CaptionPill", f);
        TopCenter(pill, 10, 190, 24);
        Img(pill, new Color(BG.r, BG.g, BG.b, 0.7f), 12).raycastTarget = false;
        var cap = Text(pill, "Caption", "Keep tiles inside the frame", Weight.Semi, 11, WHITE, TextAlignmentOptions.Center);
        Stretch(cap.rectTransform);
        return f;
    }

    // =====================================================
    // HALAMAN 6 - HOW TO PLAY
    // =====================================================
    static RectTransform BuildHowTo(Transform root)
    {
        RectTransform p = Rect("howToPanel", root);
        Stretch(p);
        Img(p, BG, 0);
        VL(p, 20, 20, 52, 24, 10, TextAnchor.UpperCenter);

        HeaderBlock(p, "HOW TO PLAY", "Learn how to use Mahjong AR");

        StepCard(p, "01", "SCAN MAHJONG",
            "Point your camera at a Mahjong tile. If more than one tile is visible, tap the tile you want. Drag to rotate, pinch to zoom.");
        StepCard(p, "02", "LEARN MAHJONG TILES",
            "Browse the four tile groups. See each tile's name, meaning, and every CHI and PON it can make.");
        StepCard(p, "03", "CHI & PON GUIDE",
            "Scan one tile. 3D rows appear behind it showing every CHI and PON. Blue tiles are the ones you still need. Pinch or use + / - to zoom.");
        StepCard(p, "04", "COMFORT",
            "Tap COMFORT to turn on Reduce Motion or change the model height, hint distance, and size. Keep tiles inside the gold frame.");

        RectTransform tips = Rect("TipsCard", p);
        LE(tips, 84);
        Img(tips, BROWN, 16);
        RectTransform inner = Rect("Inner", tips);
        Stretch(inner, 1, 1, 1, 1);
        Img(inner, BG, 15);
        var tt = Text(tips, "Title", "TIPS FOR THE AI", Weight.Bold, 12, GOLDTXT, TextAlignmentOptions.TopLeft);
        Offsets(tt.rectTransform, new Vector2(0, 1), new Vector2(1, 1), 16, -30, -16, -12);
        var tb = Text(tips, "Body",
            "Use good light, keep tiles face up, and hold the phone 20-50 cm above the table. Move the phone slowly first so the table is found.",
            Weight.Medium, 12, new Color(1, 1, 1, 0.82f), TextAlignmentOptions.TopLeft);
        Stretch(tb.rectTransform, 16, 16, 32, 8);
        AutoSize(tb, 8);

        LE(Rect("FlexSpacer", p), -1, -1, 1);
        TextMeshProUGUI tmp;
        var back = Btn(p, "BackToMenuButton", "BACK TO MENU", GOLD, BG, 52, 14, 15, out tmp);
        On(back, gm.BackToMenu);
        return p;
    }

    static void StepCard(Transform parent, string number, string title, string body)
    {
        RectTransform card = Rect("Step" + number, parent);
        LE(card, 96);
        Img(card, CARD, 16);
        var n = Text(card, "Number", number, Weight.Black, 22, GOLDTXT, TextAlignmentOptions.TopLeft);
        Offsets(n.rectTransform, new Vector2(0, 0), new Vector2(0, 1), 16, 12, 60, -12);
        var t = Text(card, "Title", title, Weight.Bold, 14, WHITE, TextAlignmentOptions.TopLeft);
        Offsets(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), 64, -36, -16, -14);
        var b = Text(card, "Body", body, Weight.Medium, 12, new Color(1, 1, 1, 0.82f), TextAlignmentOptions.TopLeft);
        Stretch(b.rectTransform, 64, 16, 38, 8);
        AutoSize(b, 8);
    }

    // =====================================================
    // POTONGAN UI YANG DIPAKAI BERULANG
    // =====================================================

    // Header gelap di atas panel berkamera
    static void CameraHeader(Transform parent, string title, string subtitle, float height)
    {
        RectTransform header = Rect("Header", parent);
        TopStretch(header, 0, height);
        Img(header, new Color(BG.r, BG.g, BG.b, 0.94f), 0);
        var t = Text(header, "Title", title, Weight.Black, 26, GOLDTXT, TextAlignmentOptions.Center);
        TopStretch(t.rectTransform, 52, 34);
        var s = Text(header, "Subtitle", subtitle, Weight.Medium, 13, MUTED, TextAlignmentOptions.Center);
        TopStretch(s.rectTransform, 88, 20);
    }

    // Judul + subjudul untuk panel berlatar penuh (Learn, How To)
    static void HeaderBlock(Transform parent, string title, string subtitle)
    {
        RectTransform h = Rect("Header", parent);
        LE(h, 56);
        var t = Text(h, "Title", title, Weight.Black, 26, GOLDTXT, TextAlignmentOptions.Center);
        TopStretch(t.rectTransform, 0, 32);
        var s = Text(h, "Subtitle", subtitle, Weight.Medium, 13, MUTED, TextAlignmentOptions.Center);
        TopStretch(s.rectTransform, 34, 20);
    }

    static TextMeshProUGUI StatusPill(Transform parent, float y, float width, string text)
    {
        RectTransform pill = Rect("StatusPill", parent);
        TopCenter(pill, y, width, 36);
        Img(pill, new Color(BG.r, BG.g, BG.b, 0.8f), 18).raycastTarget = false;
        var t = Text(pill, "StatusText", text, Weight.Bold, 12, GOLDTXT, TextAlignmentOptions.Center);
        Stretch(t.rectTransform, 12, 12, 0, 0);
        AutoSize(t, 8);
        return t;
    }

    static RectTransform BottomSheet(Transform parent, string name, float height, float side, float top, float bottom, float spacing)
    {
        RectTransform s = Rect(name, parent);
        Offsets(s, new Vector2(0, 0), new Vector2(1, 0), 0, -24, 0, height);
        Img(s, BG, 24);
        VL(s, side, side, top, bottom + 24, spacing, TextAnchor.UpperCenter);
        return s;
    }

    static void Handle(Transform sheet)
    {
        RectTransform row = Rect("Handle", sheet);
        LE(row, 4);
        RectTransform bar = Rect("Bar", row);
        Center(bar, 0, 0, 40, 4);
        Img(bar, HANDLE, 2).raycastTarget = false;
    }

    static RectTransform ButtonRow(Transform parent)
    {
        RectTransform row = Rect("ButtonRow", parent);
        LE(row, 52);
        HL(row, 0, 0, 0, 0, 12);
        return row;
    }

    // Tombol dengan latar rounded. outline = true -> bingkai emas, isi warna bg.
    static Button Btn(Transform parent, string name, string label, Color bg, Color fg, float height,
                      float radius, float fontSize, out TextMeshProUGUI labelText, bool outline = false)
    {
        RectTransform rt = Rect(name, parent);
        LE(rt, height);
        var img = Img(rt, outline ? GOLD : bg, radius);
        if (outline)
        {
            RectTransform inner = Rect("Inner", rt);
            Stretch(inner, 1.5f, 1.5f, 1.5f, 1.5f);
            Img(inner, bg, Mathf.Max(1f, radius - 1.5f)).raycastTarget = false;
        }
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        btn.colors = colors;

        labelText = Text(rt, "Label", label, Weight.Semi, fontSize, fg, TextAlignmentOptions.Center);
        Stretch(labelText.rectTransform, 4, 4, 0, 0);
        return btn;
    }

    // =====================================================
    // HELPER DASAR
    // =====================================================
    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image Img(RectTransform rt, Color color, float radius)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        if (radius > 0f && rounded != null)
        {
            img.sprite = rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, P(radius));
        }
        return img;
    }

    static TextMeshProUGUI Text(Transform parent, string name, string text, Weight weight, float size, Color color, TextAlignmentOptions align)
    {
        RectTransform rt = Rect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = FontFor(weight);
        if (f != null) t.font = f;
        if (NeedsFakeBold(weight)) t.fontStyle = FontStyles.Bold;
        t.text = text;
        t.fontSize = P(size);
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    static void AutoSize(TextMeshProUGUI t, float minDesign)
    {
        t.enableAutoSizing = true;
        t.fontSizeMax = t.fontSize;
        t.fontSizeMin = P(minDesign);
    }

    static LayoutElement LE(Component c, float height = -1, float width = -1, float flexHeight = -1, float flexWidth = -1)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        // tinggi tetap: min = preferred, dan tidak ikut membesar (flexible 0)
        if (height >= 0) { le.preferredHeight = P(height); le.minHeight = P(height); le.flexibleHeight = 0; }
        if (width >= 0) { le.preferredWidth = P(width); le.flexibleWidth = 0; }
        if (flexHeight >= 0) le.flexibleHeight = flexHeight;
        if (flexWidth >= 0) le.flexibleWidth = flexWidth;
        return le;
    }

    static VerticalLayoutGroup VL(RectTransform rt, float l, float r, float t, float b, float spacing, TextAnchor align)
    {
        var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        g.padding = new RectOffset(Mathf.RoundToInt(P(l)), Mathf.RoundToInt(P(r)), Mathf.RoundToInt(P(t)), Mathf.RoundToInt(P(b)));
        g.spacing = P(spacing);
        g.childAlignment = align;
        g.childControlWidth = true;
        g.childControlHeight = true;
        g.childForceExpandWidth = true;
        g.childForceExpandHeight = false;
        return g;
    }

    static HorizontalLayoutGroup HL(RectTransform rt, float l, float r, float t, float b, float spacing)
    {
        var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        g.padding = new RectOffset(Mathf.RoundToInt(P(l)), Mathf.RoundToInt(P(r)), Mathf.RoundToInt(P(t)), Mathf.RoundToInt(P(b)));
        g.spacing = P(spacing);
        g.childAlignment = TextAnchor.MiddleCenter;
        g.childControlWidth = true;
        g.childControlHeight = true;
        g.childForceExpandWidth = true;
        g.childForceExpandHeight = true;
        return g;
    }

    // ---------- posisi (semua dalam px desain) ----------
    static void Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(P(l), P(b));
        rt.offsetMax = new Vector2(-P(r), -P(t));
    }

    // y = jarak dari atas, h = tinggi
    static void TopStretch(RectTransform rt, float y, float h, float l = 0, float r = 0)
    {
        Offsets(rt, new Vector2(0, 1), new Vector2(1, 1), l, -(y + h), -r, -y);
    }

    static void TopCenter(RectTransform rt, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -P(y));
        rt.sizeDelta = new Vector2(P(w), P(h));
    }

    static void TopRight(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-P(x), -P(y));
        rt.sizeDelta = new Vector2(P(w), P(h));
    }

    static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(P(x), -P(y));
        rt.sizeDelta = new Vector2(P(w), P(h));
    }

    static void Center(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(P(x), P(y));
        rt.sizeDelta = new Vector2(P(w), P(h));
    }

    // offsetMin = (minX, minY), offsetMax = (maxX, maxY), dalam px desain
    static void Offsets(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, float minX, float minY, float maxX, float maxY)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(P(minX), P(minY));
        rt.offsetMax = new Vector2(P(maxX), P(maxY));
    }

    // ---------- wiring GameManager ----------
    static void On(Button b, UnityAction action)
    {
        UnityEventTools.AddPersistentListener(b.onClick, action);
    }

    static void Ref(string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop == null) { Debug.LogWarning("Field GameManager tidak ditemukan: " + field); return; }
        prop.objectReferenceValue = value;
    }

    // field ComfortSettingsUI
    static void CRef(string field, Object value)
    {
        var prop = cso.FindProperty(field);
        if (prop == null) { Debug.LogWarning("Field ComfortSettingsUI tidak ditemukan: " + field); return; }
        prop.objectReferenceValue = value;
    }

    static void RefIfFound(string field, Object value)
    {
        if (value != null) Ref(field, value);
    }

    static void RefArray(string field, Object[] values)
    {
        var prop = so.FindProperty(field);
        if (prop == null || !prop.isArray) { Debug.LogWarning("Array GameManager tidak ditemukan: " + field); return; }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
        Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
    }

    // ---------- font ----------
    static void LoadFonts()
    {
        fontBlack = FindFont(new[] { "black", "extrabold", "heavy" }, null);
        fontBold = FindFont(new[] { "bold" }, new[] { "semibold", "extrabold" });
        fontSemi = FindFont(new[] { "semibold" }, null);
        fontMedium = FindFont(new[] { "medium", "regular" }, null);
    }

    static TMP_FontAsset FindFont(string[] keys, string[] exclude)
    {
        TMP_FontAsset best = null;
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace("-", "").Replace(" ", "");
            bool match = false;
            foreach (var k in keys) if (n.Contains(k)) match = true;
            if (exclude != null) foreach (var e in exclude) if (n.Contains(e)) match = false;
            if (!match) continue;
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) continue;
            if (n.Contains("poppins")) return f;   // utamakan Poppins
            if (best == null) best = f;
        }
        return best;
    }

    static TMP_FontAsset FontFor(Weight w)
    {
        switch (w)
        {
            case Weight.Black: return fontBlack ?? fontBold ?? fontSemi ?? fontMedium;
            case Weight.Bold: return fontBold ?? fontBlack ?? fontSemi ?? fontMedium;
            case Weight.Semi: return fontSemi ?? fontBold ?? fontMedium;
            default: return fontMedium ?? fontSemi;
        }
    }

    // Jika font tebal tidak ada, pakai gaya Bold dari TMP
    static bool NeedsFakeBold(Weight w)
    {
        if (w == Weight.Black) return fontBlack == null;
        if (w == Weight.Bold) return fontBold == null && fontBlack == null;
        return false;
    }

    // ---------- sprite sudut bulat (dibuat sekali, disimpan di Assets/UI) ----------
    static Sprite GetRoundedSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (existing != null) return existing;

        const int size = 96, radius = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }
        tex.Apply();

        Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
        File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(SpritePath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(radius, radius, radius, radius);
        importer.spritePixelsPerUnit = 100;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }

    static Color Hex(string hex)
    {
        Color c;
        ColorUtility.TryParseHtmlString("#" + hex, out c);
        return c;
    }
}
#endif
