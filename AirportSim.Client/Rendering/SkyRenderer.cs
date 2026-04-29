using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class SkyRenderer
    {
        // ── Clouds ────────────────────────────────────────────────────────────
        private class Cloud
        {
            public double X, Y, Width, Height, Speed, Opacity;
        }

        private readonly List<Cloud> _clouds = new();
        private readonly Random      _rand   = new();

        // ── Stars (shown at night) ────────────────────────────────────────────
        private class Star { public double X, Y, R; }
        private readonly List<Star> _stars = new();

        public SkyRenderer()
        {
            for (int i = 0; i < 18; i++)
                _clouds.Add(new Cloud
                {
                    X       = _rand.NextDouble() * 2200,
                    Y       = _rand.NextDouble() * 260 + 20,
                    Width   = _rand.NextDouble() * 160 + 60,
                    Height  = _rand.NextDouble() * 35  + 18,
                    Speed   = _rand.NextDouble() * 0.4 + 0.1,
                    Opacity = _rand.NextDouble() * 0.4 + 0.3
                });

            for (int i = 0; i < 80; i++)
                _stars.Add(new Star
                {
                    X = _rand.NextDouble() * 2000,
                    Y = _rand.NextDouble() * 340,
                    R = _rand.NextDouble() * 1.5 + 0.5
                });
        }

        public void Render(DrawingContext ctx, DateTime simTime, WeatherCondition weather)
        {
            int hour = simTime.Hour;

            // ── Sky base colour ───────────────────────────────────────────────
            Color skyTop, skyBottom;

            if (hour >= 19 || hour < 5)
            {
                // Night
                skyTop    = Color.FromRgb(5,  8,  30);
                skyBottom = Color.FromRgb(10, 15, 50);
            }
            else if (hour == 5)
            {
                // Dawn
                skyTop    = Color.FromRgb(30,  20,  80);
                skyBottom = Color.FromRgb(230, 120,  60);
            }
            else if (hour == 18)
            {
                // Dusk
                skyTop    = Color.FromRgb(60,  30, 100);
                skyBottom = Color.FromRgb(220, 100,  40);
            }
            else
            {
                // Day
                skyTop    = Color.FromRgb(30, 120, 210);
                skyBottom = Color.FromRgb(135, 195, 235);
            }

            // NEW: weather darkens the sky
            if (weather == WeatherCondition.Storm)
            {
                skyTop    = Darken(skyTop,    0.45);
                skyBottom = Darken(skyBottom, 0.45);
            }
            else if (weather == WeatherCondition.Rain || weather == WeatherCondition.Fog)
            {
                skyTop    = Darken(skyTop,    0.25);
                skyBottom = Darken(skyBottom, 0.25);
            }

            // Draw sky as two-band gradient approximation (top half / bottom half)
            ctx.FillRectangle(new SolidColorBrush(skyTop),    new Rect(0,   0, 2000, 220));
            ctx.FillRectangle(new SolidColorBrush(skyBottom), new Rect(0, 220, 2000, 220));

            bool isNight = hour >= 19 || hour < 5;

            // ── Stars (night only) ────────────────────────────────────────────
            if (isNight && weather == WeatherCondition.Clear)
            {
                var starBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 240));
                foreach (var s in _stars)
                    ctx.DrawEllipse(starBrush, null, new Point(s.X, s.Y), s.R, s.R);
            }

            // ── Clouds ────────────────────────────────────────────────────────
            // Fog: dense low white sheet; Storm: dark heavy clouds; else normal
            IBrush cloudBrush;
            double cloudOpacityMult = 1.0;

            if (weather == WeatherCondition.Fog)
            {
                cloudBrush      = new SolidColorBrush(Color.FromArgb(180, 220, 220, 220));
                cloudOpacityMult = 2.5;
            }
            else if (weather == WeatherCondition.Storm)
            {
                cloudBrush      = new SolidColorBrush(Color.FromArgb(200, 55, 55, 70));
                cloudOpacityMult = 2.0;
            }
            else if (isNight)
            {
                cloudBrush = new SolidColorBrush(Color.FromArgb(90, 80, 80, 110));
                cloudOpacityMult = 1.0;
            }
            else
            {
                cloudBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
                cloudOpacityMult = 1.0;
            }

            foreach (var cloud in _clouds)
            {
                cloud.X -= cloud.Speed;
                if (cloud.X < -250) cloud.X = 2250;

                double alpha = Math.Clamp(cloud.Opacity * cloudOpacityMult, 0.0, 1.0);
                var brush = new SolidColorBrush(
                    Color.FromArgb((byte)(alpha * 255),
                        ((SolidColorBrush)cloudBrush).Color.R,
                        ((SolidColorBrush)cloudBrush).Color.G,
                        ((SolidColorBrush)cloudBrush).Color.B));

                ctx.FillRectangle(brush,
                    new Rect(cloud.X, cloud.Y, cloud.Width, cloud.Height),
                    (float)(cloud.Height * 0.5));
            }

            // NEW: fog ground-haze strip
            if (weather == WeatherCondition.Fog)
            {
                var haze = new SolidColorBrush(Color.FromArgb(120, 210, 210, 210));
                ctx.FillRectangle(haze, new Rect(0, 340, 2000, 140));
            }

            // NEW: rain streaks
            if (weather == WeatherCondition.Rain || weather == WeatherCondition.Storm)
                DrawRain(ctx, simTime);
        }

        private void DrawRain(DrawingContext ctx, DateTime simTime)
        {
            // Use sim-time milliseconds as a deterministic offset so streaks
            // shift position each broadcast without needing per-frame state
            int offset = (simTime.Millisecond / 50) * 18;
            var rainPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 180, 200, 230)), 1);

            for (int i = 0; i < 60; i++)
            {
                int x = ((i * 137 + offset) % 2000);
                int y = ((i * 97  + offset) % 400);
                ctx.DrawLine(rainPen, new Point(x, y), new Point(x - 4, y + 18));
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Color Darken(Color c, double amount) => Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
    }
}