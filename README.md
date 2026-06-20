# Factory I/O Gateway

Factory I/O と実 PLC を Modbus TCP ↔ SLMP / Host Link でつなぐ Windows デスクトップアプリ。

---

## 通信アーキテクチャ

```
Factory I/O
  └─(Modbus TCP Client)──► Gateway (本アプリ / Modbus TCP Slave)
                                └─(SLMP or Host Link)──► PLC
```

- Gateway は Modbus TCP **スレーブ**として待ち受ける。
- Factory I/O がマスターとして Coil・DiscreteInput・HoldingRegister・InputRegister を読み書きする。
- Gateway はポーリングループで PLC と読み書きし、Modbus データストアへ反映する。

---

## 採用技術

| 層 | 選定 | 理由 |
|---|---|---|
| UI フレームワーク | **WPF (.NET 8)** | Windows デスクトップ専用、DataGrid・カスタムコントロール・ダークテーマが成熟。リアルタイム更新も INotifyPropertyChanged で自然に書ける |
| MVVM | **CommunityToolkit.Mvvm** | ソースジェネレータで ViewModel のボイラープレートを削減 |
| Modbus TCP | **NModbus4** (NuGet) | Slave (SlaveNetwork) として動作させる実績あり |
| SLMP | 独自実装 (TCP バイナリフレーム) | iQ-R / iQ-F / Q / L 対応。フレーム組み立ては `SlmpClient.cs` に集約 |
| Host Link | 独自実装 (シリアル ASCII) | Omron C-mode コマンド (FINS 非対応) |
| JSON 設定 | **System.Text.Json** | 追加 NuGet 不要 |
| CSV | `CsvHelper` (NuGet) | Factory I/O タグ CSV のパース |

### LED インジケータ

WPF の `Rectangle` + `Style.Triggers` で実装する。  
状態 → 色のマッピングは `LedState` enum (Off / On / ForceOn / ForceOff / Error) で管理し、`IValueConverter` で `SolidColorBrush` に変換する。

---

## プロジェクト構成

```
GatewayApp.sln
└── GatewayApp/                         # WPF (.NET 8)
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml                 # メニューバー + TabControl(Monitor/Mapping)
    ├── Views/
    │   ├── MonitorView.xaml            # Coil/Input/HR/IR リアルタイム表示
    │   ├── MappingView.xaml            # DataGrid 行編集
    │   └── Dialogs/
    │       ├── PlcSettingsWindow.xaml
    │       ├── ModbusSettingsWindow.xaml
    │       ├── CsvImportPreviewWindow.xaml
    │       └── BulkAssignWindow.xaml
    ├── ViewModels/
    │   ├── MainViewModel.cs
    │   ├── MonitorViewModel.cs
    │   ├── MappingViewModel.cs
    │   ├── PlcSettingsViewModel.cs
    │   └── ModbusSettingsViewModel.cs
    ├── Models/
    │   ├── MappingEntry.cs
    │   ├── AppSettings.cs
    │   ├── PlcSettings.cs
    │   └── ModbusSettings.cs
    ├── Services/
    │   ├── GatewayService.cs           # ポーリングループ / 起動・停止
    │   ├── ModbusSlave.cs              # NModbus4 ラッパー
    │   ├── SlmpClient.cs               # SLMP TCP フレーム
    │   ├── HostLinkClient.cs           # Host Link シリアル
    │   └── SettingsService.cs          # JSON 保存・読込
    └── Converters/
        ├── LedStateConverter.cs
        └── ScaledRealConverter.cs
```

---

## データモデル

### MappingEntry

```csharp
public enum ModbusType { Coil, DiscreteInput, HoldingRegister, InputRegister }
public enum DataDirection { ToPlc, FromPlc }
public enum DisplayType { Bool, Int16, ScaledReal }
public enum LedState { Off, On, ForceOn, ForceOff, Error }

public class MappingEntry : ObservableObject
{
    public ModbusType  ModbusType    { get; init; }   // 読み取り専用
    public int         ModbusAddress { get; init; }   // 読み取り専用
    public string      PlcAddress    { get; set; }    // 例: "M0", "D10"
    public DataDirection Direction   { get; set; }
    public DisplayType DisplayType   { get; set; }
    public string      Comment       { get; set; }

    // ランタイム
    public int         RawValue      { get; set; }    // 0/1 or 0–65535
    public LedState    LedState      { get; set; }
    public int?        ForceValue    { get; set; }    // null = 強制なし
    public DateTime?   LastWritten   { get; set; }
}
```

### AppSettings (JSON 保存対象)

```csharp
public class AppSettings
{
    public PlcSettings      Plc          { get; set; }
    public ModbusSettings   Modbus       { get; set; }
    public int              RealScale    { get; set; } = 100;
    public List<MappingEntry> Mappings   { get; set; }
}
```

