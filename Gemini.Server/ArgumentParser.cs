namespace Gemini.Server
{
    public class ArgumentParser
    {
        public bool IsEmpty => !(IsHelp || IsService || IsInstall || IsUninstall
            || !string.IsNullOrEmpty(InstallFile) || UninstallId != Guid.Empty);
        public bool IsHelp { get; private set; }
        public bool IsService { get; private set; }
        public bool IsInstall { get; private set; }
        public bool IsUninstall { get; private set; }
        public Guid UninstallId { get; private set; }
        public string? InstallFile { get; private set; }

        private ArgumentParser() { }

        private void Validate()
        {
            //Do not validate if help was requested
            if (IsHelp)
            {
                return;
            }
            if (IsInstall && string.IsNullOrWhiteSpace(InstallFile))
            {
                throw new ArgumentException("/install requires a file name");
            }
            if (IsInstall && !File.Exists(InstallFile))
            {
                throw new FileNotFoundException("File specified with /install cannot be found");
            }
            if (IsUninstall && UninstallId == Guid.Empty)
            {
                throw new ArgumentException("/uninstall requires a valid id");
            }
            if (IsInstall && IsUninstall)
            {
                throw new ArgumentException("Cannot use /install and /uninstall at the same time");
            }
            if (IsService && (IsInstall || IsUninstall))
            {
                throw new ArgumentException("Cannot use /service together with /install or /uninstall");
            }
        }

        public static ArgumentParser Parse(string[] args)
        {
            bool modeSet = false;

            var parser = new ArgumentParser();

            if (args.Contains("/?"))
            {
                parser.IsHelp = true;
                return parser;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var next = i + 1 < args.Length ? args[i + 1] : null;
                switch (arg.ToUpper())
                {
                    case "/SERVICE":
                        modeSet = CheckMode(modeSet, arg);
                        parser.IsService = true;
                        break;
                    case "/INSTALL":
                        modeSet = CheckMode(modeSet, arg);
                        parser.IsInstall = true;
                        if (string.IsNullOrWhiteSpace(next))
                        {
                            throw new ArgumentException("Install switch requires an argument");
                        }
                        parser.InstallFile = next;
                        ++i;
                        break;
                    case "/UNINSTALL":
                        modeSet = CheckMode(modeSet, arg);
                        parser.IsUninstall = true;
                        if (string.IsNullOrWhiteSpace(next))
                        {
                            throw new ArgumentException("Uninstall switch requires an argument");
                        }
                        if (Guid.TryParse(next, out var id))
                        {
                            parser.UninstallId = id;
                        }
                        else
                        {
                            throw new ArgumentException($"Unable to parse '{next}' as valid plugin id");
                        }
                        ++i;
                        break;
                    default:
                        throw new ArgumentException($"Unknown switch: '{arg}'");
                }
            }
            parser.Validate();
            return parser;
        }

        private static bool CheckMode(bool mode, string arg)
        {
            if (mode)
            {
                throw new ArgumentException($"Invalid command line switch combination when parsing '{arg}'");
            }
            return true;
        }
    }
}
