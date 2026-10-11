using System;

namespace mvdmio.ASP.Jobs.Internals.Storage;

/// <summary>
///    The name of a CRON row: <c>cron_&lt;ShortName&gt;_&lt;expression&gt;</c>. The class's short name is what ties a CRON row to
///    its Job class, because the assembly-qualified type stored beside it changes with every assembly version.
/// </summary>
internal static class CronJobNames
{
   /// <summary>
   ///    Builds the name of the CRON row of <paramref name="jobType"/> for a CRON expression with its spaces removed.
   /// </summary>
   public static string For(Type jobType, string normalizedCronExpression)
   {
      return $"{PrefixFor(jobType)}{normalizedCronExpression}";
   }

   /// <summary>
   ///    The start of every CRON row name of <paramref name="jobType"/>, up to the expression. Every row name of the class
   ///    starts with it, but so do the row names of classes whose name starts with this class's name and an underscore;
   ///    <see cref="IsOfClass"/> tells them apart.
   /// </summary>
   public static string PrefixFor(Type jobType)
   {
      return $"cron_{jobType.Name}_";
   }

   /// <summary>
   ///    Holds when <paramref name="jobName"/> is a CRON row name of <paramref name="jobType"/>. What follows the prefix
   ///    must hold no underscore: a normalised CRON expression never contains one, so the row
   ///    <c>cron_Foo_Bar_*****</c> (class <c>Foo_Bar</c>) is not a row of class <c>Foo</c>.
   /// </summary>
   public static bool IsOfClass(string jobName, Type jobType)
   {
      var prefix = PrefixFor(jobType);

      return jobName.StartsWith(prefix, StringComparison.Ordinal) && jobName.IndexOf('_', prefix.Length) < 0;
   }
}
