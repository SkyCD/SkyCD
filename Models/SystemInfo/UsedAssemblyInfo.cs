using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SkyCD.Models.SystemInfo
{
    public class UsedAssemblyInfo
    {
        private readonly AssemblyName _assemblyName;
        private readonly object[] _attributes;

        public static UsedAssemblyInfo? TryCreate(Assembly assembly)
        {
            if (assembly.IsDynamic)
            {
                return null;
            }

            try
            {
                return new UsedAssemblyInfo(assembly);
            }
            catch
            {
                return null;
            }
        }

        public string? Copyright
        {
            get {
                return _attributes.OfType<AssemblyCopyrightAttribute>().FirstOrDefault()?.Copyright.Trim();
            }
        }

        public string Name {
            get {
                return _assemblyName.Name ?? string.Empty;
            }
        }

        public string Version
        {
            get
            {
                return _assemblyName.Version?.ToString() ?? string.Empty;
            }
        }

        public UsedAssemblyInfo (Assembly assembly)
        {
            _assemblyName = assembly.GetName();
            _attributes = assembly.GetCustomAttributes(true);
        }

        public string? RepositoryUrl
        {
            get
            {
                try
                {
                    var keys = new[] { "RepositoryUrl", "Repository", "PackageProjectUrl", "ProjectUrl", "SourceRepository", "SourceLink" };
                    var meta = _attributes.OfType<AssemblyMetadataAttribute>().FirstOrDefault(a => keys.Contains(a.Key));
                    if (meta != null && !string.IsNullOrEmpty(meta.Value))
                        return meta.Value.Trim();

                    var desc = _attributes.OfType<AssemblyDescriptionAttribute>().FirstOrDefault()?.Description;
                    if (!string.IsNullOrEmpty(desc) && (desc.Contains("http://") || desc.Contains("https://") || desc.Contains("github.com")))
                        return desc.Trim();

                    return null;
                }
                catch
                {
                    return null;
                }
            }
        }

    }
}
