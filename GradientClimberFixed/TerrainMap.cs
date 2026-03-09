using System;
using System.Drawing;

namespace GradientClimber
{
    public class TerrainMap
    {
        public int GridCols { get; }
        public int GridRows { get; }
        public int CellSize { get; }
        public double WorldMin { get; }
        public double WorldMax { get; }

        public double PeakX { get; private set; }
        public double PeakY { get; private set; }
        public double PeakHeight { get; private set; }

        public Level CurrentLevel { get; private set; }

        private double[,] _sampleHeights;
        private double _minHeight;
        private double _maxHeight;

        public TerrainMap(int gridCols, int gridRows, int cellSize, double worldMin, double worldMax, Level level)
        {
            GridCols = gridCols;
            GridRows = gridRows;
            CellSize = cellSize;
            WorldMin = worldMin;
            WorldMax = worldMax;
            CurrentLevel = level;

            _sampleHeights = new double[GridRows, GridCols];
            BuildSamples();
        }

        public void SetLevel(Level level)
        {
            CurrentLevel = level;
            BuildSamples();
        }

        public double Height(double x, double y)
        {
            return CurrentLevel.HeightFunc(x, y);
        }

        public double PartialX(double x, double y)
        {
            return CurrentLevel.FxFunc(x, y);
        }

        public double PartialY(double x, double y)
        {
            return CurrentLevel.FyFunc(x, y);
        }

        public PointF WorldToScreen(double x, double y)
        {
            float sx = (float)(((x - WorldMin) / (WorldMax - WorldMin)) * (GridCols * CellSize));
            float sy = (float)(((WorldMax - y) / (WorldMax - WorldMin)) * (GridRows * CellSize));
            return new PointF(sx, sy);
        }

        public Bitmap BuildBitmap()
        {
            Bitmap bmp = new Bitmap(GridCols * CellSize, GridRows * CellSize);

            using Graphics g = Graphics.FromImage(bmp);

            for (int row = 0; row < GridRows; row++)
            {
                for (int col = 0; col < GridCols; col++)
                {
                    double h = _sampleHeights[row, col];
                    double t = (h - _minHeight) / (_maxHeight - _minHeight);
                    using SolidBrush brush = new SolidBrush(HeightToColor(t));
                    g.FillRectangle(brush, col * CellSize, row * CellSize, CellSize, CellSize);
                }
            }

            using Pen contourPen = new Pen(Color.FromArgb(80, 0, 0, 0), 1);

            for (int row = 0; row < GridRows - 1; row++)
            {
                for (int col = 0; col < GridCols - 1; col++)
                {
                    int q = Quantize(_sampleHeights[row, col]);
                    int qRight = Quantize(_sampleHeights[row, col + 1]);
                    int qDown = Quantize(_sampleHeights[row + 1, col]);

                    int x = col * CellSize;
                    int y = row * CellSize;

                    if (q != qRight)
                    {
                        g.DrawLine(contourPen, x + CellSize - 1, y, x + CellSize - 1, y + CellSize);
                    }

                    if (q != qDown)
                    {
                        g.DrawLine(contourPen, x, y + CellSize - 1, x + CellSize, y + CellSize - 1);
                    }
                }
            }

            return bmp;
        }

        private void BuildSamples()
        {
            _minHeight = double.MaxValue;
            _maxHeight = double.MinValue;
            PeakHeight = double.MinValue;

            for (int row = 0; row < GridRows; row++)
            {
                for (int col = 0; col < GridCols; col++)
                {
                    double x = WorldMin + (WorldMax - WorldMin) * col / (GridCols - 1.0);
                    double y = WorldMin + (WorldMax - WorldMin) * row / (GridRows - 1.0);

                    double h = Height(x, y);
                    _sampleHeights[row, col] = h;

                    if (h < _minHeight) _minHeight = h;
                    if (h > _maxHeight) _maxHeight = h;

                    if (h > PeakHeight)
                    {
                        PeakHeight = h;
                        PeakX = x;
                        PeakY = y;
                    }
                }
            }
        }

        private int Quantize(double h)
        {
            double t = (h - _minHeight) / (_maxHeight - _minHeight);
            return (int)(t * 14);
        }

        private Color HeightToColor(double t)
        {
            if (t < 0.10) return Color.MidnightBlue;
            if (t < 0.22) return Color.RoyalBlue;
            if (t < 0.36) return Color.SeaGreen;
            if (t < 0.50) return Color.ForestGreen;
            if (t < 0.64) return Color.Goldenrod;
            if (t < 0.78) return Color.DarkOrange;
            if (t < 0.90) return Color.IndianRed;
            return Color.WhiteSmoke;
        }
    }
}