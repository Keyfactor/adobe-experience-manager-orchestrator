
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

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
