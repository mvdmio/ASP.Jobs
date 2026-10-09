using System;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Options for configuring how a job is scheduled.
/// </summary>
public class JobScheduleOptions
{
   /// <summary>
   ///    Gets or sets the unique name for this scheduled job. If a job with the same name that has not already started exists, it will be replaced.
   ///    Defaults to a new GUID.
   /// </summary>
   public string JobName { get; set; } =
#if NET9_0_OR_GREATER
      Guid.CreateVersion7().ToString("N");
#else
      Guid.NewGuid().ToString("N");
#endif

   /// <summary>
   ///    Gets or sets the group name for this job. Jobs in the same group are executed sequentially in the order they were scheduled, preventing concurrent execution within the group.
   ///    Both the in-memory and the PostgreSQL storage enforce the group. With PostgreSQL storage, a group spans every Worker Instance that shares the application name.
   ///    If a Worker Instance stops without shutting down cleanly while it runs a grouped job, the group stays blocked until another instance resets the stopped instance's claims as expired.
   ///    That happens at the first cleanup pass more than five minutes after the stopped instance's last heartbeat; cleanup runs every minute.
   ///    Set to null to not use a group. Defaults to null.
   /// </summary>
   public string? Group { get; init; }
}