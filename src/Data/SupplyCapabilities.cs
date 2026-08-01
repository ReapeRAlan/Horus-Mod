using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HorusMod.Data
{
    /// <summary>
    /// Results of a defensive, read-only prefab inspection. Unknown is kept
    /// distinct from No so Horus never advertises naval resupply after a game
    /// update merely because a private field could not be read.
    /// </summary>
    public sealed class SupplyCapabilities
    {
        public static readonly SupplyCapabilities None = new SupplyCapabilities();

        public bool HasRearmer { get; internal set; }
        public bool HasRefueler { get; internal set; }
        public bool HasUnitStorage { get; internal set; }
        public bool HasWarheadStorage { get; internal set; }

        public CapabilityState CanRearmAircraft { get; internal set; } = CapabilityState.No;
        public CapabilityState CanRearmVehicles { get; internal set; } = CapabilityState.No;
        public CapabilityState CanResupplyShips { get; internal set; } = CapabilityState.No;

        public float? RearmRange { get; internal set; }
        public float? RearmCapacity { get; internal set; }
        public float? RefuelRange { get; internal set; }
        public bool? RearmerSingleUse { get; internal set; }
        public bool? RefuelerSingleUse { get; internal set; }
        public string Diagnostic { get; internal set; }
        public CatalogFlags Flags { get; internal set; }

        public bool IsLogistics => (Flags & CatalogFlags.Logistics) != 0;

        internal static SupplyCapabilities Inspect(UnitDefinition definition)
        {
            var result = new SupplyCapabilities();
            if (definition == null || definition.unitPrefab == null)
            {
                result.Diagnostic = definition == null ? "Definition is null." : "Definition has no prefab.";
                return result;
            }

            Component[] components;
            try
            {
                components = definition.unitPrefab.GetComponentsInChildren<Component>(true);
            }
            catch (Exception ex)
            {
                result.Diagnostic = "Prefab inspection failed: " + ex.GetType().Name;
                return result;
            }

            var diagnostics = new List<string>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;

                Type componentType = component.GetType();
                if (IsTypeOrBaseNamed(componentType, "Rearmer"))
                {
                    result.HasRearmer = true;
                    result.Flags |= CatalogFlags.Logistics | CatalogFlags.Ammo;
                    bool hasAircraft = TryReadBool(component, "aircraft", out bool aircraft);
                    bool hasVehicle = TryReadBool(component, "vehicle", out bool vehicle);
                    bool hasNaval = TryReadBool(component, "naval", out bool naval);
                    bool exposesSurfaceFlags = hasAircraft || hasVehicle || hasNaval;

                    float? componentRange = ReadNullableFloat(component, "range", diagnostics);
                    result.RearmRange = Max(result.RearmRange, componentRange);
                    float? componentCapacity = ReadOptionalFloat(component, "capacity");
                    result.RearmCapacity = Max(result.RearmCapacity, componentCapacity);
                    result.RearmerSingleUse = MergeSingleUse(result.RearmerSingleUse,
                        ReadNullableBool(component, "singleUse", diagnostics));

                    bool operational = true;
                    if (!TryReadMember(component, "unit", out object owner) || !(owner is Unit))
                    {
                        operational = false;
                        diagnostics.Add(componentType.Name + ".unit is unavailable or unbound");
                    }
                    if (!componentRange.HasValue || componentRange.Value <= 0f)
                    {
                        operational = false;
                        diagnostics.Add(componentType.Name + ".range is not positive");
                    }
                    if (componentCapacity.HasValue && componentCapacity.Value <= 0f)
                    {
                        operational = false;
                        diagnostics.Add(componentType.Name + ".capacity is empty");
                    }
                    if (TryReadMember(component, "accessTransform", out object access) && !(access is Transform))
                    {
                        operational = false;
                        diagnostics.Add(componentType.Name + ".accessTransform is unbound");
                    }
                    if (component is Behaviour behaviour && (!behaviour.enabled || !behaviour.gameObject.activeSelf))
                    {
                        operational = false;
                        diagnostics.Add(componentType.Name + " is disabled or inactive on the prefab");
                    }

                    CapabilityState aircraftState = exposesSurfaceFlags
                        ? FromOptionalBool(hasAircraft, aircraft)
                        : CapabilityState.Yes;
                    CapabilityState vehicleState = exposesSurfaceFlags
                        ? FromOptionalBool(hasVehicle, vehicle)
                        : CapabilityState.Yes;
                    CapabilityState navalState = exposesSurfaceFlags
                        ? FromOptionalBool(hasNaval, naval)
                        : CapabilityState.Yes;
                    if (!exposesSurfaceFlags)
                        diagnostics.Add(componentType.Name + " uses the generic Unit rearm API; live ship validation is still recommended");
                    if (!operational)
                    {
                        aircraftState = DowngradeUnconfigured(aircraftState);
                        vehicleState = DowngradeUnconfigured(vehicleState);
                        navalState = DowngradeUnconfigured(navalState);
                    }

                    result.CanRearmAircraft = Merge(result.CanRearmAircraft, aircraftState);
                    result.CanRearmVehicles = Merge(result.CanRearmVehicles, vehicleState);
                    result.CanResupplyShips = Merge(result.CanResupplyShips, navalState);
                }
                else if (IsTypeOrBaseNamed(componentType, "Refueler"))
                {
                    result.HasRefueler = true;
                    result.Flags |= CatalogFlags.Logistics | CatalogFlags.Fuel;
                    result.RefuelRange = Max(result.RefuelRange, ReadNullableFloat(component, "range", diagnostics));
                    result.RefuelerSingleUse = MergeSingleUse(result.RefuelerSingleUse,
                        ReadNullableBool(component, "singleUse", diagnostics));
                }
                else if (IsTypeOrBaseNamed(componentType, "UnitStorage"))
                {
                    result.HasUnitStorage = true;
                    result.Flags |= CatalogFlags.Logistics | CatalogFlags.Storage;
                }
                else if (IsTypeOrBaseNamed(componentType, "WarheadStorage"))
                {
                    result.HasWarheadStorage = true;
                    result.Flags |= CatalogFlags.Logistics | CatalogFlags.Storage | CatalogFlags.Strategic;
                }
            }

            if (result.CanResupplyShips == CapabilityState.Yes)
                result.Flags |= CatalogFlags.NavalResupply;
            if (diagnostics.Count > 0) result.Diagnostic = string.Join("; ", diagnostics);
            return result;
        }

        private static CapabilityState DowngradeUnconfigured(CapabilityState state)
        {
            return state == CapabilityState.Yes ? CapabilityState.Unknown : state;
        }

        private static CapabilityState Merge(CapabilityState current, CapabilityState next)
        {
            if (current == CapabilityState.Yes || next == CapabilityState.Yes) return CapabilityState.Yes;
            if (current == CapabilityState.Unknown || next == CapabilityState.Unknown) return CapabilityState.Unknown;
            return CapabilityState.No;
        }

        private static float? Max(float? current, float? next)
        {
            if (!current.HasValue) return next;
            if (!next.HasValue) return current;
            return Mathf.Max(current.Value, next.Value);
        }

        private static float? ReadOptionalFloat(Component component, string member)
        {
            if (!TryReadMember(component, member, out object value)) return null;
            if (value is float floatValue) return floatValue;
            if (value is double doubleValue) return (float)doubleValue;
            return null;
        }

        private static bool? MergeSingleUse(bool? current, bool? next)
        {
            if (!current.HasValue) return next;
            if (!next.HasValue || current.Value != next.Value) return null;
            return current;
        }

        private static CapabilityState ReadCapability(Component component, string member, List<string> diagnostics)
        {
            bool? value = ReadNullableBool(component, member, diagnostics);
            if (!value.HasValue) return CapabilityState.Unknown;
            return value.Value ? CapabilityState.Yes : CapabilityState.No;
        }

        private static CapabilityState FromOptionalBool(bool available, bool value)
        {
            return !available ? CapabilityState.Unknown : value ? CapabilityState.Yes : CapabilityState.No;
        }

        private static bool TryReadBool(Component component, string member, out bool value)
        {
            value = false;
            if (!TryReadMember(component, member, out object raw) || !(raw is bool boolValue)) return false;
            value = boolValue;
            return true;
        }

        private static bool? ReadNullableBool(Component component, string member, List<string> diagnostics)
        {
            if (TryReadMember(component, member, out object value) && value is bool boolValue) return boolValue;
            diagnostics.Add(component.GetType().Name + "." + member + " unavailable");
            return null;
        }

        private static float? ReadNullableFloat(Component component, string member, List<string> diagnostics)
        {
            if (TryReadMember(component, member, out object value))
            {
                if (value is float floatValue) return floatValue;
                if (value is double doubleValue) return (float)doubleValue;
            }

            diagnostics.Add(component.GetType().Name + "." + member + " unavailable");
            return null;
        }

        private static bool TryReadMember(object target, string member, out object value)
        {
            value = null;
            if (target == null) return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                try
                {
                    FieldInfo field = type.GetField(member, flags | BindingFlags.DeclaredOnly);
                    if (field == null)
                    {
                        FieldInfo[] fields = type.GetFields(flags | BindingFlags.DeclaredOnly);
                        for (int i = 0; i < fields.Length; i++)
                            if (string.Equals(fields[i].Name, member, StringComparison.OrdinalIgnoreCase)) { field = fields[i]; break; }
                    }
                    if (field != null)
                    {
                        value = field.GetValue(target);
                        return true;
                    }

                    PropertyInfo property = type.GetProperty(member, flags | BindingFlags.DeclaredOnly);
                    if (property == null)
                    {
                        PropertyInfo[] properties = type.GetProperties(flags | BindingFlags.DeclaredOnly);
                        for (int i = 0; i < properties.Length; i++)
                            if (string.Equals(properties[i].Name, member, StringComparison.OrdinalIgnoreCase)) { property = properties[i]; break; }
                    }
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        value = property.GetValue(target, null);
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsTypeOrBaseNamed(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
                if (string.Equals(current.Name, name, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
