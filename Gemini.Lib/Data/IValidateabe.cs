namespace Gemini.Lib.Data
{
    /// <summary>
    /// Validateable interface
    /// </summary>
    public interface IValidateabe
    {
        /// <summary>
        /// Validates the instance and throws an exception if validation fails
        /// </summary>
        void Validate();
    }
}
