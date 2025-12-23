# PredRedLPP実装 引き継ぎドキュメント

## 🎯 プロジェクト概要

論文「Predictive multiuser redirected walking using artificial potential fields (Hirt et al., 2024)」のPredRedLPPアルゴリズムをOpenRDW2プロジェクトに実装する。

**論文URL:** https://www.frontiersin.org/articles/10.3389/frvir.2024.1365344/full
**PDF:** `C:\RDW\OpenRDW2\Hirt_原文_予測＿マルチユーザー.pdf`

---

## 📋 現在の状況

### ✅ 完了している実装（Phase 2-5）

コミット履歴：
- `2d8bb09`: Phase 2実装（基礎クラス）
- `d205235`: Phase 3-5実装（予測・評価・リダイレクター）
- `d03d41e`: コメント日本語化

#### Phase 2で作成したクラス
1. **Trajectory.cs** - 軌跡を表すデータクラス
2. **RedirectionAction.cs** - リダイレクションアクション（未使用）
3. **IPathPredictor.cs** - パス予測器のインターフェース
4. **CurvesWrapper.cs** - Curvesライブラリのラッパー
5. **PathSmoother.cs** - パス平滑化（HMD用、デフォルトOFF）
6. **ClothoidGenerationTest.cs** - テストスクリプト

#### Phase 3-5で作成したクラス
1. **LemniscatePathPredictor.cs** - レムニスケート予測器（論文 Eq.1）
2. **TrajectoryEvaluator.cs** - コスト関数（論文 Eq.14-18）
3. **PredRedLPP_Redirector.cs** - メインリダイレクター

---

## ⚠️ 現在の問題点

### 問題1: 可視化オブジェクトの位置ずれ

#### 現象
- `PredRedLPP_Trajectory`と`PredRedLPP_Lemniscate`の位置はBodyと一致
- しかし、**回転が一致しない**（可視化: 0,0,0 / Body: Y:91.624）
- 回転を手動で合わせても、Bodyの位置と一致しない

#### 原因分析

**RedirectedAvatarの階層構造:**
```
RedirectedAvatar (PredRedLPP_Redirector がアタッチ)
├── rotation: (0, 0, 0) ← RedirectedAvatar本体の回転
├── Body (HeadFollower)
│   ├── position: currPos（仮想空間）
│   └── rotation: Quaternion.LookRotation(currDir) ← currDirに基づく回転
├── TrackingSpace0
└── Simulated Avatar/Head
```

**座標系の違い:**

| 変数 | 座標系 | 計算方法 | 格納場所 |
|------|--------|----------|----------|
| `currPos` | 仮想空間 | `headTransform.position` | RedirectionManager.cs:578 |
| `currDir` | 仮想空間 | `headTransform.forward` | RedirectionManager.cs:579 |
| `currPosReal` | 物理空間 | `GetRelativePosition(currPos, trackingSpace)` | RedirectionManager.cs:578 |
| `currDirReal` | 物理空間 | `GetRelativeDirection(currDir, transform)` | RedirectionManager.cs:580 |

**現在の実装（元の状態）:**
```csharp
// PredRedLPP_Redirector.cs UpdateVisualization()
Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal); // 物理空間
Vector2 currentDir2D = Utilities.FlattenedDir2D(redirectionManager.currDirReal); // 物理空間

// GameObjectのTransform更新は行っていない
// → trajectoryVisualizer/lemniscateVisualizerは親(RedirectedAvatar)のTransformに従う
```

**正しい実装（APF_Redirector.cs:29のパターン）:**
```csharp
// 仮想空間の座標を使用
totalForcePointer.transform.position = redirectionManager.currPos;
totalForcePointer.transform.forward = transform.rotation * Utilities.UnFlatten(forceT);
```

**修正方法:**
```csharp
// UpdateVisualization()で以下を追加:
trajectoryVisualizer.transform.position = redirectionManager.currPos;
trajectoryVisualizer.transform.rotation = Quaternion.LookRotation(redirectionManager.currDir, Vector3.up);
// または
trajectoryVisualizer.transform.rotation = body.rotation;

lemniscateVisualizer.transform.position = redirectionManager.currPos;
lemniscateVisualizer.transform.rotation = Quaternion.LookRotation(redirectionManager.currDir, Vector3.up);

// 可視化用の座標も仮想空間を使用
Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPos);
Vector2 currentDir2D = Utilities.FlattenedDir2D(redirectionManager.currDir).normalized;
```

