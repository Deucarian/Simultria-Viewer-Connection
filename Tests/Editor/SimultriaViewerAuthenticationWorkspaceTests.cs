using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Authentication;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerAuthenticationWorkspaceTests
    {
        private readonly List<UnityEngine.Object> owned =
            new List<UnityEngine.Object>();
        private IDisposable hostSuspension;

        [SetUp]
        public void SetUp()
        {
            hostSuspension =
                Editor.SimultriaViewerEditorAuthenticationHost.SuspendForTests();
        }

        [TearDown]
        public void TearDown()
        {
            hostSuspension?.Dispose();
            hostSuspension = null;
            foreach (UnityEngine.Object item in owned)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }

            owned.Clear();
        }

        [Test]
        public void DevelopmentContextRequiresExplicitConnectionSettings()
        {
            SimultriaViewerDevelopmentContext context =
                Own(ScriptableObject.CreateInstance<
                    SimultriaViewerDevelopmentContext>());
            AuthenticationSession session =
                AuthenticationSession.CreateTransient();
            try
            {
                bool registered =
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        context,
                        session,
                        out IDisposable registration,
                        out _,
                        out string error);

                registration?.Dispose();
                Assert.That(registered, Is.False);
                Assert.That(error, Does.Contain("connection settings"));
            }
            finally
            {
                _ = session.ClearAsync(CancellationToken.None);
            }
        }

        [Test]
        public void ExplicitSettingsRegisterTheStableViewerTarget()
        {
            ApiConnectionSettings settings = CreateSettings();
            AuthenticationSession session =
                AuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                bool registered =
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        settings,
                        SimultriaEnvironmentIds.Development,
                        session,
                        out registration,
                        out ApiEnvironmentStatus status,
                        out string error);

                Assert.That(registered, Is.True, error);
                Assert.That(status.IsResolved, Is.True, status.Message);
                Assert.That(
                    AuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication.DefaultTargetId,
                        out AuthenticationTarget target),
                    Is.True);
                Assert.That(target.Session, Is.SameAs(session));
            }
            finally
            {
                registration?.Dispose();
                _ = session.ClearAsync(CancellationToken.None);
            }
        }

        [Test]
        public async Task ConnectionFailureDoesNotClearAuthentication()
        {
            AuthenticationSession session =
                AuthenticationSession.CreateTransient();
            await session.ReplaceAccessTokenAsync("preserved-token");
            var provider = new SimultriaViewerRuntimeConnectionProvider(
                null,
                SimultriaEnvironmentIds.Development,
                session);

            bool created = provider.TryCreate(out _, out _);

            Assert.That(created, Is.False);
            Assert.That(session.AccessToken, Is.EqualTo("preserved-token"));
        }

        [Test]
        public void CompiledPackageContainsNoRemovedProfileContract()
        {
            string packageRoot =
                "Packages/com.deucarian.simultria-viewer-integration";
            string removedType = "Simultria" + "Api" + "Profile";
            string removedField = "api" + "Profile" + "Reference";
            string removedGuid =
                "98c59614849544b49d34" + "f059afc91fb5";

            foreach (string file in Directory.GetFiles(
                packageRoot,
                "*",
                SearchOption.AllDirectories))
            {
                if (file.EndsWith("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
                    (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                     !file.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                     !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string content = File.ReadAllText(file);
                Assert.That(content, Does.Not.Contain(removedType), file);
                Assert.That(content, Does.Not.Contain(removedField), file);
                Assert.That(content, Does.Not.Contain(removedGuid), file);
            }
        }

        private ApiConnectionSettings CreateSettings()
        {
            ApiServiceDefinition definition =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            Assert.That(definition, Is.Not.Null);
            Assert.That(
                definition.TryGetEnvironmentDescriptors(
                    out IReadOnlyList<ApiEnvironmentDescriptor> descriptors,
                    out string error),
                Is.True,
                error);

            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentDescriptor descriptor in descriptors)
            {
                ApiEnvironmentProfile environment = Own(
                    ScriptableObject.CreateInstance<ApiEnvironmentProfile>());
                environment.EnvironmentId = descriptor.EnvironmentId.Value;
                environment.DisplayName = descriptor.DisplayName;
                environment.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = descriptor.EnvironmentId ==
                        SimultriaEnvironmentIds.Development
                        ? "https://simultria-viewer.invalid"
                        : string.Empty
                });
                environments.Add(environment);
            }

            return Own(ApiConnectionSettings.CreateTransient(
                environments,
                definition));
        }

        private T Own<T>(T item) where T : UnityEngine.Object
        {
            owned.Add(item);
            return item;
        }
    }
}
