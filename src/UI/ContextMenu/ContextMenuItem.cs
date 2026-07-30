using System;
using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.UI.ContextMenu
{
    public sealed class ContextMenuItem
    {
        public string Label;
        public string Shortcut;
        public string DisabledReason;
        public Sprite Icon;
        public bool Enabled = true;
        public bool IsSeparator;
        public bool IsDanger;
        public bool IsHeader;
        public Action OnClick;
        public List<ContextMenuItem> Submenu;

        public static ContextMenuItem Sep() => new ContextMenuItem { IsSeparator = true, Enabled = false };
        public static ContextMenuItem Header(string label) => new ContextMenuItem { Label = label, IsHeader = true, Enabled = false };
    }
}
