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
    private const long MaxGatewayLogBytes = 1_000_000;
    private readonly SettingsService _settingsService = new();
    private readonly CsvImportService _csvImportService = new();
    private readonly GatewayService _gatewayService = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _pollTimer;
    private bool _pollInFlight;

    [ObservableProperty]
    private PlcSettings _plc = new();

    [ObservableProperty]
    private ModbusSettings _modbus = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isForceEnabled;

    [ObservableProperty]
    private string _gatewayStatus = "停止中";

    [ObservableProperty]
    private string _modbusStatus = "Modbus TCP 停止";

    [ObservableProperty]
    private string _plcStatus = "PLC 未接続";

    [ObservableProperty]
    private DateTime _clock = DateTime.Now;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [ObservableProperty]
    private string _currentSettingsPath = string.Empty;

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

    public ObservableCollection<LogEntry> ErrorLogs { get; } = [];

    public Array ModbusTypeValues { get; } = Enum.GetValues<ModbusType>();

    public string ClockText => Clock.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string StartStopText => IsRunning ? "■ 停止" : "▶ 起動";

    public string ForceText => IsForceEnabled ? "FORCE ON" : "FORCE";

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
    public void ToggleForce()
    {
        IsForceEnabled = !IsForceEnabled;
    }

    [RelayCommand]
    public void SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(CurrentSettingsPath))
        {
            throw new InvalidOperationException("保存先の設定ファイルが未指定です。名前を付けて保存してください。");
        }

        _settingsService.Save(CurrentSettingsPath, BuildSettings());
        SetStatus($"設定を上書き保存しました: {CurrentSettingsPath}");
    }

    public void SaveSettingsAs(string path)
    {
        _settingsService.Save(path, BuildSettings());
        CurrentSettingsPath = path;
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
        SetStatus($"設定を読み込みました: {path}");
    }

    [RelayCommand]
    public async Task CycleBoolForceAsync(MappingEntry? entry)
    {
        if (entry is null || entry.IsRegister || !IsForceEnabled)
        {
            return;
        }

        entry.ForceValue = entry.ForceValue switch
        {
            null => 1,
            1 => 0,
            _ => null,
        };

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
    }

    public void ApplyBulkAssign(ModbusType modbusType, string prefix, int startNumber, int increment)
    {
        var index = 0;
        foreach (var entry in Mappings.Where(x => x.ModbusType == modbusType).OrderBy(x => x.ModbusAddress))
        {
            entry.PlcAddress = $"{prefix}{startNumber + index * increment}";
            index++;
        }
    }

    public void BeginRegisterEdit(MappingEntry? entry)
    {
        if (entry is null || !entry.IsRegister || !IsForceEnabled)
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
        catch (OperationCanceledException) when (!IsRunning)
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

        ErrorLogs.Clear();
        LastError = string.Empty;
        ClearGatewayLog();
    }

    public async ValueTask DisposeAsync()
    {
        _pollTimer.Stop();
        await _gatewayService.DisposeAsync().ConfigureAwait(false);
    }

    partial void OnClockChanged(DateTime value)
    {
        OnPropertyChanged(nameof(ClockText));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(StartStopText));
    }

    partial void OnIsForceEnabledChanged(bool value)
    {
        foreach (var entry in Mappings)
        {
            entry.ForceEnabled = value;
        }

        OnPropertyChanged(nameof(ForceText));
    }

    private async void PollTimerOnTick(object? sender, EventArgs e)
    {
        Clock = DateTime.Now;
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
        catch (OperationCanceledException) when (!IsRunning)
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

        await _gatewayService.StopAsync().ConfigureAwait(true);
        ModbusStatus = "Modbus TCP 停止";
        PlcStatus = "PLC 未接続";
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
        catch (OperationCanceledException) when (!IsRunning)
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
        LastError = message;
    }

    private void ClearStatus()
    {
        LastError = string.Empty;
    }

    private void ReportError(string message)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => ReportError(message));
            return;
        }

        LastError = message;
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
        ErrorLogs.Add(entry);
        WriteGatewayLog(entry);
        while (ErrorLogs.Count > 200)
        {
            ErrorLogs.RemoveAt(0);
        }
    }

    private void WriteGatewayLog(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(DefaultSettingsDirectory);
            var path = Path.Combine(DefaultSettingsDirectory, "gateway.log");
            RotateGatewayLogIfNeeded(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd} {entry.FullText}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void ClearGatewayLog()
    {
        try
        {
            Directory.CreateDirectory(DefaultSettingsDirectory);
            File.WriteAllText(Path.Combine(DefaultSettingsDirectory, "gateway.log"), string.Empty);
        }
        catch
        {
        }
    }

    private static void RotateGatewayLogIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length <= MaxGatewayLogBytes)
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} gateway.log rotated because it exceeded {MaxGatewayLogBytes} bytes.");
    }

    private void ApplySettings(AppSettings? settings)
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
            entry.ForceEnabled = IsForceEnabled;
        }

        _pollTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, Plc.PollingMs));
        RebuildMonitorRows();
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
        if (e.NewItems is not null)
        {
            foreach (MappingEntry entry in e.NewItems)
            {
                entry.RealScale = Modbus.RealScale;
                entry.ForceEnabled = IsForceEnabled;
                entry.PropertyChanged += OnMappingPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (MappingEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= OnMappingPropertyChanged;
            }
        }
    }

    private void OnMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MappingEntry.DisplayType))
        {
            RebuildMonitorRows();
        }
    }

    private void SortMappings()
    {
        var sorted = Mappings.OrderBy(x => x.ModbusType).ThenBy(x => x.ModbusAddress).ToList();
        Mappings.Clear();
        foreach (var item in sorted)
        {
            Mappings.Add(item);
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
                ForceEnabled = IsForceEnabled,
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
}
