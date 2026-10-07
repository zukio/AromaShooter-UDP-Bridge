# AromaShooter UDP Bridge

UDPで受け取ったコマンドに従い、[AromaShooter](https://aromajoin.com/)の香りを噴射・停止するWindowsトレイアプリです。
タイムラインやキューのタイミングは外部ソフトで管理します。

## 必要なもの

- Windows 10 / 11、.NET Framework 4.7.2以降の4.xランタイム。
- AromaShooter本体とカートリッジ。
- USB接続：WindowsからCOM機器として認識されること。必要なら機器に対応したドライバーを導入。
- BLE接続：Bluetooth LE対応アダプターとWindowsのBLEサポート。
- 配布フォルダー内のAromaShooterUdpBridge.exe
- 公式から入手した[AromaShooterWindowsSDK.dll](https://github.com/aromajoin/aromashooter-sdk-windows)、その他の同梱依存DLL。

## 起動と基本操作

1. AromaShooterを接続し、電源を入れます。
2. AromaShooterUdpBridge.exeを起動します。
3. タスクトレイのアイコンをダブルクリックして設定画面を開きます。
4. 接続方式USB/BLEとUDP受信ポートを設定し、［適用して保存］を押します。
5. ［再接続］で機器を検出し、表示されたシリアル番号を確認します。
6. テスト操作で対象、チャンバー、噴射長を選び、噴射を確認します。
7. 外部ソフトから本アプリのPCのIPアドレスとUDPポートへコマンドを送信します。

起動時自動接続が有効なら起動時に検出します。設定画面を閉じても常駐は続きます。
終了はトレイメニューの［終了］を使用します。二重起動はせず、既存の設定画面を表示します。

## トレイメニュー

| 項目                 | 操作                                         |
| -------------------- | -------------------------------------------- |
| 設定・状態           | 設定、接続一覧、受信ログ、テスト操作を表示   |
| 全停止               | 接続中の全機器を停止。待機中の噴射指示も取消 |
| 再接続               | 選択した接続方式で再検出・接続               |
| 終了                 | 受信を停止し、機器を停止・切断して終了       |

## 設定

設定ウィンドウ左下の［設定フォルダーを開く］リンクで、設定とログの保存先を開けます。［設定を再読み込み］はウィンドウ右下から操作します。

初期UDPポートは10000、接続方式はUSBです。
待受IPはこのPC側の受信インターフェースを指定するもので、送信元IPの許可リストではありません。
待受アドレス0.0.0.0は別PCからの受信にも対応します。同じPCだけなら127.0.0.1へ変更できます。

画面はライト基調です。［詳細設定］にチャンバー既定強度・内部/外部ブースター、［テスト］にテスト操作をまとめ、初期状態では折りたたみます。見出しをクリックすると開閉できます。右上の矢印アイコンは再接続です。下部右側に［設定を再読み込み］［リセット］［適用して保存］を配置し、［適用して保存］を強調しています。［リセット］は確認後に既定値を保存・適用します。

チャンバー1〜6にそれぞれ既定強度を設定できます。UDPで強度を省略したときに使用する値です。
設定や接続だけでは噴射しません。設定変更は次の噴射から有効です。
内部ブースターは香りを出すために必要で、強度1〜100で設定します。外部ブースターの既定値は0です。

設定ファイルは次の場所です。

```text
%LOCALAPPDATA%\AromaShooterUdpBridge\settings.json
```

```json
{
  "schemaVersion": 1,
  "udp": {
    "listenAddress": "0.0.0.0",
    "port": 10000
  },
  "device": {
    "transport": "USB",
    "autoConnect": true
  },
  "shoot": {
    "defaultIntensities": [100, 100, 100, 100, 100, 100],
    "internalBoosterIntensity": 100,
    "externalBoosterIntensity": 0
  }
}
```

defaultIntensitiesはチャンバー1〜6の順に6個、各0〜100です。
UIから保存した内容もこのファイルに保存されます。
外部編集後は［設定を再読み込み］を押してください。自動反映はしません。
既存設定が壊れている場合は上書きせず、待受と自動接続を開始しません。ファイルを修正して再読み込みするか、UIから明示的に既定値へ戻します。
ポート変更に失敗した場合は以前の待受を維持します。接続方式変更時は噴射を止めて旧機器を切断します。

## UDPコマンド

これはOSCではなく、UTF-8のプレーンテキストUDPです。
1回の送信に1コマンドを入れます。時刻予約・応答・ACKはありません。

### 噴射

```text
SHOOT 対象 チャンバー 噴射長ms [強度]
```

全機器のチャンバー1を、設定済みの強度で3秒噴射：

```text
SHOOT ALL 1 3000
```

全機器のチャンバー1と3を、それぞれの既定強度で3秒噴射：

```text
SHOOT ALL 1,3 3000
```

チャンバー1を強度50、チャンバー3を強度80で3秒噴射：

```text
SHOOT ALL 1,3 3000 50,80
```

特定機器のチャンバー2を強度70で1.5秒噴射：

```text
SHOOT ASN3A01192 2 1500 70
```

ASN3A01192は例です。設定画面に表示された実機のシリアル番号へ置き換えてください。
チャンバーは1〜6、強度は0〜100。複数指定では強度の個数と順序をチャンバーに合わせます。
噴射長は必須の正整数で、3000は3秒です。実装上の入力範囲は1〜2147483647msですが、実機の対応範囲は検証が必要です。
指定した強度は今回の噴射だけに使用し、設定の既定強度は変更しません。

同じ機器に次のSHOOTを送ると、前の噴射を停止して新しい噴射へ切り替えます。
同時に出すチャンバーは1つのコマンドでまとめて指定してください。
ALLはその時点の接続機器のみを対象にします。未接続時のコマンドは後から再生しません。

### 停止

全機器の全チャンバーとブースターを停止：

```text
STOP ALL
```

特定機器だけ停止：

```text
STOP ASN3A01192
```

STOPは対象機器への未実行SHOOTも取り消します。他の機器は停止しません。

### PowerShellから送信する例

同じPCのUDPポート10000へ送ります。別PCの場合は127.0.0.1をアプリが動いているPCのIPへ変更します。

```powershell
$udpClient = New-Object System.Net.Sockets.UdpClient
try {
    $message = 'SHOOT ALL 1 3000'
    $payload = [System.Text.Encoding]::UTF8.GetBytes($message)
    [void]$udpClient.Send($payload, $payload.Length, '127.0.0.1', 10000)
}
finally {
    $udpClient.Dispose()
}
```

停止を送る場合は$messageを'STOP ALL'にします。
Companion等では送信先IP、UDPポート、送信テキストを設定します。OSCの送信アクションは使用しません。

## 状態表示とログ

設定画面でUDP待受、検出済み機器、最終エラー、受信コマンドを確認できます。
［SDK呼び出し完了］はSDKの呼び出しが終了した意味で、実際に香りが出たことの確認ではありません。
接続一覧は最後の検出結果です。機器を抜いた際の即時検知を保証する表示ではありません。
ログは設定フォルダー内のlogs/へ日別に保存し、7日分を保持します。

## 困ったとき

| 症状                       | 確認事項                                                                      |
| -------------------------- | ----------------------------------------------------------------------------- |
| UDP待受を開始できない      | 別アプリが同じポートを使用していないか確認。設定画面のエラーを確認            |
| 別PCから受信できない       | 宛先IP/ポート、待受アドレス、Windowsファイアウォールを確認                    |
| 機器が見つからない         | USB/BLEの選択、機器電源、COM認識またはBluetooth LEを確認して再接続            |
| USB接続できない            | AromaPlayer等が同じCOMポートを使用していないか確認。使用中のアプリを終了/切断 |
| 受信したが噴射しない       | ログの拒否理由、対象シリアル、チャンバー、強度、カートリッジを確認            |
| JSONを編集しても変わらない | ［設定を再読み込み］を実行。JSONの構文・項目・値の範囲を確認                  |

別PCからの受信には、必要に応じてWindows Defenderファイアウォールの詳細設定で受信規則を追加します。
規則は本アプリのexeを指定し、プロトコルUDP、設定したローカルポート、利用するネットワークプロファイルと送信元PCに合わせて設定します。アプリは規則を自動追加しません。

UDPは欠落・重複・順序の入れ替わりが起こり得ます。噴射は必ず噴射長を指定して自動停止させます。
SHOOTの再送は噴射の再開始になります。停止はトレイの［全停止］でも操作できます。
アプリの異常終了や機器の異常時には終了処理の実行を保証できません。

## 開発者向け

実装仕様はdocs/SPEC.md、Codexの作業指示はAGENTS.mdを参照してください。
SDKの公式READMEとサンプル：<https://github.com/aromajoin/aromashooter-sdk-windows>

公式配布のlib/net472/AromaShooterWindowsSDK.dllを同じ相対パスへ配置してください（DLLは既存の.gitignoreによりGit管理対象外）。Visual Studio 2022の.NETデスクトップ開発、.NET Framework 4.7.2開発ツール、.NET SDK 8を備えたWindowsで、リポジトリルートから実行します。global.jsonはビルド用SDK 8を選択しますが、アプリの実行対象は.NET Framework 4.7.2です。

```powershell
msbuild .\AromaShooterUdpBridge.sln /restore /p:Configuration=Release
```

Developer PowerShell以外では、同じソリューションを`dotnet build .\AromaShooterUdpBridge.sln -c Release`でもビルドできます。Newtonsoft.Json 13.0.3はNuGetから復元されます。

自動テスト（外部テストランナー不要、失敗時は終了コード1）：

```powershell
.\tests\bin\Release\net472\AromaShooterUdpBridge.Tests.exe
.\tests\bin\Release\net472\AromaShooterUdpBridge.Tests.exe --ui
```

`--ui`はWindowsの対話セッションで実行してください。一時的に設定画面とトレイアイコンを表示し、二重起動通知・画面を閉じた後の常駐・トレイ終了を検証します。両テストとも一時フォルダーの設定と模擬SDKを使用し、通常の設定ファイルや実機を操作しません。テストの一時フォルダーは出力に表示します。

配布物は`src/bin/Release/net472/`です。このフォルダーのexe、exe.config、AromaShooterWindowsSDK.dll、Newtonsoft.Json.dll、licenses/を一緒に配布してください。settings.example.jsonは参照例であり、実際の設定保存先は常にLocalAppDataです。

構成：

- `src/Protocol.cs`：UTF-8とUDPコマンドの検証。
- `src/ControlEngine.cs`、`src/Device.cs`：直列制御、STOP優先、公式SDKアダプター。
- `src/Settings.cs`、`src/BridgeHost.cs`：厳密な設定検証、原子的保存、待受切替と復旧。
- `src/UdpListener.cs`、`src/BridgeLog.cs`：UDP受信、直近500件・日別7日分のログ。
- `src/Program.cs`、`src/SingleInstance.cs`、`src/SettingsForm.cs`：トレイ常駐、単一起動、設定UI。

社内のtrayIconAppTemplateからNotifyIcon・ContextMenuStrip・コンポーネント所有の構成とアイコンを流用しました。.NET 6固有の起動処理は.NET Framework用に変更し、ApplicationContextが終了処理を管理します。元テンプレートは変更していません。

2026-10-07：Releaseビルド成功（警告0・エラー0）。制御・設定・UDPの64チェックとUIの5チェックが成功しました。公式DLLのAPIシグネチャは照合済みです。USB/BLEの接続・実噴射・自動停止・最大噴射長・抜線・実機終了時停止は未検証です。詳細は[検証記録](docs/VERIFICATION.md)を参照してください。
