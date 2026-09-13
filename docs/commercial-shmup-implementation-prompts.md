# 商用弾幕STG実装用 Codex プロンプト集

## 使い方

各プロンプトは、新しいCodexセッションへ1つずつ、番号順に渡す。1セッションで複数Phaseを混ぜない。先行Phaseが未完なら、そのセッションでは先行Phaseの不足を解消してから対象Phaseを完了する。

各セッションの開始時に必ず現在のworktreeとテスト結果を確認する。別セッションの変更を前提にするため、ここに書かれたファイル名や型名より、最新コードと設計意図を優先する。ユーザーの未commit変更を破棄、上書き、resetしない。

進捗:

- [x] P0 契約と計測基準
- [ ] P1 固定tick、入力、乱数、Event stream
- [ ] P2 Projectile hot path、collision、graze
- [ ] P3 Definition v2とRegistry
- [ ] P4 Ship、focus、複数攻撃、laser、option
- [ ] P5 Item、power、death、extend、continue
- [ ] P6 Motion／Attack timeline
- [ ] P7 Boss phase
- [ ] P8 Score engine
- [ ] P9 Special gauge、Rank、Difficulty、RuleSet
- [ ] P10 Mode選択、Profile v2、Local leaderboard
- [ ] P11 ReplayとTraining
- [ ] P12 Asset catalog、Sprite、Background
- [ ] P13 Effect、HUD、視認性
- [ ] P14 Audio、Localization、Accessibility
- [ ] P15 Definition Editor v2
- [ ] P16 製品vertical sliceとRelease QA

Phaseを完了したセッションは、この進捗欄の該当項目だけを`[x]`へ更新する。部分実装では完了扱いにしない。

## 全体を任せる場合のmaster prompt

```text
このリポジトリを、docs/commercial-shmup-architecture.mdの設計に沿って商用弾幕シューティングを制作できる状態へ進めてください。

まずAGENTS.md、docs/commercial-shmup-architecture.md、docs/commercial-shmup-implementation-prompts.md、現在のコードとgit diffを読み、進捗欄で最初の未完了Phaseを1つ選んでください。このセッションではそのPhaseを、実装、必要なmigration、テスト、文書更新まで完了してください。先行Phaseに実際の不足がある場合だけ併せて修正し、後続Phaseを先取りしないでください。

成功条件:
- 現在の責務境界を守り、CoreとRuntimeをMonoGame／file APIへ依存させない
- 既存sample／gauntletと保存データの互換性を、そのPhaseで明示的に廃止していない限り維持する
- 変更した振る舞いの成功・境界・失敗caseを自動テストで保護する
- dotnet build goat-shooooting.sln、dotnet test goat-shooooting.sln、両content packのDefinition validationとsmoke testを成功させる
- 広範な変更ではdotnet format --verify-no-changesも成功させる
- ユーザーの既存変更を破棄せず、無関係な整形やrefactorを混ぜない
- Phaseが完全に完了した場合だけ進捗欄を更新する

最後に、実装した契約、互換性への影響、テスト結果、残るrisk、次に実行すべきPhaseを簡潔に報告してください。計画だけで終了せず、実装と検証まで完遂してください。
```

## P0: 契約と計測基準

```text
P0「契約と計測基準」を実装してください。AGENTS.mdとdocs/commercial-shmup-architecture.mdを読み、最新のworktreeと既存diffを確認してから着手してください。

目標は、後続のfixed tick、Replay、Score、Difficultyが共有する最小の型と、現行性能を測る再現可能な基準sceneを作ることです。挙動はまだ変えません。

必要な成果:
- RuntimeにRunConfiguration、RunStateの最小モデル、Simulation tick rate定数、量子化可能なInputFrame valueを追加する
- frame番号とsequenceを持つIGameplayEvent、および後続Phaseが使う主要event型の骨格を追加する。未使用の巨大な汎用payloadは作らない
- eventを1 tick単位で収集・公開するGameEventBufferを追加する
- Toolingまたは専用console projectに、active entity／bullet数、update時間、allocationを出力するheadless benchmark commandを追加する。外部benchmark packageは必要性が明確な場合だけ導入する
- 現行sample相当と10,000 bullet stress相当の入力を固定し、結果をJSONまたは安定したtextで出力する
- 主要な設計判断を短いADRまたは同設計文書の実装注記として残す

境界:
- CoreとRuntimeへMonoGame、filesystem、JSON serializerを入れない
- 現行ShootingSimulationのpublic利用方法とsample／gauntletの挙動を壊さない
- wall-clock性能値をCIのpass/fail条件にしない。機能上の上限と結果整合性だけをtestにする

検証は全build／test、両Definition validation、両smoke test。完了時だけ進捗欄のP0を更新し、変更、計測baseline、テスト結果を報告してください。
```

