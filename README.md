# Factory I/O SLMP / Host Link Gateway

Factory I/O の `Modbus TCP/IP Client` と実 PLC を接続する Windows デスクトップアプリです。

本アプリは Modbus TCP サーバーとして待ち受け、Factory I/O から見える Coil / Discrete Input / Holding Register / Input Register を、PLC の SLMP または KEYENCE Host Link デバイスへ中継します。

## 構成

```text
Factory I/O
  Modbus TCP/IP Client
        |
        v
Factory I/O Gateway
  Modbus TCP Server
        |
        v
PLC
  SLMP または KEYENCE Host Link
```

使用するローカルライブラリは次の前提です。

```text
D:\APP\NModbus
D:\APP\plc-comm-slmp-dotnet
D:\APP\plc-comm-hostlink-dotnet
```

## 動作環境

- Windows
- .NET 9 SDK
- Factory I/O
- SLMP 対応 PLC または KEYENCE Host Link 対応 PLC

## ビルドと起動

開発用ビルド:

```bat
dotnet build GatewayApp.sln --no-restore
dotnet run --project GatewayApp\GatewayApp.csproj --no-restore
```

1ファイル exe 作成:

```bat
compile.bat
```

出力先:

```text
publish\win-x64-single\GatewayApp.exe
```

作成済み exe の起動:

```bat
start.bat
```

`compile.bat` は `GatewayApp.exe` が起動中の場合、上書き publish を止めます。アプリを閉じてから再実行してください。

## 最初の起動手順

1. `compile.bat` または `dotnet run` でアプリを起動します。
2. `通信` -> `PLC通信設定` で PLC への接続先を設定します。
3. `通信` -> `Modbus通信設定` で Factory I/O から接続する Modbus 待受を設定します。
4. `ファイル` -> `Factory I/O タグ CSV インポート` でタグ CSV を読み込みます。
5. 必要に応じて `ツール` -> `PLCアドレス一括割当` で PLC アドレスを割り当てます。
6. `Mapping` タブで PLC アドレスと表示型を確認します。
7. `起動` ボタンで通信を開始します。
8. `Monitor` タブで値、Force、ログを確認します。

## Factory I/O 側設定

Factory I/O のドライバは `Modbus TCP/IP Client` を使用します。

設定例:

| 項目 | 値 |
|---|---|
| Host | gateway を起動している PC の IP |
| Port | gateway の Modbus ポート |
| Unit ID | gateway の Unit ID |

gateway の初期値は次の通りです。

| 項目 | 初期値 |
|---|---|
| 待受 IP | `127.0.0.1` |
| Port | `502` |
| Unit ID | `1` |

別 PC の Factory I/O から接続する場合は、待受 IP を PC の LAN 側 IP、または `0.0.0.0` に変更してください。Windows ファイアウォールで対象ポートを許可する必要があります。

## 通信設定

### PLC 通信設定

`通信` -> `PLC通信設定` で設定します。

| 項目 | 内容 |
|---|---|
| プロトコル | `SLMP` または `Host Link` |
| ホスト | PLC の IP アドレスまたはホスト名 |
| ポート | PLC の通信ポート |
| タイムアウト | PLC 通信のタイムアウト秒 |
| ポーリング | PLC と Modbus データを同期する周期 ms |
| 機種 | PLC プロファイル |

SLMP の機種は人間向け表示で選びます。設定ファイルにはカノニカル名で保存されます。

| 表示 | 保存値 |
|---|---|
| iQ-R | `melsec:iq-r` |
| iQ-F | `melsec:iq-f` |
| iQ-L | `melsec:iq-l` |
| MX-R | `melsec:mx-r` |
| MX-F | `melsec:mx-f` |
| QnUDV | `melsec:qnudv` |
| QnU | `melsec:qnu` |
| QCPU | `melsec:qcpu` |
| LCPU | `melsec:lcpu` |

Host Link の機種も人間向け表示で選びます。設定ファイルにはカノニカル名で保存されます。

