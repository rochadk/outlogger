using System;
using System.IO;
using System.Windows;

namespace LauncherLogout
{
    public partial class App : Application
    {
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);

            string flagPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Outlogger", "auto_logout.flag");

            if (File.Exists(flagPath))
                ShutdownHandler.LogoutAndRestoreDefaults();
        }
    }
}
