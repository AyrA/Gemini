using System.Net;

namespace Gemini.Lib.Network
{
    /// <summary>
    /// Represents an IP address range
    /// </summary>
    public class IpRange
    {
        /// <summary>
        /// Lower IP address
        /// </summary>
        private readonly byte[] _ipLow;
        /// <summary>
        /// Upper IP address
        /// </summary>
        private readonly byte[] _ipHigh;

        /// <summary>
        /// Gets the lower IP address
        /// </summary>
        public IPAddress LowerAddress => new(_ipLow);
        /// <summary>
        /// Gets the upper IP address
        /// </summary>
        public IPAddress UpperAddress => new(_ipHigh);

        /// <summary>
        /// Creates a new IP range using the given addresses
        /// </summary>
        /// <param name="lower">Lower address</param>
        /// <param name="upper">Upper address</param>
        public IpRange(IPAddress lower, IPAddress upper)
        {
            if (lower is null)
            {
                throw new ArgumentNullException(nameof(lower));
            }
            if (upper is null)
            {
                throw new ArgumentNullException(nameof(upper));
            }
            if (lower.AddressFamily != upper.AddressFamily)
            {
                throw new ArgumentException("Address types do not match", nameof(upper));
            }
            //IPv4 is faster. Use it if both addresses are mapped v4
            if (lower.IsIPv4MappedToIPv6 && upper.IsIPv4MappedToIPv6)
            {
                _ipLow = lower.MapToIPv4().GetAddressBytes();
                _ipHigh = upper.MapToIPv4().GetAddressBytes();
            }
            else
            {
                _ipLow = lower.GetAddressBytes();
                _ipHigh = upper.GetAddressBytes();
            }
            //Check if lower address is actually lower
            for (var i = 0; i < _ipLow.Length; i++)
            {
                if (_ipLow[i] < _ipHigh[i])
                {
                    break;
                }
                if (_ipLow[i] >= _ipHigh[i])
                {
                    throw new ArgumentException("Upper addres is smaller than lower address");
                }
            }
        }

        /// <summary>
        /// Creates an IP range from the given IP address and CIDR mask
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="cidr">CIDR mask</param>
        /// <remarks>
        /// <paramref name="address"/> will be set to the lowest possible address using the given mask.
        /// This means 192.168.1.10/24 will be turned into 192.169.1.0/24.
        /// Use the other constructor if this behavior is not appropriate.
        /// </remarks>
        public IpRange(IPAddress address, int cidr) : this(
            CidrDecoder.GetCidrLowerAddress(address, cidr),
            CidrDecoder.GetCidrUpperAddress(address, cidr))
        {
            //NOOP
        }

        /// <summary>
        /// Checks if the given address is inside of the IP address range (inclusive)
        /// </summary>
        /// <param name="addr">IP address</param>
        /// <returns>true, if in range</returns>
        public bool InRange(IPAddress addr)
        {
            return Compare(addr) == 0;
        }

        /// <summary>
        /// Compares the given IP address to the IP address range and returns the result
        /// </summary>
        /// <param name="addr">IP address</param>
        /// <returns>
        /// negative if less than <see cref="LowerAddress"/>,
        /// positive if more than <see cref="UpperAddress"/>,
        /// 0 otherwise
        /// </returns>
        /// <remarks>
        /// Currently this returns -1, 0, +1, but this is not guaranteed in the future.
        /// Negative and positive results may change the digit
        /// </remarks>
        public int Compare(IPAddress addr)
        {
            if (addr is null)
            {
                throw new ArgumentNullException(nameof(addr));
            }
            byte[] bytes;

            //Convert v6 to v4 if possible
            if (addr.IsIPv4MappedToIPv6 && _ipLow.Length == 4)
            {
                bytes = addr.MapToIPv4().GetAddressBytes();
            }
            else
            {
                bytes = addr.GetAddressBytes();
            }
            //Check if address below range or at the very start
            var comp = Compare(_ipLow, bytes);
            if (comp <= 0)
            {
                return Math.Sign(comp);
            }
            //Check if address inside range or at the very end
            comp = Compare(_ipHigh, bytes);
            return Math.Max(0, Math.Sign(comp));
        }

        /// <summary>
        /// Compares two byte arrays
        /// </summary>
        /// <param name="a">Array a</param>
        /// <param name="b">Array b</param>
        /// <returns>First encountered difference, or zero if none is found</returns>
        private static int Compare(byte[] a, byte[] b)
        {
            if (a is null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b is null)
            {
                throw new ArgumentNullException(nameof(b));
            }
            var l = b.Length - a.Length;
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                if (a[i] != b[i])
                {
                    return b[i] - a[i];
                }
            }
            return l;
        }

        /// <summary>
        /// Parses an IP range. This can be:
        /// "ip" (single address),
        /// "ip-ip" (range),
        /// "ip/cidr" (cidr mask)
        /// </summary>
        /// <param name="addr">Address specification</param>
        /// <returns>Parsed range</returns>
        public static IpRange Parse(string addr)
        {
            if (string.IsNullOrWhiteSpace(addr))
            {
                throw new ArgumentException($"'{nameof(addr)}' cannot be null or whitespace.", nameof(addr));
            }

            //Single ip entry
            if (IPAddress.TryParse(addr, out var ip))
            {
                return new IpRange(ip, ip);
            }

            //CIDR
            if (addr.Contains('/'))
            {
                var parts = addr.Split('/');
                if (parts.Length != 2)
                {
                    throw new FormatException("Invalid CIDR format");
                }
                return new IpRange(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
            }

            //Custom range
            if (addr.Contains('-'))
            {
                var parts = addr.Split('-');
                if (parts.Length != 2)
                {
                    throw new FormatException("Invalid IP-IP range format");
                }
                return new IpRange(IPAddress.Parse(parts[0].Trim()), IPAddress.Parse(parts[1].Trim()));
            }
            throw new FormatException($"Argument '{addr}' neither in 'IP/CIDR' nor 'IP-IP', nor 'IP' format");
        }
    }
}