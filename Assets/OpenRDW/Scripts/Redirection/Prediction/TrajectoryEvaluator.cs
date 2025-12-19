using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PredRedLPPアルゴリズムのコスト関数を使った軌跡評価器
///
/// 論文のコスト関数を実装：
/// - APFコスト（Eq. 16）：障害物との距離
/// - 見出しコスト（Eq. 17）：軌跡の向きの適切さ
/// - リセットコスト（Eq. 18）：範囲外に出るペナルティ
/// - 総コスト（Eq. 14-15）：割引率を適用した合計
///
/// コストとは：
/// - 各軌跡の「悪さ」を数値化したもの
/// - 値が小さいほど良い軌跡
/// - 複数の要素（障害物、向き、範囲）を組み合わせて評価
///
/// 論文: "Predictive multiuser redirected walking using artificial potential fields" (Hirt et al., 2024)
/// </summary>
public class TrajectoryEvaluator
{
    private GlobalConfiguration globalConfiguration;

    // コストパラメータ
    private float discountFactor; // α（論文）- 割引率、通常0.8
    private float headingCostWeight; // h0（論文）- 見出しコストの重み、通常1.0
    private const float RESET_PENALTY = 1000f; // 範囲外ペナルティ（大きな値）

    /// <summary>
    /// コンストラクタ（設定ファイルから初期化）
    ///
    /// パラメータ読み込み：
    /// - 割引率（discountFactor）
    /// - 見出しコストの重み（headingCostWeight）
    /// </summary>
    public TrajectoryEvaluator(GlobalConfiguration config)
    {
        this.globalConfiguration = config;
        this.discountFactor = config.discountFactor;
        this.headingCostWeight = config.headingCostWeight;
    }

    /// <summary>
    /// 全てのアクション-軌跡の組み合わせを評価し、最良のものを返す
    ///
    /// 評価プロセス：
    /// 1. 各軌跡のコストを計算
    /// 2. 各アクションと組み合わせて評価
    /// 3. 最小コストのアクション-軌跡ペアを選択
    /// </summary>
    /// <param name="predictions">予測された軌跡のリスト</param>
    /// <param name="actions">リダイレクションアクションのリスト</param>
    /// <param name="physicalSpace">現在の物理空間</param>
    /// <param name="redirectedAvatars">他のユーザーのリスト（APF計算用）</param>
    /// <param name="currentUserIndex">現在のユーザーの物理空間インデックス</param>
    /// <returns>最良のアクションと対応する軌跡のタプル</returns>
    public (RedirectionAction bestAction, Trajectory bestTrajectory) EvaluateAllActions(
        List<Trajectory> predictions,
        List<RedirectionAction> actions,
        SingleSpace physicalSpace,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        float minCost = float.MaxValue;
        RedirectionAction bestAction = null;
        Trajectory bestTrajectory = null;

        // Evaluate each trajectory
        foreach (var trajectory in predictions)
        {
            // 簡略化のため、リダイレクションアクションは予測軌跡の形状に
            // 大きな影響を与えないと仮定（論文の実装ノートに基づく）
            // この軌跡のコストを計算
            float trajectoryCost = CalculateTotalCost(
                trajectory,
                physicalSpace,
                redirectedAvatars,
                currentUserIndex,
                currentAvatarId
            );

            trajectory.totalCost = trajectoryCost;

            // 各アクションについて、コストは同様（簡略化モデル）
            // 完全な実装では、アクションが軌跡に与える影響をシミュレート
            foreach (var action in actions)
            {
                // 簡略版では軌跡コストを直接使用
                // より複雑な版では、アクションに基づいて軌跡を修正
                float actionCost = trajectoryCost;

                // オプション：ゲインコストを追加（現在は論文で未使用）
                // actionCost += CalculateGainCost(action);

                if (actionCost < minCost)
                {
                    minCost = actionCost;
                    bestAction = action;
                    bestTrajectory = trajectory;
                }
            }
        }

        // 有効な軌跡が見つからない場合はヌルアクションにフォールバック
        if (bestAction == null)
        {
            bestAction = RedirectionAction.CreateNullAction();
        }

        return (bestAction, bestTrajectory);
    }

