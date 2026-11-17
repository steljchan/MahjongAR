using UnityEngine;
using UnityEngine.EventSystems;

// Interaksi sentuh dengan tile 3D di kamera:
//   - KETUK tile         : memilih tile (jika ada beberapa tile di kamera)
//   - GESER 1 jari       : memutar tile terpilih (kiri-kanan & atas-bawah)
//   - CUBIT 2 jari       : zoom tile terpilih
//   - KETUK 2x           : reset tampilan
// Di Editor: klik = pilih, drag mouse = putar, scroll = zoom.
public class ModelInteraction : MonoBehaviour
{
    [Tooltip("Jika ketukan tidak mengenai tile, tile terdekat dalam radius ini (bagian dari lebar layar) yang dipilih")]
    [Range(0.03f, 0.3f)] public float tapRadius = 0.12f;

    public static ModelInteraction Instance { get; private set; }

    [Tooltip("Kamera AR untuk raycast. Kosong = Camera.main")]
    public Camera arCamera;

    public float minZoom = 0.5f;
    public float maxZoom = 4f;
    public float rotateSpeed = 0.4f;
    [Tooltip("Batas gerak jari (pixel) agar masih dihitung ketukan")]
    public float tapMaxMove = 25f;
    public float tapMaxTime = 0.35f;

    private Vector2 pressPos;
    private float pressTime;
    private bool pressOverUI;

    void Awake() { Instance = this; }

    DetectedTile Target
    {
        get { return GameManager.Instance != null ? GameManager.Instance.Selected : null; }
    }

    bool CanRotateZoom
    {
        get { return GameManager.Instance != null && GameManager.Instance.AllowModelControl; }
    }

    bool CanTapSelect
    {
        get { return GameManager.Instance != null && GameManager.Instance.AllowTapSelect; }
    }

    // ---------- dipanggil tombol UI ----------
    public void Step(float factor)
    {
        var h = Target;
        if (h == null || !CanRotateZoom) return;
        h.Zoom = Mathf.Clamp(h.Zoom * factor, minZoom, maxZoom);
        h.ApplyView();
    }

    public void ResetView()
    {
        var h = Target;
        if (h != null) h.ResetView();
    }

    bool OverUI(int pointerId)
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
    }

    void Update()
    {
        if (Input.touchCount > 0) HandleTouch();
#if UNITY_EDITOR || UNITY_STANDALONE
        else HandleMouse();
#endif
    }

    // =====================================================
    // SENTUH (HP)
    // =====================================================
    void HandleTouch()
    {
        var h = Target;

        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);

            if (t.phase == TouchPhase.Began)
            {
                pressPos = t.position;
                pressTime = Time.unscaledTime;
                pressOverUI = OverUI(t.fingerId);

                if (!pressOverUI && t.tapCount == 2 && h != null) { h.ResetView(); return; }
            }
            if (pressOverUI) return;

            if (t.phase == TouchPhase.Moved && h != null && h.IsTracked && CanRotateZoom)
            {
                h.RotY -= t.deltaPosition.x * rotateSpeed;
                h.RotX = Mathf.Clamp(h.RotX + t.deltaPosition.y * rotateSpeed, -60f, 60f);
                h.ApplyView();
            }

            if (t.phase == TouchPhase.Ended && IsTap(t.position))
                TrySelectAt(t.position);
        }
        else if (Input.touchCount == 2 && h != null && h.IsTracked && CanRotateZoom)
        {
            Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
            if (OverUI(a.fingerId) || OverUI(b.fingerId)) return;

            float prev = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
            float now = (a.position - b.position).magnitude;
            if (prev > 1f)
            {
                h.Zoom = Mathf.Clamp(h.Zoom * (now / prev), minZoom, maxZoom);
                h.ApplyView();
            }
        }
    }

    // =====================================================
    // MOUSE (Editor)
    // =====================================================
    void HandleMouse()
    {
        var h = Target;

        if (Input.GetMouseButtonDown(0))
        {
            pressPos = Input.mousePosition;
            pressTime = Time.unscaledTime;
            pressOverUI = OverUI(-1);
        }

        if (OverUI(-1) && !Input.GetMouseButton(0)) return;

        if (h != null && h.IsTracked && CanRotateZoom)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                h.Zoom = Mathf.Clamp(h.Zoom * (1f + scroll * 0.1f), minZoom, maxZoom);
                h.ApplyView();
            }

            if (Input.GetMouseButton(0) && !pressOverUI)
            {
                h.RotY -= Input.GetAxis("Mouse X") * rotateSpeed * 10f;
                h.RotX = Mathf.Clamp(h.RotX + Input.GetAxis("Mouse Y") * rotateSpeed * 10f, -60f, 60f);
                h.ApplyView();
            }
        }

        if (Input.GetMouseButtonUp(0) && !pressOverUI && IsTap(Input.mousePosition))
            TrySelectAt(Input.mousePosition);
    }

    // =====================================================
    // PILIH TILE DENGAN KETUKAN (raycast ke BoxCollider di Pivot)
    // =====================================================
    bool IsTap(Vector2 releasePos)
    {
        return (releasePos - pressPos).magnitude <= tapMaxMove &&
               Time.unscaledTime - pressTime <= tapMaxTime;
    }

    void TrySelectAt(Vector2 screenPos)
    {
        if (!CanTapSelect) return;

        Camera cam = arCamera != null ? arCamera : Camera.main;
        if (cam == null) return;

        // RaycastAll: bidang meja AR juga punya collider, jadi cari tile terdekat saja
        RaycastHit[] hits = Physics.RaycastAll(cam.ScreenPointToRay(screenPos), 10f);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var hit in hits)
        {
            var handler = hit.collider.GetComponentInParent<DetectedTile>();
            if (handler != null && handler.IsTracked)
            {
                GameManager.Instance.SelectTile(handler);
                return;
            }
        }

        // Ketukan meleset: pilih tile terdekat dari titik ketukan di layar.
        // Area sentuh jadi jauh lebih besar dari tile aslinya (temuan pengujian: TAP terlalu kecil).
        DetectedTile nearest = null;
        float best = Screen.width * tapRadius;
        foreach (var t in Object.FindObjectsByType<DetectedTile>(FindObjectsSortMode.None))
        {
            if (t == null || !t.IsTracked || t.View == TileView.Hidden) continue;
            Vector3 sp = cam.WorldToScreenPoint(t.TablePoint);
            if (sp.z <= 0f) continue;
            float d = Vector2.Distance(new Vector2(sp.x, sp.y), screenPos);
            if (d < best) { best = d; nearest = t; }
        }
        if (nearest != null) GameManager.Instance.SelectTile(nearest);
    }
}