using System.Collections.Generic;
using UnityEngine;

// Tampilan 3D sebuah tile fisik, diatur oleh GameManager.
public enum TileView
{
    Hidden,     // tidak tampil apa pun (Menu / Learn / How To)
    Candidate,  // Scan & Chi & Pon Guide: tile lain yang belum dipilih -> alas abu-abu + "TAP"
    Selected    // tile yang sedang dipilih -> model (Scan) atau baris CHI/PON (Guide) + alas kuning
}

// Satu tile fisik yang ditemukan AI, ditempatkan di dunia nyata (pengganti TileTargetHandler Vuforia).
// Dibuat dan dihapus oleh DetectedTileManager. Struktur:
//   Detected_xxx      (titik di meja tempat tile asli; Y = normal meja, Z = menjauhi kamera)
//     Pivot           (skala tile + BoxCollider untuk diketuk)
//       Pad           (alas warna, rebah di meja di sekeliling tile asli)
//       Stand         (melayang di atas tile asli, model berdiri menghadap kamera)
//         Tile_xxx    (model 3D, bisa diputar & di-zoom)
//       Label         (teks 3D di atas model, selalu menghadap kamera)
//     HintRows        (baris CHI / PON, Chi & Pon Guide)
public class DetectedTile : MonoBehaviour
{
    public TileData Tile { get; private set; }
    public bool IsTracked { get; private set; }
    public float LastSeen { get; private set; }
    /// <summary>Berapa kali tile ini terdeteksi AI.</summary>
    public int Hits { get; private set; }
    /// <summary>true setelah cukup sering terdeteksi (bukan deteksi palsu sesaat).</summary>
    public bool Confirmed { get; set; }
    /// <summary>Lama tile tidak terlihat (detik), dihitung DetectedTileManager. 0 = baru saja terdeteksi.</summary>
    [HideInInspector] public float Unseen;
    /// <summary>true jika tile terdeteksi pada hasil AI terakhir.</summary>
    [HideInInspector] public bool SeenThisCycle;
    public Transform Pivot { get; private set; }
    public Transform Model { get; private set; }
    public float BaseScale { get; private set; }
    public TileView View { get; private set; } = TileView.Hidden;

    [HideInInspector] public float Zoom = 1f;
    [HideInInspector] public float RotY = 0f;
    [HideInInspector] public float RotX = 0f;

    private float floatHeight;     // jarak bawah model 3D dari meja (meter)
    private Transform stand;
    private Vector3 uprightPos;

    // ---- voting jenis tile (mengurangi salah baca, mis. 9 terbaca 6) ----
    private readonly Dictionary<int, float> votes = new Dictionary<int, float>();
    private readonly List<int> voteKeys = new List<int>();
    /// <summary>Bobot suara lama yang dipertahankan tiap deteksi (0.7 = suara lama cepat memudar).</summary>
    public static float voteDecay = 0.7f;
    /// <summary>Jenis baru harus punya suara sekian kali lebih besar sebelum tile berganti jenis (setelah tile stabil).</summary>
    public static float switchMargin = 1.3f;
    /// <summary>Selama jumlah suara masih sedikit, tile langsung mengikuti jenis dengan suara terbanyak (tanpa margin).</summary>
    public static int freeSwitchVotes = 3;
    private int voteCount;
    /// <summary>Jika AI membaca jenis LAIN yang sama sebanyak ini berturut-turut, tile dianggap sudah diganti.</summary>
    public static int switchAfterDisagree = 2;
    /// <summary>Jika tile tidak terbaca selama ini (detik), mis. tertutup tangan saat ditukar, voting diulang dari awal.</summary>
    public static float voteResetGap = 1.0f;
    private int disagreeCount;
    private int lastDisagreeIndex = -1;
    private float lastVoteTime = -1f;

    // ---- kestabilan posisi ----
    /// <summary>Perubahan posisi lebih kecil dari ini (meter) diabaikan, supaya model tidak bergetar.</summary>
    public static float positionDeadzone = 0.004f;
    /// <summary>Saat Reduce Motion: model baru berputar menghadap kamera jika selisih sudut lebih dari ini.</summary>
    public static float reduceMotionTurnAngle = 30f;
    private Vector3 targetPos;
    private Quaternion targetRot;
    private float smoothing = 0.35f;

