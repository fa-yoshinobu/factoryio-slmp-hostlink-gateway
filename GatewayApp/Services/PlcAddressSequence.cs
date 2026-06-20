using System.Globalization;

namespace GatewayApp.Services;

public enum PlcAddressDisplayRule
{
    Default,
    DecimalNoPadding,
    OctalNoPadding,
    KeyenceBitBank,
    KeyenceXymBit,
}

public sealed record PlcAddressSequenceRule(
    string Prefix,
    bool UsesHexAddressing,
    PlcAddressDisplayRule DisplayRule);

public static class PlcAddressSequence
{
    public static bool TryFormat(
        string plcProtocol,
        string slmpProfile,
        string prefix,
        string startNumberText,
        int offset,
        out string address,
        out string error)
    {
        address = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(prefix))
        {
            error = "PLC デバイスを選択してください。";
            return false;
        }

        var rule = ResolveRule(plcProtocol, slmpProfile, prefix.Trim());
        if (!TryParseStartNumber(startNumberText, rule, out var startNumber))
        {
            error = $"{rule.Prefix} の開始番号が不正です。";
            return false;
        }

        if (rule.DisplayRule == PlcAddressDisplayRule.KeyenceBitBank && startNumber % 100 > 15)
        {
            error = $"{rule.Prefix} の下2桁は 00..15 で指定してください。";
            return false;
        }

        var startLogical = ToLogicalNumber(startNumber, rule);
        var nextLogical = (long)startLogical + offset;
        if (nextLogical < 0 || nextLogical > uint.MaxValue)
        {
            error = "PLC アドレスが範囲外です。";
            return false;
        }

