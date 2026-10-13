using System;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

/// <summary>
///    Marks a property of a job's Job Parameters as a Logged Parameter.
///    The library writes Job Parameters into none of its log lines and traces, except the properties marked with this
///    attribute: those appear in the log lines about the job and as tags on the job's trace.
///    Mark the id of the record a job works on, never a secret.
///    A marked property that holds an object, a struct, or a collection or dictionary of them shows only the Logged Parameters of those objects and structs.
///    The Job Parameters are still stored with the job and still handed to it when it runs.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LoggedParameterAttribute : Attribute;