---

### 問題2: アクションセット評価のアプローチと論文との整合性

この問題には、**3つの異なるアプローチ**が存在します。

---

## 📚 3つのアプローチの比較

### アプローチA: 現在の実装（元の状態、動作確認済み）

#### 実装内容
```csharp
// Step 5: コストに基づいて最良の軌跡を選択
Trajectory bestTrajectory = SelectBestTrajectory(feasibleTrajectories, physicalSpace);

// Step 6: 最良の軌跡に基づいてリダイレクションをリアクティブに適用
ApplyRedirectionFromTrajectory(bestTrajectory, physicalSpace);
```

**`ApplyRedirectionFromTrajectory()`の動作:**
```csharp
// 軌跡上のターゲットポイントを取得
Vector2 targetPoint = trajectory.points[targetIndex];
Vector2 desiredDir2D = (targetPoint - currentPos2D).normalized;

// 常に3つのゲイン全てを設定
SetTranslationGain(...);  // 軌跡との整合性で決定
SetRotationGain(...);     // 回転方向で決定
SetCurvature(...);        // 操舵方向で決定
ApplyGains();
```

#### 特徴
✅ **動作確認済み** - ユーザーが曲がることを確認
✅ **実装がシンプル** - リアクティブアプローチ
✅ **全てのゲインが適用される** - T/R/Cを毎フレーム計算

❌ **論文と異なる** - アクションセットUを使用していない
❌ **予測的ではない** - 毎フレームリアクティブに計算

#### 論文との差異

| 論文（Section 3.3） | 現在の実装 |
|-------------------|-----------|
| アクションセットU全体を評価（Table 2） | アクションセット不使用 |
| 複数のアクション候補から選択（Eq.13） | 軌跡に基づいてリアクティブに計算 |
| 予測的アプローチ | リアクティブアプローチ |

---

### アプローチB: 論文準拠の実装（試行したが動作せず）

#### 実装内容
```csharp
// Step 5: アクションセットU生成（論文 Table 2）
List<RedirectionAction> actionSet = RedirectionActionFactory.GenerateActionSet(globalConfiguration);
// → 19個のアクション（6 Translation + 6 Rotation + 6 Curvature + 1 Null）

// Step 6: 全アクション-軌跡ペアを評価
(RedirectionAction bestAction, Trajectory bestTrajectory) =
    trajectoryEvaluator.EvaluateAllActions(feasibleTrajectories, actionSet, ...);

// Step 7: 最良のアクションを適用
ApplyRedirectionAction(bestAction);
```

**`ApplyRedirectionAction()`の動作:**
```csharp
switch (action.gainType)
{
    case RedirectionGainType.Translation:
        SetTranslationGain(action.primaryValue);
        SetRotationGain(1.0f);  // 中立
        SetCurvature(0f);       // 中立
        break;

    case RedirectionGainType.Rotation:
        SetTranslationGain(1.0f);       // 中立
        SetRotationGain(action.primaryValue);
        SetCurvature(0f);       // 中立
        break;

    case RedirectionGainType.Curvature:
        SetTranslationGain(1.0f);  // 中立
        SetRotationGain(1.0f);     // 中立
        SetCurvature(action.primaryValue);
        break;
}
ApplyGains();
```

#### 特徴
✅ **論文に準拠** - Section 3.3の手順通り
✅ **予測的アプローチ** - 全アクションを事前評価

❌ **並進ゲインのみ適用される** - コスト評価の結果、Translation actionがbestとして選ばれ続ける
❌ **ユーザーが曲がらない** - Curvature/Rotationゲインが適用されない

#### なぜ並進ゲインのみになるのか？

**推定される原因:**

1. **コスト関数の問題**
   - APFコスト（Eq.16）が支配的
   - Translation actionが常に最小コストを持つ

2. **軌跡の問題**
   - 予測軌跡が直線的
   - Curvatureアクションの効果が評価されない

3. **評価方法の問題**
   - `TrajectoryEvaluator.EvaluateAllActions()`が各アクションの影響を正しく評価していない
   - 現在は「アクションを適用した結果の軌跡」ではなく「元の軌跡」を評価

**論文の本来の意図（推測）:**
```
For each trajectory T in predictions:
    For each action π in U:
        1. Simulate applying π to T → get T_redirected
        2. Calculate cost of T_redirected
        3. Store (T, π, cost)
Select (T*, π*) with minimum cost
```

