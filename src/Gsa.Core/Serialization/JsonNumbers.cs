using System.Globalization;
using System.Text.Json;

namespace Gsa.Core.Serialization;

/// <summary>Exact integer conversion of JSON number lexemes, without floating-point rounding.</summary>
public static class JsonNumbers
{
    public static bool TryGetInt64(JsonElement value, out long result)
    {
        result = 0;
        if (value.ValueKind != JsonValueKind.Number)
            return false;

        // JsonElement has already checked JSON number grammar. Keep the decimal digits
        // exact: decimal/double conversion could round a small fractional part to zero.
        var raw = value.GetRawText().AsSpan();
        var negative = raw[0] == '-';
        if (negative)
            raw = raw[1..];
        var exponentIndex = raw.IndexOfAny('e', 'E');
        var mantissa = exponentIndex >= 0 ? raw[..exponentIndex] : raw;
        var dotIndex = mantissa.IndexOf('.');
        var fractionDigits = dotIndex >= 0 ? mantissa.Length - dotIndex - 1 : 0;
        var digits = mantissa.ToString().Replace(".", "", StringComparison.Ordinal).TrimStart('0');

        // Zero remains zero even with an exponent outside Int32's range.
        if (digits.Length == 0)
            return true;

        var exponent = 0;
        if (exponentIndex >= 0 && !int.TryParse(raw[(exponentIndex + 1)..],
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            return false;

        var scale = (long)exponent - fractionDigits;
        if (scale < 0)
        {
            var trailingDigits = -scale;
            if (trailingDigits >= digits.Length)
                return false;
            var integerLength = digits.Length - (int)trailingDigits;
            for (var index = integerLength; index < digits.Length; index++)
                if (digits[index] != '0')
                    return false;
            digits = digits[..integerLength];
        }
        else
        {
            // An Int64 magnitude has at most 19 digits. Check before allocating zeros,
            // so a huge positive exponent cannot cause a huge allocation.
            if (digits.Length + scale > 19)
                return false;
            digits += new string('0', (int)scale);
        }

        if (digits.Length > 19)
            return false;
        return long.TryParse(negative ? "-" + digits : digits, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out result);
    }
}
