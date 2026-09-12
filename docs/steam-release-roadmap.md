# Steam 配信に向けたロードマップと設計

## 1. 目的

`goat-shooooting` を、Windows PC で次の条件を満たす配布可能なゲームへ進める。

- キーボードと一般的なゲームパッドのどちらでも、起動から終了まで操作できる
- ゲーム内で画面、音量、演出、入力を設定できる
- 設定、ハイスコア、最後に選択したコンテンツパックが再起動後も保持される
- .NET SDK を導入していないクリーンな Windows PC で起動できる
- Steam の Depot にそのまま投入できる Release 成果物を再現可能に生成できる
- 既存の headless simulation、Definition Editor、ホットリロードを壊さない

初回リリースは `win-x64` を正式対応対象とする。Linux、macOS、Steam Deck Verified、Steam実績、Steamランキング、Steam Cloud、プレイ途中の再開は後続候補とする。

## 2. 現状と設計上の制約

現在は `KeyboardInputState` がMonoGameのキーボードを直接読み、`ShootingGame` が起動メニュー、画面、音声、シミュレーションをまとめて管理している。`IInputState` はRuntimeにあり、ゲームロジックはMonoGameに依存していない。この境界は維持する。

現在の配布プロジェクトは通常の `Exe` で、RID、self-contained、製品バージョン、アイコン、配布スクリプトを持たない。セーブ先をインストールディレクトリにするとSteam更新や権限の影響を受けるため、ユーザーデータは必ずOSのユーザーデータ領域へ分離する。

## 3. 全体アーキテクチャ

```text
Keyboard ─┐
          ├─> GameInputState ─> IInputState ─> ShootingSimulation
GamePad ──┘         │
                    └─> IMenuInput ─> GameShell state machine

settings.json ─> JsonUserDataStore ─> GameSettings ─┬─> Graphics settings
                                                    ├─> Audio settings
                                                    ├─> Input settings
                                                    └─> Accessibility settings

Simulation completion ─> RunCompletionTracker ─> PlayerProfile ─> profile.json

dotnet publish ─> staged content ─> published smoke test ─> ZIP / Steam Depot
```

### プロジェクト責務

| プロジェクト | 追加する責務 |
|---|---|
| `goat-shooooting.Core` | 変更しない |
| `goat-shooooting.Definitions` | ゲーム制作者が配布する静的Definitionのみ。ユーザー設定を入れない |
| `goat-shooooting.Runtime` | `IInputState` と純粋なゲーム進行。MonoGame、ファイル、Steamに依存させない |
| `goat-shooooting.Platform`（新規） | 設定・プロフィールの型、保存インターフェース、JSON実装、保存先、ログ |
| `goat-shooooting.Framework` | キーボード／ゲームパッド統合、メニュー状態、設定画面、画面・音声への設定適用 |
| `goat-shooooting.SampleGame` | Composition root。保存領域、Repository、Frameworkを組み立てる |

`Platform` はMonoGameへ依存させない。これにより設定とセーブの破損処理、移行、原子的保存をウィンドウなしでテストできる。

## 4. ゲームパッド設計

### 4.1 入力の分離

`KeyboardInputState` を直接 `ShootingGame` が所有する構造から、次の構造へ移行する。

```csharp
public interface IMenuInput
{
    bool UpPressed { get; }
    bool DownPressed { get; }
    bool LeftPressed { get; }
    bool RightPressed { get; }
    bool ConfirmPressed { get; }
    bool CancelPressed { get; }
}

public enum ActiveInputDevice
{
    Keyboard,
    GamePad
}

public sealed class GameInputState : IInputState, IMenuInput
{
    // KeyboardState と GamePadState を毎フレーム読み、論理アクションへ統合する。
}
```

Runtimeが参照するのは引き続き `IInputState` だけとする。メニュー用操作、切断検知、表示用の最終入力デバイスはFramework内に留める。

### 4.2 初期マッピング