**現在の実装:**
```
For each trajectory T in predictions:
    1. Calculate cost of T (without considering actions)
    For each action π in U:
        2. Use same cost for all actions
Select first trajectory with minimum cost, any action
```

---

### アプローチC: ハイブリッドアプローチ（提案）

アプローチAとBの利点を組み合わせる：

#### 実装内容
```csharp
// Step 5: 最良の軌跡を選択（アプローチA）
Trajectory bestTrajectory = SelectBestTrajectory(feasibleTrajectories, physicalSpace);

// Step 6: 軌跡に向かうための最良のアクションを選択（アプローチB）
RedirectionAction bestAction = SelectBestActionForTrajectory(bestTrajectory, actionSet, physicalSpace);

// Step 7: アクションを適用
ApplyRedirectionAction(bestAction);
```

**`SelectBestActionForTrajectory()`:**
```csharp
private RedirectionAction SelectBestActionForTrajectory(
    Trajectory trajectory,
    List<RedirectionAction> actionSet,
    SingleSpace physicalSpace)
{
    float minCost = float.MaxValue;
    RedirectionAction bestAction = null;

    foreach (var action in actionSet)
    {
        // このアクションを適用した場合の効果を評価
        float cost = EvaluateActionEffect(action, trajectory, physicalSpace);

        if (cost < minCost)
        {
            minCost = cost;
            bestAction = action;
        }
    }

    return bestAction ?? RedirectionAction.CreateNullAction();
}
```

#### 特徴
✅ **動作する可能性が高い** - アプローチAベース
✅ **論文に近い** - アクションセットを使用
✅ **全ゲイン適用可能** - アクション選択による

⚠️ **論文との差異あり** - 軌跡選択とアクション選択を分離

---

## 🤔 論文のアルゴリズムをどう解釈すべきか？

### 論文の記述（Section 3.3）

> "The action set U consists of rotation gains, translation gains, curvature gains, and a combination of translation and curvature gains."

> "A cost-based analysis of T_red is used to identify the best redirection π_optimal ∈ U"

### 2つの解釈

#### 解釈1: 軌跡とアクションを独立に評価
```
1. 予測軌跡 T を生成
2. T のコストを計算（アクション非依存）
3. アクションセット U からコストが最小のものを選択
```
→ **これは意味をなさない**（アクションが軌跡に影響しないなら選択する意味がない）

#### 解釈2: アクションが軌跡に影響を与える
```
1. 予測軌跡 T を生成（ユーザーの自然な動き）
2. 各アクション π を T に適用した結果 T_red を計算
3. T_red のコストを評価
4. 最小コストの (T, π) ペアを選択
```
→ **論文の意図はこちら**だが、実装方法が不明確

### 問題: 「アクションを軌跡に適用」の意味

論文では以下が不明確：
1. どのように T に π を適用して T_red を得るのか？
2. T_red は新しい軌跡か、修正された軌跡か？
3. リダイレクションは軌跡生成時に考慮されるのか、後から適用されるのか？

---

## 💡 実装の推奨アプローチ

### オプション1: アプローチAを継続（推奨）

**理由:**
- ✅ 動作確認済み
- ✅ 実装がシンプル
- ✅ デバッグが容易
- ⚠️ 論文との差異を明記する必要あり

**修正内容:**
1. 可視化の位置ずれを修正（問題1）
2. コメントで論文との差異を明記
3. 論文の「spirit（精神）」は満たしている - 予測とAPFベースのコスト評価

---

### オプション2: アプローチCを試行

**理由:**
- ✅ 論文により近い
- ✅ アプローチAをベースにできる
- ⚠️ 追加実装が必要
- ⚠️ 動作保証なし

**実装手順:**
1. `SelectBestActionForTrajectory()`を実装
2. `EvaluateActionEffect()`でアクションの効果を評価
3. テストして動作確認

---

### オプション3: 論文著者に問い合わせ

**理由:**
- 論文のアルゴリズム記述が不明確
- 実装の詳細がGitHubで公開されていない可能性

**問い合わせ内容:**
1. アクションセットUの適用方法
2. T_redの計算方法
3. リファレンス実装の有無

---

## 📝 今後の修正タスク

### 優先度: 高（必須）

#### タスク1: 可視化の位置ずれ修正

**ファイル:** `PredRedLPP_Redirector.cs`
**メソッド:** `UpdateVisualization()`

