using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class RadarRenderer
    {
        // ── Sweep line state ──────────────────────────────────────────────────
        private double _sweepAngleDeg   = 0;
        private double _lastRenderEpoch = 0;
        private const double SweepRpm  = 6.0;

        // ── Blip trail ────────────────────────────────────────────────────────
        private class BlipTrail
        {
            public double X, Y;
            public double Age;
        }

        private readonly Dictionary<string, List<BlipTrail>> _trails = new();

        // ── Typefaces ─────────────────────────────────────────────────────────
        private readonly Typeface _font     = new("Courier New");
        private readonly Typeface _fontBold = new("Courier New", FontStyle.Normal, FontWeight.Bold);

        // ── Main entry ────────────────────────────────────────────────────────

        public void Render(DrawingContext ctx,
                           SimSnapshot?   prevSnap,
                           SimSnapshot    snap,
                           double         t,
                           Rect           bounds)
        {
            double now       = Environment.TickCount64;
            double deltaMs   = _lastRenderEpoch == 0 ? 16 : now - _lastRenderEpoch;
            _lastRenderEpoch = now;

            _sweepAngleDeg = (_sweepAngleDeg + SweepRpm * 6.0 * deltaMs / 1000.0) % 360.0;

            double cx     = bounds.X + bounds.Width  * 0.5;
            double cy     = bounds.Y + bounds.Height * 0.5;
            double radius = Math.Min(bounds.Width, bounds.Height) * 0.5 - 4;

            DrawBackground(ctx, cx, cy, radius);

            var clipGeo = new EllipseGeometry(new Rect(cx - radius, cy - radius,
                                                        radius * 2, radius * 2));
            using (ctx.PushGeometryClip(clipGeo))
            {
                DrawRangeRings(ctx, cx, cy, radius);
                DrawSweep(ctx, cx, cy, radius);

                AgeFadeTrails(deltaMs);
                DrawTrails(ctx, cx, cy, radius);

                foreach (var target in snap.ActiveAircraft)
                {
                    var prev = prevSnap?.ActiveAircraft
                                   .FirstOrDefault(a => a.FlightId == target.FlightId)
                               ?? target;

                    double wx = Lerp(prev.Position.X, target.Position.X, t);
                    double wy = Lerp(prev.Position.Y, target.Position.Y, t);

                    double nx = (wx / 2000.0) * 2.0 - 1.0;
                    double ny = (wy /  600.0) * 2.0 - 1.0;

                    double sx = cx + nx * radius;
                    double sy = cy + ny * radius;

                    if (nx * nx + ny * ny > 1.0) continue;

                    RecordTrailPoint(target.FlightId, nx, ny);

                    IBrush blipColor = target.Status switch
                    {
                        AircraftStatus.Emergency => Brushes.Red,
                        AircraftStatus.GoAround  => new SolidColorBrush(Color.FromRgb(255, 200, 0)),
                        _                        => target.FlightType == FlightType.Arrival
                            ? new SolidColorBrush(Color.FromRgb(0, 255, 180))
                            : new SolidColorBrush(Color.FromRgb(100, 220, 255))
                    };

                    DrawDiamond(ctx, sx, sy, 5, blipColor);

                    string alt   = target.AltitudeFt > 50
                        ? $"{target.AltitudeFt / 100:D2}"
                        : "GND";
                    string label = $"{target.FlightId} {alt}";

                    DrawRadarText(ctx, label, sx + 7, sy - 5, 9, blipColor);
                }
            }

            DrawBezel(ctx, cx, cy, radius);
            DrawCompassLabels(ctx, cx, cy, radius);
            DrawHud(ctx, snap, bounds);
        }

        // ── Background ────────────────────────────────────────────────────────

        private static void DrawBackground(DrawingContext ctx,
                                           double cx, double cy, double radius)
        {
            ctx.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(4, 18, 10)),
                null,
                new Point(cx, cy), radius, radius);
        }

        // ── Range rings ───────────────────────────────────────────────────────

        private static void DrawRangeRings(DrawingContext ctx,
                                           double cx, double cy, double radius)
        {
            var ringPen = new Pen(
                new SolidColorBrush(Color.FromArgb(55, 0, 200, 80)), 0.8);

            foreach (double frac in new[] { 0.25, 0.5, 0.75, 1.0 })
                ctx.DrawEllipse(null, ringPen,
                    new Point(cx, cy),
                    radius * frac, radius * frac);

            var crossPen = new Pen(
                new SolidColorBrush(Color.FromArgb(40, 0, 180, 70)), 0.7);
            ctx.DrawLine(crossPen,
                new Point(cx - radius, cy), new Point(cx + radius, cy));
            ctx.DrawLine(crossPen,
                new Point(cx, cy - radius), new Point(cx, cy + radius));
        }

        // ── Sweep wedge ───────────────────────────────────────────────────────

        private void DrawSweep(DrawingContext ctx,
                                double cx, double cy, double radius)
        {
            double sweepRad      = _sweepAngleDeg * Math.PI / 180.0;
            const double wedgeDeg = 25.0;

            var geo = new StreamGeometry();
            using (var gctx = geo.Open())
            {
                gctx.BeginFigure(new Point(cx, cy), true);

                int steps = 12;
                for (int i = 0; i <= steps; i++)
                {
                    double a = sweepRad - (wedgeDeg * i / steps) * Math.PI / 180.0;
                    gctx.LineTo(new Point(
                        cx + Math.Cos(a) * radius,
                        cy + Math.Sin(a) * radius));
                }
                gctx.EndFigure(true);
            }

            ctx.DrawGeometry(
                new SolidColorBrush(Color.FromArgb(28, 0, 255, 100)),
                null, geo);

            ctx.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 255, 100)), 1.2),
                new Point(cx, cy),
                new Point(cx + Math.Cos(sweepRad) * radius,
                          cy + Math.Sin(sweepRad) * radius));
        }

        // ── Blip trails ───────────────────────────────────────────────────────

        private void RecordTrailPoint(string flightId, double nx, double ny)
        {
            if (!_trails.ContainsKey(flightId))
                _trails[flightId] = new List<BlipTrail>();

            _trails[flightId].Add(new BlipTrail { X = nx, Y = ny, Age = 0 });

            var list = _trails[flightId];
            while (list.Count > 8) list.RemoveAt(0);
        }

        private void AgeFadeTrails(double deltaMs)
        {
            foreach (var trail in _trails.Values)
                foreach (var pt in trail)
                    pt.Age = Math.Min(1.0, pt.Age + deltaMs / 2500.0);
        }

        private void DrawTrails(DrawingContext ctx,
                                 double cx, double cy, double radius)
        {
            foreach (var trail in _trails.Values)
            {
                foreach (var pt in trail)
                {
                    if (pt.Age >= 1.0) continue;

                    byte   alpha = (byte)(80 * (1.0 - pt.Age));
                    double sx    = cx + pt.X * radius;
                    double sy    = cy + pt.Y * radius;

                    ctx.DrawEllipse(
                        new SolidColorBrush(Color.FromArgb(alpha, 0, 220, 120)),
                        null,
                        new Point(sx, sy), 2, 2);
                }
            }
        }

        // ── Bezel ─────────────────────────────────────────────────────────────

        private static void DrawBezel(DrawingContext ctx,
                                      double cx, double cy, double radius)
        {
            ctx.DrawEllipse(null,
                new Pen(new SolidColorBrush(Color.FromRgb(0, 140, 60)), 2),
                new Point(cx, cy), radius, radius);

            ctx.DrawEllipse(null,
                new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 255, 120)), 1),
                new Point(cx, cy), radius - 3, radius - 3);
        }

        // ── Compass labels ────────────────────────────────────────────────────

        private void DrawCompassLabels(DrawingContext ctx,
                                       double cx, double cy, double radius)
        {
            var labels = new (string text, double angle)[]
            {
                ("N", -90), ("E", 0), ("S", 90), ("W", 180)
            };

            foreach (var (text, angle) in labels)
            {
                double rad = angle * Math.PI / 180.0;
                double lx  = cx + Math.Cos(rad) * (radius - 11);
                double ly  = cy + Math.Sin(rad) * (radius - 11);
                DrawRadarText(ctx, text, lx - 4, ly - 6, 9,
                    new SolidColorBrush(Color.FromArgb(130, 0, 220, 90)));
            }
        }

        // ── HUD readout ───────────────────────────────────────────────────────

        private void DrawHud(DrawingContext ctx, SimSnapshot snap, Rect bounds)
        {
            double x = bounds.X + 6;
            double y = bounds.Y + bounds.Height - 30;

            int arrivals   = snap.ActiveAircraft.Count(a => a.FlightType == FlightType.Arrival);
            int departures = snap.ActiveAircraft.Count(a => a.FlightType == FlightType.Departure);
            int emergency  = snap.ActiveAircraft.Count(a => a.Status == AircraftStatus.Emergency);

            string line1 = $"ARR:{arrivals:D2}  DEP:{departures:D2}";
            string line2 = emergency > 0 ? $"EMRG:{emergency} !" : $"{snap.SimulatedTime:HH:mm}z";

            IBrush hudColor   = new SolidColorBrush(Color.FromArgb(180, 0, 210, 90));
            IBrush alertColor = new SolidColorBrush(Color.FromArgb(220, 255, 60, 60));

            DrawRadarText(ctx, line1, x, y,      10, hudColor);
            DrawRadarText(ctx, line2, x, y + 13, 10, emergency > 0 ? alertColor : hudColor);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void DrawDiamond(DrawingContext ctx,
                                        double x, double y,
                                        double size, IBrush brush)
        {
            var geo = new StreamGeometry();
            using (var gctx = geo.Open())
            {
                gctx.BeginFigure(new Point(x,        y - size), true);
                gctx.LineTo(new Point(x + size, y       ));
                gctx.LineTo(new Point(x,        y + size));
                gctx.LineTo(new Point(x - size, y       ));
                gctx.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        private void DrawRadarText(DrawingContext ctx,
                                   string  text,
                                   double  x,
                                   double  y,
                                   double  size,
                                   IBrush  brush)
        {
            var ft = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _font, size, brush);
            ctx.DrawText(ft, new Point(x, y));
        }

        private static double Lerp(double a, double b, double t) =>
            a + (b - a) * t;
    }
}