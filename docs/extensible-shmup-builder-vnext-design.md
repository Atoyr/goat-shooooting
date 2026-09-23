# 拡張可能な弾幕STGビルダー vNext設計

## 1. 目的

この文書は、現行のDefinition v2と固定tick Runtimeを保ったまま、次の性格が異なる作品をゲーム固有の分岐なしで制作できるDefinition v3の設計を定める。

- ロックオン、段階強化、プレイ内容連動Rank、短時間Score Attackを中心にした作品
- Shot／Laser、Hyper、敵弾相殺、Laser同士の強弱、chainを中心にした作品
- modeごとに弾速、密度、弾幕構造、score ruleが大きく変わる作品

目標は既存作品の固有名称や数値を再現することではない。異なるルールを次の共通能力へ分解し、同じRuntimeとEditorで組み立てられるようにする。

1. 発射後にも状態が変化するProjectile program
2. 複数resourceとstate machineからなるルール
3. Shot、Projectile、Laser間のinteraction rule
4. 部位、hardpoint、複数hitboxを持つActor
5. mode、difficulty、ship、動的Rankを直交合成するvariant
6. Stage、camera、background、audio、presentationのtimeline
7. 隔離Preview、入力記録、graph／curve編集を持つEditor

Definition v3でも次の既存契約を維持する。

- Simulationは60Hz固定tickで進む。
- CoreとRuntimeはrenderer、filesystem、JSONへ依存しない。
- 同じengine、compiled content、seed、入力列、module setならcanonical state hashが一致する。
- JSONから任意コードを実行しない。
- 不明な命令、循環、上限超過、不正参照は起動前に拒否する。
- v1／v2 contentは読込時adapterでv3の中間表現へ変換し、source fileを書き換えない。

## 2. 設計上の判断

### 2.1 一つの万能VMを作らない

Projectile、Actor、Stage、Ruleは時間の扱いと許可すべき副作用が異なる。共通化するのは型付き式、条件、時間、sequence、parallel、repeat、signalまでとし、命令はdomainごとに分ける。

```text
Shared authoring language
  Number / Bool / Vector / Id expression
  condition / sequence / parallel / repeat / wait / signal
                         |
       +-----------------+------------------+
       |                 |                  |
 Projectile ops      Actor ops          Stage ops
 motion / split      move / weapon      spawn / camera
 aim / transform     part / phase       bg / audio / route
       |
 Interaction rules   Rule reducer       Presentation recipes
```

`domain`が`projectile`のprogramではcamera命令を使えず、`stage`のprogramではscore resourceを直接書き換えられない。domain間の連携には型付きsignalまたは`IGameplayEvent`を使う。

### 2.2 Authoring modelとRuntime modelを分ける

JSON graphをtick中に解釈しない。load時に検証・解決・compileし、Runtimeは数値handleと連続配列だけを読む。

```text
JSON Definition
    -> DefinitionCatalog
    -> DefinitionCompiler
    -> CompiledCatalog
         ProgramHandle / ActorHandle / ResourceSlot / TagMask / StatKey
    -> ShootingSimulation
```

`CompiledCatalog`はimmutableとする。文字列ID、JSON Path、editor用node IDはdiagnostic mapへ残すが、hot pathでは辞書検索しない。compiled IRとmodule versionをcontent hashへ含める。

### 2.3 柔軟性は型付き合成で得る

自由なC#式、eval、reflection、JSON Patchをcontentへ許可しない。代わりに以下を提供する。

- 入力値を公開できるparameterized program
- event field、resource、difficulty、rankを読めるpure expression
- eventを条件にresourceやstateを更新するrule
- statへ`add`／`multiply`／`override`を適用するmodifier
- semantic slotからprogramを選ぶvariant binding
- tagと強度で攻撃間の関係を解くinteraction rule

これにより、作品固有コードを増やさずに組み合わせを増やせる一方、Editorで全参照と副作用を追跡できる。

## 3. Definition v3の構成

新しいcontent directoryを次の単位で追加する。

```text
programs/             Projectile／Actor／Attack用program
actors/               Enemy、boss、part、hardpoint
resources/            gauge、counter、timer、flag
state-machines/        Hyper、Break、mode固有状態
rules/                 event reducer、score、resource更新
interactions/          Shot／Projectile／Laser間の解決規則
variants/              mode別program binding
parameter-sets/        difficulty／mode別の数値集合
stage-programs/        spawn、environment、camera、audio track
effects/               event駆動presentation recipe
```

既存の`ships/`、`projectiles/`、`weapons/`、`items/`、`stages/`、`rulesets/`、`difficulties/`、`visuals/`、`audio/`は維持し、v3 fieldを段階的に追加する。

### 3.1 Run選択の直交軸

`RunConfiguration`は次の選択を独立して保持する。

```text
RuleSet     : score、resource、life、bomb、specialの意味
Variant     : 使用する敵編成と弾幕構造
Difficulty  : 速度、密度、HP、猶予時間などの数値curve
Ship        : action、weapon、option、移動特性
Seed        : gameplay RNG
Training    : start stage、checkpoint、初期resource override
```

値の解決順を固定する。

```text
program default
  -> variant parameter binding
  -> difficulty modifier
  -> ship/style modifier
  -> dynamic rank modifier
  -> active state-machine modifier
```

同じ`StatKey`へ複数の`override`が同priorityで指定された場合はvalidation errorとする。`multiply`はpriority、definition ID、記載順の安定順で適用する。

`RuleSet`と`Difficulty`だけでは弾幕構造の差を表しにくいため、`Variant`を別軸にする。たとえば少数高速弾と大量低速弾は単なる弾数倍率ではなく、異なるprogram bindingとして選択する。

### 3.2 Semantic slotとparameterized program

StageやBossから具象pattern IDを直接参照する代わりに、必要な箇所ではsemantic slotを参照できる。

