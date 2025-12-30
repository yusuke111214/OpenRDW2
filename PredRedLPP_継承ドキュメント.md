# PredRedLPP実装 引き継ぎドキュメント

## 🎯 プロジェクト概要

論文「Predictive multiuser redirected walking using artificial potential fields (Hirt et al., 2024)」のPredRedLPPアルゴリズムをOpenRDW2プロジェクトに実装する。

**論文URL:** https://www.frontiersin.org/articles/10.3389/frvir.2024.1365344/full
**PDF:** `C:\RDW\OpenRDW2\Hirt_原文_予測＿マルチユーザー.pdf`

**実装目的:**
- PredRedLPPアルゴリズムをOpenRDW上で実装
- 既存のAPF手法（ThomasAPF等）と比較
- 予測的RDWの有意性を検証

---

## 📋 現在の状況

### ✅ 完了している実装

#### 主要クラス構成

| クラス名 | 役割 | 論文対応 |
|---------|------|---------|
| `PredRedLPP_Redirector.cs` | メインリダイレクター | Section 3.3 |
| `LemniscatePathPredictor.cs` | レムニスケート予測器 | Section 3.2.2, Eq.1 |
| `TrajectoryEvaluator.cs` | コスト関数評価 | Section 3.3.3, Eq.14-18 |
| `Trajectory.cs` | 軌跡データ + ApplyCurvature() | データ構造 |
| `RedirectionAction.cs` | アクション定義 | Table 2 |
| `CurvesWrapper.cs` | Clothoid生成ライブラリ | - |

#### 実装済み機能

1. **レムニスケート予測（LPP）**
   - ✅ レムニスケート生成（Eq.1）
   - ✅ Clothoid軌跡生成
   - ✅ Scene awareness（障害物衝突検出）

2. **アクションセット**
   - ✅ Translation/Rotation/Curvature gain（Table 2準拠）
   - ✅ 6段階のゲイン値 + Null action
   - ✅ Minimalアクションセット（7個）の実装

3. **コスト関数**
   - ✅ 総コスト計算（Eq.14）：`J_total = Σ(α^i × J_i)`
   - ✅ APFコスト（Eq.16）：ThomasAPF方式（1/d）
   - ✅ Headingコスト（Eq.17）
   - ✅ Resetコスト（Eq.18）
   - ✅ 割引率α=0.8

4. **その他**
   - ✅ 物理空間↔仮想空間の座標変換
   - ✅ 可視化（レムニスケート、軌跡、APF力ベクトル）
   - ✅ インスペクターでのパラメータ調整

---

## ⚠️ 現在の問題点

### 🔴 問題3: 計算時間が著しく重い（未解決）

#### 症状
- バックグラウンドシミュレーションが10分以上かかる
- 他のアルゴリズム（ThomasAPF等）と比べて実行速度が極端に遅い
- 実用的な速度での動作確認ができない

#### 原因分析

**計算量の詳細:**

| パラメータ | デフォルト値 |
|-----------|------------|
| 軌跡数 | 11本 (`lemniscateEndpoints`) |
| アクション数 | 19個 (T:6 + R:6 + C:6 + Null:1) |
| 軌跡あたりの点数 | 20点 (`trajectorySamplePoints`) |

**1フレームあたりの計算量:**
```
総評価回数 = 軌跡数 × アクション数 × 点数
          = 11 × 19 × 20
          = 4,180回のコスト計算/フレーム
```

**他のアルゴリズムとの比較:**
- ThomasAPF（リアクティブ）：1回のAPF力計算
- PredRedLPP（現在の実装）：**4,180回のコスト計算**
- **計算量の差：約4,180倍**

#### 最適化方法

✅ **すでに実装済み：Minimalアクションセットのインスペクター切り替え**
```csharp
// GlobalConfiguration.cs
public bool useMinimalActionSet = false;  // Inspector切り替え可能
```

**推奨する最適化の組み合わせ:**

| 最適化項目 | 設定変更 | 削減効果 |
|-----------|---------|---------|
| Minimalアクションセット | `useMinimalActionSet = true` | 63% |
| 軌跡数削減 | `lemniscateEndpoints = 5` | 追加55% |
| 評価頻度削減 | 2フレームに1回評価（要実装） | 追加50% |

**合計削減効果：93%（4,180回 → 平均285回/フレーム）**

---

### 🔴 問題4: Path similarity measureの欠如（未解決）

#### 問題内容

