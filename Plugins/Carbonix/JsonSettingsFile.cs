using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix
{
    /// <summary>
    /// Loads a JSON settings file, falling back to caller-provided defaults.
    /// </summary>
    /// <remarks>
    /// Tracks a content hash so stale files are evicted when defaults change,
    /// backing up only if the user customized the file.
    /// </remarks>
    public static class JsonSettingsFile
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const string HashKey = "_defaults_hash";
        const string DataKey = "data";

        public static T LoadOrCreate<T>(string path, T defaults,
            JsonSerializerSettings jsonSettings = null)
        {
            var serializer = JsonSerializer.Create(jsonSettings);
            var defaultsToken = JToken.FromObject(defaults, serializer);
            var defaultsHash = ComputeHash(defaultsToken);

            if (File.Exists(path))
            {
                try
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    var storedHash = root[HashKey]?.Value<string>();
                    var dataToken = root[DataKey];

                    if (storedHash == defaultsHash)
                        return dataToken.ToObject<T>(serializer);

                    // Defaults changed — evict; back up only if user-modified
                    if (dataToken == null || ComputeHash(dataToken) != storedHash)
                        Backup(path);
                }
                catch (Exception e)
                {
                    _log.Error($"Failed to load {Path.GetFileName(path)}: {e.Message}");
                    Backup(path);
                }
            }

            WriteFile(path, defaultsHash, defaultsToken);
            return defaultsToken.ToObject<T>(serializer);
        }

        static void WriteFile(string path, string hash, JToken data)
        {
            var root = new JObject
            {
                [HashKey] = hash,
                [DataKey] = data
            };
            File.WriteAllText(path, root.ToString(Formatting.Indented));
        }

        static void Backup(string path)
        {
            int i = 1;
            string backupPath;
            do
            {
                backupPath = $"{path}.{i}.bak";
                i++;
            } while (File.Exists(backupPath));

            File.Move(path, backupPath);
            _log.Warn($"Backed up {Path.GetFileName(path)} to {Path.GetFileName(backupPath)}");
        }

        static string ComputeHash(JToken token)
        {
            var json = token.ToString(Formatting.None);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