```json
{
  "slotId": "stage-1.boss.phase-2.main",
  "defaultProgramId": "boss-p2-standard",
  "bindings": [
    {
      "variantId": "dense",
      "programId": "boss-p2-dense",
      "parameterSetId": "dense-arcade"
    }
  ]
}
```

programは`speed`、`count`、`gapDegrees`、`cycleFrames`のような公開parameterを宣言する。modeごとにStage全体を複製せず、programまたはparameter setだけを差し替えられる。

## 4. Program言語

### 4.1 共通構造

Authoring形式はnode ID付きの構造化ASTとする。配列形式とnode graph形式を別仕様にせず、Editorは同じASTをgraphとして表示する。

```json
{
  "schemaVersion": 3,
  "id": "delayed-ring",
  "domain": "projectile",
  "parameters": {
    "delayFrames": { "type": "integer", "default": 45, "minimum": 1 },
    "childCount": { "type": "integer", "default": 12, "minimum": 1, "maximum": 64 }
  },
  "entryPoints": {
    "onSpawn": [
      { "nodeId": "stop", "op": "set-speed", "value": 0 },
      { "nodeId": "wait", "op": "wait-frames", "value": { "parameter": "delayFrames" } },
      { "nodeId": "aim", "op": "aim-at", "target": "player-snapshot" },
      { "nodeId": "split", "op": "emit-ring", "count": { "parameter": "childCount" }, "projectileId": "child" },
      { "nodeId": "end", "op": "despawn" }
    ]
  }
}
```

各nodeは安定した`nodeId`を持つ。validation error、breakpoint、trace、migration、graph diffはこのIDを使う。

### 4.2 型付き式

式の型は`number`、`integer`、`boolean`、`vector2`、`id`、`tag-set`に限定する。式はpureで、次だけを読める。

- compile-time parameter
- difficulty parameter
- current rankとresource
- current frame、program age、phase age
- eventの型付きfield
- owner／target／playerのsnapshot値
- instanceごとに分離したseeded random stream

演算は`add`、`subtract`、`multiply`、`divide-safe`、`min`、`max`、`clamp`、`lerp`、`curve`、比較、boolean演算に限定する。文字列連結、IO、wall clock、reflection、再帰関数は許可しない。

分岐に使う値はcompile時に型検査する。ゼロ除算、NaN、Infinityは定義エラーまたは明示したfallbackへ変換し、Runtimeへ流さない。

### 4.3 制御構造

- `sequence`: 子を順に実行する。
- `parallel`: 上限付きのtrackを同時実行する。
- `repeat`: compile-timeまたは上限付き回数だけ繰り返す。
- `branch`: pure conditionで一方を選ぶ。
- `wait-frames`／`wait-seconds`: 最低1 tick待つ。
- `wait-signal`: 型付きsignalを待つ。必ずtimeoutを持つ。
- `loop-until-expire`: Projectile lifetimeなど静的に有限と証明できるdomainだけで許可する。

無制限の`while`と再帰呼び出しは許可しない。cycleを持つgraphは、最低1 tickのwaitと静的な終了上限を証明できる場合だけcompileする。

### 4.4 Domain命令

Projectile domainは次を組み込み命令として提供する。

- `set-speed`、`add-speed`、`accelerate-to`
- `set-angle`、`add-angle`、`angular-velocity`
- `aim-at`、`home-for`
- `set-motion-kernel`（linear、polar、orbit、curve）
- `emit`、`emit-fan`、`emit-ring`、`split`
- `set-tag`、`set-cancel-class`、`set-damage-enabled`
- `set-visual`、`emit-signal`、`despawn`

Actor domainは次を提供する。

- `move-to`、`move-by`、`follow-path`、`orbit`、`leave`
- `fire-program`、`start-program`、`stop-program`
- `enable-part`、`disable-part`、`set-targetable`
- `set-hurtbox-profile`、`set-animation-state`
- `spawn-actor`、`emit-signal`

Stage domainは次を提供する。

- `spawn-actor`、`spawn-formation`、`despawn-by-tag`
- `set-background`、`tween-background`、`set-scroll`
- `camera-pan`、`camera-shake-cue`、`set-world-time-scale`
- `play-bgm`、`crossfade-bgm`、`play-cue`
- `show-warning`、`show-message`
- `wait-until-clear`、`wait-boss-phase`、`checkpoint`
- `set-route-flag`、`branch-route`、`complete-stage`

## 5. Projectile Runtime

### 5.1 Behavior VMとmotion kernelを分ける

10,000発すべてで毎tick命令列を走査しない。Projectile programはspawn時またはwake frameだけ実行し、連続運動は小さなmotion kernelが処理する。

```text
ProjectileProgram VM             Motion kernel (every tick)
  set velocity                     linear integration
  set acceleration       ->        scalar/vector acceleration
  set angular velocity             polar rotation
  sleep until frame                bounded homing
  split / transform                curve segment
```

`ProjectileStore`へ次のSoA列を追加する。

```text
programHandle, programCounter, wakeFrame,
localSlotOffset, motionKernel, acceleration, angularVelocity,
targetEntityId, spawnLineageId, tagMask, interactionClass
```

localはprogramごとに固定slot数をcompileし、projectileごとのDictionaryやobjectを作らない。simple straight projectileはprogram stateを持たず、現行と同じ最短経路を通る。

### 5.2 Spawnと乱数の決定性

各spawnに`spawnLineageId`を割り当てる。乱数streamは次から導出する。

```text
run seed + stage instance + owner spawnLineageId + program handle + node ID + invocation count
```

一つのpatternへ命令を追加しても、無関係なenemyの乱数列がずれない。並列trackの評価順はcompiled track index順とする。

### 5.3 Budget

compile時に次を計算する。

- programごとの最大instruction数
- parallel track数
- local slot数
- 1 wake当たりの最大命令数
- 1 invocationと1秒当たりの理論spawn数
- 親子spawn深度