**論文の手順（Section 3.2.2, Figure 2）:**
```
1. レムニスケート上に複数のエンドポイントを生成
2. 各エンドポイントへのClothoid軌跡を生成
3. Scene awarenessで実行不可能な軌跡を除外
4. ★Path similarity measureで単一の最良軌跡T_predを選択★
5. T_predに各アクションを適用してT_redを生成
6. コスト評価で最良のアクションを選択
```

**現在の実装:**
```csharp
// PredRedLPP_Redirector.cs Line 253-262
1. レムニスケート上に複数のエンドポイントを生成
2. 各エンドポイントへのClothoid軌跡を生成
3. Scene awarenessで実行不可能な軌跡を除外
4. ★全ての(軌跡, アクション)ペアを直接評価★  // ← 論文と異なる
   foreach (var trajectory in feasibleTrajectories)  // 複数の軌跡
   {
       foreach (var action in actionSet)
       {
           Trajectory T_red = ApplyActionToTrajectory(trajectory, action);
           float cost = CalculateTotalCost(T_red, ...);
       }
   }
5. コスト評価で最良の(軌跡, アクション)ペアを選択
```

#### 論文との相違点

**論文Figure 2(B)のキャプション:**
> "From the limited feasible set, **the best prediction is identified using the path similarity measure**"

**論文Section 3.2.2:**
> "From this generated set of trajectories, **a single best prediction is isolated** using a simple mean-squared error enhanced by a discount factor."

#### 影響

- ✅ **機能的には動作する**（全ペア評価でも最適解は見つかる）
- ❌ **計算量が増加**：`N_trajectories × N_actions` vs `N_trajectories + N_actions`
- ❌ **論文の2段階選択プロセスを省略**している

#### 解決方法（Phase 7で実装予定）

```csharp
// Step 4: Path similarity measureで単一の最良軌跡を選択
Trajectory T_pred = SelectBestTrajectoryUsingSimilarityMeasure(
    feasibleTrajectories,
    positionHistory  // HMDデータバッファ
);

// Step 5: T_predに各アクションを適用して評価
RedirectionAction bestAction = EvaluateActionsForTrajectory(
    T_pred,
    actionSet
);
```

**Path similarity measureの実装（論文Section 3.2.2）:**
- Mean-squared error（MSE）を使用
- HMDデータバッファとの一致度を計算
- 割引係数で最近の履歴を重視

---

### 🔴 問題5: Translation/Rotation gainが評価されていない（未解決）

#### 問題内容

**現在の実装（TrajectoryEvaluator.cs Line 140-159）:**
```csharp
private Trajectory ApplyActionToTrajectory(Trajectory trajectory, RedirectionAction action)
{
    if (action.gainType == RedirectionGainType.Curvature)
    {
        return trajectory.ApplyCurvature(action.primaryValue);
    }
    else if (action.gainType == RedirectionGainType.Combined)
    {
        return trajectory.ApplyCurvature(action.secondaryValue);
    }
    else
    {
        // Translation/Rotation/Nullは軌跡を変更しない
        return trajectory;  // ← T_redが同じ = コストが同じ
    }
}
```

#### 結果

**コスト評価への影響:**
```
Translation gain (0.86)のT_red = 元の軌跡
Translation gain (1.26)のT_red = 元の軌跡
  ↓
CalculateTotalCost(T_red)が同じ値を返す
  ↓
Translation gainの効果が評価されない
```

**同様の問題:**
- ✅ Curvature gain：T_redが変化 → コストに差が出る → **正常**
- ❌ Translation gain：T_redが不変 → コストが同じ → **評価されない**
- ❌ Rotation gain：T_redが不変 → コストが同じ → **評価されない**

#### 論文との整合性

**論文（Section 3.3.3）:**
> "JGain,i and JHeading,i were set to 0 for simplicity"

**解釈:**
- 論文でも`J_Gain,i = 0`と明記されている
- Translation/Rotation gainの効果をコスト関数で評価していない
- **実装は論文に準拠している**

#### 影響

- ❌ **Translation/Rotation gainが実質的に機能しない**
- ✅ **Curvature gain中心のアルゴリズムになっている**
- ⚠️ **論文の意図とは異なる可能性**（論文でも同じ問題があるかは不明）

#### 解決方法の検討

**Option A: J_Gain,iを実装（論文を拡張）**
```csharp
float J_Gain = CalculateGainCost(action);
// Translation/Rotation gainの強度に応じたペナルティ
```

