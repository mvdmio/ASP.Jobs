using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace mvdmio.ASP.Jobs.Utils;

/// <summary>
///    Provides JSON serialization options for job parameters.
///    These options are configured to include internal properties, which is necessary
///    because job parameters may have internal properties that are modified during
///    the job lifecycle (e.g., in <see cref="Job{TProperties}.OnJobScheduledAsync"/>)
///    and need to be persisted to storage.
/// </summary>
internal static class JobParameterJsonOptions
{
   /// <summary>
   ///    Gets the JSON serializer options configured for job parameter serialization.
   ///    These options include support for internal properties.
   /// </summary>
   public static JsonSerializerOptions Options { get; } = CreateOptions();

   private static JsonSerializerOptions CreateOptions()
   {
      return new JsonSerializerOptions {
         TypeInfoResolver = new DefaultJsonTypeInfoResolver {
            Modifiers = { IncludeInternalPropertiesModifier }
         }
      };
   }

   /// <summary>
   ///    JSON type info modifier that includes internal properties in serialization.
   ///    By default, System.Text.Json only serializes public properties. This modifier
   ///    adds internal properties (those with assembly-level visibility) to enable
   ///    serialization of job parameters that use internal properties.
   /// </summary>
   private static void IncludeInternalPropertiesModifier(JsonTypeInfo typeInfo)
   {
      if (typeInfo.Kind != JsonTypeInfoKind.Object)
         return;

      foreach (var property in typeInfo.Type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic))
      {
         // Skip properties without both getter and setter
         if (property.GetMethod is null || property.SetMethod is null)
            continue;

         // Only include internal properties (not private)
         // IsAssembly means the method has assembly-level visibility (internal in C#)
         if (!property.GetMethod.IsAssembly && !property.SetMethod.IsAssembly)
            continue;

         var jsonPropertyInfo = typeInfo.CreateJsonPropertyInfo(property.PropertyType, property.Name);
         jsonPropertyInfo.Get = property.GetValue;
         jsonPropertyInfo.Set = property.SetValue;

         typeInfo.Properties.Add(jsonPropertyInfo);
      }
   }
}
