using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class AircraftRenderer
    {
        // ── Typefaces ─────────────────────────────────────────────────────────
        private readonly Typeface _labelFont    = new("Arial");
        private readonly Typeface _labelBold    = new("Arial", FontStyle.Normal, FontWeight.Bold);

        // ── Exhaust trail particles ───────────────────────────────────────────
        private class TrailParticle
        {
            public double X, Y, Life, Size;
        }

        private readonly Dictionary<string, List<TrailParticle>> _trails   = new();
        private readonly Random                                   _rand     = new();

        // ── Emergency flash state per aircraft ────────────────────────────────
        private readonly Dictionary<string, double> _flashAccum = new();
        private const double FlashIntervalMs = 350;

        // ── Main entry point ──────────────────────────────────────────────────

        public void Render(DrawingContext ctx,
                           SimSnapshot?   prevSnap,
                           SimSnapshot    targetSnap,
                           double         t)
        {
            // Purge trails for aircraft that have left the scene
            var activeIds = targetSnap.ActiveAircraft.Select(a => a.FlightId).ToHashSet();
            foreach (var k in _trails.Keys.Where(k => !activeIds.Contains(k)).ToList())
                _trails.Remove(k);
            foreach (var k in _flashAccum.Keys.Where(k => !activeIds.Contains(k)).ToList())
                _flashAccum.Remove(k);

            // Render each aircraft — sorted so emergencies draw on top
            var sorted = targetSnap.ActiveAircraft
                .OrderBy(a => a.Status == AircraftStatus.Emergency ? 1 : 0)
                .ToList();

            foreach (var target in sorted)
            {
                var prev = prevSnap?.ActiveAircraft
                               .FirstOrDefault(a => a.FlightId == target.FlightId)
                           ?? target;

                double x       = Lerp(prev.Position.X, target.Position.X, t);
                double y       = Lerp(prev.Position.Y, target.Position.Y, t);
                double heading = LerpAngle(prev.Heading, target.Heading, t);

                // 1. Exhaust trails
                UpdateAndDrawTrails(ctx, target, x, y, heading);

                // 2. Aircraft body + label
                using (ctx.PushTransform(Matrix.CreateTranslation(x, y)))
                {
                    DrawAircraftShape(ctx, target, heading);
                    DrawLabel(ctx, target, x, y);
                }

                // 3. Emergency beacon flash (drawn at world position, not translated)
                if (target.Status == AircraftStatus.Emergency)
                    DrawEmergencyBeacon(ctx, target.FlightId, x, y);
            }
        }

        // ── Aircraft shapes ───────────────────────────────────────────────────

        private void DrawAircraftShape(DrawingContext ctx,
                                       AircraftState  ac,
                                       double         heading)
        {
            using (ctx.PushTransform(
                Matrix.CreateRotation(heading * Math.PI / 180.0)))
            {
                switch (ac.Type)
                {
                    case AircraftType.Small:
                        DrawSmall(ctx, ac);
                        break;
                    case AircraftType.Medium:
                        DrawMedium(ctx, ac);
                        break;
                    case AircraftType.Large:
                        DrawLarge(ctx, ac);
                        break;
                }
            }
        }

        // Small — light prop / regional jet: simple swept triangle
        private void DrawSmall(DrawingContext ctx, AircraftState ac)
        {
            IBrush fill    = GetAircraftColor(ac);
            var    outline = new Pen(Brushes.White, 0.8);

            // Fuselage
            var fuse = MakePath(new[]
            {
                new Point( 12,   0),   // nose
                new Point( -8,  -4),   // left tail
                new Point( -8,   4),   // right tail
            });
            ctx.DrawGeometry(fill, outline, fuse);

            // Wings — thin swept pair
            var wings = MakePath(new[]
            {
                new Point(  2,   0),
                new Point( -6, -14),
                new Point( -9, -14),
                new Point( -4,   0),
                new Point( -9,  14),
                new Point( -6,  14),
            });
            ctx.DrawGeometry(fill, outline, wings);
        }

        // Medium — narrow-body (737 / A320 style)
        private void DrawMedium(DrawingContext ctx, AircraftState ac)
        {
            IBrush fill    = GetAircraftColor(ac);
            var    outline = new Pen(Brushes.White, 1.0);

            // Fuselage
            var fuse = MakePath(new[]
            {
                new Point( 18,   0),   // nose
                new Point(  8,  -3),
                new Point(-14,  -3),   // rear
                new Point(-14,   3),
                new Point(  8,   3),
            });
            ctx.DrawGeometry(fill, outline, fuse);

            // Main wings
            var wings = MakePath(new[]
            {
                new Point(  6,  -2),
                new Point( -2, -22),
                new Point( -7, -22),
                new Point( -4,  -2),
                new Point( -4,   2),
                new Point( -7,  22),
                new Point( -2,  22),
                new Point(  6,   2),
            });
            ctx.DrawGeometry(fill, outline, wings);

            // Tail fin (vertical stabiliser stub)
            var tail = MakePath(new[]
            {
                new Point(-11,  0),
                new Point(-14, -6),
                new Point(-14,  0),
            });
            ctx.DrawGeometry(fill, new Pen(Brushes.White, 0.6), tail);

            // Horizontal stabilisers
            var stab = MakePath(new[]
            {
                new Point(-11,  -1),
                new Point(-14,  -9),
                new Point(-16,  -9),
                new Point(-14,  -1),
                new Point(-14,   1),
                new Point(-16,   9),
                new Point(-14,   9),
                new Point(-11,   1),
            });
            ctx.DrawGeometry(fill, new Pen(Brushes.White, 0.6), stab);
        }

        // Large — wide-body (747 / A380 style)
        private void DrawLarge(DrawingContext ctx, AircraftState ac)
        {
            IBrush fill    = GetAircraftColor(ac);
            var    outline = new Pen(Brushes.White, 1.2);

            // Wide fuselage
            var fuse = MakePath(new[]
            {
                new Point( 26,   0),   // nose
                new Point( 14,  -5),
                new Point(-18,  -5),   // rear body
                new Point(-22,  -3),
                new Point(-22,   3),
                new Point(-18,   5),
                new Point( 14,   5),
            });
            ctx.DrawGeometry(fill, outline, fuse);

            // Main wings — wide, swept
            var wings = MakePath(new[]
            {
                new Point( 10,  -4),
                new Point(  0, -32),
                new Point( -6, -32),
                new Point( -4,  -4),
                new Point( -4,   4),
                new Point( -6,  32),
                new Point(  0,  32),
                new Point( 10,   4),
            });
            ctx.DrawGeometry(fill, outline, wings);

            // Engine pods under wings (two per side)
            DrawEngine(ctx, fill,  -2, -18);
            DrawEngine(ctx, fill,   4, -24);
            DrawEngine(ctx, fill,  -2,  18);
            DrawEngine(ctx, fill,   4,  24);

            // Tail fin
            var tail = MakePath(new[]
            {
                new Point(-16,   0),
                new Point(-20, -10),
                new Point(-22,  -1),
            });
            ctx.DrawGeometry(fill, new Pen(Brushes.White, 0.8), tail);

            // Horizontal stabilisers
            var stab = MakePath(new[]
            {
                new Point(-17,  -1),
                new Point(-21, -13),
                new Point(-24, -13),
                new Point(-20,  -1),
                new Point(-20,   1),
                new Point(-24,  13),
                new Point(-21,  13),
                new Point(-17,   1),
            });
            ctx.DrawGeometry(fill, new Pen(Brushes.White, 0.8), stab);
        }

        private void DrawEngine(DrawingContext ctx, IBrush fill, double x, double y)
        {
            // Small rectangle representing an engine nacelle
            ctx.FillRectangle(fill,
                new Rect(x - 5, y - 2, 10, 4));
            ctx.DrawRectangle(null,
                new Pen(Brushes.White, 0.5),
                new Rect(x - 5, y - 2, 10, 4));
        }

        // ── Colour logic ──────────────────────────────────────────────────────

        private IBrush GetAircraftColor(AircraftState ac)
        {
            if (ac.Status == AircraftStatus.Emergency)
                return new SolidColorBrush(Color.FromRgb(220, 40, 40));

            if (ac.Status == AircraftStatus.GoAround)
                return new SolidColorBrush(Color.FromRgb(255, 180, 0));

            return ac.FlightType == FlightType.Arrival
                ? new SolidColorBrush(Color.FromRgb(60,  180, 255))   // arrivals: blue
                : new SolidColorBrush(Color.FromRgb(80,  220, 140));   // departures: green
        }

        // ── Labels ────────────────────────────────────────────────────────────

        private void DrawLabel(DrawingContext ctx,
                               AircraftState  ac,
                               double         worldX,
                               double         worldY)
        {
            // Label offset — above and to the right of the aircraft
            const double lx = 22;
            const double ly = -42;

            // Leader line from aircraft centre to label
            ctx.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 0.8),
                new Point(0, 0),
                new Point(lx, ly + 36));

            // Build label lines
            string line1 = ac.FlightId;
            string line2 = ac.Phase.ToString();
            string line3 = ac.AltitudeFt > 50
                ? $"{ac.AltitudeFt:N0} ft  {ac.SpeedKts} kts"
                : $"Ground  {ac.SpeedKts} kts";

            // Status badge text
            string? badge = ac.Status switch
            {
                AircraftStatus.Emergency => "MAYDAY",
                AircraftStatus.GoAround  => "GO-AROUND",
                _                        => null
            };

            // Background pill for readability
            double boxW = 108;
            double boxH = badge != null ? 62 : 48;

            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(170, 10, 12, 20)),
                new Rect(lx - 2, ly, boxW, boxH),
                4);

            // Flight ID — bold, coloured by type
            IBrush idColor = ac.Status == AircraftStatus.Emergency
                ? Brushes.OrangeRed
                : ac.FlightType == FlightType.Arrival
                    ? new SolidColorBrush(Color.FromRgb(100, 210, 255))
                    : new SolidColorBrush(Color.FromRgb(100, 255, 160));

            DrawText(ctx, line1, _labelBold, 13, idColor,    new Point(lx + 4, ly + 4));
            DrawText(ctx, line2, _labelFont, 11, Brushes.LightGray, new Point(lx + 4, ly + 20));
            DrawText(ctx, line3, _labelFont, 10, Brushes.Gray,      new Point(lx + 4, ly + 33));

            // Status badge
            if (badge != null)
            {
                IBrush badgeBg = ac.Status == AircraftStatus.Emergency
                    ? new SolidColorBrush(Color.FromArgb(200, 180, 20, 20))
                    : new SolidColorBrush(Color.FromArgb(200, 160, 110, 0));

                ctx.FillRectangle(badgeBg,
                    new Rect(lx - 2, ly + 49, boxW, 13), 3);

                DrawText(ctx, badge, _labelBold, 10, Brushes.White,
                    new Point(lx + 4, ly + 50));
            }
        }

        // ── Emergency beacon ──────────────────────────────────────────────────

        private void DrawEmergencyBeacon(DrawingContext ctx,
                                         string         flightId,
                                         double         x,
                                         double         y)
        {
            if (!_flashAccum.ContainsKey(flightId))
                _flashAccum[flightId] = 0;

            // We advance flash accum using a fixed estimate since we don't
            // have realDeltaMs here — the canvas 60fps loop handles accuracy
            _flashAccum[flightId] += 16.6;

            bool flashOn = (_flashAccum[flightId] % (FlashIntervalMs * 2)) < FlashIntervalMs;

            if (flashOn)
            {
                // Expanding red ring
                double radius = 18 + ((_flashAccum[flightId] % FlashIntervalMs)
                                      / FlashIntervalMs) * 12;

                ctx.DrawEllipse(null,
                    new Pen(new SolidColorBrush(Color.FromArgb(180, 220, 40, 40)), 2),
                    new Point(x, y),
                    radius, radius);

                // Inner solid dot
                ctx.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(200, 255, 60, 60)),
                    null,
                    new Point(x, y), 5, 5);
            }
        }

        // ── Exhaust trails ────────────────────────────────────────────────────

        private void UpdateAndDrawTrails(DrawingContext ctx,
                                         AircraftState  ac,
                                         double         x,
                                         double         y,
                                         double         heading)
        {
            if (!_trails.ContainsKey(ac.FlightId))
                _trails[ac.FlightId] = new List<TrailParticle>();

            var particles = _trails[ac.FlightId];

            bool isFlying = ac.Phase is
                AircraftPhase.Takeoff   or AircraftPhase.Climbing  or
                AircraftPhase.Approaching or AircraftPhase.OnFinal or
                AircraftPhase.GoAround;

            if (isFlying)
            {
                double rad  = heading * Math.PI / 180.0;
                double tailX = x - Math.Cos(rad) * 16;
                double tailY = y - Math.Sin(rad) * 16;

                // Larger planes leave more particles
                int spawnCount = ac.Type switch
                {
                    AircraftType.Large  => 3,
                    AircraftType.Medium => 2,
                    _                   => 1
                };

                for (int i = 0; i < spawnCount; i++)
                    particles.Add(new TrailParticle
                    {
                        X    = tailX + (_rand.NextDouble() * 6 - 3),
                        Y    = tailY + (_rand.NextDouble() * 6 - 3),
                        Life = 1.0,
                        Size = ac.Type == AircraftType.Large ? 5.5 :
                               ac.Type == AircraftType.Medium ? 4.0 : 2.8
                    });
            }

            // Draw and age particles
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Life -= 0.025;

                if (p.Life <= 0) { particles.RemoveAt(i); continue; }

                // Emergency aircraft leave a reddish trail
                Color trailColor = ac.Status == AircraftStatus.Emergency
                    ? Color.FromArgb((byte)(140 * p.Life), 255, 120, 80)
                    : Color.FromArgb((byte)(130 * p.Life), 220, 220, 220);

                ctx.DrawEllipse(
                    new SolidColorBrush(trailColor),
                    null,
                    new Point(p.X, p.Y),
                    p.Size * p.Life,
                    p.Size * p.Life);
            }
        }

        // ── Geometry helpers ──────────────────────────────────────────────────

        private static StreamGeometry MakePath(Point[] points)
        {
            var geo = new StreamGeometry();
            using var ctx = geo.Open();
            ctx.BeginFigure(points[0], true);
            for (int i = 1; i < points.Length; i++)
                ctx.LineTo(points[i]);
            ctx.EndFigure(true);
            return geo;
        }

        private static void DrawText(DrawingContext ctx,
                                     string         text,
                                     Typeface       face,
                                     double         size,
                                     IBrush         brush,
                                     Point          origin)
        {
            var ft = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                face, size, brush);
            ctx.DrawText(ft, origin);
        }

        // ── Math ──────────────────────────────────────────────────────────────

        private static double Lerp(double a, double b, double t) =>
            a + (b - a) * t;

        private static double LerpAngle(double a, double b, double t)
        {
            double diff = b - a;
            while (diff < -180) diff += 360;
            while (diff >  180) diff -= 360;
            return a + diff * t;
        }
    }
}