| 表示 | 保存値 |
|---|---|
| KV-Nano | `keyence:kv-nano` |
| KV-Nano / XYM | `keyence:kv-nano-xym` |
| KV-3000 | `keyence:kv-3000` |
| KV-3000 / XYM | `keyence:kv-3000-xym` |
| KV-5000 | `keyence:kv-5000` |
| KV-5000 / XYM | `keyence:kv-5000-xym` |
| KV-7000 | `keyence:kv-7000` |
| KV-7000 / XYM | `keyence:kv-7000-xym` |
| KV-8000 | `keyence:kv-8000` |
| KV-8000 / XYM | `keyence:kv-8000-xym` |
| KV-X500 | `keyence:kv-x500` |
| KV-X500 / XYM | `keyence:kv-x500-xym` |

稼働中は PLC 通信設定を変更できません。停止してから変更してください。

### Modbus 通信設定

`通信` -> `Modbus通信設定` で設定します。

| 項目 | 内容 |
|---|---|
| 待受 IP | gateway が待ち受ける IP |
| ポート | Modbus TCP ポート |
| Unit ID | Modbus Unit ID |
| Scale | Float 表示時のスケール |
| Coil 最大アドレス | Coil の最終アドレス |
| DI 最大アドレス | Discrete Input の最終アドレス |
| HR 最大アドレス | Holding Register の最終アドレス |
| IR 最大アドレス | Input Register の最終アドレス |

最大アドレスを変更すると、`Mapping` と `Monitor` の行数も同期します。最大アドレスは `0` 始まりです。たとえば最大アドレス `15` は 16 点です。

稼働中は Modbus 通信設定を変更できません。停止してから変更してください。

## Modbus 種別と通信方向

gateway では Modbus 種別ごとに方向を固定しています。Mapping 画面で方向は選択しません。

| Modbus 種別 | 方向 | 内容 |
|---|---|---|
| Coil | Factory I/O -> gateway -> PLC | Factory I/O が書いたビット値を PLC へ書く |
| Holding Register | Factory I/O -> gateway -> PLC | Factory I/O が書いたレジスタ値を PLC へ書く |
| Discrete Input | PLC -> gateway -> Factory I/O | PLC から読んだビット値を Factory I/O へ返す |
| Input Register | PLC -> gateway -> Factory I/O | PLC から読んだレジスタ値を Factory I/O へ返す |

`PLC(X)` は Factory I/O から PLC へ向かう側、`PLC(Y)` は PLC から Factory I/O へ返す側として表示します。

## Mapping 画面

`Mapping` タブでは各 Modbus アドレスと PLC アドレスを対応付けます。

| 列 | 内容 |
|---|---|
| Modbus種別 | Coil / DiscreteInput / HoldingRegister / InputRegister |
| Modbusアドレス | 0 始まりの Modbus アドレス |
| PLCアドレス | PLC 側のデバイスアドレス |
| 表示型 | `Bool`, `Int`, `Float` |
| コメント | Factory I/O タグ名などの任意コメント |

注意:

- Coil / Discrete Input は `Bool` 固定です。
- Holding Register / Input Register は `Int` または `Float` を選択できます。
- Mapping 画面に現在値表示や Force 操作はありません。Force は Monitor 画面で行います。
- PLC アドレスが空の行は PLC 通信対象外です。

## Monitor 画面

`Monitor` タブは通信中の確認と Force 操作用の画面です。

画面中央上部に `Modbus TCP/IP Client` と表示されます。左側が Factory I/O の Sensors 側、右側が Actuators 側です。

| 表示 | 内容 |
|---|---|
| SENSORS | Coil / Holding Register 側のコメント |
| PLC(X) | Factory I/O -> PLC 側の PLC アドレス |
| Coil / Holding Reg | Modbus の Coil / HR |
| Input / Input Reg | Modbus の DI / IR |
| PLC(Y) | PLC -> Factory I/O 側の PLC アドレス |
| ACTUATORS | Discrete Input / Input Register 側のコメント |

値の色:

| 状態 | 表示 |
|---|---|
| 通常値 | 緑 |
| Force 値 | オレンジ |
| OFF / 0 | 暗色 |

レジスタ値にカーソルを乗せると、保持している生の整数値をツールチップで確認できます。

## Force 操作

Force は `Monitor` タブで操作します。

| 対象 | 操作 |
|---|---|
| Coil / Discrete Input | LED または値をクリックして ON/OFF を切り替える |
| Holding Register / Input Register | 値をクリックして入力し、Enter で確定する |

Force は X 側と Y 側で分かれています。

