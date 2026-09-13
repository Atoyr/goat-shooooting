# 商用弾幕シューティングへ向けたプロダクト設計

## 1. 目的

`goat-shooooting` を、1本のサンプルを動かす基盤から、複数の縦スクロール弾幕シューティングを制作し、販売品質まで仕上げられる基盤へ発展させる。

目標は既存作品を複製することではない。怒首領蜂大復活、虫姫さま、Crimzon Clover が示している次の設計上の強みを、再利用可能な機能へ分解する。

- 初心者から上級者まで遊べる複数の難易度とゲームモード
- ショット、低速移動、レーザー、ロックオン、ボム、特殊ゲージなどの機体差
- 撃破、チェイン、倍率、弾消し、アイテム、ノーミスなどが結び付くスコア設計
- 大量の弾、レーザー、複数形状の弾幕、ボスの複数フェーズ
- 練習、リプレイ、ランキング、設定、ゲームパッドを含む製品機能
- スプライト、背景、パーティクル、BGM、効果音、演出を差し替えられるコンテンツ制作環境

参考作品の公式情報では、怒首領蜂大復活は機体／スタイル、ハイパーによる敵弾相殺、カウンターレーザー、複数モードを持つ。虫姫さまは Original／Maniac／Ultra と複数難易度により弾幕の性格を変えている。Crimzon Clover は BREAK／DOUBLE BREAK、プレイ内容に応じた難度変化、Time Attack、Training、ゲージ消費型強化を備える。これらを一対一で模倣せず、下表の共通能力へ変換する。

| 参考要素 | エンジン上の共通能力 |
|---|---|
| ハイパー、BREAK、DOUBLE BREAK | 蓄積条件、段階、発動条件、継続時間、終了条件、効果を定義できる特殊ゲージ |
| チェイン、倍率、星／琥珀、弾消し点 | 型付きゲームプレイイベントを入力にする合成可能なスコアルール |
| Original／Maniac／Ultra、Novice／Arcade／Arrange | `RuleSetDefinition` と `DifficultyDefinition` の直交した選択 |
| 機体／ショットスタイル | `ShipDefinition`、複数Weapon slot、通常／低速時のLoadout |
| レーザー相殺、ロックオン | 継続攻撃、Target selection、Projectile interaction |
| プレイヤー連動の難度 | 明示的で観測可能な `RankSystem` |
| ボスの攻撃切替 | HP／時間で遷移するBoss phaseとAttack pattern timeline |
| Training、単一Stage、Time Attack | 同じSimulationを初期条件だけ変えて起動する `RunConfiguration` |

参考資料:

