# TODO — 不具合・改善リスト

調査日: 2026-06-20

---

## 不具合 (Bugs)

### [BUG-01] `LastError` がメイン画面に表示されない
- **重要度**: 高
- **方針**: これはやらない。エラーはメイン画面上部へ出さず、ログウィンドウで確認する方針とする。
- **場所**: `GatewayApp/MainWindow.xaml` StatusBar
- **内容**: `MainViewModel.LastError` は `ReportError` / `SetStatus` で設定されるが、メインウィンドウの XAML にバインディングが存在しない。StatusBar には `ModbusStatus` / `PlcStatus` / `ClockText` しか表示されていない。エラーメッセージを確認するにはログウィンドウを手動で開く必要があり、ユーザーが気付けない。
- **対応案**: StatusBar に `LastError` を追加するか、ヘッダ行に小さなエラー表示欄を設ける。

---

### [BUG-02] `LedStateConverter` が `ForceOff` 状態を `Off` と同じ色で描画する
- **重要度**: 中
- **状態**: 対応済み。`ForceOff` を暗めのオレンジで表示する。
- **場所**: `GatewayApp/Converters/LedStateConverter.cs:13`
- **内容**: `LedState.ForceOff` は switch の default に落ちてオフ時と同じ暗色になる。オレンジのボーダー（`LedStrokeConverter`）だけが唯一の手掛かりだが、幅 10px の LED では視認しにくい。力値が OFF に強制されているビットか、単に OFF のビットかを見分けられない。
- **対応案**: `ForceOff` 用に明示的な色（暗めのオレンジ等）を追加する。

```csharp
// 変更前
_ => new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),

// 変更後イメージ
LedState.ForceOff => new SolidColorBrush(Color.FromRgb(0x5a, 0x3a, 0x00)),
_ => new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
```

---

### [BUG-03] `StopAsync` の `_runCts?.Cancel()` がロック外で競合する可能性
- **重要度**: 中
- **状態**: 対応済み。`CancelRun()` で `ObjectDisposedException` を吸収し、停止競合で落ちないようにする。
- **場所**: `GatewayApp/Services/GatewayService.cs:47`
- **内容**: `StopAsync` は `_operationGate` 取得前に `_runCts?.Cancel()` を呼ぶ。別スレッドが同時に `StopUnlockedAsync` で `_runCts.Dispose()` → `_runCts = null` を行うと、null チェック後に Dispose 済みオブジェクトの `Cancel()` を呼んで `ObjectDisposedException` が発生しうる。
- **対応案**: ローカル変数にコピーしてから Cancel する。

```csharp
var cts = _runCts;
cts?.Cancel();
```

---

### [BUG-04] `ModbusSlaveService.StartAsync` の起動検出に `Task.Delay(50)` を使用
- **重要度**: 低〜中
- **状態**: 対応済み。`TcpListener.Start()` を先に実行し、ポートバインド失敗を同期的に検出する。
- **場所**: `GatewayApp/Services/ModbusSlaveService.cs:32`
- **内容**: ポートバインド失敗を検出するために 50ms 待機して `IsFaulted` を確認している。負荷の高い環境では 50ms 以内に Fault しないケースがあり、接続に成功したように見えて後から落ちる可能性がある。
- **対応案**: `TcpListener.Start()` を試みた後に `Socket.IsBound` を確認するか、`ListenAsync` がすぐに返す例外をキャッチする構造にする。

---

### [BUG-05] `async void Window_Closing` でウィンドウクローズと非同期クリーンアップが競合
- **重要度**: 低〜中
- **状態**: 対応済み。初回 Closing をキャンセルし、非同期破棄後に閉じ直す。
- **場所**: `GatewayApp/MainWindow.xaml.cs:312`
- **内容**: `Closing` イベントハンドラが `async void` のため、`DisposeAsync()` が完了する前にウィンドウが閉じてプロセスが終了することがある。PLC 切断やファイル書き込みが中断されうる。
- **対応案**: `e.Cancel = true` で一度クローズをキャンセルし、非同期クリーンアップ完了後に `Close()` を呼び直す。

---

### [BUG-06] `MappingEntrySettings.Direction` が保存・読み込みで常に上書きされる
- **重要度**: 低（設計の不整合）
- **状態**: 対応済み。保存形式から `Direction` を削除し、方向は Modbus 種別から固定算出する。
- **場所**: `GatewayApp/Models/MappingEntry.cs:167,183`
- **内容**: `ToSettings()` と `FromSettings()` のどちらも `Direction = GetDefaultDirection(ModbusType)` を使用し、`entry.Direction` の実値を無視している。`MappingEntrySettings.Direction` は保存はされるが読み込み時に必ず上書きされるため、フィールドが dead weight になっている。
- **対応案**: `MappingEntrySettings.Direction` を削除するか、読み書きで実値を使うかを決める。

---

