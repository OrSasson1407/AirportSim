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

        // Helper struct for interpolating weather visuals
        private struct WeatherVisuals
        {
            public double SkyDarken;
            public double CloudOpacityMult;
            public Color CloudColor;
            public double FogIntensity;
            public double RainIntensity;
        }

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

        // Changed signature to take the full snapshot so we can access transition variables
        public void Render(DrawingContext ctx, SimSnapshot snapshot)
        {
            double hour = snapshot.SimulatedTime.TimeOfDay.TotalHours;

            // 1. Calculate smooth time-of-day sky gradient
            var (skyTop, skyBottom) = GetTimeOfDayGradient(hour);

            // 2. Calculate weather visuals (lerping between previous and current weather)
            var prevVisuals = GetWeatherVisuals(snapshot.PreviousWeather, hour);
            var currVisuals = GetWeatherVisuals(snapshot.Weather, hour);
            var activeVisuals = LerpVisuals(prevVisuals, currVisuals, snapshot.WeatherTransitionProgress);

            // Apply weather darkening
            skyTop = Darken(skyTop, activeVisuals.SkyDarken);
            skyBottom = Darken(skyBottom, activeVisuals.SkyDarken);

            // Draw sky base
            ctx.FillRectangle(new SolidColorBrush(skyTop),    new Rect(0,   0, 2000, 220));
            ctx.FillRectangle(new SolidColorBrush(skyBottom), new Rect(0, 220, 2000, 220));

            // ── Stars (fade in at night, fade out with clouds/fog) ─────────────────
            bool isNightTime = hour >= 18.5 || hour < 5.5;
            double starVisibility = 1.0 - activeVisuals.SkyDarken; // Hide stars if stormy/cloudy
            
            if (isNightTime && starVisibility > 0)
            {
                // Smooth fade for stars based on time
                double timeAlpha = 1.0;
                if (hour >= 18.5 && hour < 19.5) timeAlpha = (hour - 18.5); // Dusk fade in
                if (hour >= 4.5 && hour < 5.5) timeAlpha = 1.0 - (hour - 4.5); // Dawn fade out
                
                byte starAlpha = (byte)(200 * timeAlpha * starVisibility);
                var starBrush = new SolidColorBrush(Color.FromArgb(starAlpha, 255, 255, 240));
                
                foreach (var s in _stars)
                    ctx.DrawEllipse(starBrush, null, new Point(s.X, s.Y), s.R, s.R);
            }

            // ── Clouds ────────────────────────────────────────────────────────
            foreach (var cloud in _clouds)
            {
                cloud.X -= cloud.Speed;
                if (cloud.X < -250) cloud.X = 2250;

                double alpha = Math.Clamp(cloud.Opacity * activeVisuals.CloudOpacityMult, 0.0, 1.0);
                var cColor = activeVisuals.CloudColor;
                var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), cColor.R, cColor.G, cColor.B));

                ctx.FillRectangle(brush,
                    new Rect(cloud.X, cloud.Y, cloud.Width, cloud.Height),
                    (float)(cloud.Height * 0.5));
            }

            // ── Fog ───────────────────────────────────────────────────────────
            if (activeVisuals.FogIntensity > 0)
            {
                var fogAlpha = (byte)(120 * activeVisuals.FogIntensity);
                var haze = new SolidColorBrush(Color.FromArgb(fogAlpha, 210, 210, 210));
                ctx.FillRectangle(haze, new Rect(0, 340, 2000, 140)); // Ground layer
                
                // Full screen haze overlay for dense fog
                var fullHaze = new SolidColorBrush(Color.FromArgb((byte)(fogAlpha * 0.5), 210, 210, 210));
                ctx.FillRectangle(fullHaze, new Rect(0, 0, 2000, 1000));
            }

            // ── Rain ──────────────────────────────────────────────────────────
            if (activeVisuals.RainIntensity > 0)
            {
                DrawRain(ctx, snapshot.SimulatedTime, activeVisuals.RainIntensity);
            }
        }

        private void DrawRain(DrawingContext ctx, DateTime simTime, double intensity)
        {
            int offset = (simTime.Millisecond / 50) * 18;
            byte alpha = (byte)(60 * intensity);
            var rainPen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 180, 200, 230)), 1);
            int dropCount = (int)(60 * intensity);

            for (int i = 0; i < dropCount; i++)
            {
                int x = ((i * 137 + offset) % 2000);
                int y = ((i * 97  + offset) % 400);
                ctx.DrawLine(rainPen, new Point(x, y), new Point(x - 4, y + 18));
            }
        }

        // ── Gradient & Interpolation Helpers ──────────────────────────────────

        private (Color top, Color bottom) GetTimeOfDayGradient(double hour)
        {
            var nightTop  = Color.FromRgb(5,  8,  30);
            var nightBot  = Color.FromRgb(10, 15, 50);
            var dawnTop   = Color.FromRgb(30, 20, 80);
            var dawnBot   = Color.FromRgb(230, 120, 60);
            var dayTop    = Color.FromRgb(30, 120, 210);
            var dayBot    = Color.FromRgb(135, 195, 235);
            var duskTop   = Color.FromRgb(60, 30, 100);
            var duskBot   = Color.FromRgb(220, 100, 40);

            // Night -> Dawn (4AM to 6AM)
            if (hour >= 4 && hour < 6) return LerpColors(nightTop, nightBot, dawnTop, dawnBot, (hour - 4) / 2.0);
            // Dawn -> Day (6AM to 8AM)
            if (hour >= 6 && hour < 8) return LerpColors(dawnTop, dawnBot, dayTop, dayBot, (hour - 6) / 2.0);
            // Day (8AM to 5PM)
            if (hour >= 8 && hour < 17) return (dayTop, dayBot);
            // Day -> Dusk (5PM to 7PM)
            if (hour >= 17 && hour < 19) return LerpColors(dayTop, dayBot, duskTop, duskBot, (hour - 17) / 2.0);
            // Dusk -> Night (7PM to 9PM)
            if (hour >= 19 && hour < 21) return LerpColors(duskTop, duskBot, nightTop, nightBot, (hour - 19) / 2.0);
            
            // Night (9PM to 4AM)
            return (nightTop, nightBot);
        }

        private WeatherVisuals GetWeatherVisuals(WeatherCondition weather, double hour)
        {
            bool isNight = hour >= 19 || hour < 5;
            return weather switch
            {
                WeatherCondition.Fog => new WeatherVisuals { SkyDarken = 0.25, CloudOpacityMult = 2.5, CloudColor = Color.FromRgb(220, 220, 220), FogIntensity = 1.0, RainIntensity = 0.0 },
                WeatherCondition.Storm => new WeatherVisuals { SkyDarken = 0.55, CloudOpacityMult = 2.0, CloudColor = Color.FromRgb(55, 55, 70), FogIntensity = 0.0, RainIntensity = 1.0 },
                WeatherCondition.Rain => new WeatherVisuals { SkyDarken = 0.30, CloudOpacityMult = 1.5, CloudColor = Color.FromRgb(100, 100, 120), FogIntensity = 0.1, RainIntensity = 0.6 },
                WeatherCondition.Cloudy => new WeatherVisuals { SkyDarken = 0.10, CloudOpacityMult = 1.8, CloudColor = isNight ? Color.FromRgb(80, 80, 110) : Color.FromRgb(200, 200, 200), FogIntensity = 0.0, RainIntensity = 0.0 },
                _ => new WeatherVisuals { SkyDarken = 0.0, CloudOpacityMult = 1.0, CloudColor = isNight ? Color.FromRgb(80, 80, 110) : Color.FromRgb(255, 255, 255), FogIntensity = 0.0, RainIntensity = 0.0 }
            };
        }

        private WeatherVisuals LerpVisuals(WeatherVisuals a, WeatherVisuals b, double t)
        {
            return new WeatherVisuals
            {
                SkyDarken = Lerp(a.SkyDarken, b.SkyDarken, t),
                CloudOpacityMult = Lerp(a.CloudOpacityMult, b.CloudOpacityMult, t),
                FogIntensity = Lerp(a.FogIntensity, b.FogIntensity, t),
                RainIntensity = Lerp(a.RainIntensity, b.RainIntensity, t),
                CloudColor = Color.FromRgb(
                    (byte)Lerp(a.CloudColor.R, b.CloudColor.R, t),
                    (byte)Lerp(a.CloudColor.G, b.CloudColor.G, t),
                    (byte)Lerp(a.CloudColor.B, b.CloudColor.B, t))
            };
        }

        private (Color top, Color bottom) LerpColors(Color aTop, Color aBot, Color bTop, Color bBot, double t)
        {
            return (
                Color.FromRgb((byte)Lerp(aTop.R, bTop.R, t), (byte)Lerp(aTop.G, bTop.G, t), (byte)Lerp(aTop.B, bTop.B, t)),
                Color.FromRgb((byte)Lerp(aBot.R, bBot.R, t), (byte)Lerp(aBot.G, bBot.G, t), (byte)Lerp(aBot.B, bBot.B, t))
            );
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0.0, 1.0);

        private static Color Darken(Color c, double amount) => Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
    }
}