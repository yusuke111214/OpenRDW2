using UnityEngine;

/// <summary>
/// リダイレクションゲインの種類を定義
///
/// ゲインとは：
/// - ユーザーの動きを拡大/縮小する倍率のこと
/// - VR空間での動きと物理空間での動きに差をつけることで、
///   狭い部屋でも広いVR空間を歩いているように錯覚させます
/// </summary>
public enum RedirectionGainType
{
    Translation,        // 並進ゲイン（前進の速さを変える）
    Rotation,          // 回転ゲイン（頭の回転を変える）
    Curvature,         // 曲率ゲイン（歩く軌跡を曲げる）
    Combined,          // 複合（並進+曲率を組み合わせ、論文通りの実装）
    Null              // ゲインなし（通常の動き）
}

/// <summary>
/// リダイレクションアクション（1つの操作）を表すクラス
///
/// このクラスの役割：
/// - どの種類のゲインを、どれくらいの強さで適用するかを保存
/// - 予測的RDWアルゴリズムで、複数の選択肢を評価する際に使用
/// </summary>
public class RedirectionAction
{
    /// <summary>
    /// 適用するゲインの種類
    /// </summary>
    public RedirectionGainType gainType;

    /// <summary>
    /// 主要なゲイン値
    ///
    /// 種類別の意味：
    /// - Translation（並進）: 倍率（例：0.86～1.26）
    ///   → 1.0より大きいと速く、小さいと遅く移動
    /// - Rotation（回転）: 倍率（例：0.8～1.49）
    ///   → 1.0より大きいと速く、小さいと遅く回転
    /// - Curvature（曲率）: 曲がり具合の値（例：-7.5～7.5）
    ///   → 正の値で右、負の値で左に曲がる
    /// </summary>
    public float primaryValue;

    /// <summary>
    /// 副次的なゲイン値（Combinedタイプで使用）
    ///
    /// Combinedの場合：
    /// - primaryValue = 並進ゲイン
    /// - secondaryValue = 曲率ゲイン
    /// </summary>
    public float secondaryValue;

    /// <summary>
    /// コンストラクタ（単一値のアクション用）
    ///
    /// 使用例：
    /// - Translation、Rotation、Curvatureなど、値が1つだけのゲイン
    /// </summary>
    public RedirectionAction(RedirectionGainType type, float value)
    {
        this.gainType = type;
        this.primaryValue = value;
        this.secondaryValue = 0f;
    }

    /// <summary>
    /// コンストラクタ（複合アクション用：並進+曲率）
    ///
    /// 使用例：
    /// - Combinedタイプで、並進と曲率を同時に適用する場合
    /// </summary>
    public RedirectionAction(RedirectionGainType type, float translationGain, float curvatureGain)
    {
        this.gainType = type;
        this.primaryValue = translationGain;
        this.secondaryValue = curvatureGain;
    }

    /// <summary>
    /// ヌルアクション（何もしない）を作成
    ///
    /// 用途：
    /// - リダイレクションを適用しない場合
    /// - すべてのゲインを1.0に設定（通常の動き）
    /// </summary>
    public static RedirectionAction CreateNullAction()
    {
        return new RedirectionAction(RedirectionGainType.Null, 1f);
    }

    /// <summary>
    /// 注意：アクションの適用はリダイレクター（PredRedLPP_Redirector等）が行います
    ///
    /// 理由：
    /// - SetTranslationGain/SetRotationGain/SetCurvatureはprotectedメソッド
    /// - このクラスからは直接呼び出せないため、データの保存のみ行います
    /// </summary>

    /// <summary>
    /// このアクションのコピーを作成
    ///
    /// 用途：
    /// - アクションを変更せず、別の処理で使いたい場合
    /// </summary>
    public RedirectionAction Clone()
    {
        return new RedirectionAction(gainType, primaryValue, secondaryValue);
    }

    /// <summary>
    /// デバッグ用の文字列表現
    ///
    /// 出力例：
    /// - "Translation: 1.200"
    /// - "Combined: T=1.100, C=0.500"
    /// </summary>
    public override string ToString()
    {
        if (gainType == RedirectionGainType.Combined)
        {
            return $"{gainType}: T={primaryValue:F3}, C={secondaryValue:F3}";
        }
        else if (gainType == RedirectionGainType.Null)
        {
            return "Null Action";
        }
        else
        {
            return $"{gainType}: {primaryValue:F3}";
        }
    }

    /// <summary>
    /// 2つのアクションが等価かチェック
    ///
    /// 判定基準：
    /// - ゲインタイプが同じ
    /// - primaryValueとsecondaryValueがほぼ同じ（誤差0.0001以内）
    /// </summary>
    public bool IsEquivalentTo(RedirectionAction other)
    {
        if (other == null) return false;
        if (gainType != other.gainType) return false;

        const float epsilon = 0.0001f;
        bool primaryMatch = Mathf.Abs(primaryValue - other.primaryValue) < epsilon;
        bool secondaryMatch = Mathf.Abs(secondaryValue - other.secondaryValue) < epsilon;

        return primaryMatch && secondaryMatch;
    }
}

