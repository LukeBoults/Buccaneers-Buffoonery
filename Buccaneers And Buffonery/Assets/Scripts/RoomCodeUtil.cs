using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

public static class RoomCodeUtil
{
    const string SEP = "."; // separator that is NOT in Base64URL alphabet

    // Public API --------------------------------------------------------------

    public static string MakeCodeIPv4Port(string ipv4, ushort port, byte version = 1)
    {
        if (!IPAddress.TryParse(ipv4, out var ip) ||
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Not a valid IPv4 address.", nameof(ipv4));

        var ipBytes = ip.GetAddressBytes(); // 4 bytes
        var data = new byte[8];             // [ver, ip0, ip1, ip2, ip3, portHi, portLo, crc]
        data[0] = version;
        Buffer.BlockCopy(ipBytes, 0, data, 1, 4);
        data[5] = (byte)(port >> 8);
        data[6] = (byte)(port & 0xFF);
        data[7] = Crc8(data, 0, 7);         // crc over first 7 bytes

        var b64 = ToBase64Url(data);        // DO NOT uppercase: Base64 is case-sensitive
        return GroupEvery(b64, 4, SEP);     // e.g., abCd.R3_- . XyZ (dots are safe to strip later)
    }

    public static bool TryDecodeIPv4Port(string code, out string ipv4, out ushort port, out string error)
    {
        ipv4 = ""; port = 0; error = "";

        try
        {
            // Keep only Base64URL chars: A-Z a-z 0-9 - _
            // (this safely removes separators like '.' or spaces without touching real data)
            string compact = Regex.Replace(code ?? string.Empty, @"[^A-Za-z0-9\-_]", "");

            var data = FromBase64Url(compact);
            if (data == null || data.Length != 8)
            {
                error = "Bad code length.";
                return false;
            }

            byte crcExpected = data[7];
            byte crcActual = Crc8(data, 0, 7);
            if (crcExpected != crcActual)
            {
                error = "Checksum mismatch (code mistyped?)";
                return false;
            }

            var ipBytes = new byte[4];
            Buffer.BlockCopy(data, 1, ipBytes, 0, 4);
            ipv4 = new IPAddress(ipBytes).ToString();
            port = (ushort)((data[5] << 8) | data[6]);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // Helpers ----------------------------------------------------------------

    static string ToBase64Url(byte[] bytes)
    {
        var s = Convert.ToBase64String(bytes); // '+', '/', '='
        s = s.TrimEnd('=').Replace('+', '-').Replace('/', '_'); // URL-safe
        return s; // IMPORTANT: no ToUpper/Lower here
    }

    static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    // CRC-8 (poly 0x07)
    static byte Crc8(byte[] data, int offset, int count)
    {
        byte crc = 0x00;
        for (int i = 0; i < count; i++)
        {
            crc ^= data[offset + i];
            for (int b = 0; b < 8; b++)
                crc = (byte)(((crc & 0x80) != 0) ? ((crc << 1) ^ 0x07) : (crc << 1));
        }
        return crc;
    }

    static string GroupEvery(string s, int group, string sep)
    {
        var sb = new StringBuilder(s.Length + s.Length / group);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && i % group == 0) sb.Append(sep);
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}