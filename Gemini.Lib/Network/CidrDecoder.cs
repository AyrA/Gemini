using System.Net;

namespace Gemini.Lib.Network
{
    /// <summary>
    /// Handles CIDR addresses
    /// </summary>
    public static class CidrDecoder
    {
        /// <summary>
        /// Possible masks per octet
        /// </summary>
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
        /// <summary>
        /// Inverse of <see cref="maskMap"/>
        /// </summary>
        private static readonly byte[] reverseMap =
        [
            0b00000000,
            0b10000000,
            0b11000000,
            0b11100000,
            0b11110000,
            0b11111000,
            0b11111100,
            0b11111110,
            0b11111111,
        ];

        private static readonly byte[][] v6Mask;
        private static readonly byte[][] v4Mask;

        static CidrDecoder()
        {
            //Initialize v4 masks
            v4Mask = new byte[33][];
            for (var i = 0; i < v4Mask.Length; i++)
            {
                var cidr = i;
                var data = new byte[4];
                int j = 0;
                while (cidr > 0)
                {
                    data[j++] = reverseMap[Math.Min(8, cidr)];
                    cidr -= 8;
                }
                v4Mask[i] = data;
            }


            //Initialize v6 masks
            v6Mask = new byte[129][];
            for (var i = 0; i < v6Mask.Length; i++)
            {
                var cidr = i;
                var data = new byte[16];
                int j = 0;
                while (cidr > 0)
                {
                    data[j++] = reverseMap[Math.Min(8, cidr)];
                    cidr -= 8;
                }
                v6Mask[i] = data;
            }
        }

        /// <summary>
        /// Get the CIDR mask using the given address range
        /// </summary>
        /// <param name="lower">Lower IP address</param>
        /// <param name="upper">Upper IP address</param>
        /// <returns>CIDR mask</returns>
        /// <remarks>
        /// This requires that <paramref name="lower"/> and <paramref name="upper"/>
        /// form the lower and upper address of a range that exactly fits a CIDR mask.
        /// Throws otherwise
        /// </remarks>
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
                //Shortcut for when entire bytes are identical
                if (bLow[i] == bHigh[i])
                {
                    if (hasPartMask)
                    {
                        throw new FormatException($"The IP addresses are not the lower and upper address of a valid CIDR mask. Failed at byte offset '{i}'");
                    }
                    mask += 8;
                }
                else
                {
                    hasPartMask = true;
                    var part = (byte)~(bLow[i] ^ bHigh[i]);
                    if (!maskMap.TryGetValue(part, out int maskAdd))
                    {
                        throw new FormatException($"Supplied addresses yield an invalid CIDR mask. Failed at byte offset '{i}'");
                    }
                    mask += maskAdd;
                }
            }
            return mask;
        }

        /// <summary>
        /// Gets the lowest address given the supplied address and mask
        /// </summary>
        /// <param name="addr">IP address</param>
        /// <param name="cidr">CIDR</param>
        /// <returns>Lowest address after masking <paramref name="addr"/> with <paramref name="cidr"/></returns>
        public static IPAddress GetCidrLowerAddress(IPAddress addr, int cidr)
        {
            ArgumentNullException.ThrowIfNull(addr);
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

        /// <summary>
        /// Gets the highest address given the supplied address and mask
        /// </summary>
        /// <param name="addr">IP address</param>
        /// <param name="cidr">CIDR</param>
        /// <returns>Highest address after masking <paramref name="addr"/> with <paramref name="cidr"/></returns>
        public static IPAddress GetCidrUpperAddress(IPAddress addr, int cidr)
        {
            ArgumentNullException.ThrowIfNull(addr);
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

        /// <summary>
        /// Converts a CIDR mask into a subnet mask
        /// </summary>
        /// <param name="cidr">CIDR</param>
        /// <param name="ipv6">Use IPv6</param>
        /// <returns>Subnet mask</returns>
        public static IPAddress CidrToAddress(int cidr, bool ipv6)
        {
            if (cidr < 0 || (!ipv6 && cidr > 32) || (ipv6 && cidr > 128))
            {
                throw new ArgumentOutOfRangeException(nameof(cidr));
            }
            return new IPAddress(ipv6 ? v6Mask[cidr] : v4Mask[cidr]);
        }
    }
}