release contentでbudgetを超えた場合、弾を黙ってdropしない。Editor／QAではsource node付きで失敗させ、製品Runtimeではrunを明示的なcontent errorとして停止する。Presentation particleだけは従来どおりdrop可能とする。

## 6. Interaction Engine

Shot、Projectile、Laserの相殺を個別Systemへhard-codeせず、interaction profileで決める。

```json
{
  "schemaVersion": 3,
  "id": "hyper-shot-cancel",
  "source": { "team": "player", "requiredTags": ["shot", "hyper"] },
  "target": { "team": "enemy", "requiredTags": ["bullet"], "maximumResistance": 2 },
  "shapeTest": "swept",
  "priority": 200,
  "actions": ["destroy-target", "emit-projectile-cancelled"]
}
```

攻撃側は`interactionPower`、対象側は`interactionResistance`を持つ。弱Laser、強Laser、Hyper Laserの関係は数値強度とtagで表す。通常Shotによる敵弾破壊、Laserによる押し合い、特定弾だけ相殺不可、相殺時のitem変換を同じ仕組みに載せる。

対応shapeは最初に`circle`、`capsule`、`aabb`、`obb`を提供する。Actorは複数shapeを持てるが、Projectileはhot path維持のため原則circleまたはcapsuleに制限する。

interactionの結果は直接scoreを変更せず、`ProjectileInteractionEvent`、`ProjectileCancelledEvent`、`LaserContactEvent`を発行する。得点、ゲージ、item化、演出はevent ruleが処理する。

## 7. Resource、Rule、State Machine

### 7.1 汎用Resource

`RunState`へ作品固有fieldを追加し続けず、compile済みslotでresourceを保持する。

```json
{
  "schemaVersion": 3,
  "id": "hyper-meter",
  "scope": "player",
  "valueType": "number",
  "initial": 0,
  "minimum": 0,
  "maximum": 1000,
  "resetPolicy": "on-run-start",
  "hud": { "format": "segmented-gauge", "segments": 10 }
}
```

scopeは`run`、`player`、`stage`、`boss-phase`を持つ。counter、gauge、timer、boolean flagを扱い、Replay snapshotとcanonical hashへ含める。

標準resourceとしてscore、chain、hit、rank、power、life、bombを同じ参照APIから読めるようにするが、既存の専用Componentを一度に置換しない。v3 adapterが専用状態とresource slotの同期責務を持つ。

### 7.2 Event rule

ruleは「事実を読む」「条件を判定する」「commandを生成する」だけとし、Worldを直接変更しない。

```json
{
  "schemaVersion": 3,
  "id": "lock-score",
  "phase": "post-interaction",
  "on": "enemy-destroyed",
  "when": {
    "op": "greater-than",
    "left": { "event": "lockCount" },
    "right": 0
  },
  "actions": [
    {
      "op": "award-score",
      "category": "lock",
      "base": { "event": "baseScore" },
      "multiplier": {
        "op": "curve",
        "input": { "event": "lockCount" },
        "curveId": "lock-multiplier"
      }
    },
    { "op": "add-resource", "resourceId": "break-meter", "value": { "event": "lockCount" } }
  ]
}
```

標準actionは次を含む。

- resourceの`add`、`set`、`clamp`、`consume`
- `award-score`と内訳event
- item／actor／projectileのspawn command
- projectile queryに対するcancel／convert command
- state transition request
- modifierの付与／解除
- gameplay／presentation signal
- route flagとachievement候補event

### 7.3 State machine

Hyper、BREAK、DOUBLE BREAK、power style切替など、継続状態を一つの特殊ゲージ実装へ押し込まない。

```text
Inactive
  -- input + meter >= 100 --> Break
Break
  -- input + meter >= 100 --> DoubleBreak
  -- meter == 0 / bomb / death --> Inactive
DoubleBreak
  -- meter == 0 / bomb / death --> Cooldown
Cooldown
  -- timer elapsed --> Inactive
```

各stateは次を宣言する。

- enter conditionとresource cost
- enter／tick／exit action
- modifier set
- 許可するaction mapping
- durationまたはdrain rule
- interrupt／upgrade transition
- visual／audio cue

これにより、active中の段階昇格、自動発動、複数segment消費、bombとの共通meter、死亡時終了を同じモデルで表現できる。

### 7.4 Modifier

modifier対象は自由文字列ではなく、Registryで定義された`StatKey`とする。

```text
player.damage
player.fire-interval
player.move-speed
enemy.hp
enemy.projectile-speed
enemy.fire-interval
emitter.projectile-count
score.event-multiplier
world.time-scale
interaction.power
```

modifierは`add`、`multiply`、`override`、`curve`を持つ。適用scope、priority、開始frame、終了frame、source stateを記録し、Debug snapshotから「最終値がなぜこの値になったか」を追跡できるようにする。

## 8. Rule評価のtick順序

eventから即座に別eventを再帰発行すると順序が不明確になるため、phaseを固定する。

```text
1. Capture InputFrameSet
2. Pre-input rules / action state machine / special transition
3. Stage program / Actor program / Weapon spawn
4. Projectile wake programs
5. Actor and Projectile motion
6. Spatial interaction / graze / hit
7. Damage / destruction / lifecycle
8. Post-interaction rules / score / resource reduction
9. Apply deferred spawn and conversion commands
10. Cleanup / canonical snapshot / presentation event export
```

ruleは`pre-input`または`post-interaction`のどちらかを宣言する。各phaseで使用可能なcommandを制限する。たとえばHyper発動時の画面内弾消しはcollision前、敵撃破によるitem生成はdamage確定後に適用する。

同一phaseでは`priority`、rule ID、action index順に評価する。command適用で生じたderived eventは、仕様で指定した次phaseまたは次tickまで再評価しない。1 tick内の暗黙なevent loopを禁止する。

## 9. InputとShip Action

物理buttonをFire／Focus／Bomb／Specialへ固定せず、edgeとholdを論理actionへ変換する。

