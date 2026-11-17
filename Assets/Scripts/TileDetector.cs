using System;
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

// Satu hasil deteksi AI.
public struct TileDetection
{
    public TileData tile;      // jenis tile (null tidak pernah dikirim)
    public Rect screenRect;    // kotak di koordinat layar Unity (pixel, origin kiri-bawah)
    public float score;        // keyakinan 0..1
    public bool strong;        // true = di atas ambang utama. false = deteksi lemah, hanya untuk "menjaga" tile lama tetap tampil
}

// Menjalankan model YOLO (ONNX) dengan Unity Inference Engine pada gambar kamera AR.
//   1. Gambar kamera AR di-Blit ke RenderTexture persegi (inputSize x inputSize).
//   2. RenderTexture diubah ke tensor (1, 3, 640, 640) lalu dijalankan di Worker.
//   3. Di dalam graph: ambil skor tertinggi per kotak, NMS, lalu buang kotak sisa.
//   4. Kotak diubah ke koordinat layar dan dikirim lewat event OnDetections.
public class TileDetector : MonoBehaviour
{
    [Header("Model AI")]
    [Tooltip("File .onnx hasil export YOLO (drag dari folder Assets/AI)")]
    public ModelAsset modelAsset;
    [Tooltip("labels.txt: satu nama kelas per baris, urutan SAMA dengan data.yaml")]
    public TextAsset labelsAsset;
    public BackendType backend = BackendType.GPUCompute;
    public int inputSize = 640;
    [Tooltip("Batas keyakinan untuk Scan dan Chi & Pon Guide")]
    [Range(0.05f, 0.95f)] public float scoreThreshold = 0.65f;
    [Range(0.05f, 0.95f)] public float iouThreshold = 0.45f;
    [Tooltip("Deteksi lemah (di bawah ambang utama, di atas nilai ini) tidak membuat tile baru, tapi menjaga tile yang sudah ada agar tidak hilang saat HP sedikit bergetar")]
    [Range(0.05f, 0.95f)] public float keepAliveThreshold = 0.3f;
    [Tooltip("Jeda minimal antar deteksi (detik). AI langsung mulai lagi setelah hasil keluar jika jeda sudah lewat")]
    public float interval = 0.05f;
    [Tooltip("Waktu maksimal (ms) yang boleh dipakai AI per frame. Lebih besar = hasil AI lebih cepat, tapi FPS kamera bisa turun. 8-14 ms cocok untuk HP")]
    [Range(2f, 30f)] public float frameBudgetMs = 12f;
    [Tooltip("Jumlah layer minimal per frame (supaya AI tetap maju walau HP lambat). 0 = jalankan seluruh model dalam 1 frame")]
    public int minLayersPerFrame = 4;

    [Header("Area scan")]
    [Tooltip("Tinggi area yang dikirim ke AI, dibanding lebar layar. 1 = persegi (tidak gepeng sama sekali), 1.33 = 3:4, 2.17 = seluruh layar (versi lama, gambar jadi gepeng)")]
    [Range(1f, 2.2f)] public float cropHeightRatio = 1.0f;
    [Tooltip("Geser area scan ke atas (+) / bawah (-), dalam bagian tinggi layar. 0.1 = pas di jendela kamera antara header dan panel bawah UI")]
    [Range(-0.3f, 0.3f)] public float cropCenterOffset = 0.1f;

    [Header("Kamera")]
    [Tooltip("Komponen AR Camera Background di Main Camera (XR Origin)")]
    public ARCameraBackground cameraBackground;
    [Tooltip("Hanya untuk tes di Editor tanpa HP: gambar meja berisi tile")]
    public Texture editorTestTexture;
    [Tooltip("Centang jika kotak debug terbalik atas-bawah")]
    public bool flipY = false;

    [Header("Debug (opsional)")]
    [Tooltip("Tampilkan kotak deteksi + teks info AI (waktu, jumlah tile, tebakan terbaik) di layar")]
    public bool showDebugBoxes = false;
    [Tooltip("RectTransform full-screen di Canvas untuk menggambar kotak deteksi")]
    public RectTransform debugBoxRoot;