| アクション | キーボード | ゲームパッド |
|---|---|---|
| 移動 | WASD / 矢印 | 左スティック / D-pad |
| ショット | Z / Space | A / X |
| ボム | X / Shift | B / Y |
| ポーズ | P | Menu / Start |
| 決定 | Enter / Z / Space | A |
| 戻る | Escape | B |
| リトライ | R / Enter | A |

- 左スティックには初期値 `0.2` の円形デッドゾーンを適用する。
- キーボードとゲームパッドの入力は同じフレームで混在可能にする。
- `Pressed` は前フレームとの差分、移動とショットは現在値として扱う。
- 最後に有効入力があったデバイスを記録し、操作ガイドの表記を切り替える。
- ゲームパッド切断時はプレイ中なら自動ポーズし、再接続またはキーボード操作を案内する。
- v1ではMonoGameの通常GamePad APIを使用する。Steam Input APIは必須依存にしない。

### 4.3 入力設定

最初の完成条件はゲームパッドの標準マッピングとキーボード再割り当てとする。ゲームパッドはSteam Inputでも変更可能なため、ゲーム内再割り当ては第2段階でもよい。

バインド保存では物理キー名を文字列で保持する。未認識値はその項目だけ既定値へ戻し、設定ファイル全体を破棄しない。決定と戻るを同じキーにするなど、メニュー操作不能になる組み合わせは保存前に拒否する。

## 5. 設定設計

### 5.1 設定モデル

```csharp
public sealed record GameSettings
{
    public int SchemaVersion { get; init; } = 1;
    public DisplaySettings Display { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public GameplaySettings Gameplay { get; init; } = new();
    public InputSettings Input { get; init; } = new();
}

public sealed record DisplaySettings
{
    public WindowMode WindowMode { get; init; } = WindowMode.Windowed;
    public int WindowScale { get; init; } = 1;
    public bool VSync { get; init; } = true;
}

public sealed record AudioSettings
{
    public float MasterVolume { get; init; } = 1.0f;
    public float EffectsVolume { get; init; } = 1.0f;
    public bool Muted { get; init; }
}

public sealed record GameplaySettings
{
    public float ScreenShakeStrength { get; init; } = 1.0f;
    public bool ControllerVibration { get; init; } = true;
}
```

値はロード時と保存前の両方で正規化する。音量と画面揺れは `0.0` から `1.0`、`WindowScale` は対応範囲内に制限する。

### 5.2 画面設定

- 初回は `Windowed` と `BorderlessFullscreen` を対応する。
- 排他的フルスクリーンはドライバー差異が大きいため初回範囲外とする。
- ゲーム内の論理解像度はDefinitionの `width`、`height` とHUD幅から決め、物理解像度から分離する。
- 物理ウィンドウへはアスペクト比を維持してスケーリングし、余白はレターボックス表示する。
- `Alt+Enter` でWindowedとBorderlessFullscreenを切り替え、直後に設定へ保存する。
- 適用できない画面設定は直前の正常値へ戻す。

現在の描画座標を直接バックバッファへ描く方式から、論理キャンバス用 `RenderTarget2D` に描いて最後に拡大する方式へ変更すると、ゲームロジックとDefinition座標を変えずに任意のモニターへ対応できる。

### 5.3 音声と演出

`GameAudio` は設定値を受け取り、各SEの実効音量を次で計算する。

```text
実効音量 = muted ? 0 : masterVolume × effectsVolume × soundBaseVolume
```

画面揺れは既存の継続時間を変えず、変位量だけ `ScreenShakeStrength` 倍する。`0` で完全に停止できるため、アクセシビリティ設定としても機能する。

### 5.4 メニュー状態

`ShootingGame` 内の条件分岐を増やし続けず、Frameworkにテスト可能な `GameShell` 状態機械を追加する。

```text
Title ──Start──> Playing
  │                │
  └─Options        ├─Pause──> Pause
                   │           ├─Resume──> Playing
                   │           ├─Options
                   │           └─Title──> Title
                   └─Result──> Result
                                ├─Retry──> Playing
                                └─Title──> Title
```

