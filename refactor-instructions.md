# Refactor TODO

> 人間がこのファイルを実装担当モデルに渡すときのコマンド例:
> `/goal refactor-instructions.md のTODOを順に完了しろ`

---

## Goal

- [ ] 既存仕様を一切壊さず、特定の技術的負債だけを小さく解消する。
- [ ] 見た目の整頓、大規模な設計変更、承認のない削除は行わない。
- [ ] 各フェーズは独立して完結させ、フェーズごとにビルドとスモークテストで検証する。
- [ ] 既存の未コミット変更と自分の変更を混ぜない。

---

## Project Context

- [ ] WPF (.NET 9) デスクトップアプリである。
- [ ] Factory I/O の Modbus TCP/IP Client と実 PLC をつなぐゲートウェイである。
- [ ] Factory I/O 側は Modbus TCP スレーブとして待ち受ける。
- [ ] PLC 側は SLMP (MELSEC) または KEYENCE Host Link でポーリング通信する。
- [ ] Coil / Holding Register は Factory I/O から PLC へ流れる。
- [ ] Discrete Input / Input Register は PLC から Factory I/O へ流れる。

### Main Entry Points

- [ ] `GatewayApp/App.xaml.cs`: WPF Application 起動、グローバル例外ハンドラ。
- [ ] `GatewayApp/MainWindow.xaml(.cs)`: メインウィンドウ、UI イベントハンドラ。
- [ ] `GatewayApp.SmokeTests/Program.cs`: STA スレッドで WPF ウィンドウを起動するヘッドレススモークテスト。

### Main Modules

- [ ] `MainViewModel`: UI 状態、ポーリングループ、Force 管理、設定 I/O、ログファイル書き込み。
- [ ] `GatewayService`: Modbus と PLC の統合オーケストレーション、`SemaphoreSlim` による排他制御。
- [ ] `ModbusSlaveService`: NModbus TCP スレーブの起動/停止、DataStore 読み書き。
- [ ] `PlcClientService`: SLMP / HostLink 接続と読み書き、プロトコル分岐。
- [ ] `CsvImportService`: Factory I/O タグ CSV のプレビューと適用。
- [ ] `SettingsService`: JSON 設定の読み書き。
- [ ] `PlcAddressSequence`: PLC アドレスの連番フォーマット。
- [ ] `CommunicationExceptionClassifier`: 停止時の期待例外を分類。
- [ ] `MappingEntry`: Modbus アドレスと PLC アドレスのマッピング。
- [ ] `AppSettings` / `PlcSettings` / `ModbusSettings`: 保存用 POCO モデル。

---

## Non-Negotiables

- [ ] `dotnet build GatewayApp/GatewayApp.csproj --no-restore` を 0 warnings / 0 errors に保つ。
- [ ] `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore` を 0 warnings / 0 errors に保つ。
- [ ] `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj` を `Smoke tests passed.` で成功させる。
- [ ] 既存 JSON 設定との後方互換性を壊さない。
- [ ] `MappingEntrySettings` に `Direction` プロパティを追加しない。
- [ ] `PlcSettings.Normalize()` が `PlcSettings` 自身を変更する副作用を維持する。
- [ ] `App.xaml.cs` のグローバル例外ハンドラが `CommunicationExceptionClassifier` を使う構造を維持する。
- [ ] エラーメッセージ、ログメッセージ、スモークテスト対象文字列を不用意に変更しない。

---

## Stop And Ask

- [ ] ビルドエラーが発生し、自力で安全に直せない場合は止まって確認する。
- [ ] スモークテストが失敗し、原因が自分の変更と無関係に見える場合は止まって確認する。
- [ ] 1つの変更が3ファイル以上を同時に修正する必要が出た場合は止まって確認する。
- [ ] 例外: Phase 5 は既定で4ファイルを変更するため、この停止条件の対象外とする。
- [ ] `AppSettings` / `MappingEntrySettings` / `PlcSettings` / `ModbusSettings` の保存済みプロパティを変更・削除する必要が出た場合は止まって確認する。
- [ ] Phase 6 の提案項目を実装したくなった場合は止まって確認する。

---

## Behaviors To Preserve

