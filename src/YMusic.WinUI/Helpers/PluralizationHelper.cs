#nullable enable

using System;

namespace YMusic.WinUI.Helpers;

public static class PluralizationHelper
{
    private static bool IsEnglish
    {
        get
        {
            try
            {
                return App.Loc?.CurrentLanguage == "en";
            }
            catch
            {
                return false;
            }
        }
    }

    public static string FormatTrackCount(int count)
    {
        if (IsEnglish)
        {
            return count == 1 ? "1 track" : $"{count} tracks";
        }

        int rem100 = count % 100;
        int rem10 = count % 10;
        if (rem100 >= 11 && rem100 <= 19) return $"{count} треков";
        if (rem10 == 1) return $"{count} трек";
        if (rem10 >= 2 && rem10 <= 4) return $"{count} трека";
        return $"{count} треков";
    }

    public static string FormatDuration(TimeSpan span)
    {
        bool isEn = IsEnglish;
        if (span.Hours > 0)
        {
            return isEn
                ? $"{span.Hours} hr {span.Minutes} min"
                : $"{span.Hours} ч {span.Minutes} мин";
        }

        return isEn ? $"{span.Minutes} min" : $"{span.Minutes} мин";
    }
}
