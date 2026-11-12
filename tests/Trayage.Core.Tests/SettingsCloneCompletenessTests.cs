using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trayage.Core.Configuration;

namespace Trayage.Core.Tests;

/// <summary>
/// <see cref="TrayageSettings.Clone"/> is written out property by property, so a property added
/// later and forgotten there would silently revert to its default in every cached load. These
/// tests set every persisted property to a non-default value and require the clone to match.
/// </summary>
public sealed class SettingsCloneCompletenessTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void TrayageSettingsClone_CopiesEveryPersistedProperty()
    {
        var original = (TrayageSettings)Fill(typeof(TrayageSettings), "settings");

        Assert.Equal(JsonSerializer.Serialize(original, Json), JsonSerializer.Serialize(original.Clone(), Json));
    }

    [Fact]
    public void ProviderAccountClone_CopiesEveryPersistedProperty()
    {
        var original = (ProviderAccount)Fill(typeof(ProviderAccount), "account");

        Assert.Equal(JsonSerializer.Serialize(original, Json), JsonSerializer.Serialize(original.Clone(), Json));
    }

    [Fact]
    public void Filler_ChangesEveryPersistedPropertyFromItsDefault()
    {
        // Guards the test itself: if the filler left something at its default, a forgotten
        // property would compare equal and slip through.
        foreach (var type in new[] { typeof(TrayageSettings), typeof(ProviderAccount), typeof(NotificationSettings) })
        {
            var defaults = Activator.CreateInstance(type)!;
            var filled = Fill(type, type.Name);
            foreach (var property in PersistedProperties(type))
            {
                Assert.NotEqual(
                    JsonSerializer.Serialize(property.GetValue(defaults), Json),
                    JsonSerializer.Serialize(property.GetValue(filled), Json));
            }
        }
    }

    private static IEnumerable<PropertyInfo> PersistedProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

    /// <summary>An instance of <paramref name="type"/> with every persisted property moved off its default.</summary>
    private static object Fill(Type type, string path)
    {
        var instance = Activator.CreateInstance(type)!;
        foreach (var property in PersistedProperties(type))
        {
            property.SetValue(instance, Different(property.PropertyType, property.GetValue(instance), $"{path}.{property.Name}"));
        }

        return instance;
    }

    private static object Different(Type type, object? current, string path)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return path;
        }

        if (underlying == typeof(bool))
        {
            return current is not true;
        }

        if (underlying == typeof(int))
        {
            return (current as int? ?? 0) + 7;
        }

        if (underlying.IsEnum)
        {
            return Enum.GetValues(underlying).Cast<object>().First(v => !v.Equals(current));
        }

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
        {
            var element = underlying.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(underlying)!;
            list.Add(element == typeof(string) ? path : Fill(element, path));
            return list;
        }

        if (underlying.IsClass)
        {
            return Fill(underlying, path);
        }

        throw new NotSupportedException($"Extend the filler for {underlying} ({path}).");
    }
}
