using System.Net;

namespace Gemini.Lib.Network
{
    public class IpRange
    {
        private readonly byte[] _ipLow;
        private readonly byte[] _ipHigh;

        public IPAddress LowerAddress => new(_ipLow);
        public IPAddress UpperAddress => new(_ipHigh);

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
        }

        public IpRange(IPAddress address, int cidr) : this(
            CidrDecoder.GetCidrLowerAddress(address, cidr),
            CidrDecoder.GetCidrUpperAddress(address, cidr))
        {
            //NOOP
        }

        public bool InRange(IPAddress addr)
        {
            return Compare(addr) == 0;
        }

        public int Compare(IPAddress addr)
        {
            if (addr is null)
            {
                throw new ArgumentNullException(nameof(addr));
            }
            byte[] bytes;
            if (addr.IsIPv4MappedToIPv6)
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

        public static IpRange Parse(string addr)
        {
            if (string.IsNullOrWhiteSpace(addr))
            {
                throw new ArgumentException($"'{nameof(addr)}' cannot be null or whitespace.", nameof(addr));
            }
            if (addr.Contains('/'))
            {
                var parts = addr.Split('/');
                if (parts.Length != 2)
                {
                    throw new FormatException("Invalid CIDR format");
                }
                return new IpRange(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
            }
            if (addr.Contains('-'))
            {
                var parts = addr.Split('-');
                if (parts.Length != 2)
                {
                    throw new FormatException("Invalid IP-IP range format");
                }
                return new IpRange(IPAddress.Parse(parts[0].Trim()), IPAddress.Parse(parts[1].Trim()));
            }
            throw new FormatException("Argument neither in 'IP/CIDR' nor 'IP-IP' format");
        }
    }
}