using System.Diagnostics;
using System.Text.Json.Nodes;
using UniGetUI.Core.IconEngine;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;
using UniGetUI.PackageEngine.Classes.Manager.BaseProviders;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.ManagerClasses.Classes;

namespace UniGetUI.PackageEngine.Managers.ScoopManager
{
    internal sealed class ScoopPkgDetailsHelper : BasePkgDetailsHelper
    {
        public ScoopPkgDetailsHelper(Scoop manager) : base(manager) { }

        protected override void GetDetails_UnSafe(IPackageDetails details)
        {
            if (details.Package.Source.Url is not null)
            {
                try
                {
                    if (details.Package.Source.Name.StartsWith("http"))
                        details.ManifestUrl = new Uri(details.Package.Source.Name);
                    else if (details.Package.Source.Name.Contains(":\\"))
                        details.ManifestUrl = new Uri("file:///" + details.Package.Source.Name.Replace("\\", "/"));
                    else
                        details.ManifestUrl = new Uri(details.Package.Source.Url + "/blob/master/bucket/" + details.Package.Id + ".json");
                }
                catch (Exception ex)
                {
                    Logger.Error("Cannot load package manifest URL");
                    Logger.Error(ex);
                }
            }

            string packageId;
            // If source is ellpised or source is a local path, omit source argument
            if (details.Package.Source.Name.Contains("...") || details.Package.Source.Name.Contains(":\\"))
                packageId = $"{details.Package.Id}";
            else
                packageId = $"{details.Package.Source.Name}/{details.Package.Id}";

            using Process p = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Manager.Status.ExecutablePath,
                    Arguments = Manager.Status.ExecutableCallArgs + " cat " + packageId,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                }
            };

            IProcessTaskLogger logger = Manager.TaskLogger.CreateNew(Enums.LoggableTaskType.LoadPackageDetails, p);

            p.Start();
            string JsonString = p.StandardOutput.ReadToEnd();
            logger.AddToStdOut(JsonString);
            logger.AddToStdErr(p.StandardError.ReadToEnd());

            if (JsonNode.Parse(JsonString) is not JsonObject contents)
            {
                throw new InvalidOperationException("Deserialized RawInfo was null");
            }

            // Load description
            if (contents["description"] is JsonArray descriptionList)
            {
                details.Description = "";
                foreach (var line in descriptionList)
                {
                    details.Description += line + "\n";
                }
            }
            else
            {
                details.Description = contents["description"]?.ToString();
            }

            // Load installer type
            if (contents["innsetup"]?.ToString() == "true")
                details.InstallerType = "Inno Setup (" + CoreTools.Translate("extracted") + ")";
            else
                details.InstallerType = CoreTools.Translate("Scoop package");

            // Load homepage and author
            if (Uri.TryCreate(contents?["homepage"]?.ToString() ?? "", UriKind.RelativeOrAbsolute, out var homepageUrl))
            {
                details.HomepageUrl = homepageUrl;

                if (homepageUrl.ToString().StartsWith("https://github.com/"))
                    details.Author = homepageUrl.ToString().Replace("https://github.com/", "").Split("/")[0];
                else
                    details.Author = homepageUrl.Host.Split(".")[^2];
            }

            // Load notes
            if (contents?["notes"] is JsonArray notesList)
            {
                details.ReleaseNotes = "";
                foreach (var line in notesList)
                {
                    details.ReleaseNotes += line + "\n";
                }
            }
            else
            {
                details.ReleaseNotes = contents?["notes"]?.ToString();
            }

            if (contents?["license"] is JsonObject licenseDetails)
            {
                details.License = licenseDetails["identifier"]?.ToString();
                if (Uri.TryCreate(licenseDetails["url"]?.ToString(), UriKind.RelativeOrAbsolute, out var licenseUrl))
                    details.LicenseUrl = licenseUrl;
            }
            else
            {
                details.License = contents?["license"]?.ToString();
            }

            // Load installers
            if (contents?["url"] is JsonArray urlList)
            {
                // Only one installer
                if (Uri.TryCreate(urlList[0]?.ToString(), UriKind.RelativeOrAbsolute, out var installerUrl))
                    details.InstallerUrl = installerUrl;

                details.InstallerHash = (contents?["hash"] as JsonArray)?[0]?.ToString();
            }
            else if (contents?["url"] is JsonValue value)
            {
                // Multiple installers
                if (Uri.TryCreate(value.ToString(), UriKind.RelativeOrAbsolute, out var installerUrl))
                    details.InstallerUrl = installerUrl;

                details.InstallerHash = contents?["hash"]?.ToString();
            }
            else if (contents?["architecture"] is JsonObject archNode)
            {
                // Architecture-based installer
                string arch = archNode.ContainsKey("64bit") ? "64bit" : archNode.First().Key;

                if (Uri.TryCreate(archNode[arch]?["url"]?.ToString(), UriKind.RelativeOrAbsolute, out var installerUrl))
                    details.InstallerUrl = installerUrl;

                details.InstallerHash = archNode[arch]?["hash"]?.ToString();
            }

