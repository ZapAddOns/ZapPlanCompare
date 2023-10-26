using System;

namespace ZapPlanCompare.Extensions
{
    public static class StringExtensions
    {
        public static string RightAlign(this string text, int width)
        {
            var result = " " + text.Substring(0, Math.Min(text.Length, width - 2)) + " ";
            result = result.PadLeft(width);
            return result;
        }

        public static string LeftAlign(this string text, int width)
        {
            var result = " " + text.Substring(0, Math.Min(text.Length, width - 2)) + " ";
            result = result.PadRight(width);
            return result;
        }

        public static string CenterAlign(this string text, int width)
        {
            var result = " " + text.Substring(0, Math.Min(text.Length, width - 2)) + " ";
            result = new string(' ', Math.Max(0, (width - result.Length) / 2)) + result;
            result = result.PadRight(width);
            return result;
        }
    }
}
