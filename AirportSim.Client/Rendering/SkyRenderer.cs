using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace AirportSim.Client.Rendering
{
    public class Cloud
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Scale { get; set; }
        public double Speed { get; set; }
    }

    public class SkyRenderer
    {
        private readonly List<Cloud> _clouds = new();
        private readonly Random _rand = new();

        public SkyRenderer()
        {
            // Generate 15 random clouds
            for (int i = 0; i < 15; i++)
            {
                _clouds.Add(new Cloud
                {
                    X = _rand.Next(0, 2000),
                    Y = _rand.Next(20, 300),
                    Scale = _rand.NextDouble() * 1.5 + 0.5,
                    Speed = _rand.NextDouble() * 0.5 + 0.1
                });
            }
        }

        public void Render(DrawingContext context, DateTime simTime)
        {
            IBrush skyBrush = Brushes.DeepSkyBlue; 
            int hour = simTime.Hour;

            if (hour >= 19 || hour < 5) skyBrush = Brushes.MidnightBlue; 
            else if (hour == 5) skyBrush = Brushes.LightSalmon; 
            else if (hour == 18) skyBrush = Brushes.DarkOrange; 

            // Draw Sky
            context.FillRectangle(skyBrush, new Rect(0, 0, 2000, 600));

            // Determine Cloud Color (dark gray at night, semi-transparent white in the day)
            IBrush cloudBrush = (hour >= 19 || hour < 5) 
                ? new SolidColorBrush(Color.FromArgb(100, 100, 100, 120)) 
                : new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));

            // Draw and move clouds
            foreach (var cloud in _clouds)
            {
                cloud.X -= cloud.Speed;
                if (cloud.X < -200) cloud.X = 2200; // Wrap around

                // FIXED: Explicitly cast the corner radius to a float
                context.FillRectangle(cloudBrush, new Rect(cloud.X, cloud.Y, 100 * cloud.Scale, 30 * cloud.Scale), (float)(15 * cloud.Scale));
            }
        }
    }
}