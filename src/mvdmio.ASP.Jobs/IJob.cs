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

   /// <summary>
   ///    Method called after a failed execution is rescheduled as a retry, before the retry is written to storage.
   ///    Use this method for logging or metrics on a per-attempt basis.
   /// </summary>
   internal Task OnJobRetryAsync(object parameters, Exception exception, RetryContext retryContext, CancellationToken cancellationToken);

   /// <summary>
   ///    The Job's Retry Policy, declaring which exceptions should be retried, how many times, and with what delay.
   ///    An empty policy (the default) means the job is never retried.
   /// </summary>
   internal RetryPolicy RetryPolicy { get; }
}

/// <summary>
///    Abstract base class for a job with typed properties.
/// </summary>
/// <typeparam name="TProperties">The type of properties passed to the job.</typeparam>
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

   async Task IJob.OnJobRetryAsync(object properties, Exception exception, RetryContext retryContext, CancellationToken cancellationToken)
   {
      if (properties is TProperties typedProperties)
         await OnJobRetryAsync(typedProperties, exception, retryContext, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }

   RetryPolicy IJob.RetryPolicy => RetryPolicy;

   /// <summary>
   ///    Method called when the job is scheduled.
   ///    Use this method for any preparation work that needs to be done immediately when the job is created.
   ///    This method may modify the properties object.
   /// </summary>
   /// <param name="parameters">The job parameters.</param>
   /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public virtual Task OnJobScheduledAsync(TProperties parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   /// <summary>
   ///    Method called to execute the job.
   ///    Use this method for all the work that needs to be done by the job.
   /// </summary>
   /// <param name="parameters">The job parameters.</param>
   /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public abstract Task ExecuteAsync(TProperties parameters, CancellationToken cancellationToken);

   /// <summary>
   ///    Method called when the job is successfully executed.
   ///    Use this method for any work that needs to be done immediately after the job has been executed.
   /// </summary>
   /// <param name="parameters">The job parameters.</param>
   /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public virtual Task OnJobExecutedAsync(TProperties parameters, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   /// <summary>
   ///    Method called when the job execution throws an exception.
   ///    Use this method for any work that needs to be done when a job fails.
   /// </summary>
   /// <param name="parameters">The job parameters.</param>
   /// <param name="exception">The exception that was thrown during job execution.</param>
   /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public virtual Task OnJobFailedAsync(TProperties parameters, Exception exception, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   /// <summary>
   ///    Method called after a failed execution is rescheduled as a retry, before the retry is written to storage.
   ///    Use this method for logging or metrics on a per-attempt basis. A throw from this method is logged and never
   ///    alters whether the retry is written.
   /// </summary>
   /// <param name="parameters">The job parameters.</param>
   /// <param name="exception">The exception that caused this retry.</param>
   /// <param name="retryContext">The attempt number, retry budget, and next attempt time for this retry.</param>
   /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   public virtual Task OnJobRetryAsync(TProperties parameters, Exception exception, RetryContext retryContext, CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   /// <summary>
   ///    The Job's Retry Policy, declaring which exceptions should be retried, how many times, and with what delay.
   ///    Defaults to an empty policy: without an override, a job behaves exactly as it would without a Retry Policy.
   /// </summary>
   public virtual RetryPolicy RetryPolicy => new();
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