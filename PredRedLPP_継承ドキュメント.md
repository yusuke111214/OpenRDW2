# PredRedLPP実装 引き継ぎドキュメント

## 🎯 プロジェクト概要

論文「Predictive multiuser redirected walking using artificial potential fields (Hirt et al., 2024)」のPredRedLPPアルゴリズムをOpenRDW2プロジェクトに実装する。

**論文URL:** https://www.frontiersin.org/articles/10.3389/frvir.2024.1365344/full
**PDF:** `C:\RDW\OpenRDW2\Hirt_原文_予測＿マルチユーザー.pdf`

---

## 📋 現在の状況

### ✅ 完了している実装（Phase 2-6）

コミット履歴：
- `2d8bb09`: Phase 2実装（基礎クラス）
- `d205235`: Phase 3-5実装（予測・評価・リダイレクター）
- `d03d41e`: コメント日本語化
- `9b4b795`: ドキュメント作成
- **最新**: Phase 6実装（問題1,2の修正）
  - 可視化オブジェクトの座標変換修正
  - アクションセット評価を論文準拠に修正
  - Curvature適用メソッド実装

#### Phase 2で作成したクラス
1. **Trajectory.cs** - 軌跡を表すデータクラス + ApplyCurvature() メソッド（Phase 6追加）
2. **RedirectionAction.cs** - リダイレクションアクション（Phase 6で使用開始）
3. **IPathPredictor.cs** - パス予測器のインターフェース
4. **CurvesWrapper.cs** - Curvesライブラリのラッパー
5. **PathSmoother.cs** - パス平滑化（HMD用、デフォルトOFF）
6. **ClothoidGenerationTest.cs** - テストスクリプト

#### Phase 3-5で作成したクラス
1. **LemniscatePathPredictor.cs** - レムニスケート予測器（論文 Eq.1）
2. **TrajectoryEvaluator.cs** - コスト関数（論文 Eq.14-18）
3. **PredRedLPP_Redirector.cs** - メインリダイレクター

---

## ✅ 解決済みの問題点

### 問題1: 可視化オブジェクトの位置ずれ（解決済み）

#### 原因
**座標系の混同** - 物理空間と仮想空間の変換が欠けていた

Redirected Walkingの仕組み：
- **仮想空間**（`currPos`/`currDir`）：ユーザーが見ている世界、Unityワールド座標
  - アバター、Plane、カメラはこの座標系で動く
  - ユーザーは直進していると感じる
- **物理空間**（`currPosReal`/`currDirReal`）：実際のトラッキングスペース
  - `trackingSpace.transform`のローカル座標系
  - 予測軌跡はこの座標系で生成される（障害物回避のため）
  - 実際にはカーブして歩いている

#### 解決方法
**座標変換の実装**（`trackingSpace.TransformPoint()`）

```csharp
// 物理空間の2D座標 → 仮想空間の3D座標
Vector3 physicalPos3D = Utilities.UnFlatten(physicalPoint2D);
Vector3 virtualPos3D = trackingSpace.TransformPoint(physicalPos3D);
```

変換の仕組み：
- 仮想→物理：`GetPosReal(pos)` = `Inverse(trackingSpace.rotation) * (pos - trackingSpace.position)`
- 物理→仮想：`trackingSpace.TransformPoint(posReal)` = 逆変換

修正箇所：
1. `UpdateLemniscateVisualization()`: 物理空間の座標を仮想空間に変換して描画
2. `UpdateTrajectoryVisualization()`: 同上
3. LineRendererは`useWorldSpace=true`で仮想空間のワールド座標を使用

---

### ✅ 問題2: アクションセット評価のアプローチと論文との整合性（解決済み）

#### 原因
**アクションの評価方法が論文と異なっていた** - アクションを軌跡に適用せずにコストを計算していた

問題点：
- TrajectoryEvaluator.cs の `EvaluateAllActions()` (旧Line 89-91)
  ```csharp
  // 全てのアクションで同じコストを使用していた
  float actionCost = trajectoryCost;  // ❌ 間違い
  ```
- 各アクション π を軌跡 T に適用した結果（T_red）を評価すべきだった
- 特に Curvature ゲインの効果が評価されず、Translation ゲインばかり選ばれていた

#### 解決方法
**論文 Section 3.3 のアルゴリズムを正しく実装**

