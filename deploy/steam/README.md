# Steam非公開betaの投入とQA

SteamworksからAppIDとWindows Depot IDを取得した後に使用します。実IDは生成物だけに展開され、テンプレートには記録しません。Steamアカウント名、パスワード、Steam Guardコードもリポジトリへ保存しません。

## 1. 事前確認

SteamCMDをValve公式手順で導入し、`steamcmd.exe`の場所を確認します。最初に配布物とVDFだけを検証します。

```powershell
./build/Publish-Game.ps1
./deploy/steam/Upload-SteamBuild.ps1 -AppId <APP_ID> -DepotId <DEPOT_ID> -SkipPublish -DryRun
```

生成されたVDFは`artifacts/steam/<APP_ID>/scripts`、SteamCMDのログと一時出力は`artifacts/steam/<APP_ID>/output`に置かれます。どちらもGit管理外です。

## 2. 非公開betaへアップロード

`beta`はSteamworks側であらかじめ作成し、必要ならパスワードを設定します。アップロードは対話可能な端末で実行します。

```powershell
./deploy/steam/Upload-SteamBuild.ps1 `
  -AppId <APP_ID> `
  -DepotId <DEPOT_ID> `
  -SteamCmdPath C:\path\to\steamcmd.exe `
  -Username <STEAM_ACCOUNT>
```

スクリプトは既定でRelease成果物を再生成し、`beta` branchへ設定します。パスワードとSteam GuardコードはSteamCMDのプロンプトへ直接入力します。コマンドライン引数、環境変数、VDFには保存しません。既存成果物を明示的に再利用するときだけ`-SkipPublish`を指定します。

## 3. QA記録

AppID: ______  DepotID: ______  BuildID: ______  Version: ______  実施日: ______

- [ ] 新規Windows環境でSteamからインストールして起動
- [ ] .NET Runtime未導入環境で起動
- [ ] Xbox系ゲームパッドだけで起動、メニュー、1プレイ、リトライ、終了
- [ ] PlayStation系ゲームパッドをSteam Input経由で同じ手順で確認
- [ ] キーボードのみで同じ手順を確認
- [ ] プレイ中のゲームパッド切断、再接続、キーボードへのフォールバック
- [ ] Windowed／BorderlessFullscreen、Alt+Tab、Alt+Enter
- [ ] 1080p、1440p、4K、高DPI、複数モニター
- [ ] 音声デバイスなし、デバイス切替、ミュート
- [ ] sample／gauntletのハイスコアとクリア回数を保存
- [ ] 更新後も`%LOCALAPPDATA%/GoatShooooting`の設定とプロフィールを保持
- [ ] profile破損時に退避、既定値復旧、ログ出力
- [ ] 前のBuildIDへロールバックして起動
- [ ] オフラインモードで起動と1プレイ
- [ ] アンインストール、再インストール後もローカルプロフィールを保持

全項目のBuildIDと結果を記録してから、Steamworks管理画面でテスト済みBuildIDをDefault branchへ手動昇格します。アップロードスクリプトからDefault branchへは反映しません。
