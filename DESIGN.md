# Factory I/O Gateway Design Notes

このファイルは実装者向けの現行設計メモです。利用者向け手順は `README.md` に記載します。

## 目的

Factory I/O の `Modbus TCP/IP Client` と実 PLC を、SLMP または KEYENCE Host Link で接続する。

gateway は Modbus TCP サーバーとして待ち受け、Modbus データストアと PLC デバイスを同期する。

## 依存関係

NuGet ではなく、ローカルのプロジェクト参照を前提にする。

```text
..\NModbus\NModbus\NModbus.csproj
..\plc-comm-slmp-dotnet\src\PlcComm.Slmp\PlcComm.Slmp.csproj
..\plc-comm-hostlink-dotnet\src\PlcComm.KvHostLink\PlcComm.KvHostLink.csproj
```

不要な通信フォールバックは実装しない。PLC ライブラリのエラーは握りつぶさず、ログに出す。

## 通信方向

方向は Modbus 種別で固定する。

| Modbus 種別 | Direction | PLC 書込 | Modbus 書込 |
|---|---|---:|---:|
| Coil | `ToPlc` | yes | Factory I/O が書く |
| HoldingRegister | `ToPlc` | yes | Factory I/O が書く |
| DiscreteInput | `FromPlc` | no | gateway が書く |
| InputRegister | `FromPlc` | no | gateway が書く |

`MappingEntry.Direction` は JSON 互換のため残っているが、保存・読み込み時は `MappingEntry.GetDefaultDirection(ModbusType)` で正規化する。

## PLC 接続

### SLMP

`PlcSettings.SlmpProfile` はカノニカル名で保存する。

接続時は `SlmpConnectionOptions(settings.Host, profile)` を作成し、Port と Timeout を設定する。

SLMP の追加ルーティング項目は gateway では持たない。

### Host Link

`PlcSettings.HostLinkProfile` はカノニカル名で保存する。

接続時は `KvHostLinkConnectionOptions` に `PlcProfile:` を明示して渡す。Host Link ライブラリ側で空プロファイルと未対応プロファイルを検証する。

## レジスタ値

通信上のレジスタ値は 16 bit raw 整数として扱う。

- PLC へ書く値は Modbus HR から来た raw 整数をそのまま書く。
- Modbus へ書く値は PLC から来た raw 整数をそのまま書く。
- 表示だけ `Scale` で割る。
- Force 入力だけ表示値から raw へ変換する。

例:

```text
Scale 100, raw 1000 => 10.00
Scale 10,  raw 1000 => 100.00
```

signed 16 bit として扱うため、表示や PLC 書込時は `short` へ変換する。内部 raw は `ushort` 相当の `int` として保持する。

## Force

Force は Monitor 画面でのみ操作する。

| Force | 対象 |
|---|---|
| FORCE X | Coil / HoldingRegister |
| FORCE Y | DiscreteInput / InputRegister |

Force 有効化時は現在値を `ForceValue` に引き継ぐ。

`FromPlc` 側の Force は PLC に書かず、Modbus データストアへ返す値を強制する。

## 一括割当

一括割当は `PlcAddressSequence` でアドレスを生成する。

SLMP:

- `B`, `W`, `X`, `Y` は16進。ただし `melsec:iq-f` の `X/Y` は8進。
- `D`, `M`, `L`, `R`, `ZR` は10進。

Host Link:

- `R`, `MR`, `LR`, `CR` は KEYENCE ビットバンク表記。下2桁は `00..15`。
- `X/Y` は KEYENCE XYM 表記。末尾1桁が `0..F` のビット。
- `B/W` は16進。
- `DM/EM/FM/ZF/D/E/F/M/L` は10進。

## ログ

通常ログ:

```text
%APPDATA%\FactoryIOGateway\gateway.log
```

未処理例外ログ:

```text
%APPDATA%\FactoryIOGateway\error.log
```

ログウィンドウは `MainViewModel.ErrorLogs` を表示する。行コピー、全コピー、ログクリアを提供する。

## ビルド

開発ビルド:

```bat
dotnet build GatewayApp.sln --no-restore
```

1ファイル exe:

```bat
compile.bat
```

`compile.bat` は publish 出力を `GatewayApp.exe` のみに整理する。
