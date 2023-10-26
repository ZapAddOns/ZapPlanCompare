namespace ZapPlanCompare.Extensions
{
    public static class ColorExtensions
    {
        public static System.Drawing.Color ToColor(this System.Windows.Media.Color color)
        {
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}
