using System.IO;
using System.Text.Json;

namespace GradientClimber
{
    public class SaveData
    {
        public bool NormalUnlocked { get; set; } = true;
        public bool HardUnlocked { get; set; } = false;
        public bool ExpertUnlocked { get; set; } = false;

        public int BestScoreClassic { get; set; } = 0;
        public int BestScoreEndless { get; set; } = 0;

        public static string FilePath => "savegame.json";

        public static SaveData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    SaveData? data = JsonSerializer.Deserialize<SaveData>(json);
                    if (data != null) return data;
                }
            }
            catch
            {
            }

            return new SaveData();
        }

        public void Save()
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}