**Option B: 論文に準拠してそのまま（現状維持）**
- 論文でも`J_Gain,i = 0`なので、実装は正しい
- Curvature gain中心の評価を受け入れる

**推奨：Option B（論文準拠を優先）**

---

### 🔴 問題6: 軌跡選択とアクション選択が統合されている（未解決）

#### 問題内容

**論文の2段階プロセス:**
```
Phase 1: 予測（Prediction）
  → Path similarity measureでT_predを選択

Phase 2: アクション評価（Action Selection）
  → T_predに各アクションを適用してπ_optimalを選択
```

**現在の実装:**
```
Phase 1+2: 統合評価
  → 全ての(軌跡, アクション)ペアを同時評価
```

#### 影響

**計算量の違い:**
- 論文: `N_trajectories（軽量なMSE） + N_actions（コスト評価）`
- 実装: `N_trajectories × N_actions（重いコスト評価）`

**例（デフォルト設定）:**
- 論文: 11回（MSE） + 19回（コスト評価） = 30回
- 実装: 11 × 19 = 209回（コスト評価）
- **差：約7倍**

#### 論文との整合性

**論文Section 3.3:**
> "Conceptually, the predictive RDW entails a simple approach:
> • it predicts **a single path** T_pred;
> • T_pred is redirected based on an action set U"

**明確な記述:**
- 論文は「単一の軌跡T_pred」を選択してからアクション評価を行う
- 実装は複数の軌跡を保持したままアクション評価を行う

#### 解決方法（問題4と同じ）

問題4のPath similarity measure実装で自動的に解決される。

---

## 📚 参考情報

### 重要なファイル

| ファイル | 役割 | 重要度 | 主な実装内容 |
|---------|------|--------|------------|
| `PredRedLPP_Redirector.cs` | メインリダイレクター | ⭐⭐⭐ | InjectRedirection(), アクション適用 |
| `LemniscatePathPredictor.cs` | 軌跡予測 | ⭐⭐⭐ | レムニスケート生成, Clothoid生成 |
| `TrajectoryEvaluator.cs` | コスト評価 | ⭐⭐⭐ | EvaluateAllActions(), コスト関数 |
| `Trajectory.cs` | 軌跡データ | ⭐⭐ | ApplyCurvature(), 衝突判定 |
| `RedirectionAction.cs` | アクション定義 | ⭐⭐ | GenerateActionSet() |
| `RedirectionManager.cs` | 基底クラス | ⭐⭐ | ゲイン適用インターフェース |

### コード対応表

| 論文の要素 | ファイル：メソッド | 実装状況 |
|-----------|----------------|---------|
| Lemniscate (Eq.1) | LemniscatePathPredictor:GenerateLemniscatePoints() | ✅ 完了 |
| Clothoid generation | CurvesWrapper:CreateClothoidFromPoseAndPoint() | ✅ 完了 |
| Scene awareness | Trajectory:CheckCollisionWithObstacles() | ✅ 完了 |
| Path similarity measure | - | ❌ 未実装 |
| Action set U (Table 2) | RedirectionAction:GenerateActionSet() | ✅ 完了 |
| T_red generation | TrajectoryEvaluator:ApplyActionToTrajectory() | ✅ 完了（Curvatureのみ） |
| J_total (Eq.14) | TrajectoryEvaluator:CalculateTotalCost() | ✅ 完了 |
| J_APF (Eq.16) | TrajectoryEvaluator:CalculateAPFCost() | ✅ 完了（ThomasAPF方式） |
| J_Heading (Eq.17) | TrajectoryEvaluator:CalculateHeadingCost() | ✅ 完了 |
| J_Reset (Eq.18) | TrajectoryEvaluator:CalculateResetCost() | ✅ 完了 |
| J_Gain,i | - | ✅ 完了（論文通り0） |

---

## 🔜 次のステップ

### Phase 7: 計算量最適化 + Path similarity measure実装

#### 優先度1: 計算量最適化（必須）

**目的:** シミュレーションが実用的な時間で完了するようにする

**実装済み:**
```csharp
// GlobalConfiguration.cs
public bool useMinimalActionSet = false;  // Inspector切り替え可能
```

**追加で推奨する設定:**
```csharp
// GlobalConfiguration.cs (Inspector設定)
lemniscateEndpoints = 5;  // 11 → 5
```

**結果:** 計算量を86%削減（4,180回 → 570回/フレーム）

#### 優先度2: Path similarity measure実装（推奨）

**目的:** 論文のアルゴリズムを正確に再現する