    /// <summary>
    /// 割引率を使って軌跡の総コストを計算
    ///
    /// 論文 Eq. 14 を実装： J_total = Σ(α^i × J_i)
    ///
    /// 割引率とは：
    /// - 未来のコストを現在価値に換算する係数
    /// - α=0.8の場合、1ステップ先のコストは0.8倍、2ステップ先は0.64倍...
    /// - 遠い未来ほど影響が小さくなる（近い将来を重視）
    /// </summary>
    public float CalculateTotalCost(
        Trajectory trajectory,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        float totalCost = 0f;

        for (int i = 0; i < trajectory.points.Count; i++)
        {
            Vector2 point = trajectory.points[i];

            // 個別コスト要素を計算（Eq. 15）
            float J_APF = CalculateAPFCost(point, space, redirectedAvatars, currentUserIndex, currentAvatarId);
            float J_Heading = CalculateHeadingCost(i, trajectory, space, redirectedAvatars, currentUserIndex, currentAvatarId);
            float J_Reset = CalculateResetCost(point, space);
            float J_Gain = 0f; // 現在の実装では未使用

            // 個別コスト（Eq. 15）
            float J_i = J_APF + J_Heading + J_Reset + J_Gain;

            // 割引率を適用（Eq. 14）
            // 例：i=0なら1.0倍、i=1なら0.8倍、i=2なら0.64倍...
            totalCost += Mathf.Pow(discountFactor, i) * J_i;
        }

        return totalCost;
    }

    /// <summary>
    /// 点でのAPFコストを計算
    ///
    /// 論文 Eq. 16 を実装： J_APF = ||F_red||
    ///
    /// APFコストとは：
    /// - その点での反発力の大きさ
    /// - 障害物に近いほど大きくなる
    /// - ThomasAPFスタイルの1/d方式を使用
    /// </summary>
    private float CalculateAPFCost(
        Vector2 point,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        List<Vector2> nearestPosList = new List<Vector2>();

        // Physical borders' contributions
        for (int i = 0; i < space.trackingSpace.Count; i++)
        {
            var p = space.trackingSpace[i];
            var q = space.trackingSpace[(i + 1) % space.trackingSpace.Count];
            var nearestPos = Utilities.GetNearestPos(point, new List<Vector2> { p, q });
            var n = Utilities.RotateVector(q - p, -90).normalized;
            var d = point - nearestPos;

            if (Vector2.Dot(n, d.normalized) > 0)
            {
                nearestPosList.Add(nearestPos);
            }
        }

        // Obstacle contributions
        foreach (var obstacle in space.obstaclePolygons)
        {
            var nearestPos = Utilities.GetNearestPos(point, obstacle);
            nearestPosList.Add(nearestPos);
        }

        // Other users as point obstacles
        foreach (var user in redirectedAvatars)
        {
            var userMovementManager = user.GetComponent<MovementManager>();
            if (userMovementManager.physicalSpaceIndex != currentUserIndex)
            {
                continue;
            }

            var uId = userMovementManager.avatarId;
            if (uId == currentAvatarId)
                continue;

            var userRedirectionManager = user.GetComponent<RedirectionManager>();
            var nearestPos = Utilities.FlattenedPos2D(userRedirectionManager.currPosReal);
            nearestPosList.Add(nearestPos);
        }

        // ThomasAPF方式で反発力の大きさを計算
        float repulsiveForce = 0f;
        foreach (var obPos in nearestPosList)
        {
            float distance = (point - obPos).magnitude;
            if (distance > 0.01f) // ゼロ除算を回避
            {
                // 距離の逆数で反発力を表現（近いほど大きい）
                repulsiveForce += 1f / distance;
            }
        }

        return repulsiveForce;
    }

    /// <summary>
    /// 点での見出しコストを計算
    ///
    /// 論文 Eq. 17 を実装： J_Heading = h0 × 0.5 × (1 - (F_red · θ) / (||F_red|| ||θ||))
    ///
    /// 見出しコストとは：
    /// - 軌跡の向きと反発力の向きの一致度
    /// - 軌跡が反発力の方向と揃っているほどコストが低い
    /// - つまり、「障害物から遠ざかる向き」を推奨
    /// </summary>
    private float CalculateHeadingCost(
        int pointIndex,
        Trajectory trajectory,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        Vector2 point = trajectory.points[pointIndex];

        // 反発力の方向を計算
        Vector2 F_red = CalculateRepulsiveForceVector(point, space, redirectedAvatars, currentUserIndex, currentAvatarId);

        // この点での軌跡の向きを取得
        Vector2 heading;
        if (pointIndex < trajectory.points.Count - 1)
        {
            // 次の点への方向
            heading = (trajectory.points[pointIndex + 1] - trajectory.points[pointIndex]).normalized;
        }
        else if (pointIndex > 0)
        {
            // 前の点からの方向
            heading = (trajectory.points[pointIndex] - trajectory.points[pointIndex - 1]).normalized;
        }
        else
        {
            return 0f; // 単一点の軌跡
        }

        // 内積を計算
        float F_red_mag = F_red.magnitude;
        float heading_mag = heading.magnitude;

        if (F_red_mag < 0.001f || heading_mag < 0.001f)
        {
            return 0f; // ゼロ除算を回避
        }

        float dotProduct = Vector2.Dot(F_red, heading);
        // 正規化された内積（-1～1の範囲）
        float normalizedDot = dotProduct / (F_red_mag * heading_mag);

        // Eq. 17：内積が1（完全に同じ向き）ならコスト0、-1（逆向き）ならコスト最大
        float J_Heading = headingCostWeight * 0.5f * (1f - normalizedDot);

        return Mathf.Max(0f, J_Heading); // 負の値にならないように
    }

