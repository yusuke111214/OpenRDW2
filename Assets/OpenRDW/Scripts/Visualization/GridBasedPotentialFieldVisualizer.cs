// グリッドベースAPFのポテンシャル場可視化
// APFGridのデータを直接使用して、効率的にヒートマップと矢印フィールドを表示します。
//
// 主な機能：
// - ポテンシャルフィールドのヒートマップ表示
// - Distance Fieldのヒートマップ表示（新機能）
// - 勾配ベクトルの矢印フィールド表示
// - 開放空間の強調表示

using System.Collections.Generic;
using UnityEngine;

public class GridBasedPotentialFieldVisualizer : MonoBehaviour
{
    [Header("Visualization Control")]
    [Tooltip("ポテンシャル場の可視化を有効にする")]
    public bool enableVisualization = false;

    [Tooltip("リアルタイムで更新するか（重いので通常はOFF）")]
    public bool realtimeUpdate = false;

    [Header("Heatmap Mode")]
    [Tooltip("ヒートマップの表示モード")]
    public HeatmapMode heatmapMode = HeatmapMode.Potential;

    public enum HeatmapMode
    {
        Potential,      // ポテンシャル値（U = F_max / (1 + k*d)）
        Distance,       // 距離値（Distance Field）
        OpenSpace       // 開放空間の強調表示
    }

    [Header("Heatmap Parameters")]
    [Tooltip("ヒートマップを表示")]
    public bool showHeatmap = true;

    [Tooltip("色マッピングのガンマ補正値（低いほど床の色の違いが強調される）")]
    [Range(0.1f, 2.0f)]
    public float colorGamma = 0.5f;

    [Tooltip("ヒートマップの高さオフセット")]
    [Range(0f, 0.5f)]
    public float heatmapHeight = 0.01f;

    [Header("Arrow Field Parameters")]
    [Tooltip("矢印フィールドを表示")]
    public bool showArrowField = true;

    [Tooltip("矢印の間隔（グリッドセル数）")]
    [Range(1, 10)]
    public int arrowSpacingCells = 2;

    [Tooltip("矢印のスケール")]
    [Range(0.05f, 1.0f)]
    public float arrowScale = 0.15f;

    [Tooltip("矢印の高さオフセット")]
    [Range(0f, 0.5f)]
    public float arrowHeight = 0.02f;

    [Header("Open Space Visualization")]
    [Tooltip("開放空間とみなす最小距離（メートル）")]
    [Range(0.5f, 3.0f)]
    public float openSpaceMinDistance = 1.0f;

    [Tooltip("開放空間の強調色")]
    public Color openSpaceColor = new Color(0, 1, 0, 0.7f);

    // Internal references
    private GlobalConfiguration globalConfiguration;
    private VisualizationManager visualizationManager;
    private RedirectionManager redirectionManager;
    private APFGrid apfGrid;

    // Visualization objects
    private GameObject heatmapObject;
    private Mesh heatmapMesh;
    private List<GameObject> arrowObjects = new List<GameObject>();

    private bool wasEnabled = false;
    private float lastUpdateTime = 0f;
    private const float UPDATE_INTERVAL = 1.0f;  // 1秒ごとに更新

    void Awake()
    {
        globalConfiguration = GetComponentInParent<GlobalConfiguration>();
        visualizationManager = GetComponent<VisualizationManager>();
        redirectionManager = GetComponent<RedirectionManager>();
    }

    void Start()
    {
        // GlobalConfigurationから初期設定を読み込む
        if (globalConfiguration != null)
        {
            enableVisualization = globalConfiguration.enableGridPotentialVisualization;
            showHeatmap = globalConfiguration.showGridHeatmap;
            showArrowField = globalConfiguration.showGridArrows;
            colorGamma = globalConfiguration.gridColorGamma;
            arrowSpacingCells = globalConfiguration.gridArrowSpacing;
            realtimeUpdate = globalConfiguration.realtimeGridUpdate;
        }
    }