### PlcSettings

```csharp
public class PlcSettings
{
    public string   Protocol        { get; set; }  // "SLMP" | "HostLink"
    public string   Host            { get; set; }
    public int      Port            { get; set; }
    public int      TimeoutSec      { get; set; } = 3;
    public int      PollingMs       { get; set; } = 100;
    public string   SlmpProfile     { get; set; }  // "iQ-R" | "iQ-F" | "Q" | "L"
}
```

### ModbusSettings

```csharp
public class ModbusSettings
{
    public string   ListenIp  { get; set; } = "127.0.0.1";
    public int      Port      { get; set; } = 502;
    public byte     UnitId    { get; set; } = 1;
    public int      RealScale { get; set; } = 100;
}
```

---

## Modbus アドレス割り当て

各種別ごとに 0 始まりの連番。内部バッファは各 512 点確保する。  
実際に使用するのは CSV インポートで登録された点数のみ。

| Modbus 種別 | Factory I/O 側 | 方向 |
|---|---|---|
| Coil (0–511) | Type=Input タグ (シミュレーションが受け取る値) | PLC → Gateway → Factory I/O |
| DiscreteInput (0–511) | Type=Output タグ (シミュレーションが送る値) | Factory I/O → Gateway → PLC |
| HoldingRegister (0–511) | Type=Input Real/Int タグ | PLC → Gateway → Factory I/O |
| InputRegister (0–511) | Type=Output Real/Int タグ | Factory I/O → Gateway → PLC |

---

## PLC アドレス割り当てルール

- 各 Modbus 種別ごとに **先頭アドレス 1 つ**を設定すると、以降は連番で自動割当。  
  例: Coil 先頭 `M0` → Coil 0=M0, Coil 1=M1, Coil 2=M2 …
- 手動で上書き可能（マッピング編集画面）。
- PLCアドレスが空欄の場合は PLC 書込をスキップする。

---

## 画面仕様

### デザイン方針

- モニタ画面: **暗いグレー背景**、白文字、色付き LED
- 設定・編集画面: **明るいグレー背景**（#ebebeb）、黒文字
- カラー LED 定義:

| 状態 | 色 |
|---|---|
| ON (自然) | 緑 `#3edc3e` |
| OFF | 暗灰 `#252525` |
| 強制 ON | オレンジ `#ff8c00` |
| 強制 OFF | 暗灰 + オレンジ外枠 |
| 非ゼロ値 | シアン `#00bcd4` |
| エラー | 赤 `#d44` |

### モニタ画面

UIのリファレンス: `monitor-mock.html`（同リポジトリ）

8カラムグリッド構成:

```
[SENSORコメント] [PLC] [Coil N] [LED] [LED] [Input N] [PLC] [ACTUATORコメント]
[SENSORコメント] [PLC] [HR N]   [値]  [値]  [IR N]    [PLC] [ACTUATORコメント]
```

- SENSORS 側 (左): Coil / HoldingRegister → PLC から書き込む値
- ACTUATORS 側 (右): DiscreteInput / InputRegister → Factory I/O から受け取る値
- FORCE スイッチ (ステータスバー): 有効時のみ強制操作が可能

#### FORCE 操作

- **Bool (LED クリック)**: 強制なし → 強制 ON (オレンジ) → 強制 OFF → 強制なし のサイクル
- **Register (値セルクリック)**: インラインテキストボックスで入力、Enter 確定 / Esc でクリア
- FORCE スイッチ OFF 時は強制値を保持したまま無効化（再 ON で復活）
- 強制はモニタ画面・マッピング画面の両方から行・セル単位で操作できる

### マッピング編集画面

DataGrid (行編集モード)。列構成:

| 列 | 編集 | 備考 |
|---|---|---|
| Modbus種別 | × | |
| Modbusアドレス | × | |
| PLCアドレス | ○ | 稼働中も編集可 |
| 方向 | ○ | ToPlc / FromPlc |
| 表示型 | ○ | Bool / Int16 / ScaledReal |
| コメント | ○ | |
| 現在値 | — | リアルタイム表示 |
| 強制操作 | ○ | Bool: ON/OFF ボタン、Register: 値入力 + 書込 |
| 最終書込時刻 | — | |

### PLC 通信設定ウィンドウ

稼働中は全フィールド disabled + 警告バナー表示。

| 項目 | 型 | 備考 |
|---|---|---|
| プロトコル | RadioButton | SLMP / Host Link |
| ホスト | TextBox | |
| ポート | IntegerBox | |
| タイムアウト (秒) | IntegerBox | |
| ポーリング (ms) | IntegerBox | |
| PLCプロファイル | ComboBox | SLMP 選択時のみ表示: iQ-R / iQ-F / Q Series / L Series |