| ボタン | 対象 |
|---|---|
| FORCE X | Coil / Holding Register |
| FORCE Y | Discrete Input / Input Register |

Force を有効にした時点で、現在値を Force 値として引き継ぎます。値を入力していない状態でも、Force 中の値はオレンジ表示になります。

Force の動作:

- Coil / Holding Register は PLC へ書き込みます。
- Discrete Input / Input Register は PLC へは書かず、Factory I/O へ返す Modbus 値を強制します。
- Force を解除すると通常の通信値に戻ります。

## Int / Float / Scale

PLC と Modbus のレジスタ値は 16 bit の整数 raw 値として扱います。

`Int`:

- raw 値を符号付き 16 bit 整数として表示します。
- 例: raw `65535` は `-1` と表示します。

`Float`:

- raw 値を `Scale` で割って表示します。
- PLC や Modbus に書く値は raw 整数のままです。

例:

| Scale | raw | 表示 |
|---:|---:|---:|
| 100 | 1000 | 10.00 |
| 100 | -1000 | -10.00 |
| 10 | 1000 | 100.00 |

通信時の扱い:

- Factory I/O から来た HR raw 値は、そのまま PLC へ書きます。
- PLC から来た raw 値は、そのまま Modbus IR/DI/Coil/HR データストアへ書きます。
- 表示だけ `Scale` で割ります。
- Force 入力だけは表示値から raw 値へ変換します。

## PLC アドレス一括割当

`ツール` -> `PLCアドレス一括割当` で、Modbus 種別ごとに PLC アドレスを連番割当します。

設定項目:

| 項目 | 内容 |
|---|---|
| 対象 Modbus 種別 | Coil / DiscreteInput / HoldingRegister / InputRegister |
| PLC デバイス | プロトコルと Modbus 種別に応じた候補 |
| 開始番号 | 開始アドレスの番号部分 |
| 増分 | 1行ごとの増分 |

候補:

| プロトコル | Modbus 種別 | PLC デバイス |
|---|---|---|
| SLMP | Coil / DI | `X`, `Y`, `M`, `L`, `B` |
| SLMP | HR / IR | `D`, `W`, `R`, `ZR` |
| Host Link | Coil / DI | `R`, `B`, `MR`, `LR`, `X`, `Y`, `M`, `L` |
| Host Link | HR / IR | `DM`, `EM`, `FM`, `ZF`, `W`, `D`, `E`, `F` |

増分ルール:

| 例 | 結果 |
|---|---|
| SLMP `B`, 開始 `F`, 増分 `1` | `B F` -> `B10` |
| SLMP iQ-F `X`, 開始 `7`, 増分 `1` | `X7` -> `X10` |
| Host Link `R`, 開始 `015`, 増分 `1` | `R015` -> `R100` |
| Host Link `X`, 開始 `0F`, 増分 `1` | `X0F` -> `X10` |

## 対応 PLC デバイス

### SLMP

一括割当で選べるデバイス:

| 用途 | デバイス |
|---|---|
| Coil / DI | `X`, `Y`, `M`, `L`, `B` |
| HR / IR | `D`, `W`, `R`, `ZR` |

ライブラリ上は `SM`, `SD`, `SB`, `SW`, `F`, `V`, `TS`, `TC`, `TN`, `CS`, `CC`, `CN`, `LZ`, `RD` なども扱えますが、gateway の一括割当候補は上表に絞っています。

### Host Link

一括割当で選べるデバイス:

| 用途 | デバイス |
|---|---|
| Coil / DI | `R`, `B`, `MR`, `LR`, `X`, `Y`, `M`, `L` |
| HR / IR | `DM`, `EM`, `FM`, `ZF`, `W`, `D`, `E`, `F` |

Host Link の実際の範囲は、PLC 通信設定で選んだ機種に依存します。

## CSV インポート

`ファイル` -> `Factory I/O タグ CSV インポート` で Factory I/O のタグ CSV を読み込みます。

認識する列名:

| 内容 | 列名 |
|---|---|
| タグ名 | `Name`, `Tag`, `Tag Name`, `タグ名` |
| 種別 | `Type`, `I/O Type`, `Tag Type`, `種別` |
| データ型 | `Data Type`, `DataType`, `Datatype`, `データ型` |
| Modbus アドレス | `Address`, `Modbus Address`, `アドレス` |

