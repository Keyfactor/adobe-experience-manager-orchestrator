using System;

namespace Keyfactor.Extensions.Orchestrator.AEMCM
{
    /// <summary>Job type names, matching the manifest capability suffixes.</summary>
    public static class JobTypes
    {
        public const string Inventory = "Inventory";
        public const string Management = "Management";
        public const string Discovery = "Discovery";
    }

    /// <summary>Marks a job class with its job type (documentation / reflection convenience).</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class JobAttribute : Attribute
    {
        public string JobType { get; }

        public JobAttribute(string jobType) => JobType = jobType;
    }
}
