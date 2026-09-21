using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyXonotic.Menu
{
    /// <summary>Scene names shared by the menu, HUD and Editor build tooling.</summary>
    public static class SceneFlow
    {
        public const string MainMenuScene = "MainMenu";

        public static bool HasMainMenu()
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == MainMenuScene) return true;
            }
            return false;
        }

        public static void LoadMainMenu()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(MainMenuScene, LoadSceneMode.Single);
        }

        public static void LoadMap(string sceneName)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }
}
