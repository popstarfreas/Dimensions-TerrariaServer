using System.IO;
using Newtonsoft.Json;

namespace Dimensions
{
    public class Config
    {
        public bool EnableGeoIP;

        public void Write(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static Config Read(string path)
        {
            if (!File.Exists(path))
            {
                Config.WriteTemplates(path);
            }
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
        }

        public static void WriteTemplates(string file)
        {
            var Conf = new Config();
            Conf.EnableGeoIP = false;
            Conf.Write(file);
        }
    }
}
