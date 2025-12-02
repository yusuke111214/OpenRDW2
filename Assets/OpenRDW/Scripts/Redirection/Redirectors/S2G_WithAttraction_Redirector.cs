// Grid-based APF with Attractive Forces for Redirected Walking
// S2G = Spatial-to-Grid approach
//
// 実装に基づく先行研究:
// - Hirt et al. (2022): Grid-based APF for efficient spatial analysis
// - Dong et al. (2020): Dynamic Artificial Potential Fields with attractive forces
// - Thomas & Rosenberg (2019): Base APF algorithm for RDW
//
// 主な改善点:
// - レイキャストベース → グリッドベースに変更
// - 物理空間全体を一度に把握できる
// - 開放空間の大域的評価が可能

using UnityEngine;
using System.Collections.Generic;

public class S2G_WithAttraction_Redirector : ThomasAPF_Redirector
{
    // グリッドシステム
    private APFGrid _apfGrid;
    private OpenSpaceFinder _openSpaceFinder;
    private AttractivePotentialField _attractiveField;

    // ステアリングターゲット
    private Vector3 _currentTarget;
    private float _lastTargetUpdateTime;

    // 可視化オブジェクト
    private GameObject _attractiveForcePointer;
    private GameObject _steeringTargetMarker;
    private GridBasedPotentialFieldVisualizer _gridVisualizer;

    // 初期化フラグ
    private bool _isInitialized = false;
    private bool _gridNeedsUpdate = true;

    // デバッグ用
    private Vector2 _lastRepulsiveForce;
    private Vector2 _lastAttractiveForce;
    private Vector2 _lastCombinedForce;

    void Start()
    {
        InitializeGridSystem();
    }

