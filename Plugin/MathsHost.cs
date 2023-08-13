using AyrA.AutoDI;
using Gemini.Lib;
using Gemini.Lib.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Plugin
{
    [AutoDIRegister(AutoDIType.Singleton, null, nameof(Register))]
    [ControllerName("Maths")]
    public class MathsHost : GeminiHost
    {
        private class Numbers
        {
            public double A { get; private set; }
            public double B { get; private set; }
            public static Numbers Parse(string query)
            {
                var m = Regex.Match(query, @"^\s*(\S+)\s+(\S+)\s*$");
                if (m.Success)
                {
                    if (
                        double.TryParse(m.Groups[1].Value, out var a) &&
                        double.TryParse(m.Groups[2].Value, out var b) &&
                        !double.IsNaN(a) && !double.IsNaN(b) &&
                        !double.IsInfinity(a) && !double.IsInfinity(b))
                    {
                        return new Numbers()
                        {
                            A = a,
                            B = b
                        };
                    }
                }
                throw new ArgumentException("Query not in '<number><space><number>' format");
            }
        }
        /// <summary>
        /// DI Registration
        /// </summary>
        public static IServiceCollection Register(IServiceCollection collection)
        {
            return collection
                .AddSingleton<GeminiController<MathsHost>>()
                .AddSingleton<MathsHost>();
        }

        private static Task<GeminiResponse> Prompt(string prompt)
        {
            return Task.FromResult(new GeminiResponse(StatusCode.Input, null, prompt));
        }

        public Task<GeminiResponse> Add(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return Prompt("Enter two numbers separated by a space");
            }
            var n = Numbers.Parse(query);
            return Task.FromResult(GeminiResponse.Ok((n.A + n.B).ToString()));
        }

        public Task<GeminiResponse> Subtract(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return Prompt("Enter two numbers separated by a space");
            }
            var n = Numbers.Parse(query);
            return Task.FromResult(GeminiResponse.Ok((n.A - n.B).ToString()));
        }

        public Task<GeminiResponse> Multiply(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return Prompt("Enter two numbers separated by a space");
            }
            var n = Numbers.Parse(query);
            return Task.FromResult(GeminiResponse.Ok((n.A * n.B).ToString()));
        }

        public Task<GeminiResponse> Divide(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return Prompt("Enter two numbers separated by a space");
            }
            var n = Numbers.Parse(query);
            return Task.FromResult(GeminiResponse.Ok((n.A / n.B).ToString()));
        }

        public Task<GeminiResponse> Calc(FormData fd)
        {
            var dblA = 0.0;
            var dblB = 0.0;
            StringValues? op = null;

            var ok = fd.TryGetValue("A", out var numA) &&
                fd.TryGetValue("B", out var numB) &&
                fd.TryGetValue("Operator", out op) &&
                !string.IsNullOrEmpty(op) &&
                double.TryParse(numA, out dblA) &&
                double.TryParse(numB, out dblB) &&
                double.IsFinite(dblA) &&
                double.IsFinite(dblB) &&
                !double.IsNaN(dblA) &&
                !double.IsNaN(dblB) &&
                "+-*/".Contains(op);
            if (ok && op != null)
            {
                var result = 0.0;
                switch (op)
                {
                    case "+":
                        result = dblA + dblB; break;
                    case "-":
                        result = dblA - dblB; break;
                    case "*":
                        result = dblA * dblB; break;
                    case "/":
                        result = dblA / dblB; break;
                }
                return Task.FromResult(GeminiResponse.Ok("# Result: " + result.ToString()));
            }
            var f = new Form
            {
                new FormField()
                {
                    Name = "A",
                    Title = "First operand (A)",
                    Description = "Enter the first value here",
                    Required = true,
                },
                new FormField()
                {
                    Name = "Operator",
                    Title = "Operator",
                    Required = true,
                    DefaultValue = "+"
                },
                new FormField()
                {
                    Name = "B",
                    Title = "Second operand (B)",
                    Description = "Enter the second value here",
                    Required = true
                }
            };
            var operatorField = f[1];

            operatorField.Options.Add("+", "A+B");
            operatorField.Options.Add("-", "A-B");
            operatorField.Options.Add("*", "A*B");
            operatorField.Options.Add("/", "A/B");

            f.MaxSize = 100;

            return Task.FromResult(f.ToResponse());
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        public override Task<GeminiResponse?> Request(Uri url, IPEndPoint clientAddress, X509Certificate? clientCertificate)
        {
            return Task.FromResult<GeminiResponse?>(null);
        }
    }
}