タイトルメニューは `START`、`OPTIONS`、`QUIT`。ポーズメニューは `RESUME`、`OPTIONS`、`RETRY`、`TITLE`。設定変更は原則即時プレビューし、Optionsを閉じるときに保存する。

## 6. セーブ設計

### 6.1 v1で保存するもの

- `settings.json`: 画面、音量、演出、入力設定
- `profile.json`: コンテンツパック別ハイスコア、クリア回数、最後に選択したコンテンツパック

プレイ中のWorld、敵、弾、乱数状態は保存しない。短時間のスコアアタックとして、1プレイの中断再開は初回リリースの対象外とする。

```csharp
public sealed record PlayerProfile
{
    public int SchemaVersion { get; init; } = 1;
    public string LastGameId { get; init; } = "sample";
    public Dictionary<string, int> HighScores { get; init; } = new();
    public Dictionary<string, int> ClearCounts { get; init; } = new();
}

public interface IUserDataStore
{
    LoadResult<GameSettings> LoadSettings();
    LoadResult<PlayerProfile> LoadProfile();
    void SaveSettings(GameSettings settings);
    void SaveProfile(PlayerProfile profile);
}
```

ハイスコアのキーはv1では `gameId` とする。将来難易度を追加するときは `gameId:difficultyId` へ移行できるよう、更新処理を `PlayerProfileService` に閉じ込める。

### 6.2 保存先

Windowsでは次を使用する。

```text
%LOCALAPPDATA%/GoatShooooting/
  settings.json
  profile.json
  logs/latest.log
```

実行ファイル横、SteamのDepot、`games/` 以下にはユーザーデータを書かない。将来Steam Cloudを有効にするときは、同期対象を `profile.json` に限定する。端末依存の解像度を含む `settings.json` は原則同期しない。

### 6.3 信頼性

- JSONは一時ファイルへ書き、flush後に同一ディレクトリ内で置換する。
- 保存の途中で終了しても、直前の正常ファイルを残す。
- `SchemaVersion` ごとの明示的な移行を用意する。
- 不正JSONは `.invalid-<UTC timestamp>` へ退避し、既定値で起動する。
- 読み込み・保存失敗はログへ記録するが、原則としてゲーム起動を妨げない。
- 1回のラン終了につきプロフィール保存は1回だけ行う。

`RunCompletionTracker` が前フレームと現在の `SimulationStatus` を比較し、`Running` から `GameOver` または `StageClear` へ変わったときだけスコアを反映する。リトライ中の重複保存を防ぐ処理をFrameworkまたはPlatform側でテストする。

## 7. 正式な配布ビルド設計

### 7.1 Publish方針

初回は次の設定を採用する。

| 項目 | 値 | 理由 |
|---|---|---|
| Configuration | `Release` | 最適化された製品ビルド |
| Runtime Identifier | `win-x64` | 初回の正式対応環境 |
| Self-contained | `true` | ユーザー側の.NET導入を不要にする |
| Single file | `false` | MonoGameのnative libraryとSteam差分更新を扱いやすくする |
| Trimming | `false` | JSON reflectionとFrameworkの互換性を優先する |
| ReadyToRun | 初回は`false` | 成果物肥大化を避け、先に互換性を安定させる |

`goat-shooooting.SampleGame.csproj` に製品名、会社名、Copyright、Version、ApplicationIcon、`CopyToPublishDirectory` を設定する。正式配布物へは次だけを含める。

```text
GoatShooooting.exe
*.dll / native libraries / *.json runtime files
games/sample/**
games/gauntlet/**
THIRD-PARTY-NOTICES.txt
```

Tooling、Editor、schemas、テスト、開発用PDBはプレイヤー向けDepotへ含めない。シンボルは別のCI artifactとして保管する。

### 7.2 ローカル配布コマンド

`build/Publish-Game.ps1` を追加し、次を一括実行する。

