using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

internal static class FtpPath
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
    }

    public static string Combine(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return Normalize(directory);
        }

        if (name.StartsWith('/'))
        {
            return Normalize(name);
        }

        var parent = Normalize(directory);
        return parent == "/" ? "/" + name : parent + "/" + name;
    }
}