    /// <summary>
    /// 外部からAPFGridを設定
    /// </summary>
    public void SetAPFGrid(APFGrid grid)
    {
        apfGrid = grid;

        if (enableVisualization && apfGrid != null)
        {
            RegenerateVisualization();
        }
    }

    void Update()
    {
        // 可視化の有効/無効が変更された場合
        if (enableVisualization != wasEnabled)
        {
            if (enableVisualization && apfGrid != null)
            {
                RegenerateVisualization();
            }
            else
            {
                ClearVisualization();
            }
            wasEnabled = enableVisualization;
        }

        // リアルタイム更新（負荷が高いので通常はOFF）
        if (enableVisualization && realtimeUpdate && apfGrid != null)
        {
            if (Time.time - lastUpdateTime > UPDATE_INTERVAL)
            {
                RegenerateVisualization();
                lastUpdateTime = Time.time;
            }
        }
    }

    /// <summary>
    /// 可視化を再生成
    /// </summary>
    public void RegenerateVisualization()
    {
        if (globalConfiguration != null && globalConfiguration.runInBackstage)
            return;

        if (apfGrid == null)
        {
            Debug.LogWarning("APFGrid is not set. Cannot generate visualization.");
            return;
        }

        ClearVisualization();

        // ヒートマップ生成
        if (showHeatmap)
        {
            GenerateHeatmap();
        }

        // 矢印フィールド生成
        if (showArrowField)
        {
            GenerateArrowField();
        }

        Debug.Log($"GridBasedPotentialFieldVisualizer: Regenerated visualization (mode={heatmapMode})");
    }

