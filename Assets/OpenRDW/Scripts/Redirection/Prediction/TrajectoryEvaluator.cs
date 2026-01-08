using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PredRedLPPアルゴリズムのコスト関数を使った軌跡評価器
///
/// 論文のコスト関数を実装（Section 3.3.3）：
/// - APFコスト（Eq. 16）：障害物との距離
///   → 非調和APF（Eq. 11）を使用：F_rep,i = a_o × exp(-b_o × d_o²)  (if d_o ≤ d_d)
/// - 見出しコスト（Eq. 17）：軌跡の向きの適切さ → 論文では0に設定
/// - リセットコスト（Eq. 18）：範囲外に出るペナルティ
/// - ゲインコスト：ゲインの大きさのペナルティ → 論文では0に設定
/// - 総コスト（Eq. 14-15）：割引率を適用した合計
///
/// 論文Section 3.3.3より：
/// "JGain,i and JHeading,i were set to 0 for simplicity"
///
/// コストとは：
/// - 各軌跡の「悪さ」を数値化したもの
/// - 値が小さいほど良い軌跡
/// - 複数の要素（障害物、向き、範囲）を組み合わせて評価
///
/// APF方式：
/// - 非調和APF（anharmonic APF）を使用（論文準拠、Eq. 11）
/// - ThomasAPF（1/d方式）から変更（問題4、問題5への対処）
/// - 閾値距離を超えると反発力が0になり、安定点通過後の挙動が改善される
///
/// 論文: "Predictive multiuser redirected walking using artificial potential fields" (Hirt et al., 2024)
/// </summary>
public class TrajectoryEvaluator
{
    private GlobalConfiguration globalConfiguration;

    // コストパラメータ
    private float discountFactor; // α（論文）- 割引率、通常0.8
    private float headingCostWeight; // h0（論文）- 見出しコストの重み、論文では0に設定（将来の拡張用）
    private const float RESET_PENALTY = 1000f; // 範囲外ペナルティ（大きな値）

    // 非調和APFパラメータ（論文Eq. 11）
    private float apfMaxValue; // a_o - 最大APF値
    private float apfDistributionWidth; // b_o - 分布幅
    private float apfThresholdDistance; // d_d - 閾値距離

    // デバッグ用フラグ
    private bool enableDebugLog = false;

    /// <summary>
    /// コンストラクタ（設定ファイルから初期化）
    ///
    /// パラメータ読み込み：
    /// - 割引率（discountFactor）
    /// - 見出しコストの重み（headingCostWeight）
    /// - 非調和APFパラメータ（apfMaxValue, apfDistributionWidth, apfThresholdDistance）
    /// </summary>
    public TrajectoryEvaluator(GlobalConfiguration config)
    {
        this.globalConfiguration = config;
        this.discountFactor = config.discountFactor;
        this.headingCostWeight = config.headingCostWeight;
        this.apfMaxValue = config.apfMaxValue;
        this.apfDistributionWidth = config.apfDistributionWidth;
        this.apfThresholdDistance = config.apfThresholdDistance;
    }

    /// <summary>
    /// 全てのアクション-軌跡の組み合わせを評価し、最良のものを返す（論文準拠版）
    ///
    /// 論文のロジック（Section 3.3）：
    /// 1. 各予測軌跡 T_pred に対して
    /// 2. 各アクション π ∈ U に対して
    /// 3. π を T_pred に適用して T_red を生成
    /// 4. T_red のコストを計算
    /// 5. 最小コストの(T_pred, π)ペアを選択
    ///
    /// 重要な実装詳細：
    /// - Curvature gainは物理空間での軌跡を変更する
    /// - Translation/Rotation gainは軌跡を変更しないが、将来的にJ_Gain,iで評価可能
    /// - 現在の実装ではJ_Gain,i=0なので、Curvature gainのみが実質的にコストに影響
    /// </summary>
    /// <param name="predictions">予測された軌跡のリスト（T_pred）</param>
    /// <param name="actions">リダイレクションアクションのリスト（U）</param>
    /// <param name="physicalSpace">現在の物理空間</param>
    /// <param name="redirectedAvatars">他のユーザーのリスト（APF計算用）</param>
    /// <param name="currentUserIndex">現在のユーザーの物理空間インデックス</param>
    /// <param name="currentAvatarId">現在のユーザーのアバターID</param>
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
        Trajectory bestRedirectedTrajectory = null;

        // 各予測軌跡を評価
        foreach (var trajectory in predictions)
        {
            // 各アクションを評価
            foreach (var action in actions)
            {
                // アクションを軌跡に適用してT_redを生成
                Trajectory T_red = ApplyActionToTrajectory(trajectory, action);

                // T_redのコストを計算
                float actionCost = CalculateTotalCost(
                    T_red,
                    physicalSpace,
                    redirectedAvatars,
                    currentUserIndex,
                    currentAvatarId
                );

                // オプション：ゲインコストを追加（現在は論文でJ_Gain,i=0）
                // actionCost += CalculateGainCost(action);

                // 最小コストを更新
                if (actionCost < minCost)
                {
                    minCost = actionCost;
                    bestAction = action;
                    bestTrajectory = trajectory; // 元の予測軌跡
                    bestRedirectedTrajectory = T_red; // リダイレクト後の軌跡
                }
            }
        }