- [ ] `ReportException` で `ErrorLogs` に追加される。
- [ ] `IsRunning=false` 時の `SocketException(OperationAborted)` はサイレント抑制される。
- [ ] Force X 有効化で Coil / Holding Register の現在値が `ForceValue` に引き継がれる。
- [ ] Force X は Input Register に影響しない。
- [ ] Coil Force は `0 -> 1 -> 0` でトグルする。
- [ ] HR Force は Int16 直接入力できる。
- [ ] Force Y 有効化で IR の現在値が `ForceValue` に引き継がれる。
- [ ] ScaledReal の Force 入力は `scale=100` で `"1.25" -> raw=125` になる。
- [ ] Scale 変更後の Force 入力は `scale=10` で `"10.00" -> raw=100` になる。
- [ ] 無効入力時に `LastError` が空でない。
- [ ] Modbus TCP の起動/停止でポート bind/unbind ができる。
- [ ] 接続なしで `MainWindow` を閉じられる。
- [ ] SLMP プロファイル文字列は `"iQ-L" -> "melsec:iq-l"` に正規化される。
- [ ] HostLink プロファイルは canonical value と人間ラベルを併用する。
- [ ] Transport は `"udp" -> "UDP"` に正規化される。
- [ ] PLC 接続失敗メッセージは接続先情報と原因を含む。
- [ ] プロトコル切り替え時にデフォルトポートが自動適用される。
- [ ] SLMP / HostLink それぞれの Bulk Assign デバイスリストを維持する。
- [ ] PLC アドレス連番は 16進 / 8進 / BCD / bit-bank / XYM-bit を維持する。
- [ ] 通常値ブラシは `#4ee072`、Force 値ブラシは `#ff8c00` を維持する。
- [ ] ForceOff LED ブラシは `#5a3a00` を維持する。
- [ ] `MappingEntrySettings` に `Direction` プロパティがない状態を維持する。
- [ ] Mapping 追加で `IsDirty=true` かつ `WindowTitle` が `" *"` で終わる。
- [ ] 最大アドレス超過マッピングの警告カウントを維持する。
- [ ] CSV で `DataType` 空行のスキップ理由は `"DataType 未設定"` のままにする。
- [ ] `LogWindow` はログ追加後 `ErrorLogs.Count > 0` になる。
- [ ] `LogWindow` は `ClearLogs` 後 `ErrorLogs.Count == 0` になる。
- [ ] `AboutWindow` のバージョンテキストは `"Version:"` で始まる。
- [ ] `AboutWindow` の `LibrariesListView` は5件以上表示する。
- [ ] ScaledReal 表示は `scale=100, raw=1000 -> "10.00"` を維持する。
- [ ] `FormatRawWithDisplay` の各フォーマットを維持する。
- [ ] `RegisterIntegerToolTip` のフォーマットを維持する。

---

## Phase 0: Baseline

- [ ] `git status --short` を実行し、既存の未コミット変更を記録する。
- [ ] 既存の未コミット変更がある場合、自分の変更と混ぜない前提で進める。
- [ ] `dotnet build GatewayApp/GatewayApp.csproj --no-restore` を実行する。
- [ ] アプリ本体ビルドが 0 warnings / 0 errors であることを確認する。
- [ ] `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore` を実行する。
- [ ] スモークテストプロジェクトビルドが 0 warnings / 0 errors であることを確認する。
- [ ] `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj` を実行する。
- [ ] `Smoke tests passed.` と exit code 0 を確認する。
- [ ] ここで失敗した場合は実装せず、結果を報告する。

---

## Phase 1: D1 - `ParseSlmpProfile` の死コード削除

### Target

- [ ] `GatewayApp/Services/PlcClientService.cs`

### TODO

- [ ] `PlcClientService.ParseSlmpProfile` を確認する。
- [ ] `"iQ-R"` / `"iQ-F"` / `"Q Series"` / `"L Series"` の分岐を削除する。
- [ ] 実装を `SlmpPlcProfiles.Parse(text.Trim())` のみにする。
- [ ] `PlcSettings.Clone()` / `Normalize()` が接続前に canonical 形式へ正規化する前提を崩さない。

### Verification

- [ ] `dotnet build GatewayApp/GatewayApp.csproj --no-restore`
- [ ] `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore`
- [ ] `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj`
- [ ] 成功結果を記録する。

---

## Phase 2: D2 - CSV プレビューの一時 `MappingEntry` 生成をなくす

### Target

- [ ] `GatewayApp/Models/MappingEntry.cs`
- [ ] `GatewayApp/Models/CsvImportPreviewItem.cs`

### TODO

- [ ] `MappingEntry.ModbusLabel` のロジックを `public static string FormatModbusLabel(ModbusType type, int address)` に抽出する。
- [ ] `MappingEntry.ModbusLabel` は `FormatModbusLabel(ModbusType, ModbusAddress)` を呼ぶ形にする。
- [ ] `CsvImportPreviewItem.ModbusLabel` は `new MappingEntry(...)` ではなく `MappingEntry.FormatModbusLabel(...)` を使う。
- [ ] ラベル文字列を変更しない。

