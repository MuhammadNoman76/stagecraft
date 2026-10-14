using System.Globalization;
using System.Text.Json.Serialization;

namespace Stagecraft.Core.Model;

/// <summary>
/// Slide numbers, a date and footer text along the bottom of every slide, drawn in the theme's style. When slide
/// numbers are on they replace the numbers some themes draw themselves.
/// </summary>
public sealed class HeaderFooter
{
    public bool SlideNumber { get; set; }

    public bool Date { get; set; }

    /// <summary>True: <see cref="DateText"/> as typed. False: today's date, updated every time the deck is shown.</summary>
    public bool FixedDate { get; set; }

    public string? DateText { get; set; }

    /// <summary>The automatic date's format: "long" (14 October 2026), "short" (14/10/2026) or "iso" (2026-10-14).</summary>
    public string DateFormat { get; set; } = "long";

    /// <summary>Footer text (null or empty: none).</summary>
    public string? Text { get; set; }

    /// <summary>Not on slides with the Title layout.</summary>
    public bool HideOnTitle { get; set; } = true;

    [JsonIgnore]
    public bool Any => SlideNumber || Date || !string.IsNullOrWhiteSpace(Text);

    public HeaderFooter Clone() => new()
    {
        SlideNumber = SlideNumber,
        Date = Date,
        FixedDate = FixedDate,
        DateText = DateText,
        DateFormat = DateFormat,
        Text = Text,
        HideOnTitle = HideOnTitle,
    };

    /// <summary>Whether the footer shows on <paramref name="slide"/>.</summary>
    public bool ShowsOn(Slide slide) => Any && !(HideOnTitle && slide.Layout == Layouts.LayoutIds.Title);

    /// <summary>The date text for today (or the fixed text).</summary>
    public string DateFor(DateTime now, CultureInfo? culture = null)
    {
        if (FixedDate) return DateText ?? "";
        var c = culture ?? CultureInfo.CurrentCulture;
        switch (DateFormat)
        {
            case "short":
                return now.ToString("d", c);
            case "iso":
                return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            default:
            {
                // The culture's long date without the weekday ("14 October 2026", "October 14, 2026").
                var pattern = c.DateTimeFormat.LongDatePattern;
                foreach (var weekday in new[] { "dddd, ", "dddd ", ", dddd", " dddd", "dddd" }) pattern = pattern.Replace(weekday, "");
                return now.ToString(pattern.Trim(' ', ','), c);
            }
        }
    }
}

/// <summary>A guide line on the slide (vertical lines have an X position, horizontal ones a Y position).</summary>
public sealed class GuideLine
{
    public bool Vertical { get; set; }

    /// <summary>Slide units from the left (vertical) or the top (horizontal).</summary>
    public double Position { get; set; }

    public GuideLine Clone() => new() { Vertical = Vertical, Position = Position };
}
