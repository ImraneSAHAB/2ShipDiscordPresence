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

                                dynamicDetails = dayStr + " (" + timeStr + ")";
                                if (gameData.Health <= 0) {
                                    dynamicState = "💀 Game Over (0/" + gameData.HealthCapacity + ")";
                                } else {
                                    dynamicState = "❤️ " + gameData.Health.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + gameData.HealthCapacity;
                                }
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

    public class GameMemoryReader
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        public class GameStateData
        {
            public bool IsValid;
            public int Day;
            public ushort Time;
            public int Entrance;
            public double Health;
            public int HealthCapacity;
            public byte EquippedMask;
            public byte PlayerForm;
        }

        private IntPtr cachedActiveAddr = IntPtr.Zero;
        private ushort lastObservedTime = 0xFFFF;
        private int cachedPid = -1;

        public GameStateData ReadGameState(int pid)
        {
            GameStateData result = new GameStateData { IsValid = false };
            if (pid <= 0) return result;

            IntPtr hProcess = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return result;

            try {
                Process targetProc = Process.GetProcessById(pid);
                IntPtr mainBase = IntPtr.Zero;
                long mainSize = 0;
                try {
                    ProcessModule mainMod = targetProc.MainModule;
                    if (mainMod != null) {
                        mainBase = mainMod.BaseAddress;
                        mainSize = mainMod.ModuleMemorySize;
                    }
                } catch { }

                bool isCachedInMainModule = false;
                if (cachedActiveAddr != IntPtr.Zero && mainBase != IntPtr.Zero && mainSize > 0) {
                    long addr = cachedActiveAddr.ToInt64();
                    long b = mainBase.ToInt64();
                    isCachedInMainModule = (addr >= b && addr < b + mainSize);
                }

                if (cachedPid != pid || cachedActiveAddr == IntPtr.Zero || !isCachedInMainModule || !VerifyZelda3Magic(hProcess, cachedActiveAddr)) {
                    cachedActiveAddr = FindActiveZelda3Address(hProcess, mainBase, mainSize);
                    cachedPid = pid;
                }

                if (cachedActiveAddr != IntPtr.Zero) {
                    IntPtr saveContextBase = new IntPtr(cachedActiveAddr.ToInt64() - 0x24);

                    byte[] buffer = new byte[0x50];
                    IntPtr bytesRead;
                    if (ReadProcessMemory(hProcess, saveContextBase, buffer, buffer.Length, out bytesRead) && bytesRead.ToInt64() >= 0x40) {
                        result.Entrance = BitConverter.ToInt32(buffer, 0x00);
                        result.EquippedMask = buffer[0x3E];
                        result.Time = BitConverter.ToUInt16(buffer, 0x0C);
                        result.Day = BitConverter.ToInt32(buffer, 0x18);
                        result.PlayerForm = buffer[0x20];

                        short rawCap = BitConverter.ToInt16(buffer, 0x34);
                        short rawHp = BitConverter.ToInt16(buffer, 0x36);

                        result.HealthCapacity = rawCap > 0 ? (rawCap / 16) : 3;
                        result.Health = rawHp > 0 ? (rawHp / 16.0) : 0;

                        if (result.Day >= 1 && result.Day <= 10 && result.HealthCapacity >= 1 && result.HealthCapacity <= 30) {
                            result.IsValid = true;
                            lastObservedTime = result.Time;
                        }
                    }
                }
            } catch { }
            finally {
                CloseHandle(hProcess);
            }

            return result;
        }

        private bool VerifyZelda3Magic(IntPtr hProcess, IntPtr addr)
        {
            byte[] buf = new byte[6];
            IntPtr read;
            if (ReadProcessMemory(hProcess, addr, buf, 6, out read) && read.ToInt64() == 6) {
                return Encoding.ASCII.GetString(buf) == "ZELDA3";
            }
            return false;
        }

        private IntPtr FindActiveZelda3Address(IntPtr hProcess, IntPtr mainBase, long mainSize)
        {
            IntPtr address = IntPtr.Zero;
            MEMORY_BASIC_INFORMATION mbi;
            byte[] searchPattern = Encoding.ASCII.GetBytes("ZELDA3");

            IntPtr bestAddr = IntPtr.Zero;
            int bestScore = -1;

            long mainStart = mainBase != IntPtr.Zero ? mainBase.ToInt64() : 0;
            long mainEnd = mainStart > 0 ? mainStart + mainSize : 0;

            while (VirtualQueryEx(hProcess, address, out mbi, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) != 0) {
                if (mbi.State == MEM_COMMIT && (mbi.Protect == PAGE_READWRITE || mbi.Protect == PAGE_EXECUTE_READWRITE)) {
                    long regionSize = mbi.RegionSize.ToInt64();
                    if (regionSize > 0 && regionSize <= 100000000) {
                        byte[] buffer = new byte[(int)regionSize];
                        IntPtr read;
                        if (ReadProcessMemory(hProcess, mbi.BaseAddress, buffer, buffer.Length, out read)) {
                            int readLen = (int)read.ToInt64();
                            for (int i = 0; i <= readLen - searchPattern.Length; i += 4) {
                                if (buffer[i] == 'Z' && buffer[i+1] == 'E' && buffer[i+2] == 'L' && buffer[i+3] == 'D' && buffer[i+4] == 'A' && buffer[i+5] == '3') {
                                    IntPtr matchAddr = new IntPtr(mbi.BaseAddress.ToInt64() + i);
                                    IntPtr saveBase = new IntPtr(matchAddr.ToInt64() - 0x24);
                                    byte[] testBuf = new byte[0x40];
                                    IntPtr testRead;
                                    if (ReadProcessMemory(hProcess, saveBase, testBuf, testBuf.Length, out testRead) && testRead.ToInt64() >= 0x38) {
                                        int day = BitConverter.ToInt32(testBuf, 0x18);
                                        short hpCap = BitConverter.ToInt16(testBuf, 0x34);
                                        ushort time = BitConverter.ToUInt16(testBuf, 0x0C);

                                        if (day >= 1 && day <= 10 && hpCap >= 16 && hpCap <= 480) {
                                            int score = 10;
                                            long mAddr = matchAddr.ToInt64();
                                            if (mainStart > 0 && mAddr >= mainStart && mAddr < mainEnd) {
                                                score += 1000;
                                            }

                                            if (time == 0x6913 || time == 0x785E || time == 0x85FD) {
                                                score -= 500;
                                            }

                                            if (score > bestScore) {
                                                bestScore = score;
                                                bestAddr = matchAddr;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                long nextAddr = mbi.BaseAddress.ToInt64() + mbi.RegionSize.ToInt64();
                if (nextAddr <= address.ToInt64()) break;
                address = new IntPtr(nextAddr);
            }

            return bestAddr;
        }
    }
}

