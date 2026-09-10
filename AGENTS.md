# Repository Guidelines

## プロジェクト構成とモジュール

ソースコードは責務ごとに `src/` 配下へ分割されています。`goat-shooooting.Core` は Entity、Component、World の状態、`Definitions` は静的設定の読込と検証、`Runtime` は Factory、System、ヘッドレスシミュレーション、`Framework` は MonoGame の入力・描画アダプター、`SampleGame` は実行エントリーポイントを担当します。テストは `tests/` で各層に対応し、層をまたぐシナリオは `goat-shooooting.IntegrationTests` に配置します。ゲーム定義 JSON は `games/sample/` の `enemies/`、`bullets/`、`weapons/`、`stages/` にあります。`Core` と `Runtime` を MonoGame へ依存させず、定義にはファイル API ではなく `IDefinitionRepository` 経由でアクセスしてください。

## ビルド・テスト・開発コマンド

次のコマンドはリポジトリルートで実行します。

- `dotnet restore`: MonoGame と xUnit の依存パッケージを取得します。
- `dotnet build goat-shooooting.sln`: 全プロジェクトをビルドします。警告もエラーとして扱われます。
- `dotnet test goat-shooooting.sln`: 単体テストと統合テストを実行します。
- `dotnet run --project src/goat-shooooting.SampleGame`: デスクトップ版ゲームを起動します。
- `dotnet run --project src/goat-shooooting.SampleGame -- --smoke-test`: ウィンドウを開かずに本番用シミュレーション経路を検証します。

使用する SDK は `global.json` で .NET 8 に固定されています。

## コーディング規約と命名

C# 12、nullable reference types、implicit usings を使用します。既存コードに合わせてインデントはスペース 4 個、namespace はファイルスコープ形式とします。型、メソッド、公開メンバーは `PascalCase`、引数とローカル変数は `camelCase`、private フィールドは `_camelCase` で命名してください。クラスは小さく保ち、可能なら `sealed` にします。依存オブジェクトはコンストラクターで検証し、更新・描画ロジックは Component ではなく System に配置します。広範な変更の提出前には `dotnet format --verify-no-changes` を実行してください。

## テスト方針

テストフレームワークは xUnit です。テストクラスは対象名に `Tests` を付け（例: `WeaponSystemTests`）、テストメソッドは期待する振る舞いを表す名前にします（例: `FireCreatesBulletButCooldownPreventsImmediateSecondShot`）。変更した層には焦点を絞った単体テストを追加し、シミュレーション全体の経路が変わる場合は統合テストも追加してください。数値によるカバレッジ基準はありませんが、変更した振る舞いと失敗ケースを明示的に保護します。Pull Request 前に全テストと smoke test を実行してください。

## Commit と Pull Request

現在の履歴では `feat: complete goat-shooooting MVP` のような Conventional Commits 形式が使われています。`feat:`、`fix:`、`test:`、`docs:` などの短い接頭辞と命令形の要約を使用してください。Pull Request には、利用者への影響または設計上の変更、影響する定義や System、関連 Issue、実行したテストを記載します。描画変更にはスクリーンショットを添付し、新しいゲーム内容を追加する場合はサンプル JSON も含めてください。
