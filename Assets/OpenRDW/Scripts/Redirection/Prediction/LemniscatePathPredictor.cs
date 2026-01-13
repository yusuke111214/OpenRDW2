using UnityEngine;
using System.Collections.Generic;
using Curves;

/// <summary>
/// レムニスケートベースのパス予測器（LPP）- PredRedLPPアルゴリズム用
///
/// レムニスケートとは：
/// - ∞（無限大）の形をした数学曲線
/// - ユーザーの前方にこの形を配置し、その上の点へのクロソイド曲線を生成
///
/// 動作原理：
/// 1. ユーザーの前方にレムニスケート形状を配置
/// 2. その形状上に複数のエンドポイント（終了地点）を等間隔で設定
/// 3. 現在位置から各エンドポイントへのクロソイド曲線を生成
/// 4. これらの曲線が「ユーザーが歩きそうな軌跡の候補」となる
///
/// 論文: "Predictive multiuser redirected walking using artificial potential fields" (Hirt et al., 2024)
/// </summary>
public class LemniscatePathPredictor : MonoBehaviour, IPathPredictor
{
    [Header("予測パラメータ")]
    [Tooltip("予測ホライゾン（メートル）。論文のA_Lem、通常3m")]
    public float predictionHorizon = 3f;

    [Tooltip("レムニスケート上のエンドポイント数（左右対称のため奇数推奨）")]
    public int lemniscateEndpoints = 11;

    [Tooltip("1つの軌跡あたりのサンプル点数")]
    public int trajectorySamplePoints = 20;

    [Tooltip("予測開始点のルックバックステップ数（論文のt-T、0=現在位置を使用）")]
    public int lookbackSteps = 0; // シミュレーション環境では現在位置を使用

    private GlobalConfiguration globalConfiguration;

    void Awake()
    {
        globalConfiguration = GetComponentInParent<GlobalConfiguration>();
    }

    void Start()
    {
        // Load parameters from GlobalConfiguration if available
        if (globalConfiguration != null)
        {
            predictionHorizon = globalConfiguration.predictionHorizon;
            lemniscateEndpoints = globalConfiguration.lemniscateEndpoints;
        }
    }

    /// <summary>
    /// レムニスケートベースの手法で予測軌跡を生成
    ///
    /// 処理フロー：
    /// 1. 予測開始点を決定（現在 or T steps前）
    /// 2. レムニスケートのエンドポイントを生成
    /// 3. 各エンドポイントへのクロソイド曲線を生成
    /// 4. 有効な軌跡のリストを返す
    /// </summary>
    public List<Trajectory> GenerateTrajectories(
        Queue<Vector3> positionHistory,
        Queue<Vector3> directionHistory,
        Vector3 currentPosition,
        Vector3 currentDirection)
    {
        List<Trajectory> trajectories = new List<Trajectory>();

        // 予測開始点を決定（論文のt-T）
        // 注意：Queue.ToArray()は古い順（最初が一番古い）
        Vector3 predictionStartPos;
        Vector3 predictionStartDir;

        if (positionHistory.Count >= lookbackSteps && lookbackSteps > 0)
        {
            // Tステップ前の履歴位置を使用
            // Queueの順序：[最古, ..., 最新]
            Vector3[] posArray = positionHistory.ToArray();
            Vector3[] dirArray = directionHistory.ToArray();

            // 最新からTステップ戻った位置を取得
            int lookbackIndex = posArray.Length - lookbackSteps;
            if (lookbackIndex < 0) lookbackIndex = 0;

            predictionStartPos = posArray[lookbackIndex];
            predictionStartDir = dirArray[lookbackIndex];
        }
        else
        {
            // 履歴が不十分な場合は現在の姿勢を使用
            predictionStartPos = currentPosition;
            predictionStartDir = currentDirection;
        }

        // 2D（XZ平面）に変換
        // Unity: Y軸=上、XZ平面=地面
        Vector2 startPos2D = Utilities.FlattenedPos2D(predictionStartPos);
        Vector2 startDir2D = Utilities.FlattenedDir2D(predictionStartDir).normalized;

        // レムニスケートのエンドポイントを生成
        List<Vector2> endpoints = GenerateLemniscatePoints(
            startPos2D,
            startDir2D,
            predictionHorizon,
            lemniscateEndpoints
        );

        // 各エンドポイントへのクロソイド軌跡を生成
        int validCount = 0;
        int filteredCount = 0;
        float minTrajectoryLength = predictionHorizon * 0.5f; // 最低限の軌跡長（predictionHorizonの50%）

        foreach (var endpoint in endpoints)
        {
            // 開始点からエンドポイントへのクロソイド曲線を作成
            Clothoid clothoid = CurvesWrapper.CreateClothoidFromPoseAndPoint(
                startPos2D,
                startDir2D,
                endpoint
            );

            // 有効なクロソイドかチェック
            if (CurvesWrapper.IsValidClothoid(clothoid))
            {
                Trajectory trajectory = new Trajectory(clothoid, trajectorySamplePoints);

                // 軌跡の長さをチェック（短すぎる軌跡を除外）
                float trajectoryLength = Vector2.Distance(trajectory.startPosition, trajectory.endPosition);
                if (trajectoryLength >= minTrajectoryLength)
                {
                    trajectories.Add(trajectory);
                    validCount++;
                }
                else
                {
                    filteredCount++;
                }
            }
        }

        // デバッグ：軌跡生成をログ出力（パフォーマンス重視なら削除可）
        // Debug.Log($"LemniscatePredictor: {endpoints.Count}個中{validCount}個の有効なクロソイド軌跡を生成");

        return trajectories;
    }

