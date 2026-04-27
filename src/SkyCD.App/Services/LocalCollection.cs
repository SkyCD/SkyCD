using System.Runtime.Serialization;

namespace SkyCD.App.Services;

public enum LocalCollection
{
    [EnumMember(Value = "catalog")]
    Catalog,

    [EnumMember(Value = "settings")]
    Settings
}