## P1: 固定tick、入力、乱数、Event stream

```text
P1「固定tick、入力、乱数、Event stream」を完全実装してください。P0の成果とdocs/commercial-shmup-architecture.mdを読み、現在のコードを事実として扱ってください。

目標は、同じRunConfiguration、seed、InputFrame列から同じSimulation結果を再現できるproduction経路です。

必要な成果:
- Runtimeの正式な進行APIを60HzのTick(InputFrame)中心へ移し、frame番号をRunStateに保持する
- Framework側に実時間accumulatorを置き、描画frameとsimulation tickを分離する。極端なstall時の最大catch-upと残時間処理を定義する
- 現行IInputStateからInputFrameを1 tickにつき一度だけcaptureする。移動軸をReplay可能な値へ量子化し、Focus／Special用bitも予約または実装する
- IRandomSourceとseed付き実装をRuntimeへ導入し、Simulation結果に影響する乱数でRandom.Sharedや時刻seedを使わない
- 各tickの型付きGameplayEventを順序付きで公開し、現行SimulationFeedbackはそのeventから互換生成する
- 同じ入力列の2 runでWorld／RunStateのcanonical hashが一致し、異なるseedまたは入力で差が出るintegration testを追加する
- pause、opening、results、retry、hot reload時のframe／RNG／event buffer規則をテストする

互換性:
- public Update(float)を直ちに削除する必要はない。互換adapterにする場合、可変deltaTimeを直接Systemへ渡さない
- 既存sample／gauntletの体感速度とstage時刻を維持する
- presentationだけの画面揺れ乱数はReplay hashへ含めない

全build／test、Definition validation、smoke、formatを実行してください。P1が完了した場合だけ進捗欄を更新し、決定性の保証範囲も文書化してください。
```

## P2: Projectile hot path、Collision、Graze

```text
P2「Projectile hot path、Collision、Graze」を実装してください。P1のfixed tickとevent contractを維持し、まずbenchmarkで現状値を記録してください。

目標は、10,000発級の弾幕を扱えるデータ構造へBullet処理を移し、当たり判定の正しさと視認性に必要なgraze情報を得ることです。

必要な成果:
- dense arraysとfree-listまたは同等の再利用機構を持つProjectileStoreをRuntimeへ追加する
- spawn／remove command bufferをtick境界で反映し、iteration中の構造変更をなくす
- 現行BulletFactory、WeaponSystem、movement、lifetime、out-of-bounds、render captureをProjectileStoreへ移行する
- owner/team、hit radius、damage、previous/current position、lifetime、visualId、behavior state、cancel属性を保持する
- actor colliderのuniform spatial gridを追加し、候補抽出後にcircleまたはswept-circleで判定する
- 高速弾のtunneling防止、1弾1target、pierce拡張点、同じ弾から同じplayerへのgraze一回性を実装する
- ProjectileSpawned、ProjectileHit、PlayerGrazed、ProjectileCancelled eventを発行する
- 既存弾幕の挙動互換testと、10,000 active projectilesを600 tick処理する機能stress test／benchmarkを追加する

境界:
- Actor ECS全体を根拠なく書き換えない
- hot loopでLINQ、一時配列、boxingを避け、allocationをbenchmarkへ表示する
- 性能のwall-clock値だけでCIを落とさない

全build／test、Definition validation、smoke、formatを通し、before／after計測値を報告してください。完了時だけP2を更新してください。
```

## P3: Definition v2とRegistry