```text
Input signal: pressed / held / released
Action map:
  tap shot button       -> rapid-shot
  hold shot button      -> laser
  release lock button   -> fire-lock-volley
  press style button    -> toggle-power-style
  press special button  -> request state transition
```

`ActionMapDefinition`はshipまたはstyle moduleから合成する。actionはWeapon program、state transition、resource actionのいずれかを要求するだけで、入力Systemから直接実行しない。

将来のlocal co-opに備え、v3内部入力は`InputFrameSet`とし、`playerIndex`ごとの量子化入力を持つ。現行`InputFrame`は1人分のadapterとして残す。resource、target selection、camera policyはscopeを持ち、single player実装時にplayer 0をhard-codeしない。

## 10. Actorと複合Boss

Actorはrootとpartからなるcompositionを持つ。

```text
Boss root
  + body       shared-health / targetable
  + left-gun   independent-health / targetable / hardpoint A
  + right-gun  independent-health / targetable / hardpoint B
  + core       phase-2 only / targetable
```

partは別Entityとして生成し、次を保持する。

- parent entity IDとlocal transform
- health policy（shared、independent、indestructible）
- damage forwarding ratio
- targetable／lock capacity
- hurtbox profileとinteraction class
- weapon hardpointとprogram
- destroy／detach時signal
- visual、animation state、render layer

親子生成順とEntity ID割当はcompiled part index順に固定する。phase transitionはpartを直接検索せず、tagまたはpart handleへ`enable`／`disable` signalを送る。ロックオンは1 Entityにつき1回へ固定せず、partごとの`lockCapacity`とlock slotを扱う。

## 11. Stage、Clock、Presentation

### 11.1 Stage program

Stageは一つのevent配列ではなく、同期する複数trackを持つ。

```text
spawn track        enemy、formation、boss、hazard
environment track background、scroll、weather、ground layer
camera track       pan、zoom、shake cue、world time scale
audio track        BGM、crossfade、stinger、ducking
ui track           warning、message、boss title
route track        checkpoint、condition、clear
```

track間は型付きsignalで同期する。`wait-until-clear`やboss phase signalにはtimeoutまたはstage終端条件を必須とし、進行不能をvalidation／soakで検出する。

### 11.2 Clock domain

固定tick自体は遅くしない。時間は次のclockへ分ける。

- `run-frame`: 60Hzで常に進み、Replay、入力、Time Attackの基準になる。
- `world-time`: Q16.16 scale accumulatorで進み、motion、weapon、world animationに使う。
- `presentation-time`: 描画演出専用でcanonical hashに含めない。

ゲーム内容に影響するslow motionは`world.time-scale` modifierとしてReplayとhashへ含める。単なる発動時の画面演出はpresentation-timeだけを変える。Boss timerがどちらのclockを使うかは定義で明示し、defaultは`run-frame`とする。

### 11.3 Presentation recipe

Simulationは粒子を直接作らず、semantic cueと値だけを出す。

```text
SpecialActivated(level, position, cueId)
ProjectileCancelled(count, bounds, interactionTag)
BossPartDestroyed(partId, position)
```

Framework側の`EffectRecipeDefinition`が、sprite particle、trail、flash、shake、post-process、audio cueへ展開する。recipeはevent fieldとaccessibility settingを読めるがWorldを変更できない。

state-driven animationは`idle`、`move-left`、`move-right`、`focus`、`break`、`damaged`、`destroy`などのsemantic stateから選ぶ。全animationをrun開始時刻基準で再生しない。

post-processはoptional passとしてbloom、color grade、distortion、afterimageを提供する。視認性に必要なhitbox、enemy bullet outline、lock markerはpost-processなしでも表示する。BGMは長尺streaming形式を扱い、SEのvoice上限、priority、duckingは既存AudioDefinitionを引き継ぐ。

## 12. Editor設計

### 12.1 Preview Sandbox

Preview requestへ次を追加する。

```text
ruleSetId / variantId / difficultyId / shipId
startStageId / checkpointId
initial resources / rank
input script or recorded InputFrame sequence
event injection / player invincibility / world time scale
```

Pattern単体、Actor単体、Boss phase、Stage、full runを同じproduction Runtimeで起動する。Browserに弾道計算を再実装しない。

seekは毎回frame 0から再実行せず、一定間隔のimmutable preview checkpointをcacheする。checkpointにはWorld、ProjectileStore、program state、resource、RNG streamを含め、復元後のhash一致をtestする。

### 12.2 専用Editor

- Pattern Graph: node接続、sequence／parallel／branch、subprogram参照
- Emitter Gizmo: fan、ring、offset、角度、速度layerをplayfield上で操作
- Curve Editor: speed、angle、rank、multiplier、difficulty curve
- Actor Composer: part、hardpoint、hurtbox、lock slot、local transform
- Stage Timeline: spawn、environment、camera、audio、UI、route track
- Rule Inspector: eventからresource、modifier、scoreへの依存graph
- Live State: resource、state machine、rank、program counter、wake frame
- Heatmap: projectile密度、graze領域、死亡位置、collision候補数

全編集はtransaction commandとして記録し、undo／redo、multi-select、copy／paste、整列、時間scale、ID renameと参照更新を提供する。保存前にdependency graph、budget、全variant compileを検証する。

### 12.3 診断

diagnosticは次を必須情報とする。

```text
file + JSON Path + node ID + domain + error code
reference chain
resolved parameter value and modifier provenance
estimated instruction/spawn budget
```

Runtime traceから「どのprogram nodeがこの弾を生成したか」「どのruleが倍率を変えたか」「どのmodifierが弾速を上げたか」をEditor上で辿れるようにする。

## 13. 拡張module

標準機能で表現できない作品向けに、拡張を3段階に分ける。

