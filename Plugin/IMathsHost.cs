using Gemini.Lib.Data;

namespace Plugin
{
    public interface IMathsHost
    {
        Task<GeminiResponse> Add(string query);
        Task<GeminiResponse> Calc(FormData fd);
        Task<GeminiResponse> Divide(string query);
        Task<GeminiResponse> Multiply(string query);
        Task<GeminiResponse> Subtract(string query);
    }
}