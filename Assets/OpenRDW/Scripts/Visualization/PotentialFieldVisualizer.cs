// ポテンシャル場の可視化
// ThomasAPFのポテンシャル場をグリッドベースで計算し、
// ヒートマップと矢印フィールドで表示します。

using System.Collections.Generic;
using UnityEngine;

public class PotentialFieldVisualizer : MonoBehaviour
{
    [Header("Visualization Control")]
    [Tooltip("ポテンシャル場の可視化を有効にする")]
    public bool enableVisualization = false;

    [Tooltip("前回の有効状態をリアルタイムで更新するか")]
    public bool realtimeUpdate = false;

    [Header("Grid Parameters")]
    [Tooltip("グリッドセルのサイズ（メートル）")]
    [Range(0.1f, 1.0f)]
    public float gridCellSize = 0.2f;

    [Header("Heatmap Parameters")]
    [Tooltip("ヒートマップを表示")]
    public bool showHeatmap = true;

    [Tooltip("ヒートマップの最大ポテンシャル値（色マッピング用）")]
    [Range(1f, 50f)]
    public float maxPotentialForColor = 20f;

    [Tooltip("ヒートマップの高さオフセット")]
    [Range(0f, 0.1f)]
    public float heatmapHeight = 0.002f;

    [Header("Arrow Field Parameters")]
    [Tooltip("矢印フィールドを表示")]
    public bool showArrowField = true;

    [Tooltip("矢印の間隔（メートル）")]
    [Range(0.2f, 2.0f)]
    public float arrowSpacing = 0.5f;

    [Tooltip("矢印のスケール")]
    [Range(0.05f, 1.0f)]
    public float arrowScale = 0.2f;

    [Tooltip("矢印の高さオフセット")]
    [Range(0f, 0.5f)]
    public float arrowHeight = 0.05f;

    // Internal references
    private GlobalConfiguration globalConfiguration;
    private RedirectionManager redirectionManager;
    private MovementManager movementManager;
    private VisualizationManager visualizationManager;

    // Grid data
    private int gridWidth;
    private int gridHeight;
    private Vector2 gridMin;
    private Vector2 gridMax;
    private float[,] potentialGrid;
    private Vector2[,] gradientGrid;

    // Visualization objects
    private GameObject heatmapObject;
    private Mesh heatmapMesh;
    private List<GameObject> arrowObjects;

    private bool wasEnabled = false;

    void Awake()
    {
        globalConfiguration = GetComponentInParent<GlobalConfiguration>();
        redirectionManager = GetComponent<RedirectionManager>();
        movementManager = GetComponent<MovementManager>();
        visualizationManager = GetComponent<VisualizationManager>();
        arrowObjects = new List<GameObject>();
    }

    void Start()
    {
        // GlobalConfigurationから初期設定を読み込む
        if (globalConfiguration != null)
        {
            enableVisualization = globalConfiguration.enablePotentialFieldVisualization;
            gridCellSize = globalConfiguration.potentialFieldGridSize;
            showHeatmap = globalConfiguration.showPotentialHeatmap;
            showArrowField = globalConfiguration.showPotentialArrows;
            arrowSpacing = globalConfiguration.potentialArrowSpacing;
            realtimeUpdate = globalConfiguration.realtimePotentialUpdate;
        }
    }

    void Update()
    {
        // 可視化の有効/無効が変更された場合
        if (enableVisualization != wasEnabled)
        {
            if (enableVisualization)
            {
                RegenerateVisualization();
            }
            else
            {
                ClearVisualization();
            }
            wasEnabled = enableVisualization;
        }

        // リアルタイム更新が有効な場合
        if (enableVisualization && realtimeUpdate)
        {
            RegenerateVisualization();
        }
    }

    /// <summary>
    /// 可視化を再生成
    /// </summary>
    public void RegenerateVisualization()
    {
        if (globalConfiguration.runInBackstage)
            return;

        ClearVisualization();

        int spaceIndex = movementManager.physicalSpaceIndex;
        if (spaceIndex < 0 || spaceIndex >= globalConfiguration.physicalSpaces.Count)
            return;

        SingleSpace space = globalConfiguration.physicalSpaces[spaceIndex];

        // グリッド計算
        CalculateGrid(space);

        // ヒートマップ生成
        if (showHeatmap)
        {
            GenerateHeatmap();
        }

        // 矢印フィールド生成
        if (showArrowField)
        {
            GenerateArrowField(space);
        }
    }