### Expected Format

```csharp
public static string FormatModbusLabel(ModbusType type, int address)
{
    return type switch
    {
        ModbusType.Coil => $"Coil {address}",
        ModbusType.DiscreteInput => $"Input {address}",
        ModbusType.HoldingRegister => $"HR {address}",
        ModbusType.InputRegister => $"IR {address}",
        _ => address.ToString(CultureInfo.InvariantCulture),
    };
}
```

### Verification

- [ ] `dotnet build GatewayApp/GatewayApp.csproj --no-restore`
- [ ] `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore`
- [ ] `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj`
- [ ] 成功結果を記録する。

---

## Phase 3: D3 - `TryParseDisplayType` の冗長チェック除去

### Target

- [ ] `GatewayApp/Services/CsvImportService.cs`

### TODO

- [ ] `CsvImportService.TryParseDisplayType` を確認する。
- [ ] Register 用の `switch` と直後の `if` チェーンを1つの `switch` 文へ統合する。
- [ ] 空文字の場合は `error = "DataType 未設定"`、`false` を維持する。
- [ ] Bool 系 Modbus の場合は `"Bool"` のみ成功にする。
- [ ] Bool 系 Modbus の不正値は `error = "Coil/Input は Bool のみ対応"` を維持する。
- [ ] Register 系 Modbus の場合は `"Int"` / `"Int16"` / `"Integer"` / `"Real"` / `"Float"` のみ成功にする。
- [ ] Register 系 Modbus の不正値は `error = "Holding/Input Register は Int または Float のみ対応"` を維持する。
- [ ] エラーメッセージ文字列を変更しない。

### Verification

- [ ] `dotnet build GatewayApp/GatewayApp.csproj --no-restore`
- [ ] `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore`
- [ ] `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj`
- [ ] CSV 空文字スキップ理由が `"DataType 未設定"` のまま通ることを確認する。
- [ ] 成功結果を記録する。

---

## Phase 4: D4 - クリップボードリトライの報告タイミング修正

### Target

- [ ] `GatewayApp/Views/Dialogs/LogWindow.xaml.cs`

### TODO

- [ ] `LogWindow.CopyToClipboardAsync` を確認する。
- [ ] リトライ途中の `attempt == 3` で `ReportException` しないようにする。
- [ ] 最後の attempt が失敗したときだけ `ReportException` する。
- [ ] 途中失敗後に最終 attempt が成功した場合、エラーログが残らないようにする。
- [ ] 既存のコピー成功時の挙動は変えない。

### Preferred Shape

```csharp
private async Task CopyToClipboardAsync(string text)
{
    for (var attempt = 0; attempt < 5; attempt++)
    {
        try
        {
            Clipboard.SetText(text);
            return;
        }
        catch (Exception ex) when (attempt < 4)
        {
            await Task.Delay(80);
        }
        catch (Exception ex)
        {
            _viewModel.ReportException(ex);
            return;
        }
    }
}
```

### Verification

- [ ] `dotnet build GatewayApp/GatewayApp.csproj --no-restore`
- [ ] `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore`
- [ ] `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj`
- [ ] 成功結果を記録する。

---

## Phase 5: D8/D9 - `LastError` / `ErrorLogs` の名前を実態に合わせる

### Target

- [ ] `GatewayApp/ViewModels/MainViewModel.cs`
- [ ] `GatewayApp/Views/Dialogs/LogWindow.xaml`
- [ ] `GatewayApp/Views/Dialogs/LogWindow.xaml.cs`
- [ ] `GatewayApp.SmokeTests/Program.cs`

### TODO

- [ ] このフェーズは4ファイル変更が既定なので、Stop And Ask の「3ファイル以上」条件の例外として扱う。
- [ ] `MainViewModel.LastError` を `StatusMessage` にリネームする。
- [ ] `MainViewModel.ErrorLogs` を `Logs` にリネームする。
- [ ] 生成プロパティ名に合わせて backing field を `_statusMessage` に変更する。
- [ ] `SetStatus` / `ClearStatus` / `ReportError` / `ClearLogs` / ログファイル失敗処理内の参照を更新する。
- [ ] `LogWindow.xaml` の `ItemsSource="{Binding ErrorLogs}"` を `ItemsSource="{Binding Logs}"` に更新する。
- [ ] `LogWindow.xaml.cs` の `_viewModel.ErrorLogs` 参照を `_viewModel.Logs` に更新する。
- [ ] `GatewayApp.SmokeTests/Program.cs` の `viewModel.LastError` を `viewModel.StatusMessage` に更新する。
- [ ] `GatewayApp.SmokeTests/Program.cs` の `viewModel.ErrorLogs` を `viewModel.Logs` に更新する。
- [ ] 型、内容、順序、上限200件、ログファイル書き込み挙動を変更しない。
- [ ] JSON 保存モデルには手を触れない。

