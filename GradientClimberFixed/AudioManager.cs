using System.Media;

namespace GradientClimber
{
    public static class AudioManager
    {
        public static void PlayMenu()
        {
            SystemSounds.Asterisk.Play();
        }

        public static void PlayHint()
        {
            SystemSounds.Question.Play();
        }

        public static void PlayFalseSummit()
        {
            SystemSounds.Exclamation.Play();
        }

        public static void PlayLevelComplete()
        {
            SystemSounds.Beep.Play();
        }

        public static void PlayWin()
        {
            SystemSounds.Hand.Play();
        }

        public static void PlayLose()
        {
            SystemSounds.Hand.Play();
        }
    }
}