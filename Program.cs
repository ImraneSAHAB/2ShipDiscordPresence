using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace TwoShipDiscordPresence
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "2Ship Discord Presence Monitor";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================");
            Console.WriteLine("        2Ship Discord Presence Monitor v1.0       ");
            Console.WriteLine("==================================================");
            Console.ResetColor();

            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            AppConfig config = AppConfig.Load(configPath);

            Console.WriteLine("[INFO] Configuration chargée depuis config.json");
            Console.WriteLine("[INFO] Application ciblée : " + config.process_name + ".exe");
            Console.WriteLine("[INFO] Détails : " + config.details);
            Console.WriteLine("[INFO] État : " + config.state);
            Console.WriteLine("[INFO] Pressez CTRL+C pour quitter.\n");

            bool wasRunning = false;
            long startTime = 0;
            DiscordRpc rpc = new DiscordRpc();

            while (true) {
                try {
                    config = AppConfig.Load(configPath);
                    Process targetProc = FindTargetProcess(config.process_name);

                    if (targetProc != null && !targetProc.HasExited) {
                        if (!wasRunning) {
                            wasRunning = true;
                            startTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] [DÉTECTÉ] Processus '" + targetProc.ProcessName + ".exe' trouvé (PID: " + targetProc.Id + ")!");
                            Console.ResetColor();
                        }

                        if (!rpc.IsConnected) {
                            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] Connexion à l'IPC Discord...");
                            if (rpc.Connect(config.client_id)) {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] Connexion IPC Discord réussie!");
                                Console.ResetColor();
                            } else {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] Impossible de se connecter à Discord (Discord ouvert ?). Nouvelle tentative dans " + config.check_interval_seconds + "s...");
                                Console.ResetColor();
                            }
                        }

                        if (rpc.IsConnected) {
                            rpc.SetActivity(
                                targetProc.Id,
                                config.details,
                                config.state,
                                startTime,
                                config.large_image,
                                config.large_text
                            );
                        }
                    } else {
                        if (wasRunning) {
                            wasRunning = false;
                            startTime = 0;
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] [FERMÉ] Le processus '" + config.process_name + "' s'est arrêté.");
                            Console.ResetColor();
                            if (rpc.IsConnected) {
                                rpc.ClearActivity(Process.GetCurrentProcess().Id);
                                rpc.Close();
                            }
                        }
                    }
                } catch {
                    // Ignorer les erreurs temporaires de boucle
                }

                Thread.Sleep(config.check_interval_seconds * 1000);
            }
        }

        private static Process FindTargetProcess(string processName)
        {
            string name = processName;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                name = name.Substring(0, name.Length - 4);
            }

            int currentPid = Process.GetCurrentProcess().Id;

            Process[] processes = Process.GetProcesses();
            foreach (var p in processes) {
                try {
                    if (p.Id == currentPid) continue;
                    if (p.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                        return p;
                    }
                } catch { }
            }

            foreach (var p in processes) {
                try {
                    if (p.Id == currentPid) continue;
                    if (p.ProcessName.Equals("2ShipDiscordPresence", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.ProcessName.ToLower().Contains("2ship")) {
                        return p;
                    }
                } catch { }
            }

            return null;
        }
    }

    public class AppConfig
    {
        public string client_id = "1266000000000000000";
        public string process_name = "2ship";
        public string details = "The Legend of Zelda: Majora's Mask";
        public string state = "Playing 2Ship2Harkinian";
        public string large_image = "majora";
        public string large_text = "Majora's Mask";
        public int check_interval_seconds = 3;

        public static AppConfig Load(string path)
        {
            AppConfig cfg = new AppConfig();
            if (!File.Exists(path)) {
                cfg.Save(path);
                return cfg;
            }

            try {
                string json = File.ReadAllText(path);
                cfg.client_id = GetJsonValue(json, "client_id") ?? cfg.client_id;
                cfg.process_name = GetJsonValue(json, "process_name") ?? cfg.process_name;
                cfg.details = GetJsonValue(json, "details") ?? cfg.details;
                cfg.state = GetJsonValue(json, "state") ?? cfg.state;
                cfg.large_image = GetJsonValue(json, "large_image") ?? cfg.large_image;
                cfg.large_text = GetJsonValue(json, "large_text") ?? cfg.large_text;
                string intervalStr = GetJsonValue(json, "check_interval_seconds");
                int val;
                if (!string.IsNullOrEmpty(intervalStr) && int.TryParse(intervalStr, out val)) {
                    cfg.check_interval_seconds = val;
                }
            } catch { }
            return cfg;
        }

        public void Save(string path)
        {
            try {
                string json = "{\n"
                    + "  \"client_id\": \"" + EscapeJson(client_id) + "\",\n"
                    + "  \"process_name\": \"" + EscapeJson(process_name) + "\",\n"
                    + "  \"details\": \"" + EscapeJson(details) + "\",\n"
                    + "  \"state\": \"" + EscapeJson(state) + "\",\n"
                    + "  \"large_image\": \"" + EscapeJson(large_image) + "\",\n"
                    + "  \"large_text\": \"" + EscapeJson(large_text) + "\",\n"
                    + "  \"check_interval_seconds\": " + check_interval_seconds + "\n"
                    + "}";
                File.WriteAllText(path, json);
            } catch { }
        }

        private static string GetJsonValue(string json, string key)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;
            int start = idx + pattern.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t' || json[start] == '\r' || json[start] == '\n')) {
                start++;
            }
            if (start >= json.Length) return null;
            if (json[start] == '"') {
                start++;
                int end = start;
                while (end < json.Length) {
                    if (json[end] == '\\') {
                        end += 2;
                        continue;
                    }
                    if (json[end] == '"') break;
                    end++;
                }
                if (end < json.Length) {
                    string val = json.Substring(start, end - start);
                    return val.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n");
                }
            } else {
                int end = start;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r') {
                    end++;
                }
                return json.Substring(start, end - start).Trim();
            }
            return null;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }

    public class DiscordRpc : IDisposable
    {
        private NamedPipeClientStream pipe;
        private bool isConnected = false;
        public bool IsConnected {
            get { return isConnected && pipe != null && pipe.IsConnected; }
        }

        public bool Connect(string clientId)
        {
            Close();
            for (int i = 0; i < 10; i++) {
                try {
                    string pipeName = "discord-ipc-" + i;
                    pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    pipe.Connect(500);

                    string json = "{\"v\":1,\"client_id\":\"" + clientId + "\"}";
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    byte[] header = new byte[8];
                    BitConverter.GetBytes(0).CopyTo(header, 0);
                    BitConverter.GetBytes(bytes.Length).CopyTo(header, 4);
                    pipe.Write(header, 0, 8);
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();

                    byte[] respHeader = new byte[8];
                    int read = pipe.Read(respHeader, 0, 8);
                    if (read == 8) {
                        int len = BitConverter.ToInt32(respHeader, 4);
                        byte[] payload = new byte[len];
                        int total = 0;
                        while (total < len) {
                            int r = pipe.Read(payload, total, len - total);
                            if (r <= 0) break;
                            total += r;
                        }
                        isConnected = true;
                        return true;
                    }
                } catch {
                    Close();
                }
            }
            return false;
        }

        public bool SetActivity(int pid, string details, string state, long startTimestamp, string largeImage, string largeText)
        {
            if (!IsConnected) return false;
            try {
                string jsonDetails = EscapeJson(details);
                string jsonState = EscapeJson(state);
                string jsonImage = EscapeJson(largeImage);
                string jsonText = EscapeJson(largeText);

                string timePart = startTimestamp > 0 ? ",\"timestamps\":{\"start\":" + startTimestamp + "}" : "";
                string assetsPart = !string.IsNullOrEmpty(largeImage) ? ",\"assets\":{\"large_image\":\"" + jsonImage + "\",\"large_text\":\"" + jsonText + "\"}" : "";

                string activityJson = "{"
                    + "\"details\":\"" + jsonDetails + "\""
                    + ",\"state\":\"" + jsonState + "\""
                    + timePart
                    + assetsPart
                    + "}";

                string fullPayload = "{"
                    + "\"cmd\":\"SET_ACTIVITY\""
                    + ",\"args\":{\"pid\":" + pid + ",\"activity\":" + activityJson + "}"
                    + ",\"nonce\":\"" + Guid.NewGuid().ToString() + "\""
                    + "}";

                byte[] bytes = Encoding.UTF8.GetBytes(fullPayload);
                byte[] header = new byte[8];
                BitConverter.GetBytes(1).CopyTo(header, 0);
                BitConverter.GetBytes(bytes.Length).CopyTo(header, 4);
                pipe.Write(header, 0, 8);
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();

                return true;
            } catch {
                isConnected = false;
                Close();
                return false;
            }
        }

        public void ClearActivity(int pid)
        {
            if (!IsConnected) return;
            try {
                string fullPayload = "{"
                    + "\"cmd\":\"SET_ACTIVITY\""
                    + ",\"args\":{\"pid\":" + pid + ",\"activity\":null}"
                    + ",\"nonce\":\"" + Guid.NewGuid().ToString() + "\""
                    + "}";
                byte[] bytes = Encoding.UTF8.GetBytes(fullPayload);
                byte[] header = new byte[8];
                BitConverter.GetBytes(1).CopyTo(header, 0);
                BitConverter.GetBytes(bytes.Length).CopyTo(header, 4);
                pipe.Write(header, 0, 8);
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();
            } catch { }
        }

        public void Close()
        {
            isConnected = false;
            if (pipe != null) {
                try { pipe.Dispose(); } catch { }
                pipe = null;
            }
        }

        public void Dispose()
        {
            Close();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