    /// <summary>
    /// レムニスケート曲線上に分布するエンドポイントを生成
    ///
    /// レムニスケート方程式（論文 Eq.1）：
    /// x(t) = A_Lem × sin(t) / (1 + cos²(t))
    /// y(t) = A_Lem × sin(t)cos(t) / (1 + cos²(t))
    /// ここで t ∈ (0, π)
    ///
    /// この式の意味：
    /// - tを0からπまで変化させると、∞（無限大）の形を描く
    /// - A_Lemは形状のサイズ（大きいほど広がる）
    /// - 原点と向きに応じて、この形状を配置
    /// </summary>
    /// <param name="origin">レムニスケートの原点</param>
    /// <param name="direction">前進方向（レムニスケートの向きを決定）</param>
    /// <param name="size">レムニスケートのサイズパラメータ（A_Lem）</param>
    /// <param name="numPoints">エンドポイントのサンプル数</param>
    /// <returns>ワールド座標でのエンドポイント位置のリスト</returns>
    private List<Vector2> GenerateLemniscatePoints(
        Vector2 origin,
        Vector2 direction,
        float size,
        int numPoints)
    {
        List<Vector2> endpoints = new List<Vector2>();

        // パラメータtを(0, π)の範囲でサンプリング
        // t=0とt=πは特異点なので避ける（計算が不安定になる）
        float tMin = 0.05f;
        float tMax = Mathf.PI - 0.05f;

        for (int i = 0; i < numPoints; i++)
        {
            // tを(0, π)で均等に分布
            float t = tMin + (tMax - tMin) * (float)i / (numPoints - 1);

            // ローカル座標でレムニスケート点を計算
            float sinT = Mathf.Sin(t);
            float cosT = Mathf.Cos(t);
            float denom = 1f + cosT * cosT;

            float x_local = size * sinT / denom;
            // y_localの符号を反転：左右のエンドポイントを入れ替える
            // これにより、時計回り・反時計回り両方向のカーブ移動を実現
            float y_local = -size * sinT * cosT / denom;

            // ワールド座標に変換
            // 前進方向の角度で回転
            float angle = Mathf.Atan2(direction.y, direction.x);
            float cosAngle = Mathf.Cos(angle);
            float sinAngle = Mathf.Sin(angle);

            // 回転行列による座標変換
            float x_world = origin.x + (x_local * cosAngle - y_local * sinAngle);
            float y_world = origin.y + (x_local * sinAngle + y_local * cosAngle);

            endpoints.Add(new Vector2(x_world, y_world));
        }

        return endpoints;
    }

