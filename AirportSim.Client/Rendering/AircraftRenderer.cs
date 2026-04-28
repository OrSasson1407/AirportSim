using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class AircraftRenderer
    {
        private readonly Typeface _labelTypeface = new("Arial");
        private readonly Random _rand = new();

        // Particle system for the exhaust trails
        private class TrailParticle
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Life { get; set; }
        }
        
        // Keeps track of the exhaust particles for each specific flight
        private readonly Dictionary<string, List<TrailParticle>> _trails = new();

        public void Render(DrawingContext context, SimSnapshot? prevSnap, SimSnapshot targetSnap, double t)
        {
            // Clean up memory: Remove trails for planes that have already departed or parked
            var activeIds = targetSnap.ActiveAircraft.Select(a => a.FlightId).ToHashSet();
            var deadTrails = _trails.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var k in deadTrails) _trails.Remove(k);

            foreach (var targetAc in targetSnap.ActiveAircraft)
            {
                var prevAc = prevSnap?.ActiveAircraft.FirstOrDefault(a => a.FlightId == targetAc.FlightId) ?? targetAc;

                double currentX = Lerp(prevAc.Position.X, targetAc.Position.X, t);
                double currentY = Lerp(prevAc.Position.Y, targetAc.Position.Y, t);
                double currentHeading = LerpAngle(prevAc.Heading, targetAc.Heading, t);

                // 1. Draw the exhaust trails behind the plane
                UpdateAndDrawTrails(context, targetAc, currentX, currentY, currentHeading);

                // 2. Draw the plane and its label
                using (context.PushTransform(Matrix.CreateTranslation(currentX, currentY)))
                {
                    DrawAircraftShape(context, targetAc, currentHeading);
                    DrawAircraftLabel(context, targetAc, currentY);
                }
            }
        }

        private void UpdateAndDrawTrails(DrawingContext context, AircraftState ac, double x, double y, double heading)
        {
            if (!_trails.ContainsKey(ac.FlightId)) _trails[ac.FlightId] = new List<TrailParticle>();
            var particles = _trails[ac.FlightId];

            // Only spawn exhaust trails if the plane is actively flying
            bool isFlying = ac.Phase is AircraftPhase.Takeoff or AircraftPhase.Climbing or AircraftPhase.Approaching or AircraftPhase.OnFinal;
            
            if (isFlying)
            {
                // Calculate the exact back of the plane based on its heading
                double rad = heading * Math.PI / 180.0;
                double backX = x - Math.Cos(rad) * 15;
                double backY = y - Math.Sin(rad) * 15;

                // Add a new particle with slight randomness for a scattered smoke effect
                particles.Add(new TrailParticle 
                { 
                    X = backX + (_rand.NextDouble() * 4 - 2), 
                    Y = backY + (_rand.NextDouble() * 4 - 2), 
                    Life = 1.0 
                });
            }

            // Draw and age all existing particles
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Life -= 0.03; // Fade out speed
                
                if (p.Life <= 0)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                // Create a semi-transparent white brush that fades as Life goes down
                IBrush particleBrush = new SolidColorBrush(Color.FromArgb((byte)(150 * p.Life), 255, 255, 255));
                context.DrawEllipse(particleBrush, null, new Point(p.X, p.Y), 4 * p.Life, 4 * p.Life);
            }
        }

        private double Lerp(double start, double end, double t) => start + (end - start) * t;

        private double LerpAngle(double start, double end, double t)
        {
            double diff = end - start;
            while (diff < -180) diff += 360;
            while (diff > 180) diff -= 360;
            return start + diff * t;
        }

        private void DrawAircraftShape(DrawingContext context, AircraftState aircraft, double currentHeading)
        {
            IBrush planeColor = aircraft.FlightType == FlightType.Arrival ? Brushes.DarkOrange : Brushes.Cyan;
            
            double size = aircraft.Type switch
            {
                AircraftType.Small => 20,
                AircraftType.Medium => 35,
                AircraftType.Large => 55,
                _ => 20
            };

            using (context.PushTransform(Matrix.CreateRotation(currentHeading * Math.PI / 180.0)))
            {
                var geometry = new StreamGeometry();
                using (var geomContext = geometry.Open())
                {
                    geomContext.BeginFigure(new Point(size / 2, 0), true);
                    geomContext.LineTo(new Point(-size / 2, -size / 3));
                    geomContext.LineTo(new Point(-size / 2, size / 3));
                    geomContext.EndFigure(true);
                }
                context.DrawGeometry(planeColor, new Pen(Brushes.White, 1), geometry);
            }
        }

        private void DrawAircraftLabel(DrawingContext context, AircraftState aircraft, double currentY)
        {
            // Calculate pseudo-altitude based on the Y coordinate (Ground is Y=480, Max height is Y=100)
            int altitude = (int)Math.Max(0, (480 - currentY) * 26); 
            string altText = altitude > 50 ? $"\nAlt: {altitude} ft" : "";

            var formattedText = new FormattedText(
                $"{aircraft.FlightId}\n{aircraft.Phase}{altText}",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _labelTypeface,
                12,
                Brushes.White);

            context.DrawLine(new Pen(Brushes.White, 1), new Point(0, 0), new Point(20, -30));
            context.DrawText(formattedText, new Point(25, -45));
        }
    }
}