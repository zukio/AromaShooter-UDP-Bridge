# AGENTS.md

## プロジェクトの目的

AromaShooterをUDPで制御するWindowsトレイアプリを実装する。
タイムラインは外部側が担当する。アプリは噴射・停止を公式SDKに中継する。
詳細と受入条件はdocs/SPEC.mdを正本とし、README.mdはユーザーマニュアルとする。
この3文書だけの状態では、アプリは未実装である。

## 作業手順

1. AGENTS.md、docs/SPEC.md、README.md、既存コードを読んでから変更する。
2. 実装依頼の場合は仕様に従い、作成・ビルド・エラー修正・必要なテストまで進める。
3. 既存のユーザー変更を保持する。無関係な機能追加や大規模な整理を行わない。
4. UDP、設定、SDK呼び出しを変更したら、関連する仕様・マニュアル・サンプルも更新する。
5. 終了報告は変更内容、ビルド/テスト結果、未検証部分を簡潔に記載する。

## 技術方針

- C# / WinForms / .NET Framework 4.7.2 / Visual Studio 2022。
- JSONはNewtonsoft.Json。SDKは公式AromaShooterWindowsSDK.dll。
- .NET 6/8等へ独断で変更しない。SDK互換性の根拠が必要。
- SDKの接続、噴射、停止、切断を小さなインターフェースで包み、実機なしで制御ロジックを検証できるようにする。
- UDP受信、コマンド解析、設定保存、制御実行、UIを分離する。過剰なフレームワーク導入はしない。
- 外部パケットとUI操作は同一の制御経路を通す。SDK呼び出しは直列化する。
- BLEスキャンはawaitする。同期USBスキャンでUIを固めない。
- NotifyIconと通信リソースを正しく破棄する。設定画面の閉じる操作はトレイへ戻す。

## 守る動作

- UDPはdocs/SPEC.mdのSHOOT/STOP形式。独断でJSONやOSCへ置換しない。
- 噴射長は必須。強度省略はチャンバーごとの設定値を使う。
- 未接続・不正指示を後から再生しない。未知対象をALLへ置き換えない。
- 新しいSHOOTは対象機器の既存噴射を置換する。他機器は止めない。
- STOPの優先処理と対象の未実行SHOOT取消を実装する。
- 内部ブースター強度0を許可しない。設定だけで噴射しない。
- 設定の保存先は%LOCALAPPDATA%\AromaShooterUdpBridge\settings.json。
- UIとJSONを二重管理しない。設定破損時に元ファイルを上書きしない。
- ポート競合時に別ポートへ自動変更しない。
- タイムライン、.nfc互換、HTTP API、OSC、インストーラーは追加しない。

## SDKの扱い

公式：https://github.com/aromajoin/aromashooter-sdk-windows

- lib/net472/AromaShooterWindowsSDK.dllを参照し、出力へコピーする。
- READMEとsamples/SmokeTest/で呼び出しを照合する。旧版のASControllerSDK/Diffuse/AromaPortを使用しない。
- DLLが不足していれば、ネットワークが許す範囲で公式リポジトリから入手する。
- 正規SDKを入手できない場合は障害を報告する。偽DLLでSDK互換性が確認できたと主張しない。
- APIシグネチャや戻り値を想像で補わない。配布DLL・文書から確認する。
- SDK呼び出し完了と実機噴射の確認を区別する。モック成功を実機成功として報告しない。
- SDKや依存DLLを再配布する際はライセンスと必要なNOTICEを確認する。

## ビルドと検証

Windows環境では必要な.NET Framework 4.7.2開発ツールを用意して、リポジトリルートから以下を実行できる構成にする。

```powershell
msbuild .\AromaShooterUdpBridge.sln /restore /p:Configuration=Release
```

- 自動テストの実行コマンドは、採用したテスト構成に合わせてREADMEへ記載する。
- UDP解析、強度解決、SDK呼び出し順序、STOP優先、設定保存/再読み込みの意味のあるテストを行う。
- 必須検証の範囲はdocs/SPEC.mdの受入条件に従う。
- ビルド失敗は原因を修正して再実行する。通っていない状態で完了と報告しない。
- Windows/.NET Frameworkのビルド環境がなければ、確認できた範囲と実行できなかったチェックを報告する。
- 実機がない場合は実機試験を未実施とする。USB/BLE、最大噴射長、終了時停止を断定しない。
- 実装完了後はREADMEの「実装前」表示を実態に合わせて更新する。

## 完了時の報告

変更点、ビルドとテストの結果、SDK/実機検証の有無、残る制限を記載する。
ユーザーが依頼していないコミット、push、PR公開は行わない。