**修正内容:**
```csharp
private void UpdateVisualization()
{
    if (!visualizePredictions || globalConfiguration.runInBackstage)
        return;

    if (trajectoryLineRenderer == null || lemniscateLineRenderer == null)
        return;

    bool isVisible = visualizationManager.ifVisible;
    trajectoryLineRenderer.enabled = isVisible;
    lemniscateLineRenderer.enabled = isVisible;

    if (!isVisible)
        return;

    // 【修正】GameObjectのTransformをBodyと同期
    // APF_Redirectorパターン（APF_Redirector.cs:29）を参考
    trajectoryVisualizer.transform.position = redirectionManager.currPos;
    trajectoryVisualizer.transform.rotation = Quaternion.LookRotation(redirectionManager.currDir, Vector3.up);

    lemniscateVisualizer.transform.position = redirectionManager.currPos;
    lemniscateVisualizer.transform.rotation = Quaternion.LookRotation(redirectionManager.currDir, Vector3.up);

    // 仮想空間の座標を使用（物理空間ではない）
    Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPos);
    Vector2 currentDir2D = Utilities.FlattenedDir2D(redirectionManager.currDir).normalized;

    UpdateLemniscateVisualization(currentPos2D, currentDir2D);
    UpdateTrajectoryVisualization();
}
```

**検証方法:**
1. Unity Editorで再生
2. Inspectorで以下を確認：
   - `PredRedLPP_Trajectory` の position/rotation が `Body` と一致
   - `PredRedLPP_Lemniscate` の position/rotation が `Body` と一致
3. アバター移動時に可視化も追従することを確認

---

### 優先度: 中（アプローチ選択後）

#### タスク2: アプローチの決定と実装

**選択肢:**
1. **アプローチA継続** - 最小限の修正（可視化のみ）
2. **アプローチC実装** - ハイブリッドアプローチ
3. **論文著者問い合わせ** - 正確な実装方法の確認

**アプローチA継続の場合:**
- コメントで論文との差異を明記
- クラス・メソッドのドキュメントを更新

**アプローチC実装の場合:**
```csharp
// 新規メソッド
private RedirectionAction SelectBestActionForTrajectory(
    Trajectory trajectory,
    List<RedirectionAction> actionSet,
    SingleSpace physicalSpace)
{
    // 実装内容:
    // 1. 各アクションの効果を評価
    // 2. 最小コストのアクションを選択
    // 3. 全てのアクションが評価されることを保証
}

private float EvaluateActionEffect(
    RedirectionAction action,
    Trajectory trajectory,
    SingleSpace physicalSpace)
{
    // 実装内容:
    // 1. アクションを適用した場合の「効果」を計算
    // 2. 軌跡の目標方向とアクションの方向の一致度
    // 3. APFコストとの組み合わせ
}
```

---

## 🔍 デバッグ情報

### コンソールログの確認ポイント

#### アプローチA（現在）
```
PredRedLPP: Generated 11 predictions
PredRedLPP: 11 feasible trajectories (from 11)
PredRedLPP: Selected trajectory with cost=X.XXX
// ApplyRedirectionFromTrajectory()内のログ（追加推奨）
```

#### アプローチB（論文準拠）
```
PredRedLPP: Generated 11 predictions
PredRedLPP: 19個のリダイレクションアクションを生成
PredRedLPP: 11 feasible trajectories (from 11)
PredRedLPP: Applied Translation action - T=1.100, R=1.000, C=0.000
// ← 常にTranslationのみ（問題）
```

#### 期待される動作
```
PredRedLPP: Applied Translation action - T=1.100, R=1.000, C=0.000
PredRedLPP: Applied Rotation action - T=1.000, R=1.200, C=0.000
PredRedLPP: Applied Curvature action - T=1.000, R=1.000, C=0.150
// ← 状況に応じて異なるアクション
```

---

## 📚 参考情報

### 重要なファイル

| ファイル | 役割 | 重要度 |
|---------|------|--------|
| `PredRedLPP_Redirector.cs` | メインリダイレクター | ⭐⭐⭐ |
| `LemniscatePathPredictor.cs` | 軌跡予測（Eq.1） | ⭐⭐⭐ |
| `TrajectoryEvaluator.cs` | コスト評価（Eq.14-18） | ⭐⭐⭐ |
| `RedirectionAction.cs` | アクション定義 | ⭐⭐ |
| `RedirectionManager.cs` | 基底クラス | ⭐⭐ |
| `APF_Redirector.cs` | APF基底クラス | ⭐⭐ |
| `HeadFollower.cs` | Body更新 | ⭐ |