### [BUG-07] `ObserveIfFaulted` が未処理例外をサイレントに破棄する
- **重要度**: 低
- **状態**: 対応済み。予期しない Listen 例外を `WarningReported` 経由でログへ出す。
- **場所**: `GatewayApp/Services/ModbusSlaveService.cs:116`
- **内容**: `ObserveListenTaskAsync` は `OperationCanceled` / `ObjectDisposed` / `IOException` / `InvalidOperationException` のみ握り潰す。それ以外の例外は `ObserveIfFaulted` で `_ = task.Exception` として観測はされるが、ログには記録されない。
- **対応案**: 予期しない例外をログに出力する。

---

### [BUG-08] `GetPlcDType` と `GetSlmpWriteDType` が完全に重複
- **重要度**: 低（コード臭）
- **状態**: 対応済み。SLMP 書き込み側も `GetPlcDType` を使う。
- **場所**: `GatewayApp/Services/PlcClientService.cs:141,146`
- **内容**: 2 つのメソッドが完全に同一の実装。一方で良い。
- **対応案**: `GetSlmpWriteDType` を削除し、書き込み側も `GetPlcDType` を呼ぶ。

---

## 改善 (Improvements)

### [IMP-01] Modbus 最大アドレス削減時に既存マッピングが警告なく削除される
- **優先度**: 高
- **状態**: 対応済み。削除対象件数を確認してから反映する。
- **場所**: `GatewayApp/ViewModels/MainViewModel.cs:673` `SyncMappingRange`
- **内容**: `ModbusSettings` の最大アドレスを小さく設定すると、超過したアドレスのマッピングエントリ（PLC アドレス・コメント含む）が静かに削除される。ユーザーが意図せず設定を失う。
- **対応案**: 削除対象のエントリ数を保存ダイアログで確認し、OK/キャンセルを提示する。

---

### [IMP-02] 未保存変更インジケーターがない
- **優先度**: 高
- **状態**: 対応済み。タイトルに `*` を表示し、終了時に未保存確認を出す。
- **場所**: `GatewayApp/MainWindow.xaml`
- **内容**: マッピングを編集したり設定を変更しても、ウィンドウタイトルやメニューに "未保存" の表示がない。誤って閉じると変更が失われる。
- **対応案**: 変更が生じたら `Title` に `*` を付加する、またはウィンドウクローズ時に未保存確認ダイアログを出す。

---

### [IMP-03] 通信エラー後の自動再接続がない
- **優先度**: 中
- **方針**: これはやらない。不必要なフォールバックは禁止し、通信エラー時は停止して原因確認を優先する。
- **場所**: `GatewayApp/ViewModels/MainViewModel.cs:438` `PollTimerOnTick`
- **内容**: ポーリングエラー発生時に `StopAsync("通信エラーで停止")` を呼び、ゲートウェイが停止する。ユーザーが手動で再起動するまで通信が止まる。工場環境で一時的なネットワーク断が発生した場合に影響が大きい。
- **対応案**: 一定回数リトライ後に停止する、またはバックオフ付きで自動再接続を試みるオプションを設ける。

---

### [IMP-04] 稼働中に設定を参照できない
- **優先度**: 中
- **状態**: 対応済み。稼働中は読み取り専用として表示する。
- **場所**: `GatewayApp/Views/Dialogs/PlcSettingsWindow.xaml.cs` / `ModbusSettingsWindow.xaml.cs`
- **内容**: `isRunning = true` の場合、設定ダイアログのフォームが `IsEnabled = false` で完全に無効化される。現在の接続設定を確認したいだけの場合も編集不可の制限を受ける（これ自体は正しいが）。
- **対応案**: `[読み取り専用で確認]` と `[キャンセル]` ボタンのみ表示するモードを用意するか、ラベル表示に切り替える。

---

### [IMP-05] Modbus TCP 接続クライアント数が表示されない
- **優先度**: 低〜中
- **状態**: 対応済み。StatusBar に接続クライアント数を表示する。
- **場所**: `GatewayApp/Services/ModbusSlaveService.cs` / `MainWindow.xaml`
- **内容**: Factory I/O や他のクライアントが接続しているかどうかを確認する手段がない。デバッグ・動作確認時に不便。
- **対応案**: NModbus の `IModbusSlaveNetwork` に接続数を取得する API があれば利用し、StatusBar または ヘッダ行に表示する。

---

### [IMP-06] ログウィンドウが自動スクロールしない
- **重要度**: 低〜中
- **方針**: これはやらない。現在の `LogWindow` はログ追加時に `ScrollIntoView` しており、この項目は対応済み。
- **場所**: `GatewayApp/Views/Dialogs/LogWindow.xaml`
- **内容**: 新しいログエントリが追加されても、`ListBox` は最下部に自動スクロールしない。古いログを見ている場合は問題ないが、リアルタイム監視には不便。
- **対応案**: `ScrollViewer.ScrollToBottom()` を `ErrorLogs.CollectionChanged` ハンドラで呼ぶ。

---

