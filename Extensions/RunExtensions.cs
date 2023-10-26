using System.Windows.Documents;

namespace ZapPlanCompare.Extensions
{
    public static class RunExtensions
    {
        // System.Windows.Media.Color

        public static Run Background(this Run run, System.Windows.Media.Color color)
        {
            run.Background = new System.Windows.Media.SolidColorBrush(color);
            return run;
        }

        public static Run Foreground(this Run run, System.Windows.Media.Color color)
        {
            run.Foreground = new System.Windows.Media.SolidColorBrush(color);
            return run;
        }

        public static Run Highlight(this Run run, bool flag, System.Windows.Media.Color? colorForeground = null, System.Windows.Media.Color? colorBackground = null)
        {
            if (!flag)
                return run;

            if (colorForeground != null)
                run.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)colorForeground);
            if (colorBackground != null)
                run.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)colorBackground);

            return run;
        }

        // SolidColorBrush

        public static Run Background(this Run run, System.Windows.Media.SolidColorBrush color)
        {
            run.Background = color;
            return run;
        }

        public static Run Foreground(this Run run, System.Windows.Media.SolidColorBrush color)
        {
            run.Foreground = color;
            return run;
        }

        public static Run Highlight(this Run run, bool flag, System.Windows.Media.SolidColorBrush colorForeground = null, System.Windows.Media.SolidColorBrush colorBackground = null)
        {
            if (!flag)
                return run;

            if (colorForeground != null)
                run.Foreground = colorForeground;
            if (colorBackground != null)
                run.Background = colorBackground;

            return run;
        }

        // byte[]
        public static Run Highlight(this Run run, bool flag, byte[] colorForeground = null, byte[] colorBackground = null)
        {
            if (!flag)
                return run;

            if (colorForeground != null && colorForeground.Length == 4)
                run.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(colorForeground[0], colorForeground[1], colorForeground[2], colorForeground[3]));
            if (colorBackground != null && colorBackground.Length == 4)
                run.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(colorBackground[0], colorBackground[1], colorBackground[2], colorBackground[3]));

            return run;
        }
    }
}
