using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class DetectedTileManager : MonoBehaviour
{
    public static DetectedTileManager Instance { get; private set; }

    [Header("Referensi")]
    public TileDetector detector;
    public ARRaycastManager raycastManager;
    public ARPlaneManager planeManager;
    [Tooltip("Main Camera di dalam XR Origin. Kosong = Camera.main")]
    public Camera arCamera;

    [Header("Ukuran tile fisik (meter)")]
    [Tooltip("Sisi panjang tile Mahjong asli")]
    public float tileLength = 0.028f;
    [Tooltip("Jarak bawah model 3D dari meja (meter). Bisa diubah pengguna lewat slider Model Height")]
    public float floatHeight = 0.045f;
    [Tooltip("Rotasi supaya model berdiri dengan muka menghadap kamera. Kalau muka terbalik/terlihat punggung: coba (90,0,0) atau (-90,0,180)")]
    public Vector3 uprightEuler = new Vector3(90f, 180f, 0f);

    [Header("Pelacakan")]
    [Tooltip("Deteksi dalam jarak dunia ini dianggap tile yang sama (meter)")]
    public float matchDistance = 0.025f;
    [Tooltip("Deteksi juga dianggap tile yang sama jika titik tile lama di layar berjarak kurang dari (nilai ini x lebar kotak) dari tengah kotak")]
    [Range(0.2f, 1.5f)] public float screenMatchFactor = 0.7f;
    [Tooltip("Tile terkonfirmasi dihapus jika tidak terlihat selama ini (detik, hanya dihitung saat HP diam dan tile di area scan)")]
    public float lostTimeout = 2f;
    [Range(0.05f, 1f)] public float smoothing = 0.35f;
    [Tooltip("Tile baru dianggap ada setelah terdeteksi sebanyak ini (mencegah deteksi palsu)")]
    [Min(1)] public int minHits = 2;
    [Tooltip("Deteksi dengan skor setinggi ini langsung dikonfirmasi tanpa menunggu minHits")]
    [Range(0.5f, 1f)] public float instantConfirmScore = 0.8f;
    [Tooltip("Calon tile yang belum terkonfirmasi dihapus jika hilang selama ini (detik)")]
    public float candidateTimeout = 0.8f;
    [Tooltip("HP dianggap bergerak jika berputar lebih cepat dari ini (derajat/detik). Saat bergerak, tile tidak cepat dihapus")]
    public float movingAngularSpeed = 35f;
    [Tooltip("HP dianggap bergerak jika bergeser lebih cepat dari ini (meter/detik)")]
    public float movingLinearSpeed = 0.2f;
    [Tooltip("Jika belum ada bidang meja, tile ditaruh sejauh ini di depan kamera")]
    public float fallbackDistance = 0.35f;
    [Tooltip("Bidang yang lebih jauh dari ini (meter) diabaikan, mis. lantai di bawah meja")]
    public float maxRayDistance = 1.2f;

        public bool HasPlane { get { return planeManager != null && planeManager.trackables.count > 0; } }
    public bool CameraMoving { get; private set; }

    private readonly List<DetectedTile> tiles = new List<DetectedTile>();
    private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private Vector3 lastCamPos;
    private Quaternion lastCamRot;
    private bool hasCamPose;
    private float movingUntil;

    private void Awake() { Instance = this; }

    private void OnEnable()
    {
        if (detector != null) detector.OnDetections += HandleDetections;
        ComfortSettings.Changed += ApplyComfortToAll;
    }

    private void OnDisable()
    {
        if (detector != null) detector.OnDetections -= HandleDetections;
        ComfortSettings.Changed -= ApplyComfortToAll;
    }

    private Camera Cam { get { return arCamera != null ? arCamera : Camera.main; } }

    private void ApplyComfortToAll()
    {
        foreach (var t in tiles) if (t != null) t.ApplyComfort();
    }

    private void HandleDetections(List<TileDetection> detections)
    {
        Camera cam = Cam;
        if (cam == null || TileModelFactory.Instance == null) return;

        foreach (var t in tiles) if (t != null) t.SeenThisCycle = false;
        var used = new HashSet<DetectedTile>();

        detections.Sort((a, b) => b.score.CompareTo(a.score));

        foreach (var d in detections)
        {
            Vector3 pos, up;
            Vector2 basePoint = new Vector2(d.screenRect.center.x, d.screenRect.yMin + d.screenRect.height * 0.1f);
            if (!TryGetWorldPoint(basePoint, cam, out pos, out up)) continue;

            DetectedTile best = FindMatch(d, pos, cam, used);

            if (best == null)
            {
                if (!d.strong) continue;  
                var go = new GameObject("Detected_" + d.tile.id);
                best = go.AddComponent<DetectedTile>();
                best.Init(d.tile, tileLength, floatHeight, uprightEuler);
                best.Vote(d.tile, d.score);
                tiles.Add(best);
            }
            else if (d.strong)
            {
                TileData voted = best.Vote(d.tile, d.score);
                if (voted.index != best.Tile.index)
                {
                    bool wasConfirmed = best.Confirmed;
                    if (wasConfirmed && GameManager.Instance != null) GameManager.Instance.TileLost(best);
                    best.ReplaceTile(voted);
                    if (wasConfirmed && GameManager.Instance != null) GameManager.Instance.TileFound(best);
                }
            }

            used.Add(best);

            if (!d.strong)
            {
                best.MarkSeen();
                continue;
            }

            best.UpdatePose(pos, up, cam.transform.forward, smoothing);

            if (!best.Confirmed && (best.Hits >= minHits || d.score >= instantConfirmScore))
            {
                best.Confirmed = true;
                if (GameManager.Instance != null) GameManager.Instance.TileFound(best);
            }
        }
    }

    private DetectedTile FindMatch(TileDetection d, Vector3 worldPos, Camera cam, HashSet<DetectedTile> used)
    {
        DetectedTile best = null;
        float bestScore = float.MaxValue;
        float boxSize = Mathf.Max(8f, Mathf.Min(d.screenRect.width, d.screenRect.height));
        Vector2 boxBase = new Vector2(d.screenRect.center.x, d.screenRect.yMin + d.screenRect.height * 0.1f);

        foreach (var t in tiles)
        {
            if (t == null || used.Contains(t)) continue;

            float worldDist = Vector3.Distance(t.TablePoint, worldPos);
            float screenNorm = float.MaxValue;
            Vector3 sp = cam.WorldToScreenPoint(t.TablePoint);
            if (sp.z > 0f) screenNorm = Vector2.Distance(new Vector2(sp.x, sp.y), boxBase) / boxSize;

            bool ok = worldDist < matchDistance || screenNorm < screenMatchFactor;
            if (!ok) continue;

            // skor gabungan: makin kecil makin cocok
            float score = Mathf.Min(worldDist / matchDistance, screenNorm / screenMatchFactor);
            if (score < bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    private void Update()
    {
        Camera cam = Cam;
        UpdateCameraMotion(cam);

        float cycle = detector != null ? detector.CycleTime : 0.5f;
        bool aiRunning = detector != null && detector.Running;
        Rect area = detector != null ? detector.CropRect : new Rect(0, 0, Screen.width, Screen.height);

        for (int i = tiles.Count - 1; i >= 0; i--)
        {
            var t = tiles[i];
            if (t == null) { tiles.RemoveAt(i); continue; }
            float rate = 1f;
            bool inView = false;
            if (aiRunning && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(t.TablePoint);
                inView = sp.z > 0f && area.Contains(new Vector2(sp.x, sp.y));
            }
            if (!aiRunning) rate = 1f;
            else if (!inView) rate = 0.15f;        
            else if (CameraMoving) rate = 0.3f; 

            t.Unseen += Time.deltaTime * rate;

            float timeout = Mathf.Max(t.Confirmed ? lostTimeout : candidateTimeout, cycle * 3f);
            if (t.Unseen > timeout)
            {
                Destroy(t.gameObject);   
                tiles.RemoveAt(i);
            }
        }
    }

    private void UpdateCameraMotion(Camera cam)
    {
        if (cam == null) return;
        Transform ct = cam.transform;
        if (hasCamPose && Time.deltaTime > 0f)
        {
            float ang = Quaternion.Angle(lastCamRot, ct.rotation) / Time.deltaTime;
            float lin = Vector3.Distance(lastCamPos, ct.position) / Time.deltaTime;
            if (ang > movingAngularSpeed || lin > movingLinearSpeed) movingUntil = Time.time + 0.3f;
        }
        CameraMoving = Time.time < movingUntil;
        lastCamPos = ct.position;
        lastCamRot = ct.rotation;
        hasCamPose = true;
    }

    private bool TryGetWorldPoint(Vector2 screenPoint, Camera cam, out Vector3 pos, out Vector3 up)
    {
        if (raycastManager != null &&
            raycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinInfinity))
        {
            foreach (var h in hits)
            {
                if (h.distance > maxRayDistance) break;                    // terlalu jauh (lantai)
                if (Vector3.Dot(h.pose.up, Vector3.up) < 0.7f) continue;   // bukan bidang datar (dinding)
                pos = h.pose.position;
                up = h.pose.up;
                return true;
            }
        }

        Ray ray = cam.ScreenPointToRay(screenPoint);
        pos = ray.GetPoint(fallbackDistance);
        up = Vector3.up;
        return true;
    }

    public void ClearAll()
    {
        foreach (var t in tiles) if (t != null) Destroy(t.gameObject);
        tiles.Clear();
    }
}