### [IMP-07] `ForceWriteAsync` で `FromPlc` エントリの `LastWritten` を誤設定
- **優先度**: 低
- **方針**: これはやらない。現在は `LastWritten` を Monitor 画面に表示しないため、ユーザー表示上の問題として扱わない。
- **場所**: `GatewayApp/Services/GatewayService.cs:125`
- **内容**: `Direction == FromPlc` のエントリに対して ForceWrite を行った場合、PLC への書き込みは行わないのに `entry.LastWritten = DateTime.Now` を設定する。Monitor 画面の `LastWritten` 表示が誤解を招く。
- **対応案**: `FromPlc` ではなく実際に PLC 書き込みを行った場合のみ `LastWritten` を更新する。

---

### [IMP-08] `SyncMappingRange` で `ObservableCollection` を連続変更して多数のイベントを発火
- **優先度**: 低
- **状態**: 対応済み。少なくともソート済みの場合は `SortMappings` の全消し全追加を行わない。
- **場所**: `GatewayApp/ViewModels/MainViewModel.cs:673`
- **内容**: `SyncMappingRange` が `ObservableCollection<MappingEntry>` を直接 `RemoveAt` / `Add` するため、エントリ数が多い場合に多数の `CollectionChanged` イベントが発火し、`RebuildMonitorRows` が後でまとめて呼ばれるまでの間も余計な UI 更新が走る可能性がある。
- **対応案**: 一時リストで差分を計算してから一括反映するか、`ObservableCollection` の `AddRange` 相当の操作を使う。

---

### [IMP-09] `RegisterForceTextBox_LostFocus` でフォーカス移動時に意図せずコミットされる
- **優先度**: 低
- **状態**: 対応済み。`LostFocus` ではコミットせず、入力欄を閉じるだけにする。確定は Enter。
- **場所**: `GatewayApp/MainWindow.xaml.cs:251`
- **内容**: 強制値入力中にテキストボックス以外の場所をクリックすると `LostFocus` が発火し、入力値がそのままコミットされる。Escape キーで意図的にキャンセルする場合と区別がつかない。
- **対応案**: フォーカス移動先が同ウィンドウ内かどうかを確認し、ウィンドウ外クリックの場合のみキャンセル扱いにする、または LostFocus でも Enter と同じくコミットする（現状）か、一貫してどちらかにする旨を設計として明確にする。

---

### [IMP-10] ウィンドウタイトルに現在の設定ファイルパスが表示されない
- **優先度**: 低
- **状態**: 対応済み。タイトルに現在の設定パスを表示する。
- **場所**: `GatewayApp/MainWindow.xaml:6`
- **内容**: 複数の設定ファイルを使い分けるユーザーは、現在どのファイルを使っているか確認できない。
- **対応案**: `CurrentSettingsPath` をタイトルバーに反映する（例: `"Factory I/O Gateway — settings.json"`）。

---

### [IMP-11] `WriteGatewayLog` の例外をサイレントに無視
- **優先度**: 低
- **状態**: 対応済み。ログ書き込み失敗は一度だけログ一覧と `LastError` に出す。
- **場所**: `GatewayApp/ViewModels/MainViewModel.cs:527`
- **内容**: ログファイル書き込み失敗（ディスク満杯など）が空の catch ブロックで握り潰される。ユーザーには通知されない。
- **対応案**: ログ書き込み失敗を `ReportError` に出すか、連続失敗時のみ UI 通知する（ログ書き込みをループさせない工夫が必要）。

---

### [IMP-12] `CsvImportService.TryParseDisplayType` でレジスタの空 DataType が非エラー扱いにならない
- **優先度**: 低
- **状態**: 対応済み。空 DataType は補完せず `"DataType 未設定"` でスキップする。
- **場所**: `GatewayApp/Services/CsvImportService.cs:163`
- **内容**: `dataType` が空文字列の場合、`Int16` にフォールバックして `return false`（スキップ）になる。「空 = Int16 として追加」が意図なら `true` を返すべきだし、「空はスキップ」が意図なら早期リターンが望ましい。現状はフォールバックして即スキップという冗長な流れ。
- **対応案**: 空文字列の場合は明示的に `displayType = DisplayType.Int16; return true;` とするか、スキップ理由のメッセージを "DataType 未設定" に明確化する。

---

## メモ

- `GatewayApp.SmokeTests` の内容未確認という前提の追記はやらない。現状は `PlcAddressSequence`、BulkAssign、About 画面などの smoke test を確認済み。

- **テストカバレッジ**: `GatewayApp.SmokeTests` プロジェクトは存在するが、本調査では内容を確認していない。`PlcAddressSequence` の複雑なアドレス変換ロジック（KeyenceBitBank, OctalNoPadding 等）はユニットテストによる保護が重要。
- **NModbus**: `NModbus` ライブラリの `SlaveDataStore` はスレッドセーフか確認が必要。ポーリング（スレッド A）とフォース書き込み（UI スレッド）が `_operationGate` で直列化されているため現状は問題ないはずだが、Modbus クライアントからの読み取り（別スレッド）との競合は別途確認を要する。