対応する Address:

| CSV Address | Modbus 種別 |
|---|---|
| `Coil N` | Coil |
| `Input N` | Discrete Input |
| `Holding Reg N` | Holding Register |
| `Input Reg N` | Input Register |

対応する Data Type:

| CSV Data Type | 表示型 |
|---|---|
| `Bool` | Bool |
| `Int`, `Int16`, `Integer` | Int |
| `Real`, `Float` | Float |

インポート時の動作:

- 追加、更新、スキップをプレビュー表示します。
- 既存行の PLC アドレスは上書きしません。
- 既存行はコメントと表示型だけ更新します。
- CSV にない既存行は削除しません。
- 新規追加された行の PLC アドレスは空です。

## 設定ファイル

設定は JSON で保存します。

既定保存先:

```text
%APPDATA%\FactoryIOGateway\settings.json
```

メニュー:

| メニュー | 内容 |
|---|---|
| 設定読み込み... | JSON 設定を読み込みます |
| 上書き保存 | 現在の保存先へ保存します |
| 名前を付けて保存... | 保存先を指定して保存します |

稼働中は設定読み込みできません。停止してから読み込んでください。

設定例:

```json
{
  "plc": {
    "protocol": "SLMP",
    "host": "192.168.250.100",
    "port": 1025,
    "timeoutSec": 3,
    "pollingMs": 100,
    "slmpProfile": "melsec:iq-r",
    "hostLinkProfile": "keyence:kv-8000"
  },
  "modbus": {
    "listenIp": "127.0.0.1",
    "port": 502,
    "unitId": 1,
    "realScale": 100,
    "maxCoilAddress": 15,
    "maxDiscreteInputAddress": 15,
    "maxHoldingRegisterAddress": 15,
    "maxInputRegisterAddress": 15
  },
  "realScale": 100,
  "mappings": [
    {
      "modbusType": "HoldingRegister",
      "modbusAddress": 0,
      "plcAddress": "D10",
      "direction": "ToPlc",
      "displayType": "ScaledReal",
      "comment": "FACTORY I/O (Time Scale)"
    }
  ]
}
```

`direction` は互換のため JSON に残りますが、読み込み時は Modbus 種別から固定方向に正規化されます。

## ログ

`ツール` -> `ログ表示` でログウィンドウを開きます。

ログウィンドウ:

- 下部リスト形式で通信ログとエラーを表示します。
- 各行の `コピー` で1行コピーできます。
- `全コピー` で全ログをコピーできます。
- `ログクリア` で表示ログと `gateway.log` をクリアします。

ログファイル:

```text
%APPDATA%\FactoryIOGateway\gateway.log
%APPDATA%\FactoryIOGateway\error.log
```

`gateway.log` は通信ログ、`error.log` は未処理例外などのアプリ例外です。

## トラブルシュート

### Factory I/O から接続できない

- Modbus 待受 IP とポートを確認してください。
- 別 PC から接続する場合、`127.0.0.1` では接続できません。
- Windows ファイアウォールで TCP ポートを許可してください。
- ポート `502` は管理者権限や既存サービスの影響を受けることがあります。必要なら別ポートに変更してください。

### PLC に書けない

- PLC アドレスが空でないか確認してください。
- Modbus 種別の方向を確認してください。PLC へ書くのは Coil / Holding Register です。
- レジスタは raw の signed 16 bit 値として書きます。
- ログ表示で `PLC WRITE` と `PLC CHECK` を確認してください。

### Float 表示が期待と違う

- `Scale` を確認してください。
- `Scale=100` の場合、raw `1000` は `10.00` 表示です。
- PLC と Modbus に流れる値は raw 整数です。表示だけ Scale で割ります。

### Host Link のアドレスがずれる

- `R`, `MR`, `LR`, `CR` は KEYENCE ビットバンク表記です。
- 下2桁は `00..15` です。
- 例: `R015` の次は `R100` です。
- `X/Y` は末尾1桁が `0..F` のビットです。

## 関連ファイル

| ファイル | 内容 |
|---|---|
| `compile.bat` | 1ファイル exe を作成 |
| `start.bat` | 作成済み exe を起動 |
| `monitor-mock.html` | Monitor 画面の見た目サンプル |
| `DESIGN.md` | 現行設計メモ |