```text
P3「Definition v2とRegistry」を実装してください。docs/commercial-shmup-architecture.mdのDefinition v2を基準にしつつ、既存sample／gauntletを壊さないmigrationを最優先してください。

目標は、Ship、Item、Pattern、Boss、RuleSet、Difficulty、Visual、Audioを後続Phaseで追加でき、typeごとの処理をswitchの増殖なしに登録できるcontent contractです。

必要な成果:
- schemaVersion付きDefinition v2 loaderと、v1 player／bullet／weapon／enemy／stageをmemory上の最新モデルへ変換するmigrationを追加する
- Ship、Projectile、Item、Pattern、Boss、RuleSet、Difficulty、Visual、Audioの最小recordとcatalog lookupを追加する。後続ロジックを先に実装する必要はない
- type＋parameterを解決するRuntime Registry群とCapabilityValidatorを追加し、組み込みの現行motion／fire／projectile behaviorをregistry経由へ移す
- Definitions側は構造、数値範囲、参照、cycle、timeline budgetを、Runtime側は対応typeとparameter contractを検証する
- unknown propertyを拒否する現在の安全性、file名／JSON path付きerror、hot reloadのlast-known-goodを維持する
- schemasをv2へ更新し、Tooling validateがv1／v2の両方で機能する
- v1 sample／gauntletのgolden integration testと、小さなv2 fixtureの成功／失敗testを追加する
- migration方針と互換性をREADMEまたは設計文書へ記載する

巨大な汎用script、式評価器、DI containerは導入しないでください。未実装機能は明示的なcapability errorにし、黙って無視しないでください。全build／test、両validation／smoke、format後にP3を更新してください。
```

## P4: Ship、Focus、複数攻撃、Laser、Option

```text
P4「Ship、Focus、複数攻撃、Laser、Option」を実装してください。Definition v2、fixed tick、ProjectileStoreを利用し、player固有処理をFrameworkへ漏らさないでください。

目標は、機体とshot styleをJSONで選び、通常移動／低速移動と複数の攻撃方式を組み合わせられることです。

必要な成果:
- IInputState／InputFrame／GameInputStateへFocusとSpecialを正式追加し、keyboard／gamepadのdefault mapping、設定、表示を接続する
- ShipDefinitionのnormal speed、focus speed、hit／graze radius、normal／focus loadout、bomb／special参照をRuntime生成へ接続する
- Weaponを複数slotと複数Emitter対応にし、offset、fan、ring、burst、fire interval、angle source、speed layersを扱う
- hold shot、focus shot、継続laser、lock-on acquisition／release、option unit追従の組み込み実装を追加する
- laser damage intervalと敵laser／projectile interactionの境界を定義する
- power level modifierを受け取れる構造にするが、Item取得はP5で実装する
- Render snapshotへship、option、laser、lock marker、hitbox表示に必要なrenderer-neutral情報を追加する
- 性格の異なるv2 test shipを最低2つ用意し、headless testで移動速度、発射、target選択、cooldown、damageを検証する

現行v1 player-basicの操作とsmokeを維持してください。全build／test、validation／smoke、format後にP4を更新してください。
```

## P5: Item、Power、Death、Extend、Continue

```text
P5「Item、Power、Death、Extend、Continue」を実装してください。現在のLivesComponentの単純減算を、設計文書のplayer life cycleへ置き換えてください。

目標は、被弾から復帰までのゲームらしい流れと、drop／回収／強化がすべてheadlessで検証できることです。

必要な成果:
- Active、HitPending、BombRescue、Dying、Respawning、Invincible、GameOverPendingの状態遷移を実装する
- manual bombとauto-bombを区別し、判定順、消費、無敵、敵弾cancel、score eventを明確にする
- death animation、respawn位置、invincibility、power loss、bomb restock、画面clearをDefinition／RuleSetから適用する
- Item entity／storeとpower、score、bomb、life、gaugeの標準item kindを追加する
- drop table、scatter、fall、magnet、collection line、focus吸引、最大power時変換を実装する
- score threshold／itemによるExtendを重複なしで実装する
- Continue可否、credit、continued flag、run resultへの反映を追加する
- PlayerHit、PlayerDied、PlayerRespawned、ItemSpawned、ItemCollected、PowerChanged、ExtendAwarded、ContinueUsed eventを追加する
- 同一tickでbombとhitが競合するcaseを含むunit／integration testを追加する

結果画面の最終UIは後続Phaseですが、Runtime状態とeventは完成させてください。全検証後にP5を更新してください。
```