### Verification

- [ ] `dotnet build GatewayApp/GatewayApp.csproj --no-restore`
- [ ] `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore`
- [ ] `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj`
- [ ] 成功結果を記録する。

---

## Phase 6: Priority B Proposal Report Only

### Rule

- [ ] Phase 6 では実装しない。
- [ ] 提案レポートだけを出す。
- [ ] 実装したくなった場合は止まってユーザー確認する。

### D5 - `MainViewModel` のログファイル書き込み抽出

- [ ] 対象: `GatewayApp/ViewModels/MainViewModel.cs`
- [ ] 追加候補: `GatewayLogService` または `LogFileService`
- [ ] 実装コストをファイル数と行数で見積もる。
- [ ] `App.xaml.cs` の `RotateErrorLogIfNeeded` との統合可否を整理する。
- [ ] ユーザーに確認すべき質問を書く。

### D6 - `SetReadOnly` 重複の共通化

- [ ] 対象: `PlcSettingsWindow.xaml.cs`
- [ ] 対象: `ModbusSettingsWindow.xaml.cs`
- [ ] 共通 utility への抽出案を見積もる。
- [ ] `IsEnabled` で代替する案のリスクを整理する。
- [ ] ユーザーに確認すべき質問を書く。

### D7 - PLC プロファイル正規化テーブルの一元化

- [ ] 対象: `AppSettings.cs`
- [ ] 対象: `PlcClientService.cs`
- [ ] 対象: `PlcSettingsWindow.xaml.cs`
- [ ] SLMP と HostLink の両方でテーブル分散があることを明記する。
- [ ] 一元化の推奨順序を提案する。
- [ ] JSON 互換性への影響がないことを確認項目に入れる。
- [ ] ユーザーに確認すべき質問を書く。

### D10 - `SortMappings` の CollectionChanged 多発改善

- [ ] 対象: `MainViewModel.cs`
- [ ] `CollectionView.SortDescriptions` 案を整理する。
- [ ] `ObservableCollection.Move` 案を整理する。
- [ ] `_suppressDirty` と `MarkDirty()` への影響を整理する。
- [ ] 大量マッピング時以外の優先度は低いことを明記する。
- [ ] ユーザーに確認すべき質問を書く。

### Report Output

- [ ] 各項目の実装コスト見積もりを書く。
- [ ] 各項目のリスクを書く。
- [ ] 各項目のユーザー確認事項を書く。
- [ ] 実装する場合の推奨順序を書く。

---

## Out Of Scope

- [ ] XAML のレイアウト変更、スタイル変更、ビジュアルリファクタリングはしない。
- [ ] 新機能を追加しない。
- [ ] NuGet パッケージのバージョンを変更しない。
- [ ] `AppSettings` / `MappingEntrySettings` のプロパティ追加、削除、リネームをしない。
- [ ] `MainWindow.xaml` の構造を変更しない。
- [ ] `GatewayService` / `ModbusSlaveService` の通信ロジックを変更しない。
- [ ] `PlcAddressSequence` のアルゴリズムを変更しない。
- [ ] Phase 6 の項目を承認前に実装しない。
- [ ] エラーメッセージ、ログメッセージの文言を不用意に変更しない。
- [ ] `DataDirection` 列挙型を変更しない。
- [ ] ポーリングループを変更しない。
- [ ] Force 機能の動作を変更しない。

---

## Per-Phase Completion Report Template

```markdown
## Phase N 完了報告

### 変更したファイル
- `path/to/file`: 変更内容の要約

### 実行したコマンドと結果
- `dotnet build GatewayApp/GatewayApp.csproj --no-restore` -> 0 warnings, 0 errors
- `dotnet build GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj --no-restore` -> 0 warnings, 0 errors
- `dotnet run --project GatewayApp.SmokeTests/GatewayApp.SmokeTests.csproj` -> `Smoke tests passed.` (exit code 0)

### 次のフェーズ
- [進む / 問題があるため確認を求める]
```