### Modbus 通信設定ウィンドウ

稼働中は全フィールド disabled + 警告バナー表示。

| 項目 | 型 |
|---|---|
| 待受 IP | TextBox |
| ポート | IntegerBox |
| ユニット ID | IntegerBox (1–247) |
| Real 表示倍率 | IntegerBox |

---

## CSV インポート

### 対象フォーマット

```csv
Name,Type,Data Type,Address
Auto,Input,Bool,Coil 0
Box conveyor,Output,Bool,Input 0
FACTORY I/O (Time Scale),Input,Real,Holding Reg 0
Counter,Output,Int,Input Reg 0
```

### Address 変換

| CSV Address | ModbusType |
|---|---|
| `Coil N` | Coil |
| `Input N` | DiscreteInput |
| `Holding Reg N` | HoldingRegister |
| `Input Reg N` | InputRegister |

### Data Type 変換

| Data Type | DisplayType |
|---|---|
| Bool | Bool |
| Int | Int16 |
| Real | ScaledReal |

### 取込ルール

1. インポート前にプレビューダイアログを表示（追加行・更新行・スキップ行を色分け）。
2. 既存行がある場合: PLCアドレスは**保持**、コメントと表示型は更新する。
3. 既存にない行は追加（PLCアドレスは空欄）。
4. CSV にない既存行は削除しない。

---

## レジスタ表示・入力

### ScaledReal

- 内部値 (Int16 raw) ÷ Real倍率 = 表示値
- 例: 倍率 100、raw 100 → 表示 `1.00`
- 入力時は逆変換: 入力値 × 倍率 = raw 書込値
- Int16 範囲 (–32768〜32767) 超はエラー表示

### Int16

- raw 値をそのまま符号付き整数として表示

---

## 強制書込

- Bool 行: Force ON / Force OFF
- Register 行: 値入力 → 書込
- PLC と Modbus データストアの**両方**に即時反映
- PLCアドレスが空欄の場合は Modbus データストアのみ更新

---

## 設定 JSON

保存パス: `%APPDATA%\FactoryIOGateway\settings.json`

```json
{
  "plc": {
    "protocol": "SLMP",
    "host": "192.168.250.100",
    "port": 1025,
    "timeoutSec": 3,
    "pollingMs": 100,
    "slmpProfile": "iQ-R"
  },
  "modbus": {
    "listenIp": "127.0.0.1",
    "port": 502,
    "unitId": 1,
    "realScale": 100
  },
  "mappings": [
    {
      "modbusType": "Coil",
      "modbusAddress": 0,
      "plcAddress": "M0",
      "direction": "ToPlc",
      "displayType": "Bool",
      "comment": "Auto"
    }
  ]
}
```

---

## 稼働中の変更ルール

| 項目 | 稼働中 | 停止中 |
|---|---|---|
| PLCアドレス | 変更可 | 変更可 |
| 方向 | 変更可 | 変更可 |
| コメント | 変更可 | 変更可 |
| 表示型 | 変更可 | 変更可 |
| 強制書込 | 可 | 可 |
| PLC通信設定 | **不可** | 変更可 |
| Modbus通信設定 | **不可** | 変更可 |

---

## メニュー構成

```
ファイル
  ├── Factory I/O タグ CSV インポート
  ├── 設定保存
  └── 終了
通信
  ├── PLC通信設定
  └── Modbus通信設定
ツール
  └── PLCアドレス一括割当
        ├── 対象 Modbus 種別
        ├── PLC デバイス接頭辞
        ├── 開始番号
        └── 増分
```

---

## 受け入れ条件

- [ ] Factory I/O タグ CSV を読み込める
- [ ] CSV インポート前に追加 / 更新のプレビューが出る
- [ ] 既存 PLCアドレスを CSV インポートで上書きしない
- [ ] モニタ画面が `monitor-mock.html` と同等のレイアウトになる（暗灰背景・白文字・8列グリッド）
- [ ] SENSORS (Coil/HR) と ACTUATORS (Input/IR) の列が正しく対応している
- [ ] Bool 行を Force ON / Force OFF できる（オレンジ LED）
- [ ] Register 行を Int16 / ScaledReal として入力・強制書込できる
- [ ] Real倍率 `100` で raw `100` が `1.00` と表示される
- [ ] 入力 `1.00` が raw `100` として書き込まれる
- [ ] FORCE スイッチ OFF 時は強制値を保持したまま無効化される
- [ ] ゲートウェイ稼働中は PLC / Modbus 通信設定を変更できない
- [ ] 設定が JSON で保存・復元できる
- [ ] `dotnet build GatewayApp.sln` が成功する

---

## ビルド

```
dotnet build GatewayApp.sln
dotnet run --project GatewayApp
```

.NET 8 SDK 必須。