/// <summary>
/// アクションセットを生成するファクトリークラス
///
/// ファクトリーとは：
/// - オブジェクトを作成する専門のクラス
/// - ここでは、予測的RDWで使用する全てのアクションを一括生成します
/// </summary>
public static class RedirectionActionFactory
{
    /// <summary>
    /// PredRedLPP論文に基づいたアクションセットUを生成
    ///
    /// 生成内容：
    /// - 各ゲインタイプについて、6つの均等な値を生成
    /// - 値は「気づかれにくい範囲」（閾値内）に収まるように設定
    ///
    /// 例：Translation（並進）の場合
    /// - MIN_TRANS_GAIN（0.86）からMAX_TRANS_GAIN（1.26）まで
    /// - 6段階：0.86, 0.94, 1.02, 1.10, 1.18, 1.26
    /// </summary>
    /// <param name="config">ゲイン閾値を含むグローバル設定</param>
    /// <returns>リダイレクションアクションのリスト</returns>
    public static System.Collections.Generic.List<RedirectionAction> GenerateActionSet(GlobalConfiguration config)
    {
        var actions = new System.Collections.Generic.List<RedirectionAction>();

        // ゲインタイプごとの離散値の数（論文に基づく）
        const int numSteps = 6;

        // 並進ゲイン（最小値から最大値まで6段階）
        for (int i = 0; i < numSteps; i++)
        {
            // t: 0～1の範囲で均等分割（0, 0.2, 0.4, 0.6, 0.8, 1.0）
            float t = (float)i / (numSteps - 1);
            // Lerp: tの値で最小値と最大値を線形補間
            float gt = Mathf.Lerp(config.MIN_TRANS_GAIN, config.MAX_TRANS_GAIN, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Translation, gt));
        }

        // 回転ゲイン（最小値から最大値まで6段階）
        for (int i = 0; i < numSteps; i++)
        {
            float t = (float)i / (numSteps - 1);
            float gr = Mathf.Lerp(config.MIN_ROT_GAIN, config.MAX_ROT_GAIN, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Rotation, gr));
        }

        // 曲率ゲイン（-1/Rから+1/Rまで6段階）
        // 負の値=左曲がり、正の値=右曲がり
        for (int i = 0; i < numSteps; i++)
        {
            float t = (float)i / (numSteps - 1);
            float gc = Mathf.Lerp(-1f / config.CURVATURE_RADIUS, 1f / config.CURVATURE_RADIUS, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Curvature, gc));
        }

        // 複合ゲイン（並進+曲率）- 論文 Section 3.3.1, Table 2
        // "a combination of translation and curvature gains (Grechkin et al., 2016)"
        // 論文準拠のため、3段階の複合ゲインを追加
        for (int i = 0; i < 3; i++)
        {
            float t = (float)i / 2f;
            float gt = Mathf.Lerp(config.MIN_TRANS_GAIN, config.MAX_TRANS_GAIN, t);
            float gc = Mathf.Lerp(-1f / config.CURVATURE_RADIUS, 1f / config.CURVATURE_RADIUS, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Combined, gt, gc));
        }

        // ヌルリダイレクション（常に含める）
        // 何もしない選択肢を用意することで、無理なリダイレクションを避ける
        actions.Add(RedirectionAction.CreateNullAction());

        return actions;
    }

    /// <summary>
    /// テスト用の最小限のアクションセットを生成
    ///
    /// 通常版との違い：
    /// - 各ゲインタイプにつき、最小値と最大値の2つだけ
    /// - 計算を高速化したい場合や、動作確認用に使用
    /// </summary>
    public static System.Collections.Generic.List<RedirectionAction> GenerateMinimalActionSet(GlobalConfiguration config)
    {
        var actions = new System.Collections.Generic.List<RedirectionAction>();

        // 各ゲインタイプについて最小値と最大値のみ
        actions.Add(new RedirectionAction(RedirectionGainType.Translation, config.MIN_TRANS_GAIN));
        actions.Add(new RedirectionAction(RedirectionGainType.Translation, config.MAX_TRANS_GAIN));

        actions.Add(new RedirectionAction(RedirectionGainType.Rotation, config.MIN_ROT_GAIN));
        actions.Add(new RedirectionAction(RedirectionGainType.Rotation, config.MAX_ROT_GAIN));

        actions.Add(new RedirectionAction(RedirectionGainType.Curvature, -1f / config.CURVATURE_RADIUS));
        actions.Add(new RedirectionAction(RedirectionGainType.Curvature, 1f / config.CURVATURE_RADIUS));

        actions.Add(RedirectionAction.CreateNullAction());

        return actions;
    }
}
