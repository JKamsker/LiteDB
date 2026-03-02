using System;
using System.Globalization;
using LiteDB;

namespace LiteDB.Vector.Utils
{
    internal static class VectorMetricParser
    {
        internal static bool TryParseOptional(BsonValue candidate, out VectorDistanceMetric? metric)
        {
            metric = null;

            if (candidate == null || candidate.IsNull)
            {
                return true;
            }

            if (TryParse(candidate, out var parsed))
            {
                metric = parsed;
                return true;
            }

            return false;
        }

        internal static bool TryParse(BsonValue candidate, out VectorDistanceMetric metric)
        {
            metric = default;

            if (candidate == null || candidate.IsNull)
            {
                return false;
            }

            if (candidate.IsNumber)
            {
                if (TryParseIntegralByte(candidate, out var numeric) &&
                    Enum.IsDefined(typeof(VectorDistanceMetric), numeric))
                {
                    metric = (VectorDistanceMetric)numeric;
                    return true;
                }

                return false;
            }

            if (candidate.IsString)
            {
                return TryParseString(candidate.AsString, out metric);
            }

            return false;
        }

        internal static bool TryParseString(string token, out VectorDistanceMetric metric)
        {
            metric = default;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var trimmed = token.Trim();

            if (TryParseIntegralByte(trimmed, out var numeric) &&
                Enum.IsDefined(typeof(VectorDistanceMetric), numeric))
            {
                metric = (VectorDistanceMetric)numeric;
                return true;
            }

            if (trimmed.Length > 0 &&
                (char.IsDigit(trimmed[0]) || trimmed[0] == '+' || trimmed[0] == '-'))
            {
                return false;
            }

            var lastSegment = trimmed;
            var lastDotIndex = trimmed.LastIndexOf('.');

            if (lastDotIndex >= 0 && lastDotIndex < trimmed.Length - 1)
            {
                lastSegment = trimmed.Substring(lastDotIndex + 1);
            }

            if (lastSegment.Length > 0 &&
                (char.IsDigit(lastSegment[0]) || lastSegment[0] == '+' || lastSegment[0] == '-'))
            {
                return false;
            }

            if (Enum.TryParse<VectorDistanceMetric>(lastSegment, true, out var parsed) &&
                Enum.IsDefined(typeof(VectorDistanceMetric), parsed))
            {
                metric = parsed;
                return true;
            }

            return false;
        }

        private static bool TryParseIntegralByte(BsonValue value, out byte result)
        {
            result = default;

            if (value == null || value.IsNull || !value.IsNumber)
            {
                return false;
            }

            if (value.IsInt32)
            {
                var number = value.AsInt32;

                if (number < byte.MinValue || number > byte.MaxValue)
                {
                    return false;
                }

                result = (byte)number;
                return true;
            }

            if (value.IsInt64)
            {
                var number = value.AsInt64;

                if (number < byte.MinValue || number > byte.MaxValue)
                {
                    return false;
                }

                result = (byte)number;
                return true;
            }

            if (value.IsDouble)
            {
                double number;

                try
                {
                    number = value.AsDouble;
                }
                catch
                {
                    return false;
                }

                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    return false;
                }

                if (number < byte.MinValue || number > byte.MaxValue)
                {
                    return false;
                }

                if (Math.Truncate(number) != number)
                {
                    return false;
                }

                result = (byte)number;
                return true;
            }

            if (value.IsDecimal)
            {
                decimal number;

                try
                {
                    number = value.AsDecimal;
                }
                catch
                {
                    return false;
                }

                if (number < byte.MinValue || number > byte.MaxValue)
                {
                    return false;
                }

                if (decimal.Truncate(number) != number)
                {
                    return false;
                }

                result = (byte)number;
                return true;
            }

            return false;
        }

        private static bool TryParseIntegralByte(string token, out byte result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                return false;
            }

            if (number < byte.MinValue || number > byte.MaxValue)
            {
                return false;
            }

            if (Math.Truncate(number) != number)
            {
                return false;
            }

            result = (byte)number;
            return true;
        }
    }
}