    /// <summary>
    /// シーン認識に基づいて軌跡をフィルタリング（障害物衝突検出）
    ///
    /// フィルタリング条件：
    /// 1. 仮想障害物との衝突がないこと
    /// 2. トラッキング空間内に収まっていること
    ///
    /// 両方の条件を満たす軌跡のみを「実行可能」として返す
    /// </summary>
    public List<Trajectory> FilterFeasibleTrajectories(
        List<Trajectory> trajectories,
        SingleSpace space)
    {
        List<Trajectory> feasible = new List<Trajectory>();

        foreach (var trajectory in trajectories)
        {
            // 仮想障害物との衝突をチェック
            bool hasCollision = trajectory.CheckCollisionWithObstacles(space.obstaclePolygons);

            // トラッキング空間内に収まっているかチェック
            bool withinBounds = trajectory.IsWithinTrackingSpace(space.trackingSpace);

            // 衝突なし かつ 範囲内 なら実行可能
            if (!hasCollision && withinBounds)
            {
                feasible.Add(trajectory);
            }
        }

        return feasible;
    }

    /// <summary>
    /// Path similarity measureを使用して単一の最良軌跡を選択（論文Section 3.2.2）
    ///
    /// 論文の記述:
    /// "From this generated set of trajectories, a single best prediction is isolated
    /// using a simple mean-squared error enhanced by a discount factor."
    ///
    /// これが論文のアルゴリズムの核心部分：
    /// - 複数の予測軌跡からT_pred（単一の最良予測）を選択
    /// - MSEベースの類似度測定を使用
    /// - 2段階選択プロセスの第1段階（予測）
    ///
    /// 処理フロー：
    /// 1. 各軌跡のMSEスコアを計算（Trajectory.CalculatePathSimilarity）
    /// 2. 最小MSEの軌跡を選択
    /// 3. 単一の最良軌跡T_predを返す
    ///
    /// 効果：
    /// - 計算量を削減（N_trajectories × N_actions → N_trajectories + N_actions）
    /// - 予測精度の向上（ユーザーの移動パターンに近い軌跡を選択）
    /// </summary>
    /// <param name="trajectories">フィルタリング済みの実行可能な軌跡リスト</param>
    /// <param name="positionHistory">HMDの位置履歴</param>
    /// <returns>最良の予測軌跡T_pred、またはnull</returns>
    public Trajectory SelectBestTrajectoryUsingSimilarityMeasure(
        List<Trajectory> trajectories,
        Queue<Vector3> positionHistory)
    {
        if (trajectories == null || trajectories.Count == 0)
            return null;

        if (positionHistory == null || positionHistory.Count < 2)
        {
            // 履歴がない場合は最初の軌跡を返す
            // 初期フレームでは履歴が不足している可能性があるため
            return trajectories[0];
        }

        float minMSE = float.MaxValue;
        Trajectory bestTrajectory = null;

        foreach (var trajectory in trajectories)
        {
            // MSEスコアを計算（割引係数はTrajectory内でデフォルト0.8）
            float mse = trajectory.CalculatePathSimilarity(positionHistory);

            if (mse < minMSE)
            {
                minMSE = mse;
                bestTrajectory = trajectory;
            }
        }

        return bestTrajectory;
    }

    /// <summary>
    /// 現在の予測ホライゾンを取得
    /// </summary>
    public float GetPredictionHorizon()
    {
        return predictionHorizon;
    }

    /// <summary>
    /// 予測ホライゾンを設定
    /// </summary>
    public void SetPredictionHorizon(float horizon)
    {
        predictionHorizon = horizon;
    }

    /// <summary>
    /// 可視化用のレムニスケート点を生成
    ///
    /// 使用目的：
    /// - リダイレクターがレムニスケート曲線を描画するために呼び出す
    /// - デバッグ時に予測範囲を視覚的に確認できる
    /// </summary>
    public List<Vector2> GenerateLemniscatePointsForVisualization(Vector2 origin, Vector2 direction, int numPoints)
    {
        return GenerateLemniscatePoints(origin, direction, predictionHorizon, numPoints);
    }
}