## P6: Motion／Attack timeline

```text
P6「Motion／Attack timeline」を実装してください。任意コードを実行するscript言語ではなく、有限個の検証可能なcommand interpreterとして作ってください。

目標は、enemyの入場、停止、曲線移動、複数弾幕、退場をJSONだけで時系列に構成できることです。

必要な成果:
- Motion commandとしてenter、move-to、move-by、follow-path、orbit、wait、leaveを実装する
- easing、duration、local／world座標、player位置のsnapshot参照を明示する
- Attack commandとしてfire、start-pattern、stop-pattern、wait、repeat、parallelを実装する
- emitterのfixed／aim-at-player／current-heading／rotating angleと、single／fan／ring／arc／random-arc／layers distributionを実装する
- fixed／range／layers／accelerating／decelerating speedをProjectile behaviorへ接続する
- difficulty tagによるcommand／emitterの有効化を可能にする
- 最大command、repeat、最小interval、同一tick spawn、理論spawn budgetをvalidateし、無限loopや爆発的生成を拒否する
- sequence終了、enemy death、stage transition、pause、hot reloadでrunner stateを正しく破棄する
- seed付きrandom-arcの決定性と、複数の代表patternのgolden snapshot testを追加する
- sampleまたは専用v2 fixtureに、入場→停止→攻撃→退場する敵を追加する

Editor側への実装はP15へ残し、Runtime interpreterを唯一の計算実装にしてください。全検証後にP6を更新してください。
```

## P7: Boss phase

```text
P7「Boss phase」を実装してください。P6のtimelineを再利用し、boss固有の巨大Systemへ弾幕計算を複製しないでください。

目標は、一体のbossがHPまたはtime limitで複数phaseを遷移し、練習単位とscore bonus単位を提供することです。

必要な成果:
- BossDefinition／BossPhaseDefinitionをRuntimeへ接続し、phase id、HP、timer、motion、attack、invulnerability、checkpointを扱う
- phase開始、HP撃破、timeout、phase終了、次phase、boss終了の状態機械を実装する
- phase間でboss Entityを維持しつつHealthとpattern runnerを安全にresetする
- 開始／終了時の敵弾cancel policy、item drop、base／time／no-miss／no-bomb bonus用eventを実装する
- Stage clear条件をboss spawn数依存からobjective完了依存へ移行し、bossなしstageも扱う
- renderer snapshotへboss名、phase名、HP fraction、残り時間、warningを追加する
- HPとtimeoutの同時成立、bomb中のphase終了、最後のphase、retry／next stageをテストする
- 3 phase以上のv2 test bossとstage fixtureを追加する

実際の製品boss画像や最終balanceは作らず、mechanicsとデータ契約を完成させてください。全検証後にP7を更新してください。
```

## P8: Score engine

```text
P8「Score engine」を実装してください。既存Telemetry.Scoreの直接加算を廃止方向へ進め、型付きGameplayEventだけを入力にする再利用可能なscore計算へ移してください。

目標は、単純撃破点からchain／multiplier／graze／cancel／item／boss bonusまでをRuleSetごとに組み替え、全得点の理由を説明できることです。

必要な成果:
- scoreをlongにし、RunStateへcurrent scoreとscore breakdownを保持する
- IScoreRuleとordered ScoreRulePipelineを実装し、RuleSetDefinitionから構成する
- base kill、chain＋timeout、hit combo、multiplier、point-blank、graze、projectile cancel、item growth、boss time、no-miss／no-bomb、stage／all clear、残resource換算、extend thresholdの標準ruleを実装する
- ScoreAwardedEventにreason、base、multiplier、final amount、source、categoryを含め、overflow policyを定義する
- 同一tickのevent順序と倍率更新順を固定する
- HUD用にchain、multiplier、gaugeへ近い表示stateをRunStateから公開する
- 既存v1の撃破点を互換rule setで同じ結果にする
- event列を直接与えるtable-driven unit testと、stage全体のscore breakdown integration testを追加する
- score ruleを追加する手順と、制作者向けの得点設計例を文書化する

個別作品の数式をコピーせず、オリジナルのtest rulesetで構成能力を示してください。全検証後にP8を更新してください。
```

