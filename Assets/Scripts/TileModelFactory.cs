using System.Collections.Generic;
using UnityEngine;
using TMPro;

// Warna alas (pad) di bawah model 3D tile.
public enum PadColor { Selected, Keep, Remove, Add, Neutral }

public class TileModelFactory : MonoBehaviour
{
    public static TileModelFactory Instance { get; private set; }

    [Header("Prefab 3D")]
    [Tooltip("Folder prefab 3D di dalam Assets/Resources")]
    public string resourcesFolder = "MahjongTilePrefabs";
    [Tooltip("Koreksi rotasi prefab supaya MUKA tile menghadap ke atas (+Y).\n" +
             "Prefab berdiri dengan muka ke depan: (90, 0, 0). Kalau yang terlihat malah punggung tile: (-90, 0, 0).")]
    public Vector3 prefabEulerOffset = new Vector3(-90f, 0f, 0f);   // prefab Sketchfab: muka menghadap +Z
    [Tooltip("Panjang standar model setelah normalisasi")]
    public float standardLength = 1.35f;

    [Header("Gambar 2D (dipakai fallback jika prefab tidak ada)")]
    public string imageFolder = "MahjongTiles";

    [Header("Material Tile")]
    public Material ghostMaterial;
    public Material bodyMaterial;
    public Material faceMaterial;

    [Header("Material Alas / Pad (BARU)")]
    [Tooltip("Kuning: tile yang sedang dipilih / tile yang di-scan")]
    public Material padSelectedMaterial;
    [Tooltip("Hijau: tile yang dipertahankan (KEEP)")]
    public Material padKeepMaterial;
    [Tooltip("Merah: tile yang harus dibuang (REMOVE)")]
    public Material padRemoveMaterial;
    [Tooltip("Biru: tile pengganti (ADD)")]
    public Material padAddMaterial;
    [Tooltip("Abu-abu: tile belum dipilih / belum dicek")]
    public Material padNeutralMaterial;

    [Header("Ukuran Fallback")]
    public float fallbackWidth = 1f;
    public float fallbackLength = 1.35f;
    public float fallbackThickness = 0.18f;

    private readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, Texture2D> faceCache = new Dictionary<string, Texture2D>();
    private readonly Dictionary<PadColor, Material> runtimePadMaterials = new Dictionary<PadColor, Material>();

