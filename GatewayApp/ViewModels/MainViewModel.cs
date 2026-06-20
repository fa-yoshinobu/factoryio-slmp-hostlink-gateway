using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatewayApp.Models;
using GatewayApp.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;

namespace GatewayApp.ViewModels;

public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly CsvImportService _csvImportService = new();
    private readonly GatewayService _gatewayService = new();
    private readonly LogFileService _logFileService = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _pollTimer;
    private bool _pollInFlight;
    private bool _suppressDirty;
    private bool _gatewayLogFailureReported;

    [ObservableProperty]
    private PlcSettings _plc = new();

    [ObservableProperty]
    private ModbusSettings _modbus = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isForceXEnabled;

    [ObservableProperty]
    private bool _isForceYEnabled;

    [ObservableProperty]
    private string _gatewayStatus = "停止中";

    [ObservableProperty]
    private string _modbusStatus = "Modbus TCP 停止";

    [ObservableProperty]
    private string _plcStatus = "PLC 未接続";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _currentSettingsPath = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    public MainViewModel()
    {
        _gatewayService.WarningReported += ReportError;
        _gatewayService.TraceReported += ReportLog;
        Mappings.CollectionChanged += OnMappingsChanged;
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(10, Plc.PollingMs)),
        };
        _pollTimer.Tick += PollTimerOnTick;

        ApplySettings(null);
        _pollTimer.Start();
    }

    public ObservableCollection<MappingEntry> Mappings { get; } = [];

    public ObservableCollection<MonitorBoolRow> BoolRows { get; } = [];

    public ObservableCollection<MonitorRegisterRow> RegisterRows { get; } = [];

    public ObservableCollection<LogEntry> Logs { get; } = [];

    public Array ModbusTypeValues { get; } = Enum.GetValues<ModbusType>();

    public string WindowTitle
    {
        get
        {
            var settingsText = string.IsNullOrWhiteSpace(CurrentSettingsPath)
                ? "未保存設定"
                : CurrentSettingsPath;
            return $"Factory I/O Gateway - {settingsText}{(IsDirty ? " *" : string.Empty)}";
        }
    }

    public string StartStopText => IsRunning ? "■ 停止" : "▶ 起動";

    public string ForceXText => IsForceXEnabled ? "FORCE X ON" : "FORCE X";

    public string ForceYText => IsForceYEnabled ? "FORCE Y ON" : "FORCE Y";

    public string ModbusClientText => $"Modbus Clients: {_gatewayService.ModbusClientCount}";

    public string DefaultSettingsPath => _settingsService.SettingsPath;

    public string DefaultSettingsDirectory => _settingsService.SettingsDirectory;

    public string SettingsDialogDirectory
    {
        get
        {
            var currentDirectory = string.IsNullOrWhiteSpace(CurrentSettingsPath)
                ? null
                : Path.GetDirectoryName(CurrentSettingsPath);

            return !string.IsNullOrWhiteSpace(currentDirectory)
                ? currentDirectory
                : DefaultSettingsDirectory;
        }
    }

    [RelayCommand]
    private async Task ToggleRunningAsync()
    {
        if (IsRunning)
        {
            await StopAsync("停止中").ConfigureAwait(true);
            return;
        }

        ClearStatus();
        try
        {
            await _gatewayService.StartAsync(Plc, Modbus).ConfigureAwait(true);
            IsRunning = true;
            GatewayStatus = "稼働中";
            ModbusStatus = $"Modbus TCP 待機中 {Modbus.ListenIp}:{Modbus.Port}";
            PlcStatus = "PLC 接続中";
            _pollTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, Plc.PollingMs));
        }
        catch (Exception ex)
        {
            IsRunning = false;
            GatewayStatus = "停止中";
            ModbusStatus = "Modbus TCP 停止";
            PlcStatus = "PLC 未接続";
            ReportError(ex.Message);
        }
    }

    [RelayCommand]
    public void ToggleForceX()
    {
        IsForceXEnabled = !IsForceXEnabled;
    }

    [RelayCommand]
    public void ToggleForceY()
    {
        IsForceYEnabled = !IsForceYEnabled;
    }

    [RelayCommand]
    public void SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(CurrentSettingsPath))
        {
            throw new InvalidOperationException("保存先の設定ファイルが未指定です。名前を付けて保存してください。");
        }

        _settingsService.Save(CurrentSettingsPath, BuildSettings());
        IsDirty = false;
        SetStatus($"設定を上書き保存しました: {CurrentSettingsPath}");
    }

    public void SaveSettingsAs(string path)
    {
        _settingsService.Save(path, BuildSettings());
        CurrentSettingsPath = path;
        IsDirty = false;
        SetStatus($"設定を保存しました: {path}");
    }

    public void LoadSettingsFromFile(string path)
    {
        if (IsRunning)
        {
            ReportError("稼働中は設定を読み込めません。");
            return;
        }

        var settings = _settingsService.Load(path);
        ApplySettings(settings);
        CurrentSettingsPath = path;
        IsDirty = false;
        SetStatus($"設定を読み込みました: {path}");
    }

    [RelayCommand]
    public async Task CycleBoolForceAsync(MappingEntry? entry)
    {
        if (entry is null || entry.IsRegister || !IsForceActiveFor(entry))
        {
            return;
        }

        var current = NormalizeBoolRaw(entry.RawValue);
        var forced = entry.ForceValue.HasValue ? NormalizeBoolRaw(entry.ForceValue.Value) : current;
        entry.ForceValue = forced == current ? InvertBoolRaw(current) : current;

        await ApplyForceAsync(entry).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ClearForceAsync(MappingEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.ForceValue = null;
        entry.IsForceEditing = false;
        await ApplyForceAsync(entry).ConfigureAwait(true);
    }

    public IReadOnlyList<CsvImportPreviewItem> PreviewCsvImport(string path)
    {
        return _csvImportService.Preview(path, Mappings, Modbus.RealScale);
    }

    public void ApplyCsvImport(IReadOnlyList<CsvImportPreviewItem> previewItems)
    {
        var result = _csvImportService.Apply(previewItems, Mappings);
        ExpandModbusMaxAddressesToMappings();
        SyncMappingsToModbusAddressLimits();
        SortMappings();
        RebuildMonitorRows();
        if (result.Added > 0 || result.Updated > 0)
        {
            MarkDirty();
        }
        SetStatus($"CSV インポート: 追加 {result.Added} / 更新 {result.Updated} / スキップ {result.Skipped}");
    }

    public void ApplyPlcSettings(PlcSettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        Plc = settings.Clone();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, Plc.PollingMs));
        MarkDirty();
    }

    public void ApplyModbusSettings(ModbusSettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        Modbus = settings.Clone();
        SyncMappingsToModbusAddressLimits();
        SortMappings();
        foreach (var entry in Mappings)
        {
            entry.RealScale = Modbus.RealScale;
        }
        RebuildMonitorRows();
        MarkDirty();
    }

    public int CountMappingsAboveModbusLimits(ModbusSettings settings)
    {
        return CountMappingsAboveLimit(ModbusType.Coil, settings.MaxCoilAddress)
            + CountMappingsAboveLimit(ModbusType.DiscreteInput, settings.MaxDiscreteInputAddress)
            + CountMappingsAboveLimit(ModbusType.HoldingRegister, settings.MaxHoldingRegisterAddress)
            + CountMappingsAboveLimit(ModbusType.InputRegister, settings.MaxInputRegisterAddress);
    }

    public void ApplyBulkAssign(ModbusType modbusType, string prefix, string startNumberText, int increment)
    {
        var entries = Mappings.Where(x => x.ModbusType == modbusType).OrderBy(x => x.ModbusAddress).ToList();
        var addresses = new List<string>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var offset = checked(index * increment);
            if (!PlcAddressSequence.TryFormat(Plc.Protocol, Plc.SlmpProfile, prefix, startNumberText, offset, out var address, out var error))
            {
                ReportError(error);
                return;
            }

            addresses.Add(address);
        }

        for (var index = 0; index < entries.Count; index++)
        {
            entries[index].PlcAddress = addresses[index];
        }

        if (entries.Count > 0)
        {
            MarkDirty();
        }
    }

    public void BeginRegisterEdit(MappingEntry? entry)
    {
        if (entry is null || !entry.IsRegister || !IsForceActiveFor(entry))
        {
            return;
        }

        entry.ForceEditText = entry.ForceValue.HasValue
            ? FormatRawForInput(entry, entry.ForceValue.Value)
            : entry.DisplayValue;
        entry.IsForceEditing = true;
    }

    public async Task CommitRegisterForceAsync(MappingEntry? entry, bool clear)
    {
        try
        {
            if (entry is null || !entry.IsRegister)
            {
                return;
            }

            if (clear)
            {
                entry.ForceValue = null;
                entry.IsForceEditing = false;
                await ApplyForceAsync(entry).ConfigureAwait(true);
                return;
            }

            if (!TryParseRegisterInput(entry, entry.ForceEditText, out var raw, out var error))
            {
                ReportError(error);
                entry.IsForceEditing = false;
                return;
            }

            entry.ForceValue = raw;
            entry.IsForceEditing = false;
            await ApplyForceAsync(entry).ConfigureAwait(true);
        }
        catch (Exception ex) when (!IsRunning && CommunicationExceptionClassifier.IsExpectedLocalStop(ex))
        {
        }
        catch (Exception ex)
        {
            if (entry is not null)
            {
                entry.IsForceEditing = false;
            }

            ReportException(ex);
        }
    }

    public void ReportException(Exception exception)
    {
        if (!IsRunning && CommunicationExceptionClassifier.IsExpectedLocalStop(exception))
        {
            return;
        }

        ReportError($"{exception.GetType().Name}: {exception.Message}");
    }

    public void ReportLog(string message)
    {
        AddLog(message);
    }

    public void ClearLogs()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(ClearLogs);
            return;
        }

        Logs.Clear();
        StatusMessage = string.Empty;
        ClearGatewayLog();
    }

    public async ValueTask DisposeAsync()
    {
        _pollTimer.Stop();
        await _gatewayService.DisposeAsync().ConfigureAwait(false);
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(StartStopText));
    }

    partial void OnCurrentSettingsPathChanged(string value)
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnIsForceXEnabledChanged(bool value)
    {
        ApplyForceState(DataDirection.ToPlc, value);
        OnPropertyChanged(nameof(ForceXText));
    }

    partial void OnIsForceYEnabledChanged(bool value)
    {
        ApplyForceState(DataDirection.FromPlc, value);
        OnPropertyChanged(nameof(ForceYText));
    }

    private void ApplyForceState(DataDirection direction, bool enabled)
    {
        foreach (var entry in Mappings.Where(x => x.Direction == direction))
        {
            if (enabled)
            {
                entry.ForceValue = entry.RawValue;
                entry.IsForceEditing = false;
            }

            entry.ForceEnabled = enabled;
        }
    }

    private bool IsForceActiveFor(MappingEntry entry)
    {
        return IsForceActiveForDirection(entry.Direction);
    }

    private bool IsForceActiveForType(ModbusType modbusType)
    {
        return IsForceActiveForDirection(MappingEntry.GetDefaultDirection(modbusType));
    }

    private bool IsForceActiveForDirection(DataDirection direction)
    {
        return direction == DataDirection.ToPlc ? IsForceXEnabled : IsForceYEnabled;
    }

    private static int NormalizeBoolRaw(int rawValue)
    {
        return rawValue == 0 ? 0 : 1;
    }

    private static int InvertBoolRaw(int rawValue)
    {
        return NormalizeBoolRaw(rawValue) == 0 ? 1 : 0;
    }

    private async void PollTimerOnTick(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ModbusClientText));
        if (!IsRunning || _pollInFlight)
        {
            return;
        }

        _pollInFlight = true;
        try
        {
            await _gatewayService.PollPlcAsync(Mappings).ConfigureAwait(true);
            ClearStatus();
        }
        catch (Exception ex) when (!IsRunning && CommunicationExceptionClassifier.IsExpectedLocalStop(ex))
        {
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
            await StopAsync("通信エラーで停止").ConfigureAwait(true);
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    private async Task StopAsync(string gatewayStatus)
    {
        IsRunning = false;
        GatewayStatus = gatewayStatus;
        ModbusStatus = "Modbus TCP 停止中";
        PlcStatus = "PLC 切断中";

        try
        {
            await _gatewayService.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (CommunicationExceptionClassifier.IsExpectedLocalStop(ex))
        {
        }
        finally
        {
            ModbusStatus = "Modbus TCP 停止";
            PlcStatus = "PLC 未接続";
        }
    }

    private async Task ApplyForceAsync(MappingEntry entry)
    {
        entry.RefreshComputed();
        if (!IsRunning)
        {
            return;
        }

        try
        {
            await _gatewayService.ForceWriteAsync(entry).ConfigureAwait(true);
        }
        catch (Exception ex) when (!IsRunning && CommunicationExceptionClassifier.IsExpectedLocalStop(ex))
        {
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
            await StopAsync("通信エラーで停止").ConfigureAwait(true);
        }
    }

    private void SetStatus(string message)
    {
        StatusMessage = message;
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
    }

    private void ReportError(string message)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => ReportError(message));
            return;
        }

        StatusMessage = message;
        AddLog(message);
    }

    private void AddLog(string message)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => AddLog(message));
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new LogEntry(message);
        Logs.Add(entry);
        WriteGatewayLog(entry);
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(0);
        }
    }

    private void WriteGatewayLog(LogEntry entry)
    {
        try
        {
            _logFileService.WriteGatewayLog(entry);
        }
        catch (Exception ex)
        {
            if (_gatewayLogFailureReported)
            {
                return;
            }

            _gatewayLogFailureReported = true;
            var message = $"ログファイル書き込み失敗: {ex.Message}";
            StatusMessage = message;
            Logs.Add(new LogEntry(message));
        }
    }

    private void ClearGatewayLog()
    {
        try
        {
            _logFileService.ClearGatewayLog();
            _gatewayLogFailureReported = false;
        }
        catch
        {
        }
    }

    private void ApplySettings(AppSettings? settings)
    {
        _suppressDirty = true;
        try
        {
            Plc = settings?.Plc?.Clone() ?? new PlcSettings();
            Modbus = settings?.Modbus?.Clone() ?? new ModbusSettings();
            Modbus.RealScale = Math.Max(1, settings?.RealScale ?? Modbus.RealScale);

            Mappings.Clear();
            var savedMappings = settings?.Mappings;
            if (savedMappings is { Count: > 0 })
            {
                foreach (var mapping in savedMappings)
                {
                    Mappings.Add(MappingEntry.FromSettings(mapping, Modbus.RealScale));
                }
            }

            InferMissingModbusMaxAddressesFromMappings();
            SyncMappingsToModbusAddressLimits();
            SortMappings();

            foreach (var entry in Mappings)
            {
                entry.ForceEnabled = IsForceActiveFor(entry);
            }

            _pollTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, Plc.PollingMs));
            RebuildMonitorRows();
            IsDirty = false;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private AppSettings BuildSettings()
    {
        return new AppSettings
        {
            Plc = Plc.Clone(),
            Modbus = Modbus.Clone(),
            RealScale = Modbus.RealScale,
            Mappings = Mappings.OrderBy(x => x.ModbusType).ThenBy(x => x.ModbusAddress).Select(x => x.ToSettings()).ToList(),
        };
    }

    private void OnMappingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            if (e.OldItems is not null)
            {
                foreach (MappingEntry entry in e.OldItems)
                {
                    entry.PropertyChanged -= OnMappingPropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (MappingEntry entry in e.NewItems)
                {
                    entry.RealScale = Modbus.RealScale;
                    entry.ForceEnabled = IsForceActiveFor(entry);
                    entry.PropertyChanged += OnMappingPropertyChanged;
                }
            }
        }

        MarkDirty();
    }

    private void OnMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MappingEntry.PlcAddress)
            or nameof(MappingEntry.DisplayType)
            or nameof(MappingEntry.Comment))
        {
            MarkDirty();
        }

        if (e.PropertyName is nameof(MappingEntry.DisplayType))
        {
            RebuildMonitorRows();
        }
    }

    private void SortMappings()
    {
        var sorted = Mappings.OrderBy(x => x.ModbusType).ThenBy(x => x.ModbusAddress).ToList();
        if (Mappings.SequenceEqual(sorted))
        {
            return;
        }

        for (var targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
        {
            var item = sorted[targetIndex];
            var currentIndex = Mappings.IndexOf(item);
            if (currentIndex != targetIndex)
            {
                Mappings.Move(currentIndex, targetIndex);
            }
        }
    }

    private void InferMissingModbusMaxAddressesFromMappings()
    {
        Modbus.MaxCoilAddress ??= GetMaxMappingAddress(ModbusType.Coil);
        Modbus.MaxDiscreteInputAddress ??= GetMaxMappingAddress(ModbusType.DiscreteInput);
        Modbus.MaxHoldingRegisterAddress ??= GetMaxMappingAddress(ModbusType.HoldingRegister);
        Modbus.MaxInputRegisterAddress ??= GetMaxMappingAddress(ModbusType.InputRegister);
    }

    private void ExpandModbusMaxAddressesToMappings()
    {
        Modbus.MaxCoilAddress = MaxNullable(Modbus.MaxCoilAddress, GetMaxMappingAddress(ModbusType.Coil));
        Modbus.MaxDiscreteInputAddress = MaxNullable(Modbus.MaxDiscreteInputAddress, GetMaxMappingAddress(ModbusType.DiscreteInput));
        Modbus.MaxHoldingRegisterAddress = MaxNullable(Modbus.MaxHoldingRegisterAddress, GetMaxMappingAddress(ModbusType.HoldingRegister));
        Modbus.MaxInputRegisterAddress = MaxNullable(Modbus.MaxInputRegisterAddress, GetMaxMappingAddress(ModbusType.InputRegister));
    }

    private void SyncMappingsToModbusAddressLimits()
    {
        SyncMappingRange(ModbusType.Coil, Modbus.MaxCoilAddress);
        SyncMappingRange(ModbusType.DiscreteInput, Modbus.MaxDiscreteInputAddress);
        SyncMappingRange(ModbusType.HoldingRegister, Modbus.MaxHoldingRegisterAddress);
        SyncMappingRange(ModbusType.InputRegister, Modbus.MaxInputRegisterAddress);
    }

    private void SyncMappingRange(ModbusType modbusType, int? maxAddress)
    {
        if (!maxAddress.HasValue)
        {
            return;
        }

        var max = maxAddress.Value;
        for (var i = Mappings.Count - 1; i >= 0; i--)
        {
            if (Mappings[i].ModbusType == modbusType && Mappings[i].ModbusAddress > max)
            {
                Mappings.RemoveAt(i);
            }
        }

        var existingAddresses = Mappings
            .Where(x => x.ModbusType == modbusType)
            .Select(x => x.ModbusAddress)
            .ToHashSet();

        for (var address = 0; address <= max; address++)
        {
            if (existingAddresses.Contains(address))
            {
                continue;
            }

            Mappings.Add(new MappingEntry(modbusType, address)
            {
                RealScale = Modbus.RealScale,
                ForceEnabled = IsForceActiveForType(modbusType),
            });
        }
    }

    private int? GetMaxMappingAddress(ModbusType modbusType)
    {
        var addresses = Mappings
            .Where(x => x.ModbusType == modbusType)
            .Select(x => x.ModbusAddress)
            .ToList();

        return addresses.Count == 0 ? null : addresses.Max();
    }

    private int CountMappingsAboveLimit(ModbusType modbusType, int? maxAddress)
    {
        return maxAddress.HasValue
            ? Mappings.Count(x => x.ModbusType == modbusType && x.ModbusAddress > maxAddress.Value)
            : 0;
    }

    private static int? MaxNullable(int? left, int? right)
    {
        return (left, right) switch
        {
            (null, null) => null,
            (null, var value) => value,
            (var value, null) => value,
            (var l, var r) => Math.Max(l.Value, r.Value),
        };
    }

    private void RebuildMonitorRows()
    {
        BoolRows.Clear();
        RegisterRows.Clear();

        var coils = Mappings.Where(x => x.ModbusType == ModbusType.Coil).OrderBy(x => x.ModbusAddress).ToList();
        var inputs = Mappings.Where(x => x.ModbusType == ModbusType.DiscreteInput).OrderBy(x => x.ModbusAddress).ToList();
        for (var i = 0; i < Math.Max(coils.Count, inputs.Count); i++)
        {
            BoolRows.Add(new MonitorBoolRow(i < coils.Count ? coils[i] : null, i < inputs.Count ? inputs[i] : null));
        }

        var holding = Mappings.Where(x => x.ModbusType == ModbusType.HoldingRegister).OrderBy(x => x.ModbusAddress).ToList();
        var inputRegisters = Mappings.Where(x => x.ModbusType == ModbusType.InputRegister).OrderBy(x => x.ModbusAddress).ToList();
        for (var i = 0; i < Math.Max(holding.Count, inputRegisters.Count); i++)
        {
            RegisterRows.Add(new MonitorRegisterRow(i < holding.Count ? holding[i] : null, i < inputRegisters.Count ? inputRegisters[i] : null));
        }
    }

    private static string FormatRawForInput(MappingEntry entry, int raw)
    {
        var signed = unchecked((short)(ushort)raw);
        return entry.DisplayType == DisplayType.ScaledReal
            ? ((double)signed / Math.Max(1, entry.RealScale)).ToString("0.00", CultureInfo.InvariantCulture)
            : signed.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseRegisterInput(MappingEntry entry, string input, out int raw, out string error)
    {
        raw = 0;
        error = string.Empty;

        if (entry.DisplayType == DisplayType.ScaledReal)
        {
            if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var scaled)
                && !double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out scaled))
            {
                error = "数値を入力してください。";
                return false;
            }

            var signed = (int)Math.Round(scaled * Math.Max(1, entry.RealScale));
            if (signed is < short.MinValue or > short.MaxValue)
            {
                error = "Int16 範囲 (-32768〜32767) を超えています。";
                return false;
            }

            raw = unchecked((ushort)(short)signed);
            return true;
        }

        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && !int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
        {
            error = "整数を入力してください。";
            return false;
        }

        if (value is < short.MinValue or > short.MaxValue)
        {
            error = "Int16 範囲 (-32768〜32767) を超えています。";
            return false;
        }

        raw = unchecked((ushort)(short)value);
        return true;
    }

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            IsDirty = true;
        }
    }
}
