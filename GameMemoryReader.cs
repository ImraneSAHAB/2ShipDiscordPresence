using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TwoShipDiscordPresence
{
    // Resolve the exact globals and native field offsets from the matching 2Ship PDB.
    // A ZELDA3 signature alone also matches save previews and cannot prove gameplay.
    public class GameMemoryReader
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] data, int size, out IntPtr read);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern bool SymInitialize(IntPtr process, string searchPath, bool invadeProcess);
        [DllImport("dbghelp.dll")]
        private static extern bool SymCleanup(IntPtr process);
        [DllImport("dbghelp.dll")]
        private static extern uint SymSetOptions(uint options);
        [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern ulong SymLoadModuleEx(IntPtr process, IntPtr file, string image,
            string moduleName, ulong baseAddress, uint size, IntPtr data, uint flags);
        [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern bool SymFromName(IntPtr process, string name, IntPtr symbol);
        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool SymGetTypeInfo(IntPtr process, ulong module, uint type, int request, IntPtr result);

        public class GameStateData
        {
            public bool IsValid;
            public bool IsInMenus;
            public string Diagnostic;
            public int Day;
            public ushort Time;
            public int Entrance;
            public double Health;
            public int HealthCapacity;
            public byte EquippedMask;
            public byte PlayerForm;
        }

        private int cachedPid = -1;
        private long cachedStartTicks;
        private long saveAddress;
        private long playStateAddress;
        private int gameModeOffset;
        private DateTime nextResolveAttempt;

        private static byte[] Read(IntPtr process, long address, int length)
        {
            if (address <= 0) return null;
            byte[] data = new byte[length];
            IntPtr read;
            return ReadProcessMemory(process, new IntPtr(address), data, length, out read)
                && read.ToInt64() == length ? data : null;
        }

        private static bool FindSymbol(IntPtr process, string name, out long address, out uint type, out ulong module)
        {
            // SYMBOL_INFO has sizeof 88 on Windows x64, with Name at offset 84.
            IntPtr info = Marshal.AllocHGlobal(88 + 1024);
            try {
                Marshal.Copy(new byte[88 + 1024], 0, info, 88 + 1024);
                Marshal.WriteInt32(info, 0, 88);
                Marshal.WriteInt32(info, 80, 1024);
                bool found = SymFromName(process, name, info);
                address = found ? Marshal.ReadInt64(info, 56) : 0;
                type = found ? (uint)Marshal.ReadInt32(info, 4) : 0;
                module = found ? (ulong)Marshal.ReadInt64(info, 32) : 0;
                return found && address != 0;
            } finally { Marshal.FreeHGlobal(info); }
        }

        private static int FindFieldOffset(IntPtr process, ulong module, uint type, string field)
        {
            IntPtr value = Marshal.AllocHGlobal(8);
            IntPtr children = IntPtr.Zero;
            try {
                // IMAGEHLP_SYMBOL_TYPE_INFO: CHILDRENCOUNT=13, FINDCHILDREN=7,
                // SYMNAME=1, OFFSET=10. Offsets come from the PC build, not N64 comments.
                if (!SymGetTypeInfo(process, module, type, 13, value)) return -1;
                int count = Marshal.ReadInt32(value);
                if (count <= 0 || count > 4096) return -1;
                children = Marshal.AllocHGlobal(8 + count * 4);
                Marshal.WriteInt32(children, 0, count);
                Marshal.WriteInt32(children, 4, 0);
                if (!SymGetTypeInfo(process, module, type, 7, children)) return -1;
                for (int i = 0; i < count; i++) {
                    uint child = (uint)Marshal.ReadInt32(children, 8 + i * 4);
                    if (!SymGetTypeInfo(process, module, child, 1, value)) continue;
                    IntPtr namePointer = Marshal.ReadIntPtr(value);
                    string name;
                    try { name = Marshal.PtrToStringUni(namePointer); }
                    finally { LocalFree(namePointer); }
                    if (name == field && SymGetTypeInfo(process, module, child, 10, value))
                        return Marshal.ReadInt32(value);
                }
                return -1;
            } finally {
                if (children != IntPtr.Zero) Marshal.FreeHGlobal(children);
                Marshal.FreeHGlobal(value);
            }
        }

        private bool Resolve(IntPtr process, Process target)
        {
            ProcessModule main = target.MainModule;
            string directory = Path.GetDirectoryName(main.FileName);
            string searchPath = directory + ";" + Path.Combine(directory, "debug");
            // Exact PDB matching, no symbol-server downloads or error dialogs.
            SymSetOptions(0x00000002 | 0x00000200 | 0x00000400 | 0x00080000);
            if (!SymInitialize(process, searchPath, false)) return false;
            try {
                if (SymLoadModuleEx(process, IntPtr.Zero, main.FileName, null,
                    (ulong)main.BaseAddress.ToInt64(), (uint)main.ModuleMemorySize, IntPtr.Zero, 0) == 0) return false;
                long save, play;
                uint saveType, ignoredType;
                ulong module, ignoredModule;
                if (!FindSymbol(process, "gSaveContext", out save, out saveType, out module)
                    || !FindSymbol(process, "gPlayState", out play, out ignoredType, out ignoredModule)) return false;
                int modeOffset = FindFieldOffset(process, module, saveType, "gameMode");
                if (modeOffset < 0) return false;
                saveAddress = save;
                playStateAddress = play;
                gameModeOffset = modeOffset;
                return true;
            } finally { SymCleanup(process); }
        }

        internal static bool IsGameplay(long playState, int gameMode)
        {
            // Title-screen scenery also uses PlayState; require GAMEMODE_NORMAL.
            return playState != 0 && gameMode == 0;
        }

        internal static GameStateData Decode(byte[] save, long playState, int gameMode)
        {
            GameStateData result = new GameStateData();
            if (gameMode < 0 || gameMode > 3) return result;
            result.IsInMenus = !IsGameplay(playState, gameMode);
            if (result.IsInMenus || save == null || save.Length < 0x40) return result;
            if (Encoding.ASCII.GetString(save, 0x24, 6) != "ZELDA3") return result;
            result.Entrance = BitConverter.ToInt32(save, 0);
            result.EquippedMask = save[4];
            result.Time = BitConverter.ToUInt16(save, 0x0C);
            result.Day = BitConverter.ToInt32(save, 0x18);
            result.PlayerForm = save[0x20];
            short capacity = BitConverter.ToInt16(save, 0x34);
            short health = BitConverter.ToInt16(save, 0x36);
            result.IsValid = result.Day >= 1 && result.Day <= 4 && capacity >= 16
                && capacity <= 320 && capacity % 16 == 0 && health >= 0 && health <= capacity
                && result.PlayerForm <= 4 && result.Entrance >= 0 && result.Entrance <= ushort.MaxValue;
            result.HealthCapacity = capacity / 16;
            // 2Ship stores health in sixteenths, while the HUD displays quarters.
            // The vanilla texture table maps remainders 1-5 to 1/4, 6-10 to 1/2,
            // and 11-15 to 3/4. Match that visual value in Discord.
            int fullHearts = health / 16;
            int remainder = health % 16;
            int displayedQuarter = remainder == 0 ? 0 : (remainder + 4) / 5;
            result.Health = fullHearts + (displayedQuarter / 4.0);
            return result;
        }

        public GameStateData ReadGameState(int pid)
        {
            GameStateData unavailable = new GameStateData { Diagnostic = "Game state unavailable" };
            IntPtr process = OpenProcess(0x0410, false, pid);
            if (process == IntPtr.Zero) return unavailable;
            try {
                using (Process target = Process.GetProcessById(pid)) {
                    long started = target.StartTime.ToUniversalTime().Ticks;
                    if (cachedPid != pid || cachedStartTicks != started) {
                        cachedPid = pid;
                        cachedStartTicks = started;
                        saveAddress = playStateAddress = 0;
                        nextResolveAttempt = DateTime.MinValue;
                    }
                    if (saveAddress == 0) {
                        unavailable.Diagnostic = "Matching debug/2ship.pdb required";
                        if (DateTime.UtcNow < nextResolveAttempt) return unavailable;
                        nextResolveAttempt = DateTime.UtcNow.AddSeconds(30);
                        if (!Resolve(process, target)) return unavailable;
                    }
                    byte[] mode = Read(process, saveAddress + gameModeOffset, 4);
                    byte[] play = Read(process, playStateAddress, 8);
                    if (mode == null || play == null) return unavailable;
                    int gameMode = BitConverter.ToInt32(mode, 0);
                    long playState = BitConverter.ToInt64(play, 0);
                    byte[] save = IsGameplay(playState, gameMode) ? Read(process, saveAddress, 0x40) : null;
                    // Do not publish a snapshot spanning a transition back to the menus.
                    byte[] modeAfter = Read(process, saveAddress + gameModeOffset, 4);
                    byte[] playAfter = Read(process, playStateAddress, 8);
                    if (modeAfter == null || playAfter == null
                        || BitConverter.ToInt32(modeAfter, 0) != gameMode
                        || BitConverter.ToInt64(playAfter, 0) != playState) return unavailable;
                    return Decode(save, playState, gameMode);
                }
            } catch (Exception ex) {
                unavailable.Diagnostic = "Game state unavailable: " + ex.Message;
                return unavailable;
            } finally { CloseHandle(process); }
        }
    }
}