            if (details.InstallerUrl is not null)
                details.InstallerSize = CoreTools.GetFileSizeAsLong(details.InstallerUrl);

            // Load release notes URL
            if (contents?["checkver"] is JsonObject checkver && Uri.TryCreate(checkver["url"]?.ToString(), UriKind.RelativeOrAbsolute,
                    out var releaseNotesUrl))
                details.ReleaseNotesUrl = releaseNotesUrl;

            // Load install error hints (custom field for UniGetUI)
            // First, load conditional error hints from install_error_hints array
            details.InstallErrorHints.Clear();
            if (contents?["install_error_hints"] is JsonArray hintsArray)
            {
                Logger.Warn($"[ScoopPkgDetailsHelper] Found install_error_hints array with {hintsArray.Count} items");
                foreach (var hintItem in hintsArray)
                {
                    if (hintItem is JsonObject hintObj)
                    {
                        string? pattern = hintObj["pattern"]?.ToString();
                        string? hint = null;

                        // Support both string and array format for hint
                        if (hintObj["hint"] is JsonArray hintLines)
                        {
                            hint = string.Join("\n", hintLines.Select(l => l?.ToString() ?? ""));
                        }
                        else
                        {
                            hint = hintObj["hint"]?.ToString();
                        }

                        if (!string.IsNullOrWhiteSpace(pattern) && !string.IsNullOrWhiteSpace(hint))
                        {
                            Logger.Warn($"[ScoopPkgDetailsHelper] Added error hint rule: pattern='{pattern}', hint='{hint}'");
                            details.InstallErrorHints.Add(new IPackageDetails.ErrorHintRule
                            {
                                Pattern = pattern,
                                Hint = hint
                            });
                        }
                    }
                }
            }

            // Then, load default error hint from install_error_hint (used when no pattern matches)
            if (contents?["install_error_hint"] is JsonArray defaultHintList)
            {
                details.InstallErrorHint = "";
                foreach (var line in defaultHintList)
                {
                    details.InstallErrorHint += line + "\n";
                }
                details.InstallErrorHint = details.InstallErrorHint.TrimEnd('\n');
                Logger.Warn($"[ScoopPkgDetailsHelper] Loaded default install_error_hint (array format): {details.InstallErrorHint}");
            }
            else if (contents?["install_error_hint"] is not null)
            {
                details.InstallErrorHint = contents["install_error_hint"]?.ToString();
                Logger.Warn($"[ScoopPkgDetailsHelper] Loaded default install_error_hint: {details.InstallErrorHint}");
            }

            details.Dependencies.Clear();
            _getDepends(details, contents);
            _getSuggests(details, contents);

            logger.Close(0);
        }

        private static void _getSuggests(IPackageDetails details, JsonObject? contents)
        {
            foreach (var rawDep in (contents?["suggest"]?.AsObject() ?? []))
            {
                List<string> innerDeps = [];

                if(rawDep.Value is JsonValue value) innerDeps.Add(value.GetValue<string>());
                else
                {
                    foreach (var iDep in rawDep.Value?.AsArray() ?? [])
                    {
                        string? val = iDep?.GetValue<string>();
                        if(val is not null) innerDeps.Add(val);
                    }
                }

                foreach(var val in innerDeps)
                    details.Dependencies.Add(new()
                    {
                        Name = val,
                        Version = "",
                        Mandatory = false,
                    });
            }
        }

        private static void _getDepends(IPackageDetails details, JsonObject? contents)
        {
            var node = contents?["depends"];
            List<string> innerDeps = [];


            if(node is JsonValue value) innerDeps.Add(value.GetValue<string>());
            else
            {
                foreach (var iDep in node?.AsArray() ?? [])
                {
                    string? val = iDep?.GetValue<string>();
                    if(val is not null) innerDeps.Add(val);
                }
            }

            foreach(var val in innerDeps)
                details.Dependencies.Add(new()
                {
                    Name = val,
                    Version = "",
                    Mandatory = true,
                });
        }

        protected override CacheableIcon? GetIcon_UnSafe(IPackage package)
        {
            if (package.Source.Name.Contains("...") || package.Source.Name.Contains(":\\"))
                return null;

            string bucketIconsDir = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop", "buckets", package.Source.Name, "icons");

            string[] extensions = [".png", ".ico", ".jpg", ".svg", ".webp"];
            foreach (string ext in extensions)
            {
                string iconPath = Path.Join(bucketIconsDir, package.Id + ext);
                if (File.Exists(iconPath))
                    return new CacheableIcon(iconPath);
            }

            return null;
        }

        protected override IReadOnlyList<Uri> GetScreenshots_UnSafe(IPackage package)
        {
            throw new NotImplementedException();
        }

        protected override string? GetInstallLocation_UnSafe(IPackage package)
        {
            return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps",
                package.Id, "current");
        }

        protected override IReadOnlyList<string> GetInstallableVersions_UnSafe(IPackage package)
        {
            throw new InvalidOperationException("Scoop does not support custom package versions");
        }
    }
}
