using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class UserCenterDetailsFormatter
    {
        private static List<string> BuildDetailLines(
            AppServerAuthenticationState authenticationState,
            AppServerRateLimitsSnapshot rateLimitsSnapshot,
            AppServerAccountTokenUsageSnapshot tokenUsageSnapshot)
        {
            return BuildDetailLines(authenticationState, rateLimitsSnapshot, tokenUsageSnapshot, DateTime.Now);
        }

        internal static List<string> BuildDetailLines(
            AppServerAuthenticationState authenticationState,
            AppServerRateLimitsSnapshot rateLimitsSnapshot,
            AppServerAccountTokenUsageSnapshot tokenUsageSnapshot,
            DateTime now)
        {
            var lines = new List<string>();
            var state = authenticationState ?? new AppServerAuthenticationState();
            var isChatGptAuthActive = IsChatGptAuthActive(state);

            lines.Add(string.Format(
                UiLocalization.Text("UserCenterForm.Details.Email", "Email: {0}"),
                GetDisplayText(state.Email, UiLocalization.Text("UserCenterForm.Value.NotProvided", "(Not provided)"))));
            lines.Add(string.Format(
                UiLocalization.Text("UserCenterForm.Details.PlanType", "Plan: {0}"),
                GetDisplayText(GetPlanTypeForDisplay(state, rateLimitsSnapshot), UiLocalization.Text("UserCenterForm.Value.Unknown", "(Unknown)"))));

            var hasRateLimitLine = false;
            if (isChatGptAuthActive && rateLimitsSnapshot != null)
            {
                foreach (var bucket in BuildOrderedRateLimitBuckets(rateLimitsSnapshot.Buckets))
                {
                    foreach (var line in BuildRateLimitLines(bucket, now))
                    {
                        hasRateLimitLine = true;
                        lines.Add(line);
                    }
                }

                var countBeforeResetCreditExpiryLines = lines.Count;
                AddResetCreditExpiryLines(lines, rateLimitsSnapshot, now);
                hasRateLimitLine = hasRateLimitLine || lines.Count > countBeforeResetCreditExpiryLines;
            }

            if (!hasRateLimitLine)
            {
                lines.Add(UiLocalization.Text(
                    isChatGptAuthActive
                        ? "UserCenterForm.Details.NoRateLimitInfo"
                        : "UserCenterForm.Details.UnavailableForApi",
                    isChatGptAuthActive
                        ? "(No quota information yet)"
                        : "Current authentication is an API profile. User Center is only available for ChatGPT accounts."));
            }

            if (isChatGptAuthActive)
            {
                AddTokenUsageLines(lines, tokenUsageSnapshot);
            }

            return lines;
        }

        private static void AddTokenUsageLines(List<string> lines, AppServerAccountTokenUsageSnapshot usage)
        {
            if (lines == null || !HasTokenUsageData(usage))
            {
                return;
            }

            if (usage.LifetimeTokens.HasValue)
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.LifetimeTokens", "Lifetime tokens: {0}"),
                    FormatTokenCount(usage.LifetimeTokens.Value)));
            }

            if (usage.PeakDailyTokens.HasValue)
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.PeakDailyTokens", "Peak daily tokens: {0}"),
                    FormatTokenCount(usage.PeakDailyTokens.Value)));
            }

            if (usage.CurrentStreakDays.HasValue || usage.LongestStreakDays.HasValue)
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.UsageStreak", "Usage streak: current {0} days, longest {1} days"),
                    FormatNullableNumber(usage.CurrentStreakDays),
                    FormatNullableNumber(usage.LongestStreakDays)));
            }

            foreach (var bucket in GetRecentDailyBuckets(usage, 7))
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.DailyTokenUsage", "{0}: {1}"),
                    FormatUsageDate(bucket.StartDate),
                    FormatTokenCount(bucket.Tokens)));
            }
        }

        private static bool HasTokenUsageData(AppServerAccountTokenUsageSnapshot usage)
        {
            return usage != null &&
                   (usage.LifetimeTokens.HasValue ||
                    usage.PeakDailyTokens.HasValue ||
                    usage.CurrentStreakDays.HasValue ||
                    usage.LongestStreakDays.HasValue ||
                    (usage.DailyBuckets != null && usage.DailyBuckets.Count > 0));
        }

        private static IEnumerable<AppServerAccountTokenUsageDailyBucket> GetRecentDailyBuckets(
            AppServerAccountTokenUsageSnapshot usage,
            int limit)
        {
            var buckets = new List<AppServerAccountTokenUsageDailyBucket>();
            if (usage != null && usage.DailyBuckets != null)
            {
                foreach (var bucket in usage.DailyBuckets)
                {
                    if (bucket != null && !string.IsNullOrWhiteSpace(bucket.StartDate))
                    {
                        buckets.Add(bucket);
                    }
                }
            }

            buckets.Sort((left, right) => string.Compare(right.StartDate, left.StartDate, StringComparison.Ordinal));
            for (var i = 0; i < buckets.Count && i < limit; i++)
            {
                yield return buckets[i];
            }
        }

        private static string FormatTokenCount(long value)
        {
            return TokenCountFormatter.Format(value);
        }

        private static string FormatNullableNumber(long? value)
        {
            return value.HasValue
                ? value.Value.ToString("N0", CultureInfo.CurrentCulture)
                : UiLocalization.Text("UserCenterForm.Value.Unknown", "(Unknown)");
        }

        private static List<AppServerRateLimitBucket> BuildOrderedRateLimitBuckets(IEnumerable<AppServerRateLimitBucket> buckets)
        {
            var result = new List<AppServerRateLimitBucket>();
            foreach (var bucket in buckets ?? new AppServerRateLimitBucket[0])
            {
                if (bucket != null)
                {
                    result.Add(bucket);
                }
            }

            result.Sort(CompareRateLimitBucketsForDisplay);
            return result;
        }

        private static int CompareRateLimitBucketsForDisplay(AppServerRateLimitBucket left, AppServerRateLimitBucket right)
        {
            var leftRank = GetRateLimitBucketSortRank(left);
            var rightRank = GetRateLimitBucketSortRank(right);
            if (leftRank != rightRank)
            {
                return leftRank.CompareTo(rightRank);
            }

            var leftLabel = GetRateLimitBucketLabel(left);
            var rightLabel = GetRateLimitBucketLabel(right);
            var labelCompare = string.Compare(leftLabel, rightLabel, StringComparison.OrdinalIgnoreCase);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return string.Compare(left != null ? left.LimitId : null, right != null ? right.LimitId : null, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetRateLimitBucketSortRank(AppServerRateLimitBucket bucket)
        {
            var text = BuildRateLimitBucketSearchText(bucket);
            if (IsSparkRateLimitBucketText(text))
            {
                return 1;
            }

            if (IsPrimaryCodexRateLimitBucketText(text))
            {
                return 0;
            }

            if (text.IndexOf("fast", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("quick", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("5.3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("快速", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            if (text.IndexOf("general", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("default", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("common", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("shared", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("通用", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("gpt-5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("gpt 5", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 3;
            }

            return 4;
        }

        private static string BuildRateLimitBucketSearchText(AppServerRateLimitBucket bucket)
        {
            if (bucket == null)
            {
                return string.Empty;
            }

            return string.Join(
                " ",
                new[]
                {
                    bucket.LimitName ?? string.Empty,
                    bucket.LimitId ?? string.Empty
                });
        }

        private static bool IsSparkRateLimitBucketText(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   (text.IndexOf("spark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("5.3", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsPrimaryCodexRateLimitBucketText(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   (text.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("all model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("all-model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("all_model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("全模型", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool IsChatGptAuthActive(AppServerAuthenticationState state)
        {
            return state != null &&
                   state.IsAuthenticated &&
                   !string.Equals(state.AuthMode, "apikey", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPlanTypeForDisplay(AppServerAuthenticationState state, AppServerRateLimitsSnapshot rateLimitsSnapshot)
        {
            var planType = state != null ? state.PlanType : null;
            if (!string.IsNullOrWhiteSpace(planType))
            {
                return planType;
            }

            if (rateLimitsSnapshot != null)
            {
                foreach (var bucket in rateLimitsSnapshot.Buckets)
                {
                    if (!string.IsNullOrWhiteSpace(bucket.PlanType))
                    {
                        return bucket.PlanType;
                    }
                }
            }

            return null;
        }

        private static List<string> BuildRateLimitLines(AppServerRateLimitBucket bucket, DateTime now)
        {
            var lines = new List<string>();
            if (bucket == null)
            {
                return lines;
            }

            if (bucket.Primary != null)
            {
                lines.Add(FormatWindowSentence(bucket.Primary, true, GetRateLimitBucketDisplayPrefix(bucket), now));
            }

            if (bucket.Secondary != null)
            {
                lines.Add(FormatWindowSentence(bucket.Secondary, false, GetRateLimitBucketDisplayPrefix(bucket), now));
            }

            if (bucket.Credits != null)
            {
                lines.Add(string.Format(
                    UiLocalization.Text("UserCenterForm.Details.Credits", "Credits: {0}"),
                    FormatCredits(bucket.Credits)));
            }

            return lines;
        }

        private static string FormatWindowSentence(
            AppServerRateLimitWindowInfo window,
            bool isPrimary,
            string prefix,
            DateTime now)
        {
            if (window == null)
            {
                return UiLocalization.Text("UserCenterForm.Details.NoCreditsInfo", "(No quota information)");
            }

            var remainingPercent = 100M - window.UsedPercent;
            if (remainingPercent < 0M)
            {
                remainingPercent = 0M;
            }

            if (remainingPercent > 100M)
            {
                remainingPercent = 100M;
            }

            var label = GetWindowLabel(window.WindowDurationMinutes, isPrimary);
            var resetText = FormatResetTimeWithCountdown(window.ResetsAtLocalTime, now);
            var sentence = string.Format(
                UiLocalization.Text(
                    "UserCenterForm.Details.WindowSentence",
                    "{0} remaining {1:0.##}%, resets {2}"),
                label,
                remainingPercent,
                resetText);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return sentence;
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Details.WindowSentenceWithPrefix", "{0}: {1}"),
                prefix,
                sentence);
        }

        private static string FormatCredits(AppServerCreditsInfo credits)
        {
            if (credits == null)
            {
                return UiLocalization.Text("UserCenterForm.Details.NoCreditsInfo", "(No quota information)");
            }

            if (credits.Unlimited)
            {
                return UiLocalization.Text("UserCenterForm.Value.Unlimited", "Unlimited");
            }

            if (!credits.HasCredits)
            {
                return UiLocalization.Text("UserCenterForm.Value.Unavailable", "Unavailable");
            }

            if (string.IsNullOrWhiteSpace(credits.Balance))
            {
                return UiLocalization.Text("UserCenterForm.Value.Available", "Available");
            }

            return decimal.TryParse(credits.Balance, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance)
                ? decimal.Truncate(balance).ToString("0", CultureInfo.CurrentCulture)
                : credits.Balance;
        }

        private static string GetRateLimitBucketLabel(AppServerRateLimitBucket bucket)
        {
            if (bucket == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(bucket.LimitName))
            {
                return bucket.LimitName.Trim();
            }

            return BuildLimitIdDisplayName(bucket.LimitId);
        }

        private static string BuildLimitIdDisplayName(string limitId)
        {
            if (string.IsNullOrWhiteSpace(limitId))
            {
                return string.Empty;
            }

            var value = limitId.Trim();
            var colonIndex = value.LastIndexOf(':');
            if (colonIndex >= 0 && colonIndex < value.Length - 1)
            {
                value = value.Substring(colonIndex + 1);
            }

            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace('_', ' ');
        }

        private static string GetRateLimitBucketDisplayPrefix(AppServerRateLimitBucket bucket)
        {
            var text = BuildRateLimitBucketSearchText(bucket);
            if (IsSparkRateLimitBucketText(text))
            {
                return UiLocalization.Text("UserCenterForm.RateLimitBucket.Spark", "Spark");
            }

            return string.Empty;
        }

        private static string GetWindowLabel(int? durationMinutes, bool isPrimary)
        {
            if (!durationMinutes.HasValue || durationMinutes.Value <= 0)
            {
                return isPrimary
                    ? UiLocalization.Text("UserCenterForm.Window.PrimaryCycle", "Current cycle")
                    : UiLocalization.Text("UserCenterForm.Window.SecondaryCycle", "Additional cycle");
            }

            if (durationMinutes.Value == 60)
            {
                return UiLocalization.Text("UserCenterForm.Window.OneHour", "1 hour");
            }

            if (durationMinutes.Value == 300)
            {
                return UiLocalization.Text("UserCenterForm.Window.FiveHours", "5 hours");
            }

            if (durationMinutes.Value == 1440)
            {
                return UiLocalization.Text("UserCenterForm.Window.Daily", "Daily");
            }

            if (durationMinutes.Value == 10080)
            {
                return UiLocalization.Text("UserCenterForm.Window.Weekly", "Weekly");
            }

            if (durationMinutes.Value % 1440 == 0)
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Window.Days", "{0} days"),
                    durationMinutes.Value / 1440);
            }

            if (durationMinutes.Value % 60 == 0)
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Window.Hours", "{0} hours"),
                    durationMinutes.Value / 60);
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Window.Minutes", "{0} minutes"),
                durationMinutes.Value);
        }

        internal static string FormatResetTime(DateTime? time)
        {
            if (!time.HasValue)
            {
                return UiLocalization.Text("UserCenterForm.Time.Unknown", "Unknown time");
            }

            var value = time.Value;
            if (value.Minute == 0)
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Time.MonthDayHour", "{0}/{1} {2}:00"),
                    value.Month,
                    value.Day,
                    value.Hour);
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Time.MonthDayHourMinute", "{0}/{1} {2}:{3:00}"),
                value.Month,
                value.Day,
                value.Hour,
                value.Minute);
        }

        internal static string FormatResetTimeWithCountdown(DateTime? time, DateTime now)
        {
            var timeText = FormatResetTime(time);
            if (!time.HasValue)
            {
                return timeText;
            }

            var countdown = FormatCountdown(time.Value - now);
            if (string.IsNullOrWhiteSpace(countdown))
            {
                return timeText;
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Time.WithCountdown", "{0}, {1} later"),
                timeText,
                countdown);
        }

        private static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
            {
                return UiLocalization.Text("UserCenterForm.Time.Expired", "expired");
            }

            var totalMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
            var days = totalMinutes / (24 * 60);
            totalMinutes %= 24 * 60;
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            var parts = new List<string>();
            if (days > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("UserCenterForm.Time.CountdownDays", "{0} days"), days));
            }

            if (hours > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("UserCenterForm.Time.CountdownHours", "{0} hours"), hours));
            }

            if (minutes > 0 || parts.Count == 0)
            {
                parts.Add(string.Format(UiLocalization.Text("UserCenterForm.Time.CountdownMinutes", "{0} minutes"), minutes));
            }

            return string.Concat(parts.ToArray());
        }

        private static void AddResetCreditExpiryLines(List<string> lines, AppServerRateLimitsSnapshot snapshot, DateTime now)
        {
            if (lines == null || snapshot == null || snapshot.ResetCredits == null)
            {
                return;
            }

            var creditNumber = 0;
            foreach (var credit in snapshot.ResetCredits)
            {
                if (credit == null ||
                    !string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase) ||
                    !credit.ExpiresAtLocalTime.HasValue)
                {
                    continue;
                }

                creditNumber++;
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.ResetCreditExpires", "Reset credit {0} {1} expires"),
                    creditNumber,
                    FormatResetTimeWithCountdown(credit.ExpiresAtLocalTime, now)));
            }
        }

        private static string FormatUsageDate(string dateText)
        {
            DateTime value;
            if (!string.IsNullOrWhiteSpace(dateText) &&
                DateTime.TryParseExact(
                    dateText.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out value))
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Time.MonthDay", "{0}/{1}"),
                    value.Month,
                    value.Day);
            }

            return GetDisplayText(dateText, UiLocalization.Text("UserCenterForm.Time.Unknown", "Unknown time"));
        }

        internal static string GetDisplayText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

    }
}