        // 有効な軌跡が見つからない場合はヌルアクションにフォールバック
        if (bestAction == null)
        {
            bestAction = RedirectionAction.CreateNullAction();
            if (predictions.Count > 0)
            {
                bestTrajectory = predictions[0];
            }
        }

        // 最良の軌跡のコストを記録（可視化やデバッグ用）
        if (bestRedirectedTrajectory != null)
        {
            bestTrajectory.totalCost = minCost;
        }

        return (bestAction, bestTrajectory);
    }

    /// <summary>
    /// 単一の軌跡に対して全てのアクションを評価し、最良のものを返す（論文準拠版）
    ///
    /// 論文Section 3.3（page 6）より：
    /// "Conceptually, the predictive RDW entails a simple approach:
    /// • it predicts a single path Tpred;
    /// • Tpred is redirected based on an action set U consisting of multiple
    ///   redirection techniques and different gains, resulting in Tred;
    /// • a cost-based analysis of Tred is used to identify the best redirection πoptimal ∈ U"
    ///
    /// 論文の2段階選択プロセス（Section 3.2.2 + 3.3）：
    /// Phase 1: Path similarity measureでT_predを選択（呼び出し側で実施済み）
    /// Phase 2: T_predに各アクション π ∈ U を適用してコスト評価（このメソッド）
    ///
    /// 計算量（論文準拠）：
    /// - このメソッド: N_actions回のコスト計算
    /// - 2段階プロセス全体: N_trajectories + N_actions（論文通り）
    ///
    /// 処理フロー：
    /// 1. T_predに対して
    /// 2. 各アクション π ∈ U に対して
    /// 3. π を T_pred に適用して T_red を生成
    /// 4. T_red のコストを計算
    /// 5. 最小コストのπを選択
    /// </summary>
    /// <param name="T_pred">選択された最良予測軌跡</param>
    /// <param name="actions">リダイレクションアクションのリスト（U）</param>
    /// <param name="physicalSpace">現在の物理空間</param>
    /// <param name="redirectedAvatars">他のユーザーのリスト（APF計算用）</param>
    /// <param name="currentUserIndex">現在のユーザーの物理空間インデックス</param>
    /// <param name="currentAvatarId">現在のユーザーのアバターID</param>
    /// <returns>最良のアクションと対応する軌跡のタプル</returns>
    public (RedirectionAction bestAction, Trajectory bestTrajectory) EvaluateActionsForSingleTrajectory(
        Trajectory T_pred,
        List<RedirectionAction> actions,
        SingleSpace physicalSpace,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        if (T_pred == null)
        {
            return (RedirectionAction.CreateNullAction(), null);
        }

        float minCost = float.MaxValue;
        RedirectionAction bestAction = null;
        Trajectory bestRedirectedTrajectory = null;

        // 各アクションを評価
        foreach (var action in actions)
        {
            // アクションを軌跡に適用してT_redを生成
            Trajectory T_red = ApplyActionToTrajectory(T_pred, action);

            // T_redのコストを計算
            float actionCost = CalculateTotalCost(
                T_red,
                physicalSpace,
                redirectedAvatars,
                currentUserIndex,
                currentAvatarId
            );

            // デバッグログ
            if (enableDebugLog)
            {
                UnityEngine.Debug.Log($"[TrajectoryEvaluator] Action: {action.ToString()}, Cost: {actionCost:F2}");
            }

            // オプション：ゲインコストを追加（現在は論文でJ_Gain,i=0）
            // actionCost += CalculateGainCost(action);

            // 最小コストを更新
            if (actionCost < minCost)
            {
                minCost = actionCost;
                bestAction = action;
                bestRedirectedTrajectory = T_red; // リダイレクト後の軌跡
            }
        }

        if (enableDebugLog)
        {
            UnityEngine.Debug.Log($"[TrajectoryEvaluator] Best action selected: {bestAction?.ToString() ?? "null"}, Min cost: {minCost:F2}");
        }

        // 有効なアクションが見つからない場合はヌルアクションにフォールバック
        if (bestAction == null)
        {
            bestAction = RedirectionAction.CreateNullAction();
        }

        // 最良の軌跡のコストを記録（可視化やデバッグ用）
        if (bestRedirectedTrajectory != null)
        {
            T_pred.totalCost = minCost;
        }

        return (bestAction, T_pred);
    }

    /// <summary>
    /// アクションを軌跡に適用してT_red（リダイレクト後の軌跡）を生成
    ///
    /// 論文の意図：
    /// - Curvature gainを適用すると、ユーザーの物理空間での実際の軌跡が曲がる
    /// - Translation/Rotation gainは物理空間での軌跡を変更しない
    /// - この「実際の軌跡」のAPFコストを評価することで、障害物回避性能を正しく評価できる
    /// </summary>
    /// <param name="trajectory">元の予測軌跡（T_pred）</param>
    /// <param name="action">適用するリダイレクションアクション（π）</param>
    /// <returns>アクションを適用した軌跡（T_red）</returns>
    private Trajectory ApplyActionToTrajectory(Trajectory trajectory, RedirectionAction action)
    {
        // Curvature gainの場合のみ軌跡を変更
        if (action.gainType == RedirectionGainType.Curvature)
        {
            // Curvatureを適用した新しい軌跡を生成
            return trajectory.ApplyCurvature(action.primaryValue);
        }
        else if (action.gainType == RedirectionGainType.Combined)
        {
            // Combined（Translation + Curvature）の場合、Curvature部分のみが軌跡に影響
            return trajectory.ApplyCurvature(action.secondaryValue);
        }
        else
        {
            // Translation, Rotation, Nullの場合は軌跡を変更しない
            // （物理空間での軌跡は同じ）
            return trajectory;
        }
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

            // 論文 Section 3.3.3（ページ7）：
            // "JGain,i and JHeading,i were set to 0 for simplicity"
            float J_Heading = 0f; // 論文準拠：簡潔さのため0に設定
            float J_Reset = CalculateResetCost(point, space);
            float J_Gain = 0f; // 論文準拠：簡潔さのため0に設定

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
    /// 論文 Eq. 11 の非調和APFを使用： F_rep,i = a_o × exp(-b_o × d_o²) × e_o  (if d_o ≤ d_d)
    ///
    /// APFコストとは：
    /// - その点での反発力の大きさ
    /// - 障害物に近いほど大きくなる
    /// - 非調和APF（anharmonic APF）方式を使用（論文準拠）
    ///
    /// 非調和APFの特徴：
    /// - 閾値距離d_dを超えると反発力が完全に0
    /// - 指数関数的減衰により、壁に近づくと反発力が爆発的に増加
    /// - 局所的影響：閾値内の障害物のみが影響する
    ///
    /// 計算手順（重要）：
    /// 1. 各障害物iから反発力ベクトル F_rep,i を計算
    /// 2. すべてのF_rep,iをベクトル合成して F_red を得る
    /// 3. F_redのノルム（大きさ）を返す
    /// ※スカラー値の単純な足し算ではなく、ベクトル合成が必要
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

        // 非調和APF方式で反発力ベクトルを計算してから大きさを取得
        // 論文Eq. 16: J_APF = ||F_red||
        // F_red = Σ F_rep,i （全障害物からの反発力ベクトルの合計）
        Vector2 repulsiveForceVector = Vector2.zero;
        foreach (var obPos in nearestPosList)
        {
            Vector2 diff = point - obPos;
            float distance = diff.magnitude;

            if (distance > 0.01f && distance <= apfThresholdDistance) // ゼロ除算を回避、閾値内のみ
            {
                // 非調和APF（Eq. 11）: F_rep,i = a_o × exp(-b_o × d_o²) × e_o
                // e_o: 障害物から点への単位方向ベクトル
                float forceMagnitude = apfMaxValue * Mathf.Exp(-apfDistributionWidth * distance * distance);
                Vector2 forceDirection = diff.normalized;
                repulsiveForceVector += forceMagnitude * forceDirection;
            }
            // distance > apfThresholdDistance の場合は force = 0（追加しない）
        }

        // ベクトルの大きさ（ノルム）を返す
        return repulsiveForceVector.magnitude;
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
    ///
    /// 非調和APF（Eq. 11）を使用：
    /// F_rep = a_o × exp(-b_o × d²) × (p - o).normalized  (if d ≤ d_d)
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

        // 反発力ベクトルを計算（非調和APF、CalculateAPFCostと同じパラメータを使用）
        Vector2 repulsiveForceVector = Vector2.zero;
        foreach (var obPos in nearestPosList)
        {
            Vector2 diff = point - obPos;
            float distance = diff.magnitude;

            if (distance > 0.01f && distance <= apfThresholdDistance) // ゼロ除算を回避、閾値内のみ
            {
                // 非調和APF（Eq. 11）のベクトル形式
                // F_rep = a_o × exp(-b_o × d²) × (p - o).normalized
                float forceMagnitude = apfMaxValue * Mathf.Exp(-apfDistributionWidth * distance * distance);
                Vector2 forceDirection = diff.normalized;
                repulsiveForceVector += forceMagnitude * forceDirection;
            }
            // distance > apfThresholdDistance の場合は force = 0（追加しない）
        }

        return repulsiveForceVector;
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
    /// - インスペクターでパラメータを調整した際に反映される
    /// </summary>
    public void UpdateParameters(GlobalConfiguration config)
    {
        this.discountFactor = config.discountFactor;
        this.headingCostWeight = config.headingCostWeight;
        this.apfMaxValue = config.apfMaxValue;
        this.apfDistributionWidth = config.apfDistributionWidth;
        this.apfThresholdDistance = config.apfThresholdDistance;
    }
}
