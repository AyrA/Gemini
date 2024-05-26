namespace Gemini.Lib
{
    /// <summary>
    /// Changes the name of a controller away from the default
    /// </summary>
    /// <remarks>
    /// Renames a controller
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public class ControllerNameAttribute(string controllerName) : Attribute
    {
        /// <summary>
        /// Name of the controller
        /// </summary>
        public string ControllerName { get; } = controllerName;
    }
}