    /// <summary>
    /// 点での反発力ベクトルを計算（見出しコスト用）
    ///
    /// CalculateAPFCostとの違い：
    /// - APFコスト：大きさ（スカラー）を返す
    /// - こちら：方向（ベクトル）を返す
    /// </summary>
    private Vector2 CalculateRepulsiveForceVector(
        Vector2 point,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        List<Vector2> nearestPosList = new List<Vector2>();

        // Physical borders' contributions
        for (int i = 0; i < space.trackingSpace.Count; i++)
        {
            var p = space.trackingSpace[i];
            var q = space.trackingSpace[(i + 1) % space.trackingSpace.Count];
            var nearestPos = Utilities.GetNearestPos(point, new List<Vector2> { p, q });
            var n = Utilities.RotateVector(q - p, -90).normalized;
            var d = point - nearestPos;

            if (Vector2.Dot(n, d.normalized) > 0)
            {
                nearestPosList.Add(nearestPos);
            }
        }

        // Obstacle contributions
        foreach (var obstacle in space.obstaclePolygons)
        {
            var nearestPos = Utilities.GetNearestPos(point, obstacle);
            nearestPosList.Add(nearestPos);
        }

        // Other users as point obstacles
        foreach (var user in redirectedAvatars)
        {
            var userMovementManager = user.GetComponent<MovementManager>();
            if (userMovementManager.physicalSpaceIndex != currentUserIndex)
            {
                continue;
            }

            var uId = userMovementManager.avatarId;
            if (uId == currentAvatarId)
                continue;

            var userRedirectionManager = user.GetComponent<RedirectionManager>();
            var nearestPos = Utilities.FlattenedPos2D(userRedirectionManager.currPosReal);
            nearestPosList.Add(nearestPos);
        }

        // 負の勾配（反発力の方向）を計算
        Vector2 ng = Vector2.zero;
        foreach (var obPos in nearestPosList)
        {
            Vector2 diff = point - obPos;
            float distance = diff.magnitude;

            if (distance > 0.01f) // ゼロ除算を回避
            {
                // 勾配の寄与： g = -1/d^2 * (p - o) / ||p - o||
                Vector2 gDelta = -1f / distance * diff.normalized;
                ng += -gDelta; // 負の勾配
            }
        }

        return ng;
    }

    /// <summary>
    /// 点でのリセットコストを計算
    ///
    /// 論文 Eq. 18 を実装： J_Reset = トラッキング空間外なら1000、内側なら0
    ///
    /// リセットコストとは：
    /// - トラッキング空間の外に出た場合の大きなペナルティ
    /// - 1000という大きな値で、絶対に範囲外に出ないようにする
    /// </summary>
    private float CalculateResetCost(Vector2 point, SingleSpace space)
    {
        bool insideTrackingSpace = Utilities.IfPosInPolygon(space.trackingSpace, point);

        return insideTrackingSpace ? 0f : RESET_PENALTY;
    }

    /// <summary>
    /// オプション：リダイレクションアクションのゲインコストを計算
    ///
    /// 現在の論文実装では未使用：
    /// - 極端なゲインにペナルティを与えることで、控えめなリダイレクションを推奨
    /// - 今のところ0を返す（使用しない）
    /// </summary>
    private float CalculateGainCost(RedirectionAction action)
    {
        // 将来的には極端なゲインにペナルティを与えることも可能
        // 現在は0を返す（未使用）
        return 0f;
    }

    /// <summary>
    /// 設定からコストパラメータを更新
    ///
    /// 実行時の調整：
    /// - GlobalConfigurationの値が変わった場合に呼び出す
    /// </summary>
    public void UpdateParameters(GlobalConfiguration config)
    {
        this.discountFactor = config.discountFactor;
        this.headingCostWeight = config.headingCostWeight;
    }
}