    /// <summary>
    /// ヒートマップメッシュを生成
    /// </summary>
    void GenerateHeatmap()
    {
        if (apfGrid == null)
            return;

        heatmapObject = new GameObject("GridBasedHeatmap");
        heatmapObject.transform.SetParent(transform);

        // レイヤー設定
        int realLayer = LayerMask.NameToLayer("Real");
        if (realLayer != -1)
        {
            heatmapObject.layer = realLayer;
        }

        // メッシュデータを構築
        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();

        int gridWidth = apfGrid.Width;
        int gridHeight = apfGrid.Height;
        float cellSize = apfGrid.CellSize;

        // ポテンシャル/距離の最小値と最大値を計算
        float minValue = float.MaxValue;
        float maxValue = float.MinValue;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3 cellPos = apfGrid.GridToWorld(x, z);
                float value = GetValueForMode(cellPos);

                if (value > 0)
                {
                    minValue = Mathf.Min(minValue, value);
                    maxValue = Mathf.Max(maxValue, value);
                }
            }
        }

        Debug.Log($"Heatmap range: min={minValue:F2}, max={maxValue:F2}, mode={heatmapMode}");

        // メッシュ生成
        for (int x = 0; x < gridWidth - 1; x++)
        {
            for (int z = 0; z < gridHeight - 1; z++)
            {
                // 4つの頂点
                Vector3 p00 = apfGrid.GridToWorld(x, z);
                Vector3 p10 = apfGrid.GridToWorld(x + 1, z);
                Vector3 p01 = apfGrid.GridToWorld(x, z + 1);
                Vector3 p11 = apfGrid.GridToWorld(x + 1, z + 1);

                // 値を取得
                float v00 = GetValueForMode(p00);
                float v10 = GetValueForMode(p10);
                float v01 = GetValueForMode(p01);
                float v11 = GetValueForMode(p11);

                // すべての頂点が値0ならスキップ
                if (v00 == 0 && v10 == 0 && v01 == 0 && v11 == 0)
                    continue;

                // 頂点インデックス
                int idx00 = vertices.Count;
                int idx10 = idx00 + 1;
                int idx01 = idx00 + 2;
                int idx11 = idx00 + 3;

                // 頂点追加（高さオフセット付き）
                // TrackingSpaceのローカル座標をワールド座標に変換
                Vector3 worldPos00 = TransformToWorldSpace(p00 + Vector3.up * heatmapHeight);
                Vector3 worldPos10 = TransformToWorldSpace(p10 + Vector3.up * heatmapHeight);
                Vector3 worldPos01 = TransformToWorldSpace(p01 + Vector3.up * heatmapHeight);
                Vector3 worldPos11 = TransformToWorldSpace(p11 + Vector3.up * heatmapHeight);

                vertices.Add(worldPos00);
                vertices.Add(worldPos10);
                vertices.Add(worldPos01);
                vertices.Add(worldPos11);

                // 色を設定
                colors.Add(GetColorForValue(v00, minValue, maxValue, p00));
                colors.Add(GetColorForValue(v10, minValue, maxValue, p10));
                colors.Add(GetColorForValue(v01, minValue, maxValue, p01));
                colors.Add(GetColorForValue(v11, minValue, maxValue, p11));

                // 三角形追加（2つの三角形でクワッド）
                triangles.Add(idx00);
                triangles.Add(idx01);
                triangles.Add(idx10);

                triangles.Add(idx10);
                triangles.Add(idx01);
                triangles.Add(idx11);
            }
        }

        // メッシュ作成
        heatmapMesh = new Mesh();
        heatmapMesh.name = "GridBasedHeatmapMesh";
        heatmapMesh.vertices = vertices.ToArray();
        heatmapMesh.colors = colors.ToArray();
        heatmapMesh.triangles = triangles.ToArray();
        heatmapMesh.RecalculateNormals();

        Debug.Log($"Heatmap mesh: {vertices.Count} vertices, {triangles.Count / 3} triangles");

        // メッシュコンポーネント追加
        MeshFilter meshFilter = heatmapObject.AddComponent<MeshFilter>();
        meshFilter.mesh = heatmapMesh;

        MeshRenderer meshRenderer = heatmapObject.AddComponent<MeshRenderer>();

        // 頂点カラーをサポートする透明マテリアル
        Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null)
            shader = Shader.Find("Mobile/Particles/Alpha Blended");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");

        Material mat = new Material(shader);
        mat.SetColor("_TintColor", Color.white);

        meshRenderer.material = mat;
        meshRenderer.enabled = visualizationManager != null ? visualizationManager.ifVisible : true;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    /// <summary>
    /// モードに応じて値を取得
    /// </summary>
    float GetValueForMode(Vector3 worldPos)
    {
        switch (heatmapMode)
        {
            case HeatmapMode.Potential:
                return apfGrid.GetPotential(worldPos);

            case HeatmapMode.Distance:
                return apfGrid.GetDistance(worldPos);

            case HeatmapMode.OpenSpace:
                // 開放空間モード：距離値を返し、後で色分け
                return apfGrid.GetDistance(worldPos);

            default:
                return 0;
        }
    }

    /// <summary>
    /// 値を色に変換
    /// </summary>
    Color GetColorForValue(float value, float minValue, float maxValue, Vector3 worldPos)
    {
        if (value == 0)
        {
            return new Color(0, 0, 0, 0); // 透明
        }

        // 開放空間モード
        if (heatmapMode == HeatmapMode.OpenSpace)
        {
            float distance = apfGrid.GetDistance(worldPos);
            if (distance >= openSpaceMinDistance)
            {
                // 開放空間：強調色
                return openSpaceColor;
            }
            else
            {
                // 非開放空間：距離に応じた色（青→赤）
                float tOpen = Mathf.Clamp01(distance / openSpaceMinDistance);
                tOpen = Mathf.Pow(tOpen, colorGamma);
                Color dangerColor = new Color(1, 0, 0, 0.7f);  // 赤（危険）
                Color safeColor = new Color(0, 0, 1, 0.7f);    // 青（やや安全）
                return Color.Lerp(dangerColor, safeColor, tOpen);
            }
        }

        // 通常モード（ポテンシャルまたは距離）
        float t;
        if (maxValue > minValue)
        {
            t = Mathf.Clamp01((value - minValue) / (maxValue - minValue));
        }
        else
        {
            t = 0.5f;
        }

        // Distance Fieldモードでは逆転（距離が大きい = 安全 = 青）
        if (heatmapMode == HeatmapMode.Distance)
        {
            t = 1.0f - t;
        }

        // ガンマ補正
        t = Mathf.Pow(t, colorGamma);

        // 5段階グラデーション: 青 → シアン → 緑 → 黄 → オレンジ → 赤
        Color color1 = new Color(0.0f, 0.0f, 1.0f, 0.85f);   // 青（最も安全/低ポテンシャル）
        Color color2 = new Color(0.0f, 1.0f, 1.0f, 0.85f);   // シアン
        Color color3 = new Color(0.0f, 1.0f, 0.0f, 0.85f);   // 緑
        Color color4 = new Color(1.0f, 1.0f, 0.0f, 0.85f);   // 黄色
        Color color5 = new Color(1.0f, 0.5f, 0.0f, 0.85f);   // オレンジ
        Color color6 = new Color(1.0f, 0.0f, 0.0f, 0.85f);   // 赤（最も危険/高ポテンシャル）

        if (t < 0.2f)
            return Color.Lerp(color1, color2, t * 5f);
        else if (t < 0.4f)
            return Color.Lerp(color2, color3, (t - 0.2f) * 5f);
        else if (t < 0.6f)
            return Color.Lerp(color3, color4, (t - 0.4f) * 5f);
        else if (t < 0.8f)
            return Color.Lerp(color4, color5, (t - 0.6f) * 5f);
        else
            return Color.Lerp(color5, color6, (t - 0.8f) * 5f);
    }

    /// <summary>
    /// 矢印フィールドを生成
    /// </summary>
    void GenerateArrowField()
    {
        if (globalConfiguration == null || globalConfiguration.negArrow == null)
        {
            Debug.LogWarning("negArrow prefab is not set in GlobalConfiguration");
            return;
        }

        int gridWidth = apfGrid.Width;
        int gridHeight = apfGrid.Height;

        for (int x = 0; x < gridWidth; x += arrowSpacingCells)
        {
            for (int z = 0; z < gridHeight; z += arrowSpacingCells)
            {
                Vector3 cellPos = apfGrid.GridToWorld(x, z);

                // 勾配（力の方向）を取得
                Vector3 gradient = apfGrid.GetGradient(cellPos);

                // 勾配が非常に小さい場合はスキップ
                if (gradient.magnitude < 0.01f)
                    continue;

                // 矢印オブジェクトを生成
                GameObject arrow = Instantiate(globalConfiguration.negArrow);
                arrow.name = $"GridArrow_{x}_{z}";
                arrow.transform.SetParent(transform);

                // レイヤー設定
                int realLayer = LayerMask.NameToLayer("Real");
                if (realLayer != -1)
                {
                    arrow.layer = realLayer;
                }

                // 位置と向きを設定
                Vector3 pos = cellPos + Vector3.up * arrowHeight;
                arrow.transform.position = pos;
                arrow.transform.forward = transform.rotation * gradient.normalized;

                // スケール調整
                float scale = arrowScale * Mathf.Clamp01(gradient.magnitude / 10f);
                arrow.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);

                // 可視性設定
                if (visualizationManager != null)
                {
                    foreach (var mr in arrow.GetComponentsInChildren<MeshRenderer>())
                    {
                        mr.enabled = visualizationManager.ifVisible;
                    }
                }

                arrowObjects.Add(arrow);
            }
        }

        Debug.Log($"Generated {arrowObjects.Count} arrows");
    }

    /// <summary>
    /// 可視化をクリア
    /// </summary>
    public void ClearVisualization()
    {
        if (heatmapObject != null)
        {
            Destroy(heatmapObject);
            heatmapObject = null;
        }

        foreach (var arrow in arrowObjects)
        {
            if (arrow != null)
                Destroy(arrow);
        }
        arrowObjects.Clear();
    }

    void OnDestroy()
    {
        ClearVisualization();
    }
}
