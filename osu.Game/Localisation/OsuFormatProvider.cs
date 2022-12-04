// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Logger = osu.Framework.Logging.Logger;

namespace osu.Game.Localisation
{
    public class OsuFormatProvider : IFormatProvider, ICustomFormatter
    {
        private readonly CultureInfo culture;

        private readonly bool prefer24HourTime;

        private readonly string shortYearMonth;
        private readonly string shortMonthDay;
        private readonly string abbreviatedLongDate;

        private static readonly DateTimeOffset sample_date_time = new DateTimeOffset(2020, 4, 9, 19, 27, 3, new TimeSpan(1, 30, 0));

        public OsuFormatProvider(CultureInfo culture, bool prefer24HourTime)
        {
            this.culture = culture;
            this.prefer24HourTime = prefer24HourTime;

            customizeDateTimeFormat(culture.DateTimeFormat);

            shortYearMonth = culture.DateTimeFormat.YearMonthPattern.Replace(@"MMMM", @"MMM");
            shortMonthDay = culture.DateTimeFormat.MonthDayPattern.Replace(@"MMMM", @"MMM");
            abbreviatedLongDate = culture.DateTimeFormat.LongDatePattern.Replace(@"MMMM", @"MMM");
        }

        object? IFormatProvider.GetFormat(Type? formatType)
        {
            if (formatType == typeof(ICustomFormatter))
                return this;

            return culture.GetFormat(formatType);
        }

        string ICustomFormatter.Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            Debug.Assert(ReferenceEquals(formatProvider, this));

            if (arg is DateTimeOffset dateTime && format?.Length == 1)
            {
                switch (format[0])
                {
                    case 'y':
                        return dateTime.ToString(shortYearMonth, culture);

                    case 'm':
                        return dateTime.ToString(shortMonthDay, culture);

                    case 'A':
                        return dateTime.ToString(abbreviatedLongDate, culture);
                }
            }

            if (arg is IFormattable iFormattable)
                return iFormattable.ToString(format, culture);

            return arg?.ToString() ?? string.Empty;
        }

        public override string ToString() => $@"{nameof(OsuFormatProvider)}(Culture={culture}, Prefer24HourTime={prefer24HourTime})";

        private void customizeDateTimeFormat(DateTimeFormatInfo dateTimeFormat)
        {
            bool has12HourDesignators = !string.IsNullOrEmpty(dateTimeFormat.AMDesignator) && !string.IsNullOrEmpty(dateTimeFormat.PMDesignator);

            if (is24HourTime(dateTimeFormat.LongTimePattern) != prefer24HourTime)
            {
                dateTimeFormat.LongTimePattern = prefer24HourTime
                    ? dateTimeFormat.GetAllDateTimePatterns('T').FirstOrDefault(is24HourTime) ?? convertTo24Hour(dateTimeFormat.LongTimePattern)
                    : dateTimeFormat.GetAllDateTimePatterns('T').FirstOrDefault(is12HourTime) ?? convertTo12Hour(dateTimeFormat.LongTimePattern, has12HourDesignators);
            }

            if (is24HourTime(dateTimeFormat.ShortTimePattern) != prefer24HourTime)
            {
                dateTimeFormat.ShortTimePattern = prefer24HourTime
                    ? dateTimeFormat.GetAllDateTimePatterns('t').FirstOrDefault(is24HourTime) ?? convertTo24Hour(dateTimeFormat.ShortTimePattern)
                    : dateTimeFormat.GetAllDateTimePatterns('t').FirstOrDefault(is12HourTime) ?? convertTo12Hour(dateTimeFormat.ShortTimePattern, has12HourDesignators);
            }

            if (dateTimeFormat.LongDatePattern.Contains(@"ddd"))
            {
                dateTimeFormat.LongDatePattern = dateTimeFormat.GetAllDateTimePatterns('D').FirstOrDefault(f => !f.Contains(@"ddd"))
                                                 ?? removeDayOfWeek(dateTimeFormat.LongDatePattern); // note that for windows languages, this will _never_ be hit.
            }
        }

        private string removeDayOfWeek(string dateFormat)
        {
            var sb = new StringBuilder(dateFormat);

            foreach (string separator in new[] { ", ", " ", "" })
            {
                foreach (string weekdayPattern in new[] { @"dddd", @"ddd" })
                {
                    sb.Replace(@$"{weekdayPattern}{separator}", string.Empty)
                      .Replace(@$"{separator}{weekdayPattern}", string.Empty);
                }
            }

            logFormatChange(@"removing day of week", dateFormat, sb.ToString());
            return sb.ToString();
        }

