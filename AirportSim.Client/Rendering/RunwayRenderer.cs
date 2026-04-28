using System;
using Avalonia;
using Avalonia.Media;

namespace AirportSim.Client.Rendering
{
    public class RunwayRenderer
    {
        public void Render(DrawingContext context, DateTime simTime)
        {
            // 1. Asphalt
            context.FillRectangle(Brushes.DarkGray, new Rect(400, 460, 1200, 40));

            // 2. Centerline
            var centerPen = new Pen(Brushes.White, 2) { DashStyle = DashStyle.Dash };
            context.DrawLine(centerPen, new Point(420, 480), new Point(1580, 480));
            
            // 3. Threshold Lines
            var thresholdPen = new Pen(Brushes.LimeGreen, 4);
            context.DrawLine(thresholdPen, new Point(400, 460), new Point(400, 500));
            context.DrawLine(thresholdPen, new Point(1600, 460), new Point(1600, 500));

            // 4. Lighting System (Active Dusk and Night: 18:00 to 05:59)
            int hour = simTime.Hour;
            if (hour >= 18 || hour < 6)
            {
                DrawRunwayLights(context);
            }
        }

        private void DrawRunwayLights(DrawingContext context)
        {
            // Edge Lights (White dots every 50 units)
            for (int x = 400; x <= 1600; x += 50)
            {
                context.DrawEllipse(Brushes.White, null, new Point(x, 460), 2, 2); // Top edge
                context.DrawEllipse(Brushes.White, null, new Point(x, 500), 2, 2); // Bottom edge
            }

            // PAPI Lights (Glide slope indicator - 2 white, 2 red)
            // Placed near the touchdown zone (x = 1400)
            context.DrawEllipse(Brushes.White, null, new Point(1400, 450), 3, 3);
            context.DrawEllipse(Brushes.White, null, new Point(1410, 450), 3, 3);
            context.DrawEllipse(Brushes.Red, null, new Point(1420, 450), 3, 3);
            context.DrawEllipse(Brushes.Red, null, new Point(1430, 450), 3, 3);
        }
    }
}