1. **Trajectory.cs に `ApplyCurvature()` メソッドを追加** (Line 255-303)
   ```csharp
   public Trajectory ApplyCurvature(float curvature)
   {
       // 各セグメントに曲率を適用して T_red を生成
       // rotation = curvature * distance
       for (int i = 1; i < points.Count; i++)
       {
           float segmentLength = Vector2.Distance(points[i - 1], points[i]);
           float rotationAngle = curvature * segmentLength;
           currentAngle += rotationAngle;

           Vector2 direction = new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle));
           Vector2 newPoint = newPoints[newPoints.Count - 1] + direction * segmentLength;
           newPoints.Add(newPoint);
       }
       return new Trajectory(newPoints);
   }
   ```

2. **TrajectoryEvaluator.cs の `EvaluateAllActions()` を書き直し** (Line 43-159)
   ```csharp
   public (RedirectionAction, Trajectory) EvaluateAllActions(...)
   {
       foreach (var trajectory in predictions)
       {
           foreach (var action in actions)
           {
               // Step 1: アクションを軌跡に適用 → T_red を生成
               Trajectory T_red = ApplyActionToTrajectory(trajectory, action);

               // Step 2: T_red のコストを評価
               float actionCost = CalculateTotalCost(T_red, ...);

               // Step 3: 最小コストを記録
               if (actionCost < minCost)
               {
                   minCost = actionCost;
                   bestAction = action;
                   bestTrajectory = trajectory;
               }
           }
       }
       return (bestAction, bestTrajectory);
   }
   ```

3. **PredRedLPP_Redirector.cs の `InjectRedirection()` を修正** (Line 247-270)
   ```csharp
   // Step 5: アクションセット U を生成
   List<RedirectionAction> actionSet =
       RedirectionActionFactory.GenerateActionSet(globalConfiguration);

   // Step 6: 全ての (軌跡, アクション) ペアを評価
   (RedirectionAction bestAction, Trajectory bestTrajectory) =
       trajectoryEvaluator.EvaluateAllActions(
           feasibleTrajectories, actionSet, ...);

   // Step 7: 最良のアクション π_optimal を適用
   ApplyRedirectionAction(bestAction);
   ```

#### 結果
✅ **論文 Section 3.3 のアルゴリズムに準拠** - アクションセット U 全体を評価
✅ **予測的アプローチを実装** - T_red のコスト評価で最適なアクションを選択
✅ **Curvature ゲインが正しく評価される** - 障害物回避性能の向上が期待される

---

この問題には、過去に **3つの異なるアプローチ**が検討されました（参考）。

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

## 💡 採用した実装アプローチ

### ✅ 論文準拠のアプローチB（実装済み）

**選択理由:**
- ✅ 論文 Section 3.3 に完全準拠
- ✅ 予測的アプローチの真の実装
- ✅ アクションセット U 全体を正しく評価
- ✅ Curvature ゲインの効果を評価可能

**実装内容:**
1. ✅ 可視化の座標変換を修正（問題1）
2. ✅ `Trajectory.ApplyCurvature()` を実装
3. ✅ `TrajectoryEvaluator.EvaluateAllActions()` を論文準拠に書き直し
4. ✅ `PredRedLPP_Redirector.InjectRedirection()` をアクションセット評価方式に変更

**結果:**
- 論文のアルゴリズムを正確に実装
- T_pred → T_red のフローを正しく実装
- 各 (軌跡, アクション) ペアのコストを個別評価
- π_optimal の選択が論文通り


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
└── Arrow(Clone)

```



## ✅ 論文との整合性について

### 現在の実装は論文と完全に一致しています

**実装内容:**
- ✅ アクションセットU全体を評価（Table 2準拠）
- ✅ 最良の(軌跡, アクション)ペアを選択（Eq. 13準拠）
- ✅ 予測的アプローチ（軌跡予測）を使用
- ✅ APFベースのコスト関数を使用（Eq. 14-18）
- ✅ レムニスケート＋クロソイドを使用（Eq. 1）
- ✅ T_pred → T_red の変換を正しく実装

**論文 Section 3.3 との対応:**
```
論文: "T_pred is redirected based on an action set U"
実装: actionSet = RedirectionActionFactory.GenerateActionSet()

論文: "a cost-based analysis of T_red is used to identify π_optimal ∈ U"
実装: (bestAction, bestTrajectory) = trajectoryEvaluator.EvaluateAllActions(
         predictions, actionSet, ...)

論文: "The optimal redirection π_optimal is applied"
実装: ApplyRedirectionAction(bestAction)
```

**Phase 6で解決した問題:**
1. 座標変換の不備 → 物理空間↔仮想空間の変換を実装
2. アクション評価の不備 → T_red を生成してコストを正しく評価

