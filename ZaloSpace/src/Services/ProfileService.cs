using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZaloSpaceManager.Models;

namespace ZaloSpaceManager.Services
{
    public class AppSettings
    {
        public string ZaloPath { get; set; }
    }

    public static class ProfileService
    {
        private static readonly string BaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZaloSpace");
        private static readonly string ProfilesFile = Path.Combine(BaseDir, "profiles.json");
        private static readonly string SettingsFile = Path.Combine(BaseDir, "settings.json");

        static ProfileService()
        {
            if (!Directory.Exists(BaseDir))
                Directory.CreateDirectory(BaseDir);
        }

        public static string GetBaseDirectory() { return BaseDir; }
        public static string GetIconsDirectory() { return Path.Combine(BaseDir, "Icons"); }

        public static AppSettings LoadSettings()
        {
            AppSettings settings = new AppSettings();
            if (File.Exists(SettingsFile))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFile, Encoding.UTF8);
                    settings.ZaloPath = ExtractJsonField(json, "ZaloPath");
                }
                catch { }
            }
            return settings;
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendFormat("  \"ZaloPath\": \"{0}\"\n", EscapeJson(settings.ZaloPath));
                sb.AppendLine("}");
                File.WriteAllText(SettingsFile, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static List<Profile> LoadProfiles()
        {
            List<Profile> list = new List<Profile>();
            if (File.Exists(ProfilesFile))
            {
                try
                {
                    string json = File.ReadAllText(ProfilesFile, Encoding.UTF8);
                    list = ParseProfilesJson(json);
                }
                catch { }
            }

            return list;
        }

        public static Profile CreateExistingDefaultProfile(string existingUserId)
        {
            string userName = string.IsNullOrEmpty(existingUserId) ? "main" : existingUserId;
            Profile p = new Profile();
            p.Id = "1";
            p.Name = string.Format("zalo_1_{0}", userName);
            p.DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZaloData");
            p.AutoStart = AutoStartService.IsProfileAutoStart("1");
            return p;
        }

        public static Profile CreateNewCloneProfile(int index, string customUserName)
        {
            string userName = string.IsNullOrEmpty(customUserName) ? string.Format("user{0}", index) : customUserName;
            Profile p = new Profile();
            p.Id = index.ToString();
            p.Name = string.Format("zalo_{0}_{1}", index, userName);
            p.DataDir = Path.Combine(BaseDir, string.Format(@"Data\Profile_{0}", index));
            p.AutoStart = AutoStartService.IsProfileAutoStart(p.Id);
            return p;
        }

        public static void SaveProfiles(List<Profile> profiles)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < profiles.Count; i++)
                {
                    Profile p = profiles[i];
                    sb.AppendLine("  {");
                    sb.AppendFormat("    \"Id\": \"{0}\",\n", EscapeJson(p.Id));
                    sb.AppendFormat("    \"Name\": \"{0}\",\n", EscapeJson(p.Name));
                    sb.AppendFormat("    \"DataDir\": \"{0}\",\n", EscapeJson(p.DataDir));
                    sb.AppendFormat("    \"AutoStart\": {0},\n", p.AutoStart ? "true" : "false");
                    sb.AppendFormat("    \"AvatarPath\": \"{0}\",\n", EscapeJson(p.AvatarPath));
                    sb.AppendFormat("    \"IconPath\": \"{0}\",\n", EscapeJson(p.IconPath));
                    sb.AppendFormat("    \"CreatedAt\": \"{0:o}\"\n", p.CreatedAt);
                    sb.Append(i == profiles.Count - 1 ? "  }\n" : "  },\n");
                }
                sb.AppendLine("]");
                File.WriteAllText(ProfilesFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SaveProfiles error: " + ex.Message);
            }
        }

        private static List<Profile> ParseProfilesJson(string json)
        {
            List<Profile> result = new List<Profile>();
            if (string.IsNullOrEmpty(json)) return result;

            int cur = 0;
            while (true)
            {
                int start = json.IndexOf('{', cur);
                if (start < 0) break;
                int end = json.IndexOf('}', start);
                if (end < 0) break;

                string block = json.Substring(start, end - start + 1);
                Profile p = new Profile();
                p.Id = ExtractJsonField(block, "Id");
                p.Name = ExtractJsonField(block, "Name");
                p.DataDir = ExtractJsonField(block, "DataDir");
                p.AvatarPath = ExtractJsonField(block, "AvatarPath");
                p.IconPath = ExtractJsonField(block, "IconPath");
                string autoStr = ExtractJsonField(block, "AutoStart");
                p.AutoStart = autoStr != null && autoStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(p.Id))
                {
                    result.Add(p);
                }
                cur = end + 1;
            }
            return result;
        }

        private static string ExtractJsonField(string block, string fieldName)
        {
            string search = "\"" + fieldName + "\"";
            int idx = block.IndexOf(search);
            if (idx < 0) return null;
            int colon = block.IndexOf(':', idx + search.Length);
            if (colon < 0) return null;

            int valStart = colon + 1;
            while (valStart < block.Length && char.IsWhiteSpace(block[valStart])) valStart++;
            if (valStart >= block.Length) return null;

            if (block[valStart] == '\"')
            {
                int quoteEnd = block.IndexOf('\"', valStart + 1);
                if (quoteEnd < 0) return null;
                return block.Substring(valStart + 1, quoteEnd - valStart - 1).Replace("\\\\", "\\").Replace("\\\"", "\"");
            }
            else
            {
                int end = valStart;
                while (end < block.Length && block[end] != ',' && block[end] != '}' && !char.IsWhiteSpace(block[end])) end++;
                return block.Substring(valStart, end - valStart).Trim();
            }
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