        private static bool is24HourTime(string timeFormat) => !enumerateNonQuotedParts(timeFormat).Any(s => s.Contains('h'));

        private static bool is12HourTime(string timeFormat) => !is24HourTime(timeFormat);

        private string convertTo24Hour(string timeFormat)
        {
            string newFormat = editNonQuotedParts(timeFormat, part
                    => part.Replace(@"hh", @"HH")
                           .Replace(@"h", @"HH")
                           .Replace(@"tt ", string.Empty) // for ko-KR
                           .Replace(@" tt", string.Empty)
                           .Replace(@"t ", string.Empty)
                           .Replace(@" t", string.Empty)
                           .Replace(@"t", string.Empty)) // for zh-* (these have "tt", but "t" is used to cover the more general case)
                .ToString();

            logFormatChange("converting to 24 hour time", timeFormat, newFormat);
            return newFormat;
        }

        private string convertTo12Hour(string timeFormat, bool has12HourDesignators)
        {
            var result = editNonQuotedParts(timeFormat, part
                => part.Replace(@"HH", @"h")
                       .Replace(@"H", @"h"));

            if (has12HourDesignators)
            {
                if (culture.Name.StartsWith(@"ja", StringComparison.OrdinalIgnoreCase))
                    result.Insert(0, @"tt");
                else
                    result.Append(@" tt");
            }

            logFormatChange(@"converting to 12 hour time", timeFormat, result.ToString());
            return result.ToString();
        }

        private static IEnumerable<string> enumerateNonQuotedParts(string format)
        {
            StringBuilder nonQuotedBuffer = new StringBuilder();

            bool inQuote = false;
            char quote = '\'';

            for (int i = 0; i < format.Length; i++)
            {
                switch (format[i])
                {
                    case '\'':
                    case '\"':
                        if (inQuote && quote == format[i])
                        {
                            // we were in a quote and found a matching exit quote, so we are outside a quote now
                            inQuote = false;
                        }
                        else if (!inQuote)
                        {
                            // entered a quote, need to flush the unquoted buffer.
                            yield return nonQuotedBuffer.ToString();

                            nonQuotedBuffer.Clear();

                            quote = format[i];
                            inQuote = true;
                        }
                        else
                        {
                            // we were in a quote and saw the other type of quote character, so we are still in a quote
                        }

                        break;

                    case '%':
                    case '\\':
                        i++; // skip next character that is escaped by this backslash
                        break;

                    default:
                        if (!inQuote)
                            nonQuotedBuffer.Append(format[i]);
                        break;
                }
            }

            if (nonQuotedBuffer.Length > 0)
                yield return nonQuotedBuffer.ToString();
        }

        private static StringBuilder editNonQuotedParts(string format, Func<StringBuilder, StringBuilder> editor)
        {
            StringBuilder final = new StringBuilder();
            StringBuilder buffer = new StringBuilder();

            bool inQuote = false;
            char quote = '\'';

            for (int i = 0; i < format.Length; i++)
            {
                switch (format[i])
                {
                    case '\'':
                    case '\"':
                        if (inQuote && quote == format[i])
                        {
                            // we were in a quote and found a matching exit quote, so we are outside a quote now
                            inQuote = false;
                        }
                        else if (!inQuote)
                        {
                            // entered a quote, need to flush the unquoted buffer.
                            final.Append(editor(buffer));
                            buffer.Clear();

                            quote = format[i];
                            inQuote = true;
                        }
                        else
                        {
                            // we were in a quote and saw the other type of quote character, so we are still in a quote
                        }

                        final.Append(format[i]);

                        break;

                    case '%':
                    case '\\':
                        final.Append(format[i]);
                        i++; // skip next character that is escaped by this backslash
                        final.Append(format[i]);
                        break;

                    default:
                        if (!inQuote)
                            buffer.Append(format[i]);
                        else
                            final.Append(format[i]);
                        break;
                }
            }

            if (buffer.Length > 0)
                final.Append(editor(buffer));

            return final;
        }

        private void logFormatChange(string message, string oldFormat, string newFormat)
        {
            Logger.Log($"{ToString()}: {message} - '{oldFormat}' -> '{newFormat}', '{sample_date_time.ToString(oldFormat, culture)}' -> '{sample_date_time.ToString(newFormat, culture)}'");
        }
    }
}
