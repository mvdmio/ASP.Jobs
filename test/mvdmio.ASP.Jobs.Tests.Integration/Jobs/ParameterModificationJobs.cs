namespace mvdmio.ASP.Jobs.Tests.Integration.Jobs;

/// <summary>
///    A test job that modifies its parameters during OnJobScheduledAsync.
///    Used to verify that parameter modifications are persisted correctly with PostgreSQL storage.
/// </summary>
public class ParameterModifyingJob : Job<ParameterModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.ModifiedInOnJobScheduled = "modified_during_scheduling";
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.ValueSeenInExecute = parameters.ModifiedInOnJobScheduled;
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? OriginalValue { get; set; }
      public string? ModifiedInOnJobScheduled { get; set; }
      public string? ValueSeenInExecute { get; set; }
   }
}

/// <summary>
///    A test job that modifies an internal property during OnJobScheduledAsync.
/// </summary>
public class InternalPropertyModifyingJob : Job<InternalPropertyModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.InternalModifiedValue = "modified_during_scheduling";
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? PublicValue { get; set; }
      internal string? InternalModifiedValue { get; set; }
   }
}

/// <summary>
///    A test job that modifies a property with a private setter during OnJobScheduledAsync.
/// </summary>
public class PrivateSetterModifyingJob : Job<PrivateSetterModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters.SetModifiedValue("modified_during_scheduling");
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public Parameters() { }

      public Parameters(string publicValue)
      {
         PublicValue = publicValue;
      }

      public string? PublicValue { get; set; }
      public string? ModifiedValue { get; private set; }

      public void SetModifiedValue(string value) => ModifiedValue = value;
   }
}

/// <summary>
///    A test job that modifies a public field during OnJobScheduledAsync.
/// </summary>
public class FieldModifyingJob : Job<FieldModifyingJob.Parameters>
{
   public override Task OnJobScheduledAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      parameters._modifiedField = "modified_during_scheduling";
      return Task.CompletedTask;
   }

   public override Task ExecuteAsync(Parameters parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   public class Parameters
   {
      public string? PublicValue { get; set; }
      public string? _modifiedField;
   }
}
