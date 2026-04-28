using Avalonia;
using Avalonia.Media;

namespace AirportSim.Client.Rendering
{
    public class GroundRenderer
    {
        public void Render(DrawingContext context)
        {
            // 1. Terrain (Grass below the runway)
            context.FillRectangle(Brushes.DarkOliveGreen, new Rect(0, 480, 2000, 120));
            context.FillRectangle(Brushes.OliveDrab, new Rect(0, 440, 2000, 40));

            // 2. The Control Tower (Base at X:300, Y:380)
            // Tower stalk
            context.FillRectangle(Brushes.Gray, new Rect(280, 380, 40, 100));
            
            // Tower cab (the glass room at the top)
            context.FillRectangle(Brushes.LightSkyBlue, new Rect(265, 360, 70, 20));
            
            // Tower roof
            context.FillRectangle(Brushes.DarkSlateGray, new Rect(260, 355, 80, 5));
        }
    }
}