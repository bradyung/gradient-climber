using System.Collections.Generic;
using System.Drawing;

namespace GradientClimber
{
    public class Player
    {
        public double X { get; set; }
        public double Y { get; set; }

        public float GlowPhase { get; set; } = 0f;

        public List<PointF> Trail { get; } = new List<PointF>();

        public Player(double x, double y)
        {
            X = x;
            Y = y;
        }

        public void Reset(double x, double y)
        {
            X = x;
            Y = y;
            GlowPhase = 0f;
            Trail.Clear();
        }
    }
}