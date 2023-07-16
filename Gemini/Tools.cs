using System.Text;

namespace Gemini
{
    public static class Tools
    {
        public static void DumpHex(byte[]? data)
        {
            Console.WriteLine("== HEX DUMP ==");
            if (data != null)
            {
                if (data.Length > 0)
                {
                    for (var i = 0; i < data.Length; i += 16)
                    {
                        var chunk = data.Skip(i).Take(16).ToArray();
                        var hex = string.Join(" ", chunk.Select(m => m.ToString("X2")));
                        var ascii = Encoding.Latin1.GetString(chunk.Select(m => (byte)(m < 0x20 || (m >= 0x7F && m <= 0x9F) ? 0x2E : m)).ToArray());
                        Console.WriteLine("{0:X8}: {1,-48} {2,-16}", i, hex, ascii);
                    }
                }
                else
                {
                    Console.WriteLine("WRN: Data is empty");
                }
            }
            else
            {
                Console.WriteLine("WRN: Data is null");
            }
            Console.WriteLine("== END ==");
        }
    }
}
