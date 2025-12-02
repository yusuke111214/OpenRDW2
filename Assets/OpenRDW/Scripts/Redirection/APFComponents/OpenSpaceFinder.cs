using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// グリッドベースで開放空間を検出し、最適なステアリングターゲットを選択
///
/// 実装に基づく先行研究:
/// - Dong et al. (2020): "Dynamic Artificial Potential Fields for Multi-User Redirected Walking"
///   開放空間の評価基準: 面積と境界までの距離の加重和
/// </summary>
public class OpenSpaceFinder
{
    private APFGrid _grid;

    // パラメータ（Dongら2020の論文値）
    private const float AreaWeight = 0.6f;      // 開放空間の面積（距離値）の重み
    private const float DistanceWeight = 0.4f;  // 境界までの距離の重み
    private const float MaxUserDistance = 5.0f; // ユーザーから遠すぎる場所へのペナルティ閾値

    public OpenSpaceFinder(APFGrid grid)
    {
        _grid = grid;
    }

    /// <summary>
    /// 最適な開放空間の中心位置を見つける
    /// </summary>
    /// <param name="userPosition">ユーザーの現在位置</param>
    /// <param name="minDistance">開放空間とみなす最小距離（メートル）</param>
    /// <returns>最適なステアリングターゲットの位置</returns>
    public Vector3 FindBestOpenSpace(Vector3 userPosition, float minDistance = 1.0f)
    {
        float bestScore = float.MinValue;
        Vector3 bestPosition = userPosition;
        int candidatesChecked = 0;
        int validCandidates = 0;

        // 全グリッドをスキャンして、最も開放的な場所を探す
        // パフォーマンス最適化: 2セルおきにチェック（粗いグリッド）
        int step = 2;

        for (int x = 0; x < _grid.Width; x += step)
        {
            for (int z = 0; z < _grid.Height; z += step)
            {
                candidatesChecked++;
                Vector3 candidatePos = _grid.GridToWorld(x, z);
                float distance = _grid.GetDistance(candidatePos);

                // 開放空間の条件：障害物から十分離れている
                if (distance < minDistance)
                {
                    continue;
                }

                validCandidates++;

                // スコア計算：距離値が大きいほど良い
                // （実際のDongらの手法では面積も考慮するが、
                //  グリッドベースでは距離値が面積の代理指標となる）
                float score = AreaWeight * distance;

                // ユーザーからの距離も考慮
                float userDistance = Vector3.Distance(userPosition, candidatePos);

                // ユーザーから近い場所を優先（ただし遠すぎる場所にペナルティ）
                if (userDistance > MaxUserDistance)
                {
                    score *= 0.3f;  // 大きなペナルティ
                }
                else
                {
                    // 近いほどボーナス（ただし適度な距離を保つ）
                    float proximityBonus = 1.0f - (userDistance / MaxUserDistance) * 0.3f;
                    score *= proximityBonus;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPosition = candidatePos;
                }
            }
        }

        // デバッグログ（60フレームに1回のみ）
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[OpenSpaceFinder] ベスト位置={bestPosition}, スコア={bestScore:F2}, " +
                      $"チェック数={candidatesChecked}, 有効候補={validCandidates}");
        }

        return bestPosition;
    }

    /// <summary>
    /// より高度な開放空間検出（局所的な最大値を検出）
    /// </summary>
    /// <param name="userPosition">ユーザーの現在位置</param>
    /// <param name="minDistance">開放空間とみなす最小距離</param>
    /// <param name="searchRadius">ユーザー周辺の検索半径（メートル）</param>
    /// <returns>最適なステアリングターゲット</returns>
    public Vector3 FindBestOpenSpaceInRadius(Vector3 userPosition, float minDistance = 1.0f, float searchRadius = 8.0f)
    {
        float bestScore = float.MinValue;
        Vector3 bestPosition = userPosition;
        int validCandidates = 0;

        // ユーザー位置をグリッド座標に変換
        Vector2Int userGridPos = _grid.WorldToGrid(userPosition);

        // 検索範囲をセル数に変換
        int searchRadiusCells = Mathf.CeilToInt(searchRadius / _grid.CellSize);

        // ユーザー周辺のグリッドのみスキャン
        for (int dx = -searchRadiusCells; dx <= searchRadiusCells; dx++)
        {
            for (int dz = -searchRadiusCells; dz <= searchRadiusCells; dz++)
            {
                int x = userGridPos.x + dx;
                int z = userGridPos.y + dz;

                // グリッド範囲外チェック
                if (x < 0 || x >= _grid.Width || z < 0 || z >= _grid.Height)
                    continue;

                Vector3 candidatePos = _grid.GridToWorld(x, z);

                // 円形検索範囲チェック
                float distToUser = Vector3.Distance(userPosition, candidatePos);
                if (distToUser > searchRadius)
                    continue;

                float distance = _grid.GetDistance(candidatePos);

                // 開放空間の条件
                if (distance < minDistance)
                    continue;

                validCandidates++;

                // スコア計算
                float score = distance;

                // ユーザーからの距離によるボーナス/ペナルティ
                if (distToUser < 2.0f)
                {
                    // 近すぎる場合はペナルティ（現在位置付近は避ける）
                    score *= 0.5f;
                }
                else if (distToUser > MaxUserDistance)
                {
                    // 遠すぎる場合もペナルティ
                    score *= 0.3f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPosition = candidatePos;
                }
            }
        }

        if (validCandidates == 0)
        {
            // 有効な候補が見つからない場合は、全体検索にフォールバック
            Debug.LogWarning($"半径{searchRadius}m内に開放空間が見つからず、全体検索にフォールバック");
            return FindBestOpenSpace(userPosition, minDistance);
        }

        return bestPosition;
    }

    /// <summary>
    /// 開放空間の領域を取得（デバッグ用）
    /// </summary>
    /// <param name="minDistance">開放空間とみなす最小距離</param>
    /// <returns>開放空間に該当するセルの位置リスト</returns>
    public List<Vector3> GetOpenSpaceRegion(float minDistance = 1.0f)
    {
        List<Vector3> openSpaceCells = new List<Vector3>();

        // パフォーマンス最適化: 2セルおきに取得
        int step = 2;

        for (int x = 0; x < _grid.Width; x += step)
        {
            for (int z = 0; z < _grid.Height; z += step)
            {
                Vector3 cellPos = _grid.GridToWorld(x, z);
                if (_grid.GetDistance(cellPos) >= minDistance)
                {
                    openSpaceCells.Add(cellPos);
                }
            }
        }

        return openSpaceCells;
    }

    /// <summary>
    /// 指定位置が開放空間かどうかを判定
    /// </summary>
    /// <param name="position">判定する位置</param>
    /// <param name="minDistance">開放空間とみなす最小距離</param>
    /// <returns>開放空間ならtrue</returns>
    public bool IsOpenSpace(Vector3 position, float minDistance = 1.0f)
    {
        float distance = _grid.GetDistance(position);
        return distance >= minDistance;
    }

    /// <summary>
    /// デバッグ情報を取得
    /// </summary>
    public string GetDebugInfo()
    {
        int openSpaceCount = 0;
        float avgDistance = 0;

        for (int x = 0; x < _grid.Width; x++)
        {
            for (int z = 0; z < _grid.Height; z++)
            {
                Vector3 cellPos = _grid.GridToWorld(x, z);
                float dist = _grid.GetDistance(cellPos);
                avgDistance += dist;
                if (dist >= 1.0f)
                {
                    openSpaceCount++;
                }
            }
        }

        int totalCells = _grid.Width * _grid.Height;
        avgDistance /= totalCells;
        float openSpaceRatio = (float)openSpaceCount / totalCells;

        return $"OpenSpaceFinder: 開放空間セル={openSpaceCount}/{totalCells} ({openSpaceRatio * 100:F1}%), " +
               $"平均距離={avgDistance:F2}m";
    }
}