    // ---- ukuran asli (sebelum slider comfort) ----
    private float tileLengthM;
    private float appear = 1f;     // animasi muncul 0..1 (dilewati saat Reduce Motion)
    private BoxCollider col;
    private bool lyingMode;   // true di Chi & Pon Guide: model utama rebah seperti baris CHI/PON
    private Quaternion uprightRot = Quaternion.identity;
    private float hintTileScale = 0.75f;
    private GameObject pad;
    private Vector3 padBaseScale = Vector3.one;
    /// <summary>Alas tile yang belum dipilih (TAP) dibuat sedikit lebih besar supaya mudah dilihat & disentuh.</summary>
    public static float candidatePadScale = 1.3f;
    private TMPro.TextMeshPro label;
    private GameObject hintRoot;
    private bool hasPose;

    // =====================================================
    // INIT & POSE (dipanggil DetectedTileManager)
    // =====================================================
    // uprightEuler: rotasi supaya model (muka +Y) berdiri dengan muka menghadap kamera (-Z)
    public void Init(TileData tile, float tileLength, float floatAboveTable, Vector3 uprightEuler)
    {
        Tile = tile;
        floatHeight = floatAboveTable;
        var f = TileModelFactory.Instance;

        tileLengthM = tileLength;

        Pivot = new GameObject("Pivot").transform;
        Pivot.SetParent(transform, false);
        BaseScale = tileLength / f.standardLength;
        Pivot.localScale = Vector3.one * BaseScale;

        // Alas rebah di meja, menandai tile asli
        pad = f.CreatePad(Pivot, PadColor.Neutral);
        pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        padBaseScale = pad.transform.localScale;

        // Model melayang di atas tile asli, berdiri menghadap kamera
        stand = new GameObject("Stand").transform;
        stand.SetParent(Pivot, false);
        uprightRot = Quaternion.Euler(uprightEuler);
        stand.localRotation = uprightRot;

        GameObject modelObject = f.Create(tile);
        if (modelObject != null)
        {
            Model = modelObject.transform;
            Model.SetParent(stand, false);
        }

        // Collider mencakup alas dan model supaya mudah diketuk
        col = Pivot.gameObject.AddComponent<BoxCollider>();
        label = TileModelFactory.MakeLabel(Pivot, "", Vector3.zero, 1f, Color.white, 3f, true);

        LayoutStand();   // posisi model, collider, dan label mengikuti slider tinggi

        Pivot.gameObject.SetActive(false);
        IsTracked = true;
        LastSeen = Time.time;
    }

    // Tinggi model di atas tile asli mengikuti slider "Model Height".
    private void LayoutStand()
    {
        var f = TileModelFactory.Instance;
        if (f == null || stand == null) return;

        // ketinggian dalam satuan lokal Pivot (1 satuan = BaseScale meter)
        float lift = floatHeight * ComfortSettings.ModelHeight / BaseScale;
        float half = f.standardLength / 2f;

        uprightPos = new Vector3(0f, lift + half, 0f);
        if (!lyingMode) stand.localPosition = uprightPos;

        if (col != null)
        {
            col.center = new Vector3(0f, (lift + 2f * half) / 2f, 0f);
            col.size = new Vector3(1.8f, lift + 2f * half + 0.4f, 1.8f);
        }
        // Label TAP (tile belum dipilih) diletakkan rendah tepat di atas alas, supaya tidak menutupi
        // model 3D tile yang sedang dipilih di sebelahnya (temuan pengujian user).
        bool candidate = View == TileView.Candidate;
        if (label != null)
            label.transform.parent.localPosition = candidate
                ? new Vector3(0f, 0.8f, 0f)
                : new Vector3(0f, lift + 2f * half + 0.6f, 0f);
        if (pad != null) pad.transform.localScale = padBaseScale * (candidate ? candidatePadScale : 1f);
    }

