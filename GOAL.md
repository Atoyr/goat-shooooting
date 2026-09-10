# goat-shooooting MVP Goal

## 1. Goal

2Dシューティングゲームを複数制作するための基礎エンジンについて、最初のVertical Sliceを完成させる。

完成状態は、

> JSONで定義されたステージ・敵・武器・弾を読み込み、プレイヤーを操作して敵を撃破できる2Dシューティングゲームが実際に動作し、その主要ゲームロジックを自動テストによって検証できること。

とする。

単にクラスやアーキテクチャを作ることはGoalではない。

実際にゲームが動作し、テストで動作を証明できるところまでを今回のGoalとする。

---

# 2. User Story

ゲーム制作者として、

- エンジン本体を変更せず
- JSONのゲーム定義を変更することで
- 敵のHP
- 敵の移動速度
- 敵の出現時刻
- 弾の速度
- 弾のダメージ

を変更したい。

これにより、将来的にEditorから同じDefinitionを編集できるようにする。

---

# 3. MVP Gameplay

最低限、次のゲームが動作すること。

```text
Game Start

↓

Player表示

↓

Player移動

↓

Player射撃

↓

5秒後
Enemy出現

↓

Enemyが移動

↓

Player BulletがEnemyに衝突

↓

Enemy HP減少

↓

HP 0

↓

Enemy破壊
```

ゲームとして最低限、

```text
Player
Enemy
Bullet
Stage
Collision
Damage
```

が実際に連携すること。

---

# 4. Architecture Goal

以下の責務を分離する。

```text
Definition
    ↓
Factory
    ↓
Entity
    ↓
Component
    ↓
System
```

## Definition

ゲームの静的な設定。

例:

- EnemyDefinition
- BulletDefinition
- WeaponDefinition
- StageDefinition
- PlayerDefinition

Runtime状態を持たない。

---

## Entity

ゲーム世界に存在するオブジェクト。

Entity自身にはゲームロジックを書かない。

---

## Component

Entityの状態を保持する。

例:

- TransformComponent
- VelocityComponent
- HealthComponent
- DamageComponent
- ColliderComponent
- PlayerComponent
- EnemyComponent
- BulletComponent
- WeaponHolderComponent

Component自身にUpdate処理を書かない。

---

## System

Componentを処理する。

最低限以下を実装する。

- PlayerInputSystem
- WeaponSystem
- MovementSystem
- CollisionSystem
- BulletHitSystem
- DamageSystem
- LifetimeSystem
- CleanupSystem
- StageSystem
- RenderSystem

---

## Factory

DefinitionからEntityを生成する。

最低限:

- PlayerFactory
- EnemyFactory
- BulletFactory

を用意する。

---

# 5. Solution Structure

初期構成は以下を基本とする。

```text
goat-shooooting.sln

src/
├─ goat-shooooting.Core
├─ goat-shooooting.Definitions
├─ goat-shooooting.Runtime
├─ goat-shooooting.Framework
└─ goat-shooooting.SampleGame

tests/
├─ goat-shooooting.Core.Tests
├─ goat-shooooting.Runtime.Tests
├─ goat-shooooting.Framework.Tests
└─ goat-shooooting.IntegrationTests

games/
└─ sample/
   ├─ game.json
   ├─ player.json
   ├─ enemies/
   ├─ bullets/
   ├─ weapons/
   └─ stages/
```

実装中に合理的な理由があれば多少変更してよい。

ただし責務境界は維持すること。

---

# 6. Technology

基本:

- C#
- .NET
- MonoGame
- System.Text.Json
- xUnit または既存プロジェクトで採用されているテストフレームワーク

.NET SDKについては実行環境を確認し、利用可能な安定版を選択する。

外部Assetがなくても動作確認できるようにする。

MVPではPlayer、Enemy、Bulletは単純な図形やRuntime生成Textureでもよい。

ゲーム性よりArchitectureと動作確認を優先する。

---

# 7. Definition

