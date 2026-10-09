namespace mvdmio.ASP.Jobs.Internals.Storage.Postgres;

/// <summary>
///    Names the job storage's own connection pool, so its connections can be told apart from the host app's pool.
/// </summary>
internal static class JobsPoolName
{
   private const string Suffix = ".Jobs";

   /// <summary>
   ///    Returns the entry assembly's name plus <c>.Jobs</c>, or the application name plus <c>.Jobs</c> when the entry assembly name is null or empty.
   /// </summary>
   /// <param name="entryAssemblyName">The simple name of the program's entry assembly, or <see langword="null" /> when there is none.</param>
   /// <param name="applicationName">The application name given to <c>UsePostgresStorage</c>.</param>
   public static string Create(string? entryAssemblyName, string applicationName)
   {
      var baseName = string.IsNullOrEmpty(entryAssemblyName) ? applicationName : entryAssemblyName;
      return baseName + Suffix;
   }
}
