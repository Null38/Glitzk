namespace Glitzk.Storage
{
    class IOSystem
    {
        readonly LogWriter logWriter = new();
        readonly SettingsStore settingsStore = new();

        private AppSettings Settings => App.Settings;

        public IOSystem()
        {
            settingsStore.ExceptionHandler = AppendLog;
        }

        public void AppendLog(Exception ex)
        {
            AppendLog(ex, DateTimeOffset.UtcNow);
        }

        public void AppendLog(Exception ex, DateTimeOffset dateTime)
        {
            if (Settings.SaveLog)
                logWriter.AppendLog(ex, dateTime);
        }

        public void Save()
        {
            settingsStore.Save(Settings);
        }
    }
}