1. Content extension: JSON program、rule、state machine、variantだけ。通常はこちらを使う。
2. Compiled capability module: 新しいopcode、event、rule action、StatKeyをhost buildへ登録する。
3. Bespoke System bridge: 専用data storeやSystemが必要な場合だけcomposition rootへ追加する。

最初からDLLの動的読込やMarketplaceは実装しない。moduleはアプリへ明示的に参照し、起動時に登録する。

```csharp
public interface IShmupRuntimeModule
{
    ModuleDescriptor Descriptor { get; }
    void Register(ModuleRegistry registry);
}
```

`ModuleDescriptor`は一意ID、semantic version、replay compatibility versionを持つ。新しいRuntime状態を追加するmoduleはvalidator、snapshot serializer、canonical hasher、budget estimatorを必須実装とする。Editor metadataがないopcodeはJSON modeでは編集できても、visual modeではread-only表示にする。

Replayはmodule ID／version／compiled content hashを保存する。不一致時に推測再生せず、理由を表示して拒否する。

## 14. Migration方針

Definition v3を一括導入しない。各段階でv1／v2 sampleとSYNC DRIVEを動かす。

### M0: Compiler境界

- `DefinitionCatalog -> CompiledCatalog`を追加する。
- 現行capabilityをbuilt-in compiled handleへ変換する。
- Runtimeの文字列lookupをtick外へ移す。
- compiled content hashとdiagnostic mapを追加する。

#### M0実装契約

- `DefinitionCompiler`と`CompiledCatalog`は、capability factoryの型を所有する`Runtime` assemblyに置く。`Definitions`は引き続きJSON構造、migration、静的validationだけを所有し、`Runtime`への参照を追加しない。
- compilerはvalidated `DefinitionCatalog`をdeep snapshotしてから、IDのordinal順で型付き数値handleを割り当てる。compiled table、展開済みtimeline、emitter参照、boss phase参照、drop参照はread-onlyとし、compile後のauthoring object変更をrunへ反映しない。
- built-in capabilityもdomain別`CapabilityHandle`へcompileし、通常のproduction tickではcapability type、definition ID、timeline includeを文字列解決しない。旧Factory APIからrun中に注入されたv1／v2 Entityだけは、互換adapterとして初回更新時にcompiled handleへ解決できる。
- compiled content hashはsource content hash、compiler contract version、built-in module ID／version／Replay互換version、登録capability type集合からSHA-256で生成する。将来module descriptorを追加する場合も同じ入力へmodule情報を追加する。
- 既存Replay v1の`contentHash`はsource hashとして維持する。新規Replayはoptional `compiledContentHash`も保存し、値が存在する場合だけ厳密一致を要求する。これにより既存Replay documentを拒否せず、compiler／module不一致を新規記録では検出する。
- diagnostic mapはdefinition kind／IDからlogical fileとJSON Pathを引ける。v1からmemory migrationしたShip／Projectileは、生成後のv2 directoryではなく元の`player.json`／`bullets/*.json`を指す。

### M1: Parameter、Expression、Variant

- 型付きparameterとpure expression compilerを追加する。
- semantic slotとvariant bindingを追加する。
- modifierの解決順とprovenance debug表示を追加する。
- 現行Difficultyをmodifierへcompileする。

#### M1実装契約

- v3 authoringは`programs/`、`variants/`、`parameter-sets/`を追加し、各documentは`schemaVersion: 3`を必須とする。既存のgame／ship／stage等はv1／v2のまま段階移行でき、空のv3 collectionは既存content hashを変えない。
- parameter schemaはIDのordinal順でslot化し、`number`、`integer`、`boolean`、`vector2`、`id`、`tag-set`をload時に型検査する。parameter setのunknown key、型違反、range違反は、variant bindingまたはsemantic slotをcompileした時点で拒否する。
- pure expressionは型検査済みnode IRへcompileし、parameter、difficulty、resource、context（frame／age／rankを含む）、event field、snapshot、instance-local deterministic random sampleだけを読む。`add`、`subtract`、`multiply`、`divide-safe`、`min`、`max`、`clamp`、`lerp`、`curve`、比較、boolean演算以外は拒否し、division fallbackを必須とする。IO、wall clock、reflection、任意code実行はIRに存在しない。
- `Variant`はRunConfigurationのDifficultyと独立した選択軸とし、semantic `slotId`を別program handleへbindできる。binding時にparameter setを型付き値配列へ確定し、通常tickではslot／program／parameter IDの文字列検索を行わない。bindingがないslotだけprogram defaultへfallbackする。
- Acceptance Pack Cはscore ruleもvariantごとに差し替える一方、3.2節のbinding例はprogramだけを規定していた。この不足を解消するため、Variantは任意の`ruleBindings`（semantic rule slotからEventRuleへのbinding）も持てる。RuleSet共通ruleと選択Variantのrule bindingをhandleで合成し、同じEventRuleは一度だけ実行する。RuleSet軸そのものは変更せず、v1／v2および`ruleBindings`未指定variantの挙動は変えない。
- `StatKey`はbuilt-in registryでhandle化し、自由文字列のままRuntimeへ流さない。modifierはsource tier（program default、variant、difficulty、ship、rank、state）、priority、definition ID、記載順で安定sortする。同じStatKey／tier／priorityの複数overrideはvalidation errorとする。
- 現行DifficultyのHP、projectile speed、fire interval、追加projectileは同じ値のmodifier setへcompileする。解決値はrun開始時にcacheし、rank／active stateだけを従来どおり動的に重ねる。各適用前後の値、source、scope、stateは`RunDebugSnapshot.ModifierProvenance`から取得できる。

### M2: Projectile Program

- ProjectileStoreへprogram stateとmotion kernelを追加する。
- turn、wait、aim、split、transformを実装する。
- wake schedulerとstatic／runtime budgetを追加する。
- 既存straight／homing／emitterを同じIRへcompileする。

#### M2実装契約

