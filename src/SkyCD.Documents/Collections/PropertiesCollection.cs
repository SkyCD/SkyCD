using System.Collections.Generic;

namespace SkyCD.Documents.Collections;

public class PropertiesCollection : SortedDictionary<string, object?>
{
    public PropertiesCollection()
        : base(System.StringComparer.CurrentCultureIgnoreCase)
    {
    }

    public PropertiesCollection(IDictionary<string, object?> source)
        : base(source, System.StringComparer.CurrentCultureIgnoreCase)
    {
    }
}