    /// <summary>Dipanggil DetectedTileManager saat slider/toggle comfort berubah.</summary>
    public void ApplyComfort()
    {
        LayoutStand();
        ApplyView();
        if (hintRoot != null) ShowHint();   // susun ulang baris CHI/PON dengan jarak/ukuran baru
    }

    // position = titik di meja, up = normal meja, camForward = arah pandang kamera
    public void UpdatePose(Vector3 position, Vector3 up, Vector3 camForward, float smoothingValue)
    {
        MarkSeen();
        Hits++;

        Vector3 fwd = Vector3.ProjectOnPlane(camForward, up);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
        Quaternion faceCamera = Quaternion.LookRotation(fwd.normalized, up);

        if (!hasPose)
        {
            transform.SetPositionAndRotation(position, faceCamera);
            targetPos = position;
            targetRot = faceCamera;
            hasPose = true;
            return;
        }

        bool reduce = ComfortSettings.ReduceMotion;
        smoothing = reduce ? smoothingValue * 0.5f : smoothingValue;

        float dz = reduce ? positionDeadzone * 2f : positionDeadzone;
        if (Vector3.Distance(targetPos, position) > dz) targetPos = position;

        if (!reduce || Quaternion.Angle(targetRot, faceCamera) > reduceMotionTurnAngle)
            targetRot = faceCamera;
    }

    public void MarkSeen()
    {
        LastSeen = Time.time;
        Unseen = 0f;
        SeenThisCycle = true;
    }

    private void Update()
    {
        if (!hasPose) return;

        // Gerak halus per frame (bukan lompat per hasil AI) -> lebih nyaman dilihat
        float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(smoothing), Time.deltaTime * 30f);
        transform.position = Vector3.Lerp(transform.position, targetPos, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);

