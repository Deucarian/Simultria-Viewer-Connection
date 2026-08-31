using System.Linq;
using Deucarian.Editor;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class ControlCenterRegistrationTests
    {
        private const string PackageId =
            "com.deucarian.simultria-viewer-integration";

        [Test]
        public void PackageRegistersStableToolAndCard()
        {
            Assert.That(
                DeucarianToolRegistry.TryGet(
                    DeucarianToolIds.SimultriaViewerDevelopment,
                    out DeucarianToolDescriptor tool),
                Is.True);
            Assert.That(tool.OwningPackage, Is.EqualTo(PackageId));

            DeucarianControlCenterSnapshot snapshot =
                DeucarianControlCenterSnapshotBuilder.Capture(true);
            Assert.That(
                snapshot.Cards.Any(
                    card => card.OwningPackage == PackageId),
                Is.True);
        }
        [Test]
        public void CardDoesNotReportSuccessForIncompleteViewerReadiness()
        {
            var state = new SimultriaViewerControlCenterSnapshot(
                true,
                "Project default",
                true,
                true,
                "Development",
                "Explicit environment simultria.development",
                false,
                false,
                "Unauthenticated",
                false,
                false,
                true);

            DeucarianControlCenterCard card =
                SimultriaViewerCardProvider.CreateCard(state);

            Assert.That(card.Status, Is.EqualTo(DeucarianControlCenterStatus.Error));
            Assert.That(card.StatusText, Does.Contain("Project and model"));
            Assert.That(string.Join(" ", card.Details), Does.Not.Contain("Bearer"));
            Assert.That(string.Join(" ", card.Details), Does.Not.Contain("https://"));
        }
    }
}