- [怒首領蜂大復活 システム紹介](https://www.cave.co.jp/gameonline/daifukkatu/system/index.html)
- [怒首領蜂大復活 ハイパーカウンターモード](https://www.cave.co.jp/gameonline/daifukkatu/system/03_hcm.html)
- [虫姫さま公式紹介](https://www.cave.co.jp/en/business/mushihimesama-for-smartphone/)
- [Crimzon Clover World EXplosion](https://store.steampowered.com/app/1718160/Crimzon_Clover_World_EXplosion/)

## 2. 現状の評価

2026-09-13時点で、次の土台はすでに存在する。

- `Core`、`Definitions`、`Runtime`、`Framework`、`Platform`、`Tooling` の責務分離
- renderer-neutralなheadless simulation
- Player／Enemy／Bullet、円形Collision、Damage、残機、ボム、無敵時間
- 直進／sine／zigzag移動、直進／追尾弾、spread／washing-machine系射撃
- 時刻指定Wave、ボス指定、複数Stage、Stage result
- キーボード／ゲームパッド、メニュー、設定、ユーザーデータ保存
- 論理解像度、レターボックス、win-x64 publish、Steam beta投入手順
- JSON検証、ブラウザEditor、ホットリロード、自動テスト

現行テストは97件すべて成功している。ただし商用弾幕STGの核としては、次がボトルネックになる。

- `World.Query` とEntity内の型Dictionaryは、大量Projectileの毎フレーム処理で割り当てと探索コストが増える。
- CollisionはBulletとTargetの全組み合わせを調べるため、弾数に対して伸びにくい。
- 可変`deltaTime`、`Random.Shared`、量子化されていない入力では、再現可能なReplayを作れない。
- Enemyの移動と射撃は一つのpattern名と一定cooldownだけで、入場、停止、退場、複数段階弾幕を記述できない。
- Playerは単一Weaponで、低速、レーザー、Option、Power level、Item取得、死亡／復帰シーケンスを持たない。
- ScoreはEnemy撃破時の固定加算だけで、得点過程を説明・検証できない。
- 描画は1px Textureによる矩形、音声は手続きToneで、Asset pipelineと演出定義がない。
- Profileのscore keyは`gameId`だけで、ruleset、difficulty、ship別の記録を持てない。

結論として、次に画像を大量追加するより、Replay可能なSimulation、Projectile専用hot path、ゲームプレイイベント、定義v2を先に作る。これらがない状態で機能を足すと、スコア、難易度、演出が互いに直接依存し、後から分離するコストが高くなる。

## 3. リリースする最初の1本の範囲

エンジンは複数ルールを表現できるようにするが、最初の製品で全参考作品の全モードを同時に作らない。最初のRelease Candidateは次を目安とする。

| 項目 | Release Candidateの基準 |
|---|---|
| ゲームモード | signature mode 1種、score attack、training |
| 難易度 | Novice、Arcade、Expertの3段階 |
| 機体 | 性格の異なる2〜3機、各機に通常／低速攻撃 |
| ステージ | 5 stage、20〜30分で1周、各stageにboss |
| 敵／弾幕 | 雑魚12種以上、boss phase 15以上、再利用pattern 30以上 |
| スコア | 30秒以内に理解でき、上達で明確に伸びる主ループ1本 |
| 製品機能 | Replay、stage／boss training、local leaderboard、設定、gamepad |
| Presentation | 製品用sprite、背景、HUD、BGM、SE、hit／destroy／cancel演出 |
| 品質 | 60Hz fixed tick、想定最大弾数で安定、全経路をcontrollerだけで操作可能 |

2周目、隠しboss、local co-op、Steam leaderboard／achievement、追加Arrange modeは、最初の1本が完成してから追加する。設計には拡張点を残す。

### 3.1 推奨するsignature rule: SYNC DRIVE（仮称）

実装の判断基準を揃えるため、最初のvertical sliceでは次のオリジナルruleを仮採用する。playtestで面白くなければ数値や名称は変えるが、P0〜P15はこのruleを表現できることを受入基準にする。

1. 敵への近距離攻撃、graze、連続撃破で3segmentのSYNC gaugeを溜める。
2. Specialを押すと1〜3segmentを任意消費し、消費量に応じたSYNC DRIVEを一定時間発動する。
3. 発動中は火力とscore倍率が上がり、focus shotで`soft`属性の敵弾を相殺できる。危険弾やlaserは相殺できない。
4. 相殺弾と敵撃破からSYNC shardが出現する。上部collection lineでまとめて回収すると倍率をbankできる。
5. 未bank倍率は時間で減衰し、被弾で大きく失う。高level DRIVEは得点機会が増える一方、Rankも上がり追加emitterが有効になる。
6. Boss phaseは残り時間、no-miss、no-bomb、bank済み倍率からbonusを計算する。

初見では「ゲージを溜めて発動し、光る弾を消して回収する」で遊べる。上級者は発動位置、level、近距離、回収タイミング、上昇Rankを管理してscoreを伸ばす。この二層構造をscoring tutorialと最初の2 stagesで段階的に教える。

このruleは、特殊ゲージ、弾属性、cancel、item、collection line、multiplier、bank、rank、boss bonusを横断するacceptance scenarioになる。各機能をSYNC DRIVE専用クラスへ結合せず、RuleSetで組み上げること。

## 4. 設計原則

### 4.1 Static Definition、Run Configuration、Runtime Stateを分ける

```text
Static Definition
  Ship / Weapon / Projectile / Enemy / Pattern / Stage / RuleSet / Difficulty / Visual / Audio
                         +
RunConfiguration
  gameId / ruleSetId / difficultyId / shipId / seed / startStage / checkpoint
                         ↓
Runtime State
  World / projectiles / score / chain / gauges / rank / frame / RNG state
```

- JSONはゲーム制作者が配る不変の設定だけを持つ。
- プレイヤーが選ぶモード、難易度、機体、seed、練習開始地点は`RunConfiguration`へ入れる。
- score、power、chain、rank、特殊ゲージは`RunState`へまとめ、Telemetryを正式な状態保存先にしない。
- SettingsとProfileは引き続き`Platform`に置き、Definitionへ混ぜない。

### 4.2 Simulationの事実を型付きEventとして公開する

System間を巨大な条件分岐で接続しない。Simulation内で発生した事実を、フレーム番号と通番を持つ型付きEventとして収集する。

```csharp
public interface IGameplayEvent
{
    long Frame { get; }
    int Sequence { get; }
}

public sealed record EnemyDamagedEvent(... ) : IGameplayEvent;
public sealed record EnemyDestroyedEvent(... ) : IGameplayEvent;
public sealed record PlayerGrazedEvent(... ) : IGameplayEvent;
public sealed record ProjectileCancelledEvent(... ) : IGameplayEvent;
public sealed record ItemCollectedEvent(... ) : IGameplayEvent;
public sealed record PlayerHitEvent(... ) : IGameplayEvent;
public sealed record BossPhaseEndedEvent(... ) : IGameplayEvent;
```

`ScoreSystem`、`GaugeSystem`、feedback、audio、achievement bridgeは同じEvent streamを読む。描画や音声がSimulationのEntityを再走査して出来事を推測しない。イベントは1 tickだけ保持し、長期集計は`RunState`へ反映する。

### 4.3 60Hz固定tickと再現可能な入力を中心に置く

- Runtimeの進行単位を`Tick(InputFrame)`へ変更し、1 tickを1/60秒に固定する。
- Frameworkは実時間をaccumulatorへ入れ、0回以上のtickを進め、描画だけ補間率を使う。
- `InputFrame`はbutton bit flagsと量子化した移動軸を持つimmutable valueとする。
- 乱数は`IRandomSource`へ注入し、run seedを保存する。Simulationで`Random.Shared`を使わない。
- Replayは`RunConfiguration`、content hash、engine/replay version、tickごとの入力差分を保存する。
- 同じbuild、同じcontent hash、同じseedでの再現を保証範囲とする。異なるbuild間の永久互換は約束せず、version不一致を明示する。
- 一定間隔のstate hashをReplayへ記録し、desyncしたframeを診断できるようにする。

### 4.4 Projectileだけは専用のdata-oriented storeにする

Actor、Item、Effectは既存ECSを段階的に使い続ける。数千〜数万になるProjectileは`ProjectileStore`へ分離する。

```text
ProjectileStore (dense arrays / pool)
  id, active, owner, position, previousPosition, velocity,
  radius, damage, age, lifetime, visualId, behaviorState, cancelFlags
```

- spawn／remove中に配列を変更せず、command bufferをtick境界で反映する。
- Colliderはuniform gridへ登録し、Projectile対Actorの候補を絞る。
- 高速弾はpreviousPositionからcurrentPositionへのswept circleで判定する。
- graze radiusとhit radiusを分け、同一Projectileから同一Playerへのgrazeは1回にする。
- Benchmarkは通常テストと分離し、10,000 active projectilesを基準シーンとして計測する。時間の閾値を不安定なCI unit testにはしない。
- hot loopではLINQ、一時配列、boxingを避ける。通常ECS全体の置換は、計測で必要と判明するまで行わない。

### 4.5 Definitionのtype文字列はRegistryで解決する

`if (MovementPattern == "...")`を増やし続けない。Runtimeに次のRegistryを置く。

- `IProjectileBehaviorFactory`
- `IActorMotionFactory`
- `IFirePatternFactory`
- `IScoreRuleFactory`
- `ISpecialGaugeRuleFactory`
- `IStageEventHandler`

Definitionは`type`と型ごとのparameterを保持する。Definitions層はJSONの構造、範囲、参照、循環、上限を検証し、Runtimeの`CapabilityValidator`がtypeとparameter contractを検証する。未認識typeは起動時にファイルとJSON pathを含むエラーにする。

任意コードをJSONから実行する汎用script言語は導入しない。まず有限個の安全なcommandを組み合わせるtimelineを採用する。

### 4.6 P0実装注記（契約と計測基準）

P0では既存ゲームの進行を変更せず、後続Phaseが共有する次の契約だけをRuntimeへ追加した。

- `SimulationTiming`は正式なtick rateを60Hzとして一箇所に定義する。`ShootingSimulation.Update(float)`をfixed tickへ移すのはP1とし、P0では互換APIと挙動を維持する。
- `RunConfiguration`はrun開始時の不変な選択とseed、`RunState`はrun中に蓄積するframeとscoreの最小状態を所有する。v1 contentにはRuleSet／Difficulty／Ship選択がないため、それらのIDは移行完了までoptionalとする。
- `InputFrame`は移動軸を`-127..127`へ量子化し、buttonをflagsとして保持する。`sbyte.MinValue`は正負の対称性を壊すため使用しない。Focus／Special bitは予約するが、入力経路への接続はP1で行う。
- `GameEventBuffer`は`BeginTick(frame)`で前tickのeventを破棄し、factoryへframeと0始まりのsequenceを渡す。異なるstampのeventは拒否し、eventの長期保存はbufferの責務にしない。
- filesystem、JSON出力、wall-clock計測はToolingへ閉じ込め、Core／Runtimeのrenderer-neutralかつfile API非依存な境界を維持する。

計測は`dotnet run --project src/goat-shooooting.Tooling -- benchmark games/sample`で再実行できる。固定workloadは、入力パターンを固定したsampleの600 updatesと、10,000個の現行Bullet entityを生成したstressの1 updateである。JSON format version、tick rate、active entity／bulletの初期・最大・終了数、update時間、update中allocation、決定的な結果checksumを出力する。時間とallocationは比較用artifactでありCIの合否条件にしない。

2026-09-13のP0 baseline（Debug、同一worktree）は、sampleが22.819 ms／1,813,936 bytes（最大14 entities／12 bullets）、stressが3,649.626 ms／2,084,744 bytes（10,001 entities／10,000 bullets）だった。環境差とJIT差があるため絶対値ではなく、P2移行前後を同一環境・同一commandで比較する。

## 5. Definition v2

すべてを一度に巨大な`game.json`へ入れず、次の単位を追加する。

```text
games/<gameId>/
  game.json
  ships/*.json
  weapons/*.json
  projectiles/*.json
  enemies/*.json
  items/*.json
  patterns/*.json
  bosses/*.json
  stages/*.json
  rulesets/*.json
  difficulties/*.json
  visuals/*.json
  audio/*.json
  assets/**
```

既存`player.json`と`bullets/*.json`はv1として読み続ける。v2 loaderは明示的な`schemaVersion`を読み、memory上で最新モデルへ移行する。既存sample／gauntletは移行完了までgolden fixtureとして残す。

### 5.1 Game、RuleSet、Difficulty

```json
{
  "schemaVersion": 2,
  "id": "product-game",
  "defaultRuleSetId": "signature",
  "ruleSetIds": ["signature", "score-attack"],
  "difficultyIds": ["novice", "arcade", "expert"],
  "shipIds": ["type-a", "type-b"],
  "stageRouteId": "main-route"
}
```

`RuleSetDefinition`は、stage route、time limit、continue可否、初期resource、score rule IDs、special gauge rule、rank rule、clear conditionを構成する。`DifficultyDefinition`は弾速、発射間隔、同時弾数、敵HP、item量、auto-bomb、rank初期値／上限などのmodifierを持つ。

難易度を単純な全体倍率だけにしない。pattern側でdifficulty tagごとの差分を指定できるようにし、Noviceでは弾数を減らし、Arcadeでは速度を変え、Expertでは追加emitterを有効にできるようにする。

### 5.2 Ship、Weapon、Power

`ShipDefinition`は次を持つ。

- hit radius、graze radius、normal speed、focus speed
- initial lives／bombs／power、respawn、invincibility、power loss
- normal／focusごとのweapon slot一覧
- optional laser、lock-on、option／support unit
- power thresholdごとのweapon modifier
- bomb definition、special action mapping、visual／audio IDs

Weaponは単発Bullet参照ではなく、複数の`EmitterDefinition`を持つ。Emitterはorigin offset、angle source、count、spread、speed bands、burst、interval、difficulty conditionを定義する。

### 5.3 MotionとAttack timeline

Enemyの動きと攻撃を分ける。

- Motion command: `enter`、`move-to`、`move-by`、`follow-path`、`orbit`、`wait`、`leave`
- Attack command: `fire`、`start-pattern`、`stop-pattern`、`wait`、`repeat`、`parallel`
- angle source: fixed、aim-at-player、current-heading、rotating
- distribution: single、fan、ring、arc、random-arc、layers
- speed: fixed、range、layers、accelerating、decelerating

各timelineには最大command数、最大repeat、最小interval、理論上の最大spawn数を検証するbudgetを設ける。Editor previewが無限loopで固まらないことをDefinition validationで保証する。

### 5.4 Boss

`BossDefinition`は複数の`BossPhaseDefinition`を持つ。

- phase id、display name、HP、time limit
- motion timeline、attack pattern IDs
- timeout時の扱い、開始／終了時の敵弾cancel
- base bonus、time bonus、no-miss／no-bomb bonus
- invulnerability window、phase transition演出
- practice checkpoint id

Stage clearを`BossComponent`の撃破数だけに依存させず、Stage objectiveが全Boss phase完了を観測する。Boss本体のEntityをphase間で維持できるようにする。

### 5.5 ItemとDrop

Item kindは`power`、`score`、`bomb`、`life`、`gauge`を標準実装し、値とvisualをDefinition化する。

- Enemy／boss phaseはdrop tableを参照する。
- Itemはspawn、scatter、fall、magnet、collection line、collectの状態を持つ。
- 画面上部のcollection line、focus中の吸引、全回収条件をルールとして設定できる。
- 最大power時のpower item変換などはScore／Gauge eventへ接続する。

## 6. Runtime System設計

### 6.1 1 tickの順序

順序はゲームルールの一部なので、テストで固定する。

```text
1. InputFrameを確定
2. pause／run controlを処理
3. stage／boss timelineを進め、spawn commandを積む
4. player action、weapon、special actionを評価
5. actor motionとprojectile behaviorを更新
6. boundsとspatial indexを更新
7. projectile interaction／cancel／graze／hitを検出
8. damage、death、boss phase、player life cycleを解決
9. item spawn／motion／collectionを解決
10. typed GameplayEventをscore／gauge／rank／extendsへ適用
11. cleanupとcommand bufferを反映
12. immutable FrameSnapshotとFeedbackEventを公開
13. replay state hashを必要なframeで計算
```

同一tickの競合規則も明示する。例として、auto-bomb判定はPlayerHit確定前、manual bombはcollision前、boss撃破scoreはphase終了event後、1 projectileは原則1 targetへだけdamageを与える。

### 6.2 Player life cycle

現在の「残機を減らしてその場で継続」を、次の状態機械へ置き換える。

```text
Active -> HitPending -> BombRescue | Dying -> Respawning -> Invincible -> Active
                                      └-> GameOverPending
```

- hit stopとdeath animation中は入力、攻撃、当たり判定の扱いを明示する。
- manual bombとauto-bombを区別し、消費量とscore penaltyをルール化する。
- death時の敵弾cancel、item drop、power loss、bomb restockをRuleSet／Shipで決める。
- Extendはscore thresholdまたはitemで付与し、各thresholdはrunにつき1回だけにする。
- Continueはscore／replay／leaderboard上で別run扱いにできるよう`Continued`を記録する。

### 6.3 攻撃とProjectile interaction

標準攻撃として次を表現可能にする。

- tap／hold shot
- focus shotとfocus移動
- 継続laserとlaser hit interval
- lock-on acquisition、target marker、release attack
- option／support unitの追従配置
- charge shot
- bomb
- special gauge activation

Projectileにはteamだけでなく、`CanDamage`、`CanBeCancelled`、`CancelResistance`、`PierceCount`、`DamageType`、`ClearBehavior`を持たせる。レーザーと敵レーザーのinteractionも専用Systemで扱い、Collisionの特殊caseへ埋め込まない。

### 6.4 Score

Scoreは`long`を使う。加算のたびに理由を持つ`ScoreAwardedEvent`を発行する。

標準rule component:

- base enemy／boss phase value
- chain countとchain timeout
- hit comboまたはcontinuous damage counter
- multiplier
- point-blank距離bonus
- graze
- projectile cancel
- item valueと連続取得による成長
- boss time／no-miss／no-bomb bonus
- stage clear／all clear bonus
- life／bomb残数換算
- extend thresholds

`RuleSetDefinition`は必要なruleだけを順番付きで組み合わせる。UIは`RunState`の表示用fieldと直近`ScoreAwardedEvent`を使う。最終scoreだけでなく、stage別内訳、最大chain、graze、cancel数、miss、bomb、phase bonusをresultsへ残す。

### 6.5 Special gaugeとRank

特殊ゲージは`Inactive`、`Active(level)`、`Cooldown`を基本状態とし、ruleごとに次を定義する。

- charge sources: damage、kill、graze、cancel、item、lock count
- activation: manual、full時automatic、段階発動
- drain: per tick、被弾／bombで終了、killで延長
- effects: damage、fire rate、bullet cancel、invincibility、score multiplier、visual／audio cue

Rankは隠れた謎の値にせず、少なくともdebug HUDとReplay metadataから観測可能にする。加算源、減算源、clamp、弾速／発射頻度／追加emitterへのmappingをDefinition化する。難易度は選択した基準、Rankはrun中の動的変化として分離する。

## 7. Rendering、Audio、UI

### 7.1 Asset境界

Runtimeはasset fileやMonoGame型を参照せず、snapshotに`visualId`、animation state、transform、tint、layerだけを出す。Frameworkの`IVisualAssetCatalog`がIDをTexture／sprite region／animationへ解決する。

標準visual:

- sprite sheet animation、origin、scale、rotation、flip、tint
- actor、projectile、item、laser、shadow、effectのrender layer
- scrolling／parallax background
- additive particle、trail、muzzle flash、hit spark、destroy、bullet cancel
- player hitbox表示、graze ring、lock marker
- boss name、HP、phase、timer
- score、high score、chain、multiplier、power、gauge、rank、stage、lives、bombs

最初はSpriteBatchで実装し、bloom／shaderはoptional post-process passとして追加する。ゲーム進行上必要な弾outlineとhitboxはpost-processなしでも見えるようにする。

`assets/manifest.json`にasset ID、相対path、種類、sprite metadataを置く。path traversal、重複ID、存在しないfile、範囲外source rectangleを起動前に検証する。開発時hot reloadとRelease時の不足asset検査を同じcatalogで行う。

### 7.2 Audio

- stage／bossごとのBGM cue、loop start／end metadata
- shot、laser、hit、destroy、item、graze、bomb、special、warning、menuのSE cue
- 同一SEの同時発音数制限、priority、pitch variation、cooldown
- Master／BGM／SE／Voice volume、mute
- boss warningやspecial発動時のducking／crossfade

Simulationは`AudioCueEvent`相当の意味イベントを出し、Frameworkが実際の再生を行う。大量hitで毎tickすべての音を鳴らさず、Audio側で集約する。

### 7.3 UIと画面遷移

`GameShell`を次へ拡張する。

```text
Boot -> Title -> ModeSelect -> DifficultySelect -> ShipSelect -> Playing
                     |                                  |
                     +-> TrainingSetup -> Playing       +-> Pause
Playing -> StageResult -> Playing | RunResult -> NameEntry -> Leaderboard -> Title
```

Resultはscoreだけでなく、stage、difficulty、ship、max chain、graze、miss、bomb、continue、clear、play timeを表示する。初回起動tutorial、操作説明、scoring help、credits、license表示を用意する。

### 7.4 AccessibilityとLocalization

- controller／keyboardの再割り当てとglyph切替
- screen shake、flash、particle density、背景明度、bullet outline／palette
- HUD scale、safe area、window／borderless、TATE向けrotation候補
- pause時に即時再開、タイトルへ戻る前の確認
- UI文字列をstring catalogへ分離し、日本語／英語を最低対応
- 色だけで危険度を伝えず、形、outline、明度も使う

## 8. Replay、Training、Leaderboard

### 8.1 Replay

Replay file:

```text
header: replayVersion, engineVersion, contentHash, RunConfiguration, createdAt
body:   run-length encoded InputFrame changes
check:  periodic frame/state hashes
result: score breakdown, clear, endFrame, final hash
```

- Replay再生中は入力をfileから供給し、pause／speed changeはSimulation外のviewer controlとする。
- incompatible replayは理由を表示し、黙って再生しない。
- local leaderboardのentryからReplayを開ける。
- Replay fileはuntrusted inputとして長さ、frame数、enum、checksumを検証する。

### 8.2 Training

Trainingは別Runtimeを作らず、`RunConfiguration`で開始stage、boss checkpoint、power、lives、bombs、rank、gauge、invincibilityを指定する。選択UIから同じproduction simulationを起動する。

training専用機能:

- stage／boss phase選択
- 初期resourceとrank設定
- 即時retry
- practice中であることを明示し、official scoreへ登録しない
- optional slow playback／hitbox表示／無敵はReplay metadataへ残す

### 8.3 ProfileとLeaderboard

score keyを構造化する。

```csharp
public readonly record struct ScoreCategoryKey(
    string GameId,
    string RuleSetId,
    string DifficultyId,
    string ShipId);
```

Profile schema v2はcategory別best score、clear count、best stage、play count、play time、unlocksを保持する。local leaderboard entryはscore breakdownとReplay pathを持つ。Steam bridgeは後から同じcategoryをSteam leaderboardへ写せる境界にするが、Steam SDKをRuntimeへ入れない。

## 9. Tooling

Definition Editorを段階的に次へ伸ばす。

- schema v2のform編集とcross-reference validation
- motion pathのcontrol point編集
- emitter／patternのプレビュー、弾数budget表示
- boss phase timelineとtime／HP条件編集
- difficulty差分の重ね表示
- stage timeline上のenemy、BGM、background、warning event
- sprite sheet region／animation preview
- scoring event traceと1waveの期待score表示
- benchmark sceneとReplay regressionの起動

Editor previewとproduction Runtimeでpattern計算コードを共有し、JavaScript側へ別実装を複製しない。必要ならToolingからheadless Runtime APIを呼び、そのsnapshotをCanvasへ渡す。

## 10. Testと観測性

### Unit

- Definitionの正常／境界／不明参照／cycle／budget超過
- 各motion、emitter、projectile behavior
- collision、swept collision、graze一回性、cancel耐性
- score ruleのevent列に対するscore breakdown
- gauge、rank、extend、auto-bomb、life cycle
- boss phaseのHP／timeout／cancel遷移
- profile migration、score category、Replay parser

### Integration

- seedとinput列が同じならframe hashと結果が一致する
- 既存sample／gauntletがv1互換loaderで完走する
- Novice／Arcade／Expertで同じstage routeを完走できる
- stage startからboss phase、result、次stage、all clearまで
- death、respawn、continue、game over、retryで状態が漏れない
- Replay record後のplaybackがfinal hashとscoreを再現する
- Training開始条件が本番runへ混入しない

### Performance／visual QA

- benchmarkはactive projectile数、tick時間、allocation、collision候補数を出力する。
- CIは機能的なstress testを実行し、性能値はartifactとして比較する。環境依存のwall clockだけでbuildを落とさない。
- 代表sceneのscreenshotを保存し、HUD欠け、layer順、letterbox、paletteを確認する。
- 最低動作環境で10,000 projectiles、effect、BGM／SE同時再生時に60fpsを維持できるかRelease QAで測る。

## 11. 実装順序と依存関係

```text
P0 契約・計測基準
 └─ P1 fixed tick / input / RNG / event stream
     ├─ P2 ProjectileStore / collision / graze
     └─ P3 Definition v2 / registries
          ├─ P4 ship / focus / weapons / laser / option
          ├─ P5 item / power / death / extend / continue
          └─ P6 motion / attack timeline
               └─ P7 boss phases
P2 + P3 + P4 + P5 + P7
 └─ P8 scoring
     └─ P9 special gauge / rank / difficulty / ruleset
         ├─ P10 mode selection / profile / local leaderboard
         └─ P11 replay / training
P3 + P4 + P5 + P7 + P8
 └─ P12 sprite / background
     └─ P13 effect / HUD / visibility
         └─ P14 BGM / SE / localization / accessibility
P3 + P6 + P7 + P12
 └─ P15 Editor v2
P0..P15
 └─ P16 product vertical slice / balance / release QA
```

各Phaseは独立したbuild可能な変更にする。Definition v2が完了するまで既存v1 contentを壊さず、各Phaseの終了時に全テスト、Definition validation、sample／gauntlet smoke testを通す。

## 12. Release gate

コード完成だけでは商用リリースにならない。次をすべて満たした時点をRelease Candidateとする。

- 5 stageをcontinueなしで開始からendingまで完走できる
- 全difficulty、全ship、training、Replay、local leaderboardの主要経路が動く
- scoring tutorialを読んだ初見プレイヤーが得点の増減理由を説明できる
- 10名以上の外部playtestで進行不能、入力不能、視認不能の重大問題がない
- crash-freeな長時間soak testと最低動作環境での性能確認を終えている
- controllerだけでbootから終了まで操作できる
- save破損、device切断、Alt+Tab、表示切替、音声deviceなしで進行不能にならない
- 日本語／英語のUIが欠けず、credits、license、privacy方針が揃っている
- 使用する画像、font、音源、shader、middlewareの配布権を確認し、台帳とnoticeを残している
- store capsule、screenshot、trailer、説明文、system requirements、support窓口を用意している
- Release build、definition validation、smoke、Replay regression、artifact検査がCIで成功する

## 13. 対象外と拡張点

最初のRelease Candidateでは必須にしない。

- online co-op、rollback netcode
- user codeを実行するmod／script plugin
- cross-versionで永久互換なReplay
- full ECS rewrite
- procedural campaign
- console certification

ただし複数Player entity、version付きDefinition／Replay、`ILeaderboardService`、platform bridgeを境界として残し、将来のlocal co-op、Steam leaderboard、achievement、cloud、追加rule setをRuntimeの全面改修なしで追加できるようにする。