        var nextPhysical = FromLogicalNumber((uint)nextLogical, rule);
        address = Format(rule, nextPhysical, startNumberText.Trim().Length);
        return true;
    }

    private static PlcAddressSequenceRule ResolveRule(string plcProtocol, string slmpProfile, string prefix)
    {
        var normalized = prefix.ToUpperInvariant();
        if (plcProtocol.Equals("HostLink", StringComparison.OrdinalIgnoreCase))
        {
            return normalized switch
            {
                "R" or "MR" or "LR" or "CR" => new(normalized, false, PlcAddressDisplayRule.KeyenceBitBank),
                "X" or "Y" => new(normalized, false, PlcAddressDisplayRule.KeyenceXymBit),
                "B" or "VB" or "W" => new(normalized, true, PlcAddressDisplayRule.Default),
                _ => new(normalized, false, PlcAddressDisplayRule.Default),
            };
        }

        if ((normalized is "X" or "Y") && IsIqFSlmpProfile(slmpProfile))
        {
            return new(normalized, false, PlcAddressDisplayRule.OctalNoPadding);
        }

        return normalized switch
        {
            "X" or "Y" or "B" or "SB" or "W" or "SW" or "DX" or "DY" => new(normalized, true, PlcAddressDisplayRule.Default),
            "M" or "L" or "F" or "V" or "SM" or "D" or "R" or "ZR" or "RD" or "SD" => new(normalized, false, PlcAddressDisplayRule.DecimalNoPadding),
            _ => new(normalized, false, PlcAddressDisplayRule.Default),
        };
    }

    private static bool TryParseStartNumber(string text, PlcAddressSequenceRule rule, out uint number)
    {
        number = 0;
        var token = text.Trim().ToUpperInvariant();
        if (token.Length == 0)
        {
            return false;
        }

        return rule.DisplayRule switch
        {
            PlcAddressDisplayRule.KeyenceXymBit => TryParseKeyenceXymBitNumber(token, out number),
            PlcAddressDisplayRule.OctalNoPadding => TryParseOctalNumber(token, out number),
            _ => TryParseNumber(token, rule.UsesHexAddressing, out number),
        };
    }

    private static bool TryParseNumber(string token, bool usesHexAddressing, out uint number)
    {
        number = 0;
        var style = usesHexAddressing ? NumberStyles.HexNumber : NumberStyles.None;
        return token.All(character => IsNumberCharacter(character, usesHexAddressing))
            && uint.TryParse(token, style, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryParseOctalNumber(string token, out uint number)
    {
        number = 0;
        foreach (var character in token)
        {
            if (character is < '0' or > '7')
            {
                return false;
            }

            var digit = (uint)(character - '0');
            if (number > (uint.MaxValue - digit) / 8)
            {
                return false;
            }

            number = (number * 8) + digit;
        }

        return token.Length > 0;
    }

    private static bool TryParseKeyenceXymBitNumber(string token, out uint number)
    {
        number = 0;
        var bankText = token.Length == 1 ? "0" : token[..^1];
        var bitText = token[^1..];
        if (!bankText.All(character => character is >= '0' and <= '9'))
        {
            return false;
        }

        if (!uint.TryParse(bankText, NumberStyles.None, CultureInfo.InvariantCulture, out var bank)
            || !uint.TryParse(bitText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bit)
            || bit > 15)
        {
            return false;
        }

        number = checked((bank * 16) + bit);
        return true;
    }

    private static uint ToLogicalNumber(uint physicalNumber, PlcAddressSequenceRule rule) =>
        rule.DisplayRule == PlcAddressDisplayRule.KeyenceBitBank
            ? checked(((physicalNumber / 100) * 16) + (physicalNumber % 100))
            : physicalNumber;

    private static uint FromLogicalNumber(uint logicalNumber, PlcAddressSequenceRule rule) =>
        rule.DisplayRule == PlcAddressDisplayRule.KeyenceBitBank
            ? checked(((logicalNumber / 16) * 100) + (logicalNumber % 16))
            : logicalNumber;

    private static string Format(PlcAddressSequenceRule rule, uint physicalNumber, int width)
    {
        return rule.DisplayRule switch
        {
            PlcAddressDisplayRule.KeyenceBitBank => $"{rule.Prefix}{FormatKeyenceBitBankNumber(physicalNumber)}",
            PlcAddressDisplayRule.KeyenceXymBit => $"{rule.Prefix}{FormatKeyenceXymBitNumber(physicalNumber)}",
            PlcAddressDisplayRule.DecimalNoPadding => $"{rule.Prefix}{physicalNumber.ToString(CultureInfo.InvariantCulture)}",
            PlcAddressDisplayRule.OctalNoPadding => $"{rule.Prefix}{Convert.ToString(physicalNumber, 8).ToUpperInvariant()}",
            _ when rule.UsesHexAddressing => $"{rule.Prefix}{physicalNumber.ToString($"X{Math.Max(1, width)}", CultureInfo.InvariantCulture)}",
            _ => $"{rule.Prefix}{physicalNumber.ToString($"D{Math.Max(1, width)}", CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatKeyenceBitBankNumber(uint physicalNumber)
    {
        var bank = physicalNumber / 100;
        var bit = physicalNumber % 100;
        return bank.ToString(CultureInfo.InvariantCulture) + bit.ToString("D2", CultureInfo.InvariantCulture);
    }

    private static string FormatKeyenceXymBitNumber(uint logicalNumber)
    {
        var bank = logicalNumber / 16;
        var bit = logicalNumber % 16;
        return bank.ToString(CultureInfo.InvariantCulture) + bit.ToString("X", CultureInfo.InvariantCulture);
    }

    private static bool IsNumberCharacter(char character, bool usesHexAddressing) =>
        usesHexAddressing
            ? character is >= '0' and <= '9' or >= 'A' and <= 'F'
            : character is >= '0' and <= '9';

    private static bool IsIqFSlmpProfile(string slmpProfile) =>
        string.Equals(slmpProfile.Trim(), "melsec:iq-f", StringComparison.OrdinalIgnoreCase);
}