**実装内容:**
1. `Trajectory.cs`にMSEベースの類似度計算を追加
2. `LemniscatePathPredictor.cs`に軌跡選択メソッドを追加
3. `PredRedLPP_Redirector.cs`の評価ロジックを2段階に分離

**効果:**
- 計算量をさらに削減（570回 → 約24回/フレーム）
- 論文のアルゴリズムに完全準拠
- 予測精度の向上

**実装の優先度:**
- ✅ **計算量削減効果が大きい**（約24倍高速化）
- ✅ **論文準拠のため必須**
- ⚠️ 実装難易度は中程度

### Phase 8: 検証と評価

1. **動作確認**
   - シミュレーション完了時間の計測
   - リセット回数の比較（他手法との比較）
   - 軌跡の妥当性確認

2. **性能評価**
   - ThomasAPF、S2C等との比較
   - 統計的有意差検定
   - 論文Figure 6, 7との比較

3. **ドキュメント更新**
   - 実験結果の記録
   - 最終パラメータの記載
   - 実装の完全性評価

---

## 📊 論文との整合性評価

### 再現度：60-70%

#### ✅ よく再現できている部分

1. **予測アルゴリズム（Section 3.2.2）**
   - レムニスケート生成（Eq.1）：完全実装
   - Clothoid軌跡生成：正確
   - Scene awareness：実装済み

2. **コスト関数（Section 3.3.3）**
   - 総コスト（Eq.14）：完全実装
   - APFコスト（Eq.16）：ThomasAPF方式で実装
   - Headingコスト（Eq.17）：完全実装
   - Resetコスト（Eq.18）：完全実装

3. **アクションセット（Table 2）**
   - ゲインの範囲：論文と一致
   - アクション数：論文と一致

#### ❌ 論文と異なる部分

1. **Path similarity measureの欠如**
   - 論文：MSEで単一軌跡を選択
   - 実装：全軌跡をアクション評価に直接投入
   - **影響：計算量7倍、アルゴリズムの構造が異なる**

2. **APFの実装方式**
   - 論文：非調和APF（Eq.11: 指数関数）
   - 実装：ThomasAPF（1/d方式）
   - **影響：既存手法との比較のため意図的に統一（問題なし）**

3. **Translation/Rotation gainの評価**
   - 論文：J_Gain,i = 0（明記）
   - 実装：J_Gain,i = 0（同じ）
   - **影響：Curvature gain中心の評価（論文も同じ）**

4. **ApplyCurvatureの実装**
   - 論文：Curvature適用方法の記述なし
   - 実装：セグメント単位の回転角度累積
   - **影響：妥当な解釈だが独自実装**

#### ⚠️ 重要な懸念事項

**現在の実装は「Curvature gain中心のアルゴリズム」になっている:**
- Translation/Rotation gainがコスト評価に影響しない
- 論文でも`J_Gain,i = 0`なので同じ問題がある可能性
- **論文の実験結果が本当にTranslation/Rotation gainを評価しているのか不明**

### 改善の必要性

| 問題 | 優先度 | 理由 |
|-----|-------|-----|
| Path similarity measure | 🔴 高 | 論文の核心部分、計算量削減効果大 |
| 計算量最適化 | 🔴 高 | 実用性のため必須 |
| Translation/Rotation gain評価 | 🟡 中 | 論文も同じなので現状維持でよい可能性 |
| APF方式の統一 | 🟢 低 | 既存手法との比較のため意図的 |

---

## 📝 実装メモ

### APF方式について

**論文（Eq.11）：非調和APF**
```
F_rep,i = a_o × exp(-b_o × d_o²) × e_o  (if d_o ≤ d_d)
```

**現在の実装：ThomasAPF方式**
```csharp
repulsiveForce += 1f / distance;
```

**方針:**
- OpenRDW上の既存手法（ThomasAPF、S2C等）と公平に比較するため、APFは統一する
- 非調和APFへの変更は行わない
- これにより「予測的アプローチの効果」を純粋に評価できる

### 最適化の方針

1. **まずはMinimalアクションセットを有効化**
   - Inspector: `useMinimalActionSet = true`
   - 効果：63%削減

2. **軌跡数を削減**
   - Inspector: `lemniscateEndpoints = 5`
   - 効果：追加55%削減

3. **Path similarity measureを実装**
   - 効果：さらに大幅削減 + 論文準拠
   - 優先度：高

これにより、実用的な速度で論文アルゴリズムの正確な評価が可能になる。
