using ZapClient.Data;

namespace ZapPlanCompare.Extensions
{
    public static class VOIContourExtensions
    {
        public static byte[] ColorAsArray(this VOIContour contour, CompareConfig config)
        {
            var result = new byte[4];

            if (contour.Color != null)
            {
                // Return a ARGB
                result[0] = (byte)contour.Color[3];
                result[1] = (byte)contour.Color[0];
                result[2] = (byte)contour.Color[1];
                result[3] = (byte)contour.Color[2];
            }

            var replace = config.GetColorForStructure(contour.Name);

            if (replace != null)
                return replace;

            return result;
        }
    }
}