    public bool Running { get; set; } = true;
    public float ActiveThreshold { get { return scoreThreshold; } }
    // Ambang di dalam graph dibuat rendah; penyaringan sebenarnya di C# (bisa berubah per mode)
    const float GraphMinScore = 0.25f;
    [Tooltip("Jumlah kandidat kotak dengan skor tertinggi yang masuk ke NMS. Dulu 8400 kotak sekaligus -> NMS makan ±350 ms dalam 1 frame dan kamera tersendat. 100 sudah jauh cukup untuk beberapa tile")]
    public int maxCandidates = 100;
    /// <summary>Rata-rata jarak waktu antar hasil deteksi (detik). Dipakai DetectedTileManager.</summary>
    public float CycleTime { get; private set; } = 0.5f;
    private float lastResultTime = -1f;
    public List<TileDetection> Latest { get; private set; } = new List<TileDetection>();
    public event Action<List<TileDetection>> OnDetections;

    private Worker worker;
    private Model compiledModel;       // disimpan supaya Worker bisa dibuat ulang jika rusak
    private Tensor<float> input;
    private RenderTexture frameRT;
    private RenderTexture screenRT;    // gambar kamera seukuran layar (diperkecil), sebelum dipotong
    private readonly System.Diagnostics.Stopwatch budgetWatch = new System.Diagnostics.Stopwatch();
    /// <summary>Waktu AI dari mulai sampai hasil keluar (ms). Untuk debug & laporan performa.</summary>
    public float LastInferenceMs { get; private set; }
    private TileData[] classToTile;
    private float nextTime;
    private bool busy;                 // true selama menunggu hasil AI selesai (tanpa menghentikan kamera)
    private float busySince;
    private Tensor<float> pendingBoxes, pendingScores;
    private Tensor<int> pendingIds;
    [Tooltip("Jika hasil AI belum siap setelah sekian detik, hasil dibaca paksa (cadangan)")]
    public float readbackTimeout = 2f;
    private IEnumerator scheduling;    // model yang sedang dijalankan sebagian per frame
    private float inferStart;
    private string debugInfo = "AI: waiting...";
    private TMPro.TextMeshProUGUI debugLabel;
    private readonly List<Image> boxPool = new List<Image>();

    // mengubah (cx, cy, w, h) menjadi (x1, y1, x2, y2) untuk NMS
    static readonly float[] CentersToCorners =
    {
        1, 0, 1, 0,
        0, 1, 0, 1,
        -0.5f, 0, 0.5f, 0,
        0, -0.5f, 0, 0.5f
    };

    // =====================================================
    // SETUP
    // =====================================================
    private void Start()
    {
        if (modelAsset == null || labelsAsset == null)
        {
            Debug.LogError("TileDetector: Model Asset dan Labels Asset wajib diisi.");
            enabled = false;
            return;
        }

        BuildLabelMap();

        Model model = ModelLoader.Load(modelAsset);
        var graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(model);
        FunctionalTensor output = Functional.Forward(model, inputs)[0];   // (1, 4 + kelas, 8400)

        FunctionalTensor boxes = output[0, 0..4, ..].Transpose(0, 1);   // (8400, 4)
        FunctionalTensor classScores = output[0, 4.., ..];              // (kelas, 8400)
        FunctionalTensor bestScore = Functional.ReduceMax(classScores, 0);
        FunctionalTensor bestClass = Functional.ArgMax(classScores, 0);
        // Ambil K kandidat dengan skor tertinggi (urut dari yang terbesar).
        // NMS SENGAJA tidak dijalankan di dalam model: layer NMS ukurannya berubah-ubah, sehingga Sentis
        // harus menunggu SEMUA layer sebelumnya selesai di main thread -> kamera berhenti ±350-500 ms
        // setiap siklus AI (terlihat di Profiler). NMS untuk K kotak dihitung di C# (sangat ringan).
        FunctionalTensor[] top = Functional.TopK(bestScore, maxCandidates, 0, true, true);
        FunctionalTensor topScore = top[0];                                  // (K)
        FunctionalTensor topIdx = top[1];                                    // (K)

        compiledModel = graph.Compile(
            Functional.IndexSelect(boxes, 0, topIdx),          // output_0 : (K, 4) cx, cy, w, h
            Functional.IndexSelect(bestClass, 0, topIdx),      // output_1 : (K) kelas
            topScore                                           // output_2 : (K) skor, urut menurun
        );
        worker = new Worker(compiledModel, backend);

        input = new Tensor<float>(new TensorShape(1, 3, inputSize, inputSize));
        frameRT = new RenderTexture(inputSize, inputSize, 0, RenderTextureFormat.ARGB32);

        Debug.Log("TileDetector siap: " + classToTile.Length + " kelas.");
    }

