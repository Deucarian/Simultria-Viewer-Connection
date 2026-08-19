using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.CommandRouting;

namespace Deucarian.SimultriaViewerConnection
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

            return handler(
                context.Application,
                payload,
                context.Command.Metadata,
                cancellationToken);
        }
    }
}