ゲームコンテンツをJSONとして外部化する。

例:

```json
{
  "id": "fighter-a",
  "hp": 30,
  "speed": 80,
  "weaponId": "enemy-basic"
}
```

Stage:

```json
{
  "id": "stage-01",
  "events": [
    {
      "time": 5.0,
      "type": "spawn-enemy",
      "enemyId": "fighter-a",
      "x": 320,
      "y": 50
    }
  ]
}
```

実際のSchemaは必要に応じて改善してよい。

---

# 8. Runtime Independence

Runtimeから直接、

```text
File.ReadAllText
JsonSerializer.Deserialize
```

を呼び出さない。

Definitionのロードは、

```text
IDefinitionRepository
```

などの抽象化を通す。

これにより将来的に、

```text
JSON
Editor Memory
Database
```

などへ変更できるようにする。

---

# 9. Input Independence

ゲームロジックからMonoGameのKeyboard APIを直接参照しない。

Inputを抽象化する。

例:

```text
IInputState
```

これによりIntegration Testから、

```text
MoveLeft
MoveRight
Fire
```

を入力できるようにする。

---

# 10. Headless Runtime

Codex自身がゲーム動作をテストできるよう、

GUIが存在しなくてもゲームロジックを実行できる構造にする。

例えば、

```text
ShootingSimulation
```

または同等の仕組みを用意する。

以下のように、

```text
Update(1/60秒)
Update(1/60秒)
...
```

をプログラムから実行できること。

描画処理が存在しなくても、

```text
Stage
Movement
Weapon
Collision
Damage
```

が動作すること。

---

# 11. Smoke Test Mode

SampleGameには自動終了するSmoke Testモードを用意する。

例:

```bash
dotnet run --project src/goat-shooooting.SampleGame -- --smoke-test
```

Smoke Testでは、

1. Definitionを読み込む
2. Game Worldを作る
3. Playerを生成する
4. 一定フレームSimulationする
5. Enemy Spawnを確認する
6. Bullet生成を確認する
7. Collision / Damage処理を確認する
8. 正常ならexit code 0
9. 異常ならexit code != 0

とする。

画面操作を必要としないこと。

---

# 12. Automated Tests

最低限、以下を自動テストする。

## Entity / Component

- Componentを追加できる
- Componentを取得できる
- Componentを削除できる

## Movement

Transform + Velocityを持つEntityが、

```text
position += velocity * deltaTime
```

で移動する。

## Stage

5秒にEnemy Spawn Eventがある場合、

```text
4.9秒 → Enemyなし
5.0秒以上 → Enemy生成済み
```

となる。

同じEventが複数回実行されない。

## Weapon

Fire条件を満たすとBulletが生成される。

Cooldown中には生成されない。

## Collision

BulletとEnemyのColliderが重なった場合、

Collisionが検出される。

## Damage

Player BulletがEnemyに命中した場合、

EnemyのHPがBullet Damage分減少する。

## Death

HPが0以下になったEnemyは削除される。

## Lifetime

Lifetimeを超えたBulletが削除される。

## Definition

JSONから、

- Enemy
- Bullet
- Weapon
- Stage

を正常に読み込める。

不正な参照IDについて適切にエラーにできる。

---

# 13. End-to-End Integration Test

最重要テスト。

次のシナリオをコードから完全自動実行する。

```text
Given

Player HP > 0
Enemy HP = 10
Player Bullet Damage = 10

Enemy Spawn Time = 1秒

When

ゲームSimulationを開始

PlayerがFireする状態を入力

十分な時間Simulationする

Then

EnemyがSpawnする

Player Bulletが生成される

Bulletが移動する

Enemyと衝突する

Enemy HPが0になる

EnemyがWorldから削除される
```

このテストが通ることをMVPの主要Acceptance Testとする。

---

# 14. Definition変更テスト

Engineコードを変更せず、

EnemyDefinitionの、

```text
HP
Speed
Spawn Time
```

を変更するだけでRuntimeの動作が変化することをテストする。