    private void InitializeGridSystem()
    {
        if (_isInitialized)
            return;

        try
        {
            // トラッキング空間の境界を取得
            Bounds trackingSpace = GetTrackingSpaceBounds();

            // グリッドパラメータを取得（GlobalConfigurationから）
            float cellSize = globalConfiguration.gridCellSize;

            // APFグリッドを初期化
            _apfGrid = new APFGrid(trackingSpace, cellSize);
            _openSpaceFinder = new OpenSpaceFinder(_apfGrid);

            // 引力フィールドを初期化（既存のAttractivePotentialFieldを再利用）
            _attractiveField = new AttractivePotentialField(
                globalConfiguration.attractionStrength,
                globalConfiguration.attractionSaturationDistance,
                globalConfiguration.attractionVelocityWeight
            );

            // 初期ターゲットをトラッキング空間の中心に設定
            _currentTarget = trackingSpace.center;
            _lastTargetUpdateTime = Time.time;

            // 初回のグリッド計算
            UpdateGridData();

            // グリッドベースの可視化システムを初期化
            InitializeGridVisualization();

            _isInitialized = true;

            Debug.Log($"S2G_WithAttraction_Redirector初期化完了: {_apfGrid.GetDebugInfo()}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"S2G_WithAttraction_Redirector初期化失敗: {e.Message}");
        }
    }

    /// <summary>
    /// グリッドベースの可視化システムを初期化
    /// </summary>
    private void InitializeGridVisualization()
    {
        // GridBasedPotentialFieldVisualizerコンポーネントを取得または追加
        _gridVisualizer = GetComponent<GridBasedPotentialFieldVisualizer>();
        if (_gridVisualizer == null)
        {
            _gridVisualizer = gameObject.AddComponent<GridBasedPotentialFieldVisualizer>();
        }

        // APFGridを設定
        if (_apfGrid != null)
        {
            _gridVisualizer.SetAPFGrid(_apfGrid);
        }
    }

    /// <summary>
    /// トラッキング空間の境界を取得
    /// </summary>
    private Bounds GetTrackingSpaceBounds()
    {
        int userIndex = movementManager.physicalSpaceIndex;
        SingleSpace space = globalConfiguration.physicalSpaces[userIndex];

        // トラッキング空間の頂点から境界を計算
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        foreach (var point in space.trackingSpace)
        {
            min.x = Mathf.Min(min.x, point.x);
            min.y = Mathf.Min(min.y, point.y);
            max.x = Mathf.Max(max.x, point.x);
            max.y = Mathf.Max(max.y, point.y);
        }

        Vector2 center2D = (min + max) / 2f;
        Vector2 size2D = max - min;

        return new Bounds(
            new Vector3(center2D.x, 0, center2D.y),
            new Vector3(size2D.x, 0, size2D.y)
        );
    }

    /// <summary>
    /// グリッドデータを更新（Distance Field + Potential Field）
    /// </summary>
    private void UpdateGridData()
    {
        if (_apfGrid == null)
            return;

        // 障害物ポリゴンを取得
        List<List<Vector2>> obstaclePolygons = GetObstaclePolygons();

        // Distance Fieldを計算
        _apfGrid.CalculateDistanceFieldFromPolygons(obstaclePolygons);

        // Potential Fieldを計算
        _apfGrid.CalculatePotentialField();

        _gridNeedsUpdate = false;

        // 可視化を更新
        if (_gridVisualizer != null)
        {
            _gridVisualizer.RegenerateVisualization();
        }

        if (globalConfiguration.logAttractionDebugInfo)
        {
            Debug.Log($"グリッドデータ更新完了: {obstaclePolygons.Count}個のポリゴン");
        }
    }

    /// <summary>
    /// 障害物ポリゴンを取得
    /// </summary>
    private List<List<Vector2>> GetObstaclePolygons()
    {
        List<List<Vector2>> polygons = new List<List<Vector2>>();

        int userIndex = movementManager.physicalSpaceIndex;
        SingleSpace space = globalConfiguration.physicalSpaces[userIndex];

        // トラッキング空間の境界を追加
        polygons.Add(new List<Vector2>(space.trackingSpace));

        // 障害物を追加
        foreach (var obstacle in space.obstaclePolygons)
        {
            polygons.Add(new List<Vector2>(obstacle));
        }

        return polygons;
    }

    public override void InjectRedirection()
    {
        // 初期化確認
        if (!_isInitialized)
        {
            InitializeGridSystem();
            if (!_isInitialized)
            {
                // 初期化失敗時はベースクラスの動作にフォールバック
                base.InjectRedirection();
                return;
            }
        }

        // グリッド更新が必要な場合（障害物が変化した場合など）
        if (_gridNeedsUpdate)
        {
            UpdateGridData();
        }

        // ユーザーの状態を取得
        Vector3 userPosition = redirectionManager.currPosReal;
        Vector3 userVelocity = redirectionManager.deltaPos / Time.deltaTime;

        // 1. グリッドベースの斥力を計算
        Vector2 repulsiveForce = _apfGrid.GetGradient2D(userPosition);

        // 2. 定期的にステアリングターゲットを更新
        if (Time.time - _lastTargetUpdateTime > globalConfiguration.attractionTargetUpdateInterval)
        {
            UpdateSteeringTarget(userPosition);
            _lastTargetUpdateTime = Time.time;
        }

        // 3. 引力を計算
        Vector2 attractiveForce = Vector2.zero;
        if (globalConfiguration.enableAttractivePotentialField)
        {
            Vector3 attractiveForce3D = _attractiveField.CalculateAttractiveForceWithVelocity(
                userPosition,
                _currentTarget,
                userVelocity
            );
            attractiveForce = new Vector2(attractiveForce3D.x, attractiveForce3D.z);
        }

        // 4. 力を合成
        float repWeight = globalConfiguration.repulsiveWeight;
        float attWeight = globalConfiguration.attractiveWeight;

        // 適応的重み調整（オプション）
        if (globalConfiguration.useAdaptiveWeights)
        {
            float distToObstacle = _apfGrid.GetDistance(userPosition);
            AdjustWeightsAdaptively(distToObstacle, ref repWeight, ref attWeight);
        }

        // 重みを正規化
        float totalWeight = repWeight + attWeight;
        if (totalWeight > 0)
        {
            repWeight /= totalWeight;
            attWeight /= totalWeight;
        }

        Vector2 combinedForce = repWeight * repulsiveForce + attWeight * attractiveForce;

        // デバッグ用に力を保存
        _lastRepulsiveForce = repulsiveForce;
        _lastAttractiveForce = attractiveForce;
        _lastCombinedForce = combinedForce;

        // 5. 合成力に基づいてリダイレクションを適用
        ApplyRedirectionByNegativeGradient(combinedForce);

        // 6. 可視化を更新
        UpdateVisualization(userPosition, repulsiveForce, attractiveForce, combinedForce);
    }

    /// <summary>
    /// ステアリングターゲットを更新
    /// </summary>
    private void UpdateSteeringTarget(Vector3 userPosition)
    {
        if (_openSpaceFinder == null)
            return;

        // グリッドベースで最適な開放空間を検出
        float minOpenDistance = globalConfiguration.gridMinOpenDistance;

        // 検索範囲を限定したバージョンを使用（パフォーマンス向上）
        if (globalConfiguration.useLocalSearch)
        {
            _currentTarget = _openSpaceFinder.FindBestOpenSpaceInRadius(
                userPosition,
                minOpenDistance,
                globalConfiguration.localSearchRadius
            );
        }
        else
        {
            _currentTarget = _openSpaceFinder.FindBestOpenSpace(userPosition, minOpenDistance);
        }
    }

    /// <summary>
    /// 距離に基づいて重みを適応的に調整
    /// </summary>
    private void AdjustWeightsAdaptively(float distToObstacle, ref float repWeight, ref float attWeight)
    {
        float closeThreshold = globalConfiguration.adaptiveCloseDistance;
        float farThreshold = globalConfiguration.adaptiveFarDistance;

        if (distToObstacle < closeThreshold)
        {
            // 障害物に近い：斥力を優先
            repWeight = 0.9f;
            attWeight = 0.1f;
        }
        else if (distToObstacle > farThreshold)
        {
            // 障害物から遠い：引力を優先
            repWeight = 0.5f;
            attWeight = 0.5f;
        }
        else
        {
            // 中間：線形補間
            float t = (distToObstacle - closeThreshold) / (farThreshold - closeThreshold);
            repWeight = Mathf.Lerp(0.9f, 0.5f, t);
            attWeight = Mathf.Lerp(0.1f, 0.5f, t);
        }
    }

    /// <summary>
    /// 可視化を更新
    /// </summary>
    private void UpdateVisualization(
        Vector3 userPosition,
        Vector2 repulsiveForce,
        Vector2 attractiveForce,
        Vector2 combinedForce)
    {
        if (!globalConfiguration.enableAttractionVisualization || globalConfiguration.runInBackstage)
        {
            return;
        }

        // 引力ベクトルの可視化
        if (_attractiveForcePointer == null && globalConfiguration.showAttractiveForce)
        {
            _attractiveForcePointer = Instantiate(globalConfiguration.negArrow);
            _attractiveForcePointer.transform.SetParent(transform);

            // シアン色に設定
            foreach (var renderer in _attractiveForcePointer.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.material.color = Color.cyan;
                renderer.enabled = visualizationManager.ifVisible;
            }
        }

        if (_attractiveForcePointer != null && globalConfiguration.showAttractiveForce)
        {
            _attractiveForcePointer.SetActive(visualizationManager.ifVisible);
            _attractiveForcePointer.transform.position = redirectionManager.currPos;

            if (attractiveForce.magnitude > 0.01f)
            {
                Vector3 attractiveForce3D = new Vector3(attractiveForce.x, 0, attractiveForce.y);
                _attractiveForcePointer.transform.forward = transform.rotation * attractiveForce3D;

                float scale = Mathf.Clamp(attractiveForce.magnitude * 0.5f, 0.5f, 2.0f);
                _attractiveForcePointer.transform.localScale = Vector3.one * scale;
            }
        }

        // ステアリングターゲットの可視化
        if (_steeringTargetMarker == null && globalConfiguration.showSteeringTarget)
        {
            _steeringTargetMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _steeringTargetMarker.transform.SetParent(transform);
            _steeringTargetMarker.transform.localScale = Vector3.one * 0.3f;

            var renderer = _steeringTargetMarker.GetComponent<MeshRenderer>();
            renderer.material.color = Color.yellow;
            renderer.enabled = visualizationManager.ifVisible;

            Destroy(_steeringTargetMarker.GetComponent<Collider>());
        }

        if (_steeringTargetMarker != null && globalConfiguration.showSteeringTarget)
        {
            _steeringTargetMarker.SetActive(visualizationManager.ifVisible);
            Vector3 targetWorldPos = redirectionManager.trackingSpace.TransformPoint(_currentTarget);
            _steeringTargetMarker.transform.position = targetWorldPos;
        }

        // 合成力の可視化（親クラスのメソッドを使用）
        UpdateTotalForcePointer(combinedForce);

        // デバッグログ
        if (globalConfiguration.logAttractionDebugInfo && Time.frameCount % 60 == 0)
        {
            float distToObstacle = _apfGrid.GetDistance(userPosition);
            Debug.Log($"[S2G-APF] Rep: {repulsiveForce.magnitude:F3}, " +
                      $"Att: {attractiveForce.magnitude:F3}, " +
                      $"Combined: {combinedForce.magnitude:F3}, " +
                      $"Dist to obstacle: {distToObstacle:F2}m");
        }
    }

    private void OnDestroy()
    {
        // 可視化オブジェクトのクリーンアップ
        if (_attractiveForcePointer != null)
            Destroy(_attractiveForcePointer);

        if (_steeringTargetMarker != null)
            Destroy(_steeringTargetMarker);

        // GridBasedPotentialFieldVisualizerは自動的にクリーンアップされる
    }

    /// <summary>
    /// ステアリングターゲットの強制更新（リセット時などに使用）
    /// </summary>
    public void ForceUpdateSteeringTarget()
    {
        if (_isInitialized)
        {
            UpdateSteeringTarget(redirectionManager.currPosReal);
        }
    }

    /// <summary>
    /// グリッドの強制再計算（障害物が変化した場合に使用）
    /// </summary>
    public void ForceGridUpdate()
    {
        _gridNeedsUpdate = true;
    }
}
