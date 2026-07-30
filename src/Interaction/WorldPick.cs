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

            if (!physicsHit) return pick;

            Unit unit = null;
            if (hit.collider != null)
            {
                unit = hit.collider.GetComponentInParent<Unit>();
                if (unit == null)
                {
                    IDamageable damageable = hit.collider.GetComponent<IDamageable>();
                    if (damageable != null) unit = damageable.GetUnit();
                }
            }

            pick.Valid = true;
            pick.Point = hit.point;
            pick.Normal = hit.normal;
            pick.Unit = unit;
            pick.Distance = distance;
            pick.Hit = hit;
            return pick;
        }
    }
}
