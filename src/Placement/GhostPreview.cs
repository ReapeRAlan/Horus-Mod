using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace HorusMod.Placement
{
    /// <summary>
    /// Local-only, non-networked ghost/preview of a unit shown before the real spawn.
    ///
    /// SAFETY: This NEVER calls Spawner / NetworkServer. It instantiates the unit prefab
    /// under a deactivated holder (so no Awake/OnEnable/AI/weapons/network/audio/physics run),
    /// strips every dangerous component, then activates only the visual mesh hierarchy.
    /// The preview cannot damage anything, cannot be networked, and is fully cleaned up.
    /// </summary>
    public class GhostPreview
    {
        private GameObject inactiveHolder;
        private readonly System.Collections.Generic.List<GameObject> ghosts = new System.Collections.Generic.List<GameObject>();
        private UnitDefinition builtDef;

        /// <summary>The definition the current ghost mesh was built from (null if none).</summary>
        public UnitDefinition BuiltDefinition => builtDef;

        public bool IsBuilt => ghosts.Count > 0;

        private void EnsureHolder()
        {
            if (inactiveHolder == null)
            {
                inactiveHolder = new GameObject("HorusGhostHolder");
                inactiveHolder.SetActive(false); // keep inactive forever so children never Awake
                inactiveHolder.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(inactiveHolder);
            }
        }

        /// <summary>
        /// Builds a stripped, transparent ghost for the given unit. Returns false (and logs)
        /// if the unit has no prefab or instantiation fails; callers should fall back gracefully.
        /// </summary>
        public bool Build(UnitDefinition def)
        {
            Clear();

            if (def == null || def.unitPrefab == null)
            {
                HorusPlugin.Logger.LogWarning("GhostPreview: selected unit has no prefab; preview skipped.");
                return false;
            }

            EnsureHolder();

            try
            {
                // Instantiate under the inactive holder => Awake/OnEnable do NOT run yet.
                GameObject g = UnityEngine.Object.Instantiate(def.unitPrefab, inactiveHolder.transform);
                g.name = "HorusGhost_" + def.unitName;

                StripDangerousComponents(g);
                MakeTransparent(g);

                // Move into the live scene and show. All scripts are gone, so activating is safe.
                g.transform.SetParent(null, false);
                g.SetActive(true);

                ghosts.Add(g);
                builtDef = def;
                return true;
            }
            catch (Exception ex)
            {
                HorusPlugin.Logger.LogError($"GhostPreview: failed to build ghost for '{def.unitName}': {ex.Message}");
                Clear();
                return false;
            }
        }

        /// <summary>
        /// Builds multiple visual ghosts for group spawning.
        /// </summary>
        public bool BuildGroup(System.Collections.Generic.List<UnitDefinition> definitions)
        {
            Clear();
            if (definitions == null || definitions.Count == 0) return false;

            EnsureHolder();

            try
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    var def = definitions[i];
                    if (def == null || def.unitPrefab == null) continue;

                    GameObject g = UnityEngine.Object.Instantiate(def.unitPrefab, inactiveHolder.transform);
                    g.name = $"HorusGhost_{i}_{def.unitName}";

                    StripDangerousComponents(g);
                    MakeTransparent(g);

                    g.transform.SetParent(null, false);
                    g.SetActive(true);

                    ghosts.Add(g);
                }

                builtDef = definitions[0];
                return ghosts.Count > 0;
            }
            catch (Exception ex)
            {
                HorusPlugin.Logger.LogError($"GhostPreview: failed to build group ghosts: {ex.Message}");
                Clear();
                return false;
            }
        }

        public void UpdateTransform(Vector3 position, Quaternion rotation)
        {
            if (ghosts.Count > 0 && ghosts[0] != null)
            {
                ghosts[0].transform.SetPositionAndRotation(position, rotation);
            }
        }

        public void UpdateTransformGroup(Vector3 centerPos, Quaternion centerRot, System.Collections.Generic.List<Vector3> relativeOffsets, System.Collections.Generic.List<UnitDefinition> definitions, Func<Vector3, UnitDefinition, Vector3> snapFunc)
        {
            for (int i = 0; i < ghosts.Count; i++)
            {
                if (ghosts[i] == null) continue;
                Vector3 offset = (i < relativeOffsets.Count) ? relativeOffsets[i] : Vector3.zero;
                Vector3 rawPos = centerPos + offset;

                UnitDefinition def = (i < definitions.Count) ? definitions[i] : null;
                Vector3 snappedPos = snapFunc != null ? snapFunc(rawPos, def) : rawPos;

                ghosts[i].transform.SetPositionAndRotation(snappedPos, centerRot);
            }
        }

        public void SetVisible(bool visible)
        {
            for (int i = 0; i < ghosts.Count; i++)
            {
                if (ghosts[i] != null && ghosts[i].activeSelf != visible)
                {
                    ghosts[i].SetActive(visible);
                }
            }
        }

        /// <summary>Destroys the current ghost meshes (safe to call repeatedly).</summary>
        public void Clear()
        {
            for (int i = 0; i < ghosts.Count; i++)
            {
                if (ghosts[i] != null)
                {
                    UnityEngine.Object.Destroy(ghosts[i]);
                }
            }
            ghosts.Clear();
            builtDef = null;
        }

        /// <summary>Full teardown including the persistent holder. Call on mod/manager disable.</summary>
        public void Dispose()
        {
            Clear();
            if (inactiveHolder != null)
            {
                UnityEngine.Object.Destroy(inactiveHolder);
                inactiveHolder = null;
            }
        }

        private static void StripDangerousComponents(GameObject root)
        {
            // Pass 1: remove ALL scripts (AI, weapons, Mirage NetworkIdentity/NetworkBehaviour,
            // audio behaviours, custom logic). MonoBehaviour covers all of these.
            foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                SafeDestroyImmediate(b);
            }

            // Pass 2: remove every remaining component except the visual mesh hierarchy.
            // This covers Rigidbody, Collider, Joint, AudioSource, ParticleSystem(+Renderer),
            // Animator, Animation, Cloth, Light, Trail/Line renderers, etc. without needing extra
            // UnityEngine module references. Run twice so RequireComponent dependents are removed
            // before the components they depend on (e.g. ParticleSystemRenderer -> ParticleSystem).
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || IsVisualKeep(c)) continue;
                    SafeDestroyImmediate(c);
                }
            }
        }

        /// <summary>Components that are safe to keep so the ghost still renders.</summary>
        private static bool IsVisualKeep(Component c)
        {
            return c is Transform
                || c is MeshFilter
                || c is MeshRenderer
                || c is SkinnedMeshRenderer
                || c is LODGroup;
        }

        private static void SafeDestroyImmediate(Component c)
        {
            if (c == null) return;
            try
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
            catch (Exception ex)
            {
                // RequireComponent or similar may refuse a removal; non-fatal for a preview.
                HorusPlugin.Logger.LogWarning($"GhostPreview: could not strip {c.GetType().Name}: {ex.Message}");
            }
        }

        private static void MakeTransparent(GameObject root)
        {
            // A clearly "preview" tint. Best-effort: if a shader ignores it the ghost is still
            // visible (solid), which is acceptable per design.
            Color tint = new Color(0.25f, 0.75f, 1f, 0.45f);

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                try
                {
                    // .materials returns per-renderer INSTANCES, so the real prefab/shared
                    // materials are never modified (other units keep their look).
                    Material[] mats = r.materials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] != null) TrySetTransparent(mats[i], tint);
                    }
                    r.materials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                catch (Exception ex)
                {
                    HorusPlugin.Logger.LogWarning($"GhostPreview: could not tint a renderer: {ex.Message}");
                }
            }
        }

        private static void TrySetTransparent(Material m, Color tint)
        {
            // Built-in Standard shader transparency setup (best-effort; ignored by other shaders).
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            // URP Lit: switch surface to Transparent if present.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);

            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;

            // Apply alpha across the common color property names.
            if (m.HasProperty("_Color"))
            {
                Color c = m.GetColor("_Color");
                c.a = tint.a;
                m.SetColor("_Color", c);
            }
            if (m.HasProperty("_BaseColor"))
            {
                Color c = m.GetColor("_BaseColor");
                c.a = tint.a;
                m.SetColor("_BaseColor", c);
            }
        }
    }
}
