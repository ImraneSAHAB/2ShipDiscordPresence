using System;
using System.Reflection;
using TwoShipDiscordPresence;

class PresenceTests
{
    private static int checks;

    private static object Format(string method, object value)
    {
        return typeof(GameMemoryReader).Assembly.GetType("TwoShipDiscordPresence.Program")
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { value });
    }

    private static void Equal(object expected, object actual)
    {
        checks++;
        if (!object.Equals(expected, actual))
            throw new Exception("Expected " + (expected ?? "null") + ", got " + (actual ?? "null"));
    }

    static void Main()
    {
        byte[] save = new byte[0x40];
        System.Text.Encoding.ASCII.GetBytes("ZELDA3").CopyTo(save, 0x24);
        BitConverter.GetBytes(0xD800).CopyTo(save, 0);
        BitConverter.GetBytes(2).CopyTo(save, 0x18);
        BitConverter.GetBytes((short)64).CopyTo(save, 0x34);
        BitConverter.GetBytes((short)52).CopyTo(save, 0x36);
        // Loaded save previews must never count as gameplay, even with valid hearts.
        Equal(true, GameMemoryReader.Decode(save, 0, 0).IsInMenus);
        Equal(false, GameMemoryReader.Decode(save, 0, 0).IsValid);
        // The animated title screen has a PlayState, but gameMode is TITLE_SCREEN.
        Equal(true, GameMemoryReader.Decode(save, 0x1234, 1).IsInMenus);
        Equal(false, GameMemoryReader.Decode(save, 0x1234, 1).IsValid);
        var active = GameMemoryReader.Decode(save, 0x1234, 0);
        Equal(true, active.IsValid);
        Equal(false, active.IsInMenus);
        Equal(3.25, active.Health);
        Equal(4, active.HealthCapacity);
        Equal(false, GameMemoryReader.Decode(save, 0x1234, -1).IsValid);
        Equal(false, GameMemoryReader.Decode(null, 0x1234, 0).IsValid);
        save[0x24] = 0;
        Equal(false, GameMemoryReader.Decode(save, 0x1234, 0).IsValid);

        // The HUD rounds the stored sixteenths to quarter-heart textures.
        save[0x24] = (byte)'Z';
        foreach (var sample in new[] { new { Raw = 53, Display = 3.25 }, new { Raw = 54, Display = 3.5 },
                                       new { Raw = 58, Display = 3.5 }, new { Raw = 59, Display = 3.75 },
                                       new { Raw = 62, Display = 3.75 }, new { Raw = 63, Display = 3.75 } }) {
            BitConverter.GetBytes((short)sample.Raw).CopyTo(save, 0x36);
            Equal(sample.Display, GameMemoryReader.Decode(save, 0x1234, 0).Health);
        }

        // Real ENTRANCE values from 2Ship, deliberately different from SceneId.
        Equal("South Clock Town", Format("FormatZone", 0xD800));
        Equal("East Clock Town", Format("FormatZone", 0xD200));
        Equal("Termina Field", Format("FormatZone", 0x5400));
        Equal("Mayor's Residence (Clock Town)", Format("FormatZone", 0));
        Equal("Woodfall Temple", Format("FormatZone", 0x3000));
        Equal("Woodfall Temple (Odolwa)", Format("FormatZone", 0x3800));
        Equal("Stone Tower Temple (Inverted)", Format("FormatZone", 0x2A00));
        Equal("The Moon", Format("FormatZone", 0xC800));
        Equal("Laundry Pool", Format("FormatZone", 0xDA00));
        Equal("Southern Swamp (Cleared)", Format("FormatZone", 0x0C00));
        Equal("Southern Swamp (Poisoned)", Format("FormatZone", 0x8400));

        int[] unused = { 8, 9, 11, 12, 13, 15, 46, 55 };
        // All 102 defined areas resolve for every spawn/layer combination.
        for (int area = 0; area < 128; area++) {
            bool known = area < 0x6E && Array.IndexOf(unused, area) < 0;
            string name = (string)Format("FormatZone", area << 9);
            Equal(known, !string.IsNullOrEmpty(name));
            for (int lowerBits = 0; lowerBits < 512; lowerBits++)
                Equal(name, Format("FormatZone", (area << 9) | lowerBits));
        }
        Equal(null, Format("FormatZone", -1));
        Equal(null, Format("FormatZone", 0x1D800));
        Equal("1st Day", Format("FormatDay", 1));
        Equal("3rd Day", Format("FormatDay", 3));
        Equal("06:00 AM", Format("FormatTime", (ushort)0x4000));
        Equal("12:00 PM", Format("FormatTime", (ushort)0x8000));
        Equal("11:59 PM", Format("FormatTime", ushort.MaxValue));
        Console.WriteLine("Passed " + checks + " checks.");
    }
}