- `ProjectileStore`のSoAへdefinition／program handle、program counter、wake frame、local slot offset、motion kernel、vector acceleration、angular velocity、target、spawn lineage、tag mask、interaction classを追加する。programを持たないstraight projectileはVM scheduleへ登録せず、従来のdense loop最短経路を維持する。
- projectile programの`onSpawn`はnode ID付きbounded IRへcompileする。M2ではspeed／angle変更、angular velocity、acceleration、1 tick以上のwait、player snapshotへのaim、期限付きhoming、ring emit／split、別projectileへのtransform、motion kernel切替、despawnを提供する。不明opcode、無上限spawn count、per-wake 256命令または4,096 spawn超過、program spawn／transform cycle、深度64超過はcompile errorとする。
- VMは全projectileを毎tick走査せず、`(wakeFrame, projectileId)`の安定priority queueから期限到来instanceだけを実行する。dense storeのswap removal時もprojectile IDからindexを更新し、古いschedule entryはwake frame照合で無効化する。
- motion kernelはlinear、scalar／vector acceleration、polar、homingを毎tickの小さい分岐で処理する。v1／v2 straight、homing、emitterは従来adapterから同じspawn command／motion kernelへcompileされる。curve kernelの状態列は予約し、curve segment authoringは後続の制御構造拡張で追加する。
- child lineageはrun seed、stage instance、owner lineage、program handle、安定node ID、invocation counter、child indexから決定的に導出する。program stateとlineageはcanonical state hashへ含める。
- compile済みbudgetをRuntimeでも再確認し、破損または想定外の動的値がbudgetを超えた場合はsilent dropせず`SimulationStatus.ContentError`とdiagnostic messageでrunを停止する。

### M3: Interaction Engine

- projectile対projectile、laser対laserのbroad phaseを追加する。
- interaction power／resistance、tag query、convert commandを実装する。
- 現行`cancel-soft`をinteraction profileへ移行する。

#### M3実装契約

- v3 `interactions/`はsource／targetのteam、required／excluded tag、minimum power、maximum resistance、shape test、priority、actionを宣言する。tagはcompile時に最大64個のregistryへhandle化し、ProjectileStore／LaserComponentでは`ulong` maskとして照合する。未知action、重複action、convert先不明、convertとdestroy-targetの競合はload／compile時に拒否する。
- projectileはteam由来の`projectile`＋`shot`／`bullet` tagとDefinition固有tag、interaction power／resistanceをSoAに保持する。laserも同じmask／power／resistanceを保持する。v2のsoft／hard／uncancelableは数値resistanceへadapter変換する。
- projectile対projectileは64px uniform grid、laser対projectileはlaser capsuleのAABBが交差するgrid cell、laser対laserはcapsule AABB gridとpair de-duplicationをbroad phaseに使う。narrow phaseはswept circleまたはcapsule segment距離で判定し、同teamは既定で相互作用させない。
- actionはsource／target破棄、target projectile変換、laser target反射、`ProjectileInteractionEvent`、`ProjectileCancelledEvent`、`LaserContactEvent`発行を持つ。score／resourceを直接変更せず、後段ruleがeventを消費する。convertはcompiled ProjectileHandleでin-place definitionを差し替え、reflectはlaserのteamと方向を決定的に反転する。`reflect-target`はtarget filterがlaserを必須tagに持つ場合だけ許可する。
- v2 laserの`projectileInteraction: cancel-soft`はcompilerが`v2-adapter.cancel-soft` profileへ変換し、通常production経路から旧hard-code cancel loopを外す。旧`LaserSystem`を単体利用する公開APIだけはcompiled catalogがない場合に互換adapterを通す。
- interaction profileが0件のpackではSystemは即returnし、10,000 projectile既存hot pathにgrid構築を追加しない。interaction state、tag、power、resistanceはcanonical hashへ含める。

### M4: Resource、Rule、State Machine

- scoped resource storeとevent rule reducerを追加する。
- active中upgrade可能なstate machineを実装する。
- score、gauge、rankの既存factoryをv3 ruleへadapterする。
- bombとspecialが同じresourceを消費できるactionを追加する。

#### M4実装契約

- `resources/`、`rules/`、`state-machines/`は`schemaVersion: 3`とし、compilerがそれぞれ`ResourceHandle`、`EventRuleHandle`、`StateMachineHandle`／`StateHandle`へ変換する。Resourceはrun／player／stage／boss-phase scope、number／counter／timer／boolean、初期値・上下限、run／stage／boss-phase／manual reset policyを持ち、scope key順のsnapshotをcanonical hashへ含める。
- RuleSetがresource／specialの意味を所有するという3.1節に対し、従来の`RuleSetDefinition`にはv3定義の選択参照が未規定だった。この曖昧さを解消するため、互換追加fieldとして`resourceIds`、`eventRuleIds`、`stateMachineIds`、`bombResourceId`を置く。未指定のv1／v2 RuleSetは従来factory adapterを使用し、source content hashでは空の追加fieldを省略する。これにより既存packを変更せず、同じcontent tree内のRuleSetごとにv3 reducerを選択できる。
- score、chain、hit、rank、power、life、bomb、gaugeはcompilerが標準Resource slotとして補完する。明示Resourceが同じIDを定義した場合はそのrange／scopeを使う。production tickの前後でv2 `RunState`／Componentとのadapter同期を行い、v3 actionと既存score／gauge／rank factoryのどちらを選んでも同じ参照APIとcanonical stateを使う。
- event ruleはimmutableな同一phaseのevent snapshotとresource snapshotだけを読み、worldを変更せずcommand列を生成する。`pre-input`／`post-interaction`内ではpriority、rule ID、event sequence、action indexの順を固定し、command適用で増えたeventを同じreducer passへ戻さない。event field、resource ID、式の型、action参照はcompile時に検証する。
- resource add／set／clamp／consume、score、state transition、signalはreducer後に適用する。item／actor／projectile spawnとprojectile cancel／convert queryはdeferred commandとしてpre-inputまたはpost-interactionの適用境界で処理する。route flag、achievement候補、presentation signalも型付きsignal eventとして後段へ渡し、ruleからWorldを直接操作しない。
- state machineはscopeごとにcurrent state、enter frame、state tickを保持する。request／automatic／resource-empty／timer-elapsed／bomb-used／player-died／rule transition、resource cost、per-tick drain、duration、enter／tick／exit command、allowed action、state modifierをcompileする。入力edgeを保持するため、active中に同じactionを再押下した場合だけ上位stateへupgradeできる。bombとspecialは`bombResourceId`とmachine `resourceId`を同じhandleへ解決することで原子的に同じmeterを消費する。
- state modifierは既存の`StatKey` registryと`ModifierResolver`へ合成し、provenanceへmachine／stateを残す。resource値とmachine stateはReplay checkpointのcanonical hashへ入り、同一input列のrecord／playbackで一致を要求する。