### 座標系まとめ

```csharp
// 仮想空間（VR空間、アバター表示に使用）
Vector3 currPos = redirectionManager.currPos;  // headTransform.position
Vector3 currDir = redirectionManager.currDir;  // headTransform.forward

// 物理空間（トラッキングスペース、APF計算に使用）
Vector3 currPosReal = redirectionManager.currPosReal;  // GetRelativePosition(currPos, trackingSpace)
Vector3 currDirReal = redirectionManager.currDirReal;  // GetRelativeDirection(currDir, transform)

// 変換
Vector2 pos2D = Utilities.FlattenedPos2D(currPos);    // XZ平面に射影
Vector2 dir2D = Utilities.FlattenedDir2D(currDir);    // XZ平面に射影
Vector3 pos3D = Utilities.UnFlatten(pos2D);           // Y=0で3Dに戻す
```

### GameObjectの親子関係

```
RedirectedAvatar (PredRedLPP_Redirector)
├── Body (HeadFollower)
│   ├── transform.position = currPos
│   └── transform.rotation = LookRotation(currDir)
├── TrackingSpace0
├── Simulated Avatar/Head (SimulatedWalker)
└── PredRedLPP_Trajectory (LineRenderer)
    └── 親: RedirectedAvatar
    └── useWorldSpace = false (ローカル座標)
└── PredRedLPP_Lemniscate (LineRenderer)
    └── 親: RedirectedAvatar
    └── useWorldSpace = false (ローカル座標)
```

---

## 🎯 次のセッションで実施すべきこと

### 必須タスク
1. **可視化の位置ずれを修正** - タスク1を実装
2. **動作確認** - Unity Editorでテスト

### 推奨タスク
1. **アプローチの決定** - A継続 or C実装 or 論文著者問い合わせ
2. **ドキュメント更新** - 選択したアプローチに基づいてコメント修正

### オプションタスク
1. **論文との対応表作成** - 実装と論文の対応関係を明確化
2. **パラメータチューニング** - コスト関数のweightなど

---

## ⚠️ 注意事項

### 論文との整合性について

現在の実装（アプローチA）は論文のアルゴリズムと**厳密には一致していません**。

**差異:**
- 論文: アクションセットU全体を評価 → 最良の(軌跡, アクション)ペアを選択
- 実装: 最良の軌跡を選択 → その軌跡に向かってリアクティブにゲイン計算

**しかし:**
- 予測的アプローチ（軌跡予測）を使用 ✓
- APFベースのコスト関数を使用 ✓
- レムニスケート＋クロソイドを使用 ✓
- 論文の「精神（spirit）」は満たしている

**推奨:**
- 現在の実装を「PredRedLPP-Reactive」として扱う
- 論文準拠版を実装する場合は「PredRedLPP-ActionSet」として別クラス化
- 両方を比較評価

---

## 📞 問い合わせ先

**論文著者:**
- Christian Hirt (hirtc@ethz.ch)
- ETH Zurich, Innovation Center Virtual Reality

**問い合わせ内容案:**
```
Subject: Implementation question about PredRedLPP algorithm

Dear Dr. Hirt,

I am implementing your PredRedLPP algorithm from the paper
"Predictive multiuser redirected walking using artificial potential fields"
in our VR locomotion system.

I have a question about Section 3.3, specifically how to apply
the action set U to the predicted trajectories:

1. How should we calculate T_red from T and π?
2. Should we simulate the effect of each action on each trajectory?
3. Is there a reference implementation available?

Thank you for your excellent work!
```

---

## 📄 変更履歴

| 日付 | 変更者 | 内容 |
|------|--------|------|
| 2025-12-23 | Claude Code | 初版作成、問題点整理 |

---

**このドキュメントをClaude Codeに渡す際の推奨プロンプト:**

```
以下の引き継ぎドキュメントを読んで、PredRedLPP実装の現状を理解してください。

[このドキュメント全文を貼り付け]

現在の優先タスクは「可視化の位置ずれ修正（タスク1）」です。
ドキュメントの指示に従って修正を実装してください。

修正後は必ず以下を確認してください：
1. 可視化オブジェクト（PredRedLPP_Trajectory/Lemniscate）がBodyと同じ位置・回転になっているか
2. アバター移動時に可視化も追従するか
3. Console.logでエラーが出ていないか
```
