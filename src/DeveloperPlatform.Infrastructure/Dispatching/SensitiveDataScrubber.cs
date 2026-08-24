using System.Reflection;
using System.Text.Json;
using DeveloperPlatform.Application.Attributes;

namespace DeveloperPlatform.Infrastructure.Dispatching;

public sealed class SensitiveDataScrubber
{
    public string ScrubAndSerialize<TCommand>(TCommand command)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var prop in typeof(TCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var isSensitive = prop.GetCustomAttribute<SensitiveDataAttribute>() != null;
            dict[prop.Name] = isSensitive ? "[REDACTED]" : prop.GetValue(command);
        }

        return JsonSerializer.Serialize(dict);
    }
}
