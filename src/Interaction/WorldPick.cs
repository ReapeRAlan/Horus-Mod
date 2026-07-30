using UnityEngine;

namespace HorusMod.Interaction
{
    public struct WorldPick
    {
        public bool Valid;
        public Vector3 Point;
        public Vector3 Normal;
        public Unit Unit;
        public float Distance;
        public RaycastHit Hit;

        public static WorldPick FromScreen(Vector2 screenPosition)
        {
            WorldPick pick = default;
            Camera cam = Camera.main;
            if (cam == null) return pick;

            Ray ray = cam.ScreenPointToRay(screenPosition);
            bool physicsHit = Physics.Raycast(ray, out RaycastHit hit, 100000f);
            float distance = physicsHit ? hit.distance : float.MaxValue;
            Unit hitUnit = physicsHit ? ResolveUnit(hit.collider) : null;

            if (Datum.WaterPlane().Raycast(ray, out float waterEnter) &&
                waterEnter > 0f &&
                waterEnter < 100000f &&
                (!physicsHit || waterEnter < distance))
            {
                hit = default;
                hit.point = ray.origin + ray.direction * waterEnter;
                hit.normal = Vector3.up;
                distance = waterEnter;
                physicsHit = true;
            }

            if (!physicsHit && hitUnit == null) return pick;

            pick.Valid = true;
            pick.Point = physicsHit ? hit.point : hitUnit.transform.position;
            pick.Normal = physicsHit ? hit.normal : Vector3.up;
            // Keep the unit detected by the physics ray even when the water plane is
            // closer. Otherwise a ship's hull is replaced by a collider-less water hit.
            pick.Unit = hitUnit ?? ResolveUnit(hit.collider);
            pick.Distance = distance;
            pick.Hit = hit;
            return pick;
        }

        public static WorldPick WithScreenUnitFallback(WorldPick pick, Vector2 screenPosition, float paddingPixels = 12f)
        {
            if (pick.Unit != null) return pick;
            Camera cam = Camera.main;
            if (cam == null || UnitRegistry.allUnits == null) return pick;

            Unit best = null;
            float bestDistanceSq = float.MaxValue;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!IsUsable(unit) || unit.definition == null) continue;
                Vector3 center = cam.WorldToScreenPoint(unit.transform.position);
                if (center.z <= 0f) continue;

                float halfWidth = Mathf.Max(unit.definition.length, unit.definition.width, 5f) * 0.5f;
                Vector3 edge = cam.WorldToScreenPoint(unit.transform.position + cam.transform.right * halfWidth);
                float radius = Mathf.Clamp(Vector2.Distance(center, edge), 10f, 300f) + paddingPixels;
                float distanceSq = ((Vector2)center - screenPosition).sqrMagnitude;
                if (distanceSq > radius * radius || distanceSq >= bestDistanceSq) continue;
                best = unit;
                bestDistanceSq = distanceSq;
            }

            if (best == null) return pick;
            pick.Valid = true;
            pick.Unit = best;
            pick.Point = best.transform.position;
            pick.Normal = Vector3.up;
            pick.Distance = Vector3.Distance(cam.transform.position, best.transform.position);
            return pick;
        }

        private static Unit ResolveUnit(Collider collider)
        {
            if (collider == null) return null;
            Unit unit = collider.GetComponentInParent<Unit>();
            if (unit != null) return unit;

            Transform current = collider.transform;
            while (current != null)
            {
                IDamageable damageable = current.GetComponent<IDamageable>();
                unit = damageable?.GetUnit();
                if (unit != null) return unit;
                current = current.parent;
            }
            return null;
        }

        private static bool IsUsable(Unit unit)
        {
            return unit != null && unit.gameObject != null && !unit.disabled &&
                   unit.unitState != Unit.UnitState.Destroyed;
        }
    }
}