### M5: Actor Composition

- parent transform、part health policy、hardpoint、複数hurtboxを追加する。
- lock slotとphase signalを追加する。
- 現行BossDefinitionをroot 1個のActorへ変換する。

#### M5実装契約

- v3 authoringは`actors/`を追加し、`schemaVersion: 3`、`id`、rootの基礎値を供給する`enemyId`、任意の`tags`と`parts`を持つ。partは`parentPartId`、local offset／rotation、`shared`／`independent`／`indestructible` health policy、0〜1のdamage forwarding ratio、targetable、lock capacity、enabled、interaction class、tags、hurtbox、hardpoint、destroy／detach signal、presentation参照を持つ。parent cycle、未知参照、重複part／hurtbox／hardpoint、無効shape寸法はvalidation errorとする。
- compilerは明示ActorをID順にhandle化し、partは親を先にしたtopological orderの中でpart ID順にindexを固定する。Actorが明示されていない既存Enemyには同じID、part 0件のroot Actorを合成する。`BossDefinition.actorId`は任意の互換追加fieldとし、未指定Bossは`enemyId`から合成Actorを選ぶ。空の追加fieldと空のv3 collectionはsource content hashへ追加しない。
- root Entityを先に、part Entityをcompiled part index順に生成する。weapon hardpointはpartを親とするmount Entityへcompile済みWeaponHandleを持たせ、親の移動・回転・enabled状態へ追従する。複数hurtboxはcircle、capsule、AABB、OBBを持て、uniform gridでは包含半径、narrow phaseでは各shapeを使う。
- shared partへのdamageはrootへ全量、independent partは自身へ適用したうえでratio分をrootへforwardし、indestructibleまたはdisabled partはdamage対象外とする。part破壊は`ActorPartDestroyedEvent`と任意signalを発行し、boss rootのphase HP解決は従来どおり`BossPhaseSystem`が所有する。
- lock-onはtarget Entity IDの集合ではなくslot列として扱い、同じpart IDを`lockCapacity`まで保持できる。複数weapon間の占有数も決定的に差し引き、距離、Entity ID、slot順で割り当てる。phaseのpart signalはcompile時にpart／tagをpart handle列へ解決し、Runtimeで文字列検索せずenable、disable、detachを適用する。
- legacy root Actorには派生static componentを追加してよいが、旧Replayのcanonical hashを変えない。v3 partのenabled／detached／parent、health、weapon state、lock slotはcanonical hashへ含める。

### M6: Stage／Presentation

- multi-track Stage programとclock domainを追加する。
- Effect recipe、state animation、post-process passを追加する。
- audio／camera／background trackをEditorへ接続する。

#### M6実装契約

- v3 authoringは`stage-programs/`、`effects/`、`animation-states/`を追加し、各documentは`schemaVersion: 3`と一意なIDを持つ。`StageDefinition.stageProgramId`は任意の互換fieldとし、未指定のv1／v2 Stageは従来の秒単位spawn eventをそのまま使う。空のv3 collectionと未指定fieldは既存source content hashを変えない。
- Stage programは`spawn`、`environment`、`camera`、`audio`、`ui`、`route`のtrackを持つ。compilerはtrack kind、track ID、event frame、node IDの順で固定し、Runtimeは型付きActor／Boss handleとcompile済みscalarだけを実行する。frame 0はStage playing開始後の最初のtickであり、同frameのsignalはこの固定track順で後続trackから観測できる。waitは`timeoutFrames`またはprogramの`endFrame`を必須とし、無期限停止を保存前に拒否する。
- `run-frame`はStage playing中の60Hz tick、`world-time`はQ16.16 accumulatorの整数frameを参照する。`set-world-time-scale`は0〜4をQ16.16へ量子化し、command実行の次tickからmotion、weapon、projectile program、world animationへ適用する。clock scale、accumulator、track cursor、wait開始frame、発火済みsignal、force-clearはcanonical hashへ含める。Boss phase clockは`run-frame`をdefaultとし、明示時だけ`world-time`を使う。
- environment／camera／audio／UI trackはWorldを直接装飾せず、`StagePresentationState`とsemantic eventを更新する。BGMは`SetMusic`としてcrossfadeし、stingerはone-shot、duckはduration付きmusic duckingとしてFrameworkへ渡す。camera shakeは有限durationで解除する。background、scroll、camera、audio、UIのpresentation状態はcanonical hashへ混ぜない。
- Effect recipeはreflectionで列挙済みのsemantic gameplay eventと、その型付きfieldだけを条件式から読む。particle、trail、flash、shake、hit-stop、audio、bloom／color-grade／distortion／afterimage stateへ展開できるが、World、score、resourceを変更できない。density、flash、shakeのaccessibility設定は展開時に適用し、hitbox、enemy bullet outline、lock markerはrecipeやpost-processが無効でも表示する。
- state animationはActor partごとのsemantic stateを`idle`、移動方向、`damaged`、`destroy`などへ解決し、state set IDとsemantic stateをrendererへ渡す。全Entity共通のrun開始時刻ではなくFrameworkのpresentation clockでframeを選ぶ。Editorはv3 schemaを公開し、Stage programの全trackを同一timeline上に表示してbackground／camera／audio nodeをproduction Runtime previewと同じcandidate contentから検証する。