## P9: Special gauge、Rank、Difficulty、RuleSet

```text
P9「Special gauge、Rank、Difficulty、RuleSet」を実装してください。P8のeventとP3のDefinitionを使い、特殊モードを個別のif文でShootingSimulationへ埋め込まないでください。

目標は、ハイパー／BREAK系の段階ゲージ、run中の動的Rank、選択Difficulty、ゲームModeを直交して構成できることです。

必要な成果:
- Inactive、Active(level)、Cooldownを持つspecial gauge stateとrule pipelineを実装する
- damage、kill、graze、cancel、item、lock countからのcharge、manual／automatic／段階発動、time drain、kill延長、bomb／death終了を定義可能にする
- active中のdamage、fire rate、projectile cancel、invincibility、score multiplier、visual／audio event効果を組み合わせる
- RankSystemへ加算源、減算源、clampを定義し、bullet speed、fire interval、追加emitter、revenge bulletへ写像する
- DifficultyDefinitionのglobal modifierとpattern tag差分をRuntime生成へ適用する
- RuleSetDefinitionでstage route、time attack、continue、initial resources、score pipeline、gauge、rank、clear conditionを構成する
- Novice／Arcade／Expertと、通常／time attackの小さなfixtureを用意する
- debug snapshot／Replay metadataからrankとgaugeを観測可能にする
- gauge、rank、difficultyが同じpatternへ与える効果、bomb／death時終了、time attack終了をテストする

機能名や数式はオリジナルにしてください。全検証後にP9を更新してください。
```

## P10: Mode選択、Profile v2、Local leaderboard

```text
P10「Mode選択、Profile v2、Local leaderboard」を実装してください。既存GameShellとPlatform保存基盤を拡張し、選択したRunConfigurationをproduction simulationへ渡してください。

目標は、controllerだけでgame、mode、difficulty、shipを選び、category別成績を保存して閲覧できることです。

必要な成果:
- GameShellをTitle→ModeSelect→DifficultySelect→ShipSelect→Playingへ拡張する
- 選択肢をDefinition catalogから生成し、戻る操作、未解放／利用不能、最後の選択復元を扱う
- ScoreCategoryKey(game、ruleset、difficulty、ship)を導入する
- PlayerProfile schema v2へbest score、clear count、best stage、play count、play time、unlockを追加し、v1 gameId scoreを失わずmigrationする
- local leaderboard entryへscore breakdown、clear、continue、timestamp、Replay参照予定fieldを保存する
- Result画面でcategory、stage、max chain、graze、miss、bomb、continue、clear、play timeを表示する
- ILeaderboardService境界をPlatform側に置き、local実装を作る。Steam依存はまだ追加しない
- 原子的保存、破損回復、重複run記録防止を維持する
- 全menu遷移、profile migration、category分離、低score非更新、同score policyをテストする

全検証後にP10を更新してください。
```

## P11: ReplayとTraining

```text
P11「ReplayとTraining」を実装してください。P1の決定的tickとP10のRunConfiguration／leaderboardを利用し、別Simulation実装を作らないでください。

目標は、runを記録・再生して結果を検証でき、stage／boss phaseを任意初期条件から練習できることです。

必要な成果:
- version、engine version、content hash、RunConfiguration、seed、作成時刻、入力差分、定期state hash、最終結果を持つReplay formatを定義する
- recorder、reader、InputFrame provider、playback controllerを実装する
- Replay fileをuntrusted inputとしてsize、frame count、enum、checksum、pathを検証する
- version／content hash不一致、破損、途中終了、desync frameをユーザーへ説明できるerrorにする
- run resultとlocal leaderboardからReplayを保存／再生できるUIを追加する
- TrainingSetupでstage、boss checkpoint、power、lives、bombs、rank、gaugeを選び、RunConfiguration overrideとして同じSimulationを起動する
- trainingではofficial leaderboard／profile bestへ登録せず、即時retryを可能にする
- optional viewer speedとhitbox表示はSimulation結果を変えないviewer controlにする。slow practiceなど結果を変える機能はpractice flagへ記録する
- record→playbackでfinal state hash、score breakdown、clearが一致するintegration testを追加する

全検証後にP11を更新してください。
```

