using System.Net;

namespace Gemini.Lib.Network
{
    public static class CidrDecoder
    {
        private static readonly Dictionary<int, int> maskMap = new()
        {
            { 0b00000000, 0 },
            { 0b10000000, 1 },
            { 0b11000000, 2 },
            { 0b11100000, 3 },
            { 0b11110000, 4 },
            { 0b11111000, 5 },
            { 0b11111100, 6 },
            { 0b11111110, 7 },
            { 0b11111111, 8 },
        };

        public static int GetCidrMask(IPAddress lower, IPAddress upper)
        {
            var bLow = (lower.IsIPv4MappedToIPv6 ? lower.MapToIPv4() : lower).GetAddressBytes();
            var bHigh = (upper.IsIPv4MappedToIPv6 ? upper.MapToIPv4() : upper).GetAddressBytes();
            int mask = 0;
            bool hasPartMask = false;
            if (bLow.Length != bHigh.Length)
            {
                throw new FormatException("Both arguments must be of the same address type. If IPv6, they must either be both v4 mapped addresses or not");
            }
            for (var i = 0; i < bLow.Length; i++)
            {
                if (bLow[i] == bHigh[i])
                {
                    if (hasPartMask)
                    {
                        throw new FormatException("The IP addresses are not the lower and upper address of a valid CIDR mask");
                    }
                    mask += 8;
                }
                else
                {
                    hasPartMask = true;
                    var part = (byte)~(bLow[i] ^ bHigh[i]);
                    if (!maskMap.TryGetValue(part, out int maskAdd))
                    {
                        throw new FormatException("Supplied addresses yield an invalid CIDR mask");
                    }
                    mask += maskAdd;
                }
            }
            return mask;
        }

        public static IPAddress GetCidrLowerAddress(IPAddress addr, int cidr)
        {
            if (addr is null)
            {
                throw new ArgumentNullException(nameof(addr));
            }
            var bytes = (addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr).GetAddressBytes();
            if (cidr < 0 || cidr > bytes.Length * 8)
            {
                throw new ArgumentOutOfRangeException(nameof(cidr));
            }
            var outBytes = new byte[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i * 8 < cidr)
                {
                    //Shortcut: Copy the byte over if the entire byte is covered by CIDR mask
                    if ((i * 8) + 8 < cidr)
                    {
                        outBytes[i] = bytes[i];
                    }
                    else
                    {
                        //Build partial mask and copy only the masked bits
                        int mask = 0xFF << (8 - (cidr % 8));
                        outBytes[i] = (byte)(bytes[i] & mask);
                    }
                }
            }
            return new IPAddress(outBytes);
        }

        public static IPAddress GetCidrUpperAddress(IPAddress addr, int cidr)
        {
            if (addr is null)
            {
                throw new ArgumentNullException(nameof(addr));
            }
            var bytes = (addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr).GetAddressBytes();
            if (cidr < 0 || cidr > bytes.Length * 8)
            {
                throw new ArgumentOutOfRangeException(nameof(cidr));
            }
            var outBytes = new byte[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i * 8 < cidr)
                {
                    //Shortcut: Copy the byte over if the entire byte is covered by CIDR mask
                    if ((i * 8) + 8 < cidr)
                    {
                        outBytes[i] = bytes[i];
                    }
                    else
                    {
                        //Build partial mask and keep only the masked bits
                        int mask = 0xFF >> (cidr % 8);
                        outBytes[i] = (byte)(bytes[i] | mask);
                    }
                }
                else
                {
                    outBytes[i] = 0xFF;
                }
            }
            return new IPAddress(outBytes);
        }
    }
}