### M7: Editor

- Preview Sandboxとcheckpoint seekを追加する。
- Pattern Graph、Curve、Actor Composer、Stage Timeline、Rule Inspectorを順に追加する。
- undo／redoとID rename transactionを追加する。

#### M7実装契約

- M7のcompiler contractはversion 8、built-in moduleは`3.0.0-m7`とする。diagnostic mapはdefinition／domain／file／JSON Path／node IDに加え、参照chain、解決済みparameter、modifier provenance、推定instruction／spawn budgetを返す。Editorのvalidateは成功時にもこのcompile結果を返し、保存前に全variant bindingとbudgetを検証する。
- Preview Sandboxは簡易再実装を持たず、productionの`ShootingSimulation`、compiled catalog、factory、Systemを使う。full stageに加えてprogram／pattern、Actor、Boss phaseをdefinition IDで隔離起動でき、入力script、signal、world time scaleを同じfixed tickへ注入する。Boss Trainingのcheckpoint探索はlegacy stage event／objectiveだけでなく、compiled Stage programの`BossHandle`参照も対象とする。旧packのcheckpoint選択は変わらない。
- seekは300 frame間隔でimmutable `SimulationCheckpoint`をcacheし、直前checkpointを復元して目的frameまで同じInputFrame列を再生する。checkpointはWorld、ProjectileStore、stage track cursorとsignal、resource／state machine、RNG、clock、pending input edgeを含む。復元後のcontinuation hashと先行実行hashの一致をテストする。
- EditorはJSON text編集に加え、Pattern Graph、Curve、Emitter gizmo、Actor part tree／hurtbox／hardpoint、Stage multi-track timeline、Rule／Resource／State inspectorを提供する。複数選択、copy／paste、undo／redoは単一transaction単位とし、ID renameは参照graphを先に列挙して全変更を一括validateし、1件でも失敗すればfileを変更しない。
- `program`、`parameter-set`、`variant`、`interaction`、`resource`、`rule`、`state-machine`、`actor`、`stage-program`、`effect`、`animation-state`をfile type routingとJSON schemaへ追加する。schema validation後にcandidate content全体をcompileし、局所documentだけでは見つからないunknown reference、cycle、override競合も保存前に拒否する。
- preview結果はprojectileのprogram／source node、rule commandのsource definition／action index、resource snapshot、active state、modifier provenanceを返す。これにより弾、score、resource、modifierをRuntimeで実際に適用したnodeまで追跡できる。
- 10,000 projectile benchmarkはcontent追加順でhomingやVM workloadへ変質しないよう、ID ordinalで最初のprogramなしstraight projectileのscalar値を選び、外部stage event／ruleの影響を除いた固定のminimal stageで600 tick測定する。player projectileに対するenemy target layerが空の場合はcollision method呼び出し前にskipし、命中event用closureをdense loopで生成しない。この基準workloadの意味を変える場合はreport formatを更新する。

各migrationは旧APIをadapterとして残し、利用箇所がゼロになってから別変更で削除する。

## 15. Acceptance pack

特定作品名を使わない3つの小さなfixtureを用意し、同じRuntimeで検証する。

### A. Lock／Break型

- 最大複数lockとlock数倍率
- active中に第2段階へ遷移
- 撃破で時間延長、bomb／deathで終了
- cancelからscore itemへ変換
- player行動でRankが上がり、撃ち返し弾が増える
- 3分Time Attackとboss checkpoint Training

### B. Shot／Laser／Hyper型

- tap／holdでShotとLaserを切り替える
- Hyper中のShotだけがsoft bulletを破壊する
- Laser強度に応じて敵Laserを防ぐ、負ける、押し返す
- Hyper levelが火力、相殺、score、Rankへ同時に作用する
- chain timerが特定の大型敵または地上物で異なる規則を持つ

### C. Mode変奏型

- 同じStage構造で少数高速、chain重視、大量低速の3 variantを切り替える
- variantごとにpattern topologyとscore ruleを差し替える
- difficultyは各variantへ独立した数値curveを重ねる
- 既存の機体／背景／boss partを複製せず共有する

各fixtureでvalidate、headless complete、Replay record/playback、checkpoint restore、全variant budget、10,000 projectile stressを実行する。

## 16. 非目標

- 汎用ゲームエンジンや汎用ビジュアルプログラミング環境にはしない。
- JSONからC#、JavaScript、Luaなどの任意コードを実行しない。
- v3導入と同時に既存v1／v2形式を削除しない。
- cosmetic presentationをSimulation stateへ混ぜない。
- すべてのActorをProjectile用SoAへ移さない。
- local co-op、network、Steam APIはこの設計の初期実装範囲に含めない。ただしplayer scope、InputFrameSet、category keyには拡張点を残す。

## 17. 完了条件

vNextの基盤完了は、機能数ではなく次で判定する。

- Acceptance pack A〜CをRuntime固有分岐なしでDefinitionから構築できる。
- 新しい弾幕programを追加するとき、C#変更を必要としない。
- mode追加時にStageやEnemy definition全体を複製しない。
- 弾、score、resource、modifierの由来をEditorでnodeまで追跡できる。
- 同一Replayのfinal hashがrecord時と一致する。
- v1／v2 content packの挙動と公開Replay契約を維持する。
- 10,000 active projectileでprogram追加前の性能基準から重大な退行がない。
- budget超過、無限cycle、競合override、不明opcodeを保存前に検出できる。

この完了条件を満たすまでは、個別作品専用の`if (ruleSetId == ...)`をRuntimeへ追加して短期的に穴を埋めない。
