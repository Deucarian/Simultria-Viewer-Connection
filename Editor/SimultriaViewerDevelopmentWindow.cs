using System;
using System.Collections.Generic;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.Editor;
using Deucarian.API.Core;
using Deucarian.Authentication;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    public sealed partial class SimultriaViewerDevelopmentWindow : EditorWindow
    {
        internal static Vector2 CompactMinimumSize =>
            new Vector2(420f, 340f);

        private Vector2 scroll;
        private string message = string.Empty;
        private DeucarianEditorStatus messageStatus = DeucarianEditorStatus.Info;
        private bool showAdvanced;
        private CancellationTokenSource operationCancellation;
    }
}
