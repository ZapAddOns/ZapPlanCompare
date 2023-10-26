using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using ZapClient;
using ZapClient.Helpers;

namespace ZapPlanCompare
{
    public class CompareConfig
    {
        public string Culture;
        public bool LineColorChanging = true;
        public Dictionary<string, byte[]> StructureColors;

        public static CompareConfig LoadConfigData(string filename = "")
        {
            // Check first for filename with hostname, then with network adress or, at the end, use this without
            if (string.IsNullOrEmpty(filename))
            {
                filename = "ZapPlanCompare." + Network.GetHostName() + ".cfg";

                if (!File.Exists(filename))
                {
                    filename = "ZapPlanCompare." + Network.GetIPAdress() + ".cfg";
                }

                if (!File.Exists(filename))
                {
                    filename = "ZapPlanCompare.cfg";
                }

                if (!File.Exists(filename))
                {
                    var config = new CompareConfig
                    {
                        Culture = System.Threading.Thread.CurrentThread.CurrentCulture.Name,
                    };

                    // Didn't find one, so create a default one
                    using (StreamWriter file = File.CreateText(filename))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        serializer.Serialize(file, config, typeof(CompareConfig));
                    }
                }
            }

            if (!File.Exists(filename))
            {
                throw new FileNotFoundException(filename);
            }

            using (StreamReader file = File.OpenText(filename))
            {
                JsonSerializer serializer = new JsonSerializer();
                return (CompareConfig)serializer.Deserialize(file, typeof(CompareConfig));
            }
        }

        public byte[] GetColorForStructure(string name)
        {
            if (StructureColors == null || !StructureColors.ContainsKey(name))
                return null;

            return StructureColors[name];
        }
    }
}