    /// <summary>
    /// グリッドのポテンシャル値と勾配を計算
    /// </summary>
    void CalculateGrid(SingleSpace space)
    {
        // グリッドの範囲を計算（トラッキングスペースのバウンディングボックス）
        if (space.trackingSpace == null || space.trackingSpace.Count == 0)
            return;

        gridMin = space.trackingSpace[0];
        gridMax = space.trackingSpace[0];

        foreach (var point in space.trackingSpace)
        {
            gridMin.x = Mathf.Min(gridMin.x, point.x);
            gridMin.y = Mathf.Min(gridMin.y, point.y);
            gridMax.x = Mathf.Max(gridMax.x, point.x);
            gridMax.y = Mathf.Max(gridMax.y, point.y);
        }

        // グリッドサイズを計算
        gridWidth = Mathf.CeilToInt((gridMax.x - gridMin.x) / gridCellSize) + 1;
        gridHeight = Mathf.CeilToInt((gridMax.y - gridMin.y) / gridCellSize) + 1;

        potentialGrid = new float[gridWidth, gridHeight];
        gradientGrid = new Vector2[gridWidth, gridHeight];

        // 障害物リストを収集（ThomasAPFと同じロジック）
        var obstaclePoints = new List<Vector2>();

        // トラッキングスペースの境界
        for (int i = 0; i < space.trackingSpace.Count; i++)
        {
            var p = space.trackingSpace[i];
            var q = space.trackingSpace[(i + 1) % space.trackingSpace.Count];

            // エッジをセグメント分割
            int segments = Mathf.Max(2, Mathf.CeilToInt((q - p).magnitude / gridCellSize));
            for (int j = 0; j < segments; j++)
            {
                float t = j / (float)segments;
                obstaclePoints.Add(Vector2.Lerp(p, q, t));
            }
        }

        // 障害物
        if (space.obstaclePolygons != null)
        {
            foreach (var obstacle in space.obstaclePolygons)
            {
                foreach (var point in obstacle)
                {
                    obstaclePoints.Add(point);
                }
            }
        }

        // 他のアバター
        if (globalConfiguration.redirectedAvatars != null)
        {
            foreach (var user in globalConfiguration.redirectedAvatars)
            {
                var userMovement = user.GetComponent<MovementManager>();
                if (userMovement.physicalSpaceIndex != movementManager.physicalSpaceIndex)
                    continue;

                if (userMovement.avatarId == movementManager.avatarId)
                    continue;

                var userPos = user.GetComponent<RedirectionManager>().currPosReal;
                obstaclePoints.Add(Utilities.FlattenedPos2D(userPos));
            }
        }

        // 各グリッドセルでポテンシャルと勾配を計算
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2 cellPos = gridMin + new Vector2(x * gridCellSize, y * gridCellSize);

                // トラッキングスペース内かチェック
                if (!TrackingSpaceGenerator.IsPointInsidePolygon(space.trackingSpace, cellPos))
                {
                    potentialGrid[x, y] = 0;
                    gradientGrid[x, y] = Vector2.zero;
                    continue;
                }

                // ThomasAPFのポテンシャル計算
                // U(p) = Σ 1/r_i
                // ∇U(p) = Σ -(p - o_i) / r_i²
                float potential = 0;
                Vector2 gradient = Vector2.zero;

                foreach (var obstaclePos in obstaclePoints)
                {
                    float distance = (cellPos - obstaclePos).magnitude;

                    // 数値安定性のため最小距離を設定
                    if (distance < 0.01f)
                        distance = 0.01f;

                    // ポテンシャル: U += 1/r
                    potential += 1f / distance;

                    // 勾配: ∇U += -(p - o) / r²
                    Vector2 gDelta = -1f / distance * (cellPos - obstaclePos).normalized;
                    gradient += gDelta;
                }

                potentialGrid[x, y] = potential;
                gradientGrid[x, y] = -gradient; // 負の勾配（誘導方向）
            }
        }
    }

    /// <summary>
    /// ヒートマップメッシュを生成
    /// </summary>
    void GenerateHeatmap()
    {
        if (gridWidth == 0 || gridHeight == 0)
            return;

        heatmapObject = new GameObject("PotentialFieldHeatmap");
        heatmapObject.transform.SetParent(transform);

        // レイヤー設定（安全にチェック）
        int realLayer = LayerMask.NameToLayer("Real");
        if (realLayer != -1)
        {
            heatmapObject.layer = realLayer;
        }
        // レイヤーが存在しない場合はデフォルトレイヤー（0）を使用

        // メッシュデータを構築
        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();

        int spaceIndex = movementManager.physicalSpaceIndex;
        SingleSpace space = globalConfiguration.physicalSpaces[spaceIndex];

        for (int x = 0; x < gridWidth - 1; x++)
        {
            for (int y = 0; y < gridHeight - 1; y++)
            {
                // 4つの頂点
                Vector2 p00 = gridMin + new Vector2(x * gridCellSize, y * gridCellSize);
                Vector2 p10 = gridMin + new Vector2((x + 1) * gridCellSize, y * gridCellSize);
                Vector2 p01 = gridMin + new Vector2(x * gridCellSize, (y + 1) * gridCellSize);
                Vector2 p11 = gridMin + new Vector2((x + 1) * gridCellSize, (y + 1) * gridCellSize);

                // トラッキングスペース内かチェック
                bool inside00 = TrackingSpaceGenerator.IsPointInsidePolygon(space.trackingSpace, p00);
                bool inside10 = TrackingSpaceGenerator.IsPointInsidePolygon(space.trackingSpace, p10);
                bool inside01 = TrackingSpaceGenerator.IsPointInsidePolygon(space.trackingSpace, p01);
                bool inside11 = TrackingSpaceGenerator.IsPointInsidePolygon(space.trackingSpace, p11);

                // すべての頂点が外側なら スキップ
                if (!inside00 && !inside10 && !inside01 && !inside11)
                    continue;

                // 頂点インデックス
                int idx00 = vertices.Count;
                int idx10 = idx00 + 1;
                int idx01 = idx00 + 2;
                int idx11 = idx00 + 3;

                // 頂点追加
                vertices.Add(Utilities.UnFlatten(p00) + Vector3.up * heatmapHeight);
                vertices.Add(Utilities.UnFlatten(p10) + Vector3.up * heatmapHeight);
                vertices.Add(Utilities.UnFlatten(p01) + Vector3.up * heatmapHeight);
                vertices.Add(Utilities.UnFlatten(p11) + Vector3.up * heatmapHeight);

                // 色を設定（ポテンシャル値で色マッピング）
                colors.Add(GetPotentialColor(potentialGrid[x, y], inside00));
                colors.Add(GetPotentialColor(potentialGrid[x + 1, y], inside10));
                colors.Add(GetPotentialColor(potentialGrid[x, y + 1], inside01));
                colors.Add(GetPotentialColor(potentialGrid[x + 1, y + 1], inside11));

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
        heatmapMesh.name = "PotentialFieldMesh";
        heatmapMesh.vertices = vertices.ToArray();
        heatmapMesh.colors = colors.ToArray();
        heatmapMesh.triangles = triangles.ToArray();
        heatmapMesh.RecalculateNormals();

        Debug.Log($"Heatmap mesh created: {vertices.Count} vertices, {triangles.Count / 3} triangles");

        // 最初の数頂点の色をデバッグ出力
        if (colors.Count > 0)
        {
            Debug.Log($"First vertex color: {colors[0]}");
            if (colors.Count > 1)
                Debug.Log($"Second vertex color: {colors[1]}");
        }

        // メッシュコンポーネント追加
        MeshFilter meshFilter = heatmapObject.AddComponent<MeshFilter>();
        meshFilter.mesh = heatmapMesh;

        MeshRenderer meshRenderer = heatmapObject.AddComponent<MeshRenderer>();

        // 頂点カラーをサポートする透明マテリアル
        // Particles/Standard Unlitシェーダーは頂点カラーをネイティブサポート
        Shader shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
        {
            // フォールバック: Mobile/Particles/Additive
            shader = Shader.Find("Mobile/Particles/Additive");
            Debug.LogWarning("Particles/Standard Unlit shader not found, using Mobile/Particles/Additive");
        }
        if (shader == null)
        {
            // 最終フォールバック: Unlit/Color
            shader = Shader.Find("Unlit/Color");
            Debug.LogWarning("Mobile/Particles/Additive shader not found, using Unlit/Color");
        }

        Material mat = new Material(shader);

        // Particles/Standard Unlitの設定
        if (shader.name.Contains("Particles/Standard Unlit"))
        {
            mat.SetInt("_BlendMode", 0); // Blend
            mat.SetInt("_ColorMode", 0); // Multiply
            mat.SetFloat("_FlipbookMode", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
        }

        meshRenderer.material = mat;
        meshRenderer.enabled = visualizationManager.ifVisible;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // デバッグ情報
        Debug.Log($"Heatmap object position: {heatmapObject.transform.position}");
        Debug.Log($"Heatmap object local position: {heatmapObject.transform.localPosition}");
        Debug.Log($"Parent transform position: {transform.position}");
        Debug.Log($"Mesh bounds: {heatmapMesh.bounds}");
        Debug.Log($"Using shader: {shader.name}");
        Debug.Log($"MeshRenderer enabled: {meshRenderer.enabled}");
        Debug.Log($"visualizationManager.ifVisible: {visualizationManager.ifVisible}");
        Debug.Log($"Heatmap GameObject active: {heatmapObject.activeSelf}");
    }

    /// <summary>
    /// ポテンシャル値を色に変換
    /// </summary>
    Color GetPotentialColor(float potential, bool inside)
    {
        if (!inside)
        {
            return new Color(0, 0, 0, 0); // 透明（外側）
        }

        // 青（低ポテンシャル） → シアン → 緑 → 黄 → 赤（高ポテンシャル）
        float t = Mathf.Clamp01(potential / maxPotentialForColor);

        Color lowColor = new Color(0, 0, 1, 0.8f);    // 青（安全）
        Color midColor = new Color(0, 1, 0, 0.85f);   // 緑（中間）
        Color highColor = new Color(1, 0, 0, 0.9f);   // 赤（危険）

        if (t < 0.5f)
        {
            return Color.Lerp(lowColor, midColor, t * 2f);
        }
        else
        {
            return Color.Lerp(midColor, highColor, (t - 0.5f) * 2f);
        }
    }

    /// <summary>
    /// 矢印フィールドを生成
    /// </summary>
    void GenerateArrowField(SingleSpace space)
    {
        if (globalConfiguration.negArrow == null)
        {
            Debug.LogWarning("negArrow prefab is not set in GlobalConfiguration");
            return;
        }

        int stepX = Mathf.Max(1, Mathf.RoundToInt(arrowSpacing / gridCellSize));
        int stepY = Mathf.Max(1, Mathf.RoundToInt(arrowSpacing / gridCellSize));

        for (int x = 0; x < gridWidth; x += stepX)
        {
            for (int y = 0; y < gridHeight; y += stepY)
            {
                Vector2 cellPos = gridMin + new Vector2(x * gridCellSize, y * gridCellSize);

                // トラッキングスペース内かチェック
                if (!TrackingSpaceGenerator.IsPointInsidePolygon(space.trackingSpace, cellPos))
                    continue;

                Vector2 gradient = gradientGrid[x, y];

                // 勾配が非常に小さい場合はスキップ
                if (gradient.magnitude < 0.01f)
                    continue;

                // 矢印オブジェクトを生成
                GameObject arrow = Instantiate(globalConfiguration.negArrow);
                arrow.name = $"Arrow_{x}_{y}";
                arrow.transform.SetParent(transform);

                // レイヤー設定（安全にチェック）
                int realLayer = LayerMask.NameToLayer("Real");
                if (realLayer != -1)
                {
                    arrow.layer = realLayer;
                }
                // レイヤーが存在しない場合はデフォルトレイヤー（0）を使用

                // 位置と向きを設定
                Vector3 pos = Utilities.UnFlatten(cellPos) + Vector3.up * arrowHeight;
                arrow.transform.position = pos;
                arrow.transform.forward = transform.rotation * Utilities.UnFlatten(gradient.normalized);

                // スケール調整（勾配の大きさで変更）
                float scale = arrowScale * Mathf.Clamp01(gradient.magnitude / 5f);
                arrow.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);

                // 可視性設定
                foreach (var mr in arrow.GetComponentsInChildren<MeshRenderer>())
                {
                    mr.enabled = visualizationManager.ifVisible;
                }

                arrowObjects.Add(arrow);
            }
        }
    }

    /// <summary>
    /// 可視化をクリア
    /// </summary>
    void ClearVisualization()
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

    // エディタ用のギズモ描画
    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!enableVisualization || potentialGrid == null)
            return;

        // グリッド境界を描画
        Gizmos.color = Color.yellow;
        Vector3 p1 = Utilities.UnFlatten(gridMin);
        Vector3 p2 = Utilities.UnFlatten(new Vector2(gridMax.x, gridMin.y));
        Vector3 p3 = Utilities.UnFlatten(gridMax);
        Vector3 p4 = Utilities.UnFlatten(new Vector2(gridMin.x, gridMax.y));

        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);
    }
    #endif
}
