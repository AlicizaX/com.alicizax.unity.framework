using System;
using System.Collections.Generic;
using AlicizaX.Editor.UIToolkit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX
{

    [CustomEditor(typeof(GameObjectPoolComponent))]
    public sealed class GameObjectPoolInspector : AlicizaUIEditor
    {
        private const int InitialSnapshotBufferSize = 32;
        private const long RefreshIntervalMs = 250;
        private const double PointerHoldTimeoutSeconds = 3d;

        private readonly HashSet<string> _expandedPools = new HashSet<string>(StringComparer.Ordinal);
        private GameObjectPoolSnapshot[] _snapshotBuffer = new GameObjectPoolSnapshot[InitialSnapshotBufferSize];
        private VisualElement _runtimeRoot;
        private double _pointerHoldDeadline;

        protected override void BuildInspector(VisualElement root)
        {
            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            panel.Body.style.paddingBottom = 0f;
            root.Add(panel);

            _runtimeRoot = new VisualElement { name = "game-object-pool-runtime" };
            _runtimeRoot.RegisterCallback<PointerDownEvent>(OnRuntimePointerDown, TrickleDown.TrickleDown);
            _runtimeRoot.RegisterCallback<PointerUpEvent>(OnRuntimePointerUp, TrickleDown.TrickleDown);
            panel.Body.Add(_runtimeRoot);

            RefreshRuntime();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntime, RefreshIntervalMs);
        }

        private void OnRuntimePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _pointerHoldDeadline = EditorApplication.timeSinceStartup + PointerHoldTimeoutSeconds;
        }

        private void OnRuntimePointerUp(PointerUpEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _pointerHoldDeadline = 0d;
        }

        private void RefreshRuntime()
        {
            if (_runtimeRoot == null || EditorApplication.timeSinceStartup < _pointerHoldDeadline)
            {
                return;
            }

            _runtimeRoot.Clear();

            if (!EditorApplication.isPlaying)
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "Enter Play Mode to inspect the runtime pool."));
                return;
            }

            if (!AppServices.HasWorld ||
                !AppServices.App.TryGet<IGameObjectPoolDebugService>(out IGameObjectPoolDebugService debugService))
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "GameObject pool service is not initialized."));
                return;
            }

            GameObjectPoolSummarySnapshot summary = debugService.GetDebugSummary();
            BuildSummary(summary);

            if (!summary.IsReady)
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateRuntimePlaceholder(
                    "Pool manager is not ready."));
                return;
            }

            AlicizaSection poolsSection = AlicizaEditorUI.CreateStaticSection("Pools");
            _runtimeRoot.Add(poolsSection);

            int snapshotCount = GetDebugSnapshots(debugService);
            if (snapshotCount == 0)
            {
                poolsSection.Body.Add(AlicizaEditorUI.CreateEmptyState("No pooled runtime instances exist yet."));
                return;
            }

            for (int i = 0; i < snapshotCount; i++)
            {
                BuildPoolCard(poolsSection.Body, debugService, _snapshotBuffer[i]);
            }
        }

        private void BuildSummary(in GameObjectPoolSummarySnapshot summary)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Runtime Summary");
            _runtimeRoot.Add(section);

            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Ready",
                summary.IsReady ? "Yes" : "No",
                summary.IsReady ? AlicizaValueTone.Default : AlicizaValueTone.Warning);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Pools",
                summary.PoolCount.ToString(),
                summary.PoolCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Loaded Prefabs",
                summary.LoadedPrefabCount.ToString(),
                summary.LoadedPrefabCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Instances",
                summary.TotalInstanceCount.ToString(),
                summary.TotalInstanceCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Active",
                summary.ActiveInstanceCount.ToString(),
                summary.ActiveInstanceCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Inactive",
                summary.InactiveInstanceCount.ToString(),
                summary.InactiveInstanceCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Pending Maintenance",
                summary.PendingMaintenanceCount.ToString(),
                summary.PendingMaintenanceCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
        }

        private void BuildPoolCard(
            VisualElement parent,
            IGameObjectPoolDebugService debugService,
            GameObjectPoolSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            string poolKey = GetPoolKey(snapshot);
            bool expanded = _expandedPools.Contains(poolKey);
            AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard(GetEntryLabel(snapshot), expanded);
            card.Meta.text = $"A{snapshot.activeCount}/I{snapshot.inactiveCount}/T{snapshot.totalCount} | Hit {snapshot.hitCount}/{snapshot.spawnCount}";
            card.Actions.Add(AlicizaEditorUI.CreateBadge(
                snapshot.policy.ToString().ToUpperInvariant(),
                AlicizaValueTone.Accent));

            card.ExpandedChanged += value =>
            {
                if (!value)
                {
                    _expandedPools.Remove(poolKey);
                    return;
                }

                _expandedPools.Add(poolKey);
                if (card.Body.childCount == 0)
                {
                    BuildPoolBody(card.Body, debugService, snapshot);
                }
            };

            parent.Add(card);

            if (expanded)
            {
                BuildPoolBody(card.Body, debugService, snapshot);
            }
        }

        private void BuildPoolBody(
            VisualElement parent,
            IGameObjectPoolDebugService debugService,
            GameObjectPoolSnapshot snapshot)
        {
            string ruleName = string.IsNullOrWhiteSpace(snapshot.entryName)
                ? snapshot.location
                : snapshot.entryName;
            AlicizaEditorUI.AddValueRow(
                parent,
                "Rule",
                ruleName,
                string.IsNullOrEmpty(ruleName) ? AlicizaValueTone.Muted : AlicizaValueTone.Default);
            AlicizaEditorUI.AddValueRow(
                parent,
                "Group",
                string.IsNullOrWhiteSpace(snapshot.group) ? "<None>" : snapshot.group);
            AlicizaEditorUI.AddValueRow(
                parent,
                "Location",
                snapshot.location,
                string.IsNullOrEmpty(snapshot.location) ? AlicizaValueTone.Muted : AlicizaValueTone.Default);
            AlicizaEditorUI.AddValueRow(parent, "Policy", snapshot.policy.ToString());
            AlicizaEditorUI.AddValueRow(parent, "Min Idle", snapshot.minIdle.ToString());
            AlicizaEditorUI.AddValueRow(parent, "Retain Target", snapshot.retainTarget.ToString());
            AlicizaEditorUI.AddValueRow(
                parent,
                "Soft / Hard",
                $"{snapshot.softCapacity} / {snapshot.hardCapacity}");
            AlicizaEditorUI.AddValueRow(parent, "Unload Prefab", snapshot.unloadPrefab ? "Yes" : "No");
            AlicizaEditorUI.AddValueRow(
                parent,
                "Active / Idle / Total",
                $"{snapshot.activeCount} / {snapshot.inactiveCount} / {snapshot.totalCount}");
            AlicizaEditorUI.AddValueRow(
                parent,
                "Prefab Loaded",
                snapshot.prefabLoaded ? "Yes" : "No",
                snapshot.prefabLoaded ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                parent,
                "Next Maintenance",
                snapshot.nextMaintenanceIn < 0f ? "None" : FormatSeconds(snapshot.nextMaintenanceIn));

            AlicizaSection counters = AlicizaEditorUI.CreateStaticSection("Counters");
            parent.Add(counters);

            AlicizaEditorUI.AddValueRow(counters.Body, "Spawn", snapshot.spawnCount.ToString());
            AlicizaEditorUI.AddValueRow(counters.Body, "Despawn", snapshot.despawnCount.ToString());
            AlicizaEditorUI.AddValueRow(
                counters.Body,
                "Hit",
                snapshot.hitCount.ToString(),
                snapshot.hitCount > 0 ? AlicizaValueTone.Default : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                counters.Body,
                "Miss",
                snapshot.missCount.ToString(),
                snapshot.missCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                counters.Body,
                "Expand",
                snapshot.expandCount.ToString(),
                snapshot.expandCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(
                counters.Body,
                "Destroy",
                snapshot.destroyCount.ToString(),
                snapshot.destroyCount > 0 ? AlicizaValueTone.Warning : AlicizaValueTone.Muted);
            AlicizaEditorUI.AddValueRow(counters.Body, "Peak Active", snapshot.peakActive.ToString());

            BuildPoolActions(parent, debugService, snapshot);

            debugService.FillDebugInstances(snapshot);

            AlicizaSection instances = AlicizaEditorUI.CreateStaticSection("Instances");
            parent.Add(instances);

            if (snapshot.InstanceCount == 0)
            {
                instances.Body.Add(AlicizaEditorUI.CreateEmptyState("No live instances in this pool."));
                return;
            }

            for (int i = 0; i < snapshot.InstanceCount; i++)
            {
                BuildInstanceRow(instances.Body, snapshot.GetInstance(i), i);
            }
        }

        private void BuildPoolActions(
            VisualElement parent,
            IGameObjectPoolDebugService debugService,
            GameObjectPoolSnapshot snapshot)
        {
            string location = snapshot.location;
            string group = snapshot.group;

            var actions = new VisualElement();
            actions.AddToClassList("alicia-actions");

            Button flushThis = AlicizaEditorUI.CreateTextButton(
                "Flush This",
                "Flush the idle instances of this pool.",
                AlicizaButtonTone.Warning);
            flushThis.clicked += () =>
            {
                debugService.Flush(location);
                RequestRuntimeRefresh();
            };
            actions.Add(flushThis);

            Button flushGroup = AlicizaEditorUI.CreateTextButton(
                "Flush Group",
                "Flush every pool in this group.",
                AlicizaButtonTone.Danger);
            flushGroup.clicked += () =>
            {
                debugService.FlushGroup(group);
                RequestRuntimeRefresh();
            };
            actions.Add(flushGroup);

            parent.Add(actions);
        }

        private static void BuildInstanceRow(
            VisualElement parent,
            GameObjectPoolInstanceSnapshot snapshot,
            int index)
        {
            if (snapshot == null)
            {
                return;
            }

            string instanceName = snapshot.instanceName;
            if (string.IsNullOrWhiteSpace(instanceName) && snapshot.gameObject != null)
            {
                instanceName = snapshot.gameObject.name;
            }

            if (string.IsNullOrWhiteSpace(instanceName))
            {
                instanceName = "Instance #" + (index + 1);
            }

            AlicizaRow row = AlicizaEditorUI.AddRow(parent, instanceName, null);
            row.Caption.tooltip = instanceName;
            row.Caption.style.whiteSpace = WhiteSpace.NoWrap;
            row.Caption.style.overflow = Overflow.Hidden;

            row.Content.Add(AlicizaEditorUI.CreateBadge(
                snapshot.isActive ? "ACTIVE" : "IDLE",
                snapshot.isActive ? AlicizaValueTone.Warning : AlicizaValueTone.Muted));

            Label life = AlicizaEditorUI.CreateValueLabel($"Life {FormatSeconds(snapshot.lifeDuration)}");
            life.style.flexGrow = 0f;
            life.style.marginLeft = 12f;
            row.Content.Add(life);

            Label idle = AlicizaEditorUI.CreateValueLabel(
                snapshot.isActive ? "Idle -" : $"Idle {FormatSeconds(snapshot.idleDuration)}",
                snapshot.isActive ? AlicizaValueTone.Muted : AlicizaValueTone.Default);
            idle.style.flexGrow = 0f;
            idle.style.marginLeft = 12f;
            row.Content.Add(idle);

            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1f;
            row.Content.Add(spacer);

            var objectField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                label = string.Empty
            };
            objectField.AddToClassList("alicia-control");
            objectField.style.width = 140f;
            objectField.style.minWidth = 140f;
            objectField.SetValueWithoutNotify(snapshot.gameObject);
            objectField.SetEnabled(false);
            row.Content.Add(objectField);
        }

        private void RequestRuntimeRefresh()
        {
            if (_runtimeRoot == null)
            {
                return;
            }

            _runtimeRoot.schedule.Execute(RefreshRuntime).ExecuteLater(0);
        }

        private static string GetPoolKey(GameObjectPoolSnapshot snapshot)
        {
            return string.IsNullOrWhiteSpace(snapshot.location) ? GetEntryLabel(snapshot) : snapshot.location;
        }

        private static string GetEntryLabel(GameObjectPoolSnapshot snapshot)
        {
            string name = string.IsNullOrWhiteSpace(snapshot.entryName) ? snapshot.location : snapshot.entryName;
            return $"[{snapshot.group}] {name}";
        }

        private static string FormatSeconds(float seconds)
        {
            return $"{seconds:F2}s";
        }

        private int GetDebugSnapshots(IGameObjectPoolDebugService debugService)
        {
            while (true)
            {
                int count = debugService.GetDebugSnapshots(_snapshotBuffer);
                if (count < _snapshotBuffer.Length)
                {
                    return count;
                }

                _snapshotBuffer = new GameObjectPoolSnapshot[_snapshotBuffer.Length << 1];
            }
        }
    }
}
