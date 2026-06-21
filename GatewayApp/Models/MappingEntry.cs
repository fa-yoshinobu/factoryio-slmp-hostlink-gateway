using CommunityToolkit.Mvvm.ComponentModel;
using GatewayApp.Services;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GatewayApp.Models;

public enum ModbusType
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister,
}

public enum DataDirection
{
    ToPlc,
    FromPlc,
}

[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Values are persisted and mirror the UI data type names.")]
public enum DisplayType
{
    Bool,
    Int16,
    ScaledReal,
}

public enum LedState
{
    Off,
    On,
    ForceOn,
    ForceOff,
    Error,
}

public partial class MappingEntry : ObservableObject
{
    private static readonly DisplayType[] BoolDisplayTypes = [DisplayType.Bool];
    private static readonly DisplayType[] RegisterDisplayTypes = [DisplayType.Int16, DisplayType.ScaledReal];

    [ObservableProperty]
    private string _plcAddress = string.Empty;

    [ObservableProperty]
    private DataDirection _direction;

    [ObservableProperty]
    private DisplayType _displayType;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private int _rawValue;

    [ObservableProperty]
    private LedState _ledState;

    [ObservableProperty]
    private int? _forceValue;

    [ObservableProperty]
    private DateTime? _lastWritten;

    [ObservableProperty]
    private bool _forceEnabled;

    [ObservableProperty]
    private int _realScale = 100;

    [ObservableProperty]
    private bool _isForceEditing;

    [ObservableProperty]
    private string _forceEditText = string.Empty;

    public MappingEntry(ModbusType modbusType, int modbusAddress)
    {
        ModbusType = modbusType;
        ModbusAddress = modbusAddress;
        Direction = GetDefaultDirection(modbusType);
        DisplayType = NormalizeDisplayType(modbusType, IsRegisterType(modbusType) ? DisplayType.Int16 : DisplayType.Bool);
        RefreshComputed();
    }

    public ModbusType ModbusType { get; }

    public int ModbusAddress { get; }

    public bool IsBool => !IsRegister;

    public bool IsRegister => IsRegisterType(ModbusType);

    public IReadOnlyList<DisplayType> AvailableDisplayTypes => IsRegister ? RegisterDisplayTypes : BoolDisplayTypes;

    public bool IsForceApplied => ForceEnabled && ForceValue.HasValue;

    public int EffectiveRawValue => IsForceApplied ? ForceValue!.Value : RawValue;

    public string ModbusLabel => FormatModbusLabel(ModbusType, ModbusAddress);

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

    public string DisplayValue
    {
        get
        {
            if (!IsRegister)
            {
                return EffectiveRawValue != 0 ? "ON" : "OFF";
            }

            var signed = unchecked((short)(ushort)EffectiveRawValue);
            return DisplayType switch
            {
                DisplayType.ScaledReal => ((double)signed / Math.Max(1, RealScale)).ToString("0.00", CultureInfo.InvariantCulture),
                _ => signed.ToString(CultureInfo.InvariantCulture),
            };
        }
    }

    public string RegisterIntegerToolTip
    {
        get
        {
            if (!IsRegister)
            {
                return string.Empty;
            }

            var signed = unchecked((short)(ushort)EffectiveRawValue);
            return $"Integer: {signed.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    public string FormatRawWithDisplay(int rawValue)
    {
        if (!IsRegister)
        {
            return rawValue == 0 ? "OFF" : "ON";
        }

        var normalized = unchecked((ushort)rawValue);
        var signed = unchecked((short)normalized);
        if (DisplayType != DisplayType.ScaledReal)
        {
            return signed.ToString(CultureInfo.InvariantCulture);
        }

        var display = ((double)signed / Math.Max(1, RealScale)).ToString("0.00", CultureInfo.InvariantCulture);
        return $"{signed} ({display})";
    }

    public string ForceSummary => ForceValue is null
        ? Loc.Text("ForceNone")
        : IsRegister
            ? Loc.Format("ForceValue", DisplayValue)
            : ForceValue.Value != 0 ? Loc.Text("ForceOn") : Loc.Text("ForceOff");

    public string LastWrittenText => LastWritten?.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture) ?? string.Empty;

    public MappingEntrySettings ToSettings()
    {
        return new MappingEntrySettings
        {
            ModbusType = ModbusType,
            ModbusAddress = ModbusAddress,
            PlcAddress = PlcAddress,
            DisplayType = NormalizeDisplayType(ModbusType, DisplayType),
            Comment = Comment,
        };
    }

    public static MappingEntry FromSettings(MappingEntrySettings settings, int realScale)
    {
        return new MappingEntry(settings.ModbusType, settings.ModbusAddress)
        {
            PlcAddress = settings.PlcAddress ?? string.Empty,
            DisplayType = NormalizeDisplayType(settings.ModbusType, settings.DisplayType),
            Comment = settings.Comment ?? string.Empty,
            RealScale = realScale,
        };
    }

    public static DataDirection GetDefaultDirection(ModbusType type)
    {
        return type is ModbusType.Coil or ModbusType.HoldingRegister
            ? DataDirection.ToPlc
            : DataDirection.FromPlc;
    }

    public static bool IsRegisterType(ModbusType type)
    {
        return type is ModbusType.HoldingRegister or ModbusType.InputRegister;
    }

    public static DisplayType NormalizeDisplayType(ModbusType type, DisplayType displayType)
    {
        if (!IsRegisterType(type))
        {
            return DisplayType.Bool;
        }

        return displayType == DisplayType.ScaledReal ? DisplayType.ScaledReal : DisplayType.Int16;
    }

    public void RefreshComputed()
    {
        if (IsRegister)
        {
            LedState = IsForceApplied ? LedState.ForceOn : EffectiveRawValue != 0 ? LedState.On : LedState.Off;
        }
        else
        {
            LedState = IsForceApplied
                ? ForceValue.GetValueOrDefault() != 0 ? LedState.ForceOn : LedState.ForceOff
                : RawValue != 0 ? LedState.On : LedState.Off;
        }

        OnPropertyChanged(nameof(IsBool));
        OnPropertyChanged(nameof(IsRegister));
        OnPropertyChanged(nameof(AvailableDisplayTypes));
        OnPropertyChanged(nameof(IsForceApplied));
        OnPropertyChanged(nameof(EffectiveRawValue));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(RegisterIntegerToolTip));
        OnPropertyChanged(nameof(ForceSummary));
        OnPropertyChanged(nameof(LastWrittenText));
    }

    partial void OnRawValueChanged(int value) => RefreshComputed();

    partial void OnForceValueChanged(int? value) => RefreshComputed();

    partial void OnForceEnabledChanged(bool value) => RefreshComputed();

    partial void OnDisplayTypeChanged(DisplayType value)
    {
        var normalized = NormalizeDisplayType(ModbusType, value);
        if (value != normalized)
        {
            DisplayType = normalized;
            return;
        }

        RefreshComputed();
    }

    partial void OnRealScaleChanged(int value) => RefreshComputed();

    partial void OnLastWrittenChanged(DateTime? value) => RefreshComputed();
}
