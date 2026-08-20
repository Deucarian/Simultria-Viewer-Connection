using System.Collections.Generic;
using System.Reflection;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Tests
{
    public sealed class SimultriaBuildEnvironmentRoutingPolicyTests
    {
        private SimultriaBuildEnvironmentRoutingPolicy policy;

        [SetUp]
        public void SetUp()
        {
            policy = ScriptableObject.CreateInstance<SimultriaBuildEnvironmentRoutingPolicy>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(policy);
        }

        [Test]
        public void ResolvesConfiguredCanonicalEnvironment()
        {
            SetRules(new RuleDefinition("test-build", SimultriaEnvironmentIds.Testing));

            bool resolved = policy.TryResolve(
                "test-build",
                out ApiEnvironmentId environmentId,
                out string error);

            Assert.That(resolved, Is.True, error);
            Assert.That(environmentId, Is.EqualTo(SimultriaEnvironmentIds.Testing));
        }

        [Test]
        public void RejectsUnknownBuildMetadata()
        {
            SetRules(new RuleDefinition("known-build", SimultriaEnvironmentIds.Development));

            bool resolved = policy.TryResolve(
                "unknown-build",
                out _,
                out string error);

            Assert.That(resolved, Is.False);
            Assert.That(error, Does.Contain("No Simultria environment rule"));
        }

        [Test]
        public void RejectsAmbiguousBuildMetadata()
        {
            SetRules(
                new RuleDefinition("same-build", SimultriaEnvironmentIds.Development),
                new RuleDefinition("same-build", SimultriaEnvironmentIds.Testing));

            bool resolved = policy.TryResolve(
                "same-build",
                out _,
                out string error);

            Assert.That(resolved, Is.False);
            Assert.That(error, Does.Contain("more than one"));
        }

        [Test]
        public void RejectsNonCanonicalEnvironment()
        {
            SetRules(new RuleDefinition(
                "custom-build",
                new ApiEnvironmentId("simultria.custom")));

            bool resolved = policy.TryResolve(
                "custom-build",
                out _,
                out string error);

            Assert.That(resolved, Is.False);
            Assert.That(error, Does.Contain("canonical"));
        }

        private void SetRules(params RuleDefinition[] definitions)
        {
            var rules = new List<SimultriaBuildEnvironmentRoutingPolicy.Rule>();
            foreach (RuleDefinition definition in definitions)
            {
                rules.Add(new SimultriaBuildEnvironmentRoutingPolicy.Rule
                {
                    BuildMetadata = definition.BuildMetadata,
                    EnvironmentId = definition.EnvironmentId
                });
            }

            typeof(SimultriaBuildEnvironmentRoutingPolicy)
                .GetField("rules", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(policy, rules);
        }

        private readonly struct RuleDefinition
        {
            public RuleDefinition(string buildMetadata, ApiEnvironmentId environmentId)
            {
                BuildMetadata = buildMetadata;
                EnvironmentId = environmentId;
            }

            public string BuildMetadata { get; }
            public ApiEnvironmentId EnvironmentId { get; }
        }
    }
}
