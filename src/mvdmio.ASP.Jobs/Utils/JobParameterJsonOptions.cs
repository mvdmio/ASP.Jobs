using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace mvdmio.ASP.Jobs.Utils;

/// <summary>
///    Provides JSON serialization options for job parameters.
///    These options are configured to include all properties and fields regardless of
///    their access level. This is necessary because job parameters may have non-public
///    members that are modified during the job lifecycle (e.g., in 
///    <see cref="Job{TProperties}.OnJobScheduledAsync"/>) and need to be persisted to storage.
/// </summary>
internal static class JobParameterJsonOptions
{
   /// <summary>
   ///    Gets the JSON serializer options configured for job parameter serialization.
   ///    These options include support for all properties and fields regardless of access level.
   /// </summary>
   public static JsonSerializerOptions Options { get; } = CreateOptions();

   private static JsonSerializerOptions CreateOptions()
   {
      return new JsonSerializerOptions {
         TypeInfoResolver = new DefaultJsonTypeInfoResolver {
            Modifiers = { IncludeAllMembersModifier }
         }
      };
   }

   /// <summary>
   ///    JSON type info modifier that includes all properties and fields in serialization,
   ///    regardless of their access level. By default, System.Text.Json only serializes
   ///    public properties with public getters and setters. This modifier adds all non-public
   ///    properties (internal, protected, private) and all fields to enable complete
   ///    serialization of job parameters.
   /// </summary>
   private static void IncludeAllMembersModifier(JsonTypeInfo typeInfo)
   {
      if (typeInfo.Kind != JsonTypeInfoKind.Object)
         return;

      var existingPropertyNames = new HashSet<string>(
         typeInfo.Properties.Select(p => p.Name),
         StringComparer.OrdinalIgnoreCase
      );

      // Add all non-public properties (internal, protected, private)
      foreach (var property in typeInfo.Type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic))
      {
         AddProperty(typeInfo, existingPropertyNames, property);
      }

      // Handle public properties with non-public setters (e.g., public get, private set)
      foreach (var property in typeInfo.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
      {
         // Check if this property has a non-public setter that wasn't already included
         if (property.SetMethod is not null && !property.SetMethod.IsPublic)
         {
            // The property is already in the list (public getter), but we need to ensure
            // the setter is accessible. Find and update the existing property info.
            var existingProp = typeInfo.Properties.FirstOrDefault(p => 
               p.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
            
            if (existingProp is not null && existingProp.Set is null)
            {
               existingProp.Set = property.SetValue;
            }
         }
      }

      // Add all fields (public and non-public)
      foreach (var field in typeInfo.Type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
      {
         // Skip compiler-generated backing fields (they start with '<' and contain '>k__BackingField')
         if (field.Name.StartsWith("<") && field.Name.Contains(">k__BackingField"))
            continue;

         AddField(typeInfo, existingPropertyNames, field);
      }
   }

   private static void AddProperty(JsonTypeInfo typeInfo, HashSet<string> existingPropertyNames, PropertyInfo property)
   {
      // Skip if already exists
      if (existingPropertyNames.Contains(property.Name))
         return;

      // Skip properties without both getter and setter
      if (property.GetMethod is null || property.SetMethod is null)
         return;

      var jsonPropertyInfo = typeInfo.CreateJsonPropertyInfo(property.PropertyType, property.Name);
      jsonPropertyInfo.Get = property.GetValue;
      jsonPropertyInfo.Set = property.SetValue;

      typeInfo.Properties.Add(jsonPropertyInfo);
      existingPropertyNames.Add(property.Name);
   }

   private static void AddField(JsonTypeInfo typeInfo, HashSet<string> existingPropertyNames, FieldInfo field)
   {
      // Skip if already exists (unlikely for fields, but check anyway)
      if (existingPropertyNames.Contains(field.Name))
         return;

      var jsonPropertyInfo = typeInfo.CreateJsonPropertyInfo(field.FieldType, field.Name);
      jsonPropertyInfo.Get = field.GetValue;
      jsonPropertyInfo.Set = field.SetValue;

      typeInfo.Properties.Add(jsonPropertyInfo);
      existingPropertyNames.Add(field.Name);
   }
}
