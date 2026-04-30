using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class AircraftRenderer
    {
        // ── Typefaces ─────────────────────────────────────────────────────────
        private readonly Typeface _labelFont    = new("Arial");
        private readonly Typeface _labelBold    = new("Arial", FontStyle.Normal, FontWeight.Bold);

        // ── Cached Global Resources ───────────────────────────────────────────
        private bool _resourcesLoaded = false;
        private StreamGeometry? _planeGeometry;
        private IBrush _brushEmergency = Brushes.Red;
        private IBrush _brushGoAround = Brushes.Orange;
        private IBrush _brushArrival = Brushes.LightGreen;
        private IBrush _brushDeparture = Brushes.Cyan;

        // ── Exhaust trail particles ───────────────────────────────────────────
        private class TrailParticle
        {
            public double X, Y, Life, Size;
        }

        private readonly Dictionary<string, List<TrailParticle>> _trails   = new();
        private readonly Random                                  _rand     = new();

        // ── Emergency flash state per aircraft ────────────────────────────────
        private readonly Dictionary<string, double> _flashAccum = new();
        private const double FlashIntervalMs = 350;

        private void EnsureResourcesLoaded()
        {
            if (_resourcesLoaded) return;
            
            if (Application.Current != null)
            {
                Application.Current.TryFindResource("AtcIconPlane", out var plane);
                _planeGeometry = plane as StreamGeometry;

                if (Application.Current.TryFindResource("AtcDanger", out var danger))
                    _brushEmergency = (IBrush)danger!;

                if (Application.Current.TryFindResource("AtcWarning", out var warning))
                    _brushGoAround = (IBrush)warning!;

                if (Application.Current.TryFindResource("AtcRunwayArrival", out var arrival))
                    _brushArrival = (IBrush)arrival!;

                if (Application.Current.TryFindResource("AtcRunwayDeparture", out var departure))
                    _brushDeparture = (IBrush)departure!;
            }
            _resourcesLoaded = true;
        }

        // ── Main entry point ──────────────────────────────────────────────────

        public void Render(DrawingContext ctx,
                           SimSnapshot?   prevSnap,
                           SimSnapshot    targetSnap,
                           double         t)
        {
            EnsureResourcesLoaded();

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
            if (_planeGeometry == null) return;

            IBrush fill = GetAircraftColor(ac);
            
            double scale = ac.Type switch
            {
                AircraftType.Small => 0.8,
                AircraftType.Large => 1.8,
                _ => 1.2
            };

            var outline = new Pen(Brushes.White, 1.0 / scale);

            using (ctx.PushTransform(
                Matrix.CreateTranslation(-12, -12) * 
                Matrix.CreateScale(scale, scale) * 
                Matrix.CreateRotation((heading + 90) * Math.PI / 180.0)))
            {
                ctx.DrawGeometry(fill, outline, _planeGeometry);

                // NEW: Draw a pushback tug attached to the nose
                if (ac.Phase == AircraftPhase.Pushback)
                {
                    // In SVG space, the plane's nose is at (11.5, 2)
                    // We draw a small golden rectangle right in front of it
                    var tugRect = new Rect(10, -4, 4, 6);
                    ctx.FillRectangle(Brushes.Goldenrod, tugRect);
                }
            }
        }

        // ── Colour logic ──────────────────────────────────────────────────────

        private IBrush GetAircraftColor(AircraftState ac)
        {
            if (ac.Status == AircraftStatus.Emergency)
                return _brushEmergency;

            if (ac.Status == AircraftStatus.GoAround)
                return _brushGoAround;

            return ac.FlightType == FlightType.Arrival
                ? _brushArrival
                : _brushDeparture;
        }

        // ── Labels ────────────────────────────────────────────────────────────

        private void DrawLabel(DrawingContext ctx,
                               AircraftState  ac,
                               double         worldX,
                               double         worldY)
        {
            const double lx = 22;
            const double ly = -42;

            ctx.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 0.8),
                new Point(0, 0),
                new Point(lx, ly + 36));

            string line1 = ac.FlightId;
            string line2 = ac.Phase.ToString();
            string line3 = ac.AltitudeFt > 50
                ? $"{ac.AltitudeFt:N0} ft  {ac.SpeedKts} kts"
                : $"Ground  {ac.SpeedKts} kts";

            string? badge = ac.Status switch
            {
                AircraftStatus.Emergency => "MAYDAY",
                AircraftStatus.GoAround  => "GO-AROUND",
                AircraftStatus.Diverting => "DIVERTED",
                _                        => null
            };

            double boxW = 108;
            double boxH = badge != null ? 62 : 48;

            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(170, 10, 12, 20)),
                new Rect(lx - 2, ly, boxW, boxH),
                4);

            IBrush idColor = GetAircraftColor(ac);

            DrawText(ctx, line1, _labelBold, 13, idColor,     new Point(lx + 4, ly + 4));
            DrawText(ctx, line2, _labelFont, 11, Brushes.LightGray, new Point(lx + 4, ly + 20));
            DrawText(ctx, line3, _labelFont, 10, Brushes.Gray,      new Point(lx + 4, ly + 33));

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

            _flashAccum[flightId] += 16.6;

            bool flashOn = (_flashAccum[flightId] % (FlashIntervalMs * 2)) < FlashIntervalMs;

            if (flashOn)
            {
                double radius = 18 + ((_flashAccum[flightId] % FlashIntervalMs)
                                      / FlashIntervalMs) * 12;

                ctx.DrawEllipse(null,
                    new Pen(new SolidColorBrush(Color.FromArgb(180, 220, 40, 40)), 2),
                    new Point(x, y),
                    radius, radius);

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
                AircraftPhase.GoAround or AircraftPhase.Diverted;

            if (isFlying)
            {
                double rad  = heading * Math.PI / 180.0;
                double tailX = x - Math.Cos(rad) * 16;
                double tailY = y - Math.Sin(rad) * 16;

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

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Life -= 0.025;

                if (p.Life <= 0) { particles.RemoveAt(i); continue; }

                Color trailColor = ac.Status == AircraftStatus.Emergency || ac.Status == AircraftStatus.Diverting
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