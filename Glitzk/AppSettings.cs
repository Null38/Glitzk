using System.ComponentModel;
using System.IO;
using System.Reflection;
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


            [XmlIgnore]
            public Dictionary<string, string> Commands;

            [XmlArray("Commands")]
            [XmlArrayItem("Command")]
            [EditorBrowsable(EditorBrowsableState.Never)]
            public Command[] CommandArray
            {
                get => Commands.Select(pair => new Command { Key = pair.Key, Value = pair.Value }).ToArray();
                set
                {
                    Commands = new Dictionary<string, string>();
                    if (value == null) return;
                    foreach (var command in value)
                        Commands[command.Key] = command.Value;
                }
            }


            public struct Command
            {
                [XmlAttribute("key")]
                public string Key;

                [XmlText]
                public string Value;
            }

            public SaveData()
            {
                ClientId = string.Empty;
                ClientSecret = string.Empty;
                AccessToken = null;
                RefreshToken = null;

                Commands = new Dictionary<string, string>
                {
                    ["!sr"] = "Song Request",
                    ["!¤¤¤¡"] = "Song Request",
                };
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
            object boxed = data;

            foreach (var field in typeof(SaveData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = field.GetValue(loaded);
                if (field.FieldType.IsInstanceOfType(value))
                    field.SetValue(boxed, value);
            }

            data = (SaveData)boxed;
        }
    }
}
