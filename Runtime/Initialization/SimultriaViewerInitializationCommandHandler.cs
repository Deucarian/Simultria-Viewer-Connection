using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.CommandRouting;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
    public delegate Task<CommandResult> SimultriaViewerInitializationDelegate<TApplicationContext>(
        TApplicationContext application,
        SimultriaViewerInitializationPayload payload,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Canonical Command Routing handler adapter. Viewer products inject only
    /// their typed application mapping; this package owns parsing and safety.
    /// </summary>
    public sealed class SimultriaViewerInitializationCommandHandler<TApplicationContext> :
        ICommandHandler<TApplicationContext>
    {
        private static readonly IReadOnlyList<string> Names =
            new[] { SimultriaViewerInitializationCommand.CommandName };

        private readonly SimultriaViewerInitializationDelegate<TApplicationContext> handler;

        public SimultriaViewerInitializationCommandHandler(
            SimultriaViewerInitializationDelegate<TApplicationContext> handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public IReadOnlyList<string> CommandNames => Names;

        public Task<CommandResult> HandleAsync(
            CommandExecutionContext<TApplicationContext> context,
            CancellationToken cancellationToken)
        {
            if (!context.Command.TryReadPayload(
                    out SimultriaViewerInitializationPayload payload,
                    out string error) ||
                payload == null ||
                !payload.IsValid(out error))
            {
                return Task.FromResult(
                    CommandResult.Failure("invalid_payload", error));
            }

            if (SimultriaViewerRuntimeEnvironment.TryGetCurrent(
                    out SimultriaViewerEnvironmentResolution resolution))
            {
                string authoritativeEnvironment =
                    resolution.EnvironmentId.Value;
                if (!string.IsNullOrWhiteSpace(payload.EnvironmentId) &&
                    !string.Equals(
                        payload.EnvironmentId.Trim(),
                        authoritativeEnvironment,
                        StringComparison.Ordinal))
                {
                    return Task.FromResult(CommandResult.Failure(
                        "environment_mismatch",
                        "The initialization environment does not match the " +
                        "environment assigned to this Unity build."));
                }

                payload.EnvironmentId = authoritativeEnvironment;
            }
            else if (!Application.isEditor)
            {
                return Task.FromResult(CommandResult.Failure(
                    "environment_unresolved",
                    "The Unity build environment has not been resolved."));
            }

            return handler(
                context.Application,
                payload,
                context.Command.Metadata,
                cancellationToken);
        }
    }
}
