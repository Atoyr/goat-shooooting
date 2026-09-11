# goat-shooooting

goat-shooooting は、JSON で定義した Player、Enemy、Weapon、Bullet、Stage を読み込み、MonoGame 上で動作させる小さな 2D シューティング基盤です。プレイヤーと敵の双方が射撃でき、被弾による Game Over、敵全滅による Stage Clear、リトライまでを1プレイとして実行できます。ゲームロジックは描画から独立しており、同じ Production Runtime を headless simulation、統合テスト、smoke test、通常ゲームのすべてで使用します。

## 必要環境

- .NET 8 SDK（`global.json` は 8.0 系の利用可能な最新 feature band を選択）
- 通常ゲームの実行時のみ、OpenGL 2.0 以上を利用できるデスクトップ環境
- NuGet の初回 restore 時に `MonoGame.Framework.DesktopGL` 3.8.5.1 と xUnit 関連パッケージを取得できること

ゲーム画像などの外部 Asset は不要です。Player、Enemy、Bullet は実行時生成した 1 ピクセル Texture から単純な図形として描画します。

## Build と Test

リポジトリルートで実行します。

```bash
dotnet restore
dotnet build
dotnet test
```

テストには ECS の基本操作、各 System の単体テスト、JSON 検証、Definition 変更テスト、ゲーム全経路の End-to-End Integration Test が含まれます。

## SampleGame

通常起動:

```bash
dotnet run --project src/goat-shooooting.SampleGame
```

高速・縦長構成の第2コンテンツパックを起動:

```bash
dotnet run --project src/goat-shooooting.SampleGame -- --game gauntlet
```

操作:

- Arrow / WASD: Player 移動
- Z / Space: 発射
- P: ポーズ／再開
- R / Enter: Game Over／Stage Clear後にリトライ
- Esc: 終了

約60秒のステージ中にScout、Fighter、Midbossが複数Waveで出現します。Enemyは下方向へ射撃し、PlayerのHPが0になるとGame Overです。Player BulletでEnemyをすべて倒すとStage Clearになります。被弾後には短い無敵時間があり、命中フラッシュ、撃破エフェクト、手続き生成した効果音、画面揺れで結果を伝えます。プレイヤーはColliderを含めて画面内に制限され、画面外へ完全に出た敵と弾は自動的に削除されます。HPバーは画面左上、現在HP・スコア・Pause／終了状態はウィンドウタイトルに表示されます。

画面を使わない smoke test:

```bash
dotnet run --project src/goat-shooooting.SampleGame -- --smoke-test
dotnet run --project src/goat-shooooting.SampleGame -- --game gauntlet --smoke-test
```

このモードは JSON 読込後に Production の `ShootingSimulation` を一定フレーム進め、Enemy spawn、双方のBullet spawnと移動、Collision、Damage、Enemy death、Stage Clear、リトライと状態初期化を観測して自動終了します。成功時は exit code 0、検証失敗または例外時は exit code 1 です。

## Project 構成

```text
goat-shooooting.sln
src/
  goat-shooooting.Core          Entity、World、状態のみを持つ Component
  goat-shooooting.Definitions   静的 Definition、検証、JSON／memory repository
  goat-shooooting.Runtime       Factory、System、headless ShootingSimulation
  goat-shooooting.Framework     MonoGame の Keyboard／描画 adapter と Game host
  goat-shooooting.SampleGame    通常起動と production smoke-test の entry point
tests/
  goat-shooooting.Core.Tests
  goat-shooooting.Runtime.Tests
  goat-shooooting.Framework.Tests
  goat-shooooting.IntegrationTests
games/sample/                  60秒の標準コンテンツパック
games/gauntlet/                高速・縦長の第2コンテンツパック
```

依存の向きは次のとおりです。

```text
JSON Definition -> IDefinitionRepository -> Factory
                                      Factory -> Entity + Component -> System
MonoGame Input ----------------> IInputState -> ShootingSimulation
ShootingSimulation -> RenderSystem snapshot -> MonoGame renderer
```

`Core` と `Runtime` は MonoGame に依存しません。Runtime はファイル API や `JsonSerializer` を直接使わず、`IDefinitionRepository` だけを参照します。Entity と Component は update／draw ロジックを持たず、処理は System にあります。

## Definition の追加・変更

サンプルの定義は [`games/sample`](games/sample) にあります。

- `game.json`: 使用する `playerId`、`stageId`、画面サイズ
- `player.json`: HP、移動速度、初期位置、Collider 半径、被弾後の無敵時間、Weapon 参照
- `enemies/*.json`: Enemy の HP、移動速度、Collider 半径、スコア、任意の Weapon 参照
- `bullets/*.json`: Bullet の速度、Damage、Collider 半径、Lifetime
- `weapons/*.json`: Bullet 参照と cooldown
- `stages/*.json`: 時刻付き `spawn-enemy` event と出現位置

新しい JSON を対象フォルダーへ追加し、一意な `id` で参照してください。Engine コードの変更は不要です。起動時に全参照と値を検証するため、不明な Player／Stage／Enemy／Weapon／Bullet ID、重複 ID、未対応 event、0 以下の HP などは `DefinitionValidationException` になります。

例:

```json
{
  "id": "fighter-b",
  "hp": 30,
  "speed": 80,
  "radius": 16
}
```

```json
{
  "time": 7.5,
  "type": "spawn-enemy",
  "enemyId": "fighter-b",
  "x": 240,
  "y": 100
}
```

## 現在の Architecture

- **Definition**: immutable-style record による静的設定。Runtime state は保持しません。
- **Factory**: `PlayerFactory`、`EnemyFactory`、`BulletFactory` が Definition を Entity＋Component へ変換します。
- **Entity / Component**: 継承階層を使わない composition model です。`Transform`、`Velocity`、`Health`、`Damage`、`Collider`、marker、Weapon、Lifetime を World が管理します。
- **System**: 入力、射撃、移動、境界、Stage、Collision、Damage、無敵時間、Feedback、Lifetime、Cleanup、Renderをそれぞれ独立したSystemが処理します。
- **Runtime**: `ShootingSimulation.Update(deltaTime)` が production実行順序、Pause、`Running`／`StageClear`／`GameOver` の状態遷移、リトライ時のWorld再構築を統括します。1フレーム単位の `SimulationFeedback` は描画APIに依存しません。
- **Framework**: `KeyboardInputState`、`ShootingGame`、手続き生成音を扱う`GameAudio`だけがMonoGame APIを扱います。RenderSystemはrenderer-neutralなsnapshotを返し、Frameworkがフラッシュ、爆発、画面揺れ、HPバーと状態表示へ変換します。

現在の範囲は Player／Enemyによる射撃、単純な下方向 Enemy 移動、円 Collider による命中、Damage／Death、勝敗とリトライまでです。Editor、networking、save、boss や高度な ECS 最適化は含みません。
