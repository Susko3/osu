// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace osu.Game.Localisation
{
    public sealed class OsuFormatProvider : IFormatProvider, ICustomFormatter
    {
        private readonly CultureInfo culture;

        private readonly bool prefer24HourTime;

        private readonly string shortYearMonth;
        private readonly string shortMonthDay;
        private readonly string weekdaylessShorterDate;
        private readonly string weekdaylessLongDate;

        public OsuFormatProvider(CultureInfo culture, bool prefer24HourTime)
        {
            this.culture = culture;
            this.prefer24HourTime = prefer24HourTime;

            customizeDateTimeFormat(culture.DateTimeFormat);

            shortYearMonth = culture.DateTimeFormat.YearMonthPattern.Replace(@"MMMM", @"MMM");
            shortMonthDay = culture.DateTimeFormat.MonthDayPattern.Replace(@"MMMM", @"MMM");
            weekdaylessLongDate = getLongDateWithoutDayOfWeek(culture.DateTimeFormat);
            weekdaylessShorterDate = weekdaylessLongDate.Replace(@"MMMM", @"MMM");
        }

        public object GetFormat(Type formatType)
        {
            if (formatType == typeof(ICustomFormatter))
                return this;

            return culture;
        }

        public string Format(string format, object? arg, IFormatProvider formatProvider)
        {
            if (arg is DateTime dateTime && format.Length == 0)
            {
                switch (format[0])
                {
                    case 'y':
                        return dateTime.ToString(shortYearMonth, culture);

                    case 'm':
                        return dateTime.ToString(shortMonthDay, culture);

                    case 'A': // %D without day of week
                        return dateTime.ToString(weekdaylessLongDate, culture);

                    case 'a': // $d without day of week
                        return dateTime.ToString(weekdaylessShorterDate, culture);
                }
            }

            if (arg is IFormattable iFormattable)
                return iFormattable.ToString(format, culture);

            return arg?.ToString() ?? string.Empty;
        }

        public override string ToString() => $@"{nameof(OsuFormatProvider)}(Culture={culture}, Prefer24HourTime={prefer24HourTime})";

        private static string getLongDateWithoutDayOfWeek(DateTimeFormatInfo dateTimeFormat)
        {
            if (dateTimeFormat.LongDatePattern.Contains(@"ddd"))
            {
                return dateTimeFormat.GetAllDateTimePatterns('D').FirstOrDefault(f => !f.Contains(@"ddd"))
                       ?? removeDayOfWeek(dateTimeFormat.LongDatePattern); // note that for windows languages, this will _never_ be hit.
            }

            return dateTimeFormat.LongDatePattern;
        }

        private static string removeDayOfWeek(string dateFormat)
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

            return sb.ToString();
        }

        private void customizeDateTimeFormat(DateTimeFormatInfo dateTimeFormat)
        {
            if (is24HourTime(dateTimeFormat.ShortTimePattern) != prefer24HourTime)
            {
                if (prefer24HourTime)
                {
                    dateTimeFormat.LongTimePattern = dateTimeFormat.GetAllDateTimePatterns('T').FirstOrDefault(is24HourTime) ?? convertTo24Hour(dateTimeFormat.LongTimePattern);
                    dateTimeFormat.ShortTimePattern = dateTimeFormat.GetAllDateTimePatterns('t').FirstOrDefault(is24HourTime) ?? convertTo24Hour(dateTimeFormat.ShortTimePattern);
                }
                else
                {
                    bool has12HourDesignators = !string.IsNullOrEmpty(dateTimeFormat.AMDesignator) && !string.IsNullOrEmpty(dateTimeFormat.PMDesignator);

                    dateTimeFormat.LongTimePattern = dateTimeFormat.GetAllDateTimePatterns('T').FirstOrDefault(s => !is24HourTime(s))
                                                     ?? convertTo12Hour(dateTimeFormat.LongTimePattern, has12HourDesignators);
                    dateTimeFormat.ShortTimePattern = dateTimeFormat.GetAllDateTimePatterns('t').FirstOrDefault(s => !is24HourTime(s))
                                                      ?? convertTo12Hour(dateTimeFormat.ShortTimePattern, has12HourDesignators);
                }
            }
        }

        private bool is24HourTime(string timeFormat) => !enumerateNonQuotedParts(timeFormat).Any(s => s.Contains('t') || s.Contains('h'));

        private static string convertTo24Hour(string timeFormat)
        {
            return editNonQuotedParts(timeFormat, s
                => s.Replace('h', 'H')
                    .Replace(@" tt ", string.Empty)
                    .Replace(@"tt ", string.Empty)
                    .Replace(@" tt", string.Empty)
                    .Replace(@"t", string.Empty)).ToString();
        }

        private static string convertTo12Hour(string timeFormat, bool has12HourDesignators)
        {
            var result = editNonQuotedParts(timeFormat, s
                => s.Replace('H', 'h'));

            if (has12HourDesignators)
                result.Append(@" tt");

            return result.ToString();
        }

        private static IEnumerable<string> enumerateNonQuotedParts(string format)
        {
            StringBuilder buffer = new StringBuilder();

            bool inQuote = false;
            char quote = '\'';

            for (int i = 0; i < format.Length; i++)
            {
                switch (format[i])
                {
                    case '\'':
                    case '\"':
                        if (inQuote && (quote == format[i]))
                        {
                            // we were in a quote and found a matching exit quote, so we are outside a quote now
                            inQuote = false;
                        }
                        else if (!inQuote)
                        {
                            // entered a quote, need to flush the unquoted buffer.
                            yield return buffer.ToString();

                            buffer.Clear();

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
                            buffer.Append(format[i]);
                        break;
                }
            }

            if (buffer.Length > 0)
                yield return buffer.ToString();
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
                        if (inQuote && (quote == format[i]))
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
                        break;
                }
            }

            if (buffer.Length > 0)
                final.Append(editor(buffer));

            return final;
        }
    }
}
