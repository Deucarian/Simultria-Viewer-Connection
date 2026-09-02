using System;
using System.Collections.Generic;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    internal static class SimultriaViewerBuildTestFactory
    {
        internal static ApiConnectionSettings CreateConnection(
            IList<UnityEngine.Object> ownedObjects,
            params ApiEnvironmentId[] unresolved)
        {
            if (ownedObjects == null)
            {
                throw new ArgumentNullException(nameof(ownedObjects));
            }

            ApiServiceDefinition definition =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            if (definition == null)
            {
                throw new InvalidOperationException(
                    "The Simultria service definition is unavailable.");
            }

            if (!definition.TryGetEnvironmentDescriptors(
                    out IReadOnlyList<ApiEnvironmentDescriptor> descriptors,
                    out string error))
            {
                throw new InvalidOperationException(error);
            }

            var profiles = new List<ApiEnvironmentProfile>();
            for (int index = 0; index < descriptors.Count; index++)
            {
                ApiEnvironmentDescriptor descriptor = descriptors[index];
                ApiEnvironmentProfile profile =
                    ScriptableObject.CreateInstance<ApiEnvironmentProfile>();
                profile.EnvironmentId = descriptor.EnvironmentId.Value;
                profile.DisplayName = descriptor.DisplayName;
                profile.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = Contains(unresolved, descriptor.EnvironmentId)
                        ? string.Empty
                        : "https://" + SafeHostPart(
                            descriptor.EnvironmentId.Value) + ".invalid"
                });
                profiles.Add(profile);
                ownedObjects.Add(profile);
            }

            ApiConnectionSettings connection =
                ApiConnectionSettings.CreateTransient(profiles, definition);
            ownedObjects.Add(connection);
            return connection;
        }

        internal static SimultriaViewerBuildConfiguration CreateConfiguration(
            IList<UnityEngine.Object> ownedObjects,
            ApiConnectionSettings connection)
        {
            SimultriaViewerBuildConfiguration configuration =
                ScriptableObject.CreateInstance<
                    SimultriaViewerBuildConfiguration>();
            configuration.ConnectionSettings = connection;
            configuration.BuildDirectoryEnvironmentId =
                SimultriaEnvironmentIds.Development;
            configuration.Product = "viewer_product";
            ownedObjects.Add(configuration);
            return configuration;
        }

        internal static SimultriaViewerDevelopmentContext CreateContext(
            IList<UnityEngine.Object> ownedObjects,
            ApiConnectionSettings connection,
            ApiEnvironmentId environmentId)
        {
            SimultriaViewerDevelopmentContext context =
                ScriptableObject.CreateInstance<
                    SimultriaViewerDevelopmentContext>();
            context.ConnectionSettingsReference = connection;
            context.EnvironmentResolutionMode =
                SimultriaViewerEnvironmentResolutionMode.Manual;
            context.EnvironmentId = environmentId;
            context.ProjectId = 12;
            context.ModelId = 34;
            context.ModelVersionId = 0;
            ownedObjects.Add(context);
            return context;
        }

        internal static void DestroyAll(IList<UnityEngine.Object> objects)
        {
            if (objects == null)
            {
                return;
            }

            for (int index = objects.Count - 1; index >= 0; index--)
            {
                if (objects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[index]);
                }
            }

            objects.Clear();
        }

        internal static string CreateSafeContextJson(
            ApiEnvironmentId environmentId)
        {
            var payload = new SimultriaViewerInitializationPayload
            {
                Revision = 1,
                EnvironmentId = environmentId.Value,
                ProjectId = 12,
                ModelId = 34,
                ForceShowLoadedModelObjects = true
            };
            return SimultriaViewerInitializationCommand.Serialize(
                SimultriaViewerInitializationCommand.Create(payload));
        }

        private static bool Contains(
            IReadOnlyList<ApiEnvironmentId> values,
            ApiEnvironmentId candidate)
        {
            if (values == null)
            {
                return false;
            }

            for (int index = 0; index < values.Count; index++)
            {
                if (values[index] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private static string SafeHostPart(string value)
        {
            return (value ?? "environment")
                .Replace("simultria.", string.Empty)
                .Replace('_', '-')
                .Replace(' ', '-');
        }
    }
}
