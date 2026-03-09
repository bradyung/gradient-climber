using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GradientClimber
{
    public class GameForm : Form
    {
        private enum ScreenState
        {
            Title,
            DifficultySelect,
            ModeSelect,
            Playing,
            LevelComplete,
            GameWon,
            GameLost
        }

        private enum Difficulty
        {
            Easy,
            Normal,
            Hard,
            Expert
        }

        private readonly List<Level> _levels = new List<Level>();
        private int _currentLevelIndex = 0;

        private TerrainMap _terrain;
        private Bitmap _terrainBitmap;
        private Player _player;
        private System.Windows.Forms.Timer _gameTimer;
        private readonly HashSet<Keys> _keysDown = new HashSet<Keys>();

        private ScreenState _screenState = ScreenState.Title;
        private Difficulty _difficulty = Difficulty.Normal;

        private DateTime _levelStartTime;
        private int _timeLimitSeconds = 90;

        private int _score = 0;
        private int _gradientStepsUsed = 0;
        private int _hintUses = 0;

        private int _maxHints = 3;
        private int _maxGradientSteps = 5;

        private string _message = "Climb using the gradient.";
        private int _messageFrames = 0;

        private const int HudWidth = 340;
        private const double TrailSpacing = 0.20;

        private PointF _lastTrailPoint;
        private bool _showBigHintArrow = false;
        private int _bigHintFrames = 0;

        private readonly Random _random = new Random();

        private bool[,] _explored;
        private readonly int _fogRadiusCells = 9;

        private bool _showPeakAfterWin = false;

        private int _falseSummitPenaltySeconds = 8;
        private int _falseSummitPenaltyScore = 100;
        private readonly List<PointF> _falseSummits = new List<PointF>();
        private readonly List<bool> _falseSummitTriggered = new List<bool>();

        private double _moveSpeed = 0.16;
        private bool _showSmallGradientArrow = true;
        private bool _allowHintArrow = true;
        private bool _allowGradientStep = true;
        private bool _showPeakMarkerDuringPlay = false;

        private SaveData _saveData = SaveData.Load();
        private GameMode _gameMode = GameMode.Classic;

        private int _menuIndex = 0;
        private int _difficultyIndex = 1;
        private int _modeIndex = 0;

        private float _pulseTime = 0f;
        private readonly List<PointF> _winParticles = new List<PointF>();
        private readonly List<PointF> _winParticleVelocity = new List<PointF>();

        private bool _showMiniMap = true;
        private bool _showContoursOnly = false;
        private bool _criticalPointGoal = false;

        private double _criticalTargetX;
        private double _criticalTargetY;

        private bool _playerNearWater = false;
        private bool _playerOnIce = false;

        public GameForm()
        {
            BuildLevels();

            _terrain = new TerrainMap(95, 95, 7, -10, 10, _levels[0]);
            _terrainBitmap = _terrain.BuildBitmap();
            _player = new Player(-8.5, -8.0);

            _explored = new bool[_terrain.GridRows, _terrain.GridCols];
            _lastTrailPoint = _terrain.WorldToScreen(_player.X, _player.Y);

            ClientSize = new Size(_terrain.GridCols * _terrain.CellSize + HudWidth, _terrain.GridRows * _terrain.CellSize);
            Text = "Gradient Climber";
            DoubleBuffered = true;
            KeyPreview = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            _gameTimer = new System.Windows.Forms.Timer();
            _gameTimer.Interval = 16;
            _gameTimer.Tick += UpdateGame;
            _gameTimer.Start();

            KeyDown += HandleKeyDown;
            KeyUp += HandleKeyUp;
        }

        private void BuildLevels()
        {
            _levels.Add(new Level(
                "Level 1: Smooth Hill",
                "A simple paraboloid. Use the gradient to climb toward the global maximum.",
                (x, y) => 12 - (x * x + y * y) / 8.0,
                (x, y) => -x / 4.0,
                (x, y) => -y / 4.0
            ));

            _levels.Add(new Level(
                "Level 2: Wavy Mountain",
                "A hill with sinusoidal ripples and misleading terrain shapes.",
                (x, y) => 10 - (x * x + y * y) / 12.0 + 2 * Math.Sin(x) * Math.Cos(y),
                (x, y) => -x / 6.0 + 2 * Math.Cos(x) * Math.Cos(y),
                (x, y) => -y / 6.0 - 2 * Math.Sin(x) * Math.Sin(y)
            ));

            _levels.Add(new Level(
                "Level 3: Tricky Peaks",
                "Local highs can fool you, but only the global maximum wins.",
                (x, y) =>
                    10 - (x * x + y * y) / 13.0
                    + 1.5 * Math.Sin(0.9 * x) * Math.Cos(0.8 * y)
                    + 2.2 * Math.Exp(-((x - 2.8) * (x - 2.8) + (y + 1.7) * (y + 1.7)) / 5.0)
                    + 1.4 * Math.Exp(-((x + 3.6) * (x + 3.6) + (y - 2.6) * (y - 2.6)) / 6.0),

                (x, y) =>
                    -2 * x / 13.0
                    + 1.35 * Math.Cos(0.9 * x) * Math.Cos(0.8 * y)
                    + 2.2 * Math.Exp(-((x - 2.8) * (x - 2.8) + (y + 1.7) * (y + 1.7)) / 5.0) * (-2 * (x - 2.8) / 5.0)
                    + 1.4 * Math.Exp(-((x + 3.6) * (x + 3.6) + (y - 2.6) * (y - 2.6)) / 6.0) * (-2 * (x + 3.6) / 6.0),

                (x, y) =>
                    -2 * y / 13.0
                    - 1.2 * Math.Sin(0.9 * x) * Math.Sin(0.8 * y)
                    + 2.2 * Math.Exp(-((x - 2.8) * (x - 2.8) + (y + 1.7) * (y + 1.7)) / 5.0) * (-2 * (y + 1.7) / 5.0)
                    + 1.4 * Math.Exp(-((x + 3.6) * (x + 3.6) + (y - 2.6) * (y - 2.6)) / 6.0) * (-2 * (y - 2.6) / 6.0)
            ));
        }

        private void ApplyDifficultySettings()
        {
            switch (_difficulty)
            {
                case Difficulty.Easy:
                    _timeLimitSeconds = 110;
                    _maxHints = 5;
                    _maxGradientSteps = 8;
                    _moveSpeed = 0.18;
                    _showSmallGradientArrow = true;
                    _allowHintArrow = true;
                    _allowGradientStep = true;
                    _showPeakMarkerDuringPlay = false;
                    break;

                case Difficulty.Normal:
                    _timeLimitSeconds = 90;
                    _maxHints = 3;
                    _maxGradientSteps = 5;
                    _moveSpeed = 0.16;
                    _showSmallGradientArrow = true;
                    _allowHintArrow = true;
                    _allowGradientStep = true;
                    _showPeakMarkerDuringPlay = false;
                    break;

                case Difficulty.Hard:
                    _timeLimitSeconds = 75;
                    _maxHints = 2;
                    _maxGradientSteps = 3;
                    _moveSpeed = 0.15;
                    _showSmallGradientArrow = true;
                    _allowHintArrow = true;
                    _allowGradientStep = true;
                    _showPeakMarkerDuringPlay = false;
                    break;

                case Difficulty.Expert:
                    _timeLimitSeconds = 65;
                    _maxHints = 0;
                    _maxGradientSteps = 0;
                    _moveSpeed = 0.145;
                    _showSmallGradientArrow = false;
                    _allowHintArrow = false;
                    _allowGradientStep = false;
                    _showPeakMarkerDuringPlay = false;
                    break;
            }
        }

        private void StartGame()
        {
            ApplyDifficultySettings();
        
            _showContoursOnly = (_gameMode == GameMode.ContourOnly);
            _criticalPointGoal = (_gameMode == GameMode.CriticalPointHunt);
        
            if (_criticalPointGoal)
            {
                _criticalTargetX = 0;
                _criticalTargetY = 0;
            }
        
            _currentLevelIndex = 0;
            _score = 0;
            LoadLevel(_currentLevelIndex);
            _screenState = ScreenState.Playing;
        }

        private void LoadLevel(int index)
        {
            _terrain.SetLevel(_levels[index]);
            _terrainBitmap?.Dispose();
            _terrainBitmap = _terrain.BuildBitmap();

            _explored = new bool[_terrain.GridRows, _terrain.GridCols];
            ResetFalseSummits();

            PointF spawn = GetRandomSpawnFarFromPeak();
            _player.Reset(spawn.X, spawn.Y);

            _gradientStepsUsed = 0;
            _hintUses = 0;
            _levelStartTime = DateTime.Now;
            _showBigHintArrow = false;
            _bigHintFrames = 0;
            _showPeakAfterWin = false;
            _lastTrailPoint = _terrain.WorldToScreen(_player.X, _player.Y);

            RevealAroundPlayer();
            ShowMessage(_levels[index].Description);
        }

        private void ResetFalseSummits()
        {
            _falseSummits.Clear();
            _falseSummitTriggered.Clear();

            if (_currentLevelIndex == 0)
            {
                AddFalseSummit(-3.5f, 4.2f);
                AddFalseSummit(4.0f, -3.2f);
            }
            else if (_currentLevelIndex == 1)
            {
                AddFalseSummit(-2.0f, 3.2f);
                AddFalseSummit(3.8f, 1.5f);
                AddFalseSummit(-4.1f, -2.7f);
            }
            else
            {
                AddFalseSummit(2.8f, -1.7f);
                AddFalseSummit(-3.6f, 2.6f);
                AddFalseSummit(0.8f, 4.1f);
            }
        }

        private void AddFalseSummit(float x, float y)
        {
            _falseSummits.Add(new PointF(x, y));
            _falseSummitTriggered.Add(false);
        }

        private PointF GetRandomSpawnFarFromPeak()
        {
            for (int i = 0; i < 500; i++)
            {
                double x = _terrain.WorldMin + _random.NextDouble() * (_terrain.WorldMax - _terrain.WorldMin);
                double y = _terrain.WorldMin + _random.NextDouble() * (_terrain.WorldMax - _terrain.WorldMin);

                double dPeak = Distance(x, y, _terrain.PeakX, _terrain.PeakY);
                if (dPeak > 6.5)
                {
                    return new PointF((float)x, (float)y);
                }
            }

            return new PointF(-8.0f, -8.0f);
        }

        private void NextLevel()
        {
            _currentLevelIndex++;

            if (_currentLevelIndex >= _levels.Count)
            {
                _screenState = ScreenState.GameWon;
            }
            else
            {
                LoadLevel(_currentLevelIndex);
                _screenState = ScreenState.Playing;
            }
        }

        private void UpdateGame(object? sender, EventArgs e)
        {
            if (_screenState != ScreenState.Playing)
            {
                Invalidate();
                return;
            }

            HandleMovement();
            UpdateTrail();
            UpdateHintArrow();
            RevealAroundPlayer();
            CheckCriticalPointWarning();
            CheckFalseSummits();
            CheckWinLoss();

            if (_messageFrames > 0)
            {
                _messageFrames--;
            }

            Invalidate();
        }

        private void HandleMovement()
        {
            double dx = 0;
            double dy = 0;

            if (_keysDown.Contains(Keys.W)) dy += _moveSpeed;
            if (_keysDown.Contains(Keys.S)) dy -= _moveSpeed;
            if (_keysDown.Contains(Keys.A)) dx -= _moveSpeed;
            if (_keysDown.Contains(Keys.D)) dx += _moveSpeed;

            double proposedX = _player.X + dx;
            double proposedY = _player.Y + dy;

            double currentHeight = _terrain.Height(_player.X, _player.Y);
            double nextHeight = _terrain.Height(proposedX, proposedY);
            double slopePenalty = Math.Abs(nextHeight - currentHeight);

            double adjustedFactor = 1.0 - Math.Min(0.55, slopePenalty * 0.15);

            _player.X += dx * adjustedFactor;
            _player.Y += dy * adjustedFactor;

            if (_player.X < _terrain.WorldMin) _player.X = _terrain.WorldMin;
            if (_player.X > _terrain.WorldMax) _player.X = _terrain.WorldMax;
            if (_player.Y < _terrain.WorldMin) _player.Y = _terrain.WorldMin;
            if (_player.Y > _terrain.WorldMax) _player.Y = _terrain.WorldMax;
        }

        private void UpdateTrail()
        {
            PointF current = _terrain.WorldToScreen(_player.X, _player.Y);

            float dx = current.X - _lastTrailPoint.X;
            float dy = current.Y - _lastTrailPoint.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist >= TrailSpacing * _terrain.CellSize)
            {
                _player.Trail.Add(current);
                _lastTrailPoint = current;
            }
        }

        private void UpdateHintArrow()
        {
            if (_bigHintFrames > 0)
            {
                _bigHintFrames--;
                _showBigHintArrow = true;
            }
            else
            {
                _showBigHintArrow = false;
            }
        }

        private void RevealAroundPlayer()
        {
            Point playerCell = WorldToCell(_player.X, _player.Y);

            for (int row = playerCell.Y - _fogRadiusCells; row <= playerCell.Y + _fogRadiusCells; row++)
            {
                for (int col = playerCell.X - _fogRadiusCells; col <= playerCell.X + _fogRadiusCells; col++)
                {
                    if (row < 0 || row >= _terrain.GridRows || col < 0 || col >= _terrain.GridCols)
                        continue;

                    int dx = col - playerCell.X;
                    int dy = row - playerCell.Y;

                    if (dx * dx + dy * dy <= _fogRadiusCells * _fogRadiusCells)
                    {
                        _explored[row, col] = true;
                    }
                }
            }
        }

        private Point WorldToCell(double x, double y)
        {
            int col = (int)((x - _terrain.WorldMin) / (_terrain.WorldMax - _terrain.WorldMin) * (_terrain.GridCols - 1));
            int row = (int)((_terrain.WorldMax - y) / (_terrain.WorldMax - _terrain.WorldMin) * (_terrain.GridRows - 1));

            col = Math.Max(0, Math.Min(_terrain.GridCols - 1, col));
            row = Math.Max(0, Math.Min(_terrain.GridRows - 1, row));

            return new Point(col, row);
        }

        private void CheckCriticalPointWarning()
        {
            double gx = _terrain.PartialX(_player.X, _player.Y);
            double gy = _terrain.PartialY(_player.X, _player.Y);
            double gradMag = Math.Sqrt(gx * gx + gy * gy);

            if (gradMag < 0.18)
            {
                ShowMessage("Gradient is small here. This could be a critical point, local max, or trap.");
            }
        }

        private void CheckFalseSummits()
        {
            for (int i = 0; i < _falseSummits.Count; i++)
            {
                if (_falseSummitTriggered[i])
                    continue;

                double d = Distance(_player.X, _player.Y, _falseSummits[i].X, _falseSummits[i].Y);
                if (d < 0.75)
                {
                    _falseSummitTriggered[i] = true;

                    _levelStartTime = _levelStartTime.AddSeconds(-_falseSummitPenaltySeconds);
                    _score = Math.Max(0, _score - _falseSummitPenaltyScore);

                    ShowMessage("False summit! You lost time and score.");
                    break;
                }
            }
        }

        private void CheckWinLoss()
        {
            int secondsLeft = GetSecondsLeft();

            if (secondsLeft <= 0)
            {
                _screenState = ScreenState.GameLost;
                return;
            }

            double d = Distance(_player.X, _player.Y, _terrain.PeakX, _terrain.PeakY);
            if (d < 0.55)
            {
                _showPeakAfterWin = true;

                int timeBonus = secondsLeft * 10;
                int stepPenalty = _gradientStepsUsed * 30;
                int hintPenalty = _hintUses * 40;
                int levelPoints = Math.Max(100, 600 + timeBonus - stepPenalty - hintPenalty);

                _score += levelPoints;
                _screenState = ScreenState.LevelComplete;
            }
        }

        private void TakeGradientStep()
        {
            if (_screenState != ScreenState.Playing || !_allowGradientStep)
                return;

            if (_gradientStepsUsed >= _maxGradientSteps)
            {
                ShowMessage("No gradient steps left.");
                return;
            }

            double gx = _terrain.PartialX(_player.X, _player.Y);
            double gy = _terrain.PartialY(_player.X, _player.Y);

            double mag = Math.Sqrt(gx * gx + gy * gy);
            if (mag < 0.0001)
                return;

            gx /= mag;
            gy /= mag;

            _player.X += gx * 0.55;
            _player.Y += gy * 0.55;

            if (_player.X < _terrain.WorldMin) _player.X = _terrain.WorldMin;
            if (_player.X > _terrain.WorldMax) _player.X = _terrain.WorldMax;
            if (_player.Y < _terrain.WorldMin) _player.Y = _terrain.WorldMin;
            if (_player.Y > _terrain.WorldMax) _player.Y = _terrain.WorldMax;

            _gradientStepsUsed++;
            ShowMessage("Gradient step taken.");
        }

        private void UseHint()
        {
            if (_screenState != ScreenState.Playing || !_allowHintArrow)
                return;

            if (_hintUses >= _maxHints)
            {
                ShowMessage("No hints left.");
                return;
            }

            _hintUses++;
            _showBigHintArrow = true;
            _bigHintFrames = 140;
            ShowMessage("Hint arrow activated.");
        }

        private int GetSecondsLeft()
        {
            int elapsed = (int)(DateTime.Now - _levelStartTime).TotalSeconds;
            return Math.Max(0, _timeLimitSeconds - elapsed);
        }

        private string GetMedalText()
        {
            if (_difficulty == Difficulty.Expert && _hintUses == 0 && _gradientStepsUsed == 0)
                return "Platinum";

            if (_hintUses == 0 && _gradientStepsUsed <= 1)
                return "Gold";

            if (_hintUses <= 1 && _gradientStepsUsed <= 3)
                return "Silver";

            return "Bronze";
        }

        private void HandleKeyDown(object? sender, KeyEventArgs e)
        {
            _keysDown.Add(e.KeyCode);

            if (_screenState == ScreenState.Title)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    AudioManager.PlayMenu();
                    _screenState = ScreenState.DifficultySelect;
                }
                return;
            }

            if (_screenState == ScreenState.DifficultySelect)
            {
                if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1)
                {
                    _difficulty = Difficulty.Easy;
                    AudioManager.PlayMenu();
                    _screenState = ScreenState.ModeSelect;
                }
                else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2)
                {
                    _difficulty = Difficulty.Normal;
                    AudioManager.PlayMenu();
                    _screenState = ScreenState.ModeSelect;
                }
                else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3)
                {
                    _difficulty = Difficulty.Hard;
                    AudioManager.PlayMenu();
                    _screenState = ScreenState.ModeSelect;
                }
                else if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4)
                {
                    _difficulty = Difficulty.Expert;
                    AudioManager.PlayMenu();
                    _screenState = ScreenState.ModeSelect;
                }
                return;
            }

            // ADD THE MODE SCREEN BLOCK HERE
            if (_screenState == ScreenState.ModeSelect)
            {
                if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1)
                {
                    _gameMode = GameMode.Classic;
                    AudioManager.PlayMenu();
                    StartGame();
                }
                else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2)
                {
                    _gameMode = GameMode.ContourOnly;
                    AudioManager.PlayMenu();
                    StartGame();
                }
                else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3)
                {
                    _gameMode = GameMode.CriticalPointHunt;
                    AudioManager.PlayMenu();
                    StartGame();
                }
                else if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4)
                {
                    _gameMode = GameMode.Endless;
                    AudioManager.PlayMenu();
                    StartGame();
                }
                return;
            }

            if (_screenState == ScreenState.Playing)
            {
                if (e.KeyCode == Keys.Space)
                {
                    TakeGradientStep();
                }
                else if (e.KeyCode == Keys.H)
                {
                    UseHint();
                }
                else if (e.KeyCode == Keys.R)
                {
                    LoadLevel(_currentLevelIndex);
                }
            }
            else if (_screenState == ScreenState.LevelComplete && e.KeyCode == Keys.Enter)
            {
                NextLevel();
            }
            else if ((_screenState == ScreenState.GameWon || _screenState == ScreenState.GameLost) && e.KeyCode == Keys.Enter)
            {
                _screenState = ScreenState.Title;
            }
        }

        private void HandleKeyUp(object? sender, KeyEventArgs e)
        {
            _keysDown.Remove(e.KeyCode);
        }

        private void ShowMessage(string text)
        {
            _message = text;
            _messageFrames = 120;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
        
            if (_screenState == ScreenState.Title)
            {
                DrawTitleScreen(g);
                return;
            }
        
            if (_screenState == ScreenState.DifficultySelect)
            {
                DrawDifficultyScreen(g);
                return;
            }
        
            if (_screenState == ScreenState.GameWon)
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                DrawOverlay(g, "YOU WON", $"Final Score: {_score}\nBest Medal: {GetMedalText()}\nPress Enter to return to title");
                return;
            }
        
            if (_screenState == ScreenState.GameLost)
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                DrawOverlay(g, "TIME UP", $"Final Score: {_score}\nPress Enter to return to title");
                return;
            }
        
            g.DrawImage(_terrainBitmap, 0, 0);
        
            DrawTrail(g);
            ApplyFogOfWar(g);
            DrawFalseSummitMarkersIfDiscovered(g);
        
            if (_showPeakMarkerDuringPlay || _showPeakAfterWin)
                DrawPeak(g);
        
            DrawPlayer(g);
        
            if (_showSmallGradientArrow)
                DrawGradientArrow(g);
        
            if (_showBigHintArrow)
                DrawBigHintArrow(g);
        
            DrawHud(g);
        
            if (_screenState == ScreenState.LevelComplete)
            {
                DrawOverlay(g, "LEVEL COMPLETE", $"Medal: {GetMedalText()}\nPress Enter for next level");
            }
        }

        private void DrawTitleScreen(Graphics g)
        {
            g.Clear(Color.FromArgb(20, 20, 30));

            using Font title = new Font("Segoe UI", 26, FontStyle.Bold);
            using Font subtitle = new Font("Segoe UI", 13, FontStyle.Regular);
            using Font body = new Font("Segoe UI", 11, FontStyle.Regular);

            g.DrawString("Gradient Climber", title, Brushes.Gold, 250, 100);
            g.DrawString("A replayable multivariable calculus game", subtitle, Brushes.White, 220, 155);

            string text =
                "Find the global maximum on each terrain.\n\n" +
                "Fog hides unexplored areas.\n" +
                "False summits can waste time and score.\n" +
                "Hints and gradient steps are limited.\n\n" +
                "Press Enter to continue";

            g.DrawString(text, body, Brushes.WhiteSmoke, new RectangleF(220, 235, 470, 240));
        }

        private void DrawDifficultyScreen(Graphics g)
        {
            g.Clear(Color.FromArgb(18, 18, 28));

            using Font title = new Font("Segoe UI", 24, FontStyle.Bold);
            using Font body = new Font("Segoe UI", 11, FontStyle.Regular);

            g.DrawString("Choose Difficulty", title, Brushes.Gold, 255, 95);

            string text =
                "1 - Easy     : more time, more hints, more gradient steps\n" +
                "2 - Normal   : balanced challenge\n" +
                "3 - Hard     : tighter timer and fewer assists\n" +
                "4 - Expert   : no hints, no gradient steps, no small arrow\n\n" +
                "Controls during play:\n" +
                "W A S D = move\n" +
                "SPACE   = gradient step\n" +
                "H       = hint arrow\n" +
                "R       = restart level";

            g.DrawString(text, body, Brushes.White, new RectangleF(185, 205, 560, 260));
        }

        private void DrawOverlay(Graphics g, string title, string subtitle)
        {
            using SolidBrush bg = new SolidBrush(Color.FromArgb(175, 0, 0, 0));
            g.FillRectangle(bg, 120, 160, 500, 220);

            using Font titleFont = new Font("Segoe UI", 24, FontStyle.Bold);
            using Font bodyFont = new Font("Segoe UI", 12, FontStyle.Regular);

            g.DrawRectangle(Pens.Gold, 120, 160, 500, 220);
            g.DrawString(title, titleFont, Brushes.Gold, 210, 200);
            g.DrawString(subtitle, bodyFont, Brushes.White, new RectangleF(180, 255, 380, 95));
        }

        private void DrawPeak(Graphics g)
        {
            PointF peak = _terrain.WorldToScreen(_terrain.PeakX, _terrain.PeakY);

            using Pen glow = new Pen(Color.Gold, 4);
            g.DrawEllipse(glow, peak.X - 12, peak.Y - 12, 24, 24);
            g.FillEllipse(Brushes.Gold, peak.X - 5, peak.Y - 5, 10, 10);
        }

        private void DrawPlayer(Graphics g)
        {
            PointF p = _terrain.WorldToScreen(_player.X, _player.Y);
            g.FillEllipse(Brushes.Black, p.X - 8, p.Y - 8, 16, 16);
            g.DrawEllipse(Pens.White, p.X - 8, p.Y - 8, 16, 16);
        }

        private void DrawTrail(Graphics g)
        {
            if (_player.Trail.Count < 2) return;

            using Pen pen = new Pen(Color.FromArgb(180, 255, 255, 255), 2);
            for (int i = 1; i < _player.Trail.Count; i++)
            {
                g.DrawLine(pen, _player.Trail[i - 1], _player.Trail[i]);
            }
        }

        private void ApplyFogOfWar(Graphics g)
        {
            using SolidBrush unseenBrush = new SolidBrush(Color.FromArgb(235, 0, 0, 0));
            using SolidBrush seenBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));

            for (int row = 0; row < _terrain.GridRows; row++)
            {
                for (int col = 0; col < _terrain.GridCols; col++)
                {
                    int x = col * _terrain.CellSize;
                    int y = row * _terrain.CellSize;

                    if (_explored[row, col])
                    {
                        g.FillRectangle(seenBrush, x, y, _terrain.CellSize, _terrain.CellSize);
                    }
                    else
                    {
                        g.FillRectangle(unseenBrush, x, y, _terrain.CellSize, _terrain.CellSize);
                    }
                }
            }
        }

        private void DrawFalseSummitMarkersIfDiscovered(Graphics g)
        {
            using Pen pen = new Pen(Color.MediumPurple, 2);

            for (int i = 0; i < _falseSummits.Count; i++)
            {
                if (!_falseSummitTriggered[i]) continue;

                PointF p = _terrain.WorldToScreen(_falseSummits[i].X, _falseSummits[i].Y);
                g.DrawEllipse(pen, p.X - 8, p.Y - 8, 16, 16);
                g.DrawLine(pen, p.X - 8, p.Y - 8, p.X + 8, p.Y + 8);
                g.DrawLine(pen, p.X + 8, p.Y - 8, p.X - 8, p.Y + 8);
            }
        }

        private void DrawGradientArrow(Graphics g)
        {
            if (_screenState != ScreenState.Playing && _screenState != ScreenState.LevelComplete) return;

            PointF p = _terrain.WorldToScreen(_player.X, _player.Y);

            double gx = _terrain.PartialX(_player.X, _player.Y);
            double gy = _terrain.PartialY(_player.X, _player.Y);
            double mag = Math.Sqrt(gx * gx + gy * gy);

            if (mag < 0.0001) return;

            gx /= mag;
            gy /= mag;

            float endX = p.X + (float)(gx * 34);
            float endY = p.Y - (float)(gy * 34);

            using Pen pen = new Pen(Color.White, 3);
            g.DrawLine(pen, p.X, p.Y, endX, endY);
            g.FillEllipse(Brushes.White, endX - 4, endY - 4, 8, 8);
        }

        private void DrawBigHintArrow(Graphics g)
        {
            PointF p = _terrain.WorldToScreen(_player.X, _player.Y);

            double gx = _terrain.PartialX(_player.X, _player.Y);
            double gy = _terrain.PartialY(_player.X, _player.Y);
            double mag = Math.Sqrt(gx * gx + gy * gy);

            if (mag < 0.0001) return;

            gx /= mag;
            gy /= mag;

            float endX = p.X + (float)(gx * 85);
            float endY = p.Y - (float)(gy * 85);

            using Pen pen = new Pen(Color.Cyan, 5);
            g.DrawLine(pen, p.X, p.Y, endX, endY);
            g.FillEllipse(Brushes.Cyan, endX - 6, endY - 6, 12, 12);
        }

        private void DrawHud(Graphics g)
        {
            int mapWidth = _terrain.GridCols * _terrain.CellSize;

            using SolidBrush bg = new SolidBrush(Color.FromArgb(240, 25, 25, 25));
            g.FillRectangle(bg, mapWidth, 0, HudWidth, ClientSize.Height);

            using Font titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
            using Font bodyFont = new Font("Segoe UI", 10, FontStyle.Regular);
            using Font smallFont = new Font("Segoe UI", 9, FontStyle.Regular);

            double h = _terrain.Height(_player.X, _player.Y);
            double fx = _terrain.PartialX(_player.X, _player.Y);
            double fy = _terrain.PartialY(_player.X, _player.Y);
            double gradMag = Math.Sqrt(fx * fx + fy * fy);

            int y = 18;

            string levelName = (_currentLevelIndex >= 0 && _currentLevelIndex < _levels.Count)
            ? _levels[_currentLevelIndex].Name
            : "All Levels Complete";

            g.DrawString(levelName, titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 34;

            g.DrawString($"Difficulty: {_difficulty}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"Score: {_score}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"Time left: {GetSecondsLeft()} s", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 26;

            g.DrawString($"Position: ({_player.X:F2}, {_player.Y:F2})", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"f(x,y): {h:F2}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"fx: {fx:F2}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"fy: {fy:F2}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"|∇f|: {gradMag:F2}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 30;

            g.DrawString("Resources", titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 32;
            g.DrawString($"Hints: {_maxHints - _hintUses}/{_maxHints}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"Gradient steps: {_maxGradientSteps - _gradientStepsUsed}/{_maxGradientSteps}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 30;

            g.DrawString("Controls", titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 32;
            g.DrawString("W A S D   move", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString("SPACE      gradient step", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString("H               hint arrow", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString("R               restart level", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 30;

            g.DrawString("Medal Goal", titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 32;
            g.DrawString("Gold: almost no help\nPlatinum: Expert with no assists", smallFont, Brushes.White, new RectangleF(mapWidth + 15, y, 300, 48));
            y += 58;

            if (_messageFrames > 0)
            {
                using SolidBrush msgBg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
                Rectangle rect = new Rectangle(mapWidth + 10, ClientSize.Height - 130, 310, 95);
                g.FillRectangle(msgBg, rect);
                g.DrawRectangle(Pens.Gold, rect);
                g.DrawString(_message, bodyFont, Brushes.White, new RectangleF(mapWidth + 20, ClientSize.Height - 118, 290, 70));
            }
        }

        private double Distance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        }
    }
}