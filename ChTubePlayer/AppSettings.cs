using System.IO;
using System.Xml.Serialization;

namespace ChTubePlayer
{
    public class AppSettings
    {
        public struct SaveData
        {
            public string ClientId;
            public string ClientSecret;
            public string? AccessToken;
            public string? RefreshToken;

            //[XmlArray("Commands")]
            //[XmlArrayItem("Command")]
            //public string[] Commands;

            public SaveData()
            {
                ClientId = string.Empty;
                ClientSecret = string.Empty;
                AccessToken = null;
                RefreshToken = null;
            }
        }

        private static readonly string FilePath =
            Path.Combine(AppContext.BaseDirectory, "program.setting");

        public SaveData data = new();
        public bool isTest;


        public void Save()
        {
            var serializer = new XmlSerializer(typeof(SaveData));
            using var writer = new StreamWriter(FilePath);
            serializer.Serialize(writer, data);
        }

        public void Load()
        {
            if (!File.Exists(FilePath))
            {
                Save();
                return;
            }

            try
            {
                var serializer = new XmlSerializer(typeof(SaveData));
                using var reader = new StreamReader(FilePath);
                var loaded = (SaveData?)serializer.Deserialize(reader);
                if (loaded.HasValue)
                    Merge(loaded.Value);
            }
            catch (InvalidOperationException)
            {
                data = new();
            }
        }

        private void Merge(SaveData loaded)
        {
            if (!string.IsNullOrEmpty(loaded.ClientId)) data.ClientId = loaded.ClientId;
            if (!string.IsNullOrEmpty(loaded.ClientSecret)) data.ClientSecret = loaded.ClientSecret;
            if (loaded.AccessToken != null) data.AccessToken = loaded.AccessToken;
            if (loaded.RefreshToken != null) data.RefreshToken = loaded.RefreshToken;
        }
    }
}
