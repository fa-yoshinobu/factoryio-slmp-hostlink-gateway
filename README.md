# Factory I/O SLMP / Host Link Gateway

Factory I/O の `Modbus TCP/IP Client` と実 PLC をつなぐ gateway アプリです。

- Factory I/O からは Modbus TCP サーバーとして見えます。
- PLC へは `SLMP` または `KEYENCE Host Link` で接続します。
- Coil / HR は Factory I/O から PLC へ書き込みます。
- DI / IR は PLC から読んで Factory I/O へ返します。

## まず使う手順

1. `GatewayApp.exe` を起動します。
2. `通信` -> `PLC通信設定` で PLC の接続先を設定します。
3. `通信` -> `Modbus通信設定` で Factory I/O から接続する IP / Port / Unit ID を設定します。
4. Factory I/O からタグ CSV をエクスポートします。
5. `ファイル` -> `Factory I/O タグ CSV インポート` で CSV を読み込みます。
6. `Mapping` で PLC アドレスを入れます。
7. 必要なら `ツール` -> `PLCアドレス一括割当` で PLC アドレスをまとめて割り当てます。
8. `ファイル` -> `名前を付けて保存...` で設定を保存します。
9. Factory I/O 側のドライバを `Modbus TCP/IP Client` にして gateway へ接続します。
10. `起動` ボタンで通信を開始します。
11. `Monitor` で値とログを確認します。

起動直後は空の設定です。保存済み設定を使う場合は、先に `ファイル` -> `設定読み込み...` で読み込んでください。

## Factory I/O 側で合わせる値

Factory I/O の `Modbus TCP/IP Client` には、gateway の Modbus 通信設定と同じ値を入れます。

| Factory I/O | gateway |
|---|---|
| Host | gateway を起動している PC の IP |
| Port | Modbus 通信設定の Port |
| Unit ID | Modbus 通信設定の Unit ID |

初期値:

| 項目 | 値 |
|---|---|
| 待受 IP | `127.0.0.1` |
| Port | `502` |
| Unit ID | `1` |

別 PC の Factory I/O から接続する場合、待受 IP は `127.0.0.1` では接続できません。PC の LAN 側 IP、または `0.0.0.0` にしてください。Windows ファイアウォールで対象ポートの許可も必要です。

## Mapping の要点

PLC アドレスが空の行は通信しません。

| Modbus 種別 | 方向 | 表示型 |
|---|---|---|
| Coil | Factory I/O -> PLC | Bool 固定 |
| Holding Register | Factory I/O -> PLC | Int / Float |
| Discrete Input | PLC -> Factory I/O | Bool 固定 |
| Input Register | PLC -> Factory I/O | Int / Float |

方向は Modbus 種別で固定です。Mapping 画面では選べません。

`PLC(X)` は Factory I/O から PLC へ向かう側、`PLC(Y)` は PLC から Factory I/O へ返す側です。

## CSV インポート

Factory I/O のタグ CSV を読み込むと、Modbus アドレスとコメントを Mapping に反映します。

- 新規行は追加します。
- 既存行はコメントと表示型だけ更新します。
- 既存行の PLC アドレスは上書きしません。
- CSV にない既存行は削除しません。
- Data Type が空、または未対応の行はスキップします。

対応する Data Type:

| CSV | Mapping |
|---|---|
| `Bool` | Bool |
| `Int`, `Int16`, `Integer` | Int |
| `Real`, `Float` | Float |

## PLC アドレス一括割当

`PLCアドレス一括割当` は、選んだ Modbus 種別に対して PLC アドレスを連番で入れる機能です。

選べるデバイス:

| プロトコル | Coil / DI | HR / IR |
|---|---|---|
| SLMP | `X`, `Y`, `M`, `L`, `B` | `D`, `W`, `R`, `ZR` |
| Host Link | `R`, `B`, `MR`, `LR`, `X`, `Y`, `M`, `L` | `DM`, `EM`, `FM`, `ZF`, `W`, `D`, `E`, `F` |

注意:

- SLMP iQ-F の `X/Y` は 8進として増えます。
- Host Link の `R/MR/LR` は KEYENCE ビットバンク表記です。例: `R015` の次は `R100`。
- Host Link の `X/Y` は末尾1桁が `0..F` のビットです。

## Monitor と Force

`Monitor` は通信中に見る画面です。

- 通常値は緑です。
- Force 値はオレンジです。
- レジスタ値にカーソルを乗せると、生の整数値を確認できます。

Force:

| ボタン | 対象 | 動作 |
|---|---|---|
| FORCE X | Coil / HR | PLC へ書く値を固定 |
| FORCE Y | DI / IR | Factory I/O へ返す値を固定 |

Force を有効にした時点で、現在値を Force 値として引き継ぎます。

## Int / Float / Scale

PLC と Modbus に流れるレジスタ値は 16 bit 整数です。

- `Int` は符号付き 16 bit 整数として表示します。
- `Float` は整数値を `Scale` で割って表示します。
- PLC や Modbus に書く値は整数値のままです。
- Force 入力だけ、表示値から整数値へ変換します。

例:

| Scale | 整数値 | 表示 |
|---:|---:|---:|
| 100 | 1000 | 10.00 |
| 100 | -1000 | -10.00 |
| 10 | 1000 | 100.00 |

## ログ

エラーや通信確認は `ツール` -> `ログ表示` で確認します。

- 各行の `コピー` で1行コピーできます。
- `全コピー` で全ログをコピーできます。
- `ログクリア` で表示ログと `gateway.log` をクリアします。

ログファイル:

```text
%APPDATA%\FactoryIOGateway\gateway.log
%APPDATA%\FactoryIOGateway\error.log
```

## よく見るところ

Factory I/O から接続できない:

- Factory I/O の Host / Port / Unit ID が gateway と同じか確認してください。
- 別 PC から接続する場合、gateway の待受 IP が `127.0.0.1` では接続できません。
- Windows ファイアウォールで TCP ポートを許可してください。
- Port `502` が使えない場合は別ポートに変更してください。

PLC に書けない:

- 通信が起動しているか確認してください。
- PLC アドレスが空でないか確認してください。
- PLC へ書くのは Coil / Holding Register です。
- `ログ表示` の `PLC WRITE` / `PLC CHECK` を確認してください。

Float 表示が違う:

- `Scale` を確認してください。
- `Scale=100` の場合、整数値 `1000` は `10.00` 表示です。
- PLC と Modbus に流れる値は整数値です。表示だけ `Scale` で割ります。

## ライセンス

MIT License です。詳細は `LICENSE` を参照してください。
