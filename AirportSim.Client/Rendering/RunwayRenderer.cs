using System;
using Avalonia;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class RunwayRenderer
    {
        private bool   _blinkOn;
        private double _blinkAccumMs;
        private const double BlinkIntervalMs = 600;

        public void Render(DrawingContext ctx, DateTime simTime,
                           WeatherCondition weather, double realDeltaMs)
        {
            int  hour    = simTime.Hour;
            bool isNight = hour >= 19 || hour < 5;
            bool isDusk  = hour == 18 || hour == 5;

            // Advance blink timer
            _blinkAccumMs += realDeltaMs;
            if (_blinkAccumMs >= BlinkIntervalMs)
            {
                _blinkOn      = !_blinkOn;
                _blinkAccumMs -= BlinkIntervalMs;
            }

            // ── Asphalt ───────────────────────────────────────────────────────
            bool wetRunway = weather == WeatherCondition.Rain ||
                             weather == WeatherCondition.Storm;

            Color asphalt = wetRunway
                ? Color.FromRgb(48, 52, 56)
                : Color.FromRgb(72, 75, 78);

            ctx.FillRectangle(new SolidColorBrush(asphalt), new Rect(400, 460, 1200, 40));

            if (wetRunway)
                ctx.FillRectangle(
                    new SolidColorBrush(Color.FromArgb(40, 140, 180, 220)),
                    new Rect(400, 460, 1200, 40));

            // ── Runway markings ───────────────────────────────────────────────
            var centerPen = new Pen(
                new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 2)
                { DashStyle = DashStyle.Dash };
            ctx.DrawLine(centerPen, new Point(440, 480), new Point(1560, 480));

            DrawThresholdBars(ctx, 400,  460);
            DrawThresholdBars(ctx, 1600, 460);

            var markPen = new Pen(
                new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 3);
            for (int i = 0; i < 3; i++)
            {
                int x = 500 + i * 60;
                ctx.DrawLine(markPen, new Point(x, 462), new Point(x, 472));
                ctx.DrawLine(markPen, new Point(x, 488), new Point(x, 498));
            }

            // ── PAPI (correctly on arrival/left threshold) ────────────────────
            if (isNight || isDusk)
            {
                ctx.DrawEllipse(Brushes.White, null, new Point(418, 455), 3, 3);
                ctx.DrawEllipse(Brushes.White, null, new Point(426, 455), 3, 3);
                ctx.DrawEllipse(Brushes.Red,   null, new Point(434, 455), 3, 3);
                ctx.DrawEllipse(Brushes.Red,   null, new Point(442, 455), 3, 3);
            }

            // ── Lighting ──────────────────────────────────────────────────────
            if (isNight || isDusk)
                DrawRunwayLights(ctx, weather);
        }

        private void DrawThresholdBars(DrawingContext ctx, int x, int y)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(240, 240, 240)), 3);
            for (int i = 0; i < 4; i++)
                ctx.DrawLine(pen,
                    new Point(x + i * 6, y),
                    new Point(x + i * 6, y + 40));
        }

        private void DrawRunwayLights(DrawingContext ctx, WeatherCondition weather)
        {
            byte lightAlpha = weather switch
            {
                WeatherCondition.Fog   => 130,
                WeatherCondition.Storm => 100,
                _                      => 220
            };

            var edgeBrush = new SolidColorBrush(
                Color.FromArgb(lightAlpha, 255, 255, 220));
            for (int x = 400; x <= 1600; x += 50)
            {
                ctx.DrawEllipse(edgeBrush, null, new Point(x, 461), 2.5, 2.5);
                ctx.DrawEllipse(edgeBrush, null, new Point(x, 499), 2.5, 2.5);
            }

            var greenBrush = new SolidColorBrush(
                Color.FromArgb(lightAlpha, 80, 255, 80));
            for (int i = 0; i < 6; i++)
                ctx.DrawEllipse(greenBrush, null, new Point(400 + i * 8, 480), 3, 3);

            var redBrush = new SolidColorBrush(
                Color.FromArgb(lightAlpha, 255, 60, 60));
            for (int i = 0; i < 6; i++)
                ctx.DrawEllipse(redBrush, null, new Point(1600 + i * 8, 480), 3, 3);

            // Approach strobes — blink at ~1 Hz
            if (_blinkOn)
            {
                var strobeBrush = new SolidColorBrush(
                    Color.FromArgb((byte)(lightAlpha * 0.85), 255, 255, 255));
                for (int i = 1; i <= 5; i++)
                    ctx.DrawEllipse(strobeBrush, null,
                        new Point(400 - i * 30, 480), 3, 3);
            }
        }
    }
}