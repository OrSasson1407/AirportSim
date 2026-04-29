using System;
using Avalonia;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class GroundRenderer
    {
        public void Render(DrawingContext ctx, DateTime simTime, WeatherCondition weather)
        {
            bool isNight = simTime.Hour >= 19 || simTime.Hour < 5;

            // ── Terrain ───────────────────────────────────────────────────────
            // Wet ground is darker
            Color grassFar  = (weather == WeatherCondition.Rain || weather == WeatherCondition.Storm)
                ? Color.FromRgb(42,  68,  35)
                : Color.FromRgb(72,  110, 55);

            Color grassNear = (weather == WeatherCondition.Rain || weather == WeatherCondition.Storm)
                ? Color.FromRgb(35,  58,  28)
                : Color.FromRgb(58,  95,  44);

            ctx.FillRectangle(new SolidColorBrush(grassFar),  new Rect(0, 440, 2000,  40));
            ctx.FillRectangle(new SolidColorBrush(grassNear), new Rect(0, 480, 2000, 120));

            // ── Terminal building (background, right side) ────────────────────
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(140, 135, 125)),
                new Rect(1650, 350, 300, 130));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(110, 105, 98)),
                new Rect(1650, 340,  300, 12));

            // Terminal windows (lit at night)
            IBrush windowBrush = isNight
                ? new SolidColorBrush(Color.FromRgb(255, 230, 140))
                : new SolidColorBrush(Color.FromRgb(160, 195, 220));

            for (int col = 0; col < 8; col++)
            for (int row = 0; row < 3; row++)
                ctx.FillRectangle(windowBrush,
                    new Rect(1665 + col * 34, 358 + row * 32, 20, 18));

            // ── Jet bridges (two stubs from terminal) ─────────────────────────
            var bridgePen = new Pen(new SolidColorBrush(Color.FromRgb(100, 95, 88)), 6);
            ctx.DrawLine(bridgePen, new Point(1680, 480), new Point(1660, 480));
            ctx.DrawLine(bridgePen, new Point(1730, 480), new Point(1710, 480));

            // ── Control tower ─────────────────────────────────────────────────
            // Stalk
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(180, 175, 165)),
                new Rect(283, 330, 34, 150));

            // Stalk detail stripe
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(155, 150, 140)),
                new Rect(288, 380, 24,  4));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(155, 150, 140)),
                new Rect(288, 420, 24,  4));

            // Cab (glass room)
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(70, 130, 160)),
                new Rect(262, 310, 76, 22));

            // Cab glass highlight
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                new Rect(265, 313, 70, 8));

            // Roof
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(90, 85, 80)),
                new Rect(258, 304, 84,  8));

            // Aviation warning light on top (red at night)
            IBrush warningLight = isNight
                ? new SolidColorBrush(Color.FromRgb(220, 40, 40))
                : new SolidColorBrush(Color.FromRgb(160, 40, 40));
            ctx.DrawEllipse(warningLight, null, new Point(300, 300), 4, 4);

            // ── Wind sock ─────────────────────────────────────────────────────
            DrawWindSock(ctx, weather, isNight);
        }

        private void DrawWindSock(DrawingContext ctx, WeatherCondition weather, bool isNight)
        {
            // Pole
            var polePen = new Pen(new SolidColorBrush(Color.FromRgb(150, 145, 135)), 3);
            ctx.DrawLine(polePen, new Point(1560, 440), new Point(1560, 400));

            // Wind strength drives how far the sock extends horizontally
            double windStrength = weather switch
            {
                WeatherCondition.Storm  => 1.0,
                WeatherCondition.Rain   => 0.75,
                WeatherCondition.Cloudy => 0.5,
                WeatherCondition.Fog    => 0.2,
                _                       => 0.35    // Clear — light breeze
            };

            double sockLength = 30 + windStrength * 28;
            double tipY       = 403 + (1.0 - windStrength) * 12; // droops in calm wind

            // Sock body (orange/white striped approximation — two colours)
            var sockPenOrange = new Pen(new SolidColorBrush(Color.FromRgb(220, 100, 30)), 5);
            var sockPenWhite  = new Pen(new SolidColorBrush(Color.FromRgb(240, 240, 240)), 5);

            ctx.DrawLine(sockPenOrange,
                new Point(1560, 401),
                new Point(1560 + sockLength * 0.5, tipY + 1));

            ctx.DrawLine(sockPenWhite,
                new Point(1560 + sockLength * 0.5, tipY + 1),
                new Point(1560 + sockLength, tipY + 2));

            // Tip cone
            ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 100, 30)),
                null, new Point(1560 + sockLength, tipY + 2), 3, 3);
        }
    }
}