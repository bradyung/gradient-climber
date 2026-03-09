using System;

namespace GradientClimber
{
    public class Level
    {
        public string Name { get; }
        public string Description { get; }
        public Func<double, double, double> HeightFunc { get; }
        public Func<double, double, double> FxFunc { get; }
        public Func<double, double, double> FyFunc { get; }

        public Level(
            string name,
            string description,
            Func<double, double, double> heightFunc,
            Func<double, double, double> fxFunc,
            Func<double, double, double> fyFunc)
        {
            Name = name;
            Description = description;
            HeightFunc = heightFunc;
            FxFunc = fxFunc;
            FyFunc = fyFunc;
        }
    }
}