    private void BuildLabelMap()
    {
        string[] lines = labelsAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        classToTile = new TileData[lines.Length];
        int ok = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string id = TileLabelMap.ToTileId(lines[i]);
            classToTile[i] = TileDatabase.ById(id);
            if (classToTile[i] != null) ok++;
            else Debug.LogWarning("TileDetector: kelas '" + lines[i].Trim() + "' tidak dikenali, diabaikan.");
        }
        Debug.Log("TileDetector: " + ok + "/" + lines.Length + " kelas cocok dengan tile.");
    }

    // Worker rusak (mis. error di tengah model) -> buang dan buat baru, supaya AI pulih sendiri
    private void RecreateWorker()
    {
        scheduling = null;
        busy = false;
        pendingBoxes = null; pendingScores = null; pendingIds = null;
        try { worker?.Dispose(); } catch (Exception e) { Debug.LogWarning("TileDetector: dispose worker lama gagal: " + e.Message); }
        worker = null;
        if (compiledModel != null) worker = new Worker(compiledModel, backend);
        Debug.Log("TileDetector: worker AI dibuat ulang.");
    }

    private void OnDestroy()
    {
        worker?.Dispose();
        input?.Dispose();
        if (frameRT != null) frameRT.Release();
        if (screenRT != null) screenRT.Release();
    }

    /// <summary>
    /// Area layar (pixel, origin kiri-bawah) yang dilihat AI. Tile di luar area ini tidak terdeteksi.
    /// Dipakai ComfortSettingsUI untuk menggambar bingkai scan, dan DetectedTileManager.
    /// </summary>
    public Rect CropRect
    {
        get
        {
            float h = Mathf.Min(Screen.height, Screen.width * cropHeightRatio);
            float y = Screen.height * (0.5f + cropCenterOffset) - h / 2f;
            y = Mathf.Clamp(y, 0f, Screen.height - h);
            return new Rect(0f, y, Screen.width, h);
        }
    }

    // =====================================================
    // LOOP DETEKSI
    // =====================================================
    private void Update()
    {
        if (worker == null) return;

        // Menunggu hasil AI selesai dihitung di thread lain. Kamera TIDAK ikut menunggu:
        // setiap frame hanya dicek "sudah siap belum?".
        if (busy)
        {
            bool ready = false;
            try
            {
                ready = pendingBoxes.IsReadbackRequestDone() && pendingIds.IsReadbackRequestDone()
                        && pendingScores.IsReadbackRequestDone();
            }
            catch (Exception) { ready = true; }
            if (!ready && Time.realtimeSinceStartup - busySince < readbackTimeout) return;

            busy = false;
            if (Running) ReadResults();   // AI sudah dimatikan: hasil dibuang
            return;
        }

        // Lanjutkan model yang sedang berjalan: hanya beberapa layer per frame.
        // PENTING: proses yang sudah dimulai SELALU diselesaikan, juga saat AI dimatikan (pindah ke Menu).
        // Dulu proses dibuang di tengah jalan -> worker rusak -> error TensorPool.Dispose dan AI mati total.
        if (scheduling != null)
        {
            bool done = false;
            try
            {
                // Jalankan layer sebanyak mungkin selama masih dalam "jatah waktu" frame ini.
                // HP cepat -> banyak layer per frame (hasil cepat). HP lambat -> FPS kamera tetap terjaga.
                budgetWatch.Restart();
                int n = 0;
                while (n < minLayersPerFrame || budgetWatch.Elapsed.TotalMilliseconds < frameBudgetMs)
                {
                    if (!scheduling.MoveNext()) { done = true; break; }
                    n++;
                }
            }
            catch (Exception e)
            {
                // Error di tengah model: worker dibuat ulang, deteksi berikutnya mulai dari awal (tidak macet)
                debugInfo = "AI ERROR: " + e.GetType().Name + " @ " + Where(e);
                Debug.LogException(e);
                RecreateWorker();
                Publish(new List<TileDetection>());
                return;
            }
            if (done)
            {
                scheduling = null;
                RequestResults();   // minta hasil secara asinkron, dibaca saat sudah siap
            }
            return;
        }

        if (!Running)
        {
            if (Latest.Count > 0) Publish(new List<TileDetection>());
            return;
        }

        if (Time.unscaledTime < nextTime) return;
        nextTime = Time.unscaledTime + interval;

        if (!CaptureFrame()) return;

        inferStart = Time.realtimeSinceStartup;
        try
        {
            TextureConverter.ToTensor(frameRT, input, default);
            if (minLayersPerFrame <= 0)
            {
                worker.Schedule(input);
                RequestResults();
            }
            else
            {
                scheduling = worker.ScheduleIterable(input);
            }
        }
        catch (Exception e)
        {
            debugInfo = "AI ERROR: " + e.GetType().Name + " @ " + Where(e);
            Debug.LogException(e);
            RecreateWorker();
        }
    }

    private bool CaptureFrame()
    {
#if UNITY_EDITOR
        if (editorTestTexture != null)
        {
            Graphics.Blit(editorTestTexture, frameRT);
            return true;
        }
#endif
        if (cameraBackground == null || cameraBackground.material == null) return false;

        // 1) Material background AR menggambar gambar kamera persis seperti yang tampil di layar,
        //    ke RenderTexture dengan rasio layar (tidak gepeng).
        int w = inputSize;
        int h = Mathf.Max(inputSize, Mathf.RoundToInt(inputSize * (float)Screen.height / Mathf.Max(1, Screen.width)));
        if (screenRT == null || screenRT.width != w || screenRT.height != h)
        {
            if (screenRT != null) screenRT.Release();
            screenRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        }
        Graphics.Blit(null, screenRT, cameraBackground.material);

        // 2) Potong area scan (CropRect) lalu kirim ke AI 640x640.
        //    Dulu seluruh layar portrait ditekan jadi persegi -> tile tampak gepeng (sumber salah baca 9/6).
        Rect crop = CropRect;
        Graphics.Blit(screenRT, frameRT,
            new Vector2(1f, crop.height / Screen.height),
            new Vector2(0f, crop.y / Screen.height));
        return true;
    }

    // Hasil AI dibaca LANGSUNG (tanpa async). Dengan backend CPU ini cepat, dan tidak bisa
    // "menggantung" seperti versi async. Hasil kosong (0 tile) tidak dibaca sama sekali.
    // Minta hasil AI tanpa menunggu (non-blocking). Update() mengecek setiap frame apakah sudah siap.
    private void RequestResults()
    {
        try
        {
            pendingBoxes = worker.PeekOutput("output_0") as Tensor<float>;
            pendingIds = worker.PeekOutput("output_1") as Tensor<int>;
            pendingScores = worker.PeekOutput("output_2") as Tensor<float>;
            if (pendingBoxes == null || pendingIds == null || pendingScores == null) return;
            pendingBoxes.ReadbackRequest();
            pendingIds.ReadbackRequest();
            pendingScores.ReadbackRequest();
            busy = true;
            busySince = Time.realtimeSinceStartup;
        }
        catch (Exception e)
        {
            debugInfo = "AI ERROR: " + e.GetType().Name + " @ " + Where(e);
            Debug.LogException(e);
            RecreateWorker();
        }
    }

    // NMS sederhana untuk K kotak yang sudah urut dari skor tertinggi
    private List<int> RunNms(Tensor<float> boxes, Tensor<float> scores, int count)
    {
        var keep = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (scores[i] < GraphMinScore) break;   // urut menurun: sisanya pasti lebih kecil
            float ax1 = boxes[i, 0] - boxes[i, 2] / 2f, ay1 = boxes[i, 1] - boxes[i, 3] / 2f;
            float ax2 = boxes[i, 0] + boxes[i, 2] / 2f, ay2 = boxes[i, 1] + boxes[i, 3] / 2f;
            float areaA = Mathf.Max(0f, ax2 - ax1) * Mathf.Max(0f, ay2 - ay1);
            bool suppressed = false;
            foreach (int k in keep)
            {
                float bx1 = boxes[k, 0] - boxes[k, 2] / 2f, by1 = boxes[k, 1] - boxes[k, 3] / 2f;
                float bx2 = boxes[k, 0] + boxes[k, 2] / 2f, by2 = boxes[k, 1] + boxes[k, 3] / 2f;
                float iw = Mathf.Min(ax2, bx2) - Mathf.Max(ax1, bx1);
                float ih = Mathf.Min(ay2, by2) - Mathf.Max(ay1, by1);
                if (iw <= 0f || ih <= 0f) continue;
                float inter = iw * ih;
                float areaB = (bx2 - bx1) * (by2 - by1);
                if (inter / Mathf.Max(1e-6f, areaA + areaB - inter) > iouThreshold) { suppressed = true; break; }
            }
            if (!suppressed) keep.Add(i);
        }
        return keep;
    }

    private void ReadResults()
    {
        Tensor<float> boxes = null, scores = null;
        Tensor<int> ids = null;
        try
        {
            var boxesOut = worker.PeekOutput("output_0") as Tensor<float>;
            var idsOut = worker.PeekOutput("output_1") as Tensor<int>;
            var scoresOut = worker.PeekOutput("output_2") as Tensor<float>;

            List<int> keep = new List<int>();
            if (boxesOut != null && idsOut != null && scoresOut != null && boxesOut.shape[0] > 0)
            {
                // data sudah siap (ReadbackRequest selesai), jadi ini cepat
                boxes = boxesOut.ReadbackAndClone();
                ids = idsOut.ReadbackAndClone();
                scores = scoresOut.ReadbackAndClone();
                keep = RunNms(boxes, scores, boxes.shape[0]);
            }

            LastInferenceMs = (Time.realtimeSinceStartup - inferStart) * 1000f;
            float now = Time.time;
            if (lastResultTime > 0f) CycleTime = Mathf.Lerp(CycleTime, now - lastResultTime, 0.3f);
            lastResultTime = now;

            BuildResult(boxes, ids, scores, keep);

            // teks debug
            int ms = Mathf.RoundToInt((Time.realtimeSinceStartup - inferStart) * 1000f);
            string top = "-";
            int topPct = 0;
            if (ids != null && scores != null && scores.shape[0] > 0)
            {
                int tc = ids[0];   // kandidat pertama = skor tertinggi
                top = (tc >= 0 && tc < classToTile.Length && classToTile[tc] != null) ? classToTile[tc].name : "?";
                topPct = Mathf.RoundToInt(scores[0] * 100f);
            }
            debugInfo = "AI " + ms + " ms | " + Mathf.RoundToInt(1f / Mathf.Max(0.001f, Time.smoothDeltaTime)) + " FPS (tiap " + Mathf.RoundToInt(CycleTime * 1000f) + " ms) | " + Latest.Count +
                        " tile | top: " + top + " " + topPct + "% (min " + Mathf.RoundToInt(ActiveThreshold * 100f) + "%)";
            DrawDebugBoxes();
        }
        catch (Exception e)
        {
            // Apa pun yang terjadi, deteksi berikutnya tetap jalan.
            debugInfo = "AI ERROR: " + e.GetType().Name + " @ " + Where(e);
            Debug.LogException(e);
            Publish(new List<TileDetection>());
        }
        finally
        {
            if (boxes != null) boxes.Dispose();
            if (ids != null) ids.Dispose();
            if (scores != null) scores.Dispose();
            busy = false;
        }
    }

    private void BuildResult(Tensor<float> boxes, Tensor<int> ids, Tensor<float> scores, List<int> keep)
    {

        var result = new List<TileDetection>();
        int count = (boxes == null || ids == null || scores == null || keep == null) ? 0 : keep.Count;

        // Di Editor dengan gambar uji, seluruh layar dipakai. Di HP hanya area CropRect.
        Rect area = CropRect;
#if UNITY_EDITOR
        if (editorTestTexture != null) area = new Rect(0f, 0f, Screen.width, Screen.height);
#endif
        float sx = area.width / inputSize;
        float sy = area.height / inputSize;
        float keepAlive = Mathf.Min(keepAliveThreshold, ActiveThreshold);

        for (int n = 0; n < count; n++)
        {
            int i = keep[n];
            int cls = ids[i];
            if (cls < 0 || cls >= classToTile.Length || classToTile[cls] == null) continue;
            if (scores[i] < keepAlive) continue;

            float cx = boxes[i, 0], cy = boxes[i, 1], w = boxes[i, 2], h = boxes[i, 3];

            // tensor: y = 0 di atas gambar. Layar Unity: y = 0 di bawah.
            float screenCy = flipY ? area.y + cy * sy : area.y + area.height - cy * sy;
            Rect r = new Rect(area.x + cx * sx - w * sx / 2f, screenCy - h * sy / 2f, w * sx, h * sy);

            result.Add(new TileDetection
            {
                tile = classToTile[cls], screenRect = r, score = scores[i],
                strong = scores[i] >= ActiveThreshold
            });
        }

        Publish(result);
    }

    private void Publish(List<TileDetection> result)
    {
        Latest = result;
        try
        {
            OnDetections?.Invoke(result);
        }
        catch (Exception e)
        {
            // Error di bagian tampilan 3D / UI, bukan di AI. Nama fungsinya ditampilkan supaya mudah dicari.
            debugInfo = "3D/UI ERROR: " + e.GetType().Name + " @ " + Where(e);
            Debug.LogException(e);
        }
        DrawDebugBoxes();
    }

    // Ambil nama fungsi pertama dari stack trace, mis. "DetectedTile.ShowHint"
    private static string Where(Exception e)
    {
        string st = e.StackTrace;
        if (string.IsNullOrEmpty(st)) return e.Message;
        foreach (string raw in st.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("at ")) line = line.Substring(3);
            if (line.Length == 0 || line.StartsWith("System.") || line.StartsWith("UnityEngine.")) continue;
            int paren = line.IndexOf('(');
            return paren > 0 ? line.Substring(0, paren).Trim() : line;
        }
        return e.Message;
    }

    // =====================================================
    // KOTAK DEBUG (untuk mengecek AI berjalan)
    // =====================================================
    private void DrawDebugBoxes()
    {
        if (debugBoxRoot == null) return;
        int n = showDebugBoxes ? Latest.Count : 0;   // deteksi lemah digambar lebih transparan

        // teks info AI di tengah layar bagian atas area kamera
        if (debugLabel == null && showDebugBoxes)
        {
            var go = new GameObject("DebugInfo", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(debugBoxRoot, false);
            debugLabel = go.GetComponent<TMPro.TextMeshProUGUI>();
            debugLabel.fontSize = 34;
            debugLabel.color = new Color(1f, 0.95f, 0.4f);
            debugLabel.alignment = TMPro.TextAlignmentOptions.Center;
            debugLabel.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(-40f, 120f);
            rt.anchoredPosition = new Vector2(0f, 300f);
        }
        if (debugLabel != null)
        {
            debugLabel.gameObject.SetActive(showDebugBoxes);
            debugLabel.text = debugInfo;
        }

        while (boxPool.Count < n)
        {
            var go = new GameObject("DebugBox", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(debugBoxRoot, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.96f, 0.79f, 0.42f, 0.25f);
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;

            var label = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            label.transform.SetParent(go.transform, false);
            var tmp = label.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.fontSize = 22;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = new Vector2(0, 1);
            lrt.anchorMax = new Vector2(1, 1);
            lrt.pivot = new Vector2(0, 0);
            lrt.sizeDelta = new Vector2(0, 30);
            boxPool.Add(img);
        }

        float scale = debugBoxRoot.rect.width / Screen.width;
        for (int i = 0; i < boxPool.Count; i++)
        {
            bool on = i < n;
            boxPool[i].gameObject.SetActive(on);
            if (!on) continue;

            Rect r = Latest[i].screenRect;
            boxPool[i].color = new Color(0.96f, 0.79f, 0.42f, Latest[i].strong ? 0.25f : 0.08f);
            var rt = (RectTransform)boxPool[i].transform;
            rt.anchoredPosition = r.position * scale;
            rt.sizeDelta = r.size * scale;
            boxPool[i].GetComponentInChildren<TMPro.TextMeshProUGUI>().text =
                Latest[i].tile.name + " " + Mathf.RoundToInt(Latest[i].score * 100) + "%";
        }
    }
}