## P12: Asset catalog、Sprite、Background

```text
P12「Asset catalog、Sprite、Background」を実装してください。Runtimeはasset IDだけを公開し、MonoGameのTexture／file loadはFrameworkへ閉じ込めてください。

目標は、1px矩形描画を残したfallbackを維持しながら、content packごとの製品画像を安全にロードし、animationとbackgroundを描画できることです。

必要な成果:
- IVisualAssetCatalogとFramework実装を追加し、assets manifestのID、相対path、texture、sprite region、animation clipを解決する
- path traversal、重複ID、file不足、範囲外rectangle、無効frame durationを起動前に検証する
- SpriteRenderItem相当へvisualId、animation、rotation、scale、tint、layer、previous/current transformを持たせる
- fixed tick間の描画補間をactor／itemへ適用し、hit判定座標は変えない
- sprite sheet animation、origin、flip、render layerを実装する
- scrolling／parallax background layerとstage背景切替をDefinition化する
- hot reload時のasset差替え、dispose、last-known-goodを扱う
- Release publishへ必要assetを含め、不足asset検査を追加する
- テスト用にライセンス上問題のない自作placeholder assetを追加し、primitive fallbackも維持する
- catalog validation unit testと代表sceneのrender smoke／screenshot手順を追加する

外部から無断取得した画像や参考作品のassetを使用しないでください。全検証と実画面確認後にP12を更新してください。
```

## P13: Effect、HUD、視認性

```text
P13「Effect、HUD、視認性」を実装してください。P12のasset catalogとP1のGameplayEvent／FrameSnapshotを利用してください。

目標は、大量弾下でもplayer、hitbox、敵弾、得点状態が判別でき、攻撃の結果が気持ちよく伝わるpresentationです。

必要な成果:
- event駆動のparticle／effect systemをFrameworkへ追加し、muzzle、hit、destroy、bullet cancel、item collect、bomb、special、boss transitionを実装する
- additive blend、trail、screen flash、hit stop、camera shakeを設定可能にし、presentationがSimulation結果を変えないようにする
- player hitbox、graze ring、lock marker、laser、optionを描画する
- HUDへscore、high score、chain、multiplier、power、special gauge、rank、stage、lives、bombsを追加する
- boss HUDへname、phase、HP、timer、warningを追加する
- 画面内の危険な敵弾を背景／player bullet／effectより優先して読めるlayer、outline、明度規則を定める
- particle density、flash、shakeを0まで下げてもgameplay情報が失われないfallbackを作る
- full／touhou／donpachi layout、各論理解像度、letterboxでHUDが欠けないtest／visual QAを追加する
- effect poolと同時数上限を設け、10,000 bullets benchmark sceneでallocationを計測する

全検証と代表screenshot確認後にP13を更新してください。
```

## P14: Audio、Localization、Accessibility

```text
P14「Audio、Localization、Accessibility」を実装してください。既存の手続きToneはasset不足時のfallbackとして残して構いませんが、製品用cue pipelineを完成させてください。

目標は、BGM／SE、日英UI、視認性と演出設定がproduction menu・save・publishまで一貫して動くことです。

必要な成果:
- AudioDefinitionとasset cueを接続し、stage／boss BGM、loop metadata、crossfade、duckingを実装する
- shot、laser、hit、destroy、item、graze、bomb、special、warning、menu SEをGameplayEventまたはUI eventから再生する
- 同一cueの同時発音上限、priority、cooldown、pitch variationを実装し、大量hit音を集約する
- SettingsへBGM／SE／Voice volumeを追加し、schema migrationと即時previewを実装する
- UI文字列をstring catalogへ分離し、日本語／英語、fallback、欠落key検査を実装する
- screen shake、flash、particle density、background brightness、bullet outline／palette、HUD scaleをOptionsへ追加する
- 色だけに依存しない敵弾／item識別を実装する
- gamepad／keyboard glyphをactive deviceとbindingに合わせて表示する
- credits、license、scoring help、操作説明をmenuから閲覧可能にする
- 音声deviceなし、missing cue、locale欠落、設定migrationをテストする

使用assetのlicenseと出典をTHIRD-PARTY-NOTICESまたはasset台帳へ記録してください。全検証後にP14を更新してください。
```

