using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SkyCD.Models.SystemInfo;

namespace SkyCD.Services
{
    public static class AssemblyInfoScanner
    {
        public static IEnumerable<UsedAssemblyInfo> ScanLoadedAssemblies()
        {
            var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;

            return AppDomain.CurrentDomain.GetAssemblies()
                .Select((Assembly a) => UsedAssemblyInfo.TryCreate(a))
                .OfType<UsedAssemblyInfo>()
                .Where((UsedAssemblyInfo info) => !string.Equals(info.Name, entryAssemblyName, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy((UsedAssemblyInfo info) => info.Name, StringComparer.OrdinalIgnoreCase)
            ;
        }
    }
}
