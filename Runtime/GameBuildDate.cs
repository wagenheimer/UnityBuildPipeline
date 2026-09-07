using System;

[Serializable]
public class GameBuildDate
{
    public int Day;
    public int Month;
    public int Year;

    public GameBuildDate()
    {
        var now = DateTime.Now;
        Day = now.Day;
        Month = now.Month;
        Year = now.Year;
    }

    public GameBuildDate(DateTime date)
    {
        Day = date.Day;
        Month = date.Month;
        Year = date.Year;
    }

    public string AsText => $"{MonthText(Month)}-{Day}-{Year}";
    public string AsTextNoYear => $"{MonthText(Month)}{Day}";

    public static string MonthText(int month) => month switch
    {
        1 => "Jan",
        2 => "Feb",
        3 => "Mar",
        4 => "Apr",
        5 => "May",
        6 => "Jun",
        7 => "Jul",
        8 => "Aug",
        9 => "Sep",
        10 => "Oct",
        11 => "Nov",
        12 => "Dec",
        _ => ""
    };

    public override string ToString() => AsText;
}
