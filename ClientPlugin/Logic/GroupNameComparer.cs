using System;
using System.Collections.Generic;

namespace ClientPlugin.Logic;

// Group names in the order a player expects: ignoring case, and numbers by value, so
// "Group 2" comes before "Group 10". Names equal that way keep a stable ordinal order.
public class GroupNameComparer : IComparer<string>
{
    public static readonly GroupNameComparer Instance = new GroupNameComparer();

    public int Compare(string x, string y)
    {
        x ??= "";
        y ??= "";
        int i = 0,
            j = 0;
        while (i < x.Length && j < y.Length)
        {
            var digits = char.IsDigit(x[i]) && char.IsDigit(y[j]);
            var endX = Run(x, i, digits);
            var endY = Run(y, j, digits);
            var result = digits
                ? CompareNumbers(x.Substring(i, endX - i), y.Substring(j, endY - j))
                : string.Compare(
                    x.Substring(i, endX - i),
                    y.Substring(j, endY - j),
                    StringComparison.CurrentCultureIgnoreCase
                );
            if (result != 0)
                return result;
            i = endX;
            j = endY;
        }

        var rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(x, y);
    }

    // The end of the run of digits, or of other characters, starting at the index
    private static int Run(string s, int start, bool digits)
    {
        var end = start + 1;
        while (end < s.Length && char.IsDigit(s[end]) == digits)
            end++;
        return end;
    }

    // By value without parsing, so any number of digits works
    private static int CompareNumbers(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        var length = a.Length.CompareTo(b.Length);
        return length != 0 ? length : string.CompareOrdinal(a, b);
    }
}
