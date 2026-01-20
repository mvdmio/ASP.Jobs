using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Interface for a job.
/// </summary>
[PublicAPI]
public interface IJob
{
   /// <summary>
   ///    Method called when the job is scheduled.
   ///    Use this method for any preparation work that needs to be done immediately when the job is created.
   ///    This method may modify the properties object.
   /// </summary>
   internal Task OnJobScheduledAsync(object properties, CancellationToken cancellationToken);

   /// <summary>
   ///    Method called to execute the job.
   ///    Use this method for all the work that needs to be done by the job.
   /// </summary>
   internal Task ExecuteAsync(object properties, CancellationToken cancellationToken);

   /// <summary>
   ///    Method called when the job is successfully executed.
   ///    Use this method for any work that needs to be done immediately after the job has been executed.
   /// </summary>
   internal Task OnJobExecutedAsync(object properties, CancellationToken cancellationToken);

   /// <summary>
   ///    Method called when the job execution throws an exception.
   ///    Use this method for any work that needs to be done when a job fails.
   /// </summary>
   internal Task OnJobFailedAsync(object parameters, Exception exception, CancellationToken cancellationToken);
}

/// <summary>
///    Interface for a job.
/// </summary>
[PublicAPI]
public abstract class Job<TProperties> : IJob
   where TProperties : class
{
   async Task IJob.OnJobScheduledAsync(object properties, CancellationToken cancellationToken)
   {
      if (properties is TProperties typedProperties)
         await OnJobScheduledAsync(typedProperties, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }

   async Task IJob.ExecuteAsync(object properties, CancellationToken cancellationToken)
   {
      if (properties is TProperties typedProperties)
         await ExecuteAsync(typedProperties, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }

   async Task IJob.OnJobExecutedAsync(object properties, CancellationToken cancellationToken)
   {
      if (properties is TProperties typedProperties)
         await OnJobExecutedAsync(typedProperties, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }

   async Task IJob.OnJobFailedAsync(object properties, Exception exception, CancellationToken cancellationToken)
   {
      if (properties is TProperties typedProperties)
         await OnJobFailedAsync(typedProperties, exception, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }

   /// <summary>
   ///    Method called when the job is scheduled.
   ///    Use this method for any preparation work that needs to be done immediately when the job is created.
   ///    This method may modify the properties object.
   /// </summary>
   /// <returns>
   ///   Modified properties object.
   /// </returns>
   public virtual Task OnJobScheduledAsync(TProperties parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   /// <summary>
   ///    Method called to execute the job.
   ///    Use this method for all the work that needs to be done by the job.
   /// </summary>
   public abstract Task ExecuteAsync(TProperties parameters, CancellationToken cancellationToken);

   /// <summary>
   ///    Method called when the job is successfully executed.
   ///    Use this method for any work that needs to be done immediately after the job has been executed.
   /// </summary>
   public virtual Task OnJobExecutedAsync(TProperties parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   /// <summary>
   ///    Method called when the job execution throws an exception.
   ///    Use this method for any work that needs to be done when a job fails.
   /// </summary>
   public virtual Task OnJobFailedAsync(TProperties parameters, Exception exception, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }
}

/// <summary>
///   Job that does not require any parameters. Uses <see cref="EmptyJobParameters"/> as the parameters type.
/// </summary>
[PublicAPI]
public abstract class Job : Job<EmptyJobParameters>;

/// <summary>
///   Parameters type for a job that does not require any parameters.
/// </summary>
[PublicAPI]
public class EmptyJobParameters;