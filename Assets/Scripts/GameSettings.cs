using UnityEngine;

// Settings the player controls from the Настройки screen (see SettingsMenu), remembered between sessions.
// Graphics (screen resolution, fullscreen) go straight through Unity's own Screen API and aren't stored here -
// only the things that need to be re-applied ourselves live in this file.
public static class GameSettings
{
    const string FovKey = "CameraFov";
    const string FpsKey = "ShowFps";

    public const float MinFov = 50f;
    public const float MaxFov = 90f;
    public const float DefaultFov = 60f;

    public static float CameraFov
    {
        get
        {
            try { return Mathf.Clamp(PlayerPrefs.GetFloat(FovKey, DefaultFov), MinFov, MaxFov); }
            catch (System.Exception) { return DefaultFov; }
        }
        set
        {
            try
            {
                PlayerPrefs.SetFloat(FovKey, Mathf.Clamp(value, MinFov, MaxFov));
                PlayerPrefs.Save();
            }
            catch (System.Exception)
            {
                // Not being able to remember the setting isn't worth an error.
            }
        }
    }

    public static bool ShowFps
    {
        get
        {
            try { return PlayerPrefs.GetInt(FpsKey, 0) != 0; }
            catch (System.Exception) { return false; }
        }
        set
        {
            try
            {
                PlayerPrefs.SetInt(FpsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (System.Exception)
            {
            }
        }
    }
}
