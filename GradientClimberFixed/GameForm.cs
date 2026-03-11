using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GradientClimber
{
    public class GameForm : Form
    {
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

        private const int HudWidth = 240;
        private const double TrailSpacing = 0.20;

        private PointF _lastTrailPoint;
        private bool _showBigHintArrow = false;
        private int _bigHintFrames = 0;

        private bool[,] _explored;
        private readonly int _fogRadiusCells = 12;

        private bool _showPeakAfterWin = false;
        private bool _showSmallGradientArrow = true;
        private bool _allowHintArrow = true;
        private bool _allowGradientStep = true;

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

            _terrain = new TerrainMap(100, 100, 6, -10, 10, _levels[0]);
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
                "Level 1",
                "A smooth hill. Follow the gradient uphill.",
                (x, y) => 12 - (x * x + y * y) / 8.0,
                (x, y) => -x / 4.0,
                (x, y) => -y / 4.0
            ));

            _levels.Add(new Level(
                "Level 2",
                "A hill with waves and local variation.",
                (x, y) => 10 - (x * x + y * y) / 12.0 + 2 * Math.Sin(x) * Math.Cos(y),
                (x, y) => -x / 6.0 + 2 * Math.Cos(x) * Math.Cos(y),
                (x, y) => -y / 6.0 - 2 * Math.Sin(x) * Math.Sin(y)
            ));

            _levels.Add(new Level(
                "Level 3",
                "Local highs can fool you. Find the true peak.",
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
                $"Round {_endlessRound}",
                "A randomized mountain. Find the highest point.",
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
                    break;

                case Difficulty.Normal:
                    _timeLimitSeconds = 90;
                    _maxHints = 3;
                    _maxGradientSteps = 5;
                    _moveSpeed = 0.16;
                    _showSmallGradientArrow = true;
                    _allowHintArrow = true;
                    _allowGradientStep = true;
                    break;

                case Difficulty.Hard:
                    _timeLimitSeconds = 75;
                    _maxHints = 2;
                    _maxGradientSteps = 3;
                    _moveSpeed = 0.15;
                    _showSmallGradientArrow = true;
                    _allowHintArrow = true;
                    _allowGradientStep = true;
                    break;

                case Difficulty.Expert:
                    _timeLimitSeconds = 65;
                    _maxHints = 0;
                    _maxGradientSteps = 0;
                    _moveSpeed = 0.145;
                    _showSmallGradientArrow = false;
                    _allowHintArrow = false;
                    _allowGradientStep = false;
                    break;
            }
        }

        private void StartGame()
        {
            ApplyDifficultySettings();

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
            _player.Trail.Clear();

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
            }
            else
            {
                AddFalseSummit(2.8f, -1.7f);
                AddFalseSummit(-3.6f, 2.6f);
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
                    ShowMessage("False summit! Lost time and score.");
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

            double dx = _terrain.PeakX - _player.X;
            double dy = _terrain.PeakY - _player.Y;
            double mag = Math.Sqrt(dx * dx + dy * dy);

            if (mag < 0.0001)
                return;

            dx /= mag;
            dy /= mag;

            _player.X += dx * 0.55;
            _player.Y += dy * 0.55;

            if (_player.X < _terrain.WorldMin) _player.X = _terrain.WorldMin;
            if (_player.X > _terrain.WorldMax) _player.X = _terrain.WorldMax;
            if (_player.Y < _terrain.WorldMin) _player.Y = _terrain.WorldMin;
            if (_player.Y > _terrain.WorldMax) _player.Y = _terrain.WorldMax;

            _gradientStepsUsed++;
            ShowMessage("Gradient step used.");
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
                MenuRenderer.DrawTitleScreen(g, ClientSize);
                return;
            }

            if (_screenState == ScreenState.DifficultySelect)
            {
                MenuRenderer.DrawDifficultyScreen(g, ClientSize, _saveData);
                return;
            }

            if (_screenState == ScreenState.ModeSelect)
            {
                MenuRenderer.DrawModeScreen(g, ClientSize, _saveData);
                return;
            }

            if (_screenState == ScreenState.GameWon)
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                MenuRenderer.DrawOverlay(g, ClientSize, "YOU WON", $"Final Score: {_score}\nBest Medal: {_lastMedal}\nPress Enter");
                return;
            }

            if (_screenState == ScreenState.GameLost)
            {
                g.Clear(Color.FromArgb(20, 20, 30));
                MenuRenderer.DrawOverlay(g, ClientSize, "TIME UP", $"Final Score: {_score}\nPress Enter");
                return;
            }

            GameRenderer.DrawWorld(
            g,
            _terrain,
            _terrainBitmap,
            _player,
            _falseSummits,
            _falseSummitTriggered,
            _winParticles,
            _showPeakAfterWin,
            _showSmallGradientArrow,
            _showBigHintArrow,
            _pulseTime,
            _fogRadiusCells
            );

            HudRenderer.Draw(
                g,
                mapWidth: _terrain.GridCols * _terrain.CellSize,
                hudWidth: HudWidth,
                clientHeight: ClientSize.Height,
                levelName: _gameMode == GameMode.Endless ? $"Endless {_endlessRound}" : _levels[_currentLevelIndex].Name,
                difficulty: _difficulty.ToString(),
                mode: _gameMode.ToString(),
                score: _score,
                timeLeft: GetSecondsLeft(),
                playerX: _player.X,
                playerY: _player.Y,
                height: _terrain.Height(_player.X, _player.Y),
                gradientMagnitude: Math.Sqrt(
                    _terrain.PartialX(_player.X, _player.Y) * _terrain.PartialX(_player.X, _player.Y) +
                    _terrain.PartialY(_player.X, _player.Y) * _terrain.PartialY(_player.X, _player.Y)),
                hintsLeft: _allowHintArrow ? (_maxHints - _hintUses).ToString() : "Off",
                stepsLeft: _allowGradientStep ? (_maxGradientSteps - _gradientStepsUsed).ToString() : "Off",
                goalText: "Reach the highest point",
                message: _messageFrames > 0 ? _message : ""
            );

            if (_screenState == ScreenState.LevelComplete)
            {
                MenuRenderer.DrawOverlay(g, ClientSize, "LEVEL COMPLETE", $"Medal: {_lastMedal}\nPress Enter");
            }
        }

        private double Distance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        }
    }
}