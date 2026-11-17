using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ComfortSettingsUI : MonoBehaviour
{
    [Header("Referensi (diisi otomatis oleh Build UI)")]
    public TileDetector detector;
    public GameObject panel;
    public Image reduceMotionBackground;
    public TMP_Text reduceMotionText;
    public Slider heightSlider;
    public Slider distanceSlider;
    public Slider sizeSlider;
    public TMP_Text heightValueText;
    public TMP_Text distanceValueText;
    public TMP_Text sizeValueText;
    public RectTransform scanFrame;

    [Header("Bingkai scan")]
    public bool showScanFrame = true;
    [Tooltip("Jarak bingkai ke dalam dari tepi area scan (unit Canvas)")]
    public float frameInset = 30f;

    static readonly Color Gold = new Color32(0xD9, 0xAE, 0x57, 0xFF);
    static readonly Color GoldText = new Color32(0xF5, 0xC9, 0x6B, 0xFF);
    static readonly Color DarkText = new Color32(0x0F, 0x23, 0x26, 0xFF);
    static readonly Color CardBg = new Color32(0x17, 0x30, 0x2F, 0xFF);

    private bool syncing;
    private RectTransform canvasRt;

    private void Awake()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvasRt = (RectTransform)canvas.rootCanvas.transform;

        Hook(heightSlider, ComfortSettings.HeightMin, ComfortSettings.HeightMax, v => ComfortSettings.ModelHeight = v);
        Hook(distanceSlider, ComfortSettings.DistanceMin, ComfortSettings.DistanceMax, v => ComfortSettings.HintDistance = v);
        Hook(sizeSlider, ComfortSettings.SizeMin, ComfortSettings.SizeMax, v => ComfortSettings.ModelSize = v);

        if (panel != null) panel.SetActive(false);
        SyncFromSettings();
    }

    private void OnEnable() { ComfortSettings.Changed += SyncFromSettings; }
    private void OnDisable() { ComfortSettings.Changed -= SyncFromSettings; }

    private void Hook(Slider s, float min, float max, System.Action<float> onChange)
    {
        if (s == null) return;
        s.minValue = min;
        s.maxValue = max;
        s.onValueChanged.AddListener(v =>
        {
            if (!syncing) onChange(v); 
        });
    }

    // =====================================================
    // DIPANGGIL TOMBOL (disambungkan oleh Build UI)
    // =====================================================
    public void OpenPanel()
    {
        if (panel == null) return;
        panel.SetActive(true);
        SyncFromSettings();
    }

    public void ClosePanel()
    {
        if (panel != null) panel.SetActive(false);
    }

    public void ToggleReduceMotion() { ComfortSettings.ReduceMotion = !ComfortSettings.ReduceMotion; }

    public void ResetAll() { ComfortSettings.ResetAll(); }

    // =====================================================
    // TAMPILAN
    // =====================================================
    private void SyncFromSettings()
    {
        syncing = true;
        bool on = ComfortSettings.ReduceMotion;
        if (reduceMotionText != null)
        {
            reduceMotionText.text = "REDUCE MOTION: " + (on ? "ON" : "OFF");
            reduceMotionText.color = on ? DarkText : GoldText;
        }
        if (reduceMotionBackground != null) reduceMotionBackground.color = on ? Gold : CardBg;

        SetSlider(heightSlider, heightValueText, ComfortSettings.ModelHeight);
        SetSlider(distanceSlider, distanceValueText, ComfortSettings.HintDistance);
        SetSlider(sizeSlider, sizeValueText, ComfortSettings.ModelSize);
        syncing = false;
    }

    private static void SetSlider(Slider s, TMP_Text label, float value)
    {
        if (s != null && !Mathf.Approximately(s.value, value)) s.value = value;
        if (label != null) label.text = value.ToString("0.0") + "x";
    }

    private void Update()
    {
        var gm = GameManager.Instance;
        GameManager.Mode mode = gm != null ? gm.CurrentMode : GameManager.Mode.Menu;
        bool arMode = mode == GameManager.Mode.Scan || mode == GameManager.Mode.Combo;

        // Pindah ke Learn / How To: panel ditutup
        if (panel != null && panel.activeSelf && !arMode && mode != GameManager.Mode.Menu) panel.SetActive(false);

        if (scanFrame == null) return;
        bool panelOpen = panel != null && panel.activeSelf;
        bool show = showScanFrame && arMode && detector != null && detector.Running && !panelOpen;
        if (scanFrame.gameObject.activeSelf != show) scanFrame.gameObject.SetActive(show);
        if (!show || canvasRt == null) return;

        // Area scan (pixel layar) -> unit Canvas
        float k = canvasRt.rect.width / Mathf.Max(1, Screen.width);
        Rect r = detector.CropRect;
        scanFrame.anchorMin = scanFrame.anchorMax = new Vector2(0.5f, 0.5f);
        scanFrame.sizeDelta = new Vector2(r.width * k - 2f * frameInset, r.height * k - 2f * frameInset);
        scanFrame.anchoredPosition = new Vector2(0f, (r.center.y - Screen.height / 2f) * k);
    }
}
