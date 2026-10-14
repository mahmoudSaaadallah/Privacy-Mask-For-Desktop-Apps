using PrivacyMask.Updates;

namespace PrivacyMask.ReleaseTool;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || args[0] is not ("pack" or "verify"))
                throw new ReleaseValidationException("Use pack or verify with named release metadata options.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var allowed = args[0] == "pack"
                ? new[] { "--exe", "--output", "--version", "--file-version", "--commit", "--policy", "--key-id" }
                : new[] { "--output", "--version", "--file-version", "--commit", "--policy" };
            for (var i = 1; i < args.Length; i += 2)
                if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1]))
                    throw new ReleaseValidationException("Unknown, duplicate or missing CLI options. Private keys are never CLI arguments.");
            if (allowed.Any(name => !options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value)))
                throw new ReleaseValidationException("Missing required release metadata option.");
            var policy = ContractJson.Read<ReleasePolicy>(UpdatePackages.ReadBounded(options["--policy"], 64 * 1024), 64 * 1024);
            ManifestRules.Validate(policy);
            if (args[0] == "pack")
            {
                const string secretName = "PRIVACYMASK_UPDATE_PRIVATE_KEY_PEM";
                var pem = Environment.GetEnvironmentVariable(secretName);
                Environment.SetEnvironmentVariable(secretName, null);
                if (string.IsNullOrWhiteSpace(pem) || pem.Length > 16 * 1024)
                    throw new ReleaseValidationException("Signing secret is missing or outside its allowed size.");
                UpdatePackages.Create(options["--exe"], options["--output"], options["--version"],
                    options["--file-version"], options["--commit"], policy, options["--key-id"], pem);
            }
            else UpdatePackages.Verify(options["--output"], policy, options["--version"], options["--commit"], options["--file-version"]);
            Console.WriteLine("Signed update assets validated successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            // Never dump crypto/JSON exception details, argv, env, stack or key material.
            Console.Error.WriteLine(exception is ReleaseValidationException
                ? exception.Message : "Release tooling rejected invalid input or could not complete safely.");
            return 1;
        }
    }
}