## P15: Definition Editor v2

```text
P15「Definition Editor v2」を実装してください。production Runtimeの計算を唯一の真実として使い、browser側に弾幕や移動の別実装を複製しないでください。

目標は、制作者がJSONを直接編集しなくても、v2 contentの参照、timeline、pattern、boss、visualを作成・検証・previewできることです。

必要な成果:
- v2 Ship、Projectile、Item、Pattern、Boss、RuleSet、Difficulty、Visual、Audioの一覧／作成／複製／削除／form編集を追加する
- motion pathのcontrol point編集と、motion／attack timelineの時間軸編集を追加する
- emitter shape、angle、speed、burst、repeat、difficulty tagをvisualに編集する
- boss phaseのHP、timer、pattern、cancel、bonus、checkpointを編集する
- Runtime headless preview APIを使い、play／pause／step／seek／seed変更／difficulty比較を行う
- active／累計projectile数、理論spawn budget、validation errorをpreviewに表示する
- stage timelineへenemy、boss、background、BGM、warning eventを配置する
- sprite region／animation previewとasset参照選択を追加する
- scoring event traceとwaveのscore breakdownを表示する
- 保存は全catalog validation成功時だけatomicに行い、既存path traversal防止とlast-known-goodを維持する
- API／service testと主要browser操作の自動化可能なtestを追加する

全build／test、validation／smoke、formatに加え、Editorでv2 fixtureを開き、変更、preview、保存、再読込を手動確認してください。完了時だけP15を更新してください。
```

## P16: 製品vertical sliceとRelease QA

```text
P16「製品vertical sliceとRelease QA」を完遂してください。これはengine機能の追加だけではなく、docs/commercial-shmup-architecture.mdのRelease Candidate基準を満たす1本のオリジナルゲームを作るPhaseです。

まず既存の全Phaseが本当に完了しているか、code、tests、実行結果で監査してください。不足があれば一覧だけで終わらず修正してください。

必要な成果:
- オリジナルのsignature ruleset、Novice／Arcade／Expert、性格の異なる2〜3 shipsを完成させる
- product ownerから別案が指定されていなければ、設計書3.1のSYNC DRIVEをsignature rulesetとして完成させる
- 5 stages、各stage boss、雑魚12種以上、boss phase 15以上、再利用pattern 30以上を作る
- 20〜30分の1周で、survivalとscoringの主ループが段階的に学べる構成にする
- score attack、stage／boss training、Replay、local leaderboardをcontentから利用可能にする
- すべての仮画像／仮音を、配布権が明確な製品assetへ置き換える。asset台帳とlicense noticeを完成させる
- scoring tutorial、controls、credits、日英UI、accessibility presetを完成させる
- 全ship×difficultyの自動soak run、Replay regression、10,000 bullets stress、save破損、controller切断、display切替を検証する
- clean Windows環境相当のself-contained publish、公開exeからのsmoke、ZIP／checksum、Steam private beta手順を検証する
- 手動QA matrix、既知の問題、minimum／recommended spec、release checklistを文書化する
- 10名以上の外部playtestを行うためのfeedback form、build識別、再現情報の集め方を用意する。実参加者がまだいない場合は未実施を正直にrelease blockerとして残す

著作権のある参考作品の画像、音、固有名、stage構成、弾幕をコピーしないでください。Release gateを満たさない項目を「完了」にしないでください。自動化できる範囲をすべて実装・検証し、人間のplaytestやストア審査だけが残る場合は具体的なblockerと実施手順を報告してください。
```

## セッション間の引き継ぎ形式

各セッションの最終回答には最低限、次を含める。

```text
Phase:
Outcome:
Key contracts changed:
Compatibility/migration:
Verification commands and results:
Artifacts or screenshots:
Known risks/blockers:
Next phase:
```

プロンプトは、目的、成功条件、制約、証拠、終了条件を先に示す構成にしている。実装経路は最新コードに合わせてセッション側が選び、計画提示だけで止まらず、安全に実行できる範囲を実装し切る。
