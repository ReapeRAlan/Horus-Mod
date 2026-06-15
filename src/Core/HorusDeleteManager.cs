using UnityEngine;
using NuclearOption.Networking;
using Mirage;
using System.Collections.Generic;

namespace HorusMod.Core
{
    public static class HorusDeleteManager
    {
        public static void HandleDeleteClick(bool mapSpawnMode, float deleteRange, HashSet<Unit> horusSpawnedUnits)
        {
            // Permission gate.
            if (!Networking.HorusPermissions.CanDelete())
            {
                HorusPlugin.Logger.LogWarning("Horus: host permission required. Delete blocked.");
                return;
            }

            // Never delete while placing from the map.
            if (mapSpawnMode) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Unit unitToDelete = null;

            // 1. Try direct click first
            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                GameObject hitObject = hit.collider != null ? hit.collider.gameObject : null;
                if (hitObject != null)
                {
                    Unit unitRoot = HorusManager.FindUnitRoot(hitObject);
                    if (unitRoot != null)
                    {
                        unitToDelete = unitRoot;
                    }
                }
            }

            // 2. If no direct unit clicked, look for nearest unit in range of the raycast hit point
            if (unitToDelete == null)
            {
                if (Physics.Raycast(ray, out RaycastHit hit2, 100000f))
                {
                    Vector3 localHitPos = hit2.point;
                    GlobalPosition globalHitPos = localHitPos.ToGlobalPosition();
                    
                    if (UnitRegistry.TryGetNearestUnit(globalHitPos, out Unit nearestUnit, deleteRange))
                    {
                        unitToDelete = nearestUnit;
                    }
                }
            }

            if (unitToDelete == null)
            {
                HorusPlugin.Logger.LogInfo("Horus: No unit found to delete (direct click or within range).");
                return;
            }

            // Validate the unit
            if (!HorusManager.Instance.IsSafeDeleteTarget(unitToDelete.gameObject))
            {
                string reason = "";
                if (HorusManager.IsBuiltinMapUnit(unitToDelete))
                {
                    reason = "original map unit is protected (enable Safety/AllowDeletingOriginalMissionUnits to remove)";
                }
                else
                {
                    reason = "not spawned by Horus (enable Safety/AllowDeletingNonHorusUnits to remove)";
                }
                HorusPlugin.Logger.LogInfo($"Horus: target is not deletable ({reason}): '{unitToDelete.unitName}'.");
                return;
            }

            DeleteUnit(unitToDelete, horusSpawnedUnits);
        }

        public static void DeleteUnit(Unit unit, HashSet<Unit> horusSpawnedUnits)
        {
            if (unit == null) return;
            GameObject go = unit.gameObject;
            string unitName = unit.unitName;
            
            horusSpawnedUnits.Remove(unit);
            
            try
            {
                if (Networking.HorusPermissions.IsMultiplayer())
                {
                    NetworkServer.Destroy(go);
                    HorusPlugin.Logger.LogInfo($"Horus (Host): Server-destroyed unit '{unitName}'.");
                }
                else
                {
                    UnityEngine.Object.Destroy(go);
                    HorusPlugin.Logger.LogInfo($"Horus (Local): Local-destroyed unit '{unitName}'.");
                }
            }
            catch (System.Exception e)
            {
                HorusPlugin.Logger.LogError($"Horus: Exception while destroying unit '{unitName}': {e.Message}");
            }
        }
    }
}