1. Release buildと全テスト
2. sample／gauntletのDefinition検証
3. `dotnet publish` による `artifacts/publish/win-x64` 生成
4. 公開成果物のexeを使った両コンテンツパックのsmoke test
5. 不要ファイル混入チェック
6. `artifacts/packages/goat-shooooting-<version>-win-x64.zip` 作成
7. SHA-256チェックサム生成

基準となるpublishコマンドは次とする。

```powershell
dotnet publish src/goat-shooooting.SampleGame/goat-shooooting.SampleGame.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish/win-x64
```

### 7.3 CI

`.github/workflows/release.yml` を追加し、tagまたは手動実行で次を行う。

- `dotnet restore --locked-mode`
- `dotnet format --verify-no-changes`
- `dotnet test -c Release`
- Definition検証
- win-x64 publish
- 公開成果物からsmoke test
- ZIP、チェックサム、シンボルをartifactへ保存

再現性のため `packages.lock.json` を導入し、NuGet依存を固定する。リリースタグとAssembly/File Versionは単一のバージョン値から設定する。

### 7.4 SteamPipe

AppID取得後に以下を追加する。

```text
deploy/steam/
  app_build_<appid>.vdf
  depot_build_<depotid>.vdf
  Upload-SteamBuild.ps1
```

- `ContentRoot` は `artifacts/publish/win-x64` を指す。
- AppID未取得時は実IDをコミットせず、`app_build.template.vdf` と `depot_build.template.vdf` を使用する。実ID入りVDFは `artifacts/steam` に生成する。
- Steamアカウント、パスワード、Steam Guardコードはリポジトリへ保存しない。
- 初回はSteamPipeの非公開beta branchへ投入し、Steamクライアントからインストールして確認する。
- Default branchへの反映はテスト済みBuildIDをSteamworks管理画面から手動で行う。

## 8. テスト戦略

### 自動テスト

| 対象 | 必須ケース |
|---|---|
| 入力統合 | キーボード、D-pad、スティック、同時入力、エッジ検出、デッドゾーン |
| 切断処理 | 切断時の自動ポーズ、キーボードへのフォールバック、再接続 |
| メニュー | 全状態遷移、キャンセル、設定変更、操作不能な割り当て拒否 |
| 設定 | default、round-trip、範囲補正、未知プロパティ、旧version移行、破損回復 |
| プロフィール | 初回作成、ハイスコア更新、低いスコアの無視、クリア回数、重複保存防止 |
| ファイル保存 | 原子的置換、書込み失敗、読込み失敗、インストール先へ書かないこと |
| 配布物 | games同梱、開発ファイル除外、公開exeによる両smoke test |

### 手動QAマトリクス

- Xbox系ゲームパッド、PlayStation系をSteam Input経由で確認
- キーボードのみ、ゲームパッドのみ、プレイ中の接続・切断
- 1080p、1440p、4K、高DPI、複数モニター
- Windowed／BorderlessFullscreen、Alt+Tab、Alt+Enter
- 音声デバイスなし、デバイス切替、ミュート
- 読取専用のインストール先、非ASCIIユーザー名
- セーブファイル破損、旧version、書込み不可
- Steamクライアントからインストール、起動、アンインストール、再インストール
- オフライン起動

## 9. ロードマップ

見積りは1人で既存コードを理解している前提の実装日数で、ストア素材や外部QAの制作期間を含まない。

### Phase 0: 方針固定（0.5日）

- 初回正式対応をWindows x64に固定
- セーブ対象を設定、ハイスコア、クリア回数、最終gameIdに固定
- Windowed／BorderlessFullscreenと論理キャンバス方式を採用
- 製品名、会社名、バージョン規約、AppID未取得時のplaceholder方針を決定

完了条件: 本文の未決事項がなく、v1の対象外機能が合意されている。

### Phase 1: Platform基盤（1.5〜2日）

- `goat-shooooting.Platform` とテストプロジェクトを追加
- `GameSettings`、`PlayerProfile`、`IUserDataStore` を実装
- LocalAppDataパス、原子的JSON保存、破損回復、schema migration、ログを実装

