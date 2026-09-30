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
            string lastLoggedZone = null;
            DiscordRpc rpc = new DiscordRpc();
            GameMemoryReader memReader = new GameMemoryReader();

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
                            string dynamicDetails = config.details;
                            string dynamicState = config.state;

                            GameMemoryReader.GameStateData gameData = memReader.ReadGameState(targetProc.Id);
                            if (gameData != null && gameData.IsValid) {
                                string dayStr = FormatDay(gameData.Day);
                                string timeStr = FormatTime(gameData.Time);
                                string zoneStr = FormatZone(gameData.Entrance);

                                string healthStr;
                                if (gameData.Health <= 0) {
                                    healthStr = "💀 Game Over (0/" + gameData.HealthCapacity + ")";
                                } else {
                                    healthStr = "❤️ " + gameData.Health.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + gameData.HealthCapacity;
                                }

                                if (!string.IsNullOrEmpty(zoneStr)) {
                                    dynamicDetails = zoneStr;
                                    dynamicState = dayStr + " (" + timeStr + ") • " + healthStr;

                                    if (zoneStr != lastLoggedZone) {
                                        SafeLog("[" + DateTime.Now.ToString("HH:mm:ss") + "] [ZONE] Player is in: " + zoneStr, ConsoleColor.Magenta);
                                        lastLoggedZone = zoneStr;
                                    }

                                    if (trayIcon != null) {
                                        string tip = "2Ship - " + zoneStr;
                                        trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
                                    }
                                    if (statusMenuItem != null) {
                                        statusMenuItem.Text = "Status: " + zoneStr;
                                    }
                                } else {
                                    dynamicDetails = config.details;
                                    dynamicState = dayStr + " (" + timeStr + ") • " + healthStr;
                                    lastLoggedZone = null;
                                    if (trayIcon != null) trayIcon.Text = "2Ship Presence - Location unavailable";
                                    if (statusMenuItem != null) statusMenuItem.Text = "Status: Location unavailable";
                                }
                            } else {
                                lastLoggedZone = null;
                                bool menus = gameData != null && gameData.IsInMenus;
                                if (menus) dynamicState = "In menus";
                                string status = menus ? "In menus" : (gameData.Diagnostic ?? "Waiting for game data");
                                if (trayIcon != null) trayIcon.Text = menus ? "2Ship Presence - In menus" : "2Ship Presence - Waiting for game data";
                                if (statusMenuItem != null) statusMenuItem.Text = "Status: " + status;
                            }

                            string actErr;
                            if (rpc.SetActivity(
                                targetProc.Id,
                                dynamicDetails,
                                dynamicState,
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
                            lastLoggedZone = null;
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

        private static string FormatZone(int entrance)
        {
            // Save.entrance encodes EntranceSceneId, NOT SceneId.
            // Source: HarbourMasters/2ship2harkinian, mm/include/z64scene.h
            // (e8757c14a0fc8701461b0458c9ed72c118bcfc67).
            // Bits 9-15 select the area; spawn and layer occupy the lower bits.
            if (entrance < 0 || entrance > ushort.MaxValue) return null;
            int entranceSceneId = (entrance >> 9) & 0x7F;
            switch (entranceSceneId) {
                case 0x00: return "Mayor's Residence (Clock Town)";
                case 0x01: return "Majora's Lair";
                case 0x02: return "Potion Shop (Southern Swamp)";
                case 0x03: return "Ranch House";
                case 0x04: return "Honey & Darling's Shop (Clock Town)";
                case 0x05: return "Beneath the Graveyard";
                case 0x06: return "Southern Swamp (Cleared)";
                case 0x07: return "Curiosity Shop";
                case 0x0A: return "Secret Grotto";
                case 0x0E: return "Cutscene";
                case 0x10: return "Ikana Canyon";
                case 0x11: return "Pirates' Fortress";
                case 0x12: return "Milk Bar";
                case 0x13: return "Stone Tower Temple";
                case 0x14: return "Treasure Chest Shop";
                case 0x15: return "Stone Tower Temple (Inverted)";
                case 0x16: return "Clock Tower Rooftop";
                case 0x17: return "Opening Passage";
                case 0x18: return "Woodfall Temple";
                case 0x19: return "Path To Mountain Village";
                case 0x1A: return "Ikana Castle";
                case 0x1B: return "Deku Scrub Playground";
                case 0x1C: return "Woodfall Temple (Odolwa)";
                case 0x1D: return "Town Shooting Gallery";
                case 0x1E: return "Snowhead Temple";
                case 0x1F: return "Milk Road";
                case 0x20: return "Pirates' Fortress (Interior)";
                case 0x21: return "Swamp Shooting Gallery";
                case 0x22: return "Pinnacle Rock";
                case 0x23: return "Fairy Fountain";
                case 0x24: return "Swamp Spider House";
                case 0x25: return "Oceanside Spider House";
                case 0x26: return "Astral Observatory";
                case 0x27: return "The Moon (Deku Trial)";
                case 0x28: return "Deku Palace";
                case 0x29: return "Mountain Smithy";
                case 0x2A: return "Termina Field";
                case 0x2B: return "Post Office";
                case 0x2C: return "Marine Research Lab";
                case 0x2D: return "Dampe's House";
                case 0x2F: return "Goron Shrine";
                case 0x30: return "Zora Hall";
                case 0x31: return "Trading Post";
                case 0x32: return "Romani Ranch";
                case 0x33: return "Stone Tower Temple (Twinmold)";
                case 0x34: return "Great Bay Coast";
                case 0x35: return "Zora Cape";
                case 0x36: return "Lottery Shop";
                case 0x38: return "Pirates' Fortress (Exterior)";
                case 0x39: return "Fisherman's Hut";
                case 0x3A: return "Goron Shop";
                case 0x3B: return "Deku King's Chamber";
                case 0x3C: return "The Moon (Goron Trial)";
                case 0x3D: return "Road To Southern Swamp";
                case 0x3E: return "Doggy Racetrack";
                case 0x3F: return "Cucco Shack";
                case 0x40: return "Ikana Graveyard";
                case 0x41: return "Snowhead Temple (Goht)";
                case 0x42: return "Southern Swamp (Poisoned)";
                case 0x43: return "Woodfall";
                case 0x44: return "The Moon (Zora Trial)";
                case 0x45: return "Goron Village (Spring)";
                case 0x46: return "Great Bay Temple";
                case 0x47: return "Waterfall Rapids";
                case 0x48: return "Beneath The Well";
                case 0x49: return "Zora Hall Rooms";
                case 0x4A: return "Goron Village (Winter)";
                case 0x4B: return "Goron Graveyard";
                case 0x4C: return "Sakon's Hideout";
                case 0x4D: return "Mountain Village (Winter)";
                case 0x4E: return "Ghost Hut";
                case 0x4F: return "Deku Shrine";
                case 0x50: return "Road To Ikana";
                case 0x51: return "Swordsman's School (Clock Town)";
                case 0x52: return "Music Box House";
                case 0x53: return "Ikana Castle (Igos du Ikana)";
                case 0x54: return "Tourist Information";
                case 0x55: return "Stone Tower";
                case 0x56: return "Stone Tower (Inverted)";
                case 0x57: return "Mountain Village (Spring)";
                case 0x58: return "Path To Snowhead";
                case 0x59: return "Snowhead";
                case 0x5A: return "Path to Goron Village (Winter)";
                case 0x5B: return "Path to Goron Village (Spring)";
                case 0x5C: return "Great Bay Temple (Gyorg)";
                case 0x5D: return "Secret Shrine";
                case 0x5E: return "Stock Pot Inn";
                case 0x5F: return "Great Bay (Cutscene)";
                case 0x60: return "Clock Tower Interior";
                case 0x61: return "Woods Of Mystery";
                case 0x62: return "Lost Woods";
                case 0x63: return "The Moon (Link Trial)";
                case 0x64: return "The Moon";
                case 0x65: return "Bomb Shop";
                case 0x66: return "Giants' Chamber";
                case 0x67: return "Gorman Track";
                case 0x68: return "Goron Racetrack";
                case 0x69: return "East Clock Town";
                case 0x6A: return "West Clock Town";
                case 0x6B: return "North Clock Town";
                case 0x6C: return "South Clock Town";
                case 0x6D: return "Laundry Pool";
                default: return null;
            }
        }

        private static string FormatDay(int day)
        {
            switch (day) {
                case 1: return "1st Day";
                case 2: return "2nd Day";
                case 3: return "3rd Day";
                case 4: return "Final Hours";
                default: return day > 4 ? "Final Hours" : "1st Day";
            }
        }

        private static string FormatTime(ushort timeValue)
        {
            long totalMinutes = ((long)timeValue * 1440L) / 65536L;
            int hours = (int)(totalMinutes / 60) % 24;
            int minutes = (int)(totalMinutes % 60);

            string ampm = hours >= 12 ? "PM" : "AM";
            int displayHours = hours % 12;
            if (displayHours == 0) displayHours = 12;

            return string.Format("{0:D2}:{1:D2} {2}", displayHours, minutes, ampm);
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
