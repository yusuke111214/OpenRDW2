using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// グリッドベースのAPFシステム
/// 物理空間を格子状に分割し、各セルのポテンシャル値と距離値を管理
///
/// 実装に基づく先行研究:
/// - Hirt et al. (2022): "Grid-based APF for redirected walking"
/// - Dong et al. (2020): "Dynamic Artificial Potential Fields"
/// </summary>
public class APFGrid
{
    // フィールド
    private float[,] _potentialField;  // 各セルのAPF値（障害物からの反発力）
    private float[,] _distanceField;   // 各セルから最も近い障害物までの距離
    private int _gridWidth;            // グリッドの横幅（セル数）
    private int _gridHeight;           // グリッドの縦幅（セル数）
    private float _cellSize;           // 1セルのサイズ（メートル）
    private Vector3 _gridOrigin;       // グリッドの原点（左下隅の座標）
    private Bounds _trackingSpace;     // トラッキング空間の境界

    // パラメータ（Hirt論文に基づく）
    private const float RepulsiveStrength = 100.0f;  // 斥力の最大強度
    private const float DecayRate = 2.0f;            // 距離による減衰率

    /// <summary>
    /// コンストラクタ：グリッドを初期化
    /// </summary>
    /// <param name="trackingSpace">トラッキング空間の境界</param>
    /// <param name="cellSize">1セルのサイズ（メートル）デフォルトは0.2m</param>
    public APFGrid(Bounds trackingSpace, float cellSize = 0.2f)
    {
        _trackingSpace = trackingSpace;
        _cellSize = cellSize;

        // グリッドのサイズを計算
        // 例：10m x 10mの空間を0.2mのセルで分割 → 50x50 = 2500セル
        _gridWidth = Mathf.CeilToInt(trackingSpace.size.x / cellSize);
        _gridHeight = Mathf.CeilToInt(trackingSpace.size.z / cellSize);

        // グリッドの原点を計算（トラッキング空間の左下隅）
        _gridOrigin = trackingSpace.center - new Vector3(
            trackingSpace.size.x / 2f,
            0,
            trackingSpace.size.z / 2f
        );

        // 配列を初期化
        _potentialField = new float[_gridWidth, _gridHeight];
        _distanceField = new float[_gridWidth, _gridHeight];

        Debug.Log($"APFGrid初期化: {_gridWidth}x{_gridHeight}セル（合計{_gridWidth * _gridHeight}セル）, セルサイズ: {cellSize}m");
    }

    /// <summary>
    /// Distance Fieldを計算
    /// 各セルから最も近い障害物までの距離を計算し、距離値として格納
    /// </summary>
    /// <param name="obstacles">障害物の位置リスト（壁の頂点、障害物の頂点など）</param>
    public void CalculateDistanceField(List<Vector2> obstacles)
    {
        // 全セルをループ
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridHeight; z++)
            {
                // このセルのワールド座標を取得（2D）
                Vector2 cellWorldPos2D = GridToWorld2D(x, z);

                // 全ての障害物までの距離を計算し、最小値を取得
                float minDistance = float.MaxValue;
                foreach (Vector2 obstacle in obstacles)
                {
                    float distance = Vector2.Distance(cellWorldPos2D, obstacle);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }
                }