完了条件: ウィンドウなしのテストで、正常保存、破損、旧version、I/O失敗を検証できる。

### Phase 2: ゲームパッドと入力統合（2〜3日）

- `GameInputState` と `IMenuInput` を追加
- GamePad、deadzone、エッジ検出、最終デバイス検出を実装
- 切断時自動ポーズと操作表示切替を実装
- キーボード再割り当てを実装

完了条件: マウスなしでゲーム起動、メニュー操作、プレイ、ポーズ、リトライ、終了ができる。

### Phase 3: 設定画面と表示基盤（3〜4日）

- `GameShell` 状態機械、Title／Pause／Options画面を実装
- RenderTargetによる論理解像度とレターボックスを実装
- Window mode、VSync、window scaleを適用
- Master／SE音量、mute、画面揺れ、振動を適用
- Options終了時の保存を接続

完了条件: 設定が即時反映され、再起動後も復元され、適用失敗時に安全に戻る。

### Phase 4: プロフィールとハイスコア（1〜2日）

- `RunCompletionTracker` と `PlayerProfileService` を実装
- GameOver／All Clear時の保存とタイトル画面のハイスコア表示を実装
- 最終gameIdの復元とゲーム内コンテンツ選択を実装

完了条件: 各コンテンツパックのハイスコアが独立して保存され、リトライで二重計上されない。

### Phase 5: 正式配布パイプライン（2〜3日）

- 製品metadata、アイコン、Third Party Noticesを追加
- `Publish-Game.ps1` とRelease workflowを追加
- 公開成果物からのsmoke test、ZIP、checksumを実装
- クリーンWindows環境で起動確認

完了条件: tagまたは手動CIから、Steam Depotへ投入可能な同一構造の成果物を生成できる。

### Phase 6: Steam非公開beta QA（2〜3日＋フィードバック対応）

- SteamPipe設定とアップロードスクリプトを追加
- 非公開beta branchへアップロード
- Steamクライアント経由のインストール、更新、ロールバック、オフライン起動を確認
- 対応コントローラーとシステム要件のストア表記を実測に合わせる

完了条件: 新規環境でSteamからインストールし、ゲームパッドだけで1プレイを完走でき、セーブが更新後も残る。

全体の実装目安は10〜15開発日。最短経路ではPhase 1とPhase 2を先に完了し、Phase 3のうち音量・画面揺れ・BorderlessFullscreenだけを実装してからPhase 4、5へ進む。

## 10. Definition of Done

初回Steam候補ビルドは、次をすべて満たしたとき完成とする。

- 全テスト、両Definition検証、両smoke testがReleaseで成功する
- 公開フォルダーのexeが.NET未導入のクリーンWindowsで起動する
- ゲームパッドだけで起動から終了まで操作できる
- 画面、音量、演出、入力設定が再起動後も保持される
- sample／gauntletのハイスコアとクリア回数が正しく保持される
- セーブ破損時もクラッシュせず、既定値で起動してログが残る
- Windowed、BorderlessFullscreen、Alt+Tab、コントローラー切断で進行不能にならない
- Steamの非公開beta branchからインストールしたビルドで同じ確認が通る
- 配布物に開発ツール、テスト、認証情報、不要な個人ファイルが含まれない
- 製品バージョン、Third Party Notices、ゲーム内クレジットが確認できる

## 11. 後続候補

初回リリース後または発売候補版の安定後に評価する。

- Steam Cloud: `profile.json` のみ同期
- Steam Achievements: 初クリア、ノーミス、ボム未使用など
- Steam Leaderboards: gameIdと難易度ごとのスコア
- Steam Input API: action set、デバイス別glyph、公式config
- Steam Deck／Linux native buildまたはProton正式確認
- プレイ途中の再開、リプレイ保存、クラッシュレポート送信
- BGM音量、言語、色覚・弾視認性など追加アクセシビリティ設定
