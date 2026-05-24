using System;

namespace DAoCLogWatcher.UI.Helpers;

internal static class ColorUtil
{
	internal static string GetZoneHeatColor(double ratio)
	{
		ratio = Math.Clamp(ratio, 0.0, 1.0);
		var hue = 120.0 - 120.0 * ratio;
		return HslToHex(hue, 0.95, 0.55);
	}

	internal static string HslToHex(double hue, double saturation, double lightness)
	{
		hue %= 360.0;
		if(hue < 0)
		{
			hue += 360.0;
		}

		var c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
		var x = c * (1.0 - Math.Abs(hue / 60.0 % 2.0 - 1.0));
		var m = lightness - c / 2.0;

		double r1, g1, b1;
		if(hue < 60)
		{
			r1 = c;
			g1 = x;
			b1 = 0;
		}
		else if(hue < 120)
		{
			r1 = x;
			g1 = c;
			b1 = 0;
		}
		else if(hue < 180)
		{
			r1 = 0;
			g1 = c;
			b1 = x;
		}
		else if(hue < 240)
		{
			r1 = 0;
			g1 = x;
			b1 = c;
		}
		else if(hue < 300)
		{
			r1 = x;
			g1 = 0;
			b1 = c;
		}
		else
		{
			r1 = c;
			g1 = 0;
			b1 = x;
		}

		var r = (int)Math.Round((r1 + m) * 255.0);
		var g = (int)Math.Round((g1 + m) * 255.0);
		var b = (int)Math.Round((b1 + m) * 255.0);

		return $"#{r:X2}{g:X2}{b:X2}";
	}
}
