using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowsOfTheForsaken.Tests.PlayMode
{
    // A load cannot be cancelled. Keep its ownership after a failed setup so the
    // next fixture never inherits a late-activating copy of the level.
    internal sealed class OwnedSceneLoad
    {
        private static readonly HashSet<OwnedSceneLoad> incompleteCleanups = new HashSet<OwnedSceneLoad>();
        private readonly HashSet<Scene> previousHandles = new HashSet<Scene>();
        private readonly HashSet<Scene> owned = new HashSet<Scene>();
        private readonly HashSet<Scene> unloading = new HashSet<Scene>();
        private readonly List<AsyncOperation> unloadOperations = new List<AsyncOperation>();
        private AsyncOperation pendingLoad;
        private Scene previousActive;
        private string path;
        private bool cleanupStarted;

        public Scene Scene { get; private set; }
        public bool LoadedWithinDeadline { get; private set; }
        public string CleanupFailure { get; private set; }

        public static void RequireNoPendingCleanup()
        {
            if (incompleteCleanups.Count != 0)
                throw new InvalidOperationException("An earlier scene cleanup is still pending; refusing another scene load.");
        }

        public IEnumerator Load(string scenePath, Func<AsyncOperation> beginLoad, float timeoutSeconds = 30f)
        {
            RequireNoPendingCleanup();
            if (pendingLoad != null) throw new InvalidOperationException("A scene lease can load only once.");
            path = scenePath;
            previousActive = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; i++) previousHandles.Add(SceneManager.GetSceneAt(i));
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            pendingLoad = beginLoad();
            if (pendingLoad == null) throw new InvalidOperationException("Scene load did not start: " + path);
            pendingLoad.completed += OnLoadCompleted;
            while (!pendingLoad.isDone && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            DiscoverOwnedScenes();
            // Include a load that blocks a single editor frame beyond the deadline.
            LoadedWithinDeadline = pendingLoad.isDone && Time.realtimeSinceStartupAsDouble <= deadline;
        }

        public IEnumerator Cleanup(float timeoutSeconds = 180f)
        {
            cleanupStarted = true;
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            // Also release deliberate activation holds used by the regression.
            if (pendingLoad != null) pendingLoad.allowSceneActivation = true;
            while (pendingLoad != null && !pendingLoad.isDone && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            DiscoverOwnedScenes();
            StartOwnedUnloads();
            while (!IsClean && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (IsClean)
                FinishClean();
            else
            {
                CleanupFailure = "Owned scene cleanup exceeded " + timeoutSeconds + " s: " + path;
                incompleteCleanups.Add(this);
                // The completion callbacks remain registered. They deactivate and
                // unload a late scene while the guard prevents more queued loads.
            }
        }

        private bool IsClean => (pendingLoad == null || pendingLoad.isDone) &&
            unloadOperations.All(operation => operation.isDone) &&
            owned.All(scene => !scene.IsValid() || !scene.isLoaded);

        private void DiscoverOwnedScenes()
        {
            if (path == null) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var candidate = SceneManager.GetSceneAt(i);
                if (candidate.path != path || previousHandles.Contains(candidate)) continue;
                owned.Add(candidate);
                if (!Scene.IsValid()) Scene = candidate;
            }
        }

        private void OnLoadCompleted(AsyncOperation operation)
        {
            DiscoverOwnedScenes();
            if (!cleanupStarted) return;
            StartOwnedUnloads();
            if (IsClean) FinishClean();
        }

        private void StartOwnedUnloads()
        {
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            foreach (var scene in owned)
            {
                if (!scene.IsValid() || !scene.isLoaded || !unloading.Add(scene)) continue;
                // Stop gameplay, rendering and NavMesh registration immediately,
                // even when Unity must defer completion of the actual unload.
                foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
                var operation = SceneManager.UnloadSceneAsync(scene);
                if (operation == null) continue;
                unloadOperations.Add(operation);
                operation.completed += OnUnloadCompleted;
            }
        }

        private void OnUnloadCompleted(AsyncOperation operation)
        {
            if (IsClean) FinishClean();
        }

        private void FinishClean()
        {
            incompleteCleanups.Remove(this);
            if (pendingLoad != null) pendingLoad.completed -= OnLoadCompleted;
            foreach (var operation in unloadOperations) operation.completed -= OnUnloadCompleted;
            // Keep any earlier cleanup failure: a later callback must not turn a
            // reported timeout into a pass.
        }
    }
}