        if (appear < 1f)
        {
            appear = Mathf.MoveTowards(appear, 1f, Time.deltaTime / 0.18f);
            ApplyView();
        }
    }

    public TileData Vote(TileData detected, float score)
    {
        if (lastVoteTime > 0f && Time.time - lastVoteTime > voteResetGap)
        {
            votes.Clear();
            voteCount = 0;
            disagreeCount = 0;
        }
        lastVoteTime = Time.time;

        if (Tile != null && detected.index != Tile.index)
        {
            if (detected.index == lastDisagreeIndex) disagreeCount++;
            else { lastDisagreeIndex = detected.index; disagreeCount = 1; }

            if (disagreeCount >= switchAfterDisagree)
            {
                votes.Clear();
                votes[detected.index] = score * score;
                voteCount = 1;
                disagreeCount = 0;
                return detected;
            }
        }
        else disagreeCount = 0;

        voteKeys.Clear();
        voteKeys.AddRange(votes.Keys);
        foreach (int k in voteKeys) votes[k] *= voteDecay;

        float v;
        votes.TryGetValue(detected.index, out v);
        votes[detected.index] = v + score * score;
        voteCount++;

        float margin = voteCount <= freeSwitchVotes ? 1f : switchMargin;

        int best = Tile.index;
        float bestVal;
        votes.TryGetValue(best, out bestVal);
        foreach (var kv in votes)
        {
            if (kv.Key != best && kv.Value > bestVal * margin)
            {
                best = kv.Key;
                bestVal = kv.Value;
            }
        }
        return TileDatabase.ByIndex(best);
    }


    public void ReplaceTile(TileData tile)
    {
        if (tile == null || Tile == null || tile.index == Tile.index) return;
        Tile = tile;
        name = "Detected_" + tile.id;
        ClearHint();
        if (Model != null) Destroy(Model.gameObject);
        Model = null;
        GameObject modelObject = TileModelFactory.Instance.Create(tile);
        if (modelObject != null)
        {
            Model = modelObject.transform;
            Model.SetParent(stand != null ? stand : Pivot, false);
        }
    }

      public Vector3 TablePoint { get { return transform.position; } }

    private void OnDestroy()
    {
        IsTracked = false;
        if (Confirmed && GameManager.Instance != null) GameManager.Instance.TileLost(this);
    }

    public void SetView(TileView view)
    {
        View = view;
        if (Pivot == null) return;

        bool show = view != TileView.Hidden;
        if (show && !Pivot.gameObject.activeSelf) appear = ComfortSettings.ReduceMotion ? 1f : 0.6f;
        Pivot.gameObject.SetActive(show);
        if (!show) { ClearHint(); return; }

        if (Model != null) Model.gameObject.SetActive(view != TileView.Candidate);

        var f = TileModelFactory.Instance;
        switch (view)
        {
            case TileView.Candidate: f.SetPadColor(pad, PadColor.Neutral); SetLabel("TAP", Color.white); break;
            case TileView.Selected: f.SetPadColor(pad, PadColor.Selected); SetLabel("", Color.white); break;
        }
        LayoutStand(); 

        if (view != TileView.Selected) ClearHint();
    }

    private void SetLabel(string text, Color color)
    {
        if (label == null) return;
        label.text = text;
        label.color = color;
        label.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void ApplyView()
    {
        if (Pivot == null) return;
        float pop = appear < 1f ? Mathf.SmoothStep(0.6f, 1f, appear) : 1f;
        Pivot.localScale = Vector3.one * BaseScale * Zoom * ComfortSettings.ModelSize * pop;
        if (hintRoot != null) hintRoot.transform.localScale = Vector3.one * Zoom;
        if (Model == null) return;
        if (stand == null) return;
        if (!lyingMode) stand.localRotation = Quaternion.Euler(RotX, RotY, 0f) * uprightRot;
    }

    public void ResetView()
    {
        Zoom = 1f;
        RotY = 0f;
        RotX = 0f;
        ApplyView();
    }

    public void ShowHint()
    {
        ClearHint();
        if (Tile == null || TileModelFactory.Instance == null) return;

        lyingMode = true;
        if (stand != null) stand.gameObject.SetActive(false);

        var rows = new List<TileData[]>(TileRules.GetChiOptions(Tile));
        var names = new List<string>();
        for (int i = 0; i < rows.Count; i++) names.Add("CHI");
        rows.Add(TileRules.GetPon(Tile));
        names.Add("PON");

        hintRoot = new GameObject("HintRows");
        hintRoot.transform.SetParent(transform, false);
        hintRoot.transform.localScale = Vector3.one * Zoom;   // zoom tetap terjaga saat baris disusun ulang

        float s = BaseScale * hintTileScale * ComfortSettings.ModelSize;
        float firstRow = 1.9f * s * ComfortSettings.HintDistance;

        for (int r = 0; r < rows.Count; r++)
        {
            float z = firstRow + r * 1.9f * s;

            for (int k = 0; k < 3; k++)
            {
                TileData t = rows[r][k];
                GameObject item = TileModelFactory.Instance.Create(t);
                if (item == null) continue;

                item.transform.SetParent(hintRoot.transform, false);
                item.transform.localScale = Vector3.one * s;
                item.transform.localPosition = new Vector3((k - 1) * 1.5f * s, 0f, z);
                item.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                bool isScanned = t.index == Tile.index && (names[r] == "PON" ? k == 0 : true);
                TileModelFactory.Instance.CreatePad(item.transform, isScanned ? PadColor.Selected : PadColor.Add);
            }

            Color c = names[r] == "CHI" ? TileModelFactory.PadTint(PadColor.Keep) : TileModelFactory.PadTint(PadColor.Selected);
            TileModelFactory.MakeLabel(hintRoot.transform, names[r], new Vector3(-2.9f * s, 0.6f * s, z), s, c, 3f, true);
        }

        if (!Tile.IsNumber)
        {
            TileModelFactory.MakeLabel(hintRoot.transform, "NO CHI",
                new Vector3(0f, 0.6f * s, firstRow + 1.7f * s), s, TileModelFactory.PadTint(PadColor.Remove), 3f, true);
        }
    }

    public void ClearHint()
    {
        if (lyingMode && stand != null)
        {
            lyingMode = false;
            stand.gameObject.SetActive(true);
            stand.localPosition = uprightPos;
            stand.localRotation = Quaternion.Euler(RotX, RotY, 0f) * uprightRot;
        }
        if (hintRoot != null)
        {
            Destroy(hintRoot);
            hintRoot = null;
        }
    }
}