    // =====================================================
    // INITIALIZATION
    // =====================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("TileModelFactory duplikat ditemukan.");
            enabled = false;
            return;
        }
        Instance = this;
    }

    private void Start() { ValidatePrefabs(); }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void ValidatePrefabs()
    {
        int found = 0;
        foreach (TileData tile in TileDatabase.All)
        {
            if (GetPrefab(tile) != null) found++;
            else Debug.LogWarning("Prefab tidak ditemukan: Resources/" + resourcesFolder + "/" + tile.id);
        }
        Debug.Log("TileModelFactory: " + found + "/" + TileDatabase.All.Length + " prefab ditemukan.");
    }

    // =====================================================
    // LOAD ASSET
    // =====================================================

    public GameObject GetPrefab(TileData tile)
    {
        if (tile == null) return null;
        GameObject prefab;
        if (prefabCache.TryGetValue(tile.id, out prefab)) return prefab;
        prefab = Resources.Load<GameObject>(resourcesFolder + "/" + tile.id);
        prefabCache[tile.id] = prefab;
        return prefab;
    }

    public Texture2D GetFace(TileData tile)
    {
        if (tile == null) return null;
        Texture2D texture;
        if (faceCache.TryGetValue(tile.id, out texture)) return texture;
        texture = Resources.Load<Texture2D>(imageFolder + "/" + tile.id);
        faceCache[tile.id] = texture;
        return texture;
    }

    // =====================================================
    // CREATE TILE
    // Hasil: root dengan permukaan tile menghadap +Y, alas di y = 0,
    // panjang horizontal terbesar = standardLength.
    // ghost = true -> tile transparan bertanda "?"
    // =====================================================

    public GameObject Create(TileData tile, bool ghost = false)
    {
        if (tile == null)
        {
            Debug.LogError("TileModelFactory: TileData bernilai null.");
            return null;
        }

        GameObject root = new GameObject((ghost ? "Ghost_" : "Tile_") + tile.id);
        GameObject prefab = GetPrefab(tile);

        if (prefab == null)
            return CreateFallback(root, tile, ghost);

        GameObject model = Instantiate(prefab, root.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(prefabEulerOffset);
        model.transform.localScale = Vector3.one;

        // Komponen bawaan Sketchfab/GLB yang tidak diperlukan
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Camera c in model.GetComponentsInChildren<Camera>(true)) Destroy(c);
        foreach (AudioListener a in model.GetComponentsInChildren<AudioListener>(true)) Destroy(a);
        foreach (Light l in model.GetComponentsInChildren<Light>(true)) Destroy(l);

        // Normalisasi ukuran
        Bounds bounds = GetBounds(model);
        float maxHorizontal = Mathf.Max(bounds.size.x, bounds.size.z);
        if (maxHorizontal > 0.0001f)
            model.transform.localScale *= standardLength / maxHorizontal;

        // Pusatkan: tengah X/Z di 0, alas di y = 0
        bounds = GetBounds(model);
        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        bounds = GetBounds(model);

        if (ghost)
        {
            ApplyMaterial(model, ghostMaterial);
            MakeText(root.transform, "?", 8f, Color.white,
                new Vector3(0f, bounds.size.y + 0.01f, 0f), Quaternion.Euler(90f, 0f, 0f));
        }

        return root;
    }

    private static void ApplyMaterial(GameObject model, Material mat)
    {
        if (mat == null) return;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    // =====================================================
    // PAD (alas warna di bawah tile)
    // =====================================================

    // Membuat alas pipih di bawah model tile. parent = root tile (permukaan +Y).
    public GameObject CreatePad(Transform parent, PadColor color)
    {
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "Pad";
        Destroy(pad.GetComponent<Collider>());
        pad.transform.SetParent(parent, false);
        pad.transform.localPosition = new Vector3(0f, -0.03f, 0f);
        pad.transform.localRotation = Quaternion.identity;
        pad.transform.localScale = new Vector3(1.75f, 0.04f, 1.75f);
        SetPadColor(pad, color);
        return pad;
    }

    public void SetPadColor(GameObject pad, PadColor color)
    {
        if (pad == null) return;
        Renderer r = pad.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = GetPadMaterial(color);
    }

    public Material GetPadMaterial(PadColor color)
    {
        Material m = null;
        switch (color)
        {
            case PadColor.Selected: m = padSelectedMaterial; break;
            case PadColor.Keep: m = padKeepMaterial; break;
            case PadColor.Remove: m = padRemoveMaterial; break;
            case PadColor.Add: m = padAddMaterial; break;
            default: m = padNeutralMaterial; break;
        }
        if (m != null) return m;

        // Cadangan jika material belum diisi di Inspector
        if (runtimePadMaterials.TryGetValue(color, out m)) return m;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        m = new Material(shader);
        Color c = PadTint(color);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        runtimePadMaterials[color] = m;
        return m;
    }

    public static Color PadTint(PadColor color)
    {
        switch (color)
        {
            case PadColor.Selected: return new Color32(0xF5, 0xC9, 0x6B, 0xFF); // kuning emas
            case PadColor.Keep: return new Color32(0x3D, 0xBE, 0x7A, 0xFF);     // hijau
            case PadColor.Remove: return new Color32(0xE5, 0x4B, 0x4B, 0xFF);   // merah
            case PadColor.Add: return new Color32(0x4A, 0x9B, 0xE8, 0xFF);      // biru
            default: return new Color32(0x8A, 0x9A, 0x9A, 0xFF);                // abu-abu
        }
    }

    // =====================================================
    // LABEL 3D
    // Membuat holder berskala 'scale' berisi teks. Jika billboard = true,
    // label selalu menghadap kamera.
    // =====================================================

    public static TextMeshPro MakeLabel(Transform parent, string text, Vector3 localPos,
                                        float scale, Color color, float fontSize, bool billboard)
    {
        GameObject holder = new GameObject("Label_" + text);
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = localPos;
        holder.transform.localRotation = Quaternion.identity;
        holder.transform.localScale = Vector3.one * scale;
        if (billboard) holder.AddComponent<Billboard>();

        TextMeshPro tmp = MakeText(holder.transform, text, fontSize, color, Vector3.zero, Quaternion.identity);
        tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    public static TextMeshPro MakeText(Transform parent, string text, float fontSize, Color color,
                                       Vector3 localPosition, Quaternion localRotation)
    {
        GameObject labelObject = new GameObject("Text");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = localPosition;
        labelObject.transform.localRotation = localRotation;

        TextMeshPro tmp = labelObject.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(8f, 3f);
        return tmp;
    }

    // =====================================================
    // FALLBACK: kubus + gambar 2D jika prefab tidak ada
    // =====================================================

    private GameObject CreateFallback(GameObject root, TileData tile, bool ghost)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "FallbackBody";
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(fallbackWidth, fallbackThickness, fallbackLength);
        body.transform.localPosition = new Vector3(0f, fallbackThickness / 2f, 0f);

        Material bodyMat = ghost ? ghostMaterial : bodyMaterial;
        if (bodyMat != null) body.GetComponent<Renderer>().sharedMaterial = bodyMat;

        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        face.name = "FallbackFace";
        Destroy(face.GetComponent<Collider>());
        face.transform.SetParent(root.transform, false);
        face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        face.transform.localPosition = new Vector3(0f, fallbackThickness + 0.003f, 0f);
        face.transform.localScale = new Vector3(fallbackWidth * 0.86f, fallbackLength * 0.9f, 1f);

        Material source = ghost ? ghostMaterial : faceMaterial;
        Material faceMat = null;
        if (source != null) faceMat = new Material(source);
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader != null) faceMat = new Material(shader);
        }

        Texture2D texture = ghost ? null : GetFace(tile);
        if (faceMat != null)
        {
            if (texture != null) faceMat.mainTexture = texture;
            face.GetComponent<Renderer>().material = faceMat;
        }

        if (ghost || texture == null)
        {
            MakeText(root.transform, ghost ? "?" : tile.name.Replace(" ", "\n"),
                ghost ? 8f : 3.5f, ghost ? Color.white : Color.black,
                new Vector3(0f, fallbackThickness + 0.01f, 0f), Quaternion.Euler(90f, 0f, 0f));
        }

        return root;
    }

    // =====================================================
    // UTIL
    // =====================================================

    public static Bounds GetBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
