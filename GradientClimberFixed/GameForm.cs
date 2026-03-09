using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly Random _random = new Random();

        private SaveData _saveData = SaveData.Load();
        private GameMode _gameMode = GameMode.Classic;
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
        private string _lastMedal = "Bronze";

        private const int HudWidth = 340;
        private const double TrailSpacing = 0.20;

        private PointF _lastTrailPoint;
        private bool _showBigHintArrow = false;
        private int _bigHintFrames = 0;

        private bool[,] _explored;
        private readonly int _fogRadiusCells = 9;

        private bool _showPeakAfterWin = false;
        private bool _showPeakMarkerDuringPlay = false;
        private bool _showSmallGradientArrow = true;
        private bool _allowHintArrow = true;
        private bool _allowGradientStep = true;
        private bool _showMiniMap = true;
        private bool _showContoursOnly = false;
        private bool _criticalPointGoal = false;

        private double _criticalTargetX;
        private double _criticalTargetY;

        private bool _playerNearWater = false;
        private bool _playerOnIce = false;
        private double _moveSpeed = 0.16;

        private float _pulseTime = 0f;

        private readonly List<PointF> _falseSummits = new List<PointF>();
        private readonly List<bool> _falseSummitTriggered = new List<bool>();
        private int _falseSummitPenaltySeconds = 8;
        private int _falseSummitPenaltyScore = 100;

        private readonly List<PointF> _winParticles = new List<PointF>();
        private readonly List<PointF> _winParticleVelocity = new List<PointF>();

        private int _endlessRound = 1;

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
            _levels.Clear();

            _levels.Add(new Level(
                "Level 1: Smooth Hill",
                "A simple paraboloid. Follow the gradient uphill toward the global maximum.",
                (x, y) => 12 - (x * x + y * y) / 8.0,
                (x, y) => -x / 4.0,
                (x, y) => -y / 4.0
            ));

            _levels.Add(new Level(
                "Level 2: Wavy Mountain",
                "A hill with ripples. The gradient still points in the direction of steepest ascent.",
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

        private Level CreateRandomEndlessLevel()
        {
            double a = 8.0 + _random.NextDouble() * 6.0;
            double bump1X = -4.0 + _random.NextDouble() * 8.0;
            double bump1Y = -4.0 + _random.NextDouble() * 8.0;
            double bump2X = -4.0 + _random.NextDouble() * 8.0;
            double bump2Y = -4.0 + _random.NextDouble() * 8.0;
            double waveX = 0.6 + _random.NextDouble() * 0.5;
            double waveY = 0.6 + _random.NextDouble() * 0.5;
            double amp = 1.2 + _random.NextDouble() * 1.6;

            return new Level(
                $"Endless Round {_endlessRound}",
                "A randomized multivariable surface. Find the global maximum again.",
                (x, y) =>
                    11
                    - (x * x + y * y) / a
                    + amp * Math.Sin(waveX * x) * Math.Cos(waveY * y)
                    + 1.8 * Math.Exp(-((x - bump1X) * (x - bump1X) + (y - bump1Y) * (y - bump1Y)) / 5.5)
                    + 1.3 * Math.Exp(-((x - bump2X) * (x - bump2X) + (y - bump2Y) * (y - bump2Y)) / 7.0),

                (x, y) =>
                    -2 * x / a
                    + amp * waveX * Math.Cos(waveX * x) * Math.Cos(waveY * y)
                    + 1.8 * Math.Exp(-((x - bump1X) * (x - bump1X) + (y - bump1Y) * (y - bump1Y)) / 5.5) * (-2 * (x - bump1X) / 5.5)
                    + 1.3 * Math.Exp(-((x - bump2X) * (x - bump2X) + (y - bump2Y) * (y - bump2Y)) / 7.0) * (-2 * (x - bump2X) / 7.0),

                (x, y) =>
                    -2 * y / a
                    - amp * waveY * Math.Sin(waveX * x) * Math.Sin(waveY * y)
                    + 1.8 * Math.Exp(-((x - bump1X) * (x - bump1X) + (y - bump1Y) * (y - bump1Y)) / 5.5) * (-2 * (y - bump1Y) / 5.5)
                    + 1.3 * Math.Exp(-((x - bump2X) * (x - bump2X) + (y - bump2Y) * (y - bump2Y)) / 7.0) * (-2 * (y - bump2Y) / 7.0)
            );
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

            _endlessRound = 1;
            _currentLevelIndex = 0;
            _score = 0;
            _lastMedal = "Bronze";

            if (_gameMode == GameMode.Endless)
            {
                _terrain.SetLevel(CreateRandomEndlessLevel());
                _terrainBitmap?.Dispose();
                _terrainBitmap = _terrain.BuildBitmap();
                LoadLevelCommon();
            }
            else
            {
                LoadLevel(_currentLevelIndex);
            }

            _screenState = ScreenState.Playing;
        }

        private void LoadLevel(int index)
        {
            _terrain.SetLevel(_levels[index]);
            _terrainBitmap?.Dispose();
            _terrainBitmap = _terrain.BuildBitmap();
            LoadLevelCommon();
        }

        private void LoadLevelCommon()
        {
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

            _winParticles.Clear();
            _winParticleVelocity.Clear();

            RevealAroundPlayer();
            ShowMessage(_terrain.CurrentLevel.Description);
        }

        private void NextLevel()
        {
            if (_gameMode == GameMode.Endless)
            {
                _endlessRound++;
                _terrain.SetLevel(CreateRandomEndlessLevel());
                _terrainBitmap?.Dispose();
                _terrainBitmap = _terrain.BuildBitmap();
                LoadLevelCommon();
                _screenState = ScreenState.Playing;
                return;
            }

            _currentLevelIndex++;

            if (_currentLevelIndex >= _levels.Count)
            {
                UnlockProgress();
                AudioManager.PlayWin();
                _screenState = ScreenState.GameWon;
            }
            else
            {
                LoadLevel(_currentLevelIndex);
                _screenState = ScreenState.Playing;
            }
        }

        private void ResetFalseSummits()
        {
            _falseSummits.Clear();
            _falseSummitTriggered.Clear();

            if (_gameMode == GameMode.Endless)
            {
                AddFalseSummit(-3.0f, 3.0f);
                AddFalseSummit(4.0f, -2.0f);
                AddFalseSummit(0.5f, 5.0f);
                return;
            }

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

        private void UpdateGame(object? sender, EventArgs e)
        {
            _pulseTime += 0.06f;
            _player.GlowPhase += 0.10f;
            UpdateWinParticles();

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

            _playerNearWater = false;
            _playerOnIce = false;

            double h = _terrain.Height(_player.X, _player.Y);

            if (h < 7.5)
            {
                _playerNearWater = true;
                adjustedFactor *= 0.70;
            }

            if (h > 11.3)
            {
                _playerOnIce = true;
                adjustedFactor *= 1.10;
            }

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
                ShowMessage("Gradient is small here. You may be near a critical point or a trap.");
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
                    AudioManager.PlayFalseSummit();
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
                AudioManager.PlayLose();
                _screenState = ScreenState.GameLost;
                return;
            }

            if (_criticalPointGoal)
            {
                double gx = _terrain.PartialX(_player.X, _player.Y);
                double gy = _terrain.PartialY(_player.X, _player.Y);
                double gradMag = Math.Sqrt(gx * gx + gy * gy);

                if (gradMag < 0.10)
                {
                    int points = 500 + secondsLeft * 8 - _hintUses * 30 - _gradientStepsUsed * 20;
                    _score += Math.Max(100, points);
                    _lastMedal = GetMedalText();
                    _showPeakAfterWin = true;
                    CreateWinParticles();
                    AudioManager.PlayLevelComplete();
                    _screenState = ScreenState.LevelComplete;
                    return;
                }
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
                _lastMedal = GetMedalText();
                CreateWinParticles();
                AudioManager.PlayLevelComplete();
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
            ShowMessage("Gradient step taken: moved in the direction of steepest ascent.");
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
            AudioManager.PlayHint();
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

        private void UnlockProgress()
        {
            if (_difficulty == Difficulty.Normal)
                _saveData.HardUnlocked = true;

            if (_difficulty == Difficulty.Hard)
                _saveData.ExpertUnlocked = true;

            if (_gameMode == GameMode.Classic && _score > _saveData.BestScoreClassic)
                _saveData.BestScoreClassic = _score;

            if (_gameMode == GameMode.Endless && _score > _saveData.BestScoreEndless)
                _saveData.BestScoreEndless = _score;

            _saveData.Save();
        }

        private void CreateWinParticles()
        {
            _winParticles.Clear();
            _winParticleVelocity.Clear();

            PointF center = _terrain.WorldToScreen(_player.X, _player.Y);

            for (int i = 0; i < 25; i++)
            {
                float vx = (float)(_random.NextDouble() * 4 - 2);
                float vy = (float)(_random.NextDouble() * 4 - 2);

                _winParticles.Add(center);
                _winParticleVelocity.Add(new PointF(vx, vy));
            }
        }

        private void UpdateWinParticles()
        {
            for (int i = 0; i < _winParticles.Count; i++)
            {
                PointF p = _winParticles[i];
                PointF v = _winParticleVelocity[i];

                _winParticles[i] = new PointF(p.X + v.X, p.Y + v.Y);
                _winParticleVelocity[i] = new PointF(v.X * 0.98f, v.Y * 0.98f);
            }
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
                    if (_saveData.HardUnlocked)
                    {
                        _difficulty = Difficulty.Hard;
                        AudioManager.PlayMenu();
                        _screenState = ScreenState.ModeSelect;
                    }
                }
                else if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4)
                {
                    if (_saveData.ExpertUnlocked)
                    {
                        _difficulty = Difficulty.Expert;
                        AudioManager.PlayMenu();
                        _screenState = ScreenState.ModeSelect;
                    }
                }
                return;
            }

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
                    if (_gameMode == GameMode.Endless)
                    {
                        _terrain.SetLevel(CreateRandomEndlessLevel());
                        _terrainBitmap?.Dispose();
                        _terrainBitmap = _terrain.BuildBitmap();
                        LoadLevelCommon();
                    }
                    else
                    {
                        LoadLevel(_currentLevelIndex);
                    }
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
            g.SmoothingMode = SmoothingMode.AntiAlias;

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

            if (_screenState == ScreenState.ModeSelect)
            {
                DrawModeScreen(g);
                return;
            }

            if (_screenState == ScreenState.GameWon)
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                DrawOverlay(g, "YOU WON", $"Final Score: {_score}\nBest Medal: {_lastMedal}\nPress Enter to return to title");
                return;
            }

            if (_screenState == ScreenState.GameLost)
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                DrawOverlay(g, "TIME UP", $"Final Score: {_score}\nPress Enter to return to title");
                return;
            }

            if (_showContoursOnly)
            {
                g.Clear(Color.Black);
                DrawContourLines(g);
            }
            else
            {
                g.DrawImage(_terrainBitmap, 0, 0);
            }

            DrawTrail(g);
            DrawFalseSummitMarkersIfDiscovered(g);

            if (_showPeakMarkerDuringPlay || _showPeakAfterWin)
                DrawPeak(g);

            DrawPlayer(g);

            if (_showSmallGradientArrow)
                DrawGradientArrow(g);

            if (_showBigHintArrow)
                DrawBigHintArrow(g);

            DrawWinParticles(g);
            ApplyFogOfWar(g);
            DrawHud(g);
            DrawMiniMap(g);

            if (_screenState == ScreenState.LevelComplete)
            {
                DrawOverlay(g, "LEVEL COMPLETE", $"Medal: {_lastMedal}\nPress Enter for next level");
            }
        }

        private void DrawTitleScreen(Graphics g)
        {
            g.Clear(Color.FromArgb(20, 20, 30));

            using Font title = new Font("Segoe UI", 28, FontStyle.Bold);
            using Font subtitle = new Font("Segoe UI", 13, FontStyle.Regular);
            using Font body = new Font("Segoe UI", 11, FontStyle.Regular);

            g.DrawString("Gradient Climber", title, Brushes.Gold, 245, 90);
            g.DrawString("A replayable multivariable calculus game", subtitle, Brushes.White, 220, 150);

            string text =
                "Explore terrain generated by multivariable functions.\n" +
                "Use gradients, contour clues, and critical-point logic.\n\n" +
                "Features:\n" +
                "- Fog of war\n" +
                "- Special modes\n" +
                "- Endless replayability\n" +
                "- Progression unlocks\n" +
                "- False summits and terrain effects\n\n" +
                "Press Enter to continue";

            g.DrawString(text, body, Brushes.WhiteSmoke, new RectangleF(205, 225, 500, 280));
        }

        private void DrawDifficultyScreen(Graphics g)
        {
            g.Clear(Color.FromArgb(18, 18, 28));

            using Font title = new Font("Segoe UI", 24, FontStyle.Bold);
            using Font body = new Font("Segoe UI", 11, FontStyle.Regular);

            g.DrawString("Choose Difficulty", title, Brushes.Gold, 250, 90);

            string hardText = _saveData.HardUnlocked ? "" : " (Locked)";
            string expertText = _saveData.ExpertUnlocked ? "" : " (Locked)";

            string text =
                $"1 - Easy      : more time, more hints, more gradient steps\n" +
                $"2 - Normal    : balanced challenge\n" +
                $"3 - Hard{hardText}\n" +
                $"4 - Expert{expertText}\n\n" +
                "Higher difficulties reduce assistance and increase pressure.\n" +
                "Expert disables hint arrows, gradient steps, and the small arrow.";

            g.DrawString(text, body, Brushes.White, new RectangleF(170, 190, 560, 250));
        }

        private void DrawModeScreen(Graphics g)
        {
            g.Clear(Color.FromArgb(18, 18, 28));

            using Font title = new Font("Segoe UI", 24, FontStyle.Bold);
            using Font body = new Font("Segoe UI", 11, FontStyle.Regular);

            g.DrawString("Choose Game Mode", title, Brushes.Gold, 225, 90);

            string text =
                "1 - Classic             : reach the global maximum\n" +
                "2 - Contour Only        : play using contour lines instead of colored terrain\n" +
                "3 - Critical Hunt       : win by finding a point where |∇f| is near zero\n" +
                "4 - Endless             : randomized mountains forever\n\n" +
                $"Best Classic Score: {_saveData.BestScoreClassic}\n" +
                $"Best Endless Score: {_saveData.BestScoreEndless}";

            g.DrawString(text, body, Brushes.White, new RectangleF(155, 185, 600, 260));
        }

        private void DrawOverlay(Graphics g, string title, string subtitle)
        {
            using SolidBrush bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            g.FillRectangle(bg, 120, 160, 500, 220);

            using Font titleFont = new Font("Segoe UI", 24, FontStyle.Bold);
            using Font bodyFont = new Font("Segoe UI", 12, FontStyle.Regular);

            g.DrawRectangle(Pens.Gold, 120, 160, 500, 220);
            g.DrawString(title, titleFont, Brushes.Gold, 205, 195);
            g.DrawString(subtitle, bodyFont, Brushes.White, new RectangleF(175, 255, 390, 95));
        }

        private void DrawPeak(Graphics g)
        {
            PointF peak = _terrain.WorldToScreen(_terrain.PeakX, _terrain.PeakY);

            float pulse = 20 + 6 * (float)Math.Sin(_pulseTime * 2f);

            using Pen glow = new Pen(Color.Gold, 4);
            g.DrawEllipse(glow, peak.X - pulse / 2, peak.Y - pulse / 2, pulse, pulse);
            g.FillEllipse(Brushes.Gold, peak.X - 5, peak.Y - 5, 10, 10);
        }

        private void DrawPlayer(Graphics g)
        {
            PointF p = _terrain.WorldToScreen(_player.X, _player.Y);

            float glowRadius = 18 + 3 * (float)Math.Sin(_player.GlowPhase);
            using SolidBrush glow = new SolidBrush(Color.FromArgb(70, 80, 220, 255));
            g.FillEllipse(glow, p.X - glowRadius / 2, p.Y - glowRadius / 2, glowRadius, glowRadius);

            g.FillEllipse(Brushes.Black, p.X - 8, p.Y - 8, 16, 16);
            g.DrawEllipse(Pens.White, p.X - 8, p.Y - 8, 16, 16);
        }

        private void DrawTrail(Graphics g)
        {
            if (_player.Trail.Count < 2) return;

            for (int i = 1; i < _player.Trail.Count; i++)
            {
                int alpha = 40 + (int)(180.0 * i / _player.Trail.Count);
                using Pen pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 2);
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

        private void DrawWinParticles(Graphics g)
        {
            foreach (PointF p in _winParticles)
            {
                g.FillEllipse(Brushes.Gold, p.X - 2, p.Y - 2, 4, 4);
            }
        }

        private void DrawMiniMap(Graphics g)
        {
            if (!_showMiniMap) return;

            int mapX = ClientSize.Width - 150;
            int mapY = 20;
            int mapW = 120;
            int mapH = 120;

            using SolidBrush bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            g.FillRectangle(bg, mapX, mapY, mapW, mapH);
            g.DrawRectangle(Pens.White, mapX, mapY, mapW, mapH);

            for (int row = 0; row < _terrain.GridRows; row += 3)
            {
                for (int col = 0; col < _terrain.GridCols; col += 3)
                {
                    if (_explored[row, col])
                    {
                        int x = mapX + col * mapW / _terrain.GridCols;
                        int y = mapY + row * mapH / _terrain.GridRows;
                        g.FillRectangle(Brushes.DimGray, x, y, 2, 2);
                    }
                }
            }

            float px = mapX + (float)((_player.X - _terrain.WorldMin) / (_terrain.WorldMax - _terrain.WorldMin) * mapW);
            float py = mapY + (float)((_terrain.WorldMax - _player.Y) / (_terrain.WorldMax - _terrain.WorldMin) * mapH);

            g.FillEllipse(Brushes.Cyan, px - 3, py - 3, 6, 6);
        }

        private void DrawContourLines(Graphics g)
        {
            using Pen pen = new Pen(Color.FromArgb(180, 120, 220, 255), 1);

            int cols = _terrain.GridCols;
            int rows = _terrain.GridRows;
            int cell = _terrain.CellSize;

            double minH = double.MaxValue;
            double maxH = double.MinValue;

            double[,] heights = new double[rows, cols];

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    double x = _terrain.WorldMin + (_terrain.WorldMax - _terrain.WorldMin) * col / (cols - 1.0);
                    double y = _terrain.WorldMin + (_terrain.WorldMax - _terrain.WorldMin) * row / (rows - 1.0);

                    double h = _terrain.Height(x, y);
                    heights[row, col] = h;

                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            }

            for (int row = 0; row < rows - 1; row++)
            {
                for (int col = 0; col < cols - 1; col++)
                {
                    int q = (int)(((heights[row, col] - minH) / (maxH - minH)) * 12);
                    int qRight = (int)(((heights[row, col + 1] - minH) / (maxH - minH)) * 12);
                    int qDown = (int)(((heights[row + 1, col] - minH) / (maxH - minH)) * 12);

                    int x = col * cell;
                    int y = row * cell;

                    if (q != qRight)
                    {
                        g.DrawLine(pen, x + cell - 1, y, x + cell - 1, y + cell);
                    }

                    if (q != qDown)
                    {
                        g.DrawLine(pen, x, y + cell - 1, x + cell, y + cell - 1);
                    }
                }
            }
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

            string levelName = (_gameMode == GameMode.Endless)
                ? $"Endless Round {_endlessRound}"
                : (_currentLevelIndex >= 0 && _currentLevelIndex < _levels.Count ? _levels[_currentLevelIndex].Name : "Complete");

            int y = 18;

            g.DrawString(levelName, titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 34;

            g.DrawString($"Difficulty: {_difficulty}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"Mode: {_gameMode}", bodyFont, Brushes.White, mapWidth + 15, y);
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
            y += 26;

            string terrainText = _playerNearWater ? "Terrain: Mud / Water" : _playerOnIce ? "Terrain: Ridge / Ice" : "Terrain: Normal";
            g.DrawString(terrainText, bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;

            string calcText = gradMag < 0.18 ? "Math hint: near critical region" : "Math hint: follow ∇f uphill";
            g.DrawString(calcText, bodyFont, Brushes.White, mapWidth + 15, y);
            y += 30;

            g.DrawString("Resources", titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 32;

            string hintsText = _allowHintArrow ? $"{_maxHints - _hintUses}/{_maxHints}" : "Disabled";
            string stepText = _allowGradientStep ? $"{_maxGradientSteps - _gradientStepsUsed}/{_maxGradientSteps}" : "Disabled";

            g.DrawString($"Hints: {hintsText}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString($"Gradient steps: {stepText}", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 30;

            g.DrawString("Controls", titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 32;
            g.DrawString("W A S D   move", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString("SPACE      gradient step", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString("H               hint arrow", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 22;
            g.DrawString("R               restart", bodyFont, Brushes.White, mapWidth + 15, y);
            y += 30;

            g.DrawString("Mode Goal", titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 30;

            string goalText = _criticalPointGoal
                ? "Find a point where |∇f| is very close to zero."
                : "Reach the global maximum on the terrain.";

            g.DrawString(goalText, smallFont, Brushes.White, new RectangleF(mapWidth + 15, y, 305, 42));
            y += 52;

            g.DrawString("Medal Goal", titleFont, Brushes.Gold, mapWidth + 15, y);
            y += 30;
            g.DrawString("Gold: almost no help\nPlatinum: Expert with no assists", smallFont, Brushes.White, new RectangleF(mapWidth + 15, y, 300, 48));
            y += 60;

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