                // 距離値を格納
                _distanceField[x, z] = minDistance;
            }
        }
    }

    /// <summary>
    /// ポリゴン辺を考慮したDistance Fieldを計算
    /// 点だけでなく、線分（壁の辺）までの距離も計算する、より正確な実装
    /// </summary>
    /// <param name="polygons">障害物のポリゴンリスト（各ポリゴンは頂点のリスト）</param>
    public void CalculateDistanceFieldFromPolygons(List<List<Vector2>> polygons)
    {
        // 全セルをループ
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridHeight; z++)
            {
                // このセルのワールド座標を取得（2D）
                Vector2 cellWorldPos2D = GridToWorld2D(x, z);

                float minDistance = float.MaxValue;

                // 各ポリゴンに対して処理
                foreach (List<Vector2> polygon in polygons)
                {
                    // ポリゴンの各辺に対して最短距離を計算
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        Vector2 p1 = polygon[i];
                        Vector2 p2 = polygon[(i + 1) % polygon.Count];

                        // 点と線分の最短距離を計算
                        float distance = DistancePointToLineSegment(cellWorldPos2D, p1, p2);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                    }
                }

                // 距離値を格納
                _distanceField[x, z] = minDistance;
            }
        }

        Debug.Log($"Distance Field計算完了: {polygons.Count}個のポリゴン");
    }

    /// <summary>
    /// 点と線分の最短距離を計算
    /// </summary>
    private float DistancePointToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLength = line.magnitude;

        if (lineLength < 0.0001f)
        {
            // 線分が点の場合
            return Vector2.Distance(point, lineStart);
        }

        // 点から線分への投影を計算
        Vector2 lineDir = line / lineLength;
        Vector2 toPoint = point - lineStart;
        float projection = Vector2.Dot(toPoint, lineDir);

        // 投影が線分の範囲内にあるかチェック
        if (projection < 0)
        {
            // 線分の開始点が最も近い
            return Vector2.Distance(point, lineStart);
        }
        else if (projection > lineLength)
        {
            // 線分の終了点が最も近い
            return Vector2.Distance(point, lineEnd);
        }
        else
        {
            // 線分上の点が最も近い
            Vector2 closestPoint = lineStart + lineDir * projection;
            return Vector2.Distance(point, closestPoint);
        }
    }

    /// <summary>
    /// ポテンシャルフィールドを計算
    /// 距離値からAPF値を計算（障害物に近いほど高いポテンシャル）
    /// </summary>
    public void CalculatePotentialField()
    {
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridHeight; z++)
            {
                float distance = _distanceField[x, z];

                // Non-harmonic APF公式: U = F_max / (1 + k * d)
                // 距離が近い → ポテンシャルが高い（危険）
                // 距離が遠い → ポテンシャルが低い（安全）
                _potentialField[x, z] = RepulsiveStrength / (1.0f + DecayRate * distance);
            }
        }

        Debug.Log("Potential Field計算完了");
    }

    /// <summary>
    /// グリッド座標をワールド座標（3D）に変換
    /// </summary>
    public Vector3 GridToWorld(int x, int z)
    {
        return _gridOrigin + new Vector3(
            x * _cellSize + _cellSize / 2f,  // セルの中心
            0,
            z * _cellSize + _cellSize / 2f
        );
    }

    /// <summary>
    /// グリッド座標をワールド座標（2D）に変換
    /// </summary>
    public Vector2 GridToWorld2D(int x, int z)
    {
        Vector3 world3D = GridToWorld(x, z);
        return new Vector2(world3D.x, world3D.z);
    }

    /// <summary>
    /// ワールド座標（3D）をグリッド座標に変換
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 localPos = worldPos - _gridOrigin;
        int x = Mathf.Clamp(Mathf.FloorToInt(localPos.x / _cellSize), 0, _gridWidth - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(localPos.z / _cellSize), 0, _gridHeight - 1);
        return new Vector2Int(x, z);
    }

    /// <summary>
    /// ワールド座標（2D）をグリッド座標に変換
    /// </summary>
    public Vector2Int WorldToGrid2D(Vector2 worldPos2D)
    {
        Vector2 localPos = worldPos2D - new Vector2(_gridOrigin.x, _gridOrigin.z);
        int x = Mathf.Clamp(Mathf.FloorToInt(localPos.x / _cellSize), 0, _gridWidth - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(localPos.y / _cellSize), 0, _gridHeight - 1);
        return new Vector2Int(x, z);
    }

    /// <summary>
    /// 指定位置のポテンシャル値を取得（バイリニア補間）
    /// </summary>
    public float GetPotential(Vector3 worldPos)
    {
        Vector2Int gridPos = WorldToGrid(worldPos);
        return _potentialField[gridPos.x, gridPos.y];
    }

    /// <summary>
    /// 指定位置の距離値を取得
    /// </summary>
    public float GetDistance(Vector3 worldPos)
    {
        Vector2Int gridPos = WorldToGrid(worldPos);
        return _distanceField[gridPos.x, gridPos.y];
    }

    /// <summary>
    /// 指定位置のAPF勾配（力の方向）を計算
    /// 有限差分法で勾配を近似
    /// </summary>
    public Vector3 GetGradient(Vector3 worldPos)
    {
        Vector2Int gridPos = WorldToGrid(worldPos);

        // 境界チェック
        if (gridPos.x <= 0 || gridPos.x >= _gridWidth - 1 ||
            gridPos.y <= 0 || gridPos.y >= _gridHeight - 1)
        {
            return Vector3.zero;
        }

        // 有限差分法で勾配を計算
        // ∂U/∂x ≈ (U(x+1) - U(x-1)) / (2 * cellSize)
        float dx = (_potentialField[gridPos.x + 1, gridPos.y] -
                   _potentialField[gridPos.x - 1, gridPos.y]) / (2f * _cellSize);
        float dz = (_potentialField[gridPos.x, gridPos.y + 1] -
                   _potentialField[gridPos.x, gridPos.y - 1]) / (2f * _cellSize);

        // 勾配の逆方向 = 力の方向（低いポテンシャルに向かう）
        return new Vector3(-dx, 0, -dz);
    }

    /// <summary>
    /// 指定位置のAPF勾配を2Dベクトルで取得
    /// </summary>
    public Vector2 GetGradient2D(Vector3 worldPos)
    {
        Vector3 grad3D = GetGradient(worldPos);
        return new Vector2(grad3D.x, grad3D.z);
    }

    // プロパティ
    public int Width => _gridWidth;
    public int Height => _gridHeight;
    public float CellSize => _cellSize;
    public Bounds TrackingSpace => _trackingSpace;

    /// <summary>
    /// デバッグ用：グリッド情報を文字列で取得
    /// </summary>
    public string GetDebugInfo()
    {
        return $"APFGrid: {_gridWidth}x{_gridHeight} (total {_gridWidth * _gridHeight} cells), " +
               $"Cell size: {_cellSize}m, " +
               $"Tracking space: {_trackingSpace.size.x}m x {_trackingSpace.size.z}m";
    }
}
