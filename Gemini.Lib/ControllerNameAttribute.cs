namespace Gemini.Lib
{
    /// <summary>
    /// Changes the name of a controller away from the default
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ControllerNameAttribute : Attribute
    {
        /// <summary>
        /// Name of the controller
        /// </summary>
        public string ControllerName { get; }

        /// <summary>
        /// Renames a controller
        /// </summary>
        public ControllerNameAttribute(string controllerName)
        {
            ControllerName = controllerName;
        }
    }
}
