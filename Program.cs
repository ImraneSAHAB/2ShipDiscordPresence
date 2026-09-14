using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TwoShipDiscordPresence
{
    class Program
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private static NotifyIcon trayIcon;
        private static ToolStripMenuItem statusMenuItem;
        private static bool isConsoleVisible = false;

        [STAThread]
        static void Main(string[] args)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            AppConfig config = AppConfig.Load(configPath);

            bool showConsoleArg = false;
            foreach (string arg in args) {
                if (arg.Equals("--show", StringComparison.OrdinalIgnoreCase) || arg.Equals("-show", StringComparison.OrdinalIgnoreCase)) {
                    showConsoleArg = true;
                }
            }

            if (!config.hide_console || showConsoleArg) {
                AllocConsole();
                isConsoleVisible = true;
            } else {
                IntPtr consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero) {
                    ShowWindow(consoleHandle, SW_HIDE);
                }
                isConsoleVisible = false;
            }

            Icon appIcon = null;
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoPath)) {
                try { appIcon = new Icon(icoPath); } catch { }
            }
            if (appIcon == null) {
                try { appIcon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }
            }
            if (appIcon == null) {
                appIcon = SystemIcons.Application;
            }

            trayIcon = new NotifyIcon();
            trayIcon.Icon = appIcon;
            trayIcon.Text = "2Ship Discord Presence";
            trayIcon.Visible = true;

            ContextMenuStrip trayMenu = new ContextMenuStrip();

            ToolStripMenuItem titleItem = new ToolStripMenuItem("2Ship Discord Presence Monitor");
            titleItem.Enabled = false;
            trayMenu.Items.Add(titleItem);

            trayMenu.Items.Add(new ToolStripSeparator());

            statusMenuItem = new ToolStripMenuItem("Status: Waiting for 2ship.exe...");
            statusMenuItem.Enabled = false;
            trayMenu.Items.Add(statusMenuItem);

            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem toggleConsoleItem = new ToolStripMenuItem("Show / Hide Console", null, (s, e) => ToggleConsole());
            trayMenu.Items.Add(toggleConsoleItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit", null, (s, e) => {
                trayIcon.Visible = false;
                Environment.Exit(0);
            });
            trayMenu.Items.Add(exitItem);

            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => ToggleConsole();

            Thread workerThread = new Thread(() => PresenceWorkerLoop(configPath));
            workerThread.IsBackground = true;
            workerThread.Start();

            Application.Run();
        }

        private static void ToggleConsole()
        {
            IntPtr handle = GetConsoleWindow();
            if (handle == IntPtr.Zero) {
                AllocConsole();
                isConsoleVisible = true;
                try { Console.Title = "2Ship Discord Presence Monitor"; } catch { }
            } else {
                if (isConsoleVisible) {
                    ShowWindow(handle, SW_HIDE);
                    isConsoleVisible = false;
                } else {
                    ShowWindow(handle, SW_SHOW);
                    isConsoleVisible = true;
                }
            }
        }

        private static void SafeLog(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            try {
                IntPtr handle = GetConsoleWindow();
                if (handle != IntPtr.Zero) {
                    Console.ForegroundColor = color;
                    Console.WriteLine(message);
                    Console.ResetColor();
                }
            } catch { }
        }

        private static void PresenceWorkerLoop(string configPath)
        {
            AppConfig config = AppConfig.Load(configPath);

            try { Console.Title = "2Ship Discord Presence Monitor"; } catch { }

            SafeLog("==================================================", ConsoleColor.Cyan);
            SafeLog("        2Ship Discord Presence Monitor v1.0       ", ConsoleColor.Cyan);
            SafeLog("==================================================", ConsoleColor.Cyan);

            SafeLog("[INFO] Configuration loaded from config.json");
            SafeLog("[INFO] Target application: " + config.process_name + ".exe");
            SafeLog("[INFO] Details: " + config.details);
            SafeLog("[INFO] State: " + config.state);
            SafeLog("[INFO] Active in System Tray (notification area).\n");

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
                            SafeLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] [DETECTED] Process '" + targetProc.ProcessName + ".exe' found (PID: " + targetProc.Id + ")!", ConsoleColor.Green);

                            if (trayIcon != null) {
                                string tip = "2Ship Presence - 2ship.exe (PID: " + targetProc.Id + ")";
                                trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
                                if (statusMenuItem != null) statusMenuItem.Text = "Status: 2ship.exe active (PID: " + targetProc.Id + ")";
                            }
                        }

                        if (!rpc.IsConnected) {
                            SafeLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] Connecting to Discord IPC...");
                            string errMessage;
                            if (rpc.Connect(config.client_id, out errMessage)) {
                                SafeLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] Discord IPC connection successful!", ConsoleColor.Green);
                            } else {
                                SafeLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] Discord connection failed: " + (errMessage ?? "Could not find Discord IPC pipe") + ". Retrying in " + config.check_interval_seconds + "s...", ConsoleColor.Yellow);
                            }
                        }

                        if (rpc.IsConnected) {
                            string actErr;
                            if (rpc.SetActivity(
                                targetProc.Id,
                                config.details,
                                config.state,
                                startTime,
                                config.large_image,
                                config.large_text,
                                config.small_image,
                                config.small_text,
                                out actErr
                            )) {
                                // Activity updated
                            } else if (!string.IsNullOrEmpty(actErr)) {
                                SafeLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] [ERROR] Status rejected by Discord: " + actErr, ConsoleColor.Red);
                            }
                        }
                    } else {
                        if (wasRunning) {
                            wasRunning = false;
                            startTime = 0;
                            SafeLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] [CLOSED] Process '" + config.process_name + "' has exited.", ConsoleColor.Yellow);

                            if (trayIcon != null) {
                                trayIcon.Text = "2Ship Presence - Waiting...";
                                if (statusMenuItem != null) statusMenuItem.Text = "Status: Waiting for 2ship.exe...";
                            }

                            if (rpc.IsConnected) {
                                rpc.ClearActivity(Process.GetCurrentProcess().Id);
                                rpc.Close();
                            }
                        }
                    }
                } catch {
                    // Ignore temporary loop errors
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
        public string client_id = "1523531784451653724";
        public string process_name = "2ship";
        public string details = "The Legend of Zelda: Majora's Mask";
        public string state = "Playing 2Ship2Harkinian";
        public string large_image = "icon";
        public string large_text = "Majora's Mask";
        public string small_image = "icon";
        public string small_text = "Made by Imashiro";
        public bool hide_console = true;
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
                cfg.small_image = GetJsonValue(json, "small_image") ?? cfg.small_image;
                cfg.small_text = GetJsonValue(json, "small_text") ?? cfg.small_text;
                string hideStr = GetJsonValue(json, "hide_console");
                if (!string.IsNullOrEmpty(hideStr)) {
                    bool hideBool;
                    if (bool.TryParse(hideStr, out hideBool)) {
                        cfg.hide_console = hideBool;
                    }
                }
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
                    + "  \"small_image\": \"" + EscapeJson(small_image) + "\",\n"
                    + "  \"small_text\": \"" + EscapeJson(small_text) + "\",\n"
                    + "  \"hide_console\": " + (hide_console ? "true" : "false") + ",\n"
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

        public bool Connect(string clientId, out string errorMessage)
        {
            errorMessage = null;
            Close();
            for (int i = 0; i < 10; i++) {
                try {
                    string pipeName = "discord-ipc-" + i;
                    pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    pipe.Connect(500);

                    string json = "{\"v\":1,\"client_id\":\"" + clientId + "\"}";
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    byte[] header = new byte[8];
                    BitConverter.GetBytes(0).CopyTo(header, 0); // Opcode 0 = HANDSHAKE
                    BitConverter.GetBytes(bytes.Length).CopyTo(header, 4);
                    pipe.Write(header, 0, 8);
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();

                    byte[] respHeader = new byte[8];
                    int read = pipe.Read(respHeader, 0, 8);
                    if (read == 8) {
                        int opCode = BitConverter.ToInt32(respHeader, 0);
                        int len = BitConverter.ToInt32(respHeader, 4);
                        byte[] payload = new byte[len];
                        int total = 0;
                        while (total < len) {
                            int r = pipe.Read(payload, total, len - total);
                            if (r <= 0) break;
                            total += r;
                        }

                        if (opCode == 1) { // FRAME (READY / Success)
                            isConnected = true;
                            return true;
                        } else if (opCode == 2) { // CLOSE (Rejected by Discord)
                            string errJson = Encoding.UTF8.GetString(payload);
                            errorMessage = "Discord connection refused: " + errJson;
                            Close();
                            return false;
                        }
                    }
                } catch (Exception ex) {
                    errorMessage = ex.Message;
                    Close();
                }
            }
            return false;
        }

        public bool SetActivity(int pid, string details, string state, long startTimestamp, string largeImage, string largeText, string smallImage, string smallText, out string errMessage)
        {
            errMessage = null;
            if (!IsConnected) return false;
            try {
                string jsonDetails = EscapeJson(details);
                string jsonState = EscapeJson(state);
                string jsonImage = EscapeJson(largeImage);
                string jsonText = EscapeJson(largeText);
                string jsonSmallImage = EscapeJson(smallImage);
                string jsonSmallText = EscapeJson(smallText);

                string timePart = startTimestamp > 0 ? ",\"timestamps\":{\"start\":" + startTimestamp + "}" : "";

                List<string> assetProps = new List<string>();
                if (!string.IsNullOrEmpty(largeImage)) assetProps.Add("\"large_image\":\"" + jsonImage + "\"");
                if (!string.IsNullOrEmpty(largeText)) assetProps.Add("\"large_text\":\"" + jsonText + "\"");
                if (!string.IsNullOrEmpty(smallImage)) assetProps.Add("\"small_image\":\"" + jsonSmallImage + "\"");
                if (!string.IsNullOrEmpty(smallText)) assetProps.Add("\"small_text\":\"" + jsonSmallText + "\"");

                string assetsPart = assetProps.Count > 0 ? ",\"assets\":{" + string.Join(",", assetProps.ToArray()) + "}" : "";

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
                BitConverter.GetBytes(1).CopyTo(header, 0); // Opcode 1 = FRAME
                BitConverter.GetBytes(bytes.Length).CopyTo(header, 4);
                pipe.Write(header, 0, 8);
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();

                byte[] respHeader = new byte[8];
                int read = pipe.Read(respHeader, 0, 8);
                if (read == 8) {
                    int opCode = BitConverter.ToInt32(respHeader, 0);
                    int len = BitConverter.ToInt32(respHeader, 4);
                    if (len > 0) {
                        byte[] respPayload = new byte[len];
                        int total = 0;
                        while (total < len) {
                            int r = pipe.Read(respPayload, total, len - total);
                            if (r <= 0) break;
                            total += r;
                        }
                        if (opCode == 2) {
                            errMessage = Encoding.UTF8.GetString(respPayload);
                            isConnected = false;
                            Close();
                            return false;
                        }
                    }
                }

                return true;
            } catch (Exception ex) {
                errMessage = ex.Message;
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

