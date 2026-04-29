using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class GroundRenderer
    {
        // ── Jet-bridge animation state ─────────────────────────────────────
        // Two gate positions (world-X) that map to the terminal stubs at x=1680, x=1730
        private static readonly double[] GateStubX = { 1680.0, 1730.0 };
        private const double BridgeRetractedLength = 20.0;
        private const double BridgeExtendedLength  = 60.0;
        private const double BridgeAnimSpeed       = 0.8;    // units per second
        private double[] _bridgeExtension          = { 0.0, 0.0 };   // 0..1

        // ── Ground-crew vehicle phase (simple oscillating park position) ───
        private double[] _fuelTruckPhase = { 0.0,  Math.PI };
        private double[] _bagCartPhase   = { 1.2,  2.4 };

        public void Render(DrawingContext ctx, DateTime simTime, WeatherCondition weather,
                           IReadOnlyList<AircraftState> aircraft, double realDeltaMs)
        {
            bool isNight = simTime.Hour >= 19 || simTime.Hour < 5;

            // ── Terrain ───────────────────────────────────────────────────────
            Color grassFar  = (weather == WeatherCondition.Rain || weather == WeatherCondition.Storm)
                ? Color.FromRgb(42,  68,  35)
                : Color.FromRgb(72,  110, 55);
            Color grassNear = (weather == WeatherCondition.Rain || weather == WeatherCondition.Storm)
                ? Color.FromRgb(35,  58,  28)
                : Color.FromRgb(58,  95,  44);

            ctx.FillRectangle(new SolidColorBrush(grassFar),  new Rect(0, 440, 2000,  40));
            ctx.FillRectangle(new SolidColorBrush(grassNear), new Rect(0, 480, 2000, 120));

            // ── Terminal building ─────────────────────────────────────────────
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(140, 135, 125)),
                new Rect(1650, 350, 300, 130));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(110, 105, 98)),
                new Rect(1650, 340, 300, 12));

            IBrush windowBrush = isNight
                ? new SolidColorBrush(Color.FromRgb(255, 230, 140))
                : new SolidColorBrush(Color.FromRgb(160, 195, 220));
            for (int col = 0; col < 8; col++)
            for (int row = 0; row < 3; row++)
                ctx.FillRectangle(windowBrush,
                    new Rect(1665 + col * 34, 358 + row * 32, 20, 18));

            // ── Gate occupancy from parked aircraft ───────────────────────────
            bool[] gateOccupied = new bool[GateStubX.Length];
            foreach (var ac in aircraft)
            {
                if (ac.Phase == AircraftPhase.Parked)
                {
                    for (int g = 0; g < GateStubX.Length; g++)
                    {
                        if (Math.Abs(ac.GateX - GateStubX[g]) < 80)
                        {
                            gateOccupied[g] = true;
                            break;
                        }
                    }
                }
            }

            // ── Animate bridge extension ──────────────────────────────────────
            double dt = Math.Clamp(realDeltaMs, 0, 100);
            for (int g = 0; g < _bridgeExtension.Length; g++)
            {
                double target = gateOccupied[g] ? 1.0 : 0.0;
                double step   = BridgeAnimSpeed * dt / 1000.0;
                _bridgeExtension[g] += (target > _bridgeExtension[g] ? 1 : -1) * step;
                _bridgeExtension[g]  = Math.Clamp(_bridgeExtension[g], 0.0, 1.0);
            }

            // ── Advance vehicle animations ────────────────────────────────────
            for (int g = 0; g < GateStubX.Length; g++)
            {
                if (gateOccupied[g])
                {
                    _fuelTruckPhase[g] += dt * 0.001;
                    _bagCartPhase[g]   += dt * 0.0015;
                }
            }

            // ── Draw jet bridges ──────────────────────────────────────────────
            DrawJetBridges(ctx);

            // ── Draw ground crew vehicles (drawn before control tower so tower is on top) ──
            DrawGroundCrew(ctx, gateOccupied, isNight);

            // ── Control tower ─────────────────────────────────────────────────
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(180, 175, 165)),
                new Rect(283, 330, 34, 150));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(155, 150, 140)),
                new Rect(288, 380, 24,  4));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(155, 150, 140)),
                new Rect(288, 420, 24,  4));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(70, 130, 160)),
                new Rect(262, 310, 76, 22));
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                new Rect(265, 313, 70, 8));
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(90, 85, 80)),
                new Rect(258, 304, 84,  8));

            IBrush warningLight = isNight
                ? new SolidColorBrush(Color.FromRgb(220, 40, 40))
                : new SolidColorBrush(Color.FromRgb(160, 40, 40));
            ctx.DrawEllipse(warningLight, null, new Point(300, 300), 4, 4);

            // ── Wind sock ─────────────────────────────────────────────────────
            DrawWindSock(ctx, weather, isNight);
        }

        // Backwards-compatible overload (no aircraft list, no delta time)
        public void Render(DrawingContext ctx, DateTime simTime, WeatherCondition weather)
            => Render(ctx, simTime, weather, Array.Empty<AircraftState>(), 16.0);

        // ─────────────────────────────────────────────────────────────────────

        private void DrawJetBridges(DrawingContext ctx)
        {
            var bridgeBodyBrush = new SolidColorBrush(Color.FromRgb(100, 95, 88));
            var bridgeRoofBrush = new SolidColorBrush(Color.FromRgb(70,  67, 62));

            for (int g = 0; g < GateStubX.Length; g++)
            {
                double stubX   = GateStubX[g];
                double ext     = _bridgeExtension[g];
                double length  = BridgeRetractedLength + ext * (BridgeExtendedLength - BridgeRetractedLength);
                double endX    = stubX - length;   // bridge extends LEFT toward apron
                double bridgeY = 480;

                // Bridge tunnel body
                ctx.FillRectangle(bridgeBodyBrush, new Rect(endX, bridgeY - 10, length, 12));
                // Roof stripe
                ctx.FillRectangle(bridgeRoofBrush, new Rect(endX, bridgeY - 10, length, 3));

                // Support leg at movable end
                if (ext > 0.05)
                {
                    var legPen = new Pen(new SolidColorBrush(Color.FromRgb(80, 76, 70)), 3);
                    ctx.DrawLine(legPen,
                        new Point(endX + 5, bridgeY + 2),
                        new Point(endX + 5, bridgeY + 18));
                }

                // Docking collar at movable end (blue, fades in with extension)
                if (ext > 0.1)
                {
                    byte a = (byte)Math.Clamp(ext * 255, 0, 255);
                    ctx.FillRectangle(
                        new SolidColorBrush(Color.FromArgb(a, 60, 130, 180)),
                        new Rect(endX - 6, bridgeY - 8, 8, 10));
                }

                // Status light: green=docked, amber=moving, dark=retracted
                Color lightColor =
                    ext > 0.95 ? Color.FromRgb(60,  200, 80) :
                    ext > 0.05 ? Color.FromRgb(220, 170, 30) :
                                 Color.FromRgb(50,  50,  50);
                ctx.DrawEllipse(new SolidColorBrush(lightColor), null,
                    new Point(endX - 2, bridgeY - 4), 3, 3);
            }
        }

        private void DrawGroundCrew(DrawingContext ctx, bool[] gateOccupied, bool isNight)
        {
            for (int g = 0; g < GateStubX.Length; g++)
            {
                if (_bridgeExtension[g] < 0.05) continue;

                byte   alpha = (byte)Math.Clamp(_bridgeExtension[g] * 2.5 * 255, 0, 255);
                double baseX = GateStubX[g] - BridgeExtendedLength - 30;
                double baseY = 490;

                // Fuel truck — gentle oscillating park offset
                double truckX = baseX - 35 + Math.Sin(_fuelTruckPhase[g]) * 4.0;
                DrawFuelTruck(ctx, truckX, baseY, alpha, isNight);

                // Baggage cart — offset right
                double cartX = baseX + 5 + Math.Sin(_bagCartPhase[g]) * 3.0;
                DrawBaggageCart(ctx, cartX, baseY, alpha, isNight);
            }
        }

        private static void DrawFuelTruck(DrawingContext ctx, double x, double y,
                                          byte alpha, bool isNight)
        {
            // Cab (yellow)
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 210, 190, 30)),
                new Rect(x, y - 12, 22, 12));
            // Tank (dark yellow)
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 175, 155, 18)),
                new Rect(x + 22, y - 10, 28, 10));
            // Wheels
            var wb = new SolidColorBrush(Color.FromArgb(alpha, 40, 40, 40));
            ctx.DrawEllipse(wb, null, new Point(x + 6,  y + 1), 4, 4);
            ctx.DrawEllipse(wb, null, new Point(x + 40, y + 1), 4, 4);
            // Hose reel (red)
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 180, 50, 50)),
                new Rect(x + 23, y - 6, 5, 5));
            if (isNight)
            {
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 255, 240, 100)),
                    null, new Point(x + 1, y - 10), 2, 2);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 220, 60, 60)),
                    null, new Point(x + 49, y - 6), 2, 2);
            }
        }

        private static void DrawBaggageCart(DrawingContext ctx, double x, double y,
                                             byte alpha, bool isNight)
        {
            // Tractor cab (orange)
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 230, 120, 30)),
                new Rect(x, y - 10, 14, 10));
            // First flat-bed trailer
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 80, 70, 60)),
                new Rect(x + 16, y - 7, 18, 7));
            // Second flat-bed trailer
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 80, 70, 60)),
                new Rect(x + 36, y - 7, 18, 7));
            // Luggage on first trailer
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 60,  120, 200)), new Rect(x + 17, y - 13, 7, 6));
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 200, 60,  60)),  new Rect(x + 25, y - 12, 7, 5));
            // Luggage on second trailer
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, 60,  180, 100)), new Rect(x + 37, y - 13, 7, 6));
            // Wheels
            var wb = new SolidColorBrush(Color.FromArgb(alpha, 40, 40, 40));
            ctx.DrawEllipse(wb, null, new Point(x + 4,  y + 1), 3, 3);
            ctx.DrawEllipse(wb, null, new Point(x + 25, y + 1), 3, 3);
            ctx.DrawEllipse(wb, null, new Point(x + 45, y + 1), 3, 3);
            if (isNight)
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 255, 240, 100)),
                    null, new Point(x + 1, y - 8), 2, 2);
        }

        private void DrawWindSock(DrawingContext ctx, WeatherCondition weather, bool isNight)
        {
            var polePen = new Pen(new SolidColorBrush(Color.FromRgb(150, 145, 135)), 3);
            ctx.DrawLine(polePen, new Point(1560, 440), new Point(1560, 400));

            double windStrength = weather switch
            {
                WeatherCondition.Storm  => 1.0,
                WeatherCondition.Rain   => 0.75,
                WeatherCondition.Cloudy => 0.5,
                WeatherCondition.Fog    => 0.2,
                _                       => 0.35
            };

            double sockLength = 30 + windStrength * 28;
            double tipY       = 403 + (1.0 - windStrength) * 12;

            var sockPenOrange = new Pen(new SolidColorBrush(Color.FromRgb(220, 100, 30)), 5);
            var sockPenWhite  = new Pen(new SolidColorBrush(Color.FromRgb(240, 240, 240)), 5);

            ctx.DrawLine(sockPenOrange, new Point(1560, 401),
                new Point(1560 + sockLength * 0.5, tipY + 1));
            ctx.DrawLine(sockPenWhite,
                new Point(1560 + sockLength * 0.5, tipY + 1),
                new Point(1560 + sockLength, tipY + 2));
            ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 100, 30)),
                null, new Point(1560 + sockLength, tipY + 2), 3, 3);
        }
    }
}