Definition駆動になっていることを証明する。

---

# 15. Visual Sample

自動テストだけでなく、

通常起動時には実際にゲーム画面を表示できること。

```bash
dotnet run --project src/goat-shooooting.SampleGame
```

最低限表示する。

```text
Player

Enemy

Player Bullet
```

Playerはキーボード操作できること。

推奨:

```text
Arrow / WASD : Move

Z / Space : Fire

Esc : Quit
```

---

# 16. Definition of Done

以下をすべて満たした場合のみDONEとする。

## Build

```bash
dotnet build
```

成功。

Warningも可能な範囲で解消する。

---

## Unit / Integration Tests

```bash
dotnet test
```

すべて成功。

Skipped Testを残して完了扱いにしない。

---

## Smoke Test

```bash
dotnet run --project src/goat-shooooting.SampleGame -- --smoke-test
```

成功。

exit code 0。

---

## Runtime

通常モードのSampleGameがビルド可能である。

実行環境で画面表示可能な場合は実際に起動して確認する。

GUIを利用できない実行環境の場合は、

- Build
- Integration Test
- Smoke Test

によってRuntimeを検証する。

---

## Documentation

READMEに以下を書く。

- 必要環境
- Build方法
- Test方法
- SampleGame起動方法
- Smoke Test方法
- Project構成
- Definitionの追加方法
- 現時点のArchitecture

---

# 17. Self Verification Loop

実装途中で失敗が発生しても作業を終了しない。

必ず、

```text
Implement
    ↓
Build
    ↓
Test
    ↓
Smoke Test
    ↓
Failure?
 ┌──Yes───────────┐
 │                │
 ▼                │
Investigate       │
 │                │
Fix               │
 │                │
 └────────────────┘

No
 ↓
Final Verification
 ↓
DONE
```

のループを行う。

「コードを書いた」という理由では完了しない。

---

# 18. Failure Policy

テストが失敗した場合、

1. エラーログを確認する
2. 原因を特定する
3. Production CodeまたはTestを修正する
4. 対象Testを再実行する
5. 全Testを再実行する

Testを通すためだけに、

- Assertionを削除する
- TestをSkipする
- 検証条件を弱くする
- 例外を握りつぶす

ことは禁止する。

Test自体がGoalと矛盾していることが明確な場合のみ、理由を残して修正する。

---

# 19. Architecture Constraints

以下は禁止。

```text
Enemy : Entity

Bullet : Entity

Player : Entity
```

Entity継承モデルにはしない。

Component方式を使う。

---

Componentに、

```text
Update()
Draw()
```

を実装しない。

処理はSystemへ置く。

---

Definitionに、

```text
CurrentHp
CooldownRemaining
CurrentPosition
```

などRuntime状態を入れない。

---

RuntimeからEditorへの依存を作らない。

---

ゲーム固有Definition値をRuntimeコードへHard Codeしない。

---

# 20. Out of Scope

今回作らない。

- Editor
- Plugin DLL動的ロード
- Module Marketplace
- Node Graph
- Boss専用システム
- Particle Editor
- 独自Script Language
- 3D
- Multiplayer
- Networking
- 高度なECS最適化
- Save System
- Configuration GUI

必要以上に先回りして実装しない。

---

# 21. Design Priority

判断に迷った場合は、

1. 動作すること
2. 自動テストできること
3. DefinitionとRuntimeが分離されていること
4. 小さく理解しやすいこと
5. 将来拡張できること

の順に優先する。

「将来使うかもしれない」という理由だけで抽象化を追加しない。

---

# 22. Final Success Criteria

最終的に第三者がRepositoryをcloneして、

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/goat-shooooting.SampleGame -- --smoke-test
```

を実行したとき、すべて成功すること。

さらに、

```bash
dotnet run --project src/goat-shooooting.SampleGame
```

によって、実際に操作可能な小さな2Dシューティングゲームを起動できること。

これを満たして初めて今回のGoalを達成したと判断する。
