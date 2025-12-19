using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// パス予測アルゴリズムのインターフェース（予測的RDWで使用）
///
/// インターフェースとは：
/// - クラスが実装すべきメソッドを定義する「契約書」のようなもの
/// - このインターフェースを実装することで、異なる予測方法を統一的に扱えます
///
/// 実装クラス：
/// - LemniscatePathPredictor (LPP)：レムニスケート形状を使った予測
/// - ForwardPathPredictor (FPP)：前方向への単純な予測
/// </summary>
public interface IPathPredictor
{
    /// <summary>
    /// 移動履歴に基づいて予測軌跡を生成
    ///
    /// 処理内容：
    /// - 過去の移動パターンを分析
    /// - ユーザーがこれから歩きそうな経路を複数予測
    /// - 各予測をTrajectoryオブジェクトとして返す
    /// </summary>
    /// <param name="positionHistory">過去の位置履歴（最新が最後）</param>
    /// <param name="directionHistory">過去の向き履歴（最新が最後）</param>
    /// <param name="currentPosition">現在の物理空間での位置</param>
    /// <param name="currentDirection">現在の物理空間での向き</param>
    /// <returns>予測された軌跡のリスト</returns>
    List<Trajectory> GenerateTrajectories(
        Queue<Vector3> positionHistory,
        Queue<Vector3> directionHistory,
        Vector3 currentPosition,
        Vector3 currentDirection
    );

    /// <summary>
    /// 仮想障害物と衝突する軌跡を除外（シーン認識）
    ///
    /// シーン認識とは：
    /// - VR空間内の障害物（壁、オブジェクト）の位置を考慮すること
    /// - ユーザーが障害物に向かって歩く予測は除外します
    /// </summary>
    /// <param name="trajectories">候補となる軌跡のリスト</param>
    /// <param name="space">障害物を含む物理空間</param>
    /// <returns>実行可能な（衝突のない）軌跡のリスト</returns>
    List<Trajectory> FilterFeasibleTrajectories(
        List<Trajectory> trajectories,
        SingleSpace space
    );

    /// <summary>
    /// 予測ホライゾン（予測する先の距離）を取得
    ///
    /// 予測ホライゾンとは：
    /// - どれだけ先まで予測するか（メートル単位）
    /// - 例：3mなら現在位置から3m先まで予測
    /// </summary>
    float GetPredictionHorizon();

    /// <summary>
    /// 予測ホライゾンを設定
    /// </summary>
    void SetPredictionHorizon(float horizon);
}
