using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.Placement
{
    public enum FormationKind
    {
        Line,
        Column,
        Grid,
        Circle,
        V
    }

    public static class FormationSolver
    {
        public static List<Vector3> GetOffsets(int count, float spacing, FormationKind kind)
        {
            var offsets = new List<Vector3>(Mathf.Max(0, count));
            if (count <= 0) return offsets;
            spacing = Mathf.Max(1f, spacing);

            switch (kind)
            {
                case FormationKind.Line:
                    for (int i = 0; i < count; i++)
                        offsets.Add(new Vector3((i - (count - 1) * 0.5f) * spacing, 0f, 0f));
                    break;
                case FormationKind.Column:
                    for (int i = 0; i < count; i++)
                        offsets.Add(new Vector3(0f, 0f, -i * spacing));
                    break;
                case FormationKind.Grid:
                    int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
                    int rows = Mathf.CeilToInt((float)count / columns);
                    for (int i = 0; i < count; i++)
                    {
                        int row = i / columns;
                        int column = i % columns;
                        offsets.Add(new Vector3(
                            (column - (columns - 1) * 0.5f) * spacing,
                            0f,
                            -(row - (rows - 1) * 0.5f) * spacing));
                    }
                    break;
                case FormationKind.Circle:
                    float radius = Mathf.Max(spacing, spacing * count / (Mathf.PI * 2f));
                    for (int i = 0; i < count; i++)
                    {
                        float angle = i * Mathf.PI * 2f / count;
                        offsets.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                    }
                    break;
                case FormationKind.V:
                    offsets.Add(Vector3.zero);
                    for (int i = 1; i < count; i++)
                    {
                        int rank = (i + 1) / 2;
                        float side = i % 2 == 1 ? -1f : 1f;
                        offsets.Add(new Vector3(side * rank * spacing, 0f, -rank * spacing));
                    }
                    break;
            }

            return offsets;
        }

        public static FormationKind FromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return FormationKind.Column;
            if (name.StartsWith("Line")) return FormationKind.Line;
            if (name.StartsWith("Grid")) return FormationKind.Grid;
            if (name.StartsWith("Circle")) return FormationKind.Circle;
            if (name.StartsWith("V")) return FormationKind.V;
            return FormationKind.Column;
        